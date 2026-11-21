using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

public sealed record NotificationReceiverOptions(
    string AppId,
    string DisplayName,
    string SdkVersion,
    string PublisherProofPath,
    string? DiscoveryDirectory = null);

public sealed class NotificationReceiver : IAsyncDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly NotificationReceiverOptions _options;
    private readonly IntegrationDiscovery _discovery;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private string? _manifestPath;

    public NotificationReceiver(NotificationReceiverOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.AppId) || string.IsNullOrWhiteSpace(options.DisplayName))
            throw new ArgumentException("The integration app id and display name are required.", nameof(options));
        _discovery = new IntegrationDiscovery(options.DiscoveryDirectory);
        PipeName = "SecRandom_Integration_" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(options.AppId + Guid.NewGuid().ToString("N"))))[..32];
    }

    public string PipeName { get; }
    public event Func<DrawResult, CancellationToken, Task<bool>>? NotificationReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_acceptLoop is not null)
            throw new InvalidOperationException("The receiver is already running.");

        var sdkAssembly = typeof(NotificationReceiver).Assembly;
        if (string.IsNullOrWhiteSpace(sdkAssembly.Location))
            throw new InvalidOperationException("The official SDK assembly location is unavailable.");

        var sdkAssemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sdkAssembly.Location)))
            .ToLowerInvariant();
        var proof = JsonSerializer.Deserialize<OfficialSdkProof>(
            File.ReadAllText(_options.PublisherProofPath), IntegrationJson.Options)
            ?? throw new InvalidDataException("The official SDK proof is invalid.");
        var publicKey = sdkAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "SecRandomIntegrationPublicKey")?.Value;
        if (!OfficialSignature.Verify(proof, publicKey)
            || !string.Equals(proof.AssemblySha256, sdkAssemblyHash, StringComparison.Ordinal)
            || !string.Equals(proof.SdkVersion, sdkAssembly.GetName().Version?.ToString(), StringComparison.Ordinal)
            || !string.Equals(proof.SdkVersion, _options.SdkVersion, StringComparison.Ordinal))
            throw new InvalidDataException("The SDK assembly does not match its official publisher proof.");

        var publisherProof = JsonSerializer.Serialize(proof, IntegrationJson.Options);
        var manifest = new IntegrationManifest(
            _options.AppId,
            _options.DisplayName,
            _options.SdkVersion,
            IntegrationProtocol.ProtocolName,
            PipeName,
            Environment.ProcessId,
            Guid.NewGuid().ToString("N"),
            ["notification.result", "linkage.acquire", "linkage.release"],
            DateTimeOffset.UtcNow,
            publisherProof);
        _manifestPath = _discovery.Publish(manifest);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }
        if (_manifestPath is not null)
        {
            try { File.Delete(_manifestPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, StrictUtf8, false, 4096, true);
        await using var writer = new StreamWriter(server, StrictUtf8, 4096, true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null || Encoding.UTF8.GetByteCount(line) > IntegrationProtocol.MaxFrameBytes)
        {
            await WriteResponseAsync(writer, null, false, error: new IntegrationError("invalid_request", "Invalid or oversized frame."))
                .ConfigureAwait(false);
            return;
        }

        IntegrationEnvelope? request;
        try { request = JsonSerializer.Deserialize<IntegrationEnvelope>(line, IntegrationJson.Options); }
        catch (JsonException)
        {
            await WriteResponseAsync(writer, null, false, error: new IntegrationError("invalid_request", "Malformed JSON."))
                .ConfigureAwait(false);
            return;
        }

        if (request is null || request.Version != IntegrationProtocol.Version || request.Type != "rpc")
        {
            await WriteResponseAsync(writer, request?.Id, false, error: new IntegrationError("unsupported_version", "Unsupported protocol frame."))
                .ConfigureAwait(false);
            return;
        }

        if (request.Method == "system.ping")
        {
            await WriteResponseAsync(writer, request.Id, true, new { pong = true });
            return;
        }

        if (request.Method == "notification.result")
        {
            var result = request.Params is { ValueKind: JsonValueKind.Object } payload
                ? payload.Deserialize<DrawResult>(IntegrationJson.Options)
                : null;
            var handlers = NotificationReceived;
            if (result is null || handlers is null)
            {
                await WriteResponseAsync(writer, request.Id, false,
                    error: new IntegrationError("not_ready", "No result handler is registered.", true));
                return;
            }

            var accepted = true;
            foreach (Func<DrawResult, CancellationToken, Task<bool>> handler in handlers.GetInvocationList())
                accepted &= await handler(result, cancellationToken).ConfigureAwait(false);
            await WriteResponseAsync(writer, request.Id, true, new { accepted });
            return;
        }

        await WriteResponseAsync(writer, request.Id, false,
            error: new IntegrationError("unsupported_method", "The method is not supported."));
    }

    private static async Task WriteResponseAsync(StreamWriter writer, string? id, bool ok, object? result = null,
        IntegrationError? error = null)
    {
        var response = new IntegrationEnvelope(IntegrationProtocol.Version, "rpc", id, null, null, result, error);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, IntegrationJson.Options)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}

