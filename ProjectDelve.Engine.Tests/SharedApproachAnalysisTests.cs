using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SharedApproachAnalysisTests
{
    private static IEnumerable<Cell> Cells(Board board) =>
        Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width).Select(x => new Cell(x, y)));

    private static GameState World(UnitType? actor = null)
    {
        actor ??= new("walker", 1, 1, 1, 0, 1);
        var target = new UnitType("target", 1, 1, 1, 0, 1);
        var token = new ActivationToken(actor.Id, "blue");
        return new()
        {
            Physical = new(new Board(6, 4, []), [new("actor", new(0, 0)), new("enemy", new(5, 0)),
                new("second", new(5, 3)), new("friend", new(3, 1))]),
            Types = [actor, target], Units = [actor.CreateUnit("actor", "blue"), target.CreateUnit("enemy", "red"),
                target.CreateUnit("second", "red"), target.CreateUnit("friend", "blue")],
            Controllers = [new(token, ControllerKind.Automated), new(new(target.Id, "red"), ControllerKind.Human)],
            Objectives = [new("blue"), new("red")],
            ResourceDefinitions = [.. ResourceTypes.All], WorldCardDefinitions = [.. WorldCardCatalog.All]
        };
    }

    private static Cell[] LegalOrigins(GameState state, string actor = "actor") => Cells(state.Physical.Board)
        .Where(c => SpatialRules.CanPlaceUnit(state, FootprintGeometry.FootprintOf(state, actor), c, actor)).ToArray();

    private static void CompareAllOrigins(GameState state)
    {
        var before = JsonSerializer.Serialize(state);
        var actual = new GameplayQueries(state);
        var expected = ReferenceAttackQueries.Create(state);
        string[][] sets = [["enemy"], ["second"], ["enemy", "second"], ["second", "enemy", "second"],
            ["friend"], ["missing"], ["enemy", "missing", "friend"], []];
        // Reverse the second pass to exercise warmed fields independently of origin order.
        var origins = LegalOrigins(state);
        Assert.NotEmpty(origins);
        foreach (var position in origins.Concat(origins.Reverse()))
        foreach (var doors in new[] { false, true })
        {
            Assert.Equal(expected.DistanceToAttackPositionFrom("actor", position, doors),
                actual.DistanceToAttackPositionFrom("actor", position, doors));
            foreach (var targets in sets)
                Assert.Equal(expected.DistanceToAttackPositionFrom("actor", position, targets, doors),
                    actual.DistanceToAttackPositionFrom("actor", position, targets, doors));
        }
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("phase")]
    [InlineData("terrain")]
    [InlineData("category")]
    [InlineData("fog")]
    [InlineData("large")]
    [InlineData("lying")]
    [InlineData("ranged-aura")]
    [InlineData("no-attacks")]
    [InlineData("disconnected")]
    public void EveryLegalOriginMatchesFrozenForwardQuery(string variant)
    {
        var actor = new UnitType("walker", 1, 1, 1, 0, 1);
        if (variant == "phase") actor = actor.WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        if (variant == "terrain") actor = actor with { TerrainRestrictedMovement = new(TerrainKind.Asphalt) };
        if (variant == "category") actor = actor with { TerrainRestriction = new([TerrainCategories.RacingGround]) };
        if (variant == "large") actor = actor with { Footprint = Footprint.TwoByTwo };
        if (variant == "ranged-aura") actor = actor with { Rng = 3, Atk = 0 };
        if (variant == "no-attacks") actor = actor with { Actions = UnitAction.None };
        var state = World(actor);
        var board = state.Physical.Board;
        board.Edges.AddRange([new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor),
            new(new(1, 1), new(2, 1), EdgeKind.Wall), new(new(2, 2), new(3, 2), EdgeKind.OpenDoor)]);
        board.Terrain.Add(new(new(2, 3), TerrainKind.SolidRock));
        if (variant is "terrain" or "category")
            board.Terrain.AddRange(Cells(board).Where(c => c.Y == 0 || c.X == 4).Select(c => new TerrainTile(c, TerrainKind.Asphalt)));
        if (variant == "fog")
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Cells(board).Where(c => c.Y != 2).ToHashSet();
        }
        if (variant == "large")
        {
            state.Physical.Figures[0] = new("actor", new(0, 2));
            // This edge is internal to some 2x2 placements and crossed by others.
            board.Edges.Add(new(new(3, 2), new(4, 2), EdgeKind.ClosedDoor));
        }
        if (variant == "lying") state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        if (variant == "ranged-aura")
        {
            var aura = new UnitType("aura", 0, 0, 0, 0, 1) { LocalAbilities = [new("aura", "Aura", AbilityTiming.Passive,
                new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Atk, 1), new(Stat.Rng, 1)]))] };
            state.Types.Add(aura);
            state.Units[3] = aura.CreateUnit("friend", "blue");
        }
        if (variant == "disconnected")
            board.Edges.AddRange(Enumerable.Range(0, board.Height).Select(y => new Edge(new(3, y), new(4, y), EdgeKind.Wall)));
        CompareAllOrigins(state);
    }

    [Theory]
    [InlineData(MovementPattern.StraightOrthogonal)]
    [InlineData(MovementPattern.DiagonalCorridor)]
    [InlineData(MovementPattern.LShape)]
    [InlineData(MovementPattern.StraightOrthogonalOrDiagonalCorridor)]
    [InlineData(MovementPattern.Adjacent)]
    public void ApproachKeepsCardinalTerrainRoutesIndependentOfMovementPattern(MovementPattern pattern)
    {
        var state = World(new("walker", 0, 1, 1, 0, 1) { MovementPattern = pattern });
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(1, 0)));
        CompareAllOrigins(state);
        var query = new GameplayQueries(state);
        Assert.Equal(4, query.DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        // MOV 0 still supplies no actual move; route analysis has neither that
        // allowance nor the marker's actual-transit restriction.
        Assert.Single(MovementRules.FindPaths(state, "actor", new(0, 0), 0));
    }

    [Fact]
    public void SpecificTargetGoalsRetainPhasePlacementsRejectedByDefaultGoalFilter()
    {
        var actor = new UnitType("walker", 1, 1, 1, 0, 1)
            .WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        var state = World(actor);
        state.Physical = new(new Board(5, 1, []), [new("actor", new(0, 0)), new("enemy", new(4, 0))]);
        state.Units.RemoveAll(u => u.Id is "second" or "friend");
        state.Physical.Board.Terrain.AddRange(Enumerable.Range(1, 3).Select(x => new TerrainTile(new(x, 0), TerrainKind.Lava)));
        var query = new GameplayQueries(state);
        Assert.Equal(3, query.DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        Assert.Null(query.DistanceToAttackPositionFrom("actor", new(0, 0)));
        CompareAllOrigins(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormallyLegalOriginCanDepartUnknownOrRestrictedTerrain(bool unknown)
    {
        var actor = new UnitType("walker", 1, 1, 1, 0, 1)
            { TerrainRestrictedMovement = unknown ? null : new(TerrainKind.Asphalt) };
        var state = World(actor);
        state.Physical = new(new Board(5, 1, []), [new("actor", new(0, 0)), new("enemy", new(4, 0))]);
        state.Units.RemoveAll(u => u.Id is "second" or "friend");
        state.Physical.Board.Terrain.AddRange(Enumerable.Range(1, 4).Select(x => new TerrainTile(new(x, 0), TerrainKind.Asphalt)));
        if (unknown)
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Enumerable.Range(1, 4).Select(x => new Cell(x, 0)).ToHashSet();
        }
        var query = new GameplayQueries(state);
        Assert.Equal(3, query.DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        Assert.Equal(3, query.DistanceToAttackPositionFrom("actor", new(0, 0)));
        CompareAllOrigins(state);
    }

    [Fact]
    public void CapturedScopedDefinitionsDriveApproachAfterRoundTrip()
    {
        var canonicalActor = new UnitType("scoped", 1, 1, 0, 0, 1);
        var target = new UnitType("target", 0, 0, 0, 0, 1);
        var canonical = new ContentBundle { Units = [canonicalActor, target], Terrain = [.. TerrainCatalog.All], Resources = [.. ResourceTypes.All] };
        var effective = ContentComposition.ComposeScopes(canonical,
            new ContentBundle { Units = [canonicalActor with { Atk = 1, Rng = 4 }] },
            new ContentBundle { Units = [canonicalActor with { Atk = 2, Rng = 2 }] });
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 6, Height = 4 }, UnitTypeIds = ["scoped", "target"],
            Units = [new("scoped", "blue", new(0, 0), UnitId: "actor"), new("target", "red", new(5, 0), UnitId: "enemy")],
            Agency = [new("scoped", "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(GameEngine.CreateGame(scenario, effective)))!;
        Assert.Equal(3, new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        CompareAllOrigins(state);
        Assert.Equal(0, canonicalActor.Atk);
    }

    [Fact]
    public void ApproachFieldsSeparateActorsAndIgnoreOccupantsAsRouteObstacles()
    {
        var state = World(new("walker", 1, 3, 1, 0, 1));
        var query = new GameplayQueries(state);
        var origin = new Cell(1, 0);
        Assert.Equal(1, query.DistanceToAttackPositionFrom("actor", origin, ["enemy"]));
        Assert.Equal(3, query.DistanceToAttackPositionFrom("friend", origin, ["enemy"]));
        Assert.Equal(1, query.DistanceToAttackPositionFrom("actor", origin));
        Assert.Equal(3, query.DistanceToAttackPositionFrom("friend", origin));

        state = World();
        state.Physical = new(new Board(6, 1, []), [new("actor", new(0, 0)), new("enemy", new(5, 0)),
            new("friend", new(1, 0)), new("second", new(2, 0))]);
        query = new GameplayQueries(state);
        Assert.Equal(4, query.DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0), 5);
        Assert.DoesNotContain(new Cell(1, 0), paths.Keys); // Friendly transit is not a stopping position.
        Assert.DoesNotContain(new Cell(3, 0), paths.Keys); // Hostile transit blocks actual movement.
        CompareAllOrigins(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseDistancesMatchIndependentForwardGraphOnSeededDirectedBoards(bool large)
    {
        var random = new Random(32091);
        var footprint = large ? Footprint.TwoByTwo : Footprint.OneByOne;
        for (var sample = 0; sample < 16; sample++)
        {
            var board = new Board(5, 4, []);
            foreach (var cell in Cells(board))
            {
                if (random.Next(4) == 0) board.Terrain.Add(new(cell, TerrainKind.Tree));
                else if (random.Next(3) == 0) board.Terrain.Add(new(cell, TerrainKind.Asphalt));
                foreach (var next in MovementRules.Neighbors(cell).Where(c => MovementRules.Inside(board, c) &&
                             (c.X > cell.X || c.Y > cell.Y)))
                    if (random.Next(4) == 0) board.Edges.Add(new(cell, next, random.Next(2) == 0 ? EdgeKind.Wall : EdgeKind.ClosedDoor));
            }
            var known = Cells(board).Where(_ => random.Next(4) != 0).ToHashSet();
            var traversal = new MovementTraversal(IgnoreTerrainAndEdges: sample % 3 == 0,
                ClosedDoorsTraversable: sample % 2 == 0, RequiredTerrain: sample % 4 == 0 ? TerrainKind.Asphalt : null,
                KnownCells: sample % 2 == 0 ? known : null);
            var origins = Cells(board).Where(c => FootprintGeometry.OccupiedCells(footprint, c).All(p => MovementRules.Inside(board, p))).ToArray();
            var goals = origins.Where(_ => random.Next(4) == 0).ToArray();
            var actual = ApproachRules.DistancesTo(board, goals.Concat(goals.Reverse()), traversal, footprint);
            foreach (var origin in origins)
            {
                // Forward exploration deliberately admits its origin regardless of
                // known terrain. Edges are directed because only arrival must fit.
                var distances = new Dictionary<Cell, int> { [origin] = 0 };
                var queue = new Queue<Cell>();
                queue.Enqueue(origin);
                while (queue.TryDequeue(out var current))
                    foreach (var next in MovementRules.Neighbors(current))
                        if (!distances.ContainsKey(next) && MovementRules.CanStep(board, footprint, current, next, traversal))
                        {
                            distances[next] = distances[current] + 1;
                            queue.Enqueue(next);
                        }
                var expected = goals.Where(distances.ContainsKey).Select(c => (int?)distances[c]).Min();
                Assert.Equal(expected, actual.TryGetValue(origin, out var distance) ? distance : (int?)null);
            }
            Assert.Empty(ApproachRules.DistancesTo(board, [], traversal, footprint));
        }
    }

    [Fact]
    public async Task ConcurrentFieldsStayDetachedAcrossTerrainEdgesAndExplorationChanges()
    {
        var state = World();
        state.FogOfWarSides = ["blue"];
        state.ExploredCells["blue"] = Cells(state.Physical.Board).ToHashSet();
        var snapshot = new GameplayQueries(state);
        var reference = ReferenceAttackQueries.Create(state);
        var origins = LegalOrigins(state);
        var expected = origins.Select(c => reference.DistanceToAttackPositionFrom("actor", c, ["enemy"], true)).ToArray();
        // Mutate before any cache is populated, proving the lazy calculation uses
        // the snapshot, not references retained from its mutable input.
        state.Physical.Board.Edges.AddRange(Enumerable.Range(0, 4).Select(y => new Edge(new(1, y), new(2, y), EdgeKind.Wall)));
        state.Physical.Board.Terrain.Add(new(new(2, 2), TerrainKind.Tree));
        state.ExploredCells["blue"] = [new(0, 0)];
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var slot = index % origins.Length;
            Assert.Equal(expected[slot], snapshot.DistanceToAttackPositionFrom("actor", origins[slot], ["enemy"], true));
            Assert.Equal(reference.DistanceToAttackPositionFrom("actor", origins[slot]),
                snapshot.DistanceToAttackPositionFrom("actor", origins[slot]));
        })));
        Assert.NotNull(snapshot.DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0)));
        CompareAllOrigins(state);
    }

    [Fact]
    public void DefaultAndAttackOrderMovementChoicesMatchOracleAndCanonicalTies()
    {
        var state = World();
        var token = state.Controllers[0].Token;
        var request = new DecisionRequest(DecisionKind.Move, state.Types[0].Id, "actor",
            [new("down", new(0, 1), [new(0, 0), new(0, 1)], Kind: ActivationChoiceKind.Move),
             new("right", new(1, 0), [new(0, 0), new(1, 0)], Kind: ActivationChoiceKind.Move)], true) { Token = token };
        var actual = new GameplayQueries(state);
        var reference = ReferenceAttackQueries.Create(state);
        Assert.Equal("right", new DefaultAutomatedProvider().Choose(request, actual));
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, reference), new DefaultAutomatedProvider().Choose(request, actual));
        state.AssignOrder(token, OrderKind.Attack, target: OrderTarget.ForUnit("second"));
        actual = new GameplayQueries(state);
        reference = ReferenceAttackQueries.Create(state);
        Assert.Equal("right", new OrderAwareAutomatedProvider().Choose(request, actual));
        Assert.Equal(new OrderAwareAutomatedProvider().Choose(request, reference), new OrderAwareAutomatedProvider().Choose(request, actual));
    }
}
