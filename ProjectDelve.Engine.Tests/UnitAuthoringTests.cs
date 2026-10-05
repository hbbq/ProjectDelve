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
        var type = UnitType.Define("ordinary", "Ordinary", Stats(2, 3, 4, 5, 6));
        Assert.Equal("ordinary", type.Id);
        Assert.Equal("Ordinary", type.DisplayName);
        Assert.Equal((2, 3, 4, 5, 6), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal("attack", Assert.Single(type.CardEntries()).Id);
        Assert.Empty(type.BonusActions);
        Assert.Empty(type.Passives);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Equal(UnitAction.NormalAttack, UnitRoster.Grunt().Actions);
    }

    [Fact]
    public void NamedUnlimitedAbilitiesPreserveCategoriesAndHaveNoUseCounters()
    {
        var type = UnitType.Define("named", "Named", Stats(1, 4, 3, 2, 5),
            Ability("Distant Touch", Unlimited(), Telekinesis()),
            Ability("Resolve", Unlimited(), Fury(atkBonus: 2, adjacentEnemies: 1)),
            Ability("Flank", Unlimited(), Backstab(atkBonus: 3)),
            Ability("Ward", Unlimited(), AdjacentFriendliesDefenceBonus(2)),
            Ability("Endurance", Unlimited(), Undying()));
        var state = Restore(State(type));
        var entries = state.Types[0].CardEntries().ToDictionary(e => e.Id);
        Assert.Equal("Distant Touch", entries["telekinesis"].Name);
        Assert.Equal("Action", entries["telekinesis"].Category);
        Assert.Equal("Resolve", entries["passive:Fury"].Name);
        Assert.Equal("Flank", entries["passive:Backstab"].Name);
        Assert.Equal("Ward", entries["passive:Aura"].Name);
        Assert.Equal("Endurance", entries["undying"].Name);
        Assert.Equal("Capability", entries["undying"].Category);
        Assert.All(entries.Values, entry =>
        {
            Assert.Null(entry.MaxUses);
            Assert.Null(entry.UseLimitText);
            Assert.Null(state.Types[0].UsesFor(state.Units[0], entry.Id));
        });
        Assert.Empty(state.Units[0].BonusActionUses);
        Assert.Null(state.Units[0].HealUses);
        Assert.Null(state.Units[0].CleaveUses);
        Assert.Equal(5, state.EffectiveAtkOf("actor")); // Fury still derives from adjacency.
        Assert.Equal(3, state.Types[0].Atk);
        state.MoveDone = true;
        state.CurrentUnitId = "actor";
        Assert.Contains(GameEngine.GameplayCandidates(state, state.Units[0]), c => c.Action == UnitAction.Telekinesis);
        Assert.All(new[] { "passive:Fury", "passive:Backstab", "passive:Aura" }, id => Assert.Equal("Passive", entries[id].Category));
    }

    [Fact]
    public void RenamedLimitedAbilityKeepsMechanicalEntryAndCounterIdentity()
    {
        var type = UnitType.Define("medic", "Medic", Stats(1, 1, 3, 2, 5),
            Ability("Mend", Uses(4), Heal(amount: 3)));
        var state = State(type);
        state.Units[1] = state.Units[1] with { SideId = "blue", CurrentHp = 1 };
        var started = GameEngine.StartRound(state, new Dice(), false);
        var ready = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var entry = Assert.Single(ready.State.Types[0].CardEntries(), e => e.Id == "heal");
        Assert.Equal("Mend", entry.Name);
        Assert.Equal("4/game", entry.UseLimitText);
        var healed = GameEngine.Advance(Restore(ready.State), new Choice("heal:target"), new Dice(), false);
        Assert.Equal(3, Assert.Single(healed.Events, e => e.Kind == "HealResolved").Healing);
        Assert.Equal("Mend", Assert.Single(healed.Events, e => e.Kind == "HealResolved").AbilityName);
        Assert.Equal(new AbilityUses(4, 3), healed.State.Units[0].HealUses);
        Assert.Equal("heal", ContentDescriptions.EntryId(new("unparsed", Action: UnitAction.Heal)));
    }

    [Fact]
    public void CantAttackOnlyRemovesNormalAttackAndDoesNotFreezeEffectiveStats()
    {
        var type = UnitType.Define("non-attacker", "Non-attacker", Stats(1, 4, 3, 2, 5), CantAttack(), Ability("Telekinesis", Unlimited(), Telekinesis()));
        var state = State(type);
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.ModifiersThisTurn.AddRange([new(Stat.Atk, 2), new(Stat.Rng, 3)]);
        var restored = Restore(state);
        Assert.Equal(UnitAction.Telekinesis, restored.Types[0].Actions);
        Assert.Equal(3, restored.Types[0].Atk);
        Assert.Equal(4, restored.Types[0].Rng);
        Assert.Equal(5, restored.EffectiveAtkOf("actor"));
        Assert.Equal(7, restored.EffectiveRngOf("actor"));
        var choices = GameEngine.GameplayCandidates(restored, restored.Units[0]).ToArray();
        Assert.DoesNotContain(choices, c => c.Action == UnitAction.NormalAttack);
        Assert.Contains(choices, c => c.Action == UnitAction.Telekinesis);
        Assert.Equal(UnitAction.SummonAdjacent, UnitRoster.Shaman().Actions);
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
        var type = UnitType.Define("custom", "Custom", Stats(1, 1, 3, 2, 5),
            Ability("Battle Cry", Uses(3), BonusActionSelfModifier(Modifier(Atk, +2)), id: "stable-key"));
        var started = GameEngine.StartRound(State(type), new Dice(), false);
        var restored = Restore(started.State);
        var entry = Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "bonus:stable-key");
        Assert.Equal("Battle Cry", entry.Name);
        Assert.Equal("+2 ATK this turn", entry.Description);
        Assert.Equal("3/game", entry.UseLimitText);
        var bonus = Assert.Single(restored.Pending!.Candidates, c => c.BonusAction is not null);
        Assert.Equal("bonus-action:stable-key", bonus.Key);
        Assert.Equal("bonus:stable-key", ContentDescriptions.EntryId(bonus));
        var used = GameEngine.Advance(restored, new Choice(bonus.Key), new Dice(), false);
        Assert.Equal(new AbilityUses(3, 2), used.State.Units[0].BonusActionUses["stable-key"]);
        Assert.Contains("stable-key", used.State.BonusActionsUsedThisActivation);
        Assert.False(used.State.Units[0].BonusActionUses.ContainsKey("Battle Cry"));
        Assert.Equal("Battle Cry", Assert.Single(used.Events).AbilityName);
        Assert.Equal(5, used.State.EffectiveAtkOf("actor"));
        var renamed = type with { BonusActions = [type.BonusActions[0] with { DisplayName = "New wording" }] };
        used.State.Types[0] = renamed;
        Assert.Equal("New wording", Assert.Single(renamed.CardEntries(), e => e.Id == entry.Id).Name);
        Assert.Equal(new AbilityUses(3, 2), renamed.UsesFor(used.State.Units[0], entry.Id));
        Assert.DoesNotContain(GameEngine.RefreshChoices(Restore(used.State), new Dice(), false).NextInput!.Candidates,
            c => c.BonusAction is not null);
    }

    [Fact]
    public void AuthoredHealAmountAndUsesDriveResolutionAndDescriptionAfterStateRoundTrip()
    {
        var type = UnitType.Define("medic", "Medic", Stats(1, 1, 3, 2, 5),
            Ability("Heal", Uses(4), Heal(amount: 3)));
        var state = State(type);
        state.Units[1] = state.Units[1] with { SideId = "blue", CurrentHp = 1 };
        var started = GameEngine.StartRound(state, new Dice(), false);
        var ready = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var restored = Restore(ready.State);
        var heal = Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "heal");
        Assert.Equal("Restore up to 3 HP to an adjacent damaged friendly Unit.", heal.Description);
        Assert.Equal("4/game", heal.UseLimitText);
        var healed = GameEngine.Advance(restored, new Choice("heal:target"), new Dice(), false);
        Assert.Equal(4, healed.State.Units[1].CurrentHp);
        Assert.Equal(3, Assert.Single(healed.Events, e => e.Kind == "HealResolved").Healing);
        Assert.Equal(new AbilityUses(4, 3), healed.State.Units[0].HealUses);
    }

    [Fact]
    public void DuplicateConcreteMechanicsAndBonusIdsAreRejectedRatherThanOverwritten()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("Heal", Uses(2), Heal(2)), Ability("Heal", Uses(3), Heal(3))));
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("Fury", Unlimited(), Fury(1, 2)), Ability("Other", Unlimited(), Fury(2, 3))));
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", Stats(1, 1, 1, 1, 1),
            Ability("same", Uses(2), BonusActionSelfModifier(Modifier(Atk, 1)), id: "same"),
            Ability("same", Uses(2), BonusActionSelfModifier(Modifier(Mov, 1)), id: "same")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Uses(0));
    }

    [Fact]
    public void TypedHelpersPreserveAlternateParametersAndExplicitUseLimits()
    {
        var type = UnitType.Define("configured", "Configured", Stats(1, 4, 3, 2, 5),
            Ability("Try Open Door", Unlimited(), TryOpenDoor(successes: 5)),
            Ability("Move After Attack", Unlimited(), MoveAfterAttack(maxMove: 3)),
            Ability("Undying", Unlimited(), Undying()),
            Ability("Fury", Unlimited(), Fury(atkBonus: 3, adjacentEnemies: 4)), Ability("Backstab", Unlimited(), Backstab(atkBonus: 2)),
            Ability("Heal", Uses(7), Heal(amount: 3)),
            Ability("Cleave", Uses(4), Cleave(triggerDamage: 5, damage: 2)),
            Ability("Holy Wave", Uses(6), HolyWave()), Ability("Fireball", Uses(8), Fireball()),
            Ability("Ward", Unlimited(), AdjacentFriendliesDefenceBonus(2)));
        var restored = Restore(State(type));
        var configuration = restored.Types[0];
        Assert.Equal(new TryOpenDoor(5), configuration.TryOpenDoor);
        Assert.Equal(new MoveAfterAttack(3), configuration.MoveAfterAttack);
        Assert.NotNull(configuration.Undying);
        Assert.Equal(new Fury { AtkBonus = 3, AdjacentEnemyThreshold = 4 }, configuration.Fury);
        Assert.Equal(new Backstab { AtkBonus = 2 }, configuration.Backstab);
        Assert.Equal(new Heal(7) { Amount = 3 }, configuration.Heal);
        Assert.Equal(new Cleave(4) { TriggerDamage = 5, Damage = 2 }, configuration.Cleave);
        Assert.Equal(new HolyWave(6), configuration.HolyWave);
        Assert.Equal(new Fireball(8), configuration.Fireball);
        Assert.Equal(new AdjacentFriendlyUnitsDefenceBonus(2), configuration.AdjacentFriendlyUnitsDefenceBonus);
        Assert.Equal(new AbilityUses(7, 7), restored.Units[0].HealUses);
        Assert.Equal(new AbilityUses(4, 4), restored.Units[0].CleaveUses);
        Assert.Equal(new AbilityUses(6, 6), restored.Units[0].HolyWaveUses);
        Assert.Equal(new AbilityUses(8, 8), restored.Units[0].FireballUses);
        Assert.Contains(configuration.CardEntries(), e => e.Id == "heal" && e.Description.Contains("3 HP"));
        Assert.Contains(configuration.CardEntries(), e => e.Id == "cleave" && e.Description.Contains("5 or more") && e.Description.Contains("2 damage"));
        Assert.Contains(configuration.CardEntries(), e => e.Name == "Ward" && e.Description.Contains("DEF +2"));
    }

    [Fact]
    public void AllCanonicalFactoriesPreserveCompleteRuntimeContentAndCustomIds()
    {
        // Pre-refactor runtime content is the independent parity fixture.
        (Func<string, UnitType> Factory, UnitType Expected)[] content =
        [
            (UnitType.Grunt, new("grunt-type", 3, 1, 3, 3, 1) { DisplayName = "Grunt" }),
            (UnitType.Zombie, new("zombie-type", 2, 1, 3, 3, 1, TryOpenDoor: new(2),
                Behaviors: UnitBehavior.ApproachThroughClosedDoors) { DisplayName = "Zombie" }),
            (UnitType.SkeletonArcher, new("skeleton-archer-type", 3, 4, 3, 3, 1,
                Behaviors: UnitBehavior.MaximizeAttackDistance) { DisplayName = "Skeleton Archer" }),
            (UnitType.Goblin, new("goblin-type", 4, 1, 2, 2, 1, MoveAfterAttack: new(1),
                Behaviors: UnitBehavior.BackAwayAfterAttack) { DisplayName = "Goblin" }),
            (UnitType.Troll, new("troll-type", 2, 1, 4, 4, 1, TryOpenDoor: new(4),
                Behaviors: UnitBehavior.ApproachThroughClosedDoors) { DisplayName = "Troll", Undying = new() }),
            (UnitType.Shaman, new("shaman-type", 2, 0, 0, 3, 1, Actions: UnitAction.SummonAdjacent,
                Behaviors: UnitBehavior.Flee | UnitBehavior.UseSummon) { DisplayName = "Shaman", SummonAdjacent = new(UnitTypeIds.Goblin, Posture.Lying),
                AbilityNames = new() { Summon = "Summon Goblin" } }),
            (UnitType.Barbarian, new("barbarian-type", 3, 1, 4, 3, 5, FreeActions: UnitFreeAction.OpenDoor)
            {
                DisplayName = "Barbarian", Fury = new() { AtkBonus = 1, AdjacentEnemyThreshold = 2 },
                Cleave = new(2) { TriggerDamage = 2, Damage = 1 }, BonusActions = [new("Rage", 2, [new(Stat.Atk, 2)])]
            }),
            (UnitType.Rogue, new("rogue-type", 4, 1, 3, 2, 4, FreeActions: UnitFreeAction.OpenDoor)
            {
                DisplayName = "Rogue", Backstab = new() { AtkBonus = 1 },
                BonusActions = [new("Dash", 2, [new(Stat.Mov, 2)]), new("Throwing Knife", 2, [new(Stat.Rng, 2), new(Stat.Atk, -1)])]
            }),
            (UnitType.Cleric, new("cleric-type", 3, 1, 3, 3, 4,
                Actions: UnitAction.NormalAttack | UnitAction.Heal | UnitAction.HolyWave, FreeActions: UnitFreeAction.OpenDoor)
            {
                DisplayName = "Cleric", Heal = new(2) { Amount = 2 }, HolyWave = new(2), AdjacentFriendlyUnitsDefenceBonus = new(1, "Aura")
            }),
            (UnitType.Wizard, new("wizard-type", 2, 4, 3, 2, 4,
                Actions: UnitAction.NormalAttack | UnitAction.Fireball | UnitAction.Telekinesis, FreeActions: UnitFreeAction.OpenDoor)
            {
                DisplayName = "Wizard", Fireball = new(2), BonusActions = [new("Focus", 2, [new(Stat.Atk, 1)])]
            })
        ];
        foreach (var (factory, expected) in content)
        {
            var actual = factory(expected.Id);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.Equal(expected.CardEntries(), actual.CardEntries());
            Assert.Equal(JsonSerializer.Serialize(expected.CreateUnit("figure", "side")),
                JsonSerializer.Serialize(actual.CreateUnit("figure", "side")));
            var custom = factory("custom-id");
            Assert.Equal("custom-id", custom.Id);
            Assert.Equal(JsonSerializer.Serialize(expected with { Id = "custom-id" }), JsonSerializer.Serialize(custom));
            Assert.Equal(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(actual))));
        }
    }

    private static GameState State(UnitType type) => new()
    {
        Physical = new(new Board(3, 2, []), [new("actor", new(0, 0)), new("target", new(1, 0))]),
        Types = [type, new("target-type", 0, 0, 0, 0, 6)],
        Units = [type.CreateUnit("actor", "blue"), new("target", "target-type", "red", 6)]
    };
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
