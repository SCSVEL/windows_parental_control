using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;

namespace KidsMonitor_Tray;

/// <summary>
/// Connects to the Service's named pipe and sends an ActivityHeartbeat every few seconds,
/// updating the given StatusViewModel from each StatusUpdate reply. Reconnects on failure
/// (e.g. Service not running yet) so Tray degrades gracefully instead of crashing.
/// </summary>
public sealed class HeartbeatWorker(StatusViewModel status)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = RunAsync(_cts.Token);

    public void Stop() => _cts.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var client = new KidsMonitorPipeClient();
                await client.ConnectAsync(timeoutMs: 3000, ct);

                while (!ct.IsCancellationRequested)
                {
                    var idle = IdleTimeProvider.GetIdleTime();
                    await client.SendAsync(nameof(ActivityHeartbeat), new ActivityHeartbeat((int)idle.TotalSeconds), ct);

                    var envelope = await client.ReadMessageAsync(ct);
                    if (envelope is null)
                    {
                        break; // Service closed the connection
                    }

                    if (envelope.Type == nameof(StatusUpdate))
                    {
                        var update = JsonSerializer.Deserialize<StatusUpdate>(envelope.Payload);
                        if (update is not null)
                        {
                            status.UpdateStatus(update.UsedSeconds, update.LimitSeconds);
                        }
                    }

                    await Task.Delay(HeartbeatInterval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                status.ShowDisconnected();
            }

            if (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(ReconnectDelay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
