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
        if (!Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.FairStrategy))
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported draw mode or fair strategy.");
        if (roster.Members.Any(member => !double.IsFinite(member.Weight) || member.Weight < 0))
            throw new ArgumentException("Member weights must be finite and non-negative.", nameof(roster));

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
            ? WeightedSample(candidates, request.Count, counts, request.FairStrategy)
            : UniformSample(candidates, request.Count);

        return new DrawResult(
            Guid.NewGuid().ToString("N"), roster.Id, request.Mode, selected,
            DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourceAppId, roster.Kind);
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
        List<RosterMember> candidates, int count, IReadOnlyDictionary<string, int> historyCounts, FairDrawStrategy strategy)
    {
        var pool = candidates.Where(member => member.Weight > 0).ToList();
        if (pool.Count < count)
            throw new InvalidOperationException("Not enough candidates with positive weight.");
        int DrawCount(RosterMember member) => historyCounts.GetValueOrDefault(member.Id);
        if (strategy == FairDrawStrategy.UniformFirst)
        {
            var minimum = pool.Min(DrawCount);
            var maximum = pool.Max(DrawCount);
            var limit = pool.Average(member => DrawCount(member));
            if (maximum - minimum > 1) limit = Math.Min(limit, minimum + 1);
            var eligible = pool.Where(member => DrawCount(member) <= limit).ToList();
            foreach (var tier in pool.Except(eligible).GroupBy(DrawCount).OrderBy(group => group.Key))
            {
                if (eligible.Count >= count) break;
                eligible.AddRange(tier);
            }
            pool = eligible;
        }
        var min = pool.Min(DrawCount);
        var ceiling = min + (min == 0 ? 1 : 2);
        var required = strategy == FairDrawStrategy.UniformFirst ? pool.Count : count;
        if (pool.Count(member => DrawCount(member) < ceiling) < required)
            ceiling = Math.Max(ceiling, pool.Select(DrawCount).Order().ElementAt(required - 1) + 1);
        var weights = pool.Select(member => Math.Max(ceiling - DrawCount(member), 0.0) * member.Weight).ToList();
        if (weights.Count(weight => weight > 0) < count || weights.Any(weight => !double.IsFinite(weight)))
            throw new InvalidOperationException("Fair draw has insufficient or invalid positive weights.");
        var result = new List<RosterMember>(count);
        for (var i = 0; i < count; i++)
        {
            var total = weights.Sum();
            if (!double.IsFinite(total) || total <= 0) throw new InvalidOperationException("Fair draw has invalid total weight.");
            var sample = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue * total;
            var selected = weights.FindLastIndex(weight => weight > 0);
            for (var j = 0; j < weights.Count; j++)
            {
                sample -= weights[j];
                if (sample < 0 && weights[j] > 0)
                {
                    selected = j;
                    break;
                }
            }
            result.Add(pool[selected]);
            pool.RemoveAt(selected);
            weights.RemoveAt(selected);
        }
        return result;
    }
}

