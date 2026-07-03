# windows_parental_control
Parental control app for windows machine that will run as service and tamper-resistant to killing processes

# Claude Plan:
# KidsMonitor — Windows Screen-Time / Parental Control App

## Context

# Goal:
This is a greenfield build. The goal is a Windows app that limits continuous computer usage, locks the machine when the limit is hit, and lets a parent unlock/override with a password. Two architecture decisions were already confirmed with the user:

- **Tech stack:** WinUI 3 on .NET 8 (C#).
- **Enforcement model:** a tamper-resistant **Windows Service** (runs as LocalSystem, survives logoff/kid killing the tray app) does the actual enforcement, paired with a **WinUI 3 tray app** for UI. This is stronger than a tray-only app (which a kid could just kill via Task Manager) but means the app **cannot be published to the Microsoft Store** (Store apps can't install services). Distribution for now is a standalone installer; Store publishing is an explicit future stretch goal, not a v1 requirement — the code should stay decoupled enough (Tray/Overlay never reference Service, only a shared `Common` library) that a future Store-only trimmed build remains plausible without a rewrite.

## Solution Structure

```
C:\WORK\KidsMonitor\
  KidsMonitor.sln
  src\
    KidsMonitor.Common\        (net8.0 class lib — models, IPC contracts, password hashing, config storage)
    KidsMonitor.Service\       (net8.0-windows Worker Service, runs as LocalSystem)
    KidsMonitor.Tray\          (WinUI 3, net8.0-windows10.0.19041.0 — tray icon, settings, first-run wizard)
    KidsMonitor.Overlay\       (WinUI 3 — full-screen lock UI, launched by the Service into the child's session)
    KidsMonitor.Installer\     (WiX v5 — MSI: service registration, ACLs, autostart)
  tests\
    KidsMonitor.Common.Tests\  (xUnit — hashing, IPC serialization)
    KidsMonitor.Service.Tests\ (xUnit — SessionTracker timer logic, clock-injectable)
  tools\
    install-service.ps1 / uninstall-service.ps1   (fast dev iteration before MSI exists)
```

Reference graph: `Service`, `Tray`, `Overlay` all depend only on `Common`. `Tray`/`Overlay` never reference `Service`.

Key packages: `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Hosting.WindowsServices` (service host), `Microsoft.WindowsAppSDK` (WinUI 3), `CommunityToolkit.Mvvm`, `H.NotifyIcon.WinUI` (WinUI 3 has no native tray-icon API), `Serilog` + `Serilog.Sinks.File`, `System.IO.Pipes` (built-in, IPC), `System.Security.Cryptography` (built-in, PBKDF2 — no external crypto package needed), WiX Toolset v5 for the installer.

## Windows Service Design

**Continuous-use detection:** the Service runs in Session 0 (no desktop) so it can't call `GetLastInputInfo` itself. Primary signal: the Tray app (running in the child's session) sends an `ActivityHeartbeat {IdleSeconds}` over the named pipe every ~5-10s; the Service accumulates continuous-use time while idle stays under a configurable threshold, using a **monotonic clock** (`Stopwatch`/`TickCount64`, not `DateTime.Now`) so wall-clock changes can't be exploited. Fallback signal: the Service also registers for `SERVICE_CONTROL_SESSIONCHANGE` (requires a custom `ServiceBase`-derived class — the generic `AddWindowsService()` hosting path doesn't expose this cleanly) so if Tray heartbeats stop arriving (tray killed) but the session is logged-on/unlocked, the Service fails safe by treating time as continuously elapsing and relaunches Tray.

**State/config persistence:** `C:\ProgramData\KidsMonitor\{config.json, password.dat, state\session-state.json, logs\}`. The Service sets a restrictive ACL on this folder **idempotently on its own startup** (SYSTEM+Administrators full control, no access for standard users) rather than via a fragile installer custom action — this also keeps `password.dat` unreadable to the child. State snapshots every ~30s so a service restart doesn't let the timer be reset for free.

**Locking mechanism (the crux):** `LockWorkStation()`/forced logoff alone are **not sufficient** — the child just logs back into their own account. Recommended approach: the Service uses `WTSQueryUserToken` → `DuplicateTokenEx` → `CreateProcessAsUser` to launch `KidsMonitor.Overlay.exe` directly on the child's interactive desktop, running with the child's own token. The overlay is full-screen, topmost, multi-monitor, and only dismissed via a password verified **server-side by the Service** (never trust the overlay process itself). Hardening layered on top for v1: a watchdog that respawns the overlay within ~1-2s if killed, temporary `DisableTaskMgr` registry policy on the child's SID while locked, and a `WH_KEYBOARD_LL` hook in the overlay to suppress Alt+Tab/Win key.

**Known ceiling (state this to the user, don't try to "solve" it):** a child with local admin rights, Safe Mode access, or a second admin account can always defeat a user-mode-only solution — no kernel driver or MDM is in scope. Deployment recommendation: the child's Windows account should be Standard, not Administrator.

**IPC:** named pipe `\\.\pipe\KidsMonitor`, persistent full-duplex connections (Service must be able to *push* events), newline-delimited JSON records defined once in `Common.Ipc.Messages` (`Hello`, `ActivityHeartbeat`, `UnlockRequest`, `SetPasswordRequest`, `SetLimitsRequest`, `StatusUpdate`, `LockTriggered`, `UnlockResult`, `ConfigUpdated`, etc.), shared by both Tray and Overlay via one `KidsMonitorPipeClient` class in `Common`.

## Password Handling

`password.dat`: PBKDF2-SHA256 via .NET 8's built-in `Rfc2898DeriveBytes.Pbkdf2` (random salt, ≥210,000 iterations per OWASP), compared with `CryptographicOperations.FixedTimeEquals`. Verification always happens server-side in the Service (acceptable since the pipe never leaves the local machine). First run: if `password.dat` doesn't exist, Service reports `SetupRequired`, enforcement stays off, Tray shows a mandatory setup wizard (limit + password) — the very first `SetPasswordRequest` (no current password) is only accepted from a pipe client whose token is in local Administrators, so a kid can't race the parent to set it. Changing the password or the time limit both require the current password.

## Tray App (WinUI 3)

Tray icon + status flyout (remaining time, "I'm a parent" unlock button) via `H.NotifyIcon.WinUI`; `SettingsWindow` (limit, idle-reset threshold, change password); `PasswordPromptDialog` (`ContentDialog`, reused across flows); `FirstRunSetupWindow`. The lock overlay is **not** hosted inside Tray — it's a separate process the Service launches directly, so locking doesn't depend on Tray staying alive; Tray's flyout is just a convenience secondary unlock path. Autostart via `HKLM\...\Run` (simple; real tamper-resistance comes from the Service's watchdog relaunching Tray, not the autostart mechanism). Ship Tray/Overlay unpackaged and self-contained (no MSIX), consistent with the "not Store-eligible yet" decision.

## Installer

MSIX is a non-starter regardless of Store plans since it can't install a Windows Service. Use **WiX Toolset v5**: elevation-required MSI that installs binaries under `Program Files\KidsMonitor\`, registers/starts the service (`Account=LocalSystem`, `Start=auto`), adds the `HKLM Run` key for Tray, and cleans up service+registry (but leaves `ProgramData` config) on uninstall. For day-to-day dev iteration before the MSI exists, use `tools\install-service.ps1` (`sc create`) + xcopy deploy.

## Build & Verification Milestones

1. **M0 — Scaffolding:** all projects build; Service installs/starts via `sc create` and logs; Tray shows a bare tray icon. No IPC yet.
2. **M1 — Live timer over IPC:** pipe client/server + `ActivityHeartbeat`; Tray flyout shows live "X of Y minutes used." Manually verify idle-reset, active counting, and graceful degradation when Tray is killed.
3. **M2 — Enforce lock:** `LockController` launches the Overlay at the limit; verify full-screen block, Alt+Tab suppression, watchdog respawn, `DisableTaskMgr` toggling — test with a short 2-minute limit.
4. **M3 — Password protection:** hashing/storage, first-run wizard, real `UnlockRequest` verification wired into the Overlay and Settings. Manually verify: fresh install forces setup, wrong password rejected, correct password unlocks, limit changes require password.
5. **M4 — Installer:** WiX MSI with service registration/ACLs/autostart/uninstall, tested end-to-end on a machine with a Standard (non-admin) test account, no dev scripts involved.
6. (Stretch, not v1): daily/weekly quotas, scheduled allowed-hours, Store-eligible trimmed tray-only build.

## Critical Files

- `src\KidsMonitor.Common\Ipc\PipeMessage.cs` + `Messages\` — the entire IPC contract shared by all three processes
- `src\KidsMonitor.Service\Worker\SessionTracker.cs` — core continuous-use/idle-reset timer logic (unit-testable, clock-injectable)
- `src\KidsMonitor.Service\Enforcement\LockController.cs` — `CreateProcessAsUser`/`WTSQueryUserToken` overlay launch + watchdog
- `src\KidsMonitor.Common\Security\PasswordHasher.cs` — PBKDF2 hash/verify shared by setup and unlock flows
- `src\KidsMonitor.Installer\Product.wxs` — service registration, ACL/registry setup

## Known Limitations (communicated upfront, not "bugs" to fix)

Local-admin child accounts, Safe Mode, or a second admin account can always defeat a user-mode-only enforcement design — no kernel driver/MDM in scope. Tray-heartbeat idle detection could in principle be spoofed by a sophisticated user; the session-change fallback mitigates the "just kill Tray" case but not deep spoofing (a v2 hardening step would verify the connecting pipe client's signed image path). Safe Mode skips third-party services by default and isn't addressed. Fast User Switching / multiple concurrent sessions need explicit testing since state is tracked per session/SID.


Start at M0: scaffold the solution/projects listed above under `C:\WORK\KidsMonitor`, get the Service installable via `tools\install-service.ps1` and logging startup, and get a bare Tray icon showing — before writing any enforcement logic.
