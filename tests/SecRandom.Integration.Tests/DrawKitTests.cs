using SecRandom.DrawKit;
using SecRandom.Integration;

namespace SecRandom.Integration.Tests;

public sealed class DrawKitTests
{
    [Fact]
    public void ModeCatalog_OnlyExposesRandomAndFair()
    {
        Assert.Equal(new[] { DrawMode.Random, DrawMode.Fair }, Enum.GetValues<DrawMode>());
    }

    [Fact]
    public void Draw_ReturnsDistinctMembersAndHonorsRosterScope()
    {
        var roster = new Roster("class-a", "A", RosterKind.Students,
        [
            new RosterMember("01", "Alice", "F", "red"),
            new RosterMember("02", "Bob", "M", "red"),
            new RosterMember("03", "Casey", "F", "blue")
        ]);
        var unrelatedHistory = new HistoryEntry("old", "class-b", ["01", "01"], DrawMode.Fair, DateTimeOffset.UtcNow);

        var result = new DrawEngine().Draw(roster, [unrelatedHistory], new DrawRequest("class-a", 2, DrawMode.Fair, Group: "red"));

        Assert.Equal(2, result.Members.Count);
        Assert.Equal(2, result.Members.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.Members, x => Assert.Equal("red", x.Group));
    }

    [Fact]
    public void RosterStore_PreservesStableIdsAndRejectsDuplicateMemberKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new JsonRosterStore(directory);
        var roster = new Roster("primary", "Class A", RosterKind.Students, [new RosterMember("member-1", "A")]);
        store.Save(roster);

        Assert.Equal("member-1", store.Find("primary")!.Members[0].Id);
        Assert.Throws<ArgumentException>(() => store.Save(roster with
        {
            Members = [new RosterMember("same", "A"), new RosterMember("same", "B")]
        }));
        Directory.Delete(directory, true);
    }

    [Fact]
    public void HistoryStore_IsIdempotentByRoundId()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new JsonHistoryStore(Path.Combine(directory, "history.json"));
        var entry = new HistoryEntry("round-1", "primary", ["member-1"], DrawMode.Random, DateTimeOffset.UtcNow);

        Assert.True(store.Append(entry));
        Assert.False(store.Append(entry));
        Assert.Single(store.Read());
        Directory.Delete(directory, true);
    }
}

