using System.IO.Pipes;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using KidsMonitor.Service.Session;

namespace KidsMonitor.Service.Ipc;

/// <summary>
/// Server side of the KidsMonitor named pipe. Accepts one connection at a time from Tray
/// (later, also Overlay), feeds ActivityHeartbeats into the shared SessionTracker, and pushes
/// a StatusUpdate back after each heartbeat so the client can render live usage.
/// </summary>
public sealed class PipeServer(SessionTracker tracker, ILogger<PipeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeProtocol.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Pipe client connected");
                await HandleClientAsync(pipe, stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Pipe client disconnected");
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Pipe client disconnected unexpectedly");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in pipe server accept loop");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var envelope = await PipeProtocol.ReadMessageAsync(reader, ct).ConfigureAwait(false);
            if (envelope is null)
            {
                break;
            }

            if (envelope.Type != nameof(ActivityHeartbeat))
            {
                logger.LogWarning("Ignoring unexpected pipe message type {Type}", envelope.Type);
                continue;
            }

            var heartbeat = System.Text.Json.JsonSerializer.Deserialize<ActivityHeartbeat>(envelope.Payload);
            if (heartbeat is null)
            {
                continue;
            }

            tracker.RecordHeartbeat(TimeSpan.FromSeconds(heartbeat.IdleSeconds));

            var status = new StatusUpdate((int)tracker.UsedTime.TotalSeconds, (int)tracker.Limit.TotalSeconds);
            await PipeProtocol.WriteMessageAsync(writer, nameof(StatusUpdate), status, ct).ConfigureAwait(false);
        }
    }
}
