using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

public sealed record AutomationEvent(
    [property: JsonPropertyName("seq")] long Sequence,
    [property: JsonPropertyName("event")] string Name,
    [property: JsonPropertyName("data")] JsonElement? Data);

public sealed class AutomationEvents(string? pipeName = null)
{
    public string PipeName { get; } = string.IsNullOrWhiteSpace(pipeName) ? AutomationProtocol.EventPipeName : pipeName;

    public async IAsyncEnumerable<AutomationEvent> SubscribeAsync(
        IEnumerable<string>? types = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        var utf8 = new UTF8Encoding(false, true);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "subscribe", types }, AutomationJson.Options)).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        var acknowledgement = await ReadFrameAsync(reader, cancellationToken).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(acknowledgement))
        {
            var root = document.RootElement;
            ThrowProtocolError(root);
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "subscribed")
                throw new InvalidDataException("SecRandom rejected the event subscription.");
        }

        while (true)
        {
            var frame = await ReadFrameAsync(reader, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            ThrowProtocolError(root);
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "event")
                throw new InvalidDataException("Invalid event frame.");
            yield return JsonSerializer.Deserialize<AutomationEvent>(frame, AutomationJson.Options)
                         ?? throw new InvalidDataException("Invalid event payload.");
        }
    }

    private static void ThrowProtocolError(JsonElement frame)
    {
        if (frame.TryGetProperty("type", out var type) && type.GetString() == "error")
        {
            var error = frame.TryGetProperty("error", out var element)
                ? element.Deserialize<AutomationError>(AutomationJson.Options)
                : null;
            throw new AutomationException(error ?? new AutomationError("invalid_response", "Invalid event error."));
        }
    }

    private static async Task<string> ReadFrameAsync(StreamReader reader, CancellationToken token)
    {
        var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
        if (line is null || Encoding.UTF8.GetByteCount(line) > AutomationProtocol.MaxFrameBytes)
            throw new InvalidDataException("The event frame was empty or too large.");
        return line;
    }
}
