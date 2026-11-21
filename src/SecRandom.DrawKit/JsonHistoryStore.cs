using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.DrawKit;

public sealed class JsonHistoryStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public JsonHistoryStore(string path) => _path = Path.GetFullPath(path);

    public IReadOnlyList<HistoryEntry> Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return [];
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, IntegrationJson.Options) ?? [];
        }
    }

    public bool Append(HistoryEntry entry)
    {
        lock (_gate)
        {
            var entries = Read().ToList();
            if (entries.Any(x => string.Equals(x.RoundId, entry.RoundId, StringComparison.Ordinal)))
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries.Append(entry), IntegrationJson.Options));
            File.Move(temp, _path, true);
            return true;
        }
    }
}

