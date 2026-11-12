using System.Collections.Immutable;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AccessibilityAnalysisTests
{
    private static AccessibilityProfile Land(int mov = 3, Footprint footprint = Footprint.OneByOne) =>
        new(footprint, ["land"], mov);
    private static GameState World(UnitType? type = null, int width = 5, int height = 1)
    {
        type ??= new("test", 2, 0, 0, 0, 1);
        return new() { Physical = new(new Board(width, height, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")], CurrentUnitId = "actor" };
    }

    [Fact]
    public void CaptureIsIsolatedAndBudgetExhaustionIsUnknown()
    {
        var board = new Board(5, 1, []);
        var analysis = AccessibilityAnalysis.Capture(board, 42);
        board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        var full = analysis.Analyze(Land(), new(0, 0));
        Assert.Equal(AccessibilityStatus.Reachable, full.StatusAt(new(4, 0)));
        Assert.Equal(42, full.Revision);
        var partial = AccessibilityAnalysis.Capture(new Board(5, 1, [])).Analyze(Land(), new(0, 0), new(WorkBudget: 1));
        Assert.False(partial.Complete);
        Assert.Equal(1, partial.WorkUsed);
        Assert.Equal(AccessibilityStatus.Unknown, partial.StatusAt(new(4, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected,
            AccessibilityAnalysis.Capture(board).Analyze(Land(), new(0, 0)).StatusAt(new(4, 0)));
    }

    [Fact]
    public void PermissionsAreExplicitAndMixedTerrainDoesNotGrantNewCapabilities()
    {
        var board = new Board(3, 1, []) { Terrain = [new(new(1, 0), TerrainKind.Water)] };
        var analysis = AccessibilityAnalysis.Capture(board);
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(Land(), new(0, 0)).StatusAt(new(2, 0)));
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze(Land() with { Capabilities = ["land", "water"] }, new(0, 0)).StatusAt(new(2, 0)));
        Assert.Throws<ArgumentException>(() => analysis.Analyze(Land() with { Capabilities = ["rock"] }, new(0, 0)));
        Assert.Throws<ArgumentException>(() => analysis.Analyze(Land() with { Capabilities = ["water"] }, new(0, 0)));
    }

    [Fact]
    public void PhaseNeedsAnIntermediateStoppingPlacementWithinEachMove()
    {
        var board = new Board(5, 1, []) { Terrain = [new(new(1, 0), TerrainKind.Tree), new(new(2, 0), TerrainKind.Tree), new(new(3, 0), TerrainKind.Tree)] };
        var analysis = AccessibilityAnalysis.Capture(board);
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(Land(3) with { Phase = true }, new(0, 0)).StatusAt(new(4, 0)));
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze(Land(4) with { Phase = true }, new(0, 0)).StatusAt(new(4, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(Land(4) with { Phase = true }, new(0, 0)).StatusAt(new(2, 0)));
    }

    [Fact]
    public void StraightPatternCanTurnBetweenMovesButActualMoveCannot()
    {
        var board = new Board(3, 3, []) { Terrain = [new(new(1, 1), TerrainKind.Tree)] };
        var analysis = AccessibilityAnalysis.Capture(board);
        var profile = Land(3) with { Pattern = MovementPattern.StraightOrthogonal };
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(profile, new(0, 0), new(AccessibilityMode.ActualMove)).StatusAt(new(2, 2)));
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze(profile, new(0, 0)).StatusAt(new(2, 2)));
    }

    [Fact]
    public void KnightLeapsBarrierAtNominalCostThree()
    {
        var board = new Board(3, 2, []) { Terrain = [new(new(1, 0), TerrainKind.Tree), new(new(1, 1), TerrainKind.Tree)] };
        var analysis = AccessibilityAnalysis.Capture(board);
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze(Land(3) with { Pattern = MovementPattern.LShape }, new(0, 0)).StatusAt(new(2, 1)));
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(Land(2) with { Pattern = MovementPattern.LShape }, new(0, 0)).StatusAt(new(2, 1)));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)] [InlineData(EdgeKind.ClosedDoor)] [InlineData(EdgeKind.WallWithWindow)]
    public void CompleteLargeFootprintsRejectInternalEdges(EdgeKind kind)
    {
        var board = new Board(4, 2, [new(new(2, 0), new(3, 0), kind)]);
        var result = AccessibilityAnalysis.Capture(board).Analyze(Land(3, Footprint.TwoByTwo), new(0, 0));
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(2, 0)));
    }

    [Fact]
    public void LyingAndSpentMoveDifferFromExplicitUprightGeometry()
    {
        var world = World();
        world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Lying };
        var analysis = AccessibilityAnalysis.Capture(world);
        Assert.Single(analysis.Analyze("actor", new(0, 0)).Reachable);
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze("actor", new(0, 0), new(AssumeUpright: true)).StatusAt(new(4, 0)));
        world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Upright };
        world.CurrentUnitId = "actor"; world.MoveDone = true;
        analysis = AccessibilityAnalysis.Capture(world);
        Assert.Single(analysis.Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove)).Reachable);
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze("actor", new(0, 0)).StatusAt(new(4, 0)));
    }

    [Theory]
    [InlineData("blue", true)] [InlineData("red", false)] [InlineData("neutral", false)]
    public void OccupancyRetainsCurrentFriendlyTransitAndOtherSideBlocking(string side, bool passes)
    {
        var world = World();
        world.Units.Add(world.Types[0].CreateUnit("blocker", side));
        world.Physical.Figures.Add(new("blocker", new(1, 0), Posture.Lying));
        world.NeutralSides.Add("neutral");
        var analysis = AccessibilityAnalysis.Capture(world);
        var result = analysis.Analyze("actor", new(0, 0), new(IgnoreFigures: false));
        Assert.Equal(passes ? AccessibilityStatus.Reachable : AccessibilityStatus.Disconnected, result.StatusAt(new(4, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(1, 0)));
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze("actor", new(0, 0)).StatusAt(new(4, 0)));
    }

    [Fact]
    public void DoorClosureRequiresReachedOperatorPositionsAndReportsConditionalAccess()
    {
        var type = new UnitType("opener", 2, 0, 0, 0, 1).WithLocal(new("open", "Open", AbilityTiming.FreeAction, new OpenDoor()));
        var world = World(type);
        world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        world.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.ClosedDoor));
        var analysis = AccessibilityAnalysis.Capture(world);
        var result = analysis.Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.Equal(AccessibilityStatus.Conditional, result.StatusAt(new(4, 0)));
        Assert.All(result.Doors, d => Assert.Equal(AccessibilityDoorStatus.Deterministic, d.Status));
        Assert.Equal(EdgeKind.ClosedDoor, world.Physical.Board.Edges[0].Kind);
        world.Physical.Board.Edges.Insert(0, new(new(0, 0), new(1, 0), EdgeKind.Wall));
        result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.All(result.Doors, d => Assert.Equal(AccessibilityDoorStatus.Inaccessible, d.Status));
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(4, 0)));
    }

    [Fact]
    public void ProbabilisticDoorsNeverBecomeUnconditionalReachability()
    {
        var type = new UnitType("opener", 2, 0, 0, 0, 1).WithLocal(new("try", "Try", AbilityTiming.Action, new TryOpenDoor(2)));
        var world = World(type);
        world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.Equal(AccessibilityDoorStatus.Probabilistic, Assert.Single(result.Doors).Status);
        Assert.Equal(AccessibilityStatus.Conditional, result.StatusAt(new(4, 0)));
    }

    [Fact]
    public void IndexedCandidatesAgreeWithAuthoritativePatternPaths()
    {
        foreach (var pattern in Enum.GetValues<MovementPattern>())
        {
            var type = new UnitType("pattern", 4, 0, 0, 0, 1) { MovementPattern = pattern };
            var world = World(type, 5, 4);
            world.Physical.Board.Edges.Add(new(new(1, 0), new(1, 1), EdgeKind.WallWithWindow));
            world.Physical.Board.Terrain.Add(new(new(2, 1), TerrainKind.Tree));
            var actual = MovementRules.FindPaths(world, "actor", new(0, 0), 4).Keys.OrderBy(c => c.Y).ThenBy(c => c.X);
            var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove));
            Assert.Equal(actual, result.Reachable);
        }
    }

    [Fact]
    public void LargeOrdinaryBoardUsesLinearBoundedWork()
    {
        var result = AccessibilityAnalysis.Capture(new Board(120, 120, [])).Analyze(Land(), new(0, 0));
        Assert.True(result.Complete);
        Assert.Equal(14400, result.Reachable.Length);
        Assert.Equal(14400, result.WorkUsed);
    }

    [Fact]
    public void EquivalentProfilesShareRevisionLocalRegions()
    {
        var analysis = AccessibilityAnalysis.Capture(new Board(120, 120, []));
        var first = analysis.Analyze(Land(), new(0, 0));
        var second = analysis.Analyze(new AccessibilityProfile(Footprint.OneByOne, ImmutableArray.Create("land"), 3), new(119, 119), new(WorkBudget: 1));
        Assert.True(second.Complete);
        Assert.Equal(0, second.WorkUsed);
        Assert.Equal(first.Reachable, second.Reachable);
    }

    [Fact]
    public void EquipmentGrantsRemainInLyingProfiles()
    {
        var world = World();
        world.EquipmentTypes.Add(new("boots", "Boots") { LocalAbilities = [new("water", "Water", AbilityTiming.Passive, new MovementCapabilityGrant(["water"]))] });
        world.Units[0] = world.Units[0] with { Inventory = [new OwnedObject("physical-boots") { EquipmentTypeId = "boots" }] };
        world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Lying };
        world.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Water));
        var analysis = AccessibilityAnalysis.Capture(world);
        Assert.Contains("water", analysis.ProfileFor("actor").Capabilities);
        Assert.True(analysis.CanStop(analysis.ProfileFor("actor"), new(1, 0)));
        Assert.Single(analysis.Analyze("actor", new(0, 0)).Reachable);
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze("actor", new(0, 0), new(AssumeUpright: true)).StatusAt(new(4, 0)));
    }

    [Fact]
    public void WaterOnlyPhaseCannotStopOnLandOrMachines()
    {
        var board = new Board(4, 1, []) { Terrain = [new(new(0, 0), TerrainKind.Water), new(new(2, 0), TerrainKind.Water), new(new(3, 0), TerrainKind.Water)] };
        board.Machines.Add(new("machine", new(2, 0), new("m", "Machine", null, new([]) { Reveal = new(1) })));
        var analysis = AccessibilityAnalysis.Capture(board);
        var profile = new AccessibilityProfile(Footprint.OneByOne, ["water"], 3, Phase: true);
        var result = analysis.Analyze(profile, new(0, 0));
        Assert.Equal(AccessibilityStatus.Reachable, result.StatusAt(new(3, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(1, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(2, 0)));
        Assert.False(analysis.CanStop(profile, new(2, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(profile with { Phase = false }, new(0, 0)).StatusAt(new(3, 0)));
    }

    [Fact]
    public void RestrictionsAreDirectedWhenStartingOffRestrictedTerrain()
    {
        var board = new Board(3, 1, []) { Terrain = [new(new(1, 0), TerrainKind.Asphalt), new(new(2, 0), TerrainKind.Asphalt)] };
        var analysis = AccessibilityAnalysis.Capture(board);
        var profile = Land() with { RequiredTerrain = TerrainKind.Asphalt };
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze(profile, new(0, 0)).StatusAt(new(2, 0)));
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze(profile, new(2, 0)).StatusAt(new(0, 0)));
    }

    [Fact]
    public void ConditionalDoorAnalysisDoesNotSpendFiniteUsesOrCosts()
    {
        foreach (var ability in new[] {
            new LocalAbilityDefinition("limited", "Limited", AbilityTiming.FreeAction, new OpenDoor()) { Usage = new(1) },
            new LocalAbilityDefinition("cost", "Cost", AbilityTiming.FreeAction, new OpenDoor()) { Costs = [new LayDownSelf()] }
        })
        {
            var world = World(new UnitType("limited", 2, 0, 0, 0, 1).WithLocal(ability));
            world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
            var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
            Assert.False(result.Complete);
            Assert.Equal(AccessibilityStatus.Unknown, result.StatusAt(new(4, 0)));
            Assert.Equal(AccessibilityDoorStatus.Unsupported, Assert.Single(result.Doors).Status);
            Assert.Empty(world.Units[0].AbilityUseCounts);
            Assert.True(world.IsUpright("actor"));
        }
    }

    [Fact]
    public void LargeActorCannotOperateDoorFromANarrowUnreachableApproach()
    {
        var type = new UnitType("large", 3, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo };
        type = type.WithLocal(new("open", "Open", AbilityTiming.FreeAction, new OpenDoor()));
        var world = World(type, 5, 2);
        world.Physical.Board.Terrain.Add(new(new(2, 1), TerrainKind.Tree));
        world.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.ClosedDoor));
        var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.Equal(AccessibilityDoorStatus.Inaccessible, Assert.Single(result.Doors).Status);
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(3, 0)));
    }

    [Fact]
    public void HypotheticalKnightUsesFullGeometryWhileActualMoveHonorsFogOfWar()
    {
        var world = World(new("knight", 3, 0, 0, 0, 1) { MovementPattern = MovementPattern.LShape }, 3, 2);
        world.FogOfWarSides = ["blue"]; world.ExploredCells["blue"] = [new(0, 0)];
        var analysis = AccessibilityAnalysis.Capture(world);
        Assert.Equal(AccessibilityStatus.Reachable, analysis.Analyze("actor", new(0, 0)).StatusAt(new(2, 1)));
        Assert.Equal(AccessibilityStatus.Disconnected, analysis.Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove)).StatusAt(new(2, 1)));
    }

    [Fact]
    public void UprightDoorHypothesisIsExplicitAndOpenPassagesAreReported()
    {
        var type = new UnitType("opener", 2, 0, 0, 0, 1).WithLocal(new("open", "Open", AbilityTiming.FreeAction, new OpenDoor()));
        var world = World(type);
        world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Lying };
        world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        world.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.OpenDoor));
        var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors, AssumeUpright: true));
        Assert.Equal(AccessibilityStatus.Conditional, result.StatusAt(new(4, 0)));
        Assert.Contains(result.Doors, d => d.Status == AccessibilityDoorStatus.OpenPassage);
        Assert.NotEmpty(result.Assumptions);
    }

    [Fact]
    public void ActualMoveWaitsForDiceAndCompletionBoundaries()
    {
        var world = World();
        world.DoorInProgress = new("actor", new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor), 2, "try");
        Assert.Single(AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove)).Reachable);
        world.DoorInProgress = null;
        world.Pending = new(DecisionKind.RollDice, world.Types[0].Id, "actor", [], false);
        Assert.Single(AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove)).Reachable);
        world.Pending = null;
        world.Completion = new([]);
        Assert.Single(AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ActualMove)).Reachable);
    }

    [Fact]
    public void StaticGeometryRejectsConflictingOccupancyOption()
    {
        var analysis = AccessibilityAnalysis.Capture(new Board(3, 1, []));
        Assert.Throws<ArgumentException>(() => analysis.Analyze(Land(), new(0, 0), new(AccessibilityMode.StaticGeometry, IgnoreFigures: false)));
    }

    [Theory]
    [InlineData(0, AccessibilityStatus.Disconnected, AccessibilityDoorStatus.Unsupported)]
    [InlineData(6, AccessibilityStatus.Conditional, AccessibilityDoorStatus.Deterministic)]
    public void DoorSuccessBoundsAreExplicit(int successes, AccessibilityStatus access, AccessibilityDoorStatus doorStatus)
    {
        var type = new UnitType("opener", 2, 0, 0, 0, 1).WithLocal(new("try", "Try", AbilityTiming.Action, new TryOpenDoor(successes)));
        var world = World(type);
        world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.True(result.Complete);
        Assert.Equal(access, result.StatusAt(new(4, 0)));
        Assert.Equal(doorStatus, Assert.Single(result.Doors).Status);
    }

    [Fact]
    public void FullCustomTerrainBoardNeedsNoImplicitStoneFloorDefinition()
    {
        var custom = new TerrainDefinition("custom", "Custom", ["custom-move"], false);
        var board = new Board(2, 1, [], [custom])
        { Terrain = [new(new(0, 0), TerrainKind.StoneFloor) { TypeId = "custom" }, new(new(1, 0), TerrainKind.StoneFloor) { TypeId = "custom" }] };
        var result = AccessibilityAnalysis.Capture(board).Analyze(new AccessibilityProfile(Footprint.OneByOne, ["custom-move"], 1), new(0, 0));
        Assert.Equal(AccessibilityStatus.Reachable, result.StatusAt(new(1, 0)));
    }

    [Fact]
    public void ZeroMovCanOperateBorderingDoorWithoutClaimingMovement()
    {
        var type = new UnitType("stationary", 0, 0, 0, 0, 1).WithLocal(new("open", "Open", AbilityTiming.FreeAction, new OpenDoor()));
        var world = World(type);
        world.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        var result = AccessibilityAnalysis.Capture(world).Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        Assert.Equal(AccessibilityDoorStatus.Deterministic, Assert.Single(result.Doors).Status);
        Assert.Single(result.Reachable);
        Assert.Empty(result.Conditional);
        Assert.Equal(AccessibilityStatus.Disconnected, result.StatusAt(new(1, 0)));
    }

    [Fact]
    public void LongMovePathCopiesConsumeTheAnalysisBudget()
    {
        var analysis = AccessibilityAnalysis.Capture(new Board(1000, 1, []));
        var profile = Land(999) with { Pattern = MovementPattern.StraightOrthogonal };
        var result = analysis.Analyze(profile, new(0, 0), new(AccessibilityMode.ActualMove, WorkBudget: 100));
        Assert.False(result.Complete);
        Assert.Equal(100, result.WorkUsed);
        Assert.Equal(AccessibilityStatus.Unknown, result.StatusAt(new(999, 0)));
    }

    [Fact]
    public void EquivalentActorsShareCompletedConditionalClosureOnlyWithinTheirUnconditionalRegion()
    {
        var type = new UnitType("opener", 2, 0, 0, 0, 1).WithLocal(new("open", "Open", AbilityTiming.FreeAction, new OpenDoor()));
        var world = World(type, 5, 2);
        world.Units.Add(type.CreateUnit("other", "blue"));
        world.Physical.Figures.Add(new("other", new(0, 1)));
        world.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        world.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        var analysis = AccessibilityAnalysis.Capture(world);
        var first = analysis.Analyze("actor", new(0, 0), new(AccessibilityMode.ConditionalDoors));
        var reused = analysis.Analyze("other", new(0, 1), new(AccessibilityMode.ConditionalDoors, WorkBudget: 1));
        Assert.True(first.Complete);
        Assert.True(reused.Complete);
        Assert.Equal(0, reused.WorkUsed);
        Assert.Equal(first.Reachable, reused.Reachable);
        Assert.Equal(first.Conditional, reused.Conditional);
        Assert.Equal(first.Doors, reused.Doors);
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.Analyze("other", new(0, 1),
            new(AccessibilityMode.ConditionalDoors, WorkBudget: -1)));
        var reverse = analysis.Analyze("other", new(4, 1), new(AccessibilityMode.ConditionalDoors));
        Assert.True(reverse.WorkUsed > 0);
        Assert.Equal(AccessibilityStatus.Reachable, reverse.StatusAt(new(4, 0)));
        Assert.Equal(AccessibilityStatus.Conditional, reverse.StatusAt(new(0, 0)));
    }
}
