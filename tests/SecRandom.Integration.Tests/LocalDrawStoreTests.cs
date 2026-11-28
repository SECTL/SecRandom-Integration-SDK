using SecRandom.DrawKit;
using SecRandom.Integration;
using System.Text.Json;

namespace SecRandom.Integration.Tests;

public sealed class LocalDrawStoreTests
{
    [Fact]
    public void RosterHistoryAndSnapshot_RoundTripAndPreserveHistoryOnDelete()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sr-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new LocalDrawStore(Path.Combine(directory, "a.sqlite"));
            store.SaveRoster(new Roster("a", "Class A", RosterKind.Students, [new RosterMember("1", "Alice")]));
            store.SaveMember("a", new RosterMember("2", "Bob"));
            store.SaveMember("a", new RosterMember("2", "Bobby"));
            Assert.Equal("Bobby", store.FindRoster("a")!.Members[1].Name);
            var result = store.Draw(new DrawRequest("a", 1, DrawMode.Fair) { FairStrategy = FairDrawStrategy.RandomFirst });
            var page = store.QueryHistory(new HistoryQuery(RosterId: "a", Limit: 1));
            Assert.Equal(1, page.Total);
            Assert.Equal(FairDrawStrategy.RandomFirst, page.Entries.Single().FairStrategy);
            Assert.Equal(1, store.GetHistorySummary("a").Selections);
            var snapshot = JsonSerializer.Deserialize<LocalDrawSnapshot>(
                JsonSerializer.Serialize(store.Export(), IntegrationJson.Options), IntegrationJson.Options)!;
            var restored = new LocalDrawStore(Path.Combine(directory, "b.sqlite"));
            restored.Import(snapshot);
            Assert.Equal(result.RoundId, restored.QueryHistory().Entries.Single().RoundId);
            Assert.ThrowsAny<Exception>(() => restored.Import(snapshot));
            Assert.Equal(1, restored.QueryHistory().Total);
            var conflicting = snapshot with { Rosters = [new Roster("new", "New", RosterKind.Students, []), .. snapshot.Rosters] };
            Assert.ThrowsAny<Exception>(() => restored.Import(conflicting));
            Assert.Null(restored.FindRoster("new"));
            Assert.True(restored.DeleteMember("a", "2"));
            Assert.True(restored.DeleteRoster("a"));
            Assert.Equal(1, restored.QueryHistory().Total);
            Assert.True(restored.DeleteHistory(result.RoundId));
            Assert.Equal(0, restored.QueryHistory().Total);
            restored.AddHistory(snapshot.History.Single());
            Assert.Equal(1, restored.ClearHistory("a"));
        }
        finally { SqliteCleanup(directory); }
    }

    [Fact]
    public async Task ConcurrentDraws_FromSeparateInstancesCommitWithoutLostHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sr-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "draw.sqlite");
            var store = new LocalDrawStore(path);
            store.SaveRoster(new Roster("a", "A", RosterKind.Students,
                Enumerable.Range(0, 5).Select(i => new RosterMember(i.ToString(), $"Member {i}")).ToArray()));
            var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
                new LocalDrawStore(path).Draw(new DrawRequest("a", 1, DrawMode.Fair) { FairStrategy = FairDrawStrategy.RandomFirst })));
            var results = await Task.WhenAll(tasks);
            Assert.Equal(12, results.Select(result => result.RoundId).Distinct().Count());
            Assert.Equal(12, store.QueryHistory().Total);
            var counts = store.GetHistorySummary("a").MemberCounts.Values.ToArray();
            Assert.Equal(5, counts.Length);
            Assert.True(counts.Max() - counts.Min() <= 2);
        }
        finally { SqliteCleanup(directory); }
    }

    [Fact]
    public void FairStrategies_RespectLeadThresholdsAndRelaxForMultipleWinners()
    {
        var roster = new Roster("a", "A", RosterKind.Students,
            [new RosterMember("zero", "Zero"), new RosterMember("one", "One"), new RosterMember("two", "Two")]);
        var history = new[] { new HistoryEntry("h", "a", ["one", "two", "two"], DrawMode.Random, DateTimeOffset.UtcNow) };
        var engine = new DrawEngine();
        for (var i = 0; i < 30; i++)
        {
            var random = engine.Draw(roster, history, new DrawRequest("a", 1, DrawMode.Fair) { FairStrategy = FairDrawStrategy.RandomFirst });
            Assert.Equal("zero", random.Members.Single().Id);
            var uniform = engine.Draw(roster, history, new DrawRequest("a", 1, DrawMode.Fair));
            Assert.DoesNotContain(uniform.Members, member => member.Id == "two");
        }
        var multiple = engine.Draw(roster, history, new DrawRequest("a", 3, DrawMode.Fair) { FairStrategy = FairDrawStrategy.RandomFirst });
        Assert.Equal(3, multiple.Members.Select(member => member.Id).Distinct().Count());
    }

    [Fact]
    public void Draw_RejectsInvalidWeightsAndKeepsPrizeKind()
    {
        var engine = new DrawEngine();
        var roster = new Roster("a", "A", RosterKind.Prizes, [new RosterMember("1", "Prize", Weight: double.NaN)]);
        Assert.Throws<ArgumentException>(() => engine.Draw(roster, [], new DrawRequest("a", 1, DrawMode.Fair)));
        var valid = roster with { Members = [new RosterMember("1", "Prize")] };
        Assert.Equal(RosterKind.Prizes, engine.Draw(valid, [], new DrawRequest("a", 1, DrawMode.Random)).RosterKind);
    }

    private static void SqliteCleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }
}
