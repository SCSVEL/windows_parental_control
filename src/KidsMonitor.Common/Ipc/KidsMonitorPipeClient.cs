using System.IO.Pipes;

namespace KidsMonitor.Common.Ipc;

/// <summary>
/// Client side of the KidsMonitor named pipe, shared by Tray and Overlay so both talk to
/// the Service the same way.
/// </summary>
public sealed class KidsMonitorPipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public KidsMonitorPipeClient()
    {
        _pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public bool IsConnected => _pipe.IsConnected;

    public async Task ConnectAsync(int timeoutMs, CancellationToken ct = default)
    {
        await _pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        _reader = new StreamReader(_pipe);
        _writer = new StreamWriter(_pipe) { AutoFlush = true };
    }

    public Task SendAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Pipe client is not connected.");
        }

        return PipeProtocol.WriteMessageAsync(_writer, type, payload, ct);
    }

    public Task<PipeEnvelope?> ReadMessageAsync(CancellationToken ct = default)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("Pipe client is not connected.");
        }

        return PipeProtocol.ReadMessageAsync(_reader, ct);
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        return _pipe.DisposeAsync();
    }
}
