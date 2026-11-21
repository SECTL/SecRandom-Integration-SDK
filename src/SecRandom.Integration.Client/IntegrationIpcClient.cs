using System.IO.Pipes;
using System.Diagnostics;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

public sealed class IntegrationIpcClient
{
    public async Task<IntegrationEnvelope> CallAsync(string pipeName, string method, object? parameters,
        CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        using var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 4096, true);
        using var reader = new StreamReader(pipe, new System.Text.UTF8Encoding(false), false, 4096, true);
        var request = new IntegrationEnvelope(IntegrationProtocol.Version, "rpc", Guid.NewGuid().ToString("N"),
            method, parameters is null ? null : JsonSerializer.SerializeToElement(parameters, IntegrationJson.Options));
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, IntegrationJson.Options)).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null || line.Length > IntegrationProtocol.MaxFrameBytes)
            throw new InvalidDataException("The integration response was empty or too large.");
        return JsonSerializer.Deserialize<IntegrationEnvelope>(line, IntegrationJson.Options)
               ?? throw new InvalidDataException("The integration response was invalid.");
    }

    public async Task<IAsyncDisposable> AcquireLinkageAsync(string appId, string instanceId,
        string? hostManifestPath = null, CancellationToken cancellationToken = default)
    {
        var path = hostManifestPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecRandom", "integrations", "host.json");
        var host = JsonSerializer.Deserialize<IntegrationHostManifest>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), IntegrationJson.Options)
            ?? throw new InvalidDataException("The SecRandom integration endpoint is invalid.");
        if (host.Protocol != IntegrationProtocol.ProtocolName || !IntegrationDiscovery.IsProcessAlive(host.ProcessId))
            throw new InvalidOperationException("SecRandom is not running with a compatible integration endpoint.");

        var leaseId = Guid.NewGuid().ToString("N");
        var request = new LinkageLeaseRequest(appId, Environment.ProcessId, instanceId, leaseId);
        var response = await CallAsync(host.Endpoint, "linkage.acquire", request, cancellationToken)
            .ConfigureAwait(false);
        if (response.Error is not null || !response.ResultAccepted())
            throw new InvalidOperationException(response.Error?.Message ?? "SecRandom rejected the linkage request.");

        return new IntegrationLinkageLease(this, host.Endpoint, request);
    }

    internal async ValueTask ReleaseLinkageAsync(string pipeName, LinkageLeaseRequest request)
    {
        try
        {
            await CallAsync(pipeName, "linkage.release", request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
        }
    }
}

internal static class IntegrationEnvelopeExtensions
{
    public static bool ResultAccepted(this IntegrationEnvelope envelope) =>
        envelope.Result is JsonElement { ValueKind: JsonValueKind.Object } result
        && result.TryGetProperty("accepted", out var accepted)
        && accepted.ValueKind == JsonValueKind.True;
}

public sealed class IntegrationLinkageLease : IAsyncDisposable
{
    private readonly IntegrationIpcClient _client;
    private readonly string _pipeName;
    private readonly LinkageLeaseRequest _request;
    private int _released;

    internal IntegrationLinkageLease(IntegrationIpcClient client, string pipeName, LinkageLeaseRequest request)
    {
        _client = client;
        _pipeName = pipeName;
        _request = request;
    }

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _released, 1) == 0
        ? _client.ReleaseLinkageAsync(_pipeName, _request)
        : ValueTask.CompletedTask;
}

