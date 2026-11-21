using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Integration;

public static class IntegrationProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 256 * 1024;
    public const string ProtocolName = "secrandom.integration.v1";
}

public enum DrawMode
{
    Random = 0,
    Fair = 1
}

public enum RosterKind
{
    Students = 0,
    Prizes = 1
}

public sealed record RosterMember(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("gender")] string? Gender = null,
    [property: JsonPropertyName("group")] string? Group = null,
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("weight")] double Weight = 1);

public sealed record Roster(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] RosterKind Kind,
    [property: JsonPropertyName("members")] IReadOnlyList<RosterMember> Members);

public sealed record HistoryEntry(
    [property: JsonPropertyName("round_id")] string RoundId,
    [property: JsonPropertyName("roster_id")] string RosterId,
    [property: JsonPropertyName("member_ids")] IReadOnlyList<string> MemberIds,
    [property: JsonPropertyName("draw_mode")] DrawMode DrawMode,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("source_app_id")] string? SourceAppId = null);

public sealed record DrawRequest(
    [property: JsonPropertyName("roster_id")] string RosterId,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("mode")] DrawMode Mode,
    [property: JsonPropertyName("gender")] string? Gender = null,
    [property: JsonPropertyName("group")] string? Group = null);

public sealed record DrawResult(
    [property: JsonPropertyName("round_id")] string RoundId,
    [property: JsonPropertyName("roster_id")] string RosterId,
    [property: JsonPropertyName("mode")] DrawMode? Mode,
    [property: JsonPropertyName("members")] IReadOnlyList<RosterMember> Members,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey,
    [property: JsonPropertyName("source_app_id")] string? SourceAppId = null,
    [property: JsonPropertyName("roster_kind")] RosterKind RosterKind = RosterKind.Students);

public sealed record IntegrationManifest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("sdk_version")] string SdkVersion,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("publisher_proof")] string? PublisherProof = null);

public sealed record IntegrationHostManifest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("protocol")] string Protocol);

public sealed record LinkageLeaseRequest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("lease_id")] string LeaseId);

public sealed record OfficialSdkProof(
    [property: JsonPropertyName("package_id")] string PackageId,
    [property: JsonPropertyName("sdk_version")] string SdkVersion,
    [property: JsonPropertyName("assembly_sha256")] string AssemblySha256,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record IntegrationEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] IntegrationError? Error = null);

public sealed record IntegrationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false);

public static class IntegrationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

