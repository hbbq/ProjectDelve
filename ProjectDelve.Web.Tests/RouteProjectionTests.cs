using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class RouteProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 7, Height = 3, DefaultTerrain = TerrainKind.Asphalt },
        UnitTypeIds = [UnitTypeIds.DiamondKart],
        Units = [new(UnitTypeIds.DiamondKart, "blue", new(0, 1), UnitId: "kart")],
        Agency = [new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Orders = [new(UnitTypeIds.DiamondKart, "blue", OrderKind.FollowRoute,
            Route: new([new(2, 0, 1, 3), new(5, 0, 2, 3)], RouteMode.Loop))],
        StoredResources = [new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Diamond, 1)]
    };

    [Fact]
    public void TransportProjectionAndPlaybackCarrySharedRouteAndAtomicPlacement()
    {
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture()));
        var start = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        var moved = GameEngine.Advance(start.State, new Pick("2,1"), new Dice(), false);
        var view = BrowserProjection.Create(moved);
        Assert.Equal(1, Assert.Single(view.Orders).Route!.CurrentIndex);
        var index = moved.Events.ToList().FindIndex(e => e.Kind == "RouteAdvanced");
        Assert.Equal(0, moved.ResolutionSteps[index - 1].StateAfter.Orders[0].Route!.CurrentIndex);
        Assert.Equal(1, moved.ResolutionSteps[index].StateAfter.Orders[0].Route!.CurrentIndex);
        Assert.Equal(1, view.ResolutionSteps.Single(s => s.EventIndex == index).Orders[0].Route!.CurrentIndex);
        Assert.Contains(view.Events, e => e.Text.Contains("advanced the group's route"));
        var target = moved.NextInput!.Candidates.First(c => c.Action == UnitAction.PlaceBananaPeel);
        var choice = view.Decision!.Candidates.Single(c => c.Key == target.Key);
        Assert.Equal("place-banana-peel", choice.EntryId);
        Assert.Equal(InteractionKind.Position, choice.Interaction.Kind);
        Assert.Equal(target.Destination, choice.Interaction.Position);
        var placed = GameEngine.Advance(moved.State, new Pick(target.Key), new Dice(), false);
        view = BrowserProjection.Create(placed);
        Assert.Contains(view.Events, e => e.Text.Contains("placed Banana Peel"));
        Assert.Contains(view.Events, e => e.Text.Contains("for Place Banana Peel"));
        var spent = placed.Events.ToList().FindIndex(e => e.Kind == "ResourceSpent");
        Assert.Single(placed.ResolutionSteps[spent].StateAfter.CellMarkers);
        Assert.Equal(0, placed.ResolutionSteps[spent].StateAfter.StoredResources.Single().Quantity);
        var restored = JsonSerializer.Deserialize<EngineResult>(JsonSerializer.Serialize(placed))!;
        Assert.Equal(1, restored.State.Orders.Single().Route!.CurrentIndex);
        Assert.Single(GambitProjection.Observation(restored.State).CellMarkers);
    }
}
