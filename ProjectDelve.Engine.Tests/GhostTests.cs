using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class GhostTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Phase does not open Doors.");
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int width = 5, int height = 1, Cell? start = null, Cell? target = null) => new()
    {
        Physical = new(new Board(width, height, []),
            [new("ghost", start ?? new(0, 0)), new("hero", target ?? new(width - 1, 0))]),
        Types = [UnitRoster.Ghost(), UnitRoster.Barbarian()],
        Units = [UnitRoster.Ghost().CreateUnit("ghost", "red"), UnitRoster.Barbarian().CreateUnit("hero", "blue")]
    };

    private static IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> Paths(GameState state) =>
        MovementRules.FindPaths(state, "ghost", state.Physical.Figures.Single(f => f.Id == "ghost").Position, 2);

    private static DecisionRequest Moves(GameState state) => new(DecisionKind.Move, UnitTypeIds.Ghost, "ghost",
        Paths(state).Where(p => p.Value.Count > 1).Select(p =>
            new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);

    [Fact]
    public void PhaseIsIntrinsicSerializableContentIndependentOfIdentity()
    {
        var ghost = UnitRoster.Ghost();
        Assert.Equal((2, 1, 3, 3, 1), (ghost.Mov, ghost.Rng, ghost.Atk, ghost.Def, ghost.Hp));
        Assert.Equal(UnitAction.NormalAttack, ghost.Actions);
        Assert.Equal(UnitBehavior.None, ghost.Behaviors);
        Assert.Equal(UnitTypeIds.Ghost, ghost.Id);
        Assert.Empty(ghost.BonusActions);
        var capability = Assert.Single(ghost.CardEntries(), e => e.Id == "phase");
        Assert.Equal("Capability", capability.Category);
        Assert.Null(capability.MaxUses);
        Assert.Contains("normally passable", capability.Description);
        Assert.Equal("Move through all terrain and Edges, including Walls and Closed Doors, without opening them.\nEnd on an unoccupied, normally passable Cell.", capability.Description);
        var state = State();
        state.Types[0] = UnitType.Define("other-type", "Other phasing content", Stats(2, 1, 3, 3, 1), Phase());
        state.Units[0] = state.Types[0].CreateUnit("ghost", "red");
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.NotNull(restored.Types[0].Phase);
        Assert.Contains(new Cell(2, 0), Paths(restored).Keys);
        Assert.Throws<ArgumentException>(() => UnitType.Define("duplicate", "Duplicate", Stats(2, 1, 3, 3, 1), Phase(), Phase()));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void MoveTraversesImpassableEdgesWithoutChangingThem(EdgeKind kind)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), kind));
        Assert.Equal(new Cell[] { new(0, 0), new(1, 0), new(2, 0) }, Paths(state)[new(2, 0)]);
        var pending = TestGame.StartRound(state, new Dice(), false);
        var moved = TestGame.Advance(pending.State, new Choice("2,0"), new Dice(), false);
        Assert.Equal(new Cell(2, 0), moved.State.Physical.Figures.Single(f => f.Id == "ghost").Position);
        Assert.Equal(kind, Assert.Single(moved.State.Physical.Board.Edges).Kind);
        Assert.DoesNotContain(TestGame.OperationEvents(moved), e => e.Kind is "DoorOpened" or "DoorOpeningAttemptResolved");
        state.Types[0] = UnitRoster.Grunt(UnitTypeIds.Ghost);
        Assert.DoesNotContain(new Cell(1, 0), Paths(state).Keys);
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("ghost", new(0, 0)));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.Water)]
    [InlineData(TerrainKind.StoneFloorWithTable)]
    public void ImpassableTerrainCanBeTraversedButNeverChosenAsDestination(TerrainKind kind)
    {
        var state = State();
        state.Physical.Board.Terrain.Add(new(new(1, 0), kind));
        Assert.Equal(new Cell[] { new(0, 0), new(1, 0), new(2, 0) }, Paths(state)[new(2, 0)]);
        Assert.DoesNotContain(new Cell(1, 0), Paths(state).Keys);
        var pending = TestGame.StartRound(state, new Dice(), false);
        Assert.DoesNotContain(pending.NextInput!.Candidates, c => c.Destination == new Cell(1, 0));
        var moved = TestGame.Advance(pending.State, new Choice("2,0"), new Dice(), false);
        Assert.Equal(new Cell(2, 0), moved.State.Physical.Figures.Single(f => f.Id == "ghost").Position);
        state.Types[0] = UnitRoster.Grunt(UnitTypeIds.Ghost);
        Assert.DoesNotContain(new Cell(2, 0), Paths(state).Keys);
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("ghost", new(0, 0)));
    }

    [Theory]
    [InlineData("red", true)]
    [InlineData("blue", false)]
    public void UnitOccupancyRemainsNormal(string side, bool canPass)
    {
        var state = State();
        state.Units.Add(UnitRoster.Barbarian().CreateUnit("occupant", side));
        state.Physical.Figures.Add(new("occupant", new(1, 0)));
        Assert.DoesNotContain(new Cell(1, 0), Paths(state).Keys);
        Assert.Equal(canPass, Paths(state).ContainsKey(new(2, 0)));
    }

    [Fact]
    public void DefaultBehaviorDiscoversAttackPositionAcrossWallAndTerrain()
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        var queries = new GameplayQueries(state);
        Assert.Equal(3, queries.DistanceToAttackPositionFrom("ghost", new(0, 0)));
        Assert.Equal(4, queries.DistanceToNearestHostileFrom("ghost", new(0, 0)));
        Assert.Equal("2,0", new DefaultAutomatedProvider().Choose(Moves(state), queries));
        state.Physical.Figures[1] = new("hero", new(3, 0));
        Assert.Equal("2,0", new DefaultAutomatedProvider().Choose(Moves(state), new GameplayQueries(state)));
        var pending = TestGame.StartRound(state, new Dice(), false);
        var moved = TestGame.Advance(pending.State, new DefaultAutomatedProvider(), new Dice(), false);
        Assert.Equal(new Cell(2, 0), moved.State.Physical.Figures.Single(f => f.Id == "ghost").Position);
        Assert.Contains(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack && c.TargetId == "hero");
    }

    [Fact]
    public void ApproachMovesAwayFromEnemyTowardLegalAttackPositionWithTopLeftTieBreak()
    {
        var state = State(7, 5, new(2, 2), new(4, 2));
        // Only the east side permits LOS. Water on the west can be crossed but not stopped on.
        foreach (var y in new[] { 1, 2, 3 })
            state.Physical.Board.Terrain.Add(new(new(3, y), TerrainKind.Water));
        state.Physical.Board.Terrain.Add(new(new(5, 2), TerrainKind.Water));
        foreach (var cell in new Cell[] { new(3, 2), new(4, 1), new(4, 3) })
            state.Physical.Board.Edges.Add(new(cell, new(4, 2), EdgeKind.Wall));
        var queries = new GameplayQueries(state);
        Assert.False(queries.CanAttackHostileFrom("ghost", new(4, 1)));
        Assert.True(queries.CanAttackHostileFrom("ghost", new(5, 1)));
        Assert.Equal(4, queries.DistanceToAttackPositionFrom("ghost", new(2, 2)));
        Assert.Equal(3, queries.DistanceToAttackPositionFrom("ghost", new(2, 1)));
        var request = Moves(state);
        var choice = new DefaultAutomatedProvider().Choose(request, queries);
        Assert.Equal("2,1", choice);
        var destination = request.Candidates.Single(c => c.Key == choice).Destination!;
        var current = queries.PositionOf("ghost");
        var hostile = queries.PositionOf("hero");
        Assert.True(Math.Abs(destination.X - hostile.X) + Math.Abs(destination.Y - hostile.Y) >
            Math.Abs(current.X - hostile.X) + Math.Abs(current.Y - hostile.Y));
    }

    [Fact]
    public void ImpassableCellsAreNotFutureAttackPositions()
    {
        var state = State(3);
        state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Water));
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("ghost", new(0, 0)));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    public void MeleeCannotAttackThroughBlockingEdges(EdgeKind kind)
    {
        var state = State(3, target: new(1, 0));
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), kind));
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "ghost", new(0, 0), "hero"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PhaseDoesNotChangeTerrainOrHostileUnitLos(bool terrain)
    {
        var state = State();
        state.Types[0] = state.Types[0] with { Rng = 4 };
        if (terrain) state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        else
        {
            state.Units.Add(UnitRoster.Barbarian().CreateUnit("blocker", "blue"));
            state.Physical.Figures.Add(new("blocker", new(1, 0), Posture.Lying));
        }
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "ghost", new(0, 0), "hero"));
    }

    [Fact]
    public void CanonicalPathStillUsesTopLeftNeighborOrderThroughObstacles()
    {
        var state = State(5, 3, new(1, 1), new(4, 2));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 0), EdgeKind.Wall));
        state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        Assert.Equal(new Cell[] { new(1, 1), new(1, 0), new(2, 0) }, Paths(state)[new(2, 0)]);
    }

    [Fact]
    public void PhaseDoesNotPermitIllegalInitialPlacement()
    {
        var state = State();
        state.Physical.Board.Terrain.Add(new(new(0, 0), TerrainKind.Water));
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new Dice(), false));
    }
}
