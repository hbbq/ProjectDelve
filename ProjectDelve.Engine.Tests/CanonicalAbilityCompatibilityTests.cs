using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalAbilityCompatibilityTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static UnitType OldCleric() => new("other-cleric", 3, 1, 3, 3, 4,
        Actions: UnitAction.NormalAttack | UnitAction.Heal | UnitAction.HolyWave,
        FreeActions: UnitFreeAction.OpenDoor)
    {
        Unique = true, DisplayName = "Cleric", CanHaveItems = true, CanHaveEquipment = true, LootDropSuccesses = 1,
        Heal = new(2) { Amount = 2 }, HolyWave = new(2), AdjacentFriendlyUnitsDefenceBonus = new(1)
    };

    private static GameState State(UnitType type, List<AbilityDefinition>? abilities = null)
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 5, Height = 3 },
            UnitTypeIds = [type.Id, "friend", "enemy"],
            UnitTypes = [type, new("friend", 0, 0, 0, 0, 5) { Unique = true }, new("enemy", 0, 0, 0, 0, 1)],
            AbilityDefinitions = abilities,
            Units = [new(type.Id, "blue", new(1, 1), UnitId: "actor"),
                new("friend", "blue", new(0, 1), InitialHp: 1, UnitId: "friend"),
                new("enemy", "red", new(2, 1), UnitId: "enemy")],
            Agency = [new(type.Id, "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human), new("enemy", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
        return GameEngine.CreateGame(definition);
    }

    private static GameState Ready(GameState state)
    {
        state.Round = 1;
        state.CurrentUnitId = "actor";
        state.ActiveToken = new(state.Units[0].TypeId, "blue");
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, state.Units[0].TypeId, "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfPassiveModifiersAffectEveryDeclaredStatFromNativeOrEquipmentSources(bool equipment)
    {
        var ability = new AbilityDefinition("local-passive", "Steady Bearing", AbilityTiming.Passive,
            new SelfModifiersMechanic([new(Stat.Atk, 2), new(Stat.Mov, 1), new(Stat.Rng, 2), new(Stat.Def, 3)]));
        var type = new UnitType("passive-bearer", 2, 1, 1, 1, 1)
        {
            CanHaveEquipment = true,
            AbilityGrants = equipment ? [] : [new("self", ability.Id)]
        };
        var state = State(type, [ability]);
        if (equipment)
        {
            state.EquipmentTypes.Add(new("local-charm", "Small Charm") { Grants = [new("self", ability.Id)] });
            state.Units[0] = state.Units[0] with { Inventory = [new("charm-copy", "local-charm")] };
        }
        state = Restore(state);
        GameEngine.ValidateScenario(state);
        Assert.Equal((3, 3, 3, 4), (state.EffectiveAtkOf("actor"), state.EffectiveMovOf("actor"),
            state.EffectiveRngOf("actor"), state.EffectiveDefOf("actor")));
        Assert.Equal((0, 0, 0, 0), (state.EffectiveAtkOf("friend"), state.EffectiveMovOf("friend"),
            state.EffectiveRngOf("friend"), state.EffectiveDefOf("friend")));
        var figureIndex = state.Physical.Figures.FindIndex(f => f.Id == "actor");
        state.Physical.Figures[figureIndex] = state.Physical.Figures[figureIndex] with { Posture = Posture.Lying };
        Assert.Equal((1, 2, 1, 1), (state.EffectiveAtkOf("actor"), state.EffectiveMovOf("actor"),
            state.EffectiveRngOf("actor"), state.EffectiveDefOf("actor")));
    }

    [Fact]
    public void MigratedClericRetainsNativeChoicesCountersAndPassiveValuesWithAnotherTypeId()
    {
        var legacy = Ready(State(OldCleric()));
        var declaredType = UnitRoster.Cleric("other-cleric");
        Assert.Null(declaredType.Heal);
        Assert.Null(declaredType.HolyWave);
        Assert.Null(declaredType.AdjacentFriendlyUnitsDefenceBonus);
        var declared = Ready(State(declaredType));
        Assert.Equal(legacy.Pending!.Candidates.Select(c => c.Key), declared.Pending!.Candidates.Select(c => c.Key));
        Assert.Equal(OldCleric().CardEntries(), declaredType.CardEntries());
        Assert.Equal(legacy.AbilitiesOf("actor").Select(g => (g.EntryId, g.AccountingId)),
            declared.AbilitiesOf("actor").Select(g => (g.EntryId, g.AccountingId)));
        Assert.Equal(legacy.Units[0].HealUses, declared.Units[0].HealUses);
        Assert.Equal(legacy.Units[0].HolyWaveUses, declared.Units[0].HolyWaveUses);
        Assert.Equal(legacy.EffectiveDefOf("friend"), declared.EffectiveDefOf("friend"));
        var healed = GameEngine.Advance(declared, new Choice("heal:friend"), new Dice(), false);
        Assert.Equal(3, healed.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 1), healed.State.Units[0].HealUses);
        Assert.Empty(healed.State.Units[0].AbilityUseCounts);
        Assert.Equal("heal", Assert.Single(healed.Events.Where(e => e.Kind == "HealResolved")).ActionId);
    }

    [Fact]
    public void BoundCountersAndNamesDoNotSupplyTheReferencedMechanicOrParameters()
    {
        var definition = new AbilityDefinition("quiet-remedy", "Quiet Remedy", AbilityTiming.Action, new HealMechanic(3))
            { Usage = new(4) };
        var type = new UnitType("independent-source", 2, 1, 1, 0, 1, Actions: UnitAction.None)
        { AbilityGrants = [new("first", definition.Id) { Compatibility = new(LegacyAbilityKind.HolyWave) }] };
        var state = Ready(State(type, [definition]));
        Assert.Equal(new AbilityUses(4, 4), state.Units[0].HolyWaveUses);
        var candidate = Assert.Single(state.Pending!.Candidates.Where(c => c.Key == "holy-wave:friend"));
        Assert.IsType<HealMechanic>(candidate.GrantedAbility!.Definition.Mechanic);
        var healed = GameEngine.Advance(state, new Choice(candidate.Key), new Dice(), false);
        Assert.Equal(4, healed.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(4, 3), healed.State.Units[0].HolyWaveUses);
        Assert.True(healed.State.IsUpright("actor"));
        Assert.DoesNotContain(healed.Events, e => e.Kind == "PostureChanged");
    }

    [Fact]
    public void MigratedBonusKeepsRawCountersAndIndependentModernGrantAccountingAcrossSave()
    {
        var type = UnitRoster.Barbarian("other-barbarian") with
        { AbilityGrants = [.. UnitRoster.Barbarian().AbilityGrants, new("extra", "rage")] };
        var state = Ready(State(type));
        Assert.Empty(type.BonusActions);
        var rage = state.Pending!.Candidates.Single(c => c.Key == "bonus-action:Rage");
        Assert.Equal("Rage", rage.GrantedAbility!.AccountingId);
        var result = GameEngine.Advance(state, new Choice(rage.Key), new Dice(), false);
        var restored = Restore(result.State);
        GameEngine.ValidateScenario(restored);
        Assert.Equal(new AbilityUses(2, 1), restored.Units[0].BonusActionUses["Rage"]);
        Assert.Equal(1, restored.BonusActionUseCountsThisActivation["Rage"]);
        Assert.Contains("Rage", restored.BonusActionsUsedThisActivation);
        Assert.Empty(restored.Units[0].AbilityUseCounts);
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == "ability:extra");
        Assert.DoesNotContain(result.NextInput.Candidates, c => c.Key == "bonus-action:Rage");
        Assert.Contains(result.Events, e => e.Kind == "AbilityUsed");
    }

    [Fact]
    public void ModernAccountingCannotCollideWithAMigratedRawBonusKey()
    {
        var ability = new AbilityDefinition("local-strength", "Strength", AbilityTiming.BonusAction,
            new SelfModifiersMechanic([new(Stat.Atk, 1)])) { Usage = new(2) };
        var type = new UnitType("local-bearer", 1, 1, 1, 0, 1)
        {
            AbilityGrants = [new("old", ability.Id) { Compatibility = new(LegacyAbilityKind.BonusAction, "unit:extra") },
                new("extra", ability.Id)]
        };
        var state = Ready(State(type, [ability]));
        var old = state.Pending!.Candidates.Single(c => c.Key == "bonus-action:unit:extra");
        var modern = state.Pending.Candidates.Single(c => c.Key == "ability:extra");
        Assert.Equal("unit:extra", old.GrantedAbility!.AccountingId);
        Assert.Equal("grant:unit:extra", modern.GrantedAbility!.AccountingId);
        var result = GameEngine.Advance(state, new Choice(old.Key), new Dice(), false);
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == modern.Key);
    }

    [Fact]
    public void OldConcreteSnapshotRetainsItsOwnDefinitionsAndSpentCounters()
    {
        var state = Ready(State(OldCleric()));
        state.Units[0] = state.Units[0] with { HealUses = new(2, 1), HolyWaveUses = new(2, 0) };
        state.AbilityDefinitions = [];
        state.EquipmentTypes = [];
        state.ItemTypes = [];
        state.LootCardDefinitions = [];
        var restored = Restore(state);
        GameEngine.ValidateScenario(restored);
        var choices = GameEngine.RefreshChoices(restored, new Dice(), false).NextInput!;
        Assert.Contains(choices.Candidates, c => c.Key == "heal:friend");
        Assert.DoesNotContain(choices.Candidates, c => c.Key == "holy-wave");
        Assert.Empty(restored.Types[0].AbilityGrants);
        Assert.Equal(new AbilityUses(2, 1), restored.Units[0].HealUses);
        Assert.Empty(restored.AbilityDefinitions);
    }

    [Fact]
    public void MigratedCountersInitializeBareUnitsOnlyWhenStartingTheGame()
    {
        var state = State(UnitRoster.Cleric("other-cleric"));
        state.Units[0] = new("actor", "other-cleric", "blue", 4);
        var started = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(new AbilityUses(2, 2), started.State.Units[0].HealUses);
        Assert.Equal(new AbilityUses(2, 2), started.State.Units[0].HolyWaveUses);
        Assert.Null(state.Units[0].HealUses);
    }

    [Fact]
    public void CounterAliasesRejectAmbiguousBindingsAndForeignStorage()
    {
        var type = UnitRoster.Cleric("other-cleric");
        Assert.Throws<ArgumentException>(() => State(type with
        { AbilityGrants = [.. type.AbilityGrants, new("duplicate", "heal") { Compatibility = new(LegacyAbilityKind.Heal) }] }));
        var state = State(type);
        state.Units[0] = state.Units[0] with { HealUses = new(3, 1) };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));
        state = State(type);
        state.Units[0] = state.Units[0] with { AbilityUseCounts = state.Units[0].AbilityUseCounts.Add("unit:heal", 1) };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));
    }

    [Fact]
    public void EquipmentCompatibilityRejectsInventedPrefixesAndSharedInlineAliasCollisions()
    {
        var state = State(OldCleric());
        state.EquipmentTypes.Add(new("test-bell", "Test Bell")
        {
            Grants = [new("ring", "goat-bell-stand-up")
                { Compatibility = new(LegacyAbilityKind.EquipmentAction, CandidatePrefix: "heal") }]
        });
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));

        state.EquipmentTypes[^1] = state.EquipmentTypes[^1] with
        {
            Grants = [new("ring", "goat-bell-stand-up")
                { Compatibility = new(LegacyAbilityKind.EquipmentAction, CandidatePrefix: "goat-bell") }]
        };
        var original = state.EquipmentTypes.FindIndex(t => t.Id == "goat-bell");
        state.EquipmentTypes[original] = new("goat-bell", "Old Bell")
        {
            Abilities = [new("stand-up", "Old Bell", EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit)
                { CandidatePrefix = "goat-bell" }]
        };
        var error = Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));
        Assert.Contains("candidate prefixes must be unique", error.Message);
    }
}
