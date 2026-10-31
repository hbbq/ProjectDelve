using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed partial class DelveChessTests
{
    private static GameState Bishop(int mov = 16) => State(UnitRoster.RoyalBishop() with { Mov = mov }, new(6, 5));
    private static GameState Knight(int mov = 3) => State(UnitRoster.RoyalKnight() with { Mov = mov }, new(6, 5));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Phase2CustomPatternScenarioTransportPreservesMechanics(bool knight)
    {
        var type = knight ? UnitRoster.RoyalKnight("custom") : UnitRoster.RoyalBishop("custom");
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 7 }, UnitTypeIds = [type.Id], UnitTypes = [type],
            Units = [new(type.Id, "either-side", new(3, 3))],
            Agency = [new(type.Id, "either-side", ControllerKind.Human)],
            Objectives = [new("either-side", ScenarioObjective.NoObjective)]
        };
        var loaded = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        Assert.Equal(type.MovementPattern, loaded.Types.Single().MovementPattern);
        Assert.Equal(type.Mechanic<AdvanceOnKill>(), loaded.Types.Single().Mechanic<AdvanceOnKill>());
        var paths = MovementRules.FindPaths(loaded, loaded.Units.Single().Id, new(3, 3), type.Mov);
        Assert.Contains(knight ? new Cell(5, 4) : new Cell(5, 5), paths.Keys);
        Assert.DoesNotContain(new Cell(3, 2), paths.Keys);
    }

    [Theory]
    [InlineData(-1, -1)] [InlineData(-1, 1)] [InlineData(1, -1)] [InlineData(1, 1)]
    public void BishopStraightDiagonalsCostTwoAndUseCanonicalCorners(int dx, int dy)
    {
        var state = Bishop(5);
        var paths = Paths(state);
        var end = new Cell(3 + 2 * dx, 3 + 2 * dy);
        var path = paths[end];
        Assert.Equal(5, path.Count);
        Assert.Equal(new Cell(3 + dx, 3 + dy), path[2]);
        // North precedes horizontal; horizontal precedes south in ordinary BFS.
        Assert.Equal(dy < 0 ? new Cell(3, 2) : new Cell(3 + dx, 3), path[1]);
        Assert.Equal(dy < 0 ? new Cell(3 + dx, 3 + 2 * dy) : new Cell(3 + 2 * dx, 3 + dy), path[3]);
        Assert.DoesNotContain(new Cell(3 + 3 * dx, 3 + 3 * dy), paths.Keys);
        Assert.All(paths.Keys, c => Assert.Equal(Math.Abs(c.X - 3), Math.Abs(c.Y - 3)));
        Assert.Single(Paths(Bishop(1)));
        Assert.Contains(new Cell(3 + dx, 3 + dy), Paths(Bishop(2)).Keys);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void BishopEitherCornerCanBeUsedButNoLongDetour(bool horizontalBlocked)
    {
        var state = Bishop();
        var blocked = horizontalBlocked ? new Cell(4, 3) : new Cell(3, 4);
        var free = horizontalBlocked ? new Cell(3, 4) : new Cell(4, 3);
        state.Physical.Board.Edges.Add(new(new(3, 3), blocked, EdgeKind.Wall));
        Assert.Equal(free, Paths(state)[new(4, 4)][1]);
        state.Physical.Board.Edges.Add(new(new(3, 3), free, EdgeKind.Wall));
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(5, 5), Paths(state).Keys);
        Assert.Contains(new Cell(4, 4), MovementRules.FindPaths(state, "m", new(3, 3), 16, ignoreMovementPattern: true).Keys);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)] [InlineData(EdgeKind.WallWithWindow, false)]
    [InlineData(EdgeKind.ClosedDoor, false)] [InlineData(EdgeKind.OpenDoor, true)]
    public void BishopBothStepsRespectEdges(EdgeKind edge, bool legal)
    {
        var state = Bishop();
        // Block the last step of one route, first step of the other.
        state.Physical.Board.Edges.Add(new(new(4, 3), new(4, 4), edge));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(3, 4), edge));
        Assert.Equal(legal, Paths(state).ContainsKey(new(4, 4)));
    }

    [Theory]
    [InlineData("a", true)] [InlineData("b", false)]
    public void BishopFriendlyTransitIncludesDiagonalCellsButNeverOccupiedDestinations(string side, bool legal)
    {
        var state = Bishop();
        foreach (var cell in new Cell[] { new(4, 3), new(3, 4), new(4, 4) })
        {
            var id = $"blocker-{cell.X}-{cell.Y}";
            state.Units.Add(new(id, "target", side, 1));
            state.Physical.Figures.Add(new(id, cell));
        }
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
        Assert.Equal(legal, Paths(state).ContainsKey(new(5, 5)));
    }

    [Fact]
    public void BishopPeelsBlockCornerAndDiagonalTransitAndOnlyDestinationArrives()
    {
        var state = Bishop();
        state.PlaceResource(ResourceTypes.Coal, new(4, 3));
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 3));
        Assert.Equal(new Cell(3, 4), Paths(state)[new(4, 4)][1]);
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(3, 4));
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
        state = Bishop();
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 4));
        Assert.Contains(new Cell(4, 4), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(5, 5), Paths(state).Keys);
        Assert.Contains(new Cell(5, 5), MovementRules.FindPaths(state, "m", new(3, 3), 16, ignoreCellMarkers: true).Keys);
        var moved = TestGame.Advance(Choices(state).State, new Choice("4,4"), new Dice(), false);
        Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(Posture.Lying, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        Assert.Empty(moved.State.CellMarkers);
        Assert.Contains(moved.Events, e => e.Kind == "ActivationCompleted");
        state = Bishop();
        state.PlaceResource(ResourceTypes.Coal, new(4, 3));
        moved = TestGame.Advance(Choices(state).State, new Choice("5,5"), new Dice(), false);
        Assert.Equal(Posture.Upright, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        Assert.Single(moved.State.Resources);
        Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(5, moved.Events.Single(e => e.Kind == "MovementCompleted").Path!.Count);
    }

    [Fact]
    public void BishopCannotLeaveOneCellWideStraightCorridorOrChangeDirection()
    {
        var state = Bishop();
        for (var y = 0; y < 7; y++)
        {
            state.Physical.Board.Terrain.Add(new(new(2, y), TerrainKind.Water));
            state.Physical.Board.Terrain.Add(new(new(4, y), TerrainKind.Water));
        }
        Assert.Single(Paths(state));
        state = Bishop();
        Assert.DoesNotContain(new Cell(4, 6), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(4, 3), Paths(state).Keys);
    }

    [Fact]
    public void BishopChoosesCornersIndependentlyAndUsesEffectiveMov()
    {
        var state = Bishop(3);
        // First SE segment must go down then right; the next must go right then down.
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(4, 4), new(4, 5), EdgeKind.Wall));
        Assert.DoesNotContain(new Cell(5, 5), Paths(state).Keys);
        state.ModifiersThisTurn.Add(new(Stat.Mov, 1));
        var path = Paths(state)[new(5, 5)];
        Assert.Equal(new Cell[] { new(3, 3), new(3, 4), new(4, 4), new(5, 4), new(5, 5) }, path);
        Assert.Equal(4, MovementRules.Cost(path));
    }

    [Fact]
    public void KnightLandingHonorsTerrainRestrictionAndMachineOccupancy()
    {
        var state = Knight();
        state.Types[0] = state.Types[0] with { TerrainRestrictedMovement = new(TerrainKind.Grass) };
        state.Physical.Board.Terrain.Add(new(new(3, 3), TerrainKind.Grass));
        state.Physical.Board.Terrain.Add(new(new(5, 4), TerrainKind.Grass));
        Assert.Equal(2, Paths(state).Count);
        state.Physical.Board.Machines.Add(new("press", new(5, 4), MachineTypes.DiamondPress));
        Assert.Single(Paths(state));
    }

    [Theory]
    [InlineData(-2, -1)] [InlineData(-2, 1)] [InlineData(2, -1)] [InlineData(2, 1)]
    [InlineData(-1, -2)] [InlineData(-1, 2)] [InlineData(1, -2)] [InlineData(1, 2)]
    public void KnightAllEightLeapsHaveNoIntermediateCells(int dx, int dy)
    {
        var paths = Paths(Knight());
        Assert.Equal(9, paths.Count);
        Assert.Equal(new Cell[] { new(3, 3), new(3 + dx, 3 + dy) }, paths[new(3 + dx, 3 + dy)]);
        Assert.Equal(3, MovementRules.Cost(paths[new(3 + dx, 3 + dy)]));
        Assert.Equal(0, MovementRules.Cost(paths[new(3, 3)]));
    }

    [Fact]
    public void KnightMovThresholdAndBoardBoundsNeverChain()
    {
        Assert.Single(Paths(Knight(2)));
        var state = Knight(2);
        state.ModifiersThisTurn.Add(new(Stat.Mov, 1));
        Assert.Equal(9, Paths(state).Count);
        Assert.Equal(Paths(Knight()).Keys.OrderBy(c => c.Y).ThenBy(c => c.X),
            Paths(Knight(30)).Keys.OrderBy(c => c.Y).ThenBy(c => c.X));
        state = Knight();
        state.Physical.Figures[0] = new("m", new(0, 0));
        var paths = MovementRules.FindPaths(state, "m", new(0, 0), 3);
        Assert.Equal(new Cell[] { new(0, 0), new(2, 1), new(1, 2) }.OrderBy(c => c.Y), paths.Keys.OrderBy(c => c.Y));
    }

    [Theory]
    [InlineData("a")] [InlineData("b")]
    public void KnightIgnoresIntermediateEdgesTerrainUnitsAndPeels(string side)
    {
        var state = Knight();
        foreach (var c in MovementRules.Neighbors(new(3, 3)))
        {
            state.Physical.Board.Edges.Add(new(new(3, 3), c, EdgeKind.Wall));
            state.Physical.Board.Terrain.Add(new(c, TerrainKind.Water));
        }
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(2, 2));
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 4));
        state.Units.Add(new("blocker", "target", side, 1));
        state.Physical.Figures.Add(new("blocker", new(4, 4)));
        Assert.Equal(9, Paths(state).Count);
        var moved = TestGame.Advance(Choices(state).State, new Choice("5,4"), new Dice(), false);
        Assert.Equal(new Cell(5, 4), moved.State.Physical.Figures.Single(f => f.Id == "m").Position);
        Assert.Equal(Posture.Upright, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        Assert.Equal(2, moved.State.CellMarkers.Count);
    }

    [Fact]
    public void KnightCannotLandOnUnitsOrImpassableTerrainAndPeelResolvesOnlyOnLanding()
    {
        var state = Knight();
        state.Physical.Board.Terrain.Add(new(new(5, 4), TerrainKind.Water));
        Assert.DoesNotContain(new Cell(5, 4), Paths(state).Keys);
        foreach (var side in new[] { "a", "b" })
        {
            state = Knight();
            state.Units.Add(new("blocker", "target", side, 1));
            state.Physical.Figures.Add(new("blocker", new(5, 4)));
            Assert.DoesNotContain(new Cell(5, 4), Paths(state).Keys);
        }
        state = Knight();
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(5, 4));
        var moved = TestGame.Advance(Choices(state).State, new Choice("5,4"), new Dice(), false);
        Assert.Equal(Posture.Lying, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        Assert.Empty(moved.State.CellMarkers);
        Assert.Contains(moved.Events, e => e.Kind == "ActivationCompleted");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Phase2AdditionalMovesAndBoostUseTheSamePattern(bool knight)
    {
        var type = UnitType.Define("boost", "Boost", UnitAuthoring.Stats(knight ? 3 : 4, 1, 3, 4, 1),
            knight ? UnitAuthoring.LShape() : UnitAuthoring.DiagonalCorridor(),
            UnitAuthoring.ResourceStorage(ResourceTypes.Mushroom, 1), UnitAuthoring.Boost(1));
        var state = State(type, new(6, 5));
        state.StoredResources.Add(new("a", type.Id, ResourceTypes.Mushroom, 1));
        var first = knight ? "5,4" : "4,4";
        var second = knight ? new Cell(4, 2) : new Cell(5, 3);
        var moved = TestGame.Advance(Choices(state).State, new Choice(first), new Dice(), false);
        var boost = moved.NextInput!.Candidates.Single(c => c.Ability?.Definition.Mechanic is Boost);
        var boosted = TestGame.Advance(moved.State, new Choice(boost.Key), new Dice(), false);
        Assert.False(boosted.State.MoveDone);
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Destination == second);
        Assert.DoesNotContain(boosted.NextInput.Candidates, c => c.Destination == (knight ? new Cell(5, 5) : new Cell(4, 5)));
        var completed = TestGame.Advance(boosted.State, new Choice($"{second.X},{second.Y}"), new Dice(), false);
        Assert.Equal(second, completed.State.Physical.Figures.Single(f => f.Id == "m").Position);
    }

    [Fact]
    public void BishopAdvanceOnKillCanChangeCheckerboardColor()
    {
        var result = Attack(State(UnitRoster.RoyalBishop(), new(4, 3)));
        Assert.True(result.NextInput!.IsMoveAfterAttack);
        Assert.Equal(new Cell(4, 3), Assert.Single(result.NextInput.Candidates).Destination);
        var moved = TestGame.Advance(result.State, new Choice("4,3"), new Dice(), false);
        Assert.Equal(new Cell[] { new(3, 3), new(4, 3) }, moved.Events.Single(e => e.Kind == "MovementCompleted").Path);
    }

    [Fact]
    public void KnightRetainsOrdinaryMeleeIncludingDiagonalAndHasNoAdvanceOrReaction()
    {
        var state = State(UnitRoster.RoyalKnight(), new(4, 4));
        state.MoveDone = true;
        Assert.Contains(Choices(state).NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack && c.TargetId == "t");
        var result = Attack(state);
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.DoesNotContain(result.NextInput?.Candidates ?? [], c => c.Destination is not null);
        state = State(UnitRoster.RoyalKnight(), new(5, 4));
        state.MoveDone = true;
        Assert.DoesNotContain(Choices(state).NextInput?.Candidates ?? [], c => c.Action == UnitAction.NormalAttack);
    }

    [Theory]
    [InlineData(UnitTypeIds.RoyalBishop)] [InlineData(UnitTypeIds.GoblinBishop)]
    [InlineData(UnitTypeIds.RoyalKnight)] [InlineData(UnitTypeIds.HobbyKnight)]
    public void Phase2LegalChoicesExecuteAndBehaviorSerializationAndPlaybackAgree(string id)
    {
        var state = State(CanonicalUnitTypes.Find(id), new(6, 5));
        var expected = Paths(state);
        var pending = Choices(state);
        var candidates = pending.NextInput!.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList();
        Assert.Equal(expected.Count - 1, candidates.Count);
        var key = new DefaultAutomatedProvider().Choose(pending.NextInput, new GameplayQueries(pending.State));
        Assert.Contains(pending.NextInput.Candidates, c => c.Key == key);
        foreach (var candidate in candidates)
        {
            var loaded = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(pending.State))!;
            var moved = TestGame.Advance(loaded, new Choice(candidate.Key), new Dice(), false);
            var ev = Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
            Assert.Equal(expected[candidate.Destination!], ev.Path);
            Assert.Equal(candidate.Destination, moved.State.Physical.Figures.Single(f => f.Id == "m").Position);
            Assert.Equal(candidate.Destination, moved.ResolutionSteps[moved.Events.IndexOf(ev)].StateAfter.Physical.Figures.Single(f => f.Id == "m").Position);
        }
        Assert.Throws<ArgumentException>(() => TestGame.Advance(pending.State, new Choice("3,2"), new Dice(), false));
        var type = state.Types[0];
        Assert.Equal((id.Contains("bishop") ? 16 : 3, 1, 3, 4, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Equal(id.Contains("bishop"), type.Mechanic<AdvanceOnKill>() is not null);
    }
}
