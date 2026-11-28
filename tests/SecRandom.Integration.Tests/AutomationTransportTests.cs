using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;
using SecRandom.Integration.Client;

namespace SecRandom.Integration.Tests;

public sealed class AutomationTransportTests
{
    [Fact]
    public async Task Draw_RejectsOverridesThatTheTargetWouldIgnore()
    {
        var client = new AutomationClient();
        await Assert.ThrowsAsync<ArgumentException>(() => client.DrawAsync(DrawChannel.Quick, new DrawOptions(Count: 2), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.ConfigureDrawAsync(DrawChannel.Lottery,
            new DrawOptions { Conditions = new LotteryDrawConditions(["gold"]) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteRoster_UsesHostContract()
    {
        var name = "sr-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var call = new AutomationClient(name).WriteRosterAsync(RosterKind.Students, "A",
            [new { id = "1", name = "Alice" }], RosterWriteMode.Merge, token: timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var request = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
        var root = request.RootElement;
        Assert.Equal("roster.write", root.GetProperty("method").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal("students", parameters.GetProperty("roster_kind").GetString());
        Assert.Equal("A", parameters.GetProperty("list_name").GetString());
        Assert.Equal("merge", parameters.GetProperty("mode").GetString());
        Assert.Equal("Alice", parameters.GetProperty("students")[0].GetProperty("name").GetString());
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { version = 2, type = "rpc", id = root.GetProperty("id").GetString(), ok = true, result = new { written = true } }));
        Assert.True((await call).GetProperty("written").GetBoolean());
    }

    [Fact]
    public async Task Call_RejectsMismatchedResponseId()
    {
        var name = "sr-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var call = new AutomationClient(name).PingAsync(timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await reader.ReadLineAsync(timeout.Token);
        await writer.WriteLineAsync("{\"version\":2,\"type\":\"rpc\",\"id\":\"wrong\",\"ok\":true}");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await call);
    }

    [Fact]
    public async Task Events_SubscribeAndReceiveOnLongLivedPipe()
    {
        var name = "sr-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = new AutomationEvents(name).SubscribeAsync(["draw.completed"], timeout.Token).GetAsyncEnumerator(timeout.Token);
        var next = events.MoveNextAsync().AsTask();
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var subscription = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
        Assert.Equal("subscribe", subscription.RootElement.GetProperty("type").GetString());
        Assert.Equal("draw.completed", subscription.RootElement.GetProperty("types")[0].GetString());
        await writer.WriteLineAsync("{\"version\":2,\"type\":\"subscribed\",\"seqStart\":0,\"types\":[\"draw.completed\"]}");
        await writer.WriteLineAsync("{\"version\":2,\"type\":\"event\",\"seq\":1,\"event\":\"draw.completed\",\"data\":{\"count\":1}}");
        Assert.True(await next);
        Assert.Equal("draw.completed", events.Current.Name);
        Assert.Equal(1, events.Current.Sequence);
        Assert.Equal(1, events.Current.Data!.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Events_ExposesDisabledErrorFromServer()
    {
        var name = "sr-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = new AutomationEvents(name).SubscribeAsync(cancellationToken: timeout.Token).GetAsyncEnumerator(timeout.Token);
        var next = events.MoveNextAsync().AsTask();
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await reader.ReadLineAsync(timeout.Token);
        await writer.WriteLineAsync("{\"version\":2,\"type\":\"error\",\"ok\":false,\"error\":{\"code\":\"ipc_disabled\",\"message\":\"disabled\"}}");
        var exception = await Assert.ThrowsAsync<AutomationException>(async () => await next);
        Assert.Equal("ipc_disabled", exception.Code);
    }

    [Fact]
    public async Task LegacyCall_ReturnsBusinessErrorWithoutTreatingItAsTransportSuccess()
    {
        var name = "sr-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var call = new AutomationClient(name).CallLegacyAsync("data/roll_call_list?name=A", timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var request = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
        Assert.Equal(1, request.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("data/roll_call_list?name=A", request.RootElement.GetProperty("payload").GetProperty("url").GetString());
        await writer.WriteLineAsync("{\"success\":true,\"type\":\"url\",\"result\":{\"status\":\"error\",\"code\":\"not_found\"}}");
        var response = await call;
        Assert.True(response.GetProperty("success").GetBoolean());
        Assert.Equal("error", response.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public async Task NotificationReceiver_PipeCanBeCreated()
    {
        await using var receiver = new NotificationReceiver(new("test.app", "Test", "1.2.0.0", "unused"));
        using var server = new NamedPipeServerStream(receiver.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.StartsWith("SecRandom_Integration_", receiver.PipeName);
    }
}
