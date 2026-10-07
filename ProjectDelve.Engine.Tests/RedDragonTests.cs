using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RedDragonTests
{
    private static GameState World(UnitType? actor = null, Cell? anchor = null)
    {
        actor ??= UnitType.RedDragon();
        return new()
        {
            Physical = new(new Board(14, 10, []), [new("dragon", anchor ?? new(1, 1))]),
            Types = [actor], Units = [actor.CreateUnit("dragon", "red")],
            Round = 1, ActiveToken = new(actor.Id, "red"), CurrentUnitId = "dragon", MoveDone = true,
            Pending = new(DecisionKind.Activation, actor.Id, "dragon", [], false)
        };
    }

    private static void Add(GameState state, string id, Cell cell, string side = "blue", UnitType? type = null)
    {
        type ??= new($"enemy:{id}", 0, 1, 1, 1, 20) { Unique = true };
        if (!state.Types.Any(t => t.Id == type.Id)) state.Types.Add(type);
        state.Units.Add(type.CreateUnit(id, side));
        state.Physical.Figures.Add(new(id, cell));
    }

    private static List<Candidate> Candidates(GameState state) =>
        GameEngine.GameplayCandidates(state, state.Units[0]).ToList();
    private static Candidate? Breath(GameState state) => Candidates(state).SingleOrDefault(c => c.Action == UnitAction.FireBreath);
    private static DecisionRequest Request(GameState state) =>
        new(DecisionKind.Activation, state.ActiveToken!.TypeId, "dragon", Candidates(state), false);
    private static string? Behavior(GameState state) => new DefaultAutomatedProvider().Choose(Request(state), new GameplayQueries(state));

    private sealed class Dice(params DefenceFace[] defence) : IRandomProvider
    {
        public int AttackRolls, DefenceRolls;
        public List<string> Order = [];
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; Order.Add("attack"); return AttackFace.Hit; }
        public DefenceFace RollDefenceDie()
        {
            Order.Add("defence");
            return DefenceRolls < defence.Length ? defence[DefenceRolls++] : CountMiss();
        }
        private DefenceFace CountMiss() { DefenceRolls++; return DefenceFace.Miss; }
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static EngineResult Act(GameState state, string key, Dice? dice = null) =>
        TestGame.Advance(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!, new Choice(key), dice ?? new(), false);

    [Fact]
    public void BreathIncludesAllLegalHostilesOnlyAndIsRelevantWithOneTarget()
    {
        var state = World();
        Add(state, "near", new(4, 1));
        Add(state, "far", new(8, 1));
        Add(state, "hidden", new(1, 5));
        Add(state, "friend", new(3, 2), "red");
        for (var x = 0; x < 14; x++)
            state.Physical.Board.Edges.Add(new(new(x, 3), new(x, 4), EdgeKind.Wall));
        var breath = Assert.IsType<Candidate>(Breath(state));
        Assert.Equal(new[] { "near" }, breath.TargetIds);
        Assert.True(breath.Relevant);
        var result = Act(state, breath.Key);
        Assert.Equal("Fire Breath", Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved").AbilityName);
        Assert.DoesNotContain(Candidates(state), c => c.TargetId == "friend");
    }

    [Fact]
    public void BreathUsesEffectiveRangeAndAttackAndListsMultiCellTargetOnce()
    {
        var state = World();
        Add(state, "large", new(6, 1), type: new("large", 0, 0, 0, 2, 20)
            { Unique = true, Footprint = Footprint.TwoByTwo });
        Add(state, "other", new(1, 4));
        Assert.Equal(new[] { "large", "other" }, Breath(state)!.TargetIds);
        state.ModifiersThisTurn.Add(new(Stat.Rng, -1));
        Assert.Equal(new[] { "other" }, Breath(state)!.TargetIds);
        state.ModifiersThisTurn.Add(new(Stat.Rng, 1));
        state.ModifiersThisTurn.Add(new(Stat.Atk, 2));
        var dice = new Dice(DefenceFace.Block, DefenceFace.Miss, DefenceFace.Miss);
        var result = Act(state, "fire-breath", dice);
        var attack = Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(6, dice.AttackRolls);
        Assert.Equal(3, dice.DefenceRolls);
        Assert.Equal(new[] { new AttackTargetResult("large", 2, 1, 5), new AttackTargetResult("other", 1, 0, 6) }, attack.Targets);
        Assert.Equal(Enumerable.Repeat("attack", 6).Concat(Enumerable.Repeat("defence", 3)), dice.Order);
    }

    [Fact]
    public void BreathRequiresRangeAndLosOnSamePairForTwoLargeUnits()
    {
        var state = World(UnitType.RedDragon() with { Rng = 3 }, new(0, 0));
        Add(state, "large", new(3, 2), type: new("large", 0, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo });
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 2), EdgeKind.Wall));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "dragon", "large"));
        Assert.Null(Breath(state));
        state.ModifiersThisTurn.Add(new(Stat.Rng, 1));
        Assert.Equal(new[] { "large" }, Breath(state)!.TargetIds);
    }

    [Fact]
    public void HostileFootprintBlocksBreathButFriendlyFootprintsAndEndpointsDoNot()
    {
        var state = World(UnitType.RedDragon() with { Rng = 7 }, new(0, 2));
        var large = new UnitType("large", 0, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo };
        Add(state, "target", new(5, 2), type: large);
        Assert.Equal(new[] { "target" }, Breath(state)!.TargetIds);
        Add(state, "blocker", new(2, 2), type: large);
        Assert.Equal(new[] { "blocker" }, Breath(state)!.TargetIds);
        state.Units[2] = state.Units[2] with { SideId = "red" };
        Assert.Equal(new[] { "target" }, Breath(state)!.TargetIds);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SharedRollSnapshotSurvivesAuraDefeatOrUndying(bool undying)
    {
        var state = World();
        Add(state, "aura", new(3, 1), type: new("aura", 0, 0, 0, 0, 1)
            { AdjacentFriendlyUnitsDefenceBonus = new(2), Undying = undying ? new() : null });
        Add(state, "later", new(3, 2));
        Assert.Equal(3, state.EffectiveDefOf("later"));
        var dice = new Dice(DefenceFace.Block, DefenceFace.Miss, DefenceFace.Miss);
        var result = Act(state, "fire-breath", dice);
        var attack = Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(4, attack.AttackDice);
        Assert.Equal(4, dice.AttackRolls);
        Assert.Equal(3, dice.DefenceRolls);
        Assert.Equal(new[] { new AttackTargetResult("aura", 0, 0, 4), new AttackTargetResult("later", 3, 1, 3) }, attack.Targets);
        Assert.Equal(1, result.State.EffectiveDefOf("later"));
        Assert.Equal(20, TestGame.OperationSteps(result)[1].StateAfter.Units[2].CurrentHp);
        Assert.Equal(17, result.State.Units[2].CurrentHp);
        Assert.False(result.State.IsUpright("aura"));
        Assert.Equal(undying, result.State.Physical.Figures.Any(f => f.Id == "aura"));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 0)]
    [InlineData(0, 1)] [InlineData(3, 1)] [InlineData(0, 2)] [InlineData(3, 2)]
    [InlineData(0, 3)] [InlineData(1, 3)] [InlineData(2, 3)] [InlineData(3, 3)]
    public void ClawUsesWholeFootprintAdjacency(int x, int y)
    {
        var state = World();
        Add(state, "adjacent", new(x, y));
        Add(state, "ranged", new(5, 1));
        Add(state, "friend", new(2, 4), "red");
        Assert.Equal("adjacent", Assert.Single(Candidates(state), c => c.Action == UnitAction.ClawAttack).TargetId);
        Assert.Contains(Candidates(state), c => c.Key == "attack:ranged");
    }

    [Fact]
    public void ClawRequiresGeometricLosOnAdjacentPairAndExcludesFriendlyAdjacentUnit()
    {
        var state = World();
        Add(state, "enemy", new(3, 1));
        Add(state, "friend", new(0, 1), "red");
        for (var y = 0; y < 10; y++)
            state.Physical.Board.Edges.Add(new(new(2, y), new(3, y), EdgeKind.Wall));
        Assert.DoesNotContain(Candidates(state), c => c.Action == UnitAction.ClawAttack);
    }

    [Fact]
    public void ClawModifierIsAttackLocalAndOrdinaryResolutionSupportsBackstab()
    {
        var state = World(UnitType.RedDragon() with { Backstab = new() });
        Add(state, "enemy", new(3, 1));
        Add(state, "support", new(4, 1), "red");
        var dice = new Dice();
        var result = Act(state, "claw-attack:enemy", dice);
        Assert.Equal(6, dice.AttackRolls);
        Assert.Equal(1, dice.DefenceRolls);
        Assert.Equal(14, result.State.Units[1].CurrentHp);
        Assert.Equal(4, result.State.EffectiveAtkOf("dragon"));
        Assert.Empty(result.State.ModifiersThisTurn);
        var nextRound = TestGame.StartRound(result.State, new Dice(), false);
        var nextAction = TestGame.Advance(nextRound.State, new Choice("stay"), new Dice(), false);
        var normal = Act(nextAction.State, "attack:enemy", new Dice());
        Assert.Equal(5, Assert.Single(TestGame.OperationEvents(normal), e => e.Kind == "AttackResolved").Attack!.AttackDice);
        var baseline = World(); Add(baseline, "enemy", new(3, 1));
        Assert.Equal(5, Assert.Single(Act(baseline, "claw-attack:enemy").Events, e => e.Kind == "AttackResolved").Attack!.AttackDice);
        Assert.Equal(4, Assert.Single(Act(baseline, "attack:enemy").Events, e => e.Kind == "AttackResolved").Attack!.AttackDice);
    }

    [Theory]
    [InlineData(3, 1, false, "claw-attack:a")]
    [InlineData(5, 1, false, "attack:a")]
    [InlineData(3, 1, true, "fire-breath")]
    [InlineData(5, 1, true, "fire-breath")]
    public void BehaviorPrioritizesAuthoritativeActions(int x, int y, bool second, string expected)
    {
        var state = World(); Add(state, "a", new(x, y));
        if (second) Add(state, "b", new(1, 4));
        Assert.Equal(expected, Behavior(state));
    }

    [Fact]
    public void ClawUsesOrdinaryDistanceThenBoardOrderRanking()
    {
        var state = World(UnitType.RedDragon() with { Rng = 0 });
        Add(state, "diagonal", new(0, 0));
        Add(state, "bottom", new(1, 3));
        Add(state, "right", new(3, 1));
        Add(state, "top", new(2, 0));
        Assert.Null(Breath(state));
        Assert.Equal("claw-attack:top", Behavior(state));
    }

    [Fact]
    public void NoCurrentAttackContinuesOrdinaryMovementAndApproach()
    {
        var state = World(); Add(state, "far", new(12, 1));
        state.MoveDone = false;
        var candidates = Candidates(state);
        Assert.DoesNotContain(candidates, c => c.Action is UnitAction.NormalAttack or UnitAction.ClawAttack or UnitAction.FireBreath);
        var request = Request(state);
        var queries = new GameplayQueries(state);
        var expected = new ApproachMovementProvider(new DefaultAutomatedProvider()).Choose(request, queries);
        var actual = new DefaultAutomatedProvider().Choose(request, queries);
        Assert.Equal(expected, actual);
        Assert.Equal("3,1", actual);
        var result = TestGame.Advance(state, new DefaultAutomatedProvider(), new Dice(), false);
        Assert.Equal(new Cell(3, 1), result.State.Physical.Figures[0].Position);
    }

    [Fact]
    public void MovementComposesWithClawOnlyAndHypotheticalStateDoesNotMutateWorld()
    {
        var state = World(UnitType.RedDragon() with { Actions = UnitAction.ClawAttack, Rng = 0 });
        Add(state, "enemy", new(5, 1)); state.MoveDone = false;
        var before = JsonSerializer.Serialize(state);
        var queries = new GameplayQueries(state);
        Assert.False(queries.CanAttackHostileFrom("dragon", new(1, 1)));
        Assert.True(queries.CanAttackHostileFrom("dragon", new(3, 1)));
        Assert.Equal(2, queries.DistanceToAttackPositionFrom("dragon", new(1, 1)));
        Assert.Equal("3,1", Behavior(state));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void BreathMembershipAndAttackDiceStayFixedWhenFirstDefeatChangesFuryAndLos()
    {
        var state = World(UnitType.RedDragon() with { Fury = new() });
        Add(state, "blocker", new(3, 1), type: new("blocker", 0, 0, 0, 0, 1));
        Add(state, "later", new(1, 3));
        Add(state, "behind", new(5, 1));
        // Both source rows have a blocked segment to behind.
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall));
        Assert.Equal(new[] { "blocker", "later" }, Breath(state)!.TargetIds);
        var result = Act(state, "fire-breath");
        var attack = Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(5, attack.AttackDice);
        Assert.All(attack.Targets, t => Assert.Equal(5, t.Damage));
        Assert.Equal(new[] { "blocker", "later" }, attack.Targets.Select(t => t.TargetId));
        Assert.Equal(20, result.State.Units.Single(u => u.Id == "behind").CurrentHp);
        Assert.Equal(4, result.State.EffectiveAtkOf("dragon"));
    }

    [Theory]
    [InlineData("fire-breath")] [InlineData("claw-attack:enemy")]
    public void NewAttacksUseOrdinaryCleaveAndMoveAfterAttackFollowUps(string action)
    {
        var state = World(UnitType.RedDragon() with { Cleave = new(), MoveAfterAttack = new(1) });
        Add(state, "enemy", new(3, 1));
        var result = Act(state, action);
        Assert.Equal(DecisionKind.Cleave, result.NextInput!.Kind);
        Assert.True(result.State.CleavePending);
        var cleaved = Act(result.State, "cleave:enemy");
        Assert.Equal(DecisionKind.Move, cleaved.NextInput!.Kind);
        Assert.True(cleaved.NextInput.IsMoveAfterAttack);
    }

    [Fact]
    public void OneByOneUnitsUseSameActionMechanicsAndLyingSourceHasNoCandidates()
    {
        var state = World(UnitType.RedDragon() with { Footprint = Footprint.OneByOne });
        Add(state, "enemy", new(2, 1));
        Assert.Contains(Candidates(state), c => c.Action == UnitAction.ClawAttack);
        Assert.Equal(new[] { "enemy" }, Breath(state)!.TargetIds);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Empty(Candidates(state));
    }
}
