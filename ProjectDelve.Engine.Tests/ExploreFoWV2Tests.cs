using System.Text.Json;
using System.Diagnostics;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class ExploreFoWV2Tests(ITestOutputHelper output)
{
    private static GameState State(int width = 14, int height = 1, Cell? position = null, int movement = 2) => new()
    {
        Physical = new(new Board(width, height, []), [new("scout", position ?? new(0, 0))]),
        Types = [new("scout-type", movement, 0, 0, 0, 1, Behaviors: UnitBehavior.ExploreFoW)],
        Units = [new("scout", "scout-type", "red", 1)],
        FogOfWarSides = ["red"],
        ExploredCells = new() { ["red"] = Enumerable.Range(0, 11).Select(x => new Cell(x, 0)).ToHashSet() }
    };

    private static DecisionRequest Moves(GameState state) => new(DecisionKind.Move, "scout-type", "scout",
        MovementRules.FindPaths(state, "scout", state.Physical.Figures[0].Position, state.Types[0].Mov)
            .Where(p => p.Value.Count > 1).Select(p => new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);

    private static MovementPolicyResult Explore(GameState state) => AutonomousMovementBehavior.Explore(Moves(state), new GameplayQueries(state));

    [Fact]
    public void SweptPathCountsDistinctDiscoveriesFromAllTransitPositionsWithoutMutatingState()
    {
        var state = State(10, 10, new(3, 3), 3);
        state.ExploredCells["red"] = Enumerable.Range(0, 7)
            .SelectMany(y => Enumerable.Range(0, 7).Select(x => new Cell(x, y))).ToHashSet();
        Cell[] path = [new(3, 3), new(4, 3), new(4, 4), new(3, 4)];
        var before = JsonSerializer.Serialize(state);
        var queries = new GameplayQueries(state);
        var score = queries.PotentialDiscoveryAlongPath("scout", path);
        Assert.True(score > queries.PotentialDiscoveryAlongPath("scout", [path[^1]]));
        Assert.Equal(score, queries.PotentialDiscoveryAlongPath("scout", path.Concat(path).ToArray()));
        Assert.Equal(score, queries.PotentialDiscoveryAlongPath("scout", path.Reverse().ToArray()));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData(false, 1, "2,0")]
    [InlineData(true, 2, "3,0")]
    public void OrdinaryVisionStopsAtFirstUnknownWhilePhaseRanksFullDiscovery(bool phase, int expectedScore, string expectedMove)
    {
        var state = State(position: new(1, 0));
        state.ExploredCells["red"] = Enumerable.Range(0, 7).Select(x => new Cell(x, 0)).ToHashSet();
        if (phase) state.Types[0] = state.Types[0].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        Assert.Equal(expectedScore, new GameplayQueries(state).PotentialDiscoveryAlongPath("scout", [new(1, 0), new(2, 0), new(3, 0)]));
        Assert.Equal(expectedMove, Explore(state).CandidateKey);
    }

    [Fact]
    public void TwoDimensionalExplorationChoosesGreatestFullPathDiscoveryBeforeTravelCost()
    {
        var state = State(12, 12, new(3, 3), 3);
        state.ExploredCells["red"] = Enumerable.Range(0, 7)
            .SelectMany(y => Enumerable.Range(0, 7).Select(x => new Cell(x, y))).ToHashSet();
        var request = Moves(state);
        var queries = new GameplayQueries(state);
        var scores = request.Candidates.ToDictionary(c => c.Key, c => queries.PotentialDiscoveryAlongPath("scout", c.Path!));
        var chosen = AutonomousMovementBehavior.Explore(request, queries);
        Assert.Equal(MovementPolicyOutcome.Move, chosen.Outcome);
        Assert.True(scores.Values.Max() > scores.Values.Min());
        Assert.Equal(scores.Values.Max(), scores[chosen.CandidateKey!]);
        var maximumCandidates = request.Candidates.Where(c => scores[c.Key] == scores.Values.Max()).ToArray();
        var expected = maximumCandidates.OrderBy(c => MovementRules.Cost(c.Path!))
            .ThenBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X).ThenBy(c => c.Key, StringComparer.Ordinal).First();
        Assert.Equal(expected.Key, chosen.CandidateKey);
        Assert.Equal(chosen, AutonomousMovementBehavior.Explore(request with { Candidates = request.Candidates.AsEnumerable().Reverse().ToList() }, queries));
    }

    [Fact]
    public void ProductiveKnownGoalUsesFigureAndMarkerIgnoringPlanningButActualMoveRemainsLegal()
    {
        var state = State();
        state.Types.Add(new("blocker-type", 0, 0, 0, 0, 1));
        state.Units.Add(new("blocker", "blocker-type", "blue", 1));
        state.Physical.Figures.Add(new("blocker", new(3, 0)));
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(1, 0)));
        var queries = new GameplayQueries(state);
        Assert.Equal(6, queries.DistanceToExplorationPositionFrom("scout", new(0, 0)));
        Assert.Equal(5, queries.DistanceToExplorationPositionFrom("scout", new(1, 0)));
        Assert.DoesNotContain(Moves(state).Candidates, c => c.Destination == new Cell(2, 0));
        Assert.Equal("1,0", Explore(state).CandidateKey);
    }

    [Fact]
    public void ProductiveGoalBeyondTemporaryEnemyBlockerProducesStayRatherThanInapplicable()
    {
        var state = State(movement: 1);
        state.Types.Add(new("blocker-type", 0, 0, 0, 0, 1));
        state.Units.Add(new("blocker", "blocker-type", "blue", 1));
        state.Physical.Figures.Add(new("blocker", new(1, 0)));
        Assert.Equal(6, new GameplayQueries(state).DistanceToExplorationPositionFrom("scout", new(0, 0)));
        Assert.Equal(MovementPolicyOutcome.Stay, Explore(state).Outcome);
    }

    [Theory]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.Wall)]
    public void ClosedDoorAndDisconnectedKnownRegionDoNotInventReachableExplorationGoal(EdgeKind edge)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(4, 0), new(5, 0), edge));
        Assert.Null(new GameplayQueries(state).DistanceToExplorationPositionFrom("scout", new(0, 0), true));
        Assert.Equal(MovementPolicyOutcome.Inapplicable, Explore(state).Outcome);
    }

    [Fact]
    public void DisabledFoWIsInapplicableEvenIfExplorationDictionaryExists()
    {
        var state = State(); state.FogOfWarSides.Clear();
        Assert.Equal(0, new GameplayQueries(state).PotentialDiscoveryAlongPath("scout", [new(0, 0), new(1, 0)]));
        Assert.Equal(MovementPolicyOutcome.Inapplicable, Explore(state).Outcome);
    }

    [Fact]
    public void AmphibiousPermissionMakesWaterExplorationRouteReachable()
    {
        var state = State();
        state.Physical.Board.Terrain.Add(new(new(3, 0), TerrainKind.Water));
        Assert.Equal(MovementPolicyOutcome.Inapplicable, Explore(state).Outcome);
        state.Types[0] = state.Types[0] with { Amphibious = true };
        Assert.Equal(6, new GameplayQueries(state).DistanceToExplorationPositionFrom("scout", new(0, 0)));
        Assert.Equal("2,0", Explore(state).CandidateKey);
    }

    [Fact]
    public void LeapScoringUsesOnlySuppliedCanonicalEndpointsAndPlanningRetainsPattern()
    {
        var state = State(10, 10, new(0, 0), movement: 3);
        state.Types[0] = state.Types[0] with { MovementPattern = MovementPattern.LShape };
        state.ExploredCells["red"] = [new(0, 0), new(2, 1)];
        var request = Moves(state);
        var leap = Assert.Single(request.Candidates);
        Assert.Equal(new Cell(2, 1), leap.Destination);
        Assert.Equal(new[] { new Cell(0, 0), new Cell(2, 1) }, leap.Path);
        var queries = new GameplayQueries(state);
        Assert.Equal(queries.PotentialDiscoveryAlongPath("scout", [new(0, 0), new(2, 1)]),
            queries.PotentialDiscoveryAlongPath("scout", leap.Path!));
        Assert.Equal(leap.Key, AutonomousMovementBehavior.Explore(request, queries).CandidateKey);
    }

    [Fact]
    public void HiddenWorldChangesDoNotChangePathScoresGoalOrChoiceAndSaveRestoresChoice()
    {
        var state = State(position: new(1, 0));
        state.ExploredCells["red"] = Enumerable.Range(0, 7).Select(x => new Cell(x, 0)).ToHashSet();
        var hidden = state.Copy();
        hidden.Physical.Board.Terrain.Add(new(new(9, 0), TerrainKind.SolidRock));
        hidden.Physical.Board.Edges.Add(new(new(10, 0), new(11, 0), EdgeKind.ClosedDoor));
        hidden.Types.Add(new("hidden-type", 0, 0, 0, 0, 1));
        hidden.Units.Add(new("hidden", "hidden-type", "blue", 1));
        hidden.Physical.Figures.Add(new("hidden", new(12, 0)));
        var originalQueries = new GameplayQueries(state); var hiddenQueries = new GameplayQueries(hidden);
        foreach (var candidate in Moves(state).Candidates)
            Assert.Equal(originalQueries.PotentialDiscoveryAlongPath("scout", candidate.Path!),
                hiddenQueries.PotentialDiscoveryAlongPath("scout", candidate.Path!));
        Assert.Equal(originalQueries.DistanceToExplorationPositionFrom("scout", new(1, 0)),
            hiddenQueries.DistanceToExplorationPositionFrom("scout", new(1, 0)));
        Assert.Equal(Explore(state), Explore(hidden));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(hidden))!;
        Assert.Equal(Explore(state), Explore(restored));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(50)]
    public void GeneratedWorldExplorationReportsRepeatableChoicesAndMeasuredMedian(int size)
    {
        var generated = WorldGenerator.Generate(new WorldGenerationRecipe(new(0, 0, size, size), new(), 723));
        var state = GameEngine.CreateGame(generated.Definition);
        var start = Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => new Cell(x, y)))
            .OrderBy(c => Math.Abs(c.X - size / 2) + Math.Abs(c.Y - size / 2))
            .First(c => SpatialRules.Fits(state.Physical.Board, Footprint.OneByOne, c));
        state.Types.Add(new("scout-type", 5, 0, 0, 0, 1, Behaviors: UnitBehavior.ExploreFoW));
        state.Units.Add(new("scout", "scout-type", "red", 1));
        state.Physical.Figures.Add(new("scout", start));
        state.FogOfWarSides = ["red"];
        FogOfWar.Refresh(state);
        var request = Moves(state);
        var choices = new List<string?>();
        var elapsed = new List<double>();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var timer = Stopwatch.StartNew();
            choices.Add(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(state)));
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
        }
        Assert.All(choices, choice => Assert.Equal(choices[0], choice));
        elapsed.Sort();
        output.WriteLine($"Generated {size}x{size}, seed 723: {request.Candidates.Count} legal Move candidates; " +
            $"choice {choices[0] ?? "Stay"}; median {elapsed[1]:F3} ms over 3 cold query snapshots.");
    }
}
