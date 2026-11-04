using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Testing;
using Xunit;

namespace ProjectDelve.Engine.Tests;

/// <summary>
/// Differential checks for the refresh reuse path. ReferenceRefresh intentionally
/// contains the pre-cache, full recomputation semantics.
/// </summary>
public sealed class FogOfWarRefreshReuseTests
{
    private static GameState NewState() => GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon());
    private sealed class FirstTokenRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private static void CompareRefresh(GameState state)
    {
        // Also compare current sight without permanent history masking an erroneous
        // reuse after a dependency shrinks vision (for example, closing a Door).
        var sightOnly = state.Copy();
        sightOnly.ExploredCells.Clear();
        var expectedSight = sightOnly.Copy();
        ReferenceRefresh(expectedSight);
        FogOfWar.Refresh(sightOnly);
        AssertExplorationEqual(expectedSight, sightOnly);
        var expected = state.Copy();
        ReferenceRefresh(expected);
        FogOfWar.Refresh(state);
        AssertExplorationEqual(expected, state);
        Assert.Equal(JsonSerializer.Serialize(FogOfWar.Observe(expected, "explorers")),
            JsonSerializer.Serialize(FogOfWar.Observe(state, "explorers")));
        var eventToObserve = new RulesEvent("MovementCompleted", UnitId: "hero", Path: [new(3, 4), new(4, 4)]);
        Assert.Equal(JsonSerializer.Serialize(FogOfWar.ObserveEvent(expected, "explorers", eventToObserve)),
            JsonSerializer.Serialize(FogOfWar.ObserveEvent(state, "explorers", eventToObserve)));
    }

    private static void AssertExplorationEqual(GameState expected, GameState actual)
    {
        Assert.Equal(expected.ExploredCells.Keys.Order(), actual.ExploredCells.Keys.Order());
        foreach (var side in expected.ExploredCells.Keys)
            Assert.True(expected.ExploredCells[side].SetEquals(actual.ExploredCells[side]), $"Exploration differs for {side}.");
    }

    // Frozen implementation of the original full Refresh calculation.
    private static void ReferenceRefresh(GameState state)
    {
        foreach (var side in state.FogOfWarSides)
        {
            if (!state.ExploredCells.TryGetValue(side, out var explored)) state.ExploredCells[side] = explored = [];
            foreach (var unit in state.Units.Where(u => u.SideId == side && u.CurrentHp > 0 &&
                state.Physical.Figures.Any(f => f.Id == u.Id)))
                ReferenceExploreFrom(state, unit, explored);
        }
    }

    private static void ReferenceExploreFrom(GameState state, Unit unit, HashSet<Cell> explored)
    {
        var board = state.Physical.Board;
        const int range = 5;
        var phase = state.HasPhase(unit.Id);
        foreach (var from in FootprintGeometry.OccupiedCells(state, unit.Id))
        for (var y = Math.Max(0, from.Y - range); y <= Math.Min(board.Height - 1, from.Y + range); y++)
        for (var x = Math.Max(0, from.X - range); x <= Math.Min(board.Width - 1, from.X + range); x++)
        {
            var to = new Cell(x, y);
            if (Math.Abs(x - from.X) + Math.Abs(y - from.Y) <= range &&
                AttackRules.HasGeometricLineOfSight(board, from, to, ignoredTerrainCell: to,
                    phaseVision: phase, discoverDestination: true)) explored.Add(to);
        }
    }

    private static void Relocate(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }

    [Fact]
    public void RefreshMatchesFullRecomputationAcrossSourceAndBoardMutations()
    {
        var state = NewState();
        CompareRefresh(state);
        state.FogOfWarSides.Add("dungeon");
        CompareRefresh(state);

        Relocate(state, "hero", new(1, 1));
        CompareRefresh(state);

        var heroIndex = state.Physical.Figures.FindIndex(f => f.Id == "hero");
        state.Physical.Figures[heroIndex] = state.Physical.Figures[heroIndex] with { Posture = Posture.Lying };
        CompareRefresh(state);

        // In-place edits must invalidate board assumptions even though list identities stay fixed.
        state.Physical.Board.Edges.Clear();
        state.Physical.Board.Edges.Add(new(new(2, 0), new(2, 1), EdgeKind.Wall));
        CompareRefresh(state);

        state.Physical.Board.Terrain.Add(new(new(1, 2), TerrainKind.Tree));
        CompareRefresh(state);

        state.Physical.Board.Machines.Add(new("vision-blocker", new(3, 1), MachineTypes.DiamondPress));
        CompareRefresh(state);

        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = EdgeKind.OpenDoor };
        CompareRefresh(state);
        state.Physical.Board.Terrain[0] = state.Physical.Board.Terrain[0] with { Kind = TerrainKind.StoneFloor };
        CompareRefresh(state);
        state.Physical.Board.Machines[0] = state.Physical.Board.Machines[0] with { Position = new(4, 1) };
        CompareRefresh(state);

        state.Units[state.Units.FindIndex(u => u.Id == "guard")] = state.Units.Single(u => u.Id == "guard") with { CurrentHp = 0 };
        CompareRefresh(state);
        state.Physical.Figures.RemoveAll(f => f.Id == "sleeper");
        CompareRefresh(state);
        state.PlaceUnit(UnitTypeIds.Grunt, "new-scout", "explorers", new(17, 7));
        CompareRefresh(state);
    }

    [Fact]
    public void RefreshTracksTypePhaseFootprintSideAndCapturedBoardDefinitions()
    {
        var state = NewState();
        state.FogOfWarSides.Add("dungeon");
        CompareRefresh(state);
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        CompareRefresh(state);
        state.Types[0] = state.Types[0].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        CompareRefresh(state);

        state.EquipmentTypes.Add(new EquipmentDefinition("vision-gear", "Vision Gear")
        {
            LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())]
        });
        var guardIndex = state.Units.FindIndex(u => u.Id == "guard");
        state.Units[guardIndex] = state.Units[guardIndex] with
        { Inventory = [new OwnedObject("gear", "vision-gear")] };
        CompareRefresh(state);
        var guardFigure = state.Physical.Figures.FindIndex(f => f.Id == "guard");
        state.Physical.Figures[guardFigure] = state.Physical.Figures[guardFigure] with { Posture = Posture.Lying };
        CompareRefresh(state);

        state.FogOfWarSides.Remove("explorers");
        CompareRefresh(state);

        // Restore the enabled side, then replace the Board snapshot with a different
        // captured terrain definition while retaining the board's ordinary lists.
        state.FogOfWarSides.Add("explorers");
        var board = state.Physical.Board;
        board.Terrain.Add(new(new(4, 4), TerrainKind.Tree));
        var defs = board.TerrainTypes.Select(d => d.Id == "tree" ? d with { BlocksLos = false } : d).ToImmutableArray();
        state.Physical = state.Physical with { Board = board with { TerrainTypes = defs } };
        CompareRefresh(state);
    }

    [Fact]
    public void RefreshAlwaysRestoresCurrentSightWithoutErasingPermanentDiscoveries()
    {
        var state = NewState();
        state.Physical.Board.Edges.Clear();
        var trajectoryCell = new Cell(19, 8);
        state.ExploredCells["explorers"].Add(trajectoryCell);
        state.ExploredCells["explorers"].Remove(new(3, 4));
        FogOfWar.Refresh(state);
        Assert.Contains(trajectoryCell, state.ExploredCells["explorers"]);
        Assert.Contains(new Cell(3, 4), state.ExploredCells["explorers"]);

        state.ExploredCells["explorers"].Remove(trajectoryCell);
        var beforeAttack = state.ExploredCells["explorers"].ToHashSet();
        FogOfWar.DiscoverAttack(state, new("hero", "attack", null, null, [new("archer", 0)], 1)
        { RangeOverride = 20 });
        var discovered = state.ExploredCells["explorers"].ToHashSet();
        Assert.NotEmpty(discovered.Except(beforeAttack));
        FogOfWar.Refresh(state);
        Assert.True(discovered.IsSubsetOf(state.ExploredCells["explorers"]));
    }

    [Fact]
    public void PreviouslyCapturedResolutionSnapshotStaysStableWhenLaterRefreshReplacesCache()
    {
        var result = GameEngine.StartRound(NewState(), new FirstTokenRandom());
        var step = result.ResolutionSteps.First();
        var snapshot = step.StateAfter;
        var state = result.State;
        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var observedJson = JsonSerializer.Serialize(FogOfWar.Observe(snapshot, "explorers"));
        var eventToObserve = result.Events[step.EventIndex];
        var observedEventJson = JsonSerializer.Serialize(FogOfWar.ObserveEvent(snapshot, "explorers", eventToObserve));

        Relocate(state, "hero", new(0, 0));
        FogOfWar.Refresh(state);

        Assert.Equal(snapshotJson, JsonSerializer.Serialize(snapshot));
        Assert.Equal(observedJson, JsonSerializer.Serialize(FogOfWar.Observe(snapshot, "explorers")));
        Assert.Equal(observedEventJson, JsonSerializer.Serialize(FogOfWar.ObserveEvent(snapshot, "explorers", eventToObserve)));
    }

    [Fact]
    public void SweepMatchesReferenceForCanonicalTransitAndDoesNotInterpolateLeaps()
    {
        var pathState = NewState();
        pathState.Physical.Board.Edges.Clear();
        var path = new[] { new Cell(3, 4), new(3, 3), new(4, 3), new(4, 4) };
        var expected = pathState.Copy();
        ReferenceSweep(expected, "hero", path);
        FogOfWar.Sweep(pathState, "hero", path);
        AssertExplorationEqual(expected, pathState);

        var leap = NewState();
        leap.Physical.Board.Edges.Clear();
        var leapExpected = leap.Copy();
        var leapPath = new[] { new Cell(3, 4), new(5, 5) };
        ReferenceSweep(leapExpected, "hero", leapPath);
        FogOfWar.Sweep(leap, "hero", leapPath);
        AssertExplorationEqual(leapExpected, leap);
        Assert.Equal(new Cell(3, 4), leap.Physical.Figures.Single(f => f.Id == "hero").Position);
    }

    [Fact]
    public void NonFogMoverSweepRefreshesStaleGeometryForEveryEnabledSide()
    {
        var state = NewState();
        state.FogOfWarSides.Add("third-side");
        state.Physical.Board.Edges.Clear();
        state.ExploredCells["explorers"].Remove(new(3, 4));
        var expected = state.Copy();
        ReferenceSweep(expected, "guard", [new(8, 4), new(9, 4)]);
        FogOfWar.Sweep(state, "guard", [new(8, 4), new(9, 4)]);
        AssertExplorationEqual(expected, state);
    }

    private static void ReferenceSweep(GameState state, string unitId, IReadOnlyList<Cell> path)
    {
        if (state.FogOfWarSides.Count == 0) return;
        foreach (var position in path)
        {
            var transit = state.Copy();
            var index = transit.Physical.Figures.FindIndex(f => f.Id == unitId);
            transit.Physical.Figures[index] = transit.Physical.Figures[index] with { Position = position };
            ReferenceRefresh(transit);
            foreach (var side in state.FogOfWarSides)
            {
                if (!state.ExploredCells.TryGetValue(side, out var cells)) state.ExploredCells[side] = cells = [];
                cells.UnionWith(transit.ExploredCells[side]);
            }
        }
    }

    [Fact]
    public void CacheIsAbsentFromSaveAndCopiesKeepExplorationIndependent()
    {
        var state = NewState();
        var completed = state.VisionCache;
        FogOfWar.Refresh(state);
        Assert.Same(completed, state.VisionCache);
        var copy = state.Copy();
        Assert.Same(state.VisionCache, copy.VisionCache);
        copy.ExploredCells["explorers"].Add(new(19, 8));
        Assert.DoesNotContain(new Cell(19, 8), state.ExploredCells["explorers"]);
        Relocate(copy, "hero", new(0, 0));
        FogOfWar.Refresh(copy);
        Assert.Contains(new Cell(0, 0), copy.ExploredCells["explorers"]);
        Assert.Contains(new Cell(3, 4), state.ExploredCells["explorers"]);
        var json = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("visionCache", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<GameState>(json)!;
        CompareRefresh(restored);
    }
}
