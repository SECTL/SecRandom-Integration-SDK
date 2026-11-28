using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

/// <summary>
/// Thin client for SecRandom's v2 automation pipe. It sends intent only; omitted draw options
/// stay under the control of SecRandom's current configuration.
/// </summary>
public sealed partial class AutomationClient
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _pipeName;

    public AutomationClient(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? AutomationProtocol.PipeName : pipeName;
    }

    public string PipeName => _pipeName;

    public async Task<RosterListsSnapshot> GetRosterListsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("roster.lists", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Result?.Deserialize<RosterListsSnapshot>(AutomationJson.Options)
               ?? new RosterListsSnapshot([], []);
    }

    public async Task<RosterReadSnapshot> ReadRosterAsync(
        RosterKind kind,
        string? list = null,
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("roster.read", new
        {
            roster_kind = kind == RosterKind.Prizes ? "prizes" : "students",
            list_name = list,
            include_disabled = includeDisabled
        }, cancellationToken).ConfigureAwait(false);
        return response.Result?.Deserialize<RosterReadSnapshot>(AutomationJson.Options)
               ?? new RosterReadSnapshot(kind == RosterKind.Prizes ? "prizes" : "students", []);
    }

    public async Task<RosterSelectionOptions> GetRosterSelectionOptionsAsync(
        RosterKind kind,
        string? list = null,
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadRosterAsync(kind, list, includeDisabled, cancellationToken)
            .ConfigureAwait(false);
        return snapshot.ToSelectionOptions();
    }

    public async Task<AutomationResponse> CallAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("An automation method is required.", nameof(method));

        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

        await using var writer = new StreamWriter(pipe, StrictUtf8, 4096, leaveOpen: true);
        using var reader = new StreamReader(pipe, StrictUtf8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);
        var request = new AutomationRequest(
            AutomationProtocol.Version,
            "rpc",
            Guid.NewGuid().ToString("N"),
            method,
            parameters is null
                ? null
                : JsonSerializer.SerializeToElement(parameters, AutomationJson.Options));

        var requestLine = JsonSerializer.Serialize(request, AutomationJson.Options);
        if (StrictUtf8.GetByteCount(requestLine) > AutomationProtocol.MaxFrameBytes)
            throw new InvalidOperationException("The automation request exceeds the protocol frame limit.");

        await writer.WriteLineAsync(requestLine).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (responseLine is null || StrictUtf8.GetByteCount(responseLine) > AutomationProtocol.MaxFrameBytes)
            throw new InvalidDataException("The automation response was empty or too large.");

        var response = JsonSerializer.Deserialize<AutomationResponse>(responseLine, AutomationJson.Options)
                       ?? throw new InvalidDataException("The automation response was invalid.");
        if (response.Version != AutomationProtocol.Version || response.Type != "rpc" || response.Id != request.Id)
            throw new InvalidDataException("The automation response does not match the request.");
        if (!response.Ok)
            throw new AutomationException(response.Error ?? new AutomationError(
                "invalid_response", "SecRandom returned an unsuccessful response without an error."));
        return response;
    }

    public async Task<JsonElement> DrawAsync(
        DrawChannel channel,
        DrawOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DrawOptions();
        if (channel == DrawChannel.Quick && options.HasDrawOverrides)
            throw new ArgumentException("Quick draw uses SecRandom's current settings and does not support overrides.", nameof(options));
        if (options.OpenMainWindow)
        {
            await CallAsync("window.show", new { target = "main", page = channel.MainPage() }, cancellationToken)
                .ConfigureAwait(false);
        }

        var response = await CallAsync(channel.Method(), options.ToParameters(), cancellationToken)
            .ConfigureAwait(false);
        return response.Result ?? default;
    }

    public async Task<JsonElement> ActivateRosterAsync(
        RosterKind kind,
        string list,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(list))
            throw new ArgumentException("A roster name is required.", nameof(list));

        var response = await CallAsync("roster.activate", new
        {
            kind = kind == RosterKind.Prizes ? "prizes" : "students",
            list
        }, cancellationToken).ConfigureAwait(false);
        return response.Result ?? default;
    }

    public async Task<JsonElement> ShowMainWindowAsync(
        string? page = null,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("window.show", new
        {
            target = "main",
            page
        }, cancellationToken).ConfigureAwait(false);
        return response.Result ?? default;
    }
}
