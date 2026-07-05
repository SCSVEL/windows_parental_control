namespace KidsMonitor.Common.Ipc.Messages;

/// <summary>First message a client sends after connecting.</summary>
public sealed record Hello(string ProcessName);

/// <summary>Sent by Tray every few seconds; IdleSeconds comes from GetLastInputInfo.</summary>
public sealed record ActivityHeartbeat(int IdleSeconds);

/// <summary>
/// Server's reply to a heartbeat: current accumulated usage vs. the configured limit, and
/// whether no password has been set yet (Tray must show the first-run setup wizard instead
/// of the normal flyout).
/// </summary>
public sealed record StatusUpdate(int UsedSeconds, int LimitSeconds, bool SetupRequired);

/// <summary>
/// Sets the password. CurrentPassword must be null and the connecting pipe client's token must
/// be in the local Administrators group when no password exists yet (first-run setup) -- this
/// stops a kid from racing the parent to set it. Once a password exists, CurrentPassword must
/// verify against it.
/// </summary>
public sealed record SetPasswordRequest(string? CurrentPassword, string NewPassword);

/// <summary>Changes the daily limit; always requires the current password.</summary>
public sealed record SetLimitsRequest(string CurrentPassword, int DailyLimitMinutes);

/// <summary>Sent by the Overlay to attempt to lift the lock.</summary>
public sealed record UnlockRequest(string Password);

/// <summary>Generic reply to SetPasswordRequest, SetLimitsRequest, and UnlockRequest.</summary>
public sealed record OperationResult(bool Success, string? Error);
