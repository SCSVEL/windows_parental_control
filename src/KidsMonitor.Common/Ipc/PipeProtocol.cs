using System.Text.Json;

namespace KidsMonitor.Common.Ipc;

/// <summary>
/// Wire format shared by both ends of the KidsMonitor named pipe: one JSON-encoded
/// PipeEnvelope per line, with the actual message JSON nested as the envelope's Payload.
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "KidsMonitor";

    public static async Task WriteMessageAsync<T>(StreamWriter writer, string type, T payload, CancellationToken ct = default)
    {
        var envelope = new PipeEnvelope(type, JsonSerializer.Serialize(payload));
        await writer.WriteLineAsync(JsonSerializer.Serialize(envelope).AsMemory(), ct).ConfigureAwait(false);
    }

    public static async Task<PipeEnvelope?> ReadMessageAsync(StreamReader reader, CancellationToken ct = default)
    {
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        return line is null ? null : JsonSerializer.Deserialize<PipeEnvelope>(line);
    }
}
