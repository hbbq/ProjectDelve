using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class FootprintProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.RedDragon) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Fact]
    public void GeometryAndPlacementPreviewsAreAuthoritativeForUnfamiliarTypesAndSnapshots()
    {
        var type = UnitType.Define("unfamiliar", "Unfamiliar", UnitAuthoring.Stats(2, 4, 2, 1, 1), UnitAuthoring.Footprint2x2());
        var state = new GameState
        {
            Physical = new(new Board(8, 5, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "red")]
        };
        var started = TestGame.StartRound(state, new Dice(), false);
        var projected = BrowserProjection.Create(started);
        Assert.Equal(2, projected.Figures["actor"].CellSpan);
        Assert.Equal(new Cell(0, 0), projected.Figures["actor"].Anchor);
        Assert.Equal(new[] { new Cell(0, 0), new(1, 0), new(0, 1), new(1, 1) }, projected.Figures["actor"].OccupiedCells);
        var move = projected.Decision!.Candidates.Single(c => c.Key == "1,0");
        Assert.Equal(InteractionKind.Position, move.Interaction.Kind);
        Assert.Equal(new[] { new Cell(1, 0), new(2, 0), new(1, 1), new(2, 1) }, move.PlacementCells);
        Assert.All(projected.ResolutionSteps, step => Assert.Equal(4, step.Figures["actor"].OccupiedCells.Count));
        var result = TestGame.Advance(started.State, new Pick("1,0"), new Dice(), false);
        var moved = BrowserProjection.Create(result);
        Assert.Equal(new Cell(1, 0), moved.Figures["actor"].Anchor);
        Assert.Contains(moved.ResolutionSteps, step => step.Figures["actor"].Anchor == new Cell(1, 0));
        var serialized = JsonSerializer.Serialize(projected);
        Assert.Contains("OccupiedCells", serialized);
        Assert.Contains("PlacementCells", serialized);
    }

    [Fact]
    public void DragonFitsShamanHuntAndUsesOrdinaryMonsterQueries()
    {
        var state = PlaytestScenarios.Create("shaman-hunt");
        var dragonId = Assert.Single(state.Units, u => u.TypeId == UnitTypeIds.RedDragon).Id;
        Assert.Single(state.Physical.Figures.Where(f => f.Id == dragonId));
        Assert.Equal(4, FootprintGeometry.OccupiedCells(state, dragonId).Count);
        var start = TestGame.StartRound(state, new Dice(), false);
        Assert.Equal(dragonId, start.State.CurrentUnitId);
        Assert.Contains(start.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        var move = TestGame.Advance(start.State, new DefaultAutomatedProvider(), new Dice(), false);
        Assert.Contains(move.Events, e => e.Kind == "MovementCompleted" && e.UnitId == dragonId);
        Assert.Equal(4, BrowserProjection.Create(move).Figures[dragonId].OccupiedCells.Count);
        Assert.DoesNotContain(state.Units, u => u.TypeId == UnitTypeIds.Goblin);
    }

    [Fact]
    public void DeathSnapshotRemovesOneWholeProjectedFigure()
    {
        var state = PlaytestScenarios.Create("shaman-hunt");
        var dragonId = Assert.Single(state.Units, u => u.TypeId == UnitTypeIds.RedDragon).Id;
        var before = BrowserProjection.Figures(state);
        state.Physical.Figures.RemoveAll(f => f.Id == dragonId);
        var after = BrowserProjection.Figures(state);
        Assert.Equal(4, before[dragonId].OccupiedCells.Count);
        Assert.False(after.ContainsKey(dragonId));
        Assert.Equal(before.Count - 1, after.Count);
    }

    private sealed class Pick(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
}
