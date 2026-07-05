using System.IO.Pipes;
using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using KidsMonitor.Service.Enforcement;
using KidsMonitor.Service.Security;
using KidsMonitor.Service.Session;

namespace KidsMonitor.Service.Ipc;

/// <summary>
/// Server side of the KidsMonitor named pipe. Accepts one connection at a time from Tray or
/// Overlay and handles heartbeats, setup, unlock, and limit-change requests.
/// </summary>
public sealed class PipeServer(
    SessionTracker tracker,
    PasswordStore passwordStore,
    ConfigStore configStore,
    LockController lockController,
    ILogger<PipeServer> logger) : BackgroundService
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

            switch (envelope.Type)
            {
                case nameof(ActivityHeartbeat):
                    await HandleHeartbeatAsync(envelope, writer, ct).ConfigureAwait(false);
                    break;

                case nameof(SetPasswordRequest):
                    await HandleSetPasswordAsync(envelope, pipe, writer, ct).ConfigureAwait(false);
                    break;

                case nameof(SetLimitsRequest):
                    await HandleSetLimitsAsync(envelope, writer, ct).ConfigureAwait(false);
                    break;

                case nameof(UnlockRequest):
                    await HandleUnlockAsync(envelope, writer, ct).ConfigureAwait(false);
                    break;

                default:
                    logger.LogWarning("Ignoring unexpected pipe message type {Type}", envelope.Type);
                    break;
            }
        }
    }

    private async Task HandleHeartbeatAsync(PipeEnvelope envelope, StreamWriter writer, CancellationToken ct)
    {
        var heartbeat = JsonSerializer.Deserialize<ActivityHeartbeat>(envelope.Payload);
        if (heartbeat is null)
        {
            return;
        }

        tracker.RecordHeartbeat(TimeSpan.FromSeconds(heartbeat.IdleSeconds));

        var status = new StatusUpdate((int)tracker.UsedTime.TotalSeconds, (int)tracker.Limit.TotalSeconds, passwordStore.IsSetupRequired);
        await PipeProtocol.WriteMessageAsync(writer, nameof(StatusUpdate), status, ct).ConfigureAwait(false);
    }

    private async Task HandleSetPasswordAsync(PipeEnvelope envelope, NamedPipeServerStream pipe, StreamWriter writer, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<SetPasswordRequest>(envelope.Payload);
        if (request is null)
        {
            return;
        }

        if (passwordStore.IsSetupRequired && !PipeClientAuth.ConnectedClientIsAdministrator(pipe))
        {
            logger.LogWarning("Rejected first-run SetPasswordRequest from a non-administrator client");
            await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult),
                new OperationResult(false, "Only an administrator can complete first-run setup."), ct).ConfigureAwait(false);
            return;
        }

        var success = passwordStore.TrySetPassword(request.CurrentPassword, request.NewPassword, out var error);
        await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult), new OperationResult(success, error), ct).ConfigureAwait(false);
    }

    private async Task HandleSetLimitsAsync(PipeEnvelope envelope, StreamWriter writer, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<SetLimitsRequest>(envelope.Payload);
        if (request is null)
        {
            return;
        }

        if (!passwordStore.Verify(request.CurrentPassword))
        {
            await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult),
                new OperationResult(false, "Current password is incorrect."), ct).ConfigureAwait(false);
            return;
        }

        var newLimit = TimeSpan.FromMinutes(request.DailyLimitMinutes);
        tracker.UpdateLimit(newLimit);
        configStore.WriteDailyLimitMinutes(request.DailyLimitMinutes);

        await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult), new OperationResult(true, null), ct).ConfigureAwait(false);
    }

    private async Task HandleUnlockAsync(PipeEnvelope envelope, StreamWriter writer, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<UnlockRequest>(envelope.Payload);
        if (request is null)
        {
            return;
        }

        if (!passwordStore.Verify(request.Password))
        {
            await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult),
                new OperationResult(false, "Incorrect password."), ct).ConfigureAwait(false);
            return;
        }

        // Respond before disengaging -- DisengageLock kills the Overlay process that's likely
        // the very client we're replying to.
        await PipeProtocol.WriteMessageAsync(writer, nameof(OperationResult), new OperationResult(true, null), ct).ConfigureAwait(false);

        tracker.Reset();
        lockController.DisengageLock();
    }
}
