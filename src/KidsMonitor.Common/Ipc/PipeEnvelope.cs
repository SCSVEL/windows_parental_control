namespace KidsMonitor.Common.Ipc;

public sealed record PipeEnvelope(string Type, string Payload);
