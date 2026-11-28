using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SecRandom.Integration.Client;

public sealed partial class AutomationClient
{
    /// <summary>Calls a v1 route and returns its complete transport and business response.</summary>
    public async Task<JsonElement> CallLegacyAsync(string route, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(route)) throw new ArgumentException("A route is required.", nameof(route));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        var utf8 = new UTF8Encoding(false, true);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true);
        var request = JsonSerializer.Serialize(new { version = 1, type = "url", payload = new { url = route } });
        if (utf8.GetByteCount(request) > SecRandom.Integration.AutomationProtocol.MaxFrameBytes)
            throw new InvalidOperationException("The request exceeds the protocol frame limit.");
        await writer.WriteLineAsync(request).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        var response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (response is null || utf8.GetByteCount(response) > SecRandom.Integration.AutomationProtocol.MaxFrameBytes)
            throw new InvalidDataException("The legacy response was empty or too large.");
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("success", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("The legacy response was invalid.");
        return document.RootElement.Clone();
    }
}
