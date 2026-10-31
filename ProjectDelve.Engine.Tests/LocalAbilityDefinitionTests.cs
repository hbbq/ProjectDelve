using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LocalAbilityDefinitionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static LocalAbilityDefinition Heal(string key = "use", int amount = 2) =>
        new(key, "Printed Name", AbilityTiming.Action, new HealMechanic(amount));
    private static GameState ContentState(UnitType type) => new()
    {
        Physical = new(new Board(1, 1, []), []), Units = [],
        Types = [type], EquipmentTypes = [], ItemTypes = [], LootCardDefinitions = []
    };
    private static UnitType Bearer(params LocalAbilityDefinition[] abilities) =>
        new("bearer", 1, 1, 1, 1, 1) { LocalAbilities = [.. abilities], CanHaveEquipment = true, CanHaveItems = true };
    private static ScenarioDefinition Scenario(UnitType type) => new()
    {
        Board = new() { Width = 4, Height = 3 },
        UnitTypeIds = [type.Id, "target"],
        UnitTypes = [type, new("target", 0, 0, 0, 0, 1)],
        Units = [new(type.Id, "blue", new(1, 1), UnitId: "actor"), new("target", "red", new(2, 1), UnitId: "enemy")],
        Agency = [new(type.Id, "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    [Fact]
    public void LocalKeysAreScopedToOwnersWithIndependentPayloads()
    {
        var first = Bearer(Heal(amount: 3));
        var second = new UnitType("second", 1, 1, 1, 1, 1)
        { LocalAbilities = [new("use", "Different Name", AbilityTiming.BonusAction, new ExtraActionsMechanic(2))] };
        var state = ContentState(first);
        state.Types.Add(second);
        state.EquipmentTypes.Add(new("equipment", "Equipment")
        { LocalAbilities = [new("use", "Equipment Wave", AbilityTiming.Action, new HolyWaveMechanic())] });
        state.ItemTypes.Add(new("item", "Item")
        { LocalAbilities = [new("use", "Item Flame", AbilityTiming.Action, new FireballMechanic())] });
        state.Units = [new("actor", first.Id, "blue", 1)
            { Inventory = [new("equipment-copy", "equipment"), new("item-copy") { ItemTypeId = "item" }] },
            new("other", second.Id, "blue", 1)];
        AbilityContent.ValidateState(state);
        var abilities = state.AbilitiesOf("actor").ToArray();
        Assert.Equal(3, abilities.Length);
        Assert.All(abilities, a => Assert.Equal("use", a.Key));
        Assert.Equal(3, Assert.IsType<HealMechanic>(abilities[0].Definition.Mechanic).Amount);
        Assert.IsType<HolyWaveMechanic>(abilities[1].Definition.Mechanic);
        Assert.IsType<FireballMechanic>(abilities[2].Definition.Mechanic);
        Assert.IsType<ExtraActionsMechanic>(Assert.Single(state.AbilitiesOf("other")).Definition.Mechanic);
    }

    [Fact]
    public void RenamingAndReorderingSameMechanicEntriesPreservesIndependentCountersAndBonusAllowances()
    {
        var first = new LocalAbilityDefinition("first", "Same Printed Name", AbilityTiming.BonusAction,
            new SelfModifiersMechanic([new(Stat.Atk, 2)])) { Usage = new(2) };
        var second = first with { Key = "second" };
        var scenario = Scenario(Bearer(first, second));
        var json = ScenarioDefinitionJson.ToJson(scenario);
        Assert.DoesNotContain("abilityId", json);
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        state.Round = 1; state.CurrentUnitId = "actor"; state.ActiveToken = new("bearer", "blue");
        state.MoveDone = true; state.Pending = new(DecisionKind.Activation, "bearer", "actor", [], false);
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var result = GameEngine.Advance(state, new Choice("ability:first"), new Dice(), false);
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "actor").AbilityUseCounts["unit:first"]);
        var typeIndex = result.State.Types.FindIndex(t => t.Id == "bearer");
        result.State.Types[typeIndex] = result.State.Types[typeIndex] with
        {
            LocalAbilities = [second with { Name = "Renamed Second" }, first with { Name = "Renamed First" }]
        };
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        GameEngine.ValidateScenario(restored);
        var choices = GameEngine.RefreshChoices(restored, new Dice(), false);
        Assert.DoesNotContain(choices.NextInput!.Candidates, c => c.Key == "ability:first");
        Assert.Contains(choices.NextInput.Candidates, c => c.Key == "ability:second" && c.AbilityName == "Renamed Second");
        var usedSecond = GameEngine.Advance(choices.State, new Choice("ability:second"), new Dice(), false);
        var unit = usedSecond.State.Units.Single(u => u.Id == "actor");
        Assert.Equal(1, unit.AbilityUseCounts["unit:first"]);
        Assert.Equal(1, unit.AbilityUseCounts["unit:second"]);
        Assert.Equal(1, usedSecond.State.BonusActionUsesThisActivation("unit:first"));
        Assert.Equal(1, usedSecond.State.BonusActionUsesThisActivation("unit:second"));
        Assert.Equal(5, usedSecond.State.EffectiveAtkOf("actor"));
    }

    [Fact]
    public void InvalidLocalFieldsReportOwnerAndFieldPaths()
    {
        (LocalAbilityDefinition Ability, string Field)[] cases =
        [
            (Heal("") , "key"),
            (Heal() with { Name = "" }, "name"),
            (Heal() with { Timing = (AbilityTiming)99 }, "timing"),
            (Heal() with { Usage = new(0) }, "usage"),
            (Heal() with { Usage = null! }, "usage"),
            (Heal(amount: 0), "mechanic.amount"),
            (Heal() with { DescriptionOverride = "Restore 2 HP." }, "descriptionOverride")
        ];
        foreach (var (ability, field) in cases)
        {
            var error = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(ContentState(Bearer(ability))));
            Assert.Equal("unitTypes[0].localAbilities[0]." + field, ScenarioValidationContext.PathOf(error));
        }
        var duplicate = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(ContentState(Bearer(Heal(), Heal()))));
        Assert.Equal("unitTypes[0].localAbilities[1].key", ScenarioValidationContext.PathOf(duplicate));
    }

    [Fact]
    public void SourcePoliciesApplyToLocalDeclarationsAndNullItemListsHavePrecisePaths()
    {
        var equipment = ContentState(Bearer());
        equipment.EquipmentTypes.Add(new("charged", "Charged")
        { LocalAbilities = [Heal() with { Usage = new(2) }] });
        AbilityContent.ValidateState(equipment);
        equipment.EquipmentTypes[0] = equipment.EquipmentTypes[0] with
        { LocalAbilities = [Heal() with { Usage = new(0) }] };
        var error = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(equipment));
        Assert.Equal("equipmentTypes[0].localAbilities[0].usage", ScenarioValidationContext.PathOf(error));
        var item = ContentState(Bearer());
        item.ItemTypes.Add(new("passive", "Passive")
        { LocalAbilities = [new("passive", "Passive", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Def, 1)]))] });
        error = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(item));
        Assert.Equal("itemTypes[0].localAbilities[0].timing", ScenarioValidationContext.PathOf(error));
        item.ItemTypes[0] = item.ItemTypes[0] with { LocalAbilities = default };
        error = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(item));
        Assert.Equal("itemTypes[0].localAbilities", ScenarioValidationContext.PathOf(error));
        var reserved = ContentState(Bearer());
        reserved.EquipmentTypes.Add(new("bell", "Bell") { LocalAbilities = [Heal("open-door")] });
        AbilityContent.ValidateState(reserved); // Former structural entry names are ordinary owner-local keys.
    }

}
