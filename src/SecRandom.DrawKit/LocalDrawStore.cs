using System.Text.Json;
using Microsoft.Data.Sqlite;
using SecRandom.Integration;

namespace SecRandom.DrawKit;

public sealed record HistoryQuery(string? RosterId = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, int Offset = 0, int Limit = 100);
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, long Total);
public sealed record HistorySummary(long Rounds, long Selections, IReadOnlyDictionary<string, int> MemberCounts);
public sealed record LocalDrawSnapshot(int Version, IReadOnlyList<Roster> Rosters, IReadOnlyList<HistoryEntry> History);

/// <summary>Transactional, application-owned roster and history database. It never accesses SecRandom data.</summary>
public sealed class LocalDrawStore
{
    private readonly string _connectionString;

    public LocalDrawStore(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 30 }.ToString();
        using var connection = Open();
        Execute(connection, null, "CREATE TABLE IF NOT EXISTS rosters (id TEXT PRIMARY KEY, data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS history (round_id TEXT PRIMARY KEY, roster_id TEXT NOT NULL, at TEXT NOT NULL, data TEXT NOT NULL); CREATE INDEX IF NOT EXISTS history_roster_time ON history(roster_id, at);");
    }

    public void SaveRoster(Roster roster)
    {
        ValidateRoster(roster);
        using var connection = Open();
        SaveRoster(connection, null, roster);
    }

    public Roster? FindRoster(string id)
    {
        using var connection = Open();
        return FindRoster(connection, null, id);
    }

    public IReadOnlyList<Roster> ListRosters()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM rosters ORDER BY id";
        using var reader = command.ExecuteReader();
        var items = new List<Roster>();
        while (reader.Read()) items.Add(Deserialize<Roster>(reader.GetString(0)));
        return items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void SaveMember(string rosterId, RosterMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var roster = FindRoster(connection, transaction, rosterId)
            ?? throw new KeyNotFoundException($"Roster '{rosterId}' was not found.");
        var members = roster.Members.ToList();
        var index = members.FindIndex(item => item.Id == member.Id);
        if (index < 0) members.Add(member); else members[index] = member;
        var updated = roster with { Members = members };
        ValidateRoster(updated);
        SaveRoster(connection, transaction, updated);
        transaction.Commit();
    }

    public bool DeleteMember(string rosterId, string memberId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var roster = FindRoster(connection, transaction, rosterId)
            ?? throw new KeyNotFoundException($"Roster '{rosterId}' was not found.");
        var members = roster.Members.Where(member => member.Id != memberId).ToArray();
        if (members.Length == roster.Members.Count) return false;
        SaveRoster(connection, transaction, roster with { Members = members });
        transaction.Commit();
        return true;
    }

    /// <summary>Deletes a roster; history is retained until explicitly deleted.</summary>
    public bool DeleteRoster(string id)
    {
        using var connection = Open();
        using var command = Command(connection, null, "DELETE FROM rosters WHERE id=$id", ("$id", id));
        return command.ExecuteNonQuery() != 0;
    }

    public DrawResult Draw(DrawRequest request, string? sourceAppId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var roster = FindRoster(connection, transaction, request.RosterId)
            ?? throw new KeyNotFoundException($"Roster '{request.RosterId}' was not found.");
        var history = ReadHistory(connection, transaction, request.RosterId);
        var result = new DrawEngine().Draw(roster, history, request, sourceAppId);
        AppendHistory(connection, transaction, new HistoryEntry(result.RoundId, result.RosterId,
            result.Members.Select(member => member.Id).ToArray(), request.Mode, result.At, sourceAppId)
            { FairStrategy = request.Mode == DrawMode.Fair ? request.FairStrategy : null });
        transaction.Commit();
        return result;
    }

    public HistoryPage QueryHistory(HistoryQuery? query = null)
    {
        query ??= new HistoryQuery();
        if (query.Offset < 0 || query.Limit is < 1 or > 1000 || query.From > query.To)
            throw new ArgumentOutOfRangeException(nameof(query));
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        const string where = " WHERE ($roster IS NULL OR roster_id=$roster) AND ($from IS NULL OR at >= $from) AND ($to IS NULL OR at <= $to)";
        var parameters = new (string, object?)[] { ("$roster", query.RosterId), ("$from", Format(query.From)), ("$to", Format(query.To)) };
        using var count = Command(connection, transaction, "SELECT COUNT(*) FROM history" + where, parameters);
        var total = (long)count.ExecuteScalar()!;
        using var command = Command(connection, transaction, "SELECT data FROM history" + where + " ORDER BY at DESC, round_id DESC LIMIT $limit OFFSET $offset", parameters);
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        using var reader = command.ExecuteReader();
        var items = new List<HistoryEntry>();
        while (reader.Read()) items.Add(Deserialize<HistoryEntry>(reader.GetString(0)));
        return new HistoryPage(items, total);
    }

    public void AddHistory(HistoryEntry entry)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        AppendHistory(connection, transaction, entry);
        transaction.Commit();
    }

    public bool DeleteHistory(string roundId)
    {
        using var connection = Open();
        using var command = Command(connection, null, "DELETE FROM history WHERE round_id=$id", ("$id", roundId));
        return command.ExecuteNonQuery() != 0;
    }

    public HistorySummary GetHistorySummary(string rosterId)
    {
        if (string.IsNullOrWhiteSpace(rosterId)) throw new ArgumentException("Roster id is required.", nameof(rosterId));
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var entries = ReadHistory(connection, transaction, rosterId);
        var counts = entries.SelectMany(entry => entry.MemberIds)
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new HistorySummary(entries.Count, counts.Values.Sum(value => (long)value), counts);
    }

    public int ClearHistory(string rosterId)
    {
        if (string.IsNullOrWhiteSpace(rosterId)) throw new ArgumentException("Roster id is required.", nameof(rosterId));
        using var connection = Open();
        using var command = Command(connection, null, "DELETE FROM history WHERE roster_id=$id", ("$id", rosterId));
        return command.ExecuteNonQuery();
    }

    public LocalDrawSnapshot Export()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, transaction, "SELECT data FROM rosters ORDER BY id");
        using var reader = command.ExecuteReader();
        var rosters = new List<Roster>();
        while (reader.Read()) rosters.Add(Deserialize<Roster>(reader.GetString(0)));
        reader.Close();
        return new LocalDrawSnapshot(1, rosters, ReadHistory(connection, transaction, null));
    }

    /// <summary>Imports a snapshot atomically. Existing roster and round IDs cause the import to fail.</summary>
    public void Import(LocalDrawSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1) throw new ArgumentException("Unsupported snapshot version.", nameof(snapshot));
        foreach (var roster in snapshot.Rosters) ValidateRoster(roster);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var roster in snapshot.Rosters)
        {
            using var command = Command(connection, transaction, "INSERT INTO rosters(id,data) VALUES($id,$data)", ("$id", roster.Id), ("$data", Serialize(roster)));
            command.ExecuteNonQuery();
        }
        foreach (var entry in snapshot.History) AppendHistory(connection, transaction, entry);
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void ValidateRoster(Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (string.IsNullOrWhiteSpace(roster.Id) || string.IsNullOrWhiteSpace(roster.Name) || !Enum.IsDefined(roster.Kind)
            || roster.Members is null || roster.Members.Any(member => member is null || string.IsNullOrWhiteSpace(member.Id)
                || string.IsNullOrWhiteSpace(member.Name) || !double.IsFinite(member.Weight) || member.Weight < 0)
            || roster.Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != roster.Members.Count)
            throw new ArgumentException("Invalid roster or duplicate member id.", nameof(roster));
    }

    private static void SaveRoster(SqliteConnection connection, SqliteTransaction? transaction, Roster roster)
    {
        using var command = Command(connection, transaction, "INSERT INTO rosters(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=excluded.data", ("$id", roster.Id), ("$data", Serialize(roster)));
        command.ExecuteNonQuery();
    }

    private static Roster? FindRoster(SqliteConnection connection, SqliteTransaction? transaction, string id)
    {
        using var command = Command(connection, transaction, "SELECT data FROM rosters WHERE id=$id", ("$id", id));
        return command.ExecuteScalar() is string data ? Deserialize<Roster>(data) : null;
    }

    private static List<HistoryEntry> ReadHistory(SqliteConnection connection, SqliteTransaction transaction, string? rosterId)
    {
        using var command = Command(connection, transaction, "SELECT data FROM history WHERE ($id IS NULL OR roster_id=$id)", ("$id", rosterId));
        using var reader = command.ExecuteReader();
        var entries = new List<HistoryEntry>();
        while (reader.Read()) entries.Add(Deserialize<HistoryEntry>(reader.GetString(0)));
        return entries;
    }

    private static void AppendHistory(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.RoundId) || string.IsNullOrWhiteSpace(entry.RosterId)
            || entry.MemberIds is null || entry.MemberIds.Any(string.IsNullOrWhiteSpace) || !Enum.IsDefined(entry.DrawMode)
            || (entry.FairStrategy is { } strategy && !Enum.IsDefined(strategy)))
            throw new ArgumentException("Invalid history entry.", nameof(entry));
        using var command = Command(connection, transaction, "INSERT INTO history(round_id,roster_id,at,data) VALUES($round,$roster,$at,$data)",
            ("$round", entry.RoundId), ("$roster", entry.RosterId), ("$at", Format(entry.At)), ("$data", Serialize(entry)));
        command.ExecuteNonQuery();
    }

    private static string? Format(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O");
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, IntegrationJson.Options);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, IntegrationJson.Options) ?? throw new InvalidDataException("Invalid stored data.");
    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
