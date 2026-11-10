using System.Collections;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class StateDetachmentTests
{
    internal static GameState World()
    {
        var type = new UnitType("test", 3, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new Board(8, 8, []), [new("actor", new(2, 2))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")],
            WorldEffects = new(1, 2) { Composition = new List<WorldCardQuantity> { new("test-world") } },
            WorldDeck = new(), LootDeck = new(),
            ExploredCells = new() { ["blue"] = [new(2, 2)] },
            ObjectiveStartUnitIds = new() { ["blue"] = ["actor"] },
            Pending = new(DecisionKind.Activation, "test", "actor",
                [new("step", new(3, 2), [new(2, 2), new(3, 2)])], false)
        };
    }

    [Fact]
    public void DetachedCopySeparatesEveryMutableStateContainerAndPendingPaths()
    {
        var state = World();
        var before = JsonSerializer.Serialize(state);
        var copy = state.DetachedCopy();
        foreach (var property in typeof(GameState).GetProperties().Where(p => p.CanWrite))
        {
            var original = property.GetValue(state);
            if (original is IList or IDictionary or HashSet<string>)
                Assert.NotSame(original, property.GetValue(copy));
        }
        Assert.NotSame(state.Physical.Figures, copy.Physical.Figures);
        Assert.NotSame(state.Physical.Board.Edges, copy.Physical.Board.Edges);
        Assert.NotSame(state.Physical.Board.Terrain, copy.Physical.Board.Terrain);
        Assert.NotSame(state.Physical.Board.Machines, copy.Physical.Board.Machines);
        Assert.NotSame(state.WorldDeck!.DrawPile, copy.WorldDeck!.DrawPile);
        Assert.NotSame(state.WorldDeck.DiscardPile, copy.WorldDeck.DiscardPile);
        Assert.NotSame(state.WorldDeck.ActiveContinuous, copy.WorldDeck.ActiveContinuous);
        Assert.NotSame(state.LootDeck!.DrawPile, copy.LootDeck!.DrawPile);
        Assert.NotSame(state.LootDeck.DiscardPile, copy.LootDeck.DiscardPile);
        Assert.NotSame(state.WorldEffects!.Composition, copy.WorldEffects!.Composition);
        copy.Pending!.Candidates[0].Path!.Clear();
        copy.Pending.Candidates.Clear();
        copy.ExploredCells["blue"].Clear();
        copy.Physical.Figures.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void RawCopyDoesNotEvaluateStatsOrResolveContent()
    {
        var state = World();
        state.Types.Clear(); // A raw copy must work even before content is resolved.
        var copy = state.DetachedCopy();
        Assert.Empty(copy.Types);
        Assert.Single(copy.Units);
        Assert.Single(copy.Pending!.Candidates);
    }
}
