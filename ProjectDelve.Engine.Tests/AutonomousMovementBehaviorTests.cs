using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AutonomousMovementBehaviorTests
{
    private static GameState State(UnitBehavior behavior, int width = 12, int height = 3, Cell? position = null) => new()
    {
        Physical = new(new Board(width, height, []), [new("actor", position ?? new(1, 1))]),
        Types = [new("actor-type", 2, 1, 1, 0, 1, Behaviors: behavior), new("friend-type", 0, 1, 1, 0, 1)],
        Units = [new("actor", "actor-type", "red", 1)]
    };

    private static void Add(GameState state, string id, Cell cell, string side = "red", string type = "friend-type")
    {
        state.Units.Add(new(id, type, side, 1));
        state.Physical.Figures.Add(new(id, cell));
    }

    private static DecisionRequest Moves(GameState state, bool afterAttack = false) => new(DecisionKind.Move,
        "actor-type", "actor", MovementRules.FindPaths(state, "actor",
                state.Physical.Figures.Single(f => f.Id == "actor").Position, 2)
            .Where(p => p.Value.Count > 1).Select(p => new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(),
        true, IsMoveAfterAttack: afterAttack);

    private static string? Choose(GameState state, bool afterAttack = false) =>
        new DefaultAutomatedProvider().Choose(Moves(state, afterAttack), new GameplayQueries(state));

    [Fact]
    public void ExploreConservativeFrontierTiePrefersShortestMove()
    {
        var state = State(UnitBehavior.ExploreFoW, height: 1, position: new(1, 0));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        Assert.Equal("2,0", Choose(state));
        Assert.DoesNotContain(new Cell(7, 0), state.ExploredCells["red"]);
    }

    [Fact]
    public void HiddenTerrainUnitsAndEdgesCannotChangeExplorationChoice()
    {
        var state = State(UnitBehavior.ExploreFoW, height: 1, position: new(1, 0));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        var altered = state.Copy();
        altered.Physical.Board.Terrain.Add(new(new(8, 0), TerrainKind.SolidRock));
        altered.Physical.Board.Edges.Add(new(new(9, 0), new(10, 0), EdgeKind.Wall));
        Add(altered, "hidden", new(9, 0), "blue");
        Assert.Equal("2,0", Choose(altered));
        Assert.Equal(Choose(state), Choose(altered));
    }

    [Fact]
    public void KnownClosedDoorPreventsSpeculativeRevealThroughIt()
    {
        var state = State(UnitBehavior.ExploreFoW, height: 1, position: new(1, 0));
        state.Physical.Board.Edges.Add(new(new(6, 0), new(7, 0), EdgeKind.ClosedDoor));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        Add(state, "friend", new(0, 0));
        Assert.Null(Choose(state));
    }

    [Theory]
    [InlineData(TerrainKind.Tree, false, "2,0")]
    [InlineData(TerrainKind.SolidRock, false, null)]
    [InlineData(TerrainKind.SolidRock, true, "3,0")]
    public void ExplorationUsesVisionPropertiesAndPhase(TerrainKind terrain, bool phase, string? expected)
    {
        var state = State(UnitBehavior.ExploreFoW, height: 1, position: new(1, 0));
        state.Physical.Board.Terrain.Add(new(new(6, 0), terrain));
        if (phase) state.Types[0] = state.Types[0].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        Assert.Equal(expected, Choose(state));
    }

    [Fact]
    public void DisconnectedProductiveKnownIslandDoesNotPreventFriendlyFallback()
    {
        var state = State(UnitBehavior.ExploreFoW, width: 14, height: 1, position: new(1, 0));
        state.Physical.Board.Edges.Add(new(new(6, 0), new(7, 0), EdgeKind.Wall));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        state.ExploredCells["red"].UnionWith([new(10, 0), new(11, 0), new(12, 0), new(13, 0)]);
        Add(state, "friend", new(4, 0));
        Assert.Equal("3,0", Choose(state));
    }

    [Fact]
    public void DisabledExplorationFallsBackToFriendlyRoute()
    {
        var state = State(UnitBehavior.ExploreFoW);
        Add(state, "friend", new(8, 1));
        Assert.Equal("3,1", Choose(state));
    }

    [Fact]
    public void FriendlyRouteMakesStrictProgressAndNeverEntersOccupiedDestination()
    {
        var state = State(UnitBehavior.GoToFriendly, height: 1, position: new(0, 0));
        Add(state, "friend", new(6, 0));
        Assert.Equal("2,0", Choose(state));
        state.Physical.Figures[0] = new("actor", new(5, 0));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void SelfIsNotFriendlyGoalAndDisconnectedFriendDoesNotCauseWandering()
    {
        var state = State(UnitBehavior.GoToFriendly, height: 1, position: new(0, 0));
        Assert.Null(Choose(state));
        Add(state, "friend", new(8, 0));
        state.Physical.Board.Edges.Add(new(new(4, 0), new(5, 0), EdgeKind.Wall));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void FriendlyGoalsExcludeOverlapWithLargeFriend()
    {
        var state = State(UnitBehavior.GoToFriendly, width: 9, height: 4, position: new(0, 1));
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        Add(state, "friend", new(6, 1));
        Assert.Equal("2,1", Choose(state));
        Assert.Equal(0, new GameplayQueries(state).DistanceToFriendlyPositionFrom("actor", new(5, 1)));
    }

    [Fact]
    public void FriendlyPlanningIgnoresPeelsButActualMoveCannotCrossThem()
    {
        var state = State(UnitBehavior.GoToFriendly, height: 1, position: new(0, 0));
        Add(state, "friend", new(8, 0));
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(1, 0)));
        Assert.DoesNotContain(Moves(state).Candidates, c => c.Destination == new Cell(2, 0));
        Assert.Equal("1,0", Choose(state));
    }

    [Fact]
    public void ClosedDoorPlanningPreferenceDoesNotMakeActualMovePassDoor()
    {
        var state = State(UnitBehavior.GoToFriendly, height: 1, position: new(0, 0));
        Add(state, "friend", new(8, 0));
        state.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.ClosedDoor));
        Assert.Null(Choose(state));
        state.Types[0] = state.Types[0] with { Behaviors = UnitBehavior.GoToFriendly | UnitBehavior.ApproachThroughClosedDoors };
        Assert.Equal("2,0", Choose(state));
    }

    [Fact]
    public void BufferCountsDistinctFriendsAndStayWinsEqualScore()
    {
        var state = State(UnitBehavior.MaximizeFriendlyAdjacent, position: new(0, 1));
        Add(state, "left", new(1, 0));
        Add(state, "right", new(3, 0));
        Assert.Equal("2,1", Choose(state));
        state.Physical.Figures[0] = new("actor", new(2, 1));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void BufferWithoutAdjacentFriendsUsesFriendlyRoute()
    {
        var state = State(UnitBehavior.MaximizeFriendlyAdjacent);
        Add(state, "friend", new(10, 1));
        Assert.Equal("3,1", Choose(state));
    }

    [Fact]
    public void BufferAdjacencyDoesNotReadUnknownDiagonalFlankTerrain()
    {
        var state = State(UnitBehavior.MaximizeFriendlyAdjacent, position: new(1, 1));
        Add(state, "friend", new(2, 2));
        state.FogOfWarSides = ["red"];
        state.ExploredCells["red"] = [new(1, 1), new(2, 2)];
        var blocked = state.Copy();
        blocked.Physical.Board.Terrain.Add(new(new(2, 1), TerrainKind.SolidRock));
        blocked.Physical.Board.Terrain.Add(new(new(1, 2), TerrainKind.SolidRock));
        Assert.Equal(0, new GameplayQueries(state).CountAdjacentFriendliesFrom("actor", new(1, 1)));
        Assert.Equal(0, new GameplayQueries(blocked).CountAdjacentFriendliesFrom("actor", new(1, 1)));
        Assert.Equal(Choose(state), Choose(blocked));
    }

    [Fact]
    public void AttackFallbackDoesNotReplaceCongestedReachableAttackApproach()
    {
        var state = State(UnitBehavior.GoToFriendly, height: 1, position: new(2, 0));
        Add(state, "hostile", new(4, 0), "blue");
        Add(state, "blocker", new(3, 0));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void AttackBehaviorWithNoReachableHostileUsesFriendlyRoute()
    {
        var state = State(UnitBehavior.GoToFriendly);
        Add(state, "friend", new(10, 1));
        Assert.Equal("3,1", Choose(state));
    }

    [Fact]
    public void UnflaggedAttackBehaviorRetainsStayWhenNoHostileIsReachable()
    {
        var state = State(UnitBehavior.None);
        Add(state, "friend", new(10, 1));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void FriendlyFallbackDoesNotReplaceReachableHostileApproach()
    {
        var state = State(UnitBehavior.GoToFriendly, position: new(4, 1));
        Add(state, "friend", new(0, 1));
        Add(state, "hostile", new(10, 1), "blue");
        Assert.Equal("6,1", Choose(state));
    }

    [Fact]
    public void StandingOrderOverridesAutonomousOrdinaryMovement()
    {
        var state = State(UnitBehavior.GoToFriendly, position: new(3, 1));
        Add(state, "friend", new(10, 1));
        state.Orders.Add(new(new("actor-type", "red"), OrderKind.MoveTo, new(0, 1)));
        Assert.Equal("1,1", new OrderAwareAutomatedProvider().Choose(Moves(state), new GameplayQueries(state)));
    }

    [Fact]
    public void PostAttackBackAwayRetainsPriorityOverExplorationFallback()
    {
        var state = State(UnitBehavior.ExploreFoW | UnitBehavior.BackAwayAfterAttack, position: new(3, 1));
        Add(state, "hostile", new(3, 0), "blue");
        Add(state, "friend", new(10, 1));
        Assert.Equal("3,2", Choose(state, afterAttack: true));
    }

    [Fact]
    public void SaveContinuationRetainsDeterministicExplorationChoice()
    {
        var state = State(UnitBehavior.ExploreFoW, height: 1, position: new(1, 0));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal("2,0", Choose(restored));
        Assert.Equal(Choose(state), Choose(restored));
    }
}
