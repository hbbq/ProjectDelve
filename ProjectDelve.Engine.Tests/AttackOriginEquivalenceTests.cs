using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AttackOriginEquivalenceTests
{
    private static IEnumerable<Cell> Cells(Board board) => Enumerable.Range(0, board.Height)
        .SelectMany(y => Enumerable.Range(0, board.Width).Select(x => new Cell(x, y)));

    private static GameState World(UnitType? actor = null)
    {
        actor ??= new("local-actor", 2, 1, 1, 0, 3);
        var target = new UnitType("local-target", 1, 1, 1, 0, 2);
        return new()
        {
            Physical = new(new Board(8, 5, []), [new("actor", new(0, 0)), new("enemy", new(6, 0)),
                new("second", new(6, 3)), new("friend", new(1, 3))]),
            Types = [actor, target],
            Units = [actor.CreateUnit("actor", "blue"), target.CreateUnit("enemy", "red"),
                target.CreateUnit("second", "red"), target.CreateUnit("friend", "blue")],
            Controllers = [new(new(actor.Id, "blue"), ControllerKind.Automated),
                new(new(target.Id, "red"), ControllerKind.Human)],
            Objectives = [new("blue"), new("red")],
            ResourceDefinitions = [.. ResourceTypes.All], WorldCardDefinitions = [.. WorldCardCatalog.All]
        };
    }

    private static void Compare(GameState state)
    {
        var before = JsonSerializer.Serialize(state);
        var query = new GameplayQueries(state);
        var reference = ReferenceAttackQueries.Create(state);
        var all = query.AttackOriginCandidates("actor").ToArray();
        Assert.Equal(all.OrderBy(c => c.Y).ThenBy(c => c.X), all);
        Assert.Equal(all.Length, all.Distinct().Count());
        var known = query.KnownHostileUnitIds("actor");
        foreach (var target in known)
        {
            var bounded = query.AttackOriginCandidates("actor", target, targeted: true).ToHashSet();
            // Omission proof uses every in-board footprint anchor, including figure-
            // occupied anchors that approach accepts but actual Move choices reject.
            foreach (var origin in Cells(state.Physical.Board).Where(c =>
                         FootprintGeometry.OccupiedCells(state, "actor", c).All(p => MovementRules.Inside(state.Physical.Board, p))))
            {
                var legal = AttackRules.EvaluateApproachFrom(state, "actor", origin, target) == UnitTargetEvaluation.Possible ||
                    state.AbilitiesOf("actor").Where(a => a.Definition.Mechanic is ClawAttack or FireBreath or TargetUnitAttackMechanic)
                        .Any(a => AttackRules.EvaluateAbilityFrom(state, "actor", origin, target, a) == UnitTargetEvaluation.Possible);
                if (legal)
                {
                    Assert.Contains(origin, bounded);
                    Assert.Contains(origin, all);
                }
            }
        }
        var placements = Cells(state.Physical.Board).Where(c => SpatialRules.CanPlaceUnit(state,
            FootprintGeometry.FootprintOf(state, "actor"), c, "actor")).ToArray();
        Assert.NotEmpty(placements);
        // Independent forward exhaustive search checks the final field, not just
        // geometric membership; repeat warmed queries in reverse origin order.
        var samples = new[] { placements[0], placements[placements.Length / 2], placements[^1] }.Distinct().ToArray();
        string[][] sets = [["enemy"], ["second"], ["second", "enemy", "second"], ["friend", "missing"], []];
        foreach (var origin in samples.Concat(samples.Reverse()))
        foreach (var closedDoors in new[] { false, true })
        {
            Assert.Equal(reference.DistanceToAttackPositionFrom("actor", origin, closedDoors),
                query.DistanceToAttackPositionFrom("actor", origin, closedDoors));
            foreach (var targets in sets)
                Assert.Equal(reference.DistanceToAttackPositionFrom("actor", origin, targets, closedDoors),
                    query.DistanceToAttackPositionFrom("actor", origin, targets, closedDoors));
        }
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("ranged")]
    [InlineData("large")]
    [InlineData("claw")]
    [InlineData("breath")]
    [InlineData("fixed")]
    [InlineData("fury")]
    [InlineData("backstab")]
    [InlineData("fireball")]
    [InlineData("fog")]
    [InlineData("unreachable")]
    [InlineData("phase")]
    public void BoundedOriginsAndApproachMatchExhaustiveEvaluation(string variant)
    {
        var actor = new UnitType("local-actor", 2, variant == "ranged" ? 3 : 1, variant == "fury" ? 0 : 1, 0, 3);
        if (variant == "large") actor = actor with { Footprint = Footprint.TwoByTwo };
        AbilityMechanic? mechanic = variant switch
        {
            "claw" => new ClawAttack(), "breath" => new FireBreath(),
            "fixed" => new TargetUnitAttackMechanic(4, 2), "fury" => new Fury { AdjacentEnemyThreshold = 1 },
            "backstab" => new Backstab(), "fireball" => new FireballMechanic(), "phase" => new Phase(), _ => null
        };
        if (mechanic is not null)
            actor = actor.WithLocal(new("local", "Local Ability", mechanic is Fury or Backstab or Phase
                ? AbilityTiming.Passive : AbilityTiming.Action, mechanic));
        if (variant is "claw" or "breath" or "fixed") actor = actor with { Actions = UnitAction.None };
        var state = World(actor);
        if (variant == "large") state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        var board = state.Physical.Board;
        board.Edges.AddRange([new(new(2, 0), new(3, 0), EdgeKind.ClosedDoor),
            new(new(2, 1), new(3, 1), EdgeKind.Wall)]);
        board.Terrain.Add(new(new(3, 3), TerrainKind.Tree));
        if (variant == "unreachable")
            board.Edges.AddRange(Enumerable.Range(0, board.Height).Select(y => new Edge(new(4, y), new(5, y), EdgeKind.Wall)));
        if (variant == "phase") board.Terrain.Add(new(new(5, 0), TerrainKind.Lava));
        if (variant == "fog")
        {
            state.Physical.Figures[1] = new("enemy", new(3, 0));
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Cells(board).Where(c => c != new Cell(6, 3)).ToHashSet();
            Assert.False(FogOfWar.IsUnitObservable(state, "blue", "second"));
            Assert.Empty(new GameplayQueries(state).AttackOriginCandidates("actor", "second", targeted: true));
        }
        if (variant == "fury")
        {
            Assert.Equal(0, state.EffectiveAtkOf("actor"));
            Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateApproachFrom(state, "actor", new(5, 0), "enemy"));
        }
        Compare(state);
    }

    [Fact]
    public void MeleeIncludesDiagonalOccupiedPairsAndRemainsBounded()
    {
        var state = World(new("local-actor", 2, 1, 1, 0, 3) { Footprint = Footprint.TwoByTwo });
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        var candidates = new GameplayQueries(state).AttackOriginCandidates("actor", "enemy", targeted: true).ToArray();
        // Actor's bottom-right Cell (5,2) is diagonal to target's (6,1).
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateApproachFrom(state, "actor", new(4, 2), "enemy"));
        Assert.Contains(new Cell(4, 2), candidates);
        Assert.DoesNotContain(new Cell(0, 0), candidates);
        Assert.True(candidates.Length < state.Physical.Board.Width * state.Physical.Board.Height);
        Compare(state);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, -2)]
    public void PositionalRangeAurasFallBackIncludingEquipmentAndNegativeAuras(bool equipment, int amount)
    {
        var state = World(new("local-actor", 2, amount < 0 ? 4 : 1, 1, 0, 3));
        var aura = new LocalAbilityDefinition("range-aura", "Range Aura", AbilityTiming.Passive,
            new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Rng, amount)]));
        if (equipment)
        {
            state.EquipmentTypes.Add(new("local-aura-equipment", "Local Aura") { LocalAbilities = [aura] });
            state.Units[3] = state.Units[3] with { Inventory = [new("physical-aura", "local-aura-equipment")] };
        }
        else state.Types.Add(new("aura-source", 1, 0, 0, 0, 1) { LocalAbilities = [aura] });
        if (!equipment) state.Units[3] = state.Types[^1].CreateUnit("friend", "blue");
        state.Physical.Figures[3] = new("friend", new(1, 0));
        Assert.Equal(Cells(state.Physical.Board), new GameplayQueries(state).AttackOriginCandidates("actor"));
        Compare(state);
    }

    [Fact]
    public void IndirectFireballUsesExhaustiveOrigins()
    {
        var state = World(new UnitType("local-actor", 2, 2, 1, 0, 3, UnitAction.None)
            .WithLocal(new("cast", "Local Fireball", AbilityTiming.Action, new FireballMechanic())));
        Assert.Equal(Cells(state.Physical.Board), new GameplayQueries(state).AttackOriginCandidates("actor"));
        Compare(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquipmentAttackGrantsRetainPerPhysicalSourceUsage(bool exhausted)
    {
        var attack = new LocalAbilityDefinition("shoot", "Local Attack", AbilityTiming.Action, new TargetUnitAttackMechanic(4, 2))
            { Usage = new(1) };
        var state = World(new("local-actor", 2, 0, 0, 0, 3, UnitAction.None));
        state.EquipmentTypes.Add(new("local-launcher", "Local Launcher") { LocalAbilities = [attack] });
        state.Units[0] = state.Units[0] with { Inventory = [new("physical-launcher", "local-launcher")
        {
            AbilityUseCounts = exhausted ? ImmutableDictionary<string, int>.Empty.Add("shoot", 1) : ImmutableDictionary<string, int>.Empty
        }] };
        Assert.Equal(!exhausted, new GameplayQueries(state).CanAttackAnyFrom("actor", new(3, 0), ["enemy"]));
        Compare(state);
    }

    [Fact]
    public void LazyOriginContextsUseDetachedLocalDefinitionsAndKnowledge()
    {
        var state = World(new("local-actor", 2, 3, 1, 0, 3));
        var snapshot = new GameplayQueries(state);
        state.Types[0] = state.Types[0] with { Rng = 0 };
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        Assert.NotEmpty(snapshot.AttackOriginCandidates("actor", "enemy", targeted: true));
        Assert.Empty(new GameplayQueries(state).AttackOriginCandidates("actor", "enemy", targeted: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClawAndLargerFixedRangeGrantKeepTheirIndependentGeometryAndUses(bool exhausted)
    {
        var actor = new UnitType("local-actor", 2, 0, 1, 0, 3, UnitAction.None)
            .WithLocal(new("claw", "Local Claw", AbilityTiming.Action, new ClawAttack()))
            .WithLocal(new("shoot", "Local Shot", AbilityTiming.Action, new TargetUnitAttackMechanic(4, 2)) { Usage = new(1) });
        var state = World(actor);
        if (exhausted) state.Units[0] = state.Units[0] with
            { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("unit:shoot", 1) };
        var query = new GameplayQueries(state);
        var diagonal = new Cell(5, 1);
        var distant = new Cell(2, 0);
        Assert.Contains(diagonal, query.AttackOriginCandidates("actor", "enemy", targeted: true));
        Assert.True(query.CanAttackAnyFrom("actor", diagonal, ["enemy"]));
        var claw = state.AbilitiesOf("actor").Single(a => a.Key == "claw");
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateAbilityFrom(state, "actor", diagonal, "enemy", claw));
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateAbilityFrom(state, "actor", distant, "enemy", claw));
        Assert.Equal(!exhausted, query.CanAttackAnyFrom("actor", distant, ["enemy"]));
        Compare(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneralAttackGoalsUseReceivingTypeWaterOccupancy(bool amphibious)
    {
        var actor = new UnitType("local-actor", 2, 1, 1, 0, 3) { Amphibious = amphibious };
        var state = World(actor);
        state.Physical = new(new Board(6, 1, []), [new("actor", new(0, 0)), new("enemy", new(5, 0))]);
        state.Units.RemoveAll(u => u.Id is "second" or "friend");
        state.Physical.Board.Terrain.AddRange(Enumerable.Range(1, 4).Select(x => new TerrainTile(new(x, 0), TerrainKind.Water)));
        var query = new GameplayQueries(state);
        var board = state.Physical.Board;
        foreach (var origin in Cells(board).Where(c => SpatialRules.CanPlaceUnit(state, actor, c, "actor")))
        foreach (var closedDoors in new[] { false, true })
        {
            // Independent exhaustive forward oracle preserves the original general
            // goal filter, including Amphibious receiving-Type occupancy. The older
            // frozen ReferenceAttackQueries predates that distinction.
            var expected = ApproachRules.Distances(board, origin,
                    traversal: MovementTraversal.For(state, "actor", closedDoors), footprint: actor.Footprint)
                .Where(pair => SpatialRules.Fits(board, actor.Footprint, pair.Key, MovementTraversal.OccupancyFor(actor)) &&
                    AttackRules.EvaluateApproachFrom(state, "actor", pair.Key, "enemy") == UnitTargetEvaluation.Possible)
                .Select(pair => (int?)pair.Value).Min();
            Assert.Equal(expected, query.DistanceToAttackPositionFrom("actor", origin, closedDoors));
        }
        Assert.Equal(amphibious ? 4 : (int?)null, query.DistanceToAttackPositionFrom("actor", new(0, 0)));
        if (amphibious)
        {
            Assert.Contains(new Cell(4, 0), query.AttackOriginCandidates("actor"));
            Assert.Equal(0, query.DistanceToAttackPositionFrom("actor", new(4, 0)));
        }
    }

    [Fact]
    public void NoKnownHostileSuppliesNoAttackOriginsOrApproachGoals()
    {
        var state = World();
        state.Units.RemoveAll(u => u.SideId == "red");
        state.Physical.Figures.RemoveAll(f => f.Id is "enemy" or "second");
        var query = new GameplayQueries(state);
        Assert.Empty(query.AttackOriginCandidates("actor"));
        Assert.Null(query.DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.False(query.CanAttackHostileFrom("actor", new(0, 0)));
    }
}
