using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;
using static ProjectDelve.Engine.Stat;

namespace ProjectDelve.Engine.Tests;

public sealed class UnitAuthoringTests
{
    [Fact]
    public void StatsOnlyDefinitionHasBaselineAttackAndNoOtherContent()
    {
        var type = UnitType.Define("ordinary", "Ordinary", Stats(2, 3, 4, 5, 1));
        Assert.Equal("ordinary", type.Id);
        Assert.Equal("Ordinary", type.DisplayName);
        Assert.Equal((2, 3, 4, 5, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal("attack", Assert.Single(type.CardEntries()).Id);
        Assert.Empty(type.LocalAbilities);
        Assert.Empty(type.LocalAbilities.Where(a => a.Timing == AbilityTiming.Passive));
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Equal(UnitAction.NormalAttack, UnitRoster.Grunt().Actions);
    }

    [Fact]
    public void NamedUnlimitedAbilitiesPreserveCategoriesAndHaveNoUseCounters()
    {
        var type = UnitType.Define("named", "Named", Stats(1, 4, 3, 2, 5), Unique(),
            Ability("Distant Touch", Unlimited(), Telekinesis(), id: "touch"),
            Ability("Resolve", Unlimited(), Fury(atkBonus: 2, adjacentEnemies: 1), id: "resolve"),
            Ability("Flank", Unlimited(), Backstab(atkBonus: 3), id: "flank"),
            Ability("Ward", Unlimited(), AdjacentFriendliesDefenceBonus(2), id: "ward"),
            Ability("Endurance", Unlimited(), Undying(), id: "undying"));
        var state = Restore(State(type));
        var entries = state.Types[0].CardEntries().ToDictionary(e => e.Id);
        Assert.Equal("Distant Touch", entries["ability:touch"].Name);
        Assert.Equal("Action", entries["ability:touch"].Category);
        Assert.Equal("Resolve", entries["ability:resolve"].Name);
        Assert.Equal("Flank", entries["ability:flank"].Name);
        Assert.Equal("Ward", entries["ability:ward"].Name);
        Assert.Equal("Endurance", entries["ability:undying"].Name);
        Assert.Equal("Capability", entries["ability:undying"].Category);
        Assert.All(entries.Values, entry =>
        {
            Assert.Null(entry.MaxUses);
            Assert.Null(entry.UseLimitText);
            Assert.Null(state.Types[0].UsesFor(state.Units[0], entry.Id));
        });
        Assert.Empty(state.Units[0].AbilityUseCounts);
        Assert.Equal(5, state.EffectiveAtkOf("actor")); // Fury still derives from adjacency.
        Assert.Equal(3, state.Types[0].Atk);
        state.MoveDone = true;
        state.CurrentUnitId = "actor";
        Assert.Contains(GameEngine.GameplayCandidates(state, state.Units[0]), c => c.Action == UnitAction.Telekinesis);
        Assert.All(new[] { "ability:resolve", "ability:flank", "ability:ward" }, id => Assert.Equal("Passive", entries[id].Category));
    }

    [Fact]
    public void RenamedLimitedAbilityKeepsMechanicalEntryAndCounterIdentity()
    {
        var type = UnitType.Define("medic", "Medic", Stats(1, 1, 3, 2, 5), Unique(),
            Ability("Mend", Uses(4), Heal(amount: 3), id: "heal"));
        var state = State(type);
        state.Units[1] = state.Units[1] with { SideId = "blue", CurrentHp = 1 };
        var started = TestGame.StartRound(state, new Dice(), false);
        var ready = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
        var entry = Assert.Single(ready.State.Types[0].CardEntries(), e => e.Id == "ability:heal");
        Assert.Equal("Mend", entry.Name);
        Assert.Equal("4/game", entry.UseLimitText);
        var healed = TestGame.Advance(Restore(ready.State), new Choice("ability:heal:target"), new Dice(), false);
        Assert.Equal(3, Assert.Single(TestGame.OperationEvents(healed), e => e.Kind == "HealResolved").Healing);
        Assert.Equal("Mend", Assert.Single(TestGame.OperationEvents(healed), e => e.Kind == "HealResolved").AbilityName);
        Assert.Equal(new AbilityUses(4, 3), healed.State.Units[0].Uses("heal", 4));
        Assert.Equal("ability:heal", type.CardEntries().Single(e => e.Name == "Mend").Id);
    }

    [Fact]
    public void CantAttackOnlyRemovesNormalAttackAndDoesNotFreezeEffectiveStats()
    {
        var type = UnitType.Define("non-attacker", "Non-attacker", Stats(1, 4, 3, 2, 5), Unique(), CantAttack(), Ability("Telekinesis", Unlimited(), Telekinesis(), id: "touch"));
        var state = State(type);
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.ModifiersThisTurn.AddRange([new(Stat.Atk, 2), new(Stat.Rng, 3)]);
        var restored = Restore(state);
        Assert.Equal(UnitAction.None, restored.Types[0].Actions);
        Assert.Equal(3, restored.Types[0].Atk);
        Assert.Equal(4, restored.Types[0].Rng);
        Assert.Equal(5, restored.EffectiveAtkOf("actor"));
        Assert.Equal(7, restored.EffectiveRngOf("actor"));
        var choices = GameEngine.GameplayCandidates(restored, restored.Units[0]).ToArray();
        Assert.DoesNotContain(choices, c => c.Action == UnitAction.NormalAttack);
        Assert.Contains(choices, c => c.Action == UnitAction.Telekinesis);
        Assert.True(UnitRoster.Shaman().HasAction(UnitAction.SummonAdjacent));
    }

    [Fact]
    public void BehaviorsAreSeparateOrderIndependentMetadataAndNeverCardEntries()
    {
        var type = UnitType.Define("policy", "Policy", Stats(2, 1, 3, 2, 1));
        var first = type.WithBehaviors(UnitBehavior.Flee, UnitBehavior.UseSummon);
        var second = type.WithBehaviors(UnitBehavior.UseSummon, UnitBehavior.Flee);
        Assert.Equal(first, second);
        Assert.Equal(type.Actions, first.Actions);
        Assert.Equal(type.CardEntries(), first.CardEntries());
        Assert.DoesNotContain(first.CardEntries(), e => e.Category == "Behavior");
    }

    [Fact]
    public void NamedBonusPresentationDoesNotChangePersistedIdentityOrCounters()
    {
        var type = UnitType.Define("custom", "Custom", Stats(1, 1, 3, 2, 5), Unique(),
            Ability("Battle Cry", Uses(3), BonusActionSelfModifier(Modifier(Atk, +2)), id: "stable-key"));
        var started = TestGame.StartRound(State(type), new Dice(), false);
        var restored = Restore(started.State);
        var entry = Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "ability:stable-key");
        Assert.Equal("Battle Cry", entry.Name);
        Assert.Equal("ATK +2 this activation.", entry.Description);
        Assert.Equal("3/game", entry.UseLimitText);
        var bonus = Assert.Single(restored.Pending!.Candidates, c => c.Ability?.Definition.Timing == AbilityTiming.BonusAction);
        Assert.Equal("ability:stable-key", bonus.Key);
        Assert.Equal("ability:stable-key", ContentDescriptions.EntryId(bonus));
        var used = TestGame.Advance(restored, new Choice(bonus.Key), new Dice(), false);
        Assert.Equal(new AbilityUses(3, 2), used.State.Units[0].Uses("stable-key", 3));
        Assert.Equal(1, used.State.BonusActionUsesThisActivation("unit:stable-key"));
        Assert.False(used.State.Units[0].AbilityUseCounts.ContainsKey("unit:Battle Cry"));
        Assert.Equal("Battle Cry", Assert.Single(TestGame.OperationEvents(used)).AbilityName);
        Assert.Equal(5, used.State.EffectiveAtkOf("actor"));
        var renamed = type with { LocalAbilities = [type.LocalAbilities[0] with { Name = "New wording" }] };
        used.State.Types[0] = renamed;
        Assert.Equal("New wording", Assert.Single(renamed.CardEntries(), e => e.Id == entry.Id).Name);
        Assert.Equal(new AbilityUses(3, 2), renamed.UsesFor(used.State.Units[0], entry.Id));
        Assert.DoesNotContain(GameEngine.RefreshChoices(Restore(used.State), new Dice(), false).NextInput!.Candidates,
            c => c.Ability?.Definition.Timing == AbilityTiming.BonusAction);
    }

    [Fact]
    public void AuthoredHealAmountAndUsesDriveResolutionAndDescriptionAfterStateRoundTrip()
    {
        var type = UnitType.Define("medic", "Medic", Stats(1, 1, 3, 2, 5), Unique(),
            Ability("Heal", Uses(4), Heal(amount: 3), id: "heal"));
        var state = State(type);
        state.Units[1] = state.Units[1] with { SideId = "blue", CurrentHp = 1 };
        var started = TestGame.StartRound(state, new Dice(), false);
        var ready = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
        var restored = Restore(ready.State);
        var heal = Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "ability:heal");
        Assert.Equal("Heal an adjacent damaged friendly Unit for 3 HP.", heal.Description);
        Assert.Equal("4/game", heal.UseLimitText);
        var healed = TestGame.Advance(restored, new Choice("ability:heal:target"), new Dice(), false);
        Assert.Equal(4, healed.State.Units[1].CurrentHp);
        Assert.Equal(3, Assert.Single(TestGame.OperationEvents(healed), e => e.Kind == "HealResolved").Healing);
        Assert.Equal(new AbilityUses(4, 3), healed.State.Units[0].Uses("heal", 4));
    }

    [Fact]
    public void DuplicateConcreteMechanicsAndBonusIdsAreRejectedRatherThanOverwritten()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("Heal", Uses(2), Heal(2), id: "heal"), Ability("Heal", Uses(3), Heal(3), id: "heal")));
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("Fury", Unlimited(), Fury(1, 2), id: "resolve"), Ability("Other", Unlimited(), Fury(2, 3), id: "resolve")));
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("same", Uses(2), BonusActionSelfModifier(Modifier(Atk, 1)), id: "same"),
            Ability("same", Uses(2), BonusActionSelfModifier(Modifier(Mov, 1)), id: "same")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Uses(0));
    }

    [Fact]
    public void TypedHelpersPreserveAlternateParametersAndExplicitUseLimits()
    {
        var type = UnitType.Define("configured", "Configured", Stats(1, 4, 3, 2, 5), Unique(),
            Ability("Try Open Door", Unlimited(), TryOpenDoor(successes: 5), id: "try-door"),
            Ability("Move After Attack", Unlimited(), MoveAfterAttack(maxMove: 3), id: "move-after-attack"),
            Ability("Undying", Unlimited(), Undying(), id: "undying"),
            Ability("Fury", Unlimited(), Fury(atkBonus: 3, adjacentEnemies: 4), id: "resolve"), Ability("Backstab", Unlimited(), Backstab(atkBonus: 2), id: "flank"),
            Ability("Heal", Uses(7), Heal(amount: 3), id: "heal"),
            Ability("Cleave", Uses(4), Cleave(triggerDamage: 5, damage: 2), id: "cleave"),
            Ability("Holy Wave", Uses(6), HolyWave(), id: "holy-wave"), Ability("Fireball", Uses(8), Fireball(), id: "fireball"),
            Ability("Ward", Unlimited(), AdjacentFriendliesDefenceBonus(2), id: "ward"));
        var restored = Restore(State(type));
        var configuration = restored.Types[0];
        Assert.Equal(new TryOpenDoor(5), configuration.Mechanic<TryOpenDoor>());
        Assert.Equal(new MoveAfterAttack(3), configuration.Mechanic<MoveAfterAttack>());
        Assert.NotNull(configuration.Mechanic<Undying>());
        Assert.Equal(new Fury { AtkBonus = 3, AdjacentEnemyThreshold = 4 }, configuration.Mechanic<Fury>());
        Assert.Equal(new Backstab { AtkBonus = 2 }, configuration.Mechanic<Backstab>());
        Assert.Equal(new HealMechanic(3), configuration.Mechanic<HealMechanic>());
        Assert.Equal(new Cleave { TriggerDamage = 5, Damage = 2 }, configuration.Mechanic<Cleave>());
        Assert.Equal(new HolyWaveMechanic(), configuration.Mechanic<HolyWaveMechanic>());
        Assert.Equal(new FireballMechanic(), configuration.Mechanic<FireballMechanic>());
        Assert.Equal(new[] { new ModifierThisTurn(Stat.Def, 2) }, configuration.Mechanic<AdjacentModifiersMechanic>()!.Modifiers);
        Assert.Equal(new AbilityUses(7, 7), restored.Units[0].Uses("heal", 7));
        Assert.Equal(new AbilityUses(4, 4), restored.Units[0].Uses("cleave", 4));
        Assert.Equal(new AbilityUses(6, 6), restored.Units[0].Uses("holy-wave", 6));
        Assert.Equal(new AbilityUses(8, 8), restored.Units[0].Uses("fireball", 8));
        Assert.Contains(configuration.CardEntries(), e => e.Id == "ability:heal" && e.Description.Contains("3 HP"));
        Assert.Contains(configuration.CardEntries(), e => e.Id == "ability:cleave" && e.Description.Contains("5 or more") && e.Description.Contains("2 damage"));
        Assert.Contains(configuration.CardEntries(), e => e.Name == "Ward" && e.Description.Contains("DEF +2"));
    }

    [Fact]
    public void CanonicalFactoriesPreserveLocalAbilityPayloadsAndCustomOwnerIds()
    {
        Func<string, UnitType>[] factories = [UnitType.Grunt, UnitType.Zombie, UnitType.SkeletonArcher,
            UnitType.Goblin, UnitType.BombImp, UnitType.Troll, UnitType.Shaman, UnitType.Geomancer,
            UnitType.Barbarian, UnitType.Rogue, UnitType.Cleric, UnitType.Wizard];
        foreach (var factory in factories)
        {
            var type = factory("custom-id");
            Assert.Equal("custom-id", type.Id);
            var restored = JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type))!;
            Assert.Equal(JsonSerializer.Serialize(type), JsonSerializer.Serialize(restored));
            Assert.Equal(type.CardEntries(), restored.CardEntries());
            Assert.Equal(type.LocalAbilities.Select(a => a.Key), restored.LocalAbilities.Select(a => a.Key));
            Assert.Empty(restored.CreateUnit("figure", "side").AbilityUseCounts);
            AbilityContent.Validate(restored.LocalAbilities, AbilitySourceKind.Unit);
        }
    }
    private static GameState State(UnitType type) => new()
    {
        Physical = new(new Board(3, 2, []), [new("actor", new(0, 0)), new("target", new(1, 0))]),
        Types = [type, new("target-type", 0, 0, 0, 0, 6) { Unique = true }],
        Units = [type.CreateUnit("actor", "blue"), new("target", "target-type", "red", 6)]
    };
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
