using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
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
    // Serializes access to tracker/passwordStore/configStore/lockController across
    // concurrently-connected clients (see below); each message is handled to completion
    // before the next one (from any client) starts, so business logic never needs its own locks.
    private readonly SemaphoreSlim _handlerLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Pipe client connected");

                // Fire-and-forget: HeartbeatWorker holds its connection open indefinitely, so
                // awaiting HandleClientAsync here (as before) would leave the accept loop stuck
                // inside that one connection forever, and every other client (Settings,
                // FirstRunSetup) would time out trying to connect. Handling each client on its
                // own task lets the loop immediately create the next pipe instance and accept
                // the next connection.
                _ = HandleClientConnectionAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in pipe server accept loop");
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        try
        {
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
            logger.LogError(ex, "Unexpected error handling pipe client");
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private const int PIPE_ACCESS_DUPLEX = 0x00000003;
    private const int FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int PIPE_TYPE_BYTE = 0x00000000;
    private const int PIPE_READMODE_BYTE = 0x00000000;
    private const int PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateNamedPipeW(
        string lpName,
        int dwOpenMode,
        int dwPipeMode,
        int nMaxInstances,
        int nOutBufferSize,
        int nInBufferSize,
        int nDefaultTimeOut,
        ref SECURITY_ATTRIBUTES lpSecurityAttributes);

    /// <summary>
    /// Without an explicit ACL, Windows gives a pipe created by this LocalSystem service a
    /// default DACL covering only SYSTEM/Administrators -- Tray and Overlay, running as the
    /// standard logged-in user, would get Access Denied on connect before the server ever sees
    /// them (no exception here, no log line, the client just retries forever). The managed
    /// PipeSecurity type is available inbox, but the constructors/helpers that would let
    /// NamedPipeServerStream take one directly require the separate
    /// System.IO.Pipes.AccessControl package, which the .NET 8 SDK on this machine silently
    /// drops from the compile references (resolved in project.assets.json, never reaches csc --
    /// a local SDK/NuGet conflict-resolution quirk, not a namespace-availability issue). Calling
    /// CreateNamedPipeW directly with the PipeSecurity's binary form sidesteps that entirely.
    /// </summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        var descriptorBytes = pipeSecurity.GetSecurityDescriptorBinaryForm();
        var descriptorHandle = GCHandle.Alloc(descriptorBytes, GCHandleType.Pinned);
        try
        {
            var securityAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = descriptorHandle.AddrOfPinnedObject(),
                bInheritHandle = 0,
            };

            var handle = CreateNamedPipeW(
                $@"\\.\pipe\{PipeProtocol.PipeName}",
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_REJECT_REMOTE_CLIENTS,
                4,
                0,
                0,
                0,
                ref securityAttributes);
            var error = Marshal.GetLastWin32Error();

            var safeHandle = new SafePipeHandle(handle, ownsHandle: true);
            if (safeHandle.IsInvalid)
            {
                safeHandle.Dispose();
                throw new IOException($"CreateNamedPipeW failed with Win32 error {error}");
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, safeHandle);
        }
        finally
        {
            descriptorHandle.Free();
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

            await _handlerLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
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
            finally
            {
                _handlerLock.Release();
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

        var status = new StatusUpdate(
            (int)tracker.UsedTime.TotalSeconds,
            (int)tracker.Limit.TotalSeconds,
            passwordStore.IsSetupRequired,
            (int)tracker.BreakInterval.TotalMinutes,
            (int)tracker.BreakDuration.TotalMinutes);
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

        var breakInterval = TimeSpan.FromMinutes(request.BreakIntervalMinutes);
        var breakDuration = TimeSpan.FromMinutes(request.BreakDurationMinutes);
        tracker.UpdateBreakSettings(breakInterval, breakDuration);
        configStore.WriteBreakSettings(request.BreakIntervalMinutes, request.BreakDurationMinutes);

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
