using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class CellMarkerProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 6, Height = 3 },
        UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0), UnitId: "actor")],
        Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 0))]
    };

    [Fact]
    public void DesignerTransportAndBrowserPlaybackPreserveAuthoritativeMarkerSnapshotsAndEvents()
    {
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture()));
        var start = GameEngine.StartRound(GameEngine.CreateGame(decoded), new Dice(), false);
        var view = BrowserProjection.Create(start);
        Assert.Contains(view.Decision!.Candidates, c => c.Interaction.Position == new Cell(2, 0));
        Assert.DoesNotContain(view.Decision.Candidates, c => c.Interaction.Position == new Cell(3, 0));
        var observation = GambitProjection.Observation(start.State);
        Assert.Single(observation.CellMarkers);
        var result = GameEngine.Advance(start.State, new Choice("2,0"), new Dice(), false);
        view = BrowserProjection.Create(result);
        var consumed = Assert.Single(result.Events, e => e.Kind == "CellMarkerConsumed");
        Assert.Equal(CellMarkerTypes.BananaPeel, GambitProjection.Event(consumed).CellMarkerType);
        Assert.Contains(view.Events, e => e.Text.Contains("consumed Banana Peel at 2,0"));
        Assert.Single(result.ResolutionSteps[0].StateAfter.CellMarkers);
        Assert.Empty(result.ResolutionSteps[1].StateAfter.CellMarkers);
        Assert.Equal(new Cell(2, 0), view.ResolutionSteps[0].Figures["actor"].Anchor);
        Assert.Single(observation.CellMarkers);
        Assert.Empty(GambitProjection.Observation(result.State).CellMarkers);
        var restored = JsonSerializer.Deserialize<EngineResult>(JsonSerializer.Serialize(result))!;
        Assert.Single(restored.ResolutionSteps[0].StateAfter.CellMarkers);
        Assert.Empty(restored.State.CellMarkers);
    }

}
