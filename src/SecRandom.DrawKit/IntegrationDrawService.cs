using SecRandom.Integration;

namespace SecRandom.DrawKit;

public sealed class IntegrationDrawService(JsonRosterStore rosters, JsonHistoryStore history, DrawEngine engine)
{
    public DrawResult Draw(DrawRequest request, string? sourceAppId = null)
    {
        var roster = rosters.Find(request.RosterId)
                     ?? throw new KeyNotFoundException($"Roster '{request.RosterId}' was not found.");
        var priorHistory = history.Read();
        var result = engine.Draw(roster, priorHistory, request, sourceAppId);
        var saved = history.Append(new HistoryEntry(result.RoundId, result.RosterId,
            result.Members.Select(member => member.Id).ToArray(), result.Mode ?? DrawMode.Random, result.At, sourceAppId)
            { FairStrategy = request.Mode == DrawMode.Fair ? request.FairStrategy : null });
        if (!saved)
            throw new InvalidOperationException("The draw round was already committed.");
        return result;
    }
}

