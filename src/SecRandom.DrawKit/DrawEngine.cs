using System.Security.Cryptography;
using SecRandom.Integration;

namespace SecRandom.DrawKit;

public sealed class DrawEngine
{
    public DrawResult Draw(Roster roster, IReadOnlyCollection<HistoryEntry> history, DrawRequest request,
        string? sourceAppId = null)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(roster.Id, request.RosterId, StringComparison.Ordinal))
            throw new ArgumentException("The request roster does not match the supplied roster.", nameof(request));
        if (request.Count is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(request.Count));

        var candidates = roster.Members
            .Where(x => x.Enabled)
            .Where(x => request.Gender is null || string.Equals(x.Gender, request.Gender, StringComparison.OrdinalIgnoreCase))
            .Where(x => request.Group is null || string.Equals(x.Group, request.Group, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (request.Count > candidates.Count)
            throw new InvalidOperationException("Not enough enabled candidates.");

        var counts = history
            .Where(x => string.Equals(x.RosterId, roster.Id, StringComparison.Ordinal))
            .SelectMany(x => x.MemberIds)
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var selected = request.Mode == DrawMode.Fair
            ? WeightedSample(candidates, request.Count, counts)
            : UniformSample(candidates, request.Count);

        return new DrawResult(
            Guid.NewGuid().ToString("N"), roster.Id, request.Mode, selected,
            DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourceAppId);
    }

    private static IReadOnlyList<RosterMember> UniformSample(List<RosterMember> candidates, int count)
    {
        var pool = candidates.ToList();
        var result = new List<RosterMember>(count);
        for (var i = 0; i < count; i++)
        {
            var index = RandomNumberGenerator.GetInt32(pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return result;
    }

    private static IReadOnlyList<RosterMember> WeightedSample(
        List<RosterMember> candidates, int count, IReadOnlyDictionary<string, int> historyCounts)
    {
        var pool = candidates.ToList();
        var result = new List<RosterMember>(count);
        for (var i = 0; i < count; i++)
        {
            var weights = pool.Select(member =>
                Math.Max(0, member.Weight) / (1 + historyCounts.GetValueOrDefault(member.Id))).ToArray();
            var total = weights.Sum();
            if (total <= 0)
                throw new InvalidOperationException("Fair draw has no candidates with positive weight.");
            var sample = RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue * total;
            var selected = 0;
            for (var j = 0; j < weights.Length; j++)
            {
                sample -= weights[j];
                if (sample <= 0)
                {
                    selected = j;
                    break;
                }
            }
            result.Add(pool[selected]);
            pool.RemoveAt(selected);
        }
        return result;
    }
}

