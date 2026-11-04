using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AttackQueryReuseTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(Json(state))!;

    private static GameState World(UnitType actor)
    {
        actor = actor with { Unique = actor.Hp > 1 };
        var target = new UnitType("target", 2, 1, 1, 1, 1);
        var aura = new UnitType("aura-type", 0, 0, 0, 0, 1) { LocalAbilities = [
            new("aura", "Aura", AbilityTiming.Passive, new AdjacentUnitsStatModifiersMechanic(
                RelationshipSelector.Friendlies, [new(Stat.Rng, 1), new(Stat.Atk, 1)]))] };
        return new()
        {
            Physical = new(new Board(7, 5, []), [new("actor", new(0, 2)), new("enemy", new(5, 2)),
                new("second", new(4, 3)), new("friend", new(5, 1)), new("aura-one", new(1, 1)), new("aura-two", new(1, 3))]),
            Types = [actor, target, aura],
            Units = [actor.CreateUnit("actor", "blue"), target.CreateUnit("enemy", "red"),
                target.CreateUnit("second", "red"), target.CreateUnit("friend", "blue"),
                aura.CreateUnit("aura-one", "blue"), aura.CreateUnit("aura-two", "blue")],
            EquipmentTypes = [.. EquipmentCatalog.All], ItemTypes = [.. ItemCatalog.All],
            ResourceDefinitions = [.. ResourceTypes.All], WorldCardDefinitions = [.. WorldCardCatalog.All]
        };
    }

    // The same snapshot sees repeated, reordered and overlapping target sets. An
    // aggregate cached for one set must never stand in for another set's answer.
    private static void Compare(GameState state)
    {
        var before = Json(state);
        IGameplayQueries actual = new GameplayQueries(state);
        var expected = ReferenceAttackQueries.Create(state);
        var footprint = FootprintGeometry.FootprintOf(state, "actor");
        var positions = new[] { new Cell(0, 2), new(2, 2), new(3, 1), new(4, 2), new(0, 0) }
            .Where(cell => SpatialRules.CanPlaceUnit(state, footprint, cell, "actor")).ToArray();
        string[][] sets = [["enemy"], ["second"], ["enemy", "second"], ["second", "enemy", "second"], ["friend"], ["missing"], []];
        foreach (var position in positions.Concat(positions.Reverse()))
        {
            foreach (var targets in sets)
            {
                Assert.Equal(expected.CanAttackAnyFrom("actor", position, targets), actual.CanAttackAnyFrom("actor", position, targets));
                Assert.Equal(expected.DistanceToAttackPositionFrom("actor", position, targets), actual.DistanceToAttackPositionFrom("actor", position, targets));
            }
            Assert.Equal(expected.CanAttackHostileFrom("actor", position), actual.CanAttackHostileFrom("actor", position));
            Assert.Equal(expected.DistanceToNearestAttackableHostileFrom("actor", position), actual.DistanceToNearestAttackableHostileFrom("actor", position));
            foreach (var doors in new[] { false, true })
                Assert.Equal(expected.DistanceToAttackPositionFrom("actor", position, doors), actual.DistanceToAttackPositionFrom("actor", position, doors));
        }
        Assert.Equal(before, Json(state));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("ranged")]
    [InlineData("fury")]
    [InlineData("backstab")]
    [InlineData("dragon")]
    [InlineData("fireball")]
    [InlineData("large")]
    [InlineData("large-target")]
    [InlineData("lying")]
    [InlineData("equipment")]
    [InlineData("spent")]
    [InlineData("cost")]
    [InlineData("fog")]
    [InlineData("obstacles")]
    public void RepeatedAttackQueriesMatchIndependentUncachedOracle(string variant)
    {
        var actor = variant switch
        {
            "fury" => UnitType.Barbarian() with { Atk = 0 },
            "backstab" => UnitType.Rogue() with { Atk = 0 },
            "dragon" => UnitType.RedDragon(),
            "fireball" => UnitType.Wizard(),
            "large" => new UnitType("actor-type", 3, 2, 2, 1, 5) { Footprint = Footprint.TwoByTwo },
            "ranged" or "fog" or "obstacles" => new UnitType("actor-type", 3, 4, 2, 1, 5),
            _ => new UnitType("actor-type", 3, 1, 2, 1, 5)
        };
        var state = World(actor);
        if (variant == "large-target")
        {
            var largeTarget = new UnitType("large-target", 0, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo };
            state.Types.Add(largeTarget);
            state.Units[1] = largeTarget.CreateUnit("enemy", "red");
        }
        if (variant == "large") state.Physical.Figures[^1] = state.Physical.Figures[^1] with { Position = new(1, 4) };
        if (variant is "equipment" or "spent" or "cost")
        {
            state.Types[0] = actor with { Actions = UnitAction.None, CanHaveEquipment = true };
            state.EquipmentTypes.Add(new("lens", "Lens") { LocalAbilities = [new("beam", "Beam", AbilityTiming.Action,
                new TargetUnitAttackMechanic(4, 2)) { Usage = new(1), Costs = variant == "cost" ? [new SpendResource(ResourceTypes.Diamond.Id, 1)] : [] }] });
            state.Units[0] = state.Units[0] with { Inventory = [new("physical-lens", "lens")] };
            if (variant == "spent")
            {
                var obj = Assert.Single(state.Units[0].Inventory);
                state.Units[0] = state.Units[0] with { Inventory = [obj with { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("beam", 1) }] };
            }
        }
        if (variant == "lying") state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        
        if (variant == "fog")
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = [new(0, 2), new(1, 2), new(2, 2), new(3, 2), new(4, 2), new(5, 2)];
            Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 2), ["second"]));
        }
        if (variant == "obstacles")
        {
            state.Physical.Board.Edges.AddRange([new(new(2, 1), new(3, 1), EdgeKind.Wall),
                new(new(2, 2), new(3, 2), EdgeKind.ClosedDoor), new(new(2, 3), new(3, 3), EdgeKind.OpenDoor)]);
            state.Physical.Board.Terrain.Add(new(new(3, 0), TerrainKind.SolidRock));
        }
        Compare(state);
        Compare(Restore(state));
    }

    [Fact]
    public void PlacementValidationStillRunsBeforeWarmCacheLookups()
    {
        var state = World(new("actor-type", 3, 2, 1, 0, 1));
        IGameplayQueries actual = new GameplayQueries(state);
        var expected = ReferenceAttackQueries.Create(state);
        Assert.Equal(expected.CanAttackHostileFrom("actor", new(0, 2)), actual.CanAttackHostileFrom("actor", new(0, 2)));
        foreach (var position in new[] { new Cell(-1, 0), new(7, 2), new(5, 2) })
        {
            Assert.Throws<ArgumentException>(() => expected.CanAttackHostileFrom("actor", position));
            Assert.Throws<ArgumentException>(() => actual.CanAttackHostileFrom("actor", position));
        }
    }

    [Fact]
    public void CachedAnswersRemainDetachedAndFreshQueriesSeeMovementDamageEquipmentAndWorldChanges()
    {
        var state = World(new("actor-type", 3, 1, 1, 0, 4) { CanHaveEquipment = true });
        var snapshot = new GameplayQueries(state);
        Assert.True(snapshot.CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(6, 0) };
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        Assert.True(snapshot.CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(5, 2) };
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        Assert.True(snapshot.CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        state.Units[1] = state.Units[1] with { CurrentHp = 1 };
        state.Types[0] = state.Types[0] with { Actions = UnitAction.None };
        state.EquipmentTypes.Add(new("lens", "Lens") { LocalAbilities = [new("beam", "Beam", AbilityTiming.Action, new TargetUnitAttackMechanic(4, 2))] });
        var unequipped = new GameplayQueries(state);
        Assert.False(unequipped.CanAttackHostileFrom("actor", new(4, 2)));
        state.Units[0] = state.Units[0] with { Inventory = [new("lens-one", "lens")] };
        Assert.True(new GameplayQueries(state).CanAttackHostileFrom("actor", new(4, 2)));
        Assert.False(unequipped.CanAttackHostileFrom("actor", new(4, 2)));
        state.Types[0] = state.Types[0] with { Actions = UnitAction.NormalAttack };
        state.Units[0] = state.Units[0] with { Inventory = [] };
        var beforeWorld = new GameplayQueries(state);
        Assert.True(beforeWorld.CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        state.WorldDeck = new() { ActiveContinuous = [new("weakness", WorldEffect.Weakness)] };
        // At this hypothetical placement both auras are out of range.
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
        Assert.True(beforeWorld.CanAttackAnyFrom("actor", new(4, 2), ["enemy"]));
    }

    [Fact]
    public void CapturedScopedContentUsesEffectiveDefinitionsAndSurvivesSerialization()
    {
        var local = new UnitType("scoped", 2, 1, 0, 0, 4) { Unique = true };
        var target = new UnitType("target", 0, 0, 0, 0, 1);
        var canonical = new ContentBundle { Units = [local, target], Terrain = [.. TerrainCatalog.All], Resources = [.. ResourceTypes.All] };
        var workspace = new ContentBundle { Units = [local with { Atk = 1, Rng = 4 }] };
        var scenarioContent = new ContentBundle { Units = [local with { Atk = 2, Rng = 2 }] };
        var effective = ContentComposition.ComposeScopes(canonical, workspace, scenarioContent);
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 5 }, UnitTypeIds = ["scoped", "target"],
            Units = [new("scoped", "blue", new(0, 2), UnitId: "actor"), new("target", "red", new(5, 2), UnitId: "enemy")],
            Agency = [new("scoped", "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = Restore(GameEngine.CreateGame(scenario, effective));
        Assert.Equal(2, state.EffectiveRngOf("actor"));
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(2, 2), ["enemy"]));
        Assert.True(new GameplayQueries(state).CanAttackAnyFrom("actor", new(3, 2), ["enemy"]));
        Compare(state);
        Assert.Equal(0, local.Atk);
    }

    private sealed class SeededDice(int seed) : IRandomProvider
    {
        private readonly Random random = new(seed);
        public List<string> Calls { get; } = [];
        private int Next(string kind, int count)
        {
            var value = random.Next(count);
            Calls.Add($"{kind}:{value}");
            return value;
        }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[Next("token", bag.Count)];
        public AttackFace RollAttackDie() => Next("attack", 6) < 3 ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => Next("defence", 6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
        public int RollD6() => Next("d6", 6) + 1;
        public int RollD8() => Next("d8", 8) + 1;
        public int WorldShuffleIndex(int count) => Next("shuffle", count);
    }

    private sealed class AutomatedChoice(GameState state, bool reference, List<string> trace, IReadOnlyList<string>? replay, int choiceIndex) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries)
        {
            trace.Add(Json(request)); // Includes candidate ordering, not just the selected key.
            var selected = request.Kind == DecisionKind.RollDice ? "roll-dice" :
                new OrderAwareAutomatedProvider().Choose(request, reference ? ReferenceAttackQueries.Create(state) : queries);
            if (replay is not null)
            {
                Assert.Equal(replay[choiceIndex], selected ?? "<none>");
                selected = replay[choiceIndex] == "<none>" ? null : replay[choiceIndex];
            }
            trace.Add(selected ?? "<none>");
            return selected;
        }
    }

    private static (List<string> Trace, List<string> Choices, List<string> Random) Run(int seed, bool reference, bool resume, IReadOnlyList<string>? replay = null)
    {
        var actor = new UnitType("pursuer", 2, 1, 2, 1, 5) { Unique = true, Behaviors = UnitBehavior.None };
        var archer = new UnitType("archer", 2, 3, 2, 1, 4) { Unique = true };
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 3 }, UnitTypeIds = [actor.Id, archer.Id], UnitTypes = [actor, archer],
            Units = [new(actor.Id, "blue", new(0, 1), UnitId: "actor"), new(archer.Id, "red", new(5, 1), UnitId: "enemy")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new(archer.Id, "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var dice = new SeededDice(seed);
        var result = GameEngine.StartRound(GameEngine.CreateGame(scenario), dice, false);
        List<string> trace = [Json(result)];
        List<string> choices = [];
        for (var step = 0; result.NextInput is not null && step < 100; step++)
        {
            var state = resume ? Restore(result.State) : result.State;
            var choiceTrace = new List<string>();
            result = GameEngine.Advance(state, new AutomatedChoice(state, reference, choiceTrace, replay, choices.Count), dice, false);
            Assert.Equal(2, choiceTrace.Count);
            choices.Add(choiceTrace[1]);
            trace.AddRange(choiceTrace);
            trace.Add(Json(result)); // Full events, resolution snapshots and authoritative state.
        }
        Assert.Null(result.NextInput);
        Assert.True(result.State.RoundComplete);
        Assert.Contains(dice.Calls, call => call.StartsWith("attack:", StringComparison.Ordinal));
        return (trace, choices, dice.Calls);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(43)]
    public void CompleteAutomatedDecisionTraceMatchesOracleSaveResumeAndReplay(int seed)
    {
        var optimized = Run(seed, reference: false, resume: false);
        var oracle = Run(seed, reference: true, resume: false);
        var resumed = Run(seed, reference: false, resume: true);
        var replayed = Run(seed, reference: true, resume: true, optimized.Choices);
        foreach (var other in new[] { oracle, resumed, replayed })
        {
            Assert.Equal(optimized.Trace, other.Trace);
            Assert.Equal(optimized.Choices, other.Choices);
            Assert.Equal(optimized.Random, other.Random);
        }
    }

    [Fact]
    public void ReuseSeparatesActorsAtTheSameHypotheticalCell()
    {
        var state = World(new("actor-type", 3, 4, 2, 0, 4));
        var queries = new GameplayQueries(state);
        var position = new Cell(3, 0);
        Assert.True(queries.CanAttackAnyFrom("actor", position, ["enemy"]));
        Assert.False(queries.CanAttackAnyFrom("friend", position, ["enemy"]));
        Assert.True(queries.CanAttackHostileFrom("actor", position));
        Assert.False(queries.CanAttackHostileFrom("friend", position));
        Assert.True(queries.CanAttackAnyFrom("actor", position, ["enemy"]));
    }

    [Fact]
    public async Task ConcurrentReadsMatchUncachedAnswersOnTheSameDetachedSnapshot()
    {
        var state = World(UnitType.Wizard());
        var snapshot = new GameplayQueries(state);
        var reference = ReferenceAttackQueries.Create(state);
        var positions = new[] { new Cell(0, 2), new(2, 2), new(4, 2) };
        static string Answers(IGameplayQueries queries, Cell position) => Json(new
        {
            Pair = queries.CanAttackAnyFrom("actor", position, ["enemy"]),
            Other = queries.CanAttackAnyFrom("actor", position, ["second"]),
            Any = queries.CanAttackHostileFrom("actor", position),
            Nearest = queries.DistanceToNearestAttackableHostileFrom("actor", position),
            Approach = queries.DistanceToAttackPositionFrom("actor", position)
        });
        var expected = positions.Select(position => Answers(reference, position)).ToArray();
        var tasks = Enumerable.Range(0, 24).Select(index => Task.Run(() =>
        {
            var slot = index % positions.Length;
            Assert.Equal(expected[slot], Answers(snapshot, positions[slot]));
        }));
        await Task.WhenAll(tasks);
    }

    [Theory]
    [InlineData("posture")]
    [InlineData("exploration")]
    [InlineData("summon")]
    [InlineData("door")]
    public void DetachedAnswersDoNotLeakAcrossFurtherWorldSnapshots(string change)
    {
        var actor = new UnitType("actor-type", 1, 4, 1, 0, 1);
        var target = new UnitType("target", 0, 1, 1, 0, 1);
        var state = new GameState
        {
            Physical = new(new Board(5, 1, []), [new("actor", new(0, 0)), new("enemy", new(4, 0))]),
            Types = [actor, target], Units = [actor.CreateUnit("actor", "blue"), target.CreateUnit("enemy", "red")],
            FogOfWarSides = ["blue"], ExploredCells = new() { ["blue"] = Enumerable.Range(0, 5).Select(x => new Cell(x, 0)).ToHashSet() }
        };
        var snapshot = new GameplayQueries(state);
        Assert.True(snapshot.CanAttackAnyFrom("actor", new(0, 0), ["enemy"]));
        switch (change)
        {
            case "posture": state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying }; break;
            case "exploration": state.ExploredCells["blue"] = [new(0, 0)]; break;
            case "summon":
                state.Units.Add(target.CreateUnit("new-hostile", "red"));
                state.Physical.Figures.Add(new("new-hostile", new(2, 0)));
                break;
            case "door": state.Physical.Board.Edges.Add(new(new(2, 0), new(3, 0), EdgeKind.ClosedDoor)); break;
        }
        var current = new GameplayQueries(state);
        Assert.False(current.CanAttackAnyFrom("actor", new(0, 0), ["enemy"]));
        Assert.True(snapshot.CanAttackAnyFrom("actor", new(0, 0), ["enemy"]));
        Assert.Equal(ReferenceAttackQueries.Create(state).CanAttackAnyFrom("actor", new(0, 0), ["enemy"]),
            current.CanAttackAnyFrom("actor", new(0, 0), ["enemy"]));
    }
}
