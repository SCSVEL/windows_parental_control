# KidsMonitor

A tamper-resistant parental control app for Windows that limits daily screen time and locks
the machine with a full-screen overlay once the limit is reached. Enforcement runs as a
LocalSystem Windows Service (not just a tray app), so a child can't bypass it by killing a
process from Task Manager.

For the original design rationale and architecture notes, see [plan.md](plan.md).

## Features

- **Daily time limit** — tracks continuous active use and locks the screen once the configured
  daily limit is reached. Usage resets automatically at local midnight, even if the machine is
  asleep, locked, or logged off overnight.
- **Full-screen lock overlay** — borderless, always-on-top, spans every monitor, forcibly takes
  keyboard focus, and can't be closed, Alt+Tabbed away from, or minimized. Task Manager is
  disabled for the child's account while locked.
- **Tamper-resistant** — the lock is driven and watched by a Windows Service running as
  LocalSystem, independent of the tray app. If the overlay process is killed, it's relaunched
  within about a second. The service itself is configured to auto-restart if it ever crashes.
- **Password-protected unlock** — a parent password (PBKDF2-hashed, verified only inside the
  Service, never trusted client-side) unlocks the screen early from either the lock overlay or
  the tray app.
- **Idle-reset threshold** — only counts time as "used" while the child is actually active;
  a configurable idle window (default 3 minutes) keeps things like watching a video from
  ticking down as usage.
- **Optional mandatory breaks** — force a short break every N minutes of continuous use; a
  break lock lifts on its own after its duration elapses, no password needed.
- **Live status in the tray** — the tray icon's flyout shows "X of Y min used today" at a
  glance.

## Requirements

- Windows 10 or 11, 64-bit
- Administrator rights on the machine (for installation only — the child's own account should
  be a Standard, non-admin account; see [Known limitations](#known-limitations))

## Installing

1. Download the latest `KidsMonitor.zip` from the
   [Releases page](https://github.com/SCSVEL/windows_parental_control/releases).
2. Extract it and run `KidsMonitor.msi`, elevating when prompted.
3. The installer registers and starts the `KidsMonitorService` Windows service (auto-start, and
   configured to auto-restart itself if it ever crashes) and launches the tray icon in the
   current session. The tray app also auto-starts on future logons.

## First-run setup

On first launch there's no password set yet, so enforcement is off and the tray icon's flyout
reads "Setup required." Clicking it opens a setup window where the parent (this step requires
running as a local administrator, so a child can't race to set it first) chooses:

- The initial **daily time limit** (minutes)
- The **parent password** used for all future unlocks and settings changes

Once submitted, enforcement is live immediately.

## Day-to-day usage

- **Checking remaining time:** right-click (or click) the tray icon — the flyout shows how many
  minutes have been used out of today's limit.
- **When the limit is hit:** the full-screen lock appears automatically. A parent can unlock it
  early by entering the password directly on the lock screen.
- **Changing settings:** tray icon → **Settings...** → enter the current password to change the
  daily limit, mandatory-break interval/duration, idle-reset threshold, and/or the password
  itself. All changes require the current password.
- **Automatic reset:** the used-time counter resets on its own at local midnight — no action
  needed, and it works even if the machine was locked or asleep overnight.

## Uninstalling

Uninstall `KidsMonitor` from Windows' **Apps & Features** (or **Add/Remove Programs**) like any
other app. This removes the service and its registry entries. Configuration (`config.json`,
`password.dat`) is intentionally left behind in `C:\ProgramData\KidsMonitor`, so reinstalling
later picks up the same limit and password rather than starting over.

## Known limitations

- A child with local administrator rights, Safe Mode access, or a second admin account can
  defeat any user-mode-only enforcement — there's no kernel driver or MDM involved. **The
  child's Windows account should be a Standard (non-admin) account.**
- Safe Mode skips third-party services by default, so enforcement doesn't apply there.

See [plan.md](plan.md) for the full architecture writeup, including the reasoning behind these
tradeoffs.

## Development

```
dotnet build KidsMonitor.sln                          # build everything
dotnet test tests\KidsMonitor.Common.Tests\...         # unit tests
dotnet test tests\KidsMonitor.Service.Tests\...
tools\install-service.ps1                              # fast dev-loop install (no MSI)
tools\build-installer.ps1                               # publish + build the real MSI
```

Solution layout, IPC contract, and the locking mechanism are documented in
[plan.md](plan.md).
