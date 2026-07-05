namespace KidsMonitor.Common.Ipc.Messages;

/// <summary>First message a client sends after connecting.</summary>
public sealed record Hello(string ProcessName);

/// <summary>Sent by Tray every few seconds; IdleSeconds comes from GetLastInputInfo.</summary>
public sealed record ActivityHeartbeat(int IdleSeconds);

/// <summary>Server's reply to a heartbeat: current accumulated usage vs. the configured limit.</summary>
public sealed record StatusUpdate(int UsedSeconds, int LimitSeconds);
