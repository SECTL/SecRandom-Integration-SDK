using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Tests;

public sealed class AutomationContractTests
{
    [Fact]
    public void DrawOptions_DefaultsToSecRandomSettings()
    {
        var options = new DrawOptions();
        Assert.False(options.HasDrawOverrides);
        Assert.Null(options.ToParameters());
    }

    [Fact]
    public void DrawOptions_OnlySerializesExplicitOverrides()
    {
        var parameters = JsonSerializer.SerializeToElement(
            new DrawOptions(Count: 2).ToParameters(), AutomationJson.Options);

        Assert.Equal(2, parameters.GetProperty("count").GetInt32());
        Assert.False(parameters.TryGetProperty("gender", out _));
        Assert.False(parameters.TryGetProperty("group", out _));
        Assert.False(parameters.TryGetProperty("list", out _));
    }

    [Fact]
    public void ChannelsMapToStableAutomationMethods()
    {
        Assert.Equal("draw.rollCall.trigger", DrawChannel.RollCall.Method());
        Assert.Equal("draw.quick.trigger", DrawChannel.Quick.Method());
        Assert.Equal("draw.lottery.trigger", DrawChannel.Lottery.Method());
    }

    [Fact]
    public void DrawOptions_SerializesLotteryConditionsUsingWireNames()
    {
        var options = new DrawOptions { Conditions = new LotteryDrawConditions(["gold"], "Class A", Group: "1") };
        var parameters = JsonSerializer.SerializeToElement(options.ToParameters(), AutomationJson.Options);
        var conditions = parameters.GetProperty("conditions");
        Assert.Equal("gold", conditions.GetProperty("prize_tags")[0].GetString());
        Assert.Equal("Class A", conditions.GetProperty("student_list").GetString());
        Assert.Equal("1", conditions.GetProperty("group").GetString());
        Assert.False(conditions.TryGetProperty("gender", out _));
    }

    [Fact]
    public void RosterRead_ExtractsDistinctSelectionOptions()
    {
        var snapshot = new RosterReadSnapshot("students", [
            new RosterSnapshotList("A", true, 2, 2, false, [
                new RosterSnapshotMember("1", "A", "F", "red"),
                new RosterSnapshotMember("2", "B", "F", "blue")
            ])
        ]);

        var options = snapshot.ToSelectionOptions();

        Assert.Equal(["blue", "red"], options.Groups);
        Assert.Equal(["F"], options.Genders);
    }
}
