using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalAbilityRosterTests
{
    [Fact]
    public void CanonicalAbilitiesAreCompleteOwnerLocalDeclarations()
    {
        foreach (var type in CanonicalUnitTypes.All)
        {
            AbilityContent.Validate(type.LocalAbilities, AbilitySourceKind.Unit);
            Assert.Equal(type.LocalAbilities.Length, type.LocalAbilities.Select(a => a.Key).Distinct().Count());
            Assert.All(type.LocalAbilities, ability => Assert.NotNull(MechanicCatalog.FindByPayload(ability.Mechanic)));
        }
        Assert.Equal(new[] { "heal", "holy-wave", "aura" }, UnitRoster.Cleric().LocalAbilities
            .Where(a => a.Mechanic is HealMechanic or HolyWaveMechanic or AdjacentModifiersMechanic).Select(a => a.Key));
        Assert.IsType<FireballMechanic>(UnitRoster.Wizard().LocalAbilities.Single(a => a.Key == "fireball").Mechanic);
        Assert.IsType<SelfModifiersMechanic>(UnitRoster.Barbarian().LocalAbilities.Single(a => a.Key == "rage").Mechanic);
    }

    [Fact]
    public void CanonicalOwnersRoundTripEveryAbilityAndRuntimeDefinition()
    {
        foreach (var type in CanonicalUnitTypes.All)
        {
            var restored = JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type))!;
            Assert.Equal(JsonSerializer.Serialize(type), JsonSerializer.Serialize(restored));
            Assert.Equal(type.CardEntries(), restored.CardEntries());
            Assert.Equal(JsonSerializer.Serialize(type.CreateUnit("original", "side")),
                JsonSerializer.Serialize(restored.CreateUnit("original", "side")));
        }
    }

    [Fact]
    public void OrdinaryAuthoringDeclaresReusableMechanicsWithExplicitLocalUsage()
    {
        var type = UnitType.Define("new-medic", "New Medic", Stats(2, 1, 2, 1, 1),
            LocalAbility(new("prayer", "Prayer", AbilityTiming.Action, new HolyWaveMechanic()) { Usage = new(2) }),
            LocalAbility(new("medicine", "Medicine", AbilityTiming.Action, new HealMechanic(2))),
            LocalAbility(new("ward", "Ward", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 1)]))));
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 2, Height = 2 }, UnitTypeIds = [type.Id], UnitTypes = [type],
            Units = [new(type.Id, "blue", new(0, 0), UnitId: "medic")],
            Agency = [new(type.Id, "blue", ControllerKind.Human)], Loot = LootSettings.Empty
        });
        var abilities = state.AbilitiesOf("medic").ToArray();
        Assert.IsType<HolyWaveMechanic>(abilities.Single(a => a.Key == "prayer").Definition.Mechanic);
        Assert.Equal(new HealMechanic(2), abilities.Single(a => a.Key == "medicine").Definition.Mechanic);
        Assert.Equal(new AbilityUses(2, 2), state.AbilityUsesOf("medic", abilities.Single(a => a.Key == "prayer")));
        Assert.Empty(state.Units[0].AbilityUseCounts);
    }

    [Theory]
    [InlineData("abilityId")]
    [InlineData("compatibility")]
    public void RemovedReferenceAndCompatibilityFieldsAreRejected(string obsoleteField)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(
            new LocalAbilityDefinition("use", "Use", AbilityTiming.Action, new HolyWaveMechanic())))!;
        node[obsoleteField] = "obsolete";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LocalAbilityDefinition>(node.ToJsonString()));
    }

    [Fact]
    public void OwnerKeysAreUniqueWhileSharedMechanicsCanRepeat()
    {
        var first = new LocalAbilityDefinition("same", "First", AbilityTiming.Action, new HealMechanic(1));
        Assert.Throws<ArgumentException>(() => UnitType.Define("invalid", "Invalid", Stats(1, 1, 1, 1, 1),
            LocalAbility(first), LocalAbility(first with { Name = "Second" })));
        var type = UnitType.Define("two-prayers", "Two Prayers", Stats(1, 1, 1, 1, 1),
            LocalAbility(first with { Key = "first" }), LocalAbility(first with { Key = "second" }));
        Assert.Equal(2, type.LocalAbilities.Length);
    }
}
