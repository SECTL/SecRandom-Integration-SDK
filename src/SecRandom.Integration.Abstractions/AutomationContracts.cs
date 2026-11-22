using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Integration;

/// <summary>
/// Constants shared by every language binding. The transport is deliberately small: one JSON
/// request and one JSON response per current-user pipe connection.
/// </summary>
public static class AutomationProtocol
{
    public const int Version = 2;
    public const int MaxFrameBytes = 256 * 1024;
    public const string PipeName = "SecRandom_IPC_SecRandom_3F2A1B0E";
    public const string EventPipeName = "SecRandom_IPC_Events_SecRandom_3F2A1B0E";
}

public enum DrawChannel
{
    RollCall,
    Quick,
    Lottery
}

/// <summary>
/// Optional per-draw overrides. A null value is omitted from the wire request, which makes
/// SecRandom use the setting already selected in its UI.
/// </summary>
public sealed record DrawOptions(
    string? List = null,
    int? Count = null,
    string? Gender = null,
    string? Group = null,
    bool OpenMainWindow = false)
{
    public bool HasDrawOverrides => List is not null || Count is not null || Gender is not null || Group is not null;

    public object? ToParameters() => HasDrawOverrides
        ? new { list = List, count = Count, gender = Gender, group = Group }
        : null;
}

public sealed record RosterListsSnapshot(
    [property: JsonPropertyName("students")] IReadOnlyList<string> Students,
    [property: JsonPropertyName("prizes")] IReadOnlyList<string> Prizes);

public sealed record RosterSnapshotMember(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("enabled")] bool Enabled = true);

public sealed record RosterSnapshotList(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("members")] IReadOnlyList<RosterSnapshotMember> Members);

public sealed record RosterReadSnapshot(
    [property: JsonPropertyName("roster_kind")] string RosterKind,
    [property: JsonPropertyName("lists")] IReadOnlyList<RosterSnapshotList> Lists)
{
    public RosterSelectionOptions ToSelectionOptions() => new(
        RosterKind,
        Lists.SelectMany(list => list.Members).ToArray(),
        Lists.SelectMany(list => list.Members)
            .Select(member => member.Group)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray(),
        Lists.SelectMany(list => list.Members)
            .Select(member => member.Gender)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
}

public sealed record RosterSelectionOptions(
    string RosterKind,
    IReadOnlyList<RosterSnapshotMember> Members,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Genders);

public sealed record AutomationRequest(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record AutomationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false,
    [property: JsonPropertyName("details")] JsonElement? Details = null);

public sealed record AutomationResponse(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] JsonElement? Result = null,
    [property: JsonPropertyName("error")] AutomationError? Error = null);

public sealed class AutomationException : Exception
{
    public AutomationException(AutomationError error)
        : base($"{error.Code}: {error.Message}")
    {
        Error = error;
    }

    public AutomationError Error { get; }
    public string Code => Error.Code;
    public bool Retryable => Error.Retryable;
    public JsonElement? Details => Error.Details;
}

public static class AutomationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public static class DrawChannelExtensions
{
    public static string Method(this DrawChannel channel) => channel switch
    {
        DrawChannel.RollCall => "draw.rollCall.trigger",
        DrawChannel.Quick => "draw.quick.trigger",
        DrawChannel.Lottery => "draw.lottery.trigger",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    public static string MainPage(this DrawChannel channel) => channel == DrawChannel.Lottery
        ? "main.lottery"
        : "main.rollCall";
}
