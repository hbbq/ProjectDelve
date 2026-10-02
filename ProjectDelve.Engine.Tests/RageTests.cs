using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RageTests
{
    private sealed class Random : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int enemyX = 1, UnitType? type = null)
    {
        type ??= UnitType.Barbarian();
        return new()
        {
            Physical = new(new Board(6, 2, []), [new("hero", new(0, 0)), new("enemy", new(enemyX, 0))]),
            Types = [type, new("enemy-type", 0, 0, 0, 0, 20)],
            Units = [type.CreateUnit("hero", "blue"), new("enemy", "enemy-type", "red", 20)]
        };
    }

    private static EngineResult Choose(GameState state, string key, Random? random = null, bool auto = false) =>
        GameEngine.Advance(state, new Choice(key), random ?? new(), auto);
    private static Candidate Rage(EngineResult result) =>
        Assert.Single(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
    private static AbilityUses Uses(GameState state) => state.Units.Single(u => u.Id == "hero").BonusActionUses["Rage"];
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void BarbarianExplicitlySuppliesRageAndStartsWithTwoOfTwoUses()
    {
        var type = UnitType.Barbarian();
        var ability = Assert.Single(type.BonusActions);
        Assert.Equal("Rage", ability.Name);
        Assert.Equal(2, ability.MaxUses);
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 2), Assert.Single(ability.Modifiers));
        Assert.Equal(new AbilityUses(2, 2), Uses(State()));
        Assert.Empty(UnitType.Hero("hero", 1, 1, 1, 1, 1).BonusActions);
        Assert.All(new[] { UnitType.Grunt(), UnitType.Zombie(), UnitType.SkeletonArcher(), UnitType.Goblin() },
            t => Assert.Empty(t.BonusActions));
        var bare = State();
        bare.Units[0] = new("hero", type.Id, "blue", type.Hp);
        var started = GameEngine.StartRound(bare, new Random(), false);
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.Empty(bare.Units[0].BonusActionUses); // Initialization is detached from the supplied scenario.
    }

    [Fact]
    public void RageConsumesBonusActionAndUseImmediatelyAndAttackRollsEffectiveAtkAfterSerialization()
    {
        var random = new Random();
        var started = GameEngine.StartRound(State(), random, false);
        Assert.Equal(ActivationChoiceKind.BonusAction, Rage(started).Kind);
        Assert.False(Rage(started).Relevant);
        var raging = Choose(started.State, Rage(started).Key, random);
        Assert.Equal(new AbilityUses(2, 1), Uses(raging.State));
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.True(raging.State.BonusActionUsed);
        Assert.False(raging.State.MoveDone);
        Assert.False(raging.State.ActionDone);
        Assert.Equal(4, raging.State.Types[0].Atk);
        Assert.Equal(6, raging.State.EffectiveAtkOf("hero"));
        Assert.Equal(0, raging.State.EffectiveAtkOf("enemy"));
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 2), Assert.Single(raging.State.ModifiersThisTurn));
        Assert.DoesNotContain(raging.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        Assert.Throws<ArgumentException>(() => Choose(raging.State, Rage(started).Key));
        Assert.Equal("Rage", Assert.Single(raging.Events).AbilityName);
        Assert.Equal(6, Assert.Single(raging.ResolutionSteps).StateAfter.EffectiveAtkOf("hero"));

        var moved = Choose(Restore(raging.State), "stay", random);
        Assert.Equal(6, moved.State.EffectiveAtk["hero"]);
        var attacked = Choose(Restore(moved.State), "attack:enemy", random);
        var attack = Assert.Single(attacked.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(6, random.AttackRolls);
        Assert.Equal(6, attack.Hits);
        Assert.Equal(6, attack.Damage);
        Assert.Equal(6, attacked.ResolutionSteps.First().StateAfter.EffectiveAtkOf("hero"));
        Assert.True(attacked.State.RoundComplete);
        Assert.Empty(attacked.State.ModifiersThisTurn);
        Assert.Equal(4, attacked.State.EffectiveAtkOf("hero"));
        Assert.Equal(14, attacked.State.Units.Single(u => u.Id == "enemy").CurrentHp);

        var next = GameEngine.StartRound(Restore(attacked.State), random, false);
        Assert.Equal(4, next.State.EffectiveAtkOf("hero"));
        Assert.Equal(new AbilityUses(2, 1), Uses(next.State));
        var second = Choose(next.State, Rage(next).Key, random);
        Assert.Equal(new AbilityUses(2, 0), Uses(second.State));
        var ended = Choose(Choose(second.State, "stay").State, "end-turn");
        var third = GameEngine.StartRound(Restore(ended.State), random, false);
        Assert.False(third.State.BonusActionUsed);
        Assert.Equal(new AbilityUses(2, 0), Uses(third.State));
        Assert.DoesNotContain(third.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        Assert.Throws<ArgumentException>(() => Choose(third.State, Rage(next).Key));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, false)]
    public void RelevanceRequiresMoveCompletedAndAnAvailableAttack(int enemyX, bool relevant)
    {
        var started = GameEngine.StartRound(State(enemyX), new Random(), false);
        Assert.False(Rage(started).Relevant);
        var moved = Choose(started.State, "stay");
        Assert.Equal(relevant, Rage(moved).Relevant);
        if (relevant)
        {
            var attacked = Choose(moved.State, "attack:enemy");
            Assert.True(attacked.State.ActionDone);
            Assert.False(Rage(attacked).Relevant);
            moved = attacked;
        }
        // Even after Action, or with no target, the supplied irrelevant key is legal.
        var raging = Choose(moved.State, Rage(moved).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(raging.State));
        Assert.True(raging.State.RoundComplete); // Sole End Turn still auto-resolves.
        Assert.Empty(raging.State.ModifiersThisTurn);
    }

    [Fact]
    public void RageUseRemainsSpentWhenNoAttackIsMade()
    {
        var started = GameEngine.StartRound(State(), new Random(), false);
        var raging = Choose(started.State, Rage(started).Key);
        var ended = Choose(Choose(raging.State, "stay").State, "end-turn");
        Assert.DoesNotContain(ended.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(new AbilityUses(2, 1), Uses(ended.State));
        Assert.Equal(4, ended.State.EffectiveAtkOf("hero"));
    }

    [Fact]
    public void RelevantSubsetCanEndTurnWithoutSpendingLegalIrrelevantRage()
    {
        var started = GameEngine.StartRound(State(5), new Random());
        var ended = Choose(started.State, "stay", auto: true);
        Assert.True(ended.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(ended.State));
        var exposed = Choose(started.State, "stay");
        Assert.False(Rage(exposed).Relevant);
        Assert.Contains(exposed.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn && c.Relevant);
        var refreshed = GameEngine.RefreshChoices(exposed.State, new Random());
        Assert.True(refreshed.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(refreshed.State));
    }

    [Fact]
    public void MovingIntoRangeMakesRageRelevantAndKeepsTheModifierThroughAttack()
    {
        var started = GameEngine.StartRound(State(2), new Random(), false);
        Assert.False(Rage(started).Relevant);
        var moved = Choose(started.State, "1,0");
        Assert.True(Rage(moved).Relevant);
        Assert.Contains(moved.NextInput!.Candidates, c => c.Key == "attack:enemy");
        var raging = Choose(moved.State, Rage(moved).Key);
        Assert.True(raging.State.MoveDone);
        Assert.False(raging.State.ActionDone);
        Assert.Equal(6, raging.State.EffectiveAtkOf("hero"));
        var random = new Random();
        Choose(raging.State, "attack:enemy", random);
        Assert.Equal(6, random.AttackRolls);
    }

    [Fact]
    public void EndingFirstBarbariansActivationDoesNotTransferModifierOrSpendSecondUnitsUses()
    {
        var state = State();
        state.Units.Add(UnitType.Barbarian().CreateUnit("ally", "blue"));
        state.Physical.Figures.Add(new("ally", new(0, 1)));
        var selecting = GameEngine.StartRound(state, new Random(), false);
        var started = Choose(selecting.State, "hero");
        var raging = Choose(started.State, Rage(started).Key);
        var beforeEnd = Restore(Choose(raging.State, "stay").State);
        var ended = Choose(beforeEnd, "end-turn");
        Assert.Equal("ally", ended.State.CurrentUnitId);
        Assert.Empty(ended.State.ModifiersThisTurn);
        Assert.Equal(4, ended.State.EffectiveAtkOf("ally"));
        Assert.Equal(4, ended.State.EffectiveAtkOf("hero"));
        Assert.Equal(new AbilityUses(2, 2), ended.State.Units.Single(u => u.Id == "ally").BonusActionUses["Rage"]);
        Assert.Equal(new AbilityUses(2, 1), Uses(ended.State));
        Assert.Equal(6, beforeEnd.EffectiveAtkOf("hero"));
        Assert.Single(beforeEnd.ModifiersThisTurn); // Completion cannot mutate a prior snapshot.
    }

    [Fact]
    public void ModifierBehaviorIsReusableAndCanEnableZeroBaseAtkAttack()
    {
        var type = UnitType.Barbarian("other") with
        {
            Atk = 0, BonusActions = [new("Battle Focus", 2, [new(Stat.Atk, 2)])]
        };
        var started = GameEngine.StartRound(State(type: type), new Random(), false);
        var moved = Choose(started.State, "stay");
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.True(Rage(moved).Relevant);
        var raging = Choose(moved.State, Rage(moved).Key);
        Assert.Equal(2, raging.State.EffectiveAtkOf("hero"));
        Assert.Contains(raging.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var random = new Random();
        Choose(raging.State, "attack:enemy", random);
        Assert.Equal(2, random.AttackRolls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void RemainingUsesCannotExceedMaximumOrBeNegative(int remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityUses(2, remaining));
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize<AbilityUses>(
            $"{{\"MaxUses\":2,\"RemainingUses\":{remaining}}}"));
    }
}
