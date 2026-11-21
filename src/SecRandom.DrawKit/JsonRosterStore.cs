using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.DrawKit;

public sealed class JsonRosterStore
{
    private readonly string _directory;
    private readonly object _gate = new();

    public JsonRosterStore(string directory) => _directory = Path.GetFullPath(directory);

    public void Save(Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (string.IsNullOrWhiteSpace(roster.Id) || string.IsNullOrWhiteSpace(roster.Name))
            throw new ArgumentException("A roster needs a stable id and a display name.", nameof(roster));
        if (roster.Members.Any(x => string.IsNullOrWhiteSpace(x.Id)))
            throw new ArgumentException("Every roster member needs a stable id.", nameof(roster));
        if (roster.Members.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != roster.Members.Count)
            throw new ArgumentException("Roster member ids must be unique.", nameof(roster));

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath(roster.Id);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(roster, IntegrationJson.Options));
            File.Move(temp, path, true);
        }
    }

    public Roster? Find(string rosterId)
    {
        lock (_gate)
        {
            var path = GetPath(rosterId);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<Roster>(File.ReadAllText(path), IntegrationJson.Options);
        }
    }

    public IReadOnlyList<Roster> List()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
                return [];
            return Directory.EnumerateFiles(_directory, "*.json")
                .Select(path => JsonSerializer.Deserialize<Roster>(File.ReadAllText(path), IntegrationJson.Options))
                .Where(roster => roster is not null)
                .Cast<Roster>()
                .OrderBy(roster => roster.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    private string GetPath(string id)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return Path.Combine(_directory, safe + ".json");
    }
}

