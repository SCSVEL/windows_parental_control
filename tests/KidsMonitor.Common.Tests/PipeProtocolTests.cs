using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;

namespace KidsMonitor.Common.Tests;

public class PipeProtocolTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsActivityHeartbeat()
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
        {
            await PipeProtocol.WriteMessageAsync(writer, nameof(ActivityHeartbeat), new ActivityHeartbeat(IdleSeconds: 42));
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var envelope = await PipeProtocol.ReadMessageAsync(reader);

        Assert.NotNull(envelope);
        Assert.Equal(nameof(ActivityHeartbeat), envelope!.Type);

        var payload = System.Text.Json.JsonSerializer.Deserialize<ActivityHeartbeat>(envelope.Payload);
        Assert.NotNull(payload);
        Assert.Equal(42, payload!.IdleSeconds);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsStatusUpdate()
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
        {
            await PipeProtocol.WriteMessageAsync(writer, nameof(StatusUpdate), new StatusUpdate(UsedSeconds: 100, LimitSeconds: 7200, SetupRequired: false));
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var envelope = await PipeProtocol.ReadMessageAsync(reader);

        var payload = System.Text.Json.JsonSerializer.Deserialize<StatusUpdate>(envelope!.Payload);
        Assert.Equal(100, payload!.UsedSeconds);
        Assert.Equal(7200, payload.LimitSeconds);
    }

    [Fact]
    public async Task MultipleMessages_AreDelimitedByLine()
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
        {
            await PipeProtocol.WriteMessageAsync(writer, nameof(ActivityHeartbeat), new ActivityHeartbeat(1));
            await PipeProtocol.WriteMessageAsync(writer, nameof(ActivityHeartbeat), new ActivityHeartbeat(2));
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);

        var first = await PipeProtocol.ReadMessageAsync(reader);
        var second = await PipeProtocol.ReadMessageAsync(reader);
        var third = await PipeProtocol.ReadMessageAsync(reader);

        Assert.Equal(1, System.Text.Json.JsonSerializer.Deserialize<ActivityHeartbeat>(first!.Payload)!.IdleSeconds);
        Assert.Equal(2, System.Text.Json.JsonSerializer.Deserialize<ActivityHeartbeat>(second!.Payload)!.IdleSeconds);
        Assert.Null(third);
    }
}
