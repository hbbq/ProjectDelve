using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class AlertIntegrationTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(t => t.SideId == "blue") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttackAlertPresentationOnlyIncludesObservableWokenUnits(bool fog)
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 16, Height = 5,
                Edges = Enumerable.Range(0, 5).Select(y => new EdgeDefinition(new(5, y), EdgeDirection.Right, EdgeKind.Wall)).ToList() },
            UnitTypeIds = ["hero", "goblin"],
            UnitTypes = [new("hero", 1, 4, 1, 0, 1), UnitRoster.Goblin("goblin")],
            Units = [new("hero", "blue", new(1, 2), UnitId: "hero", WakeDistance: 0),
                new("goblin", "red", new(4, 2), UnitId: "target", GroupId: "camp", Dormant: true, WakeDistance: 0),
                new("goblin", "red", new(7, 2), UnitId: "hidden-relay", GroupId: "camp", WakeDistance: 0),
                new("goblin", "red", new(10, 2), UnitId: "hidden-sleeper", GroupId: "camp", Dormant: true, WakeDistance: 0)],
            Agency = [new("hero", "blue", ControllerKind.Human), new("goblin", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            FogOfWarSides = fog ? ["blue"] : [], Loot = LootSettings.Empty
        };
        // DELVE1 and save state use the existing models: Alert adds no serialized fields.
        definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var dice = new Dice();
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        result = GameEngine.Advance(result.State, new Pick("stay"), dice, false);
        result = GameEngine.Advance(result.State, new Pick("attack:target"), dice, false);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "UnitWoken"));
        Assert.False(result.State.Units.Single(u => u.Id == "hidden-sleeper").Dormant);
        var observed = BrowserProjection.Observe(result, "blue");
        var serialized = JsonSerializer.Serialize(observed);
        Assert.Contains("target", serialized);
        if (fog)
        {
            Assert.DoesNotContain("hidden-relay", serialized);
            Assert.DoesNotContain("hidden-sleeper", serialized);
            Assert.Single(observed.Events.Where(e => e.Kind == "UnitWoken"));
        }
        else Assert.Equal(2, observed.Events.Count(e => e.Kind == "UnitWoken"));
        Assert.All(observed.Events.Where(e => e.Kind == "UnitWoken"), e => Assert.Null(e.SourceUnitId));
        Assert.DoesNotContain(result.Events, e => e.Kind == "Alert");
    }
}
