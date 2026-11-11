using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BreakAdjacentEdgeTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Count;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("No Attack dice expected.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("No Defence dice expected.");
        public int RollD6() => ++Count % 2 == 1 ? 1 : 6;
    }
    private sealed class Choice : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) =>
            request.Candidates.Single(c => c.Ability?.Definition.Key == "break").Key;
    }

    private static GameState State(Cell anchor, Footprint footprint = Footprint.OneByOne,
        int threshold = 6, EdgeKind kind = EdgeKind.OpenDoor)
    {
        var type = new UnitType("breaker", 0, 0, 0, 0, 1)
        { Footprint = footprint, LocalAbilities = [new("break", "Break", AbilityTiming.Action, new BreakAdjacentObjects(threshold))] };
        var board = new Board(6, 6, []);
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            if (x + 1 < board.Width) board.Edges.Add(new(new(x, y), new(x + 1, y), kind));
            if (y + 1 < board.Height) board.Edges.Add(new(new(x, y), new(x, y + 1), kind));
        }
        return new()
        {
            Physical = new(board, [new("actor", anchor)]), Types = [type], Units = [type.CreateUnit("actor", "blue")],
            Round = 1, ActiveToken = new(type.Id, "blue"), CurrentUnitId = "actor", MoveDone = true,
            Pending = new(DecisionKind.Activation, type.Id, "actor", [], false)
        };
    }

    [Theory]
    [InlineData(2, 2, Footprint.OneByOne, 1, 1, 3, 3, 12, 8)]
    [InlineData(0, 2, Footprint.OneByOne, 0, 1, 2, 3, 7, 5)]
    [InlineData(0, 0, Footprint.OneByOne, 0, 0, 2, 2, 4, 3)]
    [InlineData(2, 2, Footprint.TwoByTwo, 1, 1, 4, 4, 24, 12)]
    [InlineData(0, 0, Footprint.TwoByTwo, 0, 0, 3, 3, 12, 5)]
    public void CompleteClippedAreaIncludesExactlyItsInternalEdgesAndNeighboringTerrain(
        int x, int y, Footprint footprint, int left, int top, int width, int height, int edgeCount, int terrainCount)
    {
        var state = State(new(x, y), footprint);
        var source = FootprintGeometry.OccupiedCells(state, "actor");
        var area = Enumerable.Range(top, height).SelectMany(row => Enumerable.Range(left, width).Select(col => new Cell(col, row))).ToHashSet();
        // A nonblocking destructible assembly permits LOS throughout the area.
        state.Physical.Board.Terrain.AddRange(area.Select(c => new TerrainTile(c) { TypeId = "campfire" }));
        var targets = BreakObjectRules.Targets(state, source, area: true);
        var cells = targets.Where(t => t.Cell is not null).Select(t => t.Cell!).ToArray();
        var edges = targets.Where(t => t.Edge is not null).Select(t => t.Edge!).ToArray();
        Assert.Equal(terrainCount, cells.Length);
        Assert.Equal(area.Except(source).OrderBy(c => c.Y).ThenBy(c => c.X), cells);
        Assert.Equal(edgeCount, edges.Length);
        var expected = state.Physical.Board.Edges.Where(e => area.Contains(e.A) && area.Contains(e.B))
            .OrderBy(e => e.A.Y).ThenBy(e => e.A.X).ThenBy(e => e.B.Y).ThenBy(e => e.B.X).ToArray();
        Assert.Equal(expected, edges);
        Assert.DoesNotContain(edges, e => !area.Contains(e.A) || !area.Contains(e.B));
        Assert.Contains(edges, e => !source.Contains(e.A) && !source.Contains(e.B));
        if (footprint == Footprint.OneByOne && x == 2 && y == 2)
        {
            var perimeter = state.Physical.Board.Edges.Where(e => area.Contains(e.A) != area.Contains(e.B)).ToArray();
            Assert.Equal(12, perimeter.Length);
            Assert.DoesNotContain(edges, perimeter.Contains);
            Assert.Equal(4, BreakObjectRules.Targets(state, source).Count(t => t.Edge is not null));
        }
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.WallWithWindow)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.OpenDoor)]
    public void GuaranteedBatchRemovesAllTwelveLegalStructuresAndPreservesPerimeter(EdgeKind kind)
    {
        var state = State(new(2, 2), kind: kind);
        var before = state.Physical.Board.Edges.ToArray();
        var dice = new Dice();
        var result = TestGame.Advance(state, new Choice(), dice, false);
        var resolved = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
        Assert.Equal(12, resolved.BreakOutcomes.Length);
        Assert.All(resolved.BreakOutcomes, o => { Assert.True(o.Succeeded); Assert.Null(o.DieRoll); Assert.Equal(EdgeKind.None, o.AfterEdgeKind); });
        Assert.Equal(0, dice.Count);
        Assert.DoesNotContain(result.Events, e => e.Kind == "DiceRolled");
        Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == resolved.Kind);
        var removed = resolved.BreakOutcomes.Select(o => o.Target.Edge!).ToHashSet();
        Assert.Equal(before.Where(e => !removed.Contains(e)), result.State.Physical.Board.Edges);
        Assert.Equal(before, state.Physical.Board.Edges);
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void SharedEdgeIdentityAndRollOrderIgnoreListOrderAndReversedEndpoints()
    {
        string? expected = null;
        foreach (var reverse in new[] { false, true })
        {
            var state = State(new(2, 2), threshold: 3);
            if (reverse)
            {
                state.Physical.Board.Edges.Reverse();
                for (var i = 0; i < state.Physical.Board.Edges.Count; i++)
                {
                    var edge = state.Physical.Board.Edges[i];
                    state.Physical.Board.Edges[i] = edge with { A = edge.B, B = edge.A };
                }
            }
            var source = FootprintGeometry.OccupiedCells(state, "actor");
            var targets = BreakObjectRules.Targets(state, source.Concat(source).ToArray(), area: true);
            Assert.Equal(12, targets.Length);
            Assert.Equal(12, targets.Select(t => t.Key).Distinct().Count());
            var dice = new Dice();
            var result = TestGame.Advance(state, new Choice(), dice, false);
            var outcomes = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved").BreakOutcomes;
            Assert.Equal(targets, outcomes.Select(o => o.Target));
            Assert.Equal(12, dice.Count);
            Assert.Equal(6, outcomes.Count(o => o.Succeeded));
            var serialized = JsonSerializer.Serialize(outcomes);
            if (expected is not null) Assert.Equal(expected, serialized);
            expected = serialized;
            GameEngine.ValidateScenario(result.State);
        }
    }

    [Fact]
    public void DuplicateSharedEdgeRepresentationsProduceOneCanonicalTarget()
    {
        var state = State(new(2, 2));
        var edge = new Edge(new(1, 1), new(2, 1), EdgeKind.OpenDoor);
        // Query robustness: authored Boards ordinarily store each shared Edge once.
        state.Physical.Board.Edges.Add(edge);
        state.Physical.Board.Edges.Add(edge with { A = edge.B, B = edge.A });
        var targets = BreakObjectRules.Targets(state, [new(2, 2)], area: true);
        Assert.Equal(12, targets.Length);
        Assert.Single(targets, t => t.Edge == edge);
    }

    [Fact]
    public void AreaStillRejectsInvalidEdgesAndDoesNotExposeBlockedTerrainRecipients()
    {
        var state = State(new(2, 2), kind: EdgeKind.Wall);
        state.Physical.Board.Terrain.Add(new(new(3, 2), TerrainKind.SolidRock));
        // Invalid board entries are query-only probes, not authored gameplay state.
        state.Physical.Board.Edges.AddRange([new(new(1, 1), new(2, 2), EdgeKind.Wall),
            new(new(-1, 2), new(0, 2), EdgeKind.Wall), new(new(1, 1), new(1, 1), EdgeKind.Wall)]);
        var targets = BreakObjectRules.Targets(state, [new(2, 2)], area: true);
        Assert.Equal(12, targets.Length);
        Assert.All(targets, t => Assert.NotNull(t.Edge));
        Assert.DoesNotContain(targets, t => t.Cell == new Cell(3, 2));
        state.Physical.Board.Edges.Clear();
        Assert.Equal(new Cell(3, 2), Assert.Single(BreakObjectRules.Targets(state, [new(2, 2)], area: true)).Cell);
    }
}
