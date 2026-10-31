using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WizardTests
{
    private sealed class Random : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(UnitType? type = null, int enemyX = 4)
    {
        type ??= UnitType.Wizard();
        return new()
        {
            Physical = new(new Board(7, 2, []), [new("wizard", new(0, 0)), new("enemy", new(enemyX, 0))]),
            Types = [type, new("enemy-type", 0, 0, 0, 0, 20) { Unique = true }],
            Units = [type.CreateUnit("wizard", "blue"), new("enemy", "enemy-type", "red", 20)]
        };
    }

    private static EngineResult Choose(GameState state, string key, Random? random = null) =>
        TestGame.Advance(state, new Choice(key), random ?? new(), false);
    private static AbilityUses Uses(GameState state) => state.Units.Single(u => u.Id == "wizard").Uses("focus");
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void WizardSuppliesNormalAttackFireballTelekinesisOpenDoorAndGenericFocusContent()
    {
        var type = UnitType.Wizard();
        Assert.Equal("wizard-type", type.Id);
        Assert.Equal("custom", UnitType.Wizard("custom").Id);
        Assert.Equal("Wizard", type.DisplayName);
        Assert.Equal((3, 4, 3, 2, 4), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.IsType<Telekinesis>(type.LocalAbilities.Single(a => a.Key == "telekinesis").Mechanic);
        Assert.Contains(AbilityQueries.NativeAbilities(type, "wizard"), grant => grant.Definition.Mechanic is FireballMechanic);
        Assert.IsType<OpenDoor>(type.LocalAbilities.Single(a => a.Key == "open-door").Mechanic);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.DoesNotContain(type.LocalAbilities, a => a.Timing == AbilityTiming.Passive);
        var ability = Assert.Single(AbilityQueries.NativeAbilities(type, "wizard"), grant => grant.Definition.Timing == AbilityTiming.BonusAction).Definition;
        Assert.Equal("Focus", ability.Name);
        Assert.Equal(2, ability.Usage.MaxUses);
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 1), Assert.Single(Assert.IsType<SelfModifiersMechanic>(ability.Mechanic).Modifiers));
        Assert.Equal(4, type.CreateUnit("wizard", "any-side").CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), Uses(State()));
        var bare = State();
        bare.Units[0] = new("wizard", type.Id, "blue", 4);
        Assert.Equal(new AbilityUses(2, 2), Uses(TestGame.StartRound(bare, new Random(), false).State));
    }

    [Fact]
    public void FocusUsesEffectiveAttackAndExpiresWhilePersistentUsesSurviveLaterActivations()
    {
        var random = new Random();
        var started = TestGame.StartRound(State(), random, false);
        Assert.Equal(3, started.State.EffectiveAtkOf("wizard"));
        var focus = Assert.Single(started.NextInput!.Candidates, c => c.Ability?.Definition.Name == "Focus");
        Assert.Equal(ActivationChoiceKind.BonusAction, focus.Kind);
        var focused = Choose(started.State, focus.Key);
        Assert.False(focused.State.ActionDone);
        Assert.False(focused.State.MoveDone);
        Assert.Equal(new AbilityUses(2, 1), Uses(focused.State));
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.Equal(new KeyValuePair<string, int>("unit:focus", 1), Assert.Single(focused.State.BonusActionUseCountsThisActivation));
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 1), Assert.Single(focused.State.ModifiersThisTurn));
        Assert.Equal(4, focused.State.EffectiveAtkOf("wizard"));
        Assert.Equal(4, focused.State.EffectiveAtk["wizard"]);
        Assert.Equal("Focus", Assert.Single(TestGame.OperationEvents(focused)).AbilityName);
        Assert.Equal(4, Assert.Single(TestGame.OperationSteps(focused)).StateAfter.EffectiveAtkOf("wizard"));
        Assert.DoesNotContain(focused.NextInput!.Candidates, c => c.Key == focus.Key);
        Assert.Throws<ArgumentException>(() => Choose(focused.State, focus.Key));

        var stayed = Choose(Restore(focused.State), "stay");
        Assert.Equal(4, stayed.State.EffectiveAtkAgainst("wizard", "enemy"));
        Assert.Contains(stayed.NextInput!.Candidates, c => c.Key == "attack:enemy" && c.Relevant);
        var attacked = Choose(Restore(stayed.State), "attack:enemy", random);
        Assert.Equal(4, random.AttackRolls);
        Assert.Equal(4, Assert.Single(TestGame.OperationEvents(attacked), e => e.Kind == "AttackResolved").Hits);
        Assert.True(attacked.State.RoundComplete);
        Assert.Empty(attacked.State.ModifiersThisTurn);
        Assert.Equal(3, attacked.State.EffectiveAtkOf("wizard"));

        var second = TestGame.StartRound(Restore(attacked.State), random, false);
        Assert.Equal(new AbilityUses(2, 1), Uses(second.State));
        Assert.Empty(second.State.BonusActionUseCountsThisActivation);
        Assert.Equal(3, second.State.EffectiveAtkOf("wizard"));
        var focusedAgain = Choose(second.State, focus.Key);
        Assert.Equal(4, focusedAgain.State.EffectiveAtkOf("wizard"));
        Assert.Equal(new AbilityUses(2, 0), Uses(focusedAgain.State));
        var ended = Choose(Choose(focusedAgain.State, "stay").State, "end-turn");
        Assert.Equal(3, ended.State.EffectiveAtkOf("wizard"));
        var third = TestGame.StartRound(Restore(ended.State), random, false);
        Assert.Equal(new AbilityUses(2, 0), Uses(third.State));
        Assert.DoesNotContain(third.NextInput!.Candidates, c => c.Key == focus.Key);
        Assert.Throws<ArgumentException>(() => Choose(third.State, focus.Key));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void FocusRelevanceUsesOrdinaryAttackOpportunities(int enemyX, bool relevant)
    {
        var started = TestGame.StartRound(State(enemyX: enemyX), new Random(), false);
        Assert.False(Assert.Single(started.NextInput!.Candidates, c => c.Ability?.Definition.Timing == AbilityTiming.BonusAction).Relevant);
        var stayed = Choose(started.State, "stay");
        Assert.Equal(relevant, Assert.Single(stayed.NextInput!.Candidates, c => c.Ability?.Definition.Timing == AbilityTiming.BonusAction).Relevant);
    }

    [Fact]
    public void FocusStacksWithDistinctBonusActionWithoutConsumingItOrTheAction()
    {
        var type = UnitType.Wizard() with
        {
            LocalAbilities = [.. UnitType.Wizard().LocalAbilities, new("other-bonus", "Other bonus", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 2)])) { Usage = new(2) }]
        };
        var started = TestGame.StartRound(State(type), new Random(), false);
        var focused = Choose(started.State, "ability:focus");
        Assert.Contains(focused.NextInput!.Candidates, c => c.Key == "ability:other-bonus");
        var boosted = Choose(focused.State, "ability:other-bonus");
        Assert.Equal(6, boosted.State.EffectiveAtkOf("wizard"));
        Assert.False(boosted.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), Uses(boosted.State));
        var random = new Random();
        Choose(Choose(boosted.State, "stay").State, "attack:enemy", random);
        Assert.Equal(6, random.AttackRolls);
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("red")]
    public void OpenDoorIsFreeRegardlessOfSideAndPreservesFocus(string side)
    {
        var state = State();
        state.Units[0] = state.Units[0] with { SideId = side };
        var door = new Edge(new(0, 0), new(1, 0), EdgeKind.ClosedDoor);
        state.Physical.Board.Edges.Add(door);
        var started = TestGame.StartRound(state, new Random(), false);
        var candidate = Assert.Single(started.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        Assert.Equal(ActivationChoiceKind.FreeAction, candidate.Kind);
        var opened = Choose(started.State, candidate.Key);
        Assert.Equal(EdgeKind.OpenDoor, opened.State.Physical.Board.Edges[0].Kind);
        Assert.Equal("DoorOpened", Assert.Single(TestGame.OperationEvents(opened)).Kind);
        Assert.False(opened.State.ActionDone);
        Assert.False(opened.State.MoveDone);
        Assert.Empty(opened.State.BonusActionUseCountsThisActivation);
        Assert.Equal(new AbilityUses(2, 2), Uses(opened.State));
        Assert.Contains(opened.NextInput!.Candidates, c => c.Ability?.Definition.Name == "Focus");
    }

    [Fact]
    public void CardWordingAndUseLimitAreDomainOwned()
    {
        var type = UnitType.Wizard();
        Assert.Equal(new[] { "Attack", "Open Door", "Fireball", "Telekinesis", "Focus" }, type.CardEntries().Select(e => e.Name));
        var focus = Assert.Single(type.CardEntries(), e => e.Name == "Focus");
        Assert.Equal(new CardEntryDescription("ability:focus", "Focus", "Bonus Action", "ATK +1 this activation.", 2), focus);
        Assert.Equal("2/game", focus.UseLimitText);
        Assert.Equal(new AbilityUses(2, 2), type.UsesFor(type.CreateUnit("wizard", "blue"), focus.Id));
    }
}
