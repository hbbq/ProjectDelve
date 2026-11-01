using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ContentDefinitionTests
{
    private static UnitType Summoner(string id, string target) => new(id, 1, 1, 1, 1, 1)
    { LocalAbilities = [new("summon", "Summon", AbilityTiming.Action, new SummonAdjacent(target, Posture.Lying))] };

    [Fact]
    public void StandaloneMechanicsUseExistingEnvelopeAndParameterContracts()
    {
        var equipment = new EquipmentDefinition("bell", "Bell")
        { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new HealMechanic(2))] };
        Assert.Empty(ContentDefinitions.Validate(new(equipment)));
        Assert.NotEmpty(ContentDefinitions.Validate(new(equipment with
        { LocalAbilities = [new("heal", "Heal", AbilityTiming.Passive, new HealMechanic(2))] })));
        Assert.NotEmpty(ContentDefinitions.Validate(new(equipment with
        { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new HealMechanic(0))] })));
        Assert.NotEmpty(ContentDefinitions.Validate(new(new ItemDefinition("item", "Item"))));
        Assert.NotEmpty(ContentDefinitions.Validate(new(new UnitType("bad", -1, 0, 0, 0, 1))));
        Assert.NotEmpty(ContentDefinitions.Validate(new(new LootCardDefinition("bad", "Bad"))));
        Assert.Empty(ContentDefinitions.Validate(new(MachineTypes.DiamondPress)));
        Assert.Empty(ContentDefinitions.Validate(new(ResourceTypes.Coal)));
    }

    [Fact]
    public void MissingRewardsAndFamilyMismatchesKeepProvenance()
    {
        var source = new ContentSource("cards.json", "my-package", "loot[3]");
        var card = new LootCardDefinition("reward", "Reward") { EquipmentTypeId = "missing" };
        var graph = new ContentDependencyGraph([new(card, source), new(new ItemDefinition("missing", "Wrong family"))]);
        var error = Assert.Single(graph.Diagnostics.Where(d => d.Owner.TypeId == "reward"));
        Assert.Equal("familyMismatch", error.Code);
        Assert.Equal("equipmentTypeId", error.Path);
        Assert.Equal(source, error.Source);
        var invalidAbility = new EquipmentDefinition("invalid", "Invalid")
        { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new HealMechanic(0))] };
        Assert.Equal("localAbilities[0].mechanic.amount", Assert.Single(ContentDefinitions.Validate(new(invalidAbility))).Path);
        Assert.Equal(new(ContentFamily.Equipment, "missing"), error.Dependency);
        Assert.Equal("missingReference", Assert.Single(new ContentDependencyGraph([new(card)]).Diagnostics).Code);
    }

    [Fact]
    public void EquipmentAndLootReachScopedSummonsThroughCyclesDeterministically()
    {
        var equipment = new EquipmentDefinition("bell", "Bell") { LocalAbilities = Summoner("unused", "a").LocalAbilities };
        var loot = new LootCardDefinition("loot", "Loot") { EquipmentTypeId = equipment.Id };
        ContentDefinition[] definitions = [new(loot), new(equipment), new(Summoner("a", "b")), new(Summoner("b", "a")), new(Summoner("self", "self"))];
        var graph = new ContentDependencyGraph(definitions);
        Assert.Empty(graph.Diagnostics);
        var root = new ContentIdentity(ContentFamily.LootCard, "loot");
        Assert.Equal(new ContentIdentity[] {new(ContentFamily.Unit, "a"), new(ContentFamily.Unit, "b"), new(ContentFamily.Equipment, "bell")}, graph.Closure([root]));
        Assert.Equal(graph.Closure([root]).ToArray(), new ContentDependencyGraph(definitions.Reverse()).Closure([root]).ToArray());
        Assert.Single(graph.Closure([new(ContentFamily.Unit, "self")]));
    }

    [Fact]
    public void TerrainDependenciesPreserveAssemblyRestrictions()
    {
        var surface = new TerrainDefinition("surface", "Surface", true, false);
        var component = new FixedComponentDefinition("object", "Object", false, false);
        var assembly = new TerrainAssemblyDefinition("tile", "Tile", surface.Id) { Components = [new("main", component.Id)] };
        var graph = new ContentDependencyGraph([new(surface), new(component), new(assembly)]);
        Assert.Empty(graph.Diagnostics);
        Assert.Equal(2, graph.Closure([new(ContentFamily.Terrain, "tile")]).Length);
        var nested = assembly with { Id = "nested", SurfaceId = "tile" };
        Assert.Contains(new ContentDependencyGraph([new(surface), new(component), new(assembly), new(nested)]).Diagnostics, d => d.Code == "invalidComposition");
    }

    [Fact]
    public void ResourcesRemainOwnedPayloadsAndImplicitRequirementsAreVisible()
    {
        var machine = MachineTypes.DiamondPress;
        Assert.Contains(ContentDefinitions.Dependencies(machine), d => d.Target.TypeId == "coal" && d.Kind == ContentDependencyKind.Embedded);
        var unit = Summoner("unit", "unit") with { LocalAbilities = [new("troll", "Troll", AbilityTiming.Action, new SummonTroll(1))] };
        var graph = new ContentDependencyGraph([new(unit), new(UnitRoster.Troll())]);
        Assert.Contains(graph.Diagnostics, d => d.Dependency == new ContentIdentity(ContentFamily.Resource, "diamond"));
        Assert.Empty(new ContentDependencyGraph([new(unit), new(UnitRoster.Troll())], [ResourceTypes.Diamond]).Diagnostics);
        var terrain = new TerrainDefinition("producer", "Producer", true, false) { Production = new DirectionalResourceProduction("coal", 1) };
        Assert.Equal("production.resourceTypeId", Assert.Single(ContentDefinitions.Dependencies(terrain)).Path);
    }

    [Fact]
    public void ExistingObjectValidationAgreesWithStandaloneValidation()
    {
        foreach (var equipment in EquipmentCatalog.All)
        {
            Assert.Empty(ContentDefinitions.Validate(new(equipment)));
            AbilityContent.ValidateObjectCatalogs([equipment], [], [], CanonicalUnitTypes.All.ToArray());
        }
        foreach (var unit in CanonicalUnitTypes.All) Assert.Empty(ContentDefinitions.Validate(new(unit)));
        foreach (var item in ItemCatalog.All) Assert.Empty(ContentDefinitions.Validate(new(item)));
        foreach (var card in LootCardCatalog.All) Assert.Empty(ContentDefinitions.Validate(new(card)));
        foreach (var card in WorldCardCatalog.All) Assert.Empty(ContentDefinitions.Validate(new(card)));
        foreach (var terrain in TerrainCatalog.All) Assert.Empty(ContentDefinitions.Validate(new(terrain)));
    }

    [Fact]
    public void CompositionValidationAndDependenciesUseExistingQuantityDomains()
    {
        var deck = new WorldDeckDefinition("deck", [new("card", 2)]);
        Assert.Empty(ContentDefinitions.Validate(new(deck)));
        Assert.Equal(new(ContentFamily.WorldCard, "card"), Assert.Single(ContentDefinitions.Dependencies(deck)).Target);
        Assert.NotEmpty(ContentDefinitions.Validate(new(deck with { Composition = [new("card", 1001)] })));
        Assert.NotEmpty(ContentDefinitions.Validate(new(deck with { Composition = [new("card"), new("card")] })));
        var preset = new LootPreset("preset", "Preset", "", [new() { LootCardId = "reward" }]);
        Assert.Empty(ContentDefinitions.Validate(new(preset)));
        Assert.Equal(new(ContentFamily.LootCard, "reward"), Assert.Single(ContentDefinitions.Dependencies(preset)).Target);
        Assert.NotEmpty(ContentDefinitions.Validate(new(preset with { Composition = [new(Count: 1001)] })));
        var dynamicPreset = preset with { Composition = [], IsDynamic = true };
        var graph = new ContentDependencyGraph([new(dynamicPreset), new(new LootCardDefinition("card", "Card") { Immediate = new HealCollectorMechanic(2) })]);
        Assert.Empty(graph.Diagnostics);
        Assert.Equal(new(ContentFamily.LootCard, "card"), Assert.Single(graph.DirectDependencies(new(ContentFamily.LootPreset, "preset"))).Target);
    }

    [Fact]
    public void StructuralPathsConflictsAndInlineVariantsStayInspectable()
    {
        var source = new ContentSource("test.json", "test", "equipment[2]");
        var error = Assert.Single(ContentDefinitions.Validate(new(new EquipmentDefinition("id", ""), source)));
        Assert.Equal("name", error.Path);
        Assert.Equal(source, error.Source);
        Assert.Contains(new ContentDependencyGraph([new(new EquipmentDefinition("id", "A")), new(new EquipmentDefinition("id", "B"))]).Diagnostics,
            d => d.Code == "conflictingDefinition");
        var unit = Summoner("unit", "unit") with { ResourceStorage = new([new(new("coal", "Different owned name"), 2)]) };
        Assert.Empty(ContentDefinitions.Validate(new(unit)));
        Assert.Equal("Different owned name", Assert.IsType<ResourceType>(Assert.Single(ContentDefinitions.Dependencies(unit).Where(d => d.Kind == ContentDependencyKind.Embedded)).EmbeddedDefinition).Name);
        var cycle = new TerrainAssemblyDefinition("cycle", "Cycle", "cycle");
        Assert.Contains(new ContentDependencyGraph([new(cycle)]).Diagnostics, d => d.Code == "invalidComposition");
    }
}
