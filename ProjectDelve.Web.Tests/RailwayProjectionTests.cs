using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class RailwayProjectionTests
{
    internal static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 6, Height = 3, Cells = Enumerable.Range(0, 6)
            .Select(x => new CellDefinition(new(x, 0), TerrainKind.Rail, RailDirection.East)).ToList() },
        UnitTypeIds = [UnitTypeIds.Locomotive, UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Locomotive, "rail", new(0, 0), UnitId: "train"),
            new(UnitTypeIds.Grunt, "blue", new(1, 0), UnitId: "struck"),
            new(UnitTypeIds.Grunt, "red", new(5, 2), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Locomotive, "rail", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        NeutralSides = ["rail"], Objectives = [new("rail", ScenarioObjective.NoObjective)],
        Resources = [new(ResourceTypes.Coal, new(0, 0))]
    };
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }

    [Fact]
    public void BrowserProjectsAutomaticRailStepsSwapPostureAndNoObjective()
    {
        var result = GameEngine.StartRound(GameEngine.CreateGame(Fixture()), new Dice(), false);
        var browser = BrowserProjection.Create(result);
        Assert.Equal("No Objective", browser.Objectives.Single(o => o.SideId == "rail").Name);
        Assert.Contains(browser.Cards["train"].Entries, e => e.Content.Name == "Run Rails (4)" && e.Content.Category == "Automatic");
        Assert.Contains(browser.Events, e => e.Text == "train swapped places with struck");
        Assert.Equal(3, browser.Events.Count(e => e.Role == OutcomeRole.Movement && e.Text.StartsWith("train ran Rails:")));
        var swap = result.Events.FindIndex(e => e.Kind == "PlacesSwapped");
        Assert.Equal(new(1, 0), browser.ResolutionSteps[swap].Figures["train"].Anchor);
        Assert.Equal(new(0, 0), browser.ResolutionSteps[swap].Figures["struck"].Anchor);
        var posture = result.Events.FindIndex(e => e.Kind == "PostureChanged");
        Assert.False(result.ResolutionSteps[posture].StateAfter.IsUpright("struck"));
        Assert.NotEqual("train", browser.Decision?.UnitId);
        Assert.All(result.State.Physical.Board.Terrain, tile => Assert.Equal(RailDirection.East, tile.Direction));
        Assert.All(result.ResolutionSteps, step => Assert.Equal(new(0, 0), Assert.Single(step.StateAfter.Resources).Position));
    }

    [Fact]
    public void GambitDistinguishesNeutralUnitsAndRetainsRailDirections()
    {
        var state = GameEngine.CreateGame(Fixture());
        var observation = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        Assert.Equal(GambitAgency.Neutral, observation.Units.Single(u => u.Id == "train").Agency);
        Assert.Equal(GambitAgency.Hostile, observation.Units.Single(u => u.Id == "enemy").Agency);
        Assert.All(observation.Board.Terrain, tile => Assert.Equal(RailDirection.East, tile.Direction));
        var json = JsonSerializer.Serialize(observation, GambitJson.Options);
        Assert.Contains("\"direction\":\"East\"", json);
    }
}
