using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DefeatTests
{
    [Theory]
    [InlineData(true, Posture.Upright, true)]
    [InlineData(true, Posture.Lying, false)]
    [InlineData(false, Posture.Upright, false)]
    public void DirectDefeatAtPositiveHpChecksReplacementAndRetainsSourceFootprint(
        bool undying, Posture posture, bool replaced)
    {
        var type = UnitType.RedDragon() with { Undying = undying ? new() : null };
        var unit = type.CreateUnit("source", "red") with { CurrentHp = 4 };
        var figure = new Figure(unit.Id, new(1, 2), posture);
        var state = new GameState
        {
            Physical = new(new Board(5, 5, []), [figure]), Types = [type], Units = [unit]
        };

        // A direct lifecycle instruction, rather than Damage reaching zero.
        var outcome = GameEngine.ResolveDefeat(state, unit.Id, "other", "direct-defeat");
        Assert.Equal("other", outcome.SourceUnitId);
        Assert.Equal("direct-defeat", outcome.ActionId);
        if (replaced)
        {
            Assert.Equal("PostureChanged", outcome.Kind);
            Assert.Null(outcome.DefeatContext);
            Assert.Equal(1, state.Units[0].CurrentHp);
            Assert.Equal(figure with { Posture = Posture.Lying }, Assert.Single(state.Physical.Figures));
        }
        else
        {
            Assert.Equal("UnitDefeated", outcome.Kind);
            Assert.Empty(state.Physical.Figures);
            Assert.Equal(0, state.Units[0].CurrentHp);
            var retained = JsonSerializer.Deserialize<RulesEvent>(JsonSerializer.Serialize(outcome))!.DefeatContext!;
            Assert.Equal(unit, retained.Unit);
            Assert.Equal(type.Id, retained.Type.Id);
            Assert.Equal(figure, retained.Figure);
            Assert.Equal(new[] { new Cell(1, 2), new(2, 2), new(1, 3), new(2, 3) }, retained.OccupiedCells);
            state.Units.Clear();
            state.Types.Clear();
            Assert.Equal(4, retained.Unit.CurrentHp);
            Assert.Equal(Footprint.TwoByTwo, retained.Type.Footprint);
        }
    }
}
