using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FootprintTests
{
    private static UnitType Large(string id = "large") => new(id, 3, 1, 2, 1, 1) { Footprint = Footprint.TwoByTwo };
    private static GameState World(UnitType? actor = null, Cell? anchor = null, int width = 8, int height = 6)
    {
        actor ??= Large();
        return new()
        {
            Physical = new(new Board(width, height, []), [new("actor", anchor ?? new(1, 1))]),
            Types = [actor], Units = [actor.CreateUnit("actor", "blue")]
        };
    }
    private static void Add(GameState state, string id, UnitType type, Cell anchor, string side = "red", Posture posture = Posture.Upright)
    {
        if (!state.Types.Any(t => t.Id == type.Id)) state.Types.Add(type);
        state.Units.Add(type.CreateUnit(id, side));
        state.Physical.Figures.Add(new(id, anchor, posture));
    }
    private static List<Candidate> Actions(GameState state)
    {
        state.MoveDone = true;
        return GameEngine.GameplayCandidates(state, state.Units.Single(u => u.Id == "actor")).ToList();
    }
    private sealed class Dice : IRandomProvider
    {
        public int AttackRolls, DefenceRolls;
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() { DefenceRolls++; return DefenceFace.Miss; }
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static EngineResult Act(GameState state, string key, Dice? dice = null)
    {
        state.Round = 1; state.ActiveTypeId = state.Units[0].TypeId; state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, state.ActiveTypeId, "actor", [], false);
        return GameEngine.Advance(state, new Choice(key), dice ?? new Dice(), false);
    }

    [Fact]
    public void FootprintsHaveExactlyTheDeclaredTopLeftCellsAndRoundTrip()
    {
        Assert.Equal([new Cell(3, 4)], FootprintGeometry.OccupiedCells(Footprint.OneByOne, new(3, 4)));
        Assert.Equal([new Cell(3, 4), new(4, 4), new(3, 5), new(4, 5)],
            FootprintGeometry.OccupiedCells(Footprint.TwoByTwo, new(3, 4)));
        var state = World();
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(FootprintGeometry.OccupiedCells(state, "actor"), FootprintGeometry.OccupiedCells(restored, "actor"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FootprintGeometry.OccupiedCells((Footprint)9, new(0, 0)));
    }

    [Theory]
    [InlineData(-1, 1)] [InlineData(1, -1)] [InlineData(7, 1)] [InlineData(1, 5)]
    public void CompleteBoundaryIsCheckedAtSetupRuntimeAndHypotheticalPlacement(int x, int y)
    {
        var state = World(anchor: new(x, y));
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice(), false));
        var valid = World();
        Assert.Throws<ArgumentException>(() => valid.PlaceUnit("large", "other", "red", new(x, y)));
        Assert.Throws<ArgumentException>(() => MovementRules.FindPaths(valid, "actor", new(x, y)));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)] [InlineData(TerrainKind.Water)] [InlineData(TerrainKind.StoneFloorWithTable)]
    public void NonAnchorTerrainRejectsPlacementEvenWithPhase(TerrainKind terrain)
    {
        var state = World(Large() with { Phase = new() });
        state.Physical.Board.Terrain.Add(new(new(2, 2), terrain));
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice()));
        Assert.False(SpatialRules.CanPlaceUnit(state, Footprint.TwoByTwo, new(1, 1), "actor"));
        Assert.Throws<ArgumentException>(() => HypotheticalPosition.Validate(state, "actor", new(1, 1)));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)] [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.WallWithWindow, false)] [InlineData(EdgeKind.OpenDoor, true)]
    public void AllInternalEdgesUseNormalPlacementPassability(EdgeKind kind, bool legal)
    {
        foreach (var edge in new[] { (new Cell(1, 1), new Cell(2, 1)), (new Cell(1, 2), new Cell(2, 2)),
                     (new Cell(1, 1), new Cell(1, 2)), (new Cell(2, 1), new Cell(2, 2)) })
        {
            var state = World();
            state.Physical.Board.Edges.Add(new(edge.Item1, edge.Item2, kind));
            Assert.Equal(legal, SpatialRules.CanPlaceUnit(state, Footprint.TwoByTwo, new(1, 1), "actor"));
            if (legal) GameEngine.StartRound(state, new Dice(), false);
            else Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice(), false));
        }
    }

    [Fact]
    public void DifferingAnchorsCannotOverlapAndLyingRetainsAllCells()
    {
        var state = World();
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal("actor", FootprintGeometry.UnitAtCell(state, new(2, 2)));
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("large", "other", "red", new(2, 2)));
        Add(state, "other", new("small", 0, 0, 0, 0, 1), new(2, 2));
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice(), false));
    }

    [Theory]
    [InlineData(0, -1)] [InlineData(-1, 0)] [InlineData(1, 0)] [InlineData(0, 1)]
    public void OneStepTranslatesTheWholeFootprintInEveryDirection(int dx, int dy)
    {
        var state = World();
        var to = new Cell(1 + dx, 1 + dy);
        Assert.Equal([new Cell(1, 1), to], MovementRules.FindPaths(state, "actor", new(1, 1), 1)[to]);
        Assert.Equal(4, FootprintGeometry.OccupiedCells(state, "actor", to).Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NarrowOpeningRequiresAFittingRouteForMovementAndApproach(bool broadRoute)
    {
        var state = World(anchor: new(0, 1), width: 7, height: 6);
        Add(state, "target", new("target", 0, 0, 0, 0, 1), new(6, 1));
        for (var y = 0; y < 6; y++)
            if (y != 1 && !(broadRoute && y >= 4)) state.Physical.Board.Edges.Add(new(new(2, y), new(3, y), EdgeKind.Wall));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 1));
        var queries = new GameplayQueries(state);
        if (broadRoute)
        {
            Assert.Contains(new Cell(4, 1), paths.Keys);
            Assert.Contains(new Cell(2, 4), paths[new(4, 1)]);
            Assert.NotNull(queries.DistanceToAttackPositionFrom("actor", new(0, 1)));
        }
        else
        {
            Assert.DoesNotContain(new Cell(4, 1), paths.Keys);
            Assert.Null(queries.DistanceToAttackPositionFrom("actor", new(0, 1)));
        }
        // The exact same opening admits an ordinary 1x1 mover.
        state.Types[0] = state.Types[0] with { Footprint = Footprint.OneByOne };
        Assert.Contains(new Cell(4, 1), MovementRules.FindPaths(state, "actor", new(0, 1)).Keys);
    }

    [Fact]
    public void NonAnchorCrossedEdgeBlocksAndCanonicalPathsRemainDeterministic()
    {
        var state = World();
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall));
        Assert.DoesNotContain(new Cell(2, 1), MovementRules.FindPaths(state, "actor", new(1, 1), 1).Keys);
        var all = MovementRules.FindPaths(state, "actor", new(1, 1));
        var bounded = MovementRules.FindPaths(state, "actor", new(1, 1), 5);
        foreach (var pair in bounded) Assert.Equal(all[pair.Key], pair.Value);
        Assert.Equal([new Cell(1, 1), new(1, 0), new(2, 0), new(3, 0)], all[new(3, 0)]);
    }

    [Theory]
    [InlineData("blue", true)] [InlineData("red", false)]
    public void FriendlyFootprintsCanBeTraversedButHostilesBlockAndNeitherCanBeDestinations(string side, bool through)
    {
        var state = World(anchor: new(0, 0), width: 8, height: 2);
        Add(state, "blocker", Large("blocker"), new(2, 0), side, Posture.Lying);
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.DoesNotContain(new Cell(1, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(3, 0), paths.Keys);
        Assert.Equal(through, paths.ContainsKey(new(4, 0)));
        Assert.Throws<ArgumentException>(() => HypotheticalPosition.Validate(state, "actor", new(1, 0)));
    }

    [Fact]
    public void PhaseTraversesWholeFootprintButStopsOnlyOnNormalLegalPlacements()
    {
        var state = World(Large() with { Phase = new() }, new(0, 0), 8, 2);
        state.Physical.Board.Terrain.Add(new(new(2, 1), TerrainKind.Tree));
        state.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.Wall));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(5, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(1, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(3, 0), paths.Keys); // Internal Wall at stopping placement.
        Assert.DoesNotContain(new Cell(7, 0), paths.Keys);
        Add(state, "hostile", new("small", 0, 0, 0, 0, 1), new(5, 1));
        Assert.DoesNotContain(new Cell(6, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
    }

    [Theory]
    [InlineData(Footprint.OneByOne, Footprint.TwoByTwo)]
    [InlineData(Footprint.TwoByTwo, Footprint.OneByOne)]
    [InlineData(Footprint.TwoByTwo, Footprint.TwoByTwo)]
    public void MeleeAndRangedTargetingUseOccupiedPairs(Footprint source, Footprint target)
    {
        var state = World(Large() with { Footprint = source }, new(0, 0));
        var targetAnchor = new Cell(source == Footprint.TwoByTwo ? 2 : 1, 1);
        Add(state, "target", Large("target") with { Footprint = target }, targetAnchor);
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(4, 0) };
        state.Types[0] = state.Types[0] with { Rng = source == Footprint.TwoByTwo ? 3 : 4 };
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
        Assert.Equal(source == Footprint.TwoByTwo ? 3 : 4, new GameplayQueries(state).ManhattanDistanceBetweenUnits("actor", "target"));
    }

    [Fact]
    public void RangeAndLosCannotBeCombinedAcrossDifferentCellPairsIncludingFireballCast()
    {
        var state = World(Large() with { Rng = 2, Actions = UnitAction.NormalAttack | UnitAction.Telekinesis | UnitAction.Fireball,
            Fireball = new() }, new(0, 0));
        Add(state, "target", new("small", 0, 0, 0, 0, 1), new(3, 0));
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "actor", new(1, 0), new(3, 0)));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", new(1, 1), new(3, 0)));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", "target"));
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
        Assert.DoesNotContain(Actions(state), c => c.Key is "attack:target" or "telekinesis:target" or "fireball:3,0");
        state.Types[0] = state.Types[0] with { Rng = 3 };
        Assert.Contains(Actions(state), c => c.Key == "attack:target");
        Assert.Contains(Actions(state), c => c.Key == "telekinesis:target");
        Assert.Contains(Actions(state), c => c.Key == "fireball:3,0");
    }

    [Fact]
    public void NonAnchorHostileInteriorBlocksButCompleteSourceAndTargetAreExempt()
    {
        var state = World(Large() with { Rng = 7 }, new(0, 2));
        Add(state, "target", Large("target"), new(5, 2));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", new(0, 3), new(6, 3), "target"));
        Add(state, "blocker", Large("blocker"), new(2, 2));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "actor", new(0, 3), new(6, 3), "target"));
        state.Units[2] = state.Units[2] with { SideId = "blue" };
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", new(0, 3), new(6, 3), "target"));
        // Fireball target-Cell LOS exempts every Cell of its endpoint occupant.
        state.Units[2] = state.Units[2] with { SideId = "red" };
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", new(1, 3), new(3, 3)));
    }

    [Fact]
    public void TwoLargeUnitsCannotCombineBlockedShortPairWithVisibleLongPair()
    {
        var state = World(Large() with { Rng = 3 }, new(0, 0));
        Add(state, "target", Large("target"), new(3, 2));
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 2), EdgeKind.Wall));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "actor", new(1, 1), new(3, 2), "target"));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", new(1, 1), new(4, 2), "target"));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "actor", "target"));
        Assert.Equal(3, new GameplayQueries(state).ManhattanDistanceBetweenUnits("actor", "target"));
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
        state.Types[0] = state.Types[0] with { Rng = 4 };
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
    }

    [Fact]
    public void FuryBackstabAndAuraCountUnitsOnceAndNeverSelf()
    {
        var state = World(Large() with { Fury = new(), Backstab = new(), AdjacentFriendlyUnitsDefenceBonus = new(1) });
        Add(state, "target", Large("target"), new(3, 1));
        Assert.False(SpatialRules.AreAdjacent(state, "actor", "actor"));
        Assert.True(SpatialRules.AreAdjacent(state, "actor", "target"));
        Assert.Equal(2, state.EffectiveAtkOf("actor")); // One large enemy does not meet threshold 2.
        Assert.Equal(1, state.EffectiveDefOf("actor")); // No self-Aura.
        Add(state, "support", Large("support") with { AdjacentFriendlyUnitsDefenceBonus = new(1) }, new(3, 3), "blue");
        Assert.Equal(2, state.EffectiveDefOf("actor"));
        Assert.Equal(3, state.EffectiveAtkAgainst("actor", "target"));
        Add(state, "second", new("small", 0, 0, 0, 0, 1), new(0, 2));
        Assert.Equal(3, state.EffectiveAtkOf("actor"));
    }

    [Fact]
    public void HealHolyWaveAndCleaveProduceOneTargetPerLargeUnit()
    {
        var type = Large() with { Actions = UnitAction.Heal | UnitAction.HolyWave, Heal = new(), HolyWave = new(), Cleave = new() };
        var state = World(type);
        Add(state, "friend", Large("friend") with { Hp = 4, Unique = true }, new(3, 1), "blue");
        state.Units[1] = state.Units[1] with { CurrentHp = 1 };
        Add(state, "enemy", Large("enemy"), new(1, 3));
        Assert.Single(Actions(state).Where(c => c.TargetId == "friend"));
        Assert.Equal(new[] { "enemy" }, Actions(state).Single(c => c.Key == "holy-wave").TargetIds.ToArray());
        var healed = Act(state, "heal:friend");
        Assert.Equal(3, healed.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Single(healed.Events.Where(e => e.Kind == "HealResolved"));
        var waved = Act(state, "holy-wave");
        Assert.Single(waved.Events.Where(e => e.Kind == "PostureChanged" && e.UnitId == "enemy"));
        Assert.Equal(4, FootprintGeometry.OccupiedCells(waved.State, "enemy").Count);
        state.CleavePending = true;
        var cleaved = Act(state, "cleave:enemy");
        Assert.Single(cleaved.Events.Where(e => e.Kind == "CleaveResolved"));
        Assert.DoesNotContain(cleaved.State.Physical.Figures, f => f.Id == "enemy");
    }

    [Fact]
    public void FireballIntersectionsSnapshotDefenceAndResolveDamageAndFollowupsOnce()
    {
        var state = World(Large() with { Rng = 6, Atk = 2, Actions = UnitAction.Fireball, Fireball = new(),
            Cleave = new(), MoveAfterAttack = new(1) }, new(0, 0));
        Add(state, "target", Large("target") with { Hp = 8, Unique = true, Def = 3 }, new(2, 0));
        var candidate = Actions(state).Single(c => c.Key == "fireball:3,0");
        Assert.Equal(new[] { "target" }, candidate.TargetIds.ToArray()); // Four intersecting Cells, one target.
        var dice = new Dice();
        var result = Act(state, candidate.Key, dice);
        Assert.Equal(2, dice.AttackRolls);
        Assert.Equal(3, dice.DefenceRolls);
        var attack = result.Events.Single(e => e.Kind == "AttackResolved").Attack!;
        Assert.Single(attack.Targets);
        Assert.Equal(3, attack.Targets[0].DefenceDice);
        Assert.Equal(6, result.State.Units.Single(u => u.Id == "target").CurrentHp);
        Assert.Equal(1, result.State.MoveAfterAttackAllowance);
        Assert.True(result.State.CleavePending);
        Assert.Single(result.NextInput!.Candidates.Where(c => c.TargetId == "target"));
        Assert.Equal(1, result.State.Units[0].FireballUses!.RemainingUses);
    }

    [Fact]
    public void FireballUsesNonAnchorIntersectionAndGeometricExplosionLos()
    {
        var state = World(Large() with { Rng = 6, Actions = UnitAction.Fireball, Fireball = new() }, new(6, 0));
        Add(state, "target", Large("target"), new(4, 2));
        Assert.Contains("target", Actions(state).Single(c => c.Key == "fireball:6,3").TargetIds);
        state.Physical.Board.Edges.Add(new(new(5, 2), new(6, 2), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(5, 3), new(6, 3), EdgeKind.Wall));
        Assert.DoesNotContain("target", Actions(state).Single(c => c.Key == "fireball:6,3").TargetIds);
    }

    [Fact]
    public void HypotheticalAttackPassivesUseRelocatedFootprintWithoutChangingWorld()
    {
        var state = World(Large() with { Atk = 0, Fury = new() }, new(0, 0));
        Add(state, "target", new("small", 0, 0, 0, 0, 1), new(4, 1));
        Add(state, "other", new("small", 0, 0, 0, 0, 1), new(4, 2));
        Assert.Equal(0, state.EffectiveAtkOf("actor"));
        var before = JsonSerializer.Serialize(state);
        Assert.True(new GameplayQueries(state).CanAttackHostileFrom("actor", new(2, 1)));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void FleeUsesRealMoveFootprintButRanksOneCellRoutesAndBothHostileFootprints()
    {
        var state = World(Large() with { Actions = UnitAction.None, Behaviors = UnitBehavior.Flee }, new(0, 1), 8, 5);
        Add(state, "hostile", Large("hostile"), new(6, 1));
        for (var y = 0; y < 5; y++)
            if (y != 1) state.Physical.Board.Edges.Add(new(new(2, y), new(3, y), EdgeKind.Wall));
        var queries = new GameplayQueries(state);
        Assert.Equal(5, queries.DistanceToNearestHostileFrom("actor", new(0, 1)));
        Assert.Equal(4, queries.DistanceToNearestHostileFrom("actor", new(1, 1)));
        Assert.DoesNotContain(new Cell(4, 1), MovementRules.FindPaths(state, "actor", new(0, 1)).Keys);
        var candidates = GameEngine.GameplayCandidates(state, state.Units[0]).ToList();
        var request = new DecisionRequest(DecisionKind.Move, "large", "actor", candidates, true);
        var chosen = new DefaultMonsterProvider().Choose(request, queries);
        // Moving down increases the nearest Cell-pair route distance; the chosen footprint stays west.
        Assert.NotNull(chosen);
        var move = candidates.Single(c => c.Key == chosen);
        Assert.True(move.Destination!.X <= 1);
        Assert.True(queries.DistanceToNearestHostileFrom("actor", move.Destination) > 5);
        Assert.All(move.Path!, anchor => Assert.True(SpatialRules.Fits(state.Physical.Board, Footprint.TwoByTwo, anchor)));
    }

    [Fact]
    public void DoorsAndSummonedLargeFootprintsUseAllCellsAndOneCandidatePerPlacement()
    {
        var state = World(Large() with { FreeActions = UnitFreeAction.OpenDoor, TryOpenDoor = new(6),
            Actions = UnitAction.SummonAdjacent, SummonAdjacent = new("spawn", Posture.Lying) });
        state.Types.Add(Large("spawn"));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.ClosedDoor));
        var actions = Actions(state);
        Assert.Single(actions.Where(c => c.Door is not null));
        var spawns = actions.Where(c => c.Action == UnitAction.SummonAdjacent).ToList();
        Assert.Contains(spawns, c => c.Destination == new Cell(3, 0));
        Assert.Equal(spawns.Count, spawns.Select(c => c.Destination).Distinct().Count());
        Assert.All(spawns, c => Assert.True(SpatialRules.CanPlaceUnit(state, Footprint.TwoByTwo, c.Destination!)));
        var summoned = Act(state, spawns.First().Key);
        var created = summoned.Events.Single(e => e.Kind == "UnitCreated").UnitId!;
        Assert.Equal(4, FootprintGeometry.OccupiedCells(summoned.State, created).Count);
        Assert.False(summoned.State.IsUpright(created));
    }

    [Fact]
    public void RedDragonHasExactContentAndUsesOrdinaryActivationMoveAndAttack()
    {
        var dragon = UnitType.RedDragon();
        Assert.Equal((2, 4, 4, 4, 8), (dragon.Mov, dragon.Rng, dragon.Atk, dragon.Def, dragon.Hp));
        Assert.Equal("Red Dragon", dragon.DisplayName);
        Assert.True(dragon.Unique);
        Assert.Equal(Footprint.TwoByTwo, dragon.Footprint);
        Assert.Equal(UnitAction.NormalAttack, dragon.Actions);
        Assert.Equal(UnitFreeAction.None, dragon.FreeActions);
        Assert.Equal(UnitBehavior.None, dragon.Behaviors);
        Assert.Single(dragon.CardEntries());
        Assert.Empty(dragon.BonusActions);
        Assert.Empty(dragon.Passives);
        Assert.Null(dragon.Phase); Assert.Null(dragon.Undying); Assert.Null(dragon.MoveAfterAttack);
        var state = World(dragon, new(0, 0));
        Add(state, "target", new("small", 0, 0, 0, 0, 1), new(6, 1));
        var dice = new Dice();
        var start = GameEngine.StartRound(state, dice, false);
        Assert.Equal(new[] { UnitTypeIds.RedDragon, "small" }, new[] { start.State.ActiveTypeId!, start.State.Bag.Single() });
        var moved = GameEngine.Advance(start.State, new DefaultMonsterProvider(), dice, false);
        Assert.Single(moved.Events.Where(e => e.Kind == "MovementCompleted"));
        var attacked = GameEngine.Advance(moved.State, new DefaultMonsterProvider(), dice, false);
        Assert.Single(attacked.Events.Where(e => e.Kind == "AttackResolved"));
        Assert.DoesNotContain(attacked.State.Physical.Figures, f => f.Id == "target");
        Assert.Equal(8, attacked.State.Units[0].CurrentHp);
    }

    [Fact]
    public void LargeAuraDeathDoesNotChangeCapturedDefenceForLaterLargeTarget()
    {
        var state = World(Large() with { Rng = 8, Actions = UnitAction.Fireball, Fireball = new() }, new(0, 0));
        Add(state, "aura", Large("aura") with { Def = 0, AdjacentFriendlyUnitsDefenceBonus = new(2) }, new(3, 0));
        Add(state, "recipient", Large("recipient") with { Hp = 8, Unique = true, Def = 1 }, new(5, 0));
        Assert.Equal(3, state.EffectiveDefOf("recipient"));
        var result = Act(state, "fireball:4,1");
        var attack = result.Events.Single(e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(2, attack.Targets.Length);
        Assert.Equal(3, attack.Targets.Single(t => t.TargetId == "recipient").DefenceDice);
        Assert.Equal(1, result.State.EffectiveDefOf("recipient"));
        Assert.Equal(6, result.State.Units.Single(u => u.Id == "recipient").CurrentHp);
        Assert.Single(result.Events.Where(e => e.Kind == "UnitDied" && e.UnitId == "aura"));
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "aura");
    }

    [Fact]
    public void ClosedDoorApproachAndPhaseUseWholeFootprintWithoutChangingActualLegality()
    {
        var state = World(Large(), new(0, 0), 8, 2);
        Add(state, "target", new("small", 0, 0, 0, 0, 1), new(7, 0));
        state.Physical.Board.Edges.Add(new(new(2, 0), new(3, 0), EdgeKind.ClosedDoor));
        state.Physical.Board.Edges.Add(new(new(2, 1), new(3, 1), EdgeKind.ClosedDoor));
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.NotNull(new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0), true));
        Assert.DoesNotContain(new Cell(4, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
        state.Physical.Board.Edges[1] = state.Physical.Board.Edges[1] with { Kind = EdgeKind.Wall };
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0), true));
        state.Types[0] = state.Types[0] with { Phase = new() };
        Assert.NotNull(new GameplayQueries(state).DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.Contains(new Cell(4, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
    }

    [Fact]
    public void DefaultDoorOrderingUsesOppositeCellOfNonAnchorBorders()
    {
        var state = World(Large() with { Actions = UnitAction.None, TryOpenDoor = new(6) });
        state.Physical.Board.Edges.Add(new(new(1, 2), new(1, 3), EdgeKind.ClosedDoor));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.ClosedDoor));
        var candidates = Actions(state);
        var request = new DecisionRequest(DecisionKind.Act, "large", "actor", candidates, true);
        var choice = new DefaultMonsterProvider().Choose(request, new GameplayQueries(state));
        Assert.Equal(candidates.Single(c => c.Door!.A == new Cell(2, 2)).Key, choice);
    }

    [Fact]
    public void FleeIgnoresUnreachableHostilesAndPreservesClosedDoorParameters()
    {
        var state = World(Large() with { Actions = UnitAction.None, Behaviors = UnitBehavior.Flee }, new(0, 0), 8, 2);
        Add(state, "hostile", Large("hostile"), new(6, 0));
        foreach (var y in new[] { 0, 1 }) state.Physical.Board.Edges.Add(new(new(2, y), new(3, y), EdgeKind.ClosedDoor));
        Assert.Null(new GameplayQueries(state).DistanceToNearestHostileFrom("actor", new(0, 0)));
        Assert.Equal(5, new GameplayQueries(state).DistanceToNearestHostileFrom("actor", new(0, 0), true));
        var request = new DecisionRequest(DecisionKind.Move, "large", "actor",
            GameEngine.GameplayCandidates(state, state.Units[0]).ToList(), true);
        Assert.Null(new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void LyingDragonStandsOnceAndCompletesOneActivationWithoutLosingItsBase()
    {
        var state = World(UnitType.RedDragon());
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Single(result.Events.Where(e => e.Kind == "PostureChanged"));
        Assert.True(result.State.RoundComplete);
        Assert.Single(result.Events.Where(e => e.Kind == "TokenDrawn"));
        Assert.DoesNotContain(result.Events, e => e.Kind is "MovementCompleted" or "AttackResolved");
        Assert.Equal(4, FootprintGeometry.OccupiedCells(result.State, "actor").Count);
        Assert.Equal(8, result.State.Units[0].CurrentHp);
    }

    [Fact]
    public void BonusRelevanceUsesFootprintMovementAndTargeting()
    {
        var state = World(UnitType.Rogue() with { Footprint = Footprint.TwoByTwo }, new(0, 0), 10, 3);
        Add(state, "target", new("small", 0, 0, 0, 0, 1), new(3, 0));
        state.Round = 1; state.ActiveTypeId = state.Units[0].TypeId; state.CurrentUnitId = "actor";
        var beforeMove = GameEngine.RefreshChoices(state, new Dice(), false);
        Assert.True(beforeMove.NextInput!.Candidates.Single(c => c.Key == "bonus-action:Dash").Relevant);
        state.MoveDone = true;
        var beforeAttack = GameEngine.RefreshChoices(state, new Dice(), false);
        Assert.DoesNotContain(beforeAttack.NextInput!.Candidates, c => c.Key == "attack:target");
        Assert.True(beforeAttack.NextInput.Candidates.Single(c => c.Key == "bonus-action:Throwing Knife").Relevant);
        var knife = GameEngine.Advance(beforeAttack.State, new Choice("bonus-action:Throwing Knife"), new Dice(), false);
        Assert.Contains(knife.NextInput!.Candidates, c => c.Key == "attack:target");
    }
}
