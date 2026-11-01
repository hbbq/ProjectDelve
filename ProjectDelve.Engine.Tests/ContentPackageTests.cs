using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ContentPackageTests
{
    private static ContentPackage Package(string id = "example") => new()
    {
        Id = id, Definitions = new()
        {
            Equipment = [new("bell", "Bell") { LocalAbilities =
                [new("stand", "Stand", AbilityTiming.Action, new StandAdjacentFriendlyMechanic()) { Usage = new(2) },
                 new("open", "Open", AbilityTiming.FreeAction, new OpenDoor())] }],
            LootCards = [new("gift", "Gift") { EquipmentTypeId = "bell" }]
        }
    };

    [Fact]
    public void TypedRoundTripIsDeterministicAndPreservesAbilityAndDeckOrder()
    {
        var original = Package() with { Definitions = Package().Definitions with
        {
            WorldCards = [new("second", "Second", WorldCardLifecycle.Immediate) { Immediate = new FullyHealUnitsMechanic() },
                new("first", "First", WorldCardLifecycle.Immediate) { Immediate = new LayDownAllUnitsMechanic() }],
            DefaultWorldDeck = [new("second", 2), new("first", 1)]
        } };
        var json = ContentPackageJson.ToJson(original);
        var roundTrip = ContentPackageJson.FromJson(json, "package.json");
        Assert.Equal(json, ContentPackageJson.ToJson(roundTrip));
        Assert.DoesNotContain("\"source\":", json);
        Assert.Equal(new[] { "stand", "open" }, roundTrip.Definitions.Equipment[0].LocalAbilities.Select(a => a.Key));
        Assert.Equal(2, roundTrip.Definitions.Equipment[0].LocalAbilities[0].Usage.MaxUses);
        Assert.Equal(new[] { "second", "first" }, roundTrip.Definitions.DefaultWorldDeck.Select(row => row.TypeId));
        Assert.Equal(2, roundTrip.Definitions.DefaultWorldDeck[0].Count);
        Assert.Single(ContentPackageCatalog.Compose([new(roundTrip)]).Definitions.LootCards);
    }

    [Fact]
    public void JsonAndCSharpUseOneCompositionAndDuplicatesNeverShadow()
    {
        var package = ContentPackageJson.FromJson(ContentPackageJson.ToJson(Package()));
        var combined = ContentPackageCatalog.Compose([new(package, "example.json")], ContentBundle.Canonical());
        Assert.Equal(EquipmentCatalog.All.Count + 1, combined.Definitions.Equipment.Length);
        Assert.Throws<ContentCompositionException>(() => ContentPackageCatalog.Compose([new(package)],
            ContentBundle.Canonical() with { Equipment = [.. EquipmentCatalog.All, package.Definitions.Equipment[0]] }));
        Assert.Throws<ArgumentException>(() => ContentPackageCatalog.Compose([new(package), new(package)]));
        var external = new ContentPackage { Id = "external", Dependencies = [ContentPackageCatalog.CSharpPackageId],
            Definitions = new() { LootCards = [new("external-gift", "External gift") { EquipmentTypeId = EquipmentCatalog.All[0].Id }] } };
        _ = ContentPackageCatalog.Compose([new(external)], ContentBundle.Canonical());
        var diagnostic = Assert.Single(Assert.Throws<ContentCompositionException>(() => ContentPackageCatalog.Compose(
            [new(external with { Dependencies = [] }, "external.json")], ContentBundle.Canonical())).Diagnostics);
        Assert.Equal("undeclaredPackageDependency", diagnostic.Code);
        Assert.Equal("external.json", diagnostic.Source!.Document);
        Assert.Equal(ContentPackageCatalog.CSharpPackageId, diagnostic.RelatedSource!.Package);
    }

    [Fact]
    public void EveryCurrentCanonicalFamilyCanRoundTripAsOneTypedPackage()
    {
        var original = new ContentPackage { Id = "canonical-export", Definitions = ContentBundle.Canonical() };
        var json = ContentPackageJson.ToJson(original);
        var copy = ContentPackageJson.FromJson(json);
        Assert.Equal(json, ContentPackageJson.ToJson(copy));
        var effective = ContentPackageCatalog.Compose([new(copy)]).Definitions;
        Assert.Equal(original.Definitions.Units.Length, effective.Units.Length);
        Assert.Equal(original.Definitions.Terrain.Length, effective.Terrain.Length);
        Assert.Equal(original.Definitions.DefaultWorldDeck.ToArray(), effective.DefaultWorldDeck.ToArray());
    }

    [Theory]
    [InlineData("{\"formatVersion\":99,\"id\":\"x\",\"definitions\":{}}")]
    [InlineData("{\"formatVersion\":1,\"id\":\"\",\"definitions\":{}}")]
    [InlineData("{\"formatVersion\":1,\"id\":\"x\",\"dependencies\":[\"x\"],\"definitions\":{}}")]
    public void UnsupportedHeadersCarryTheirDocumentAndPath(string json)
    {
        var error = Assert.Throws<ArgumentException>(() => ContentPackageJson.FromJson(json, "broken.json"));
        Assert.Equal("broken.json", ContentPackageJson.SourceOf(error)!.Document);
        Assert.NotNull(ScenarioValidationContext.PathOf(error));
    }

    [Fact]
    public void UnknownAndMissingJsonFieldsAndProvenanceAreRejected()
    {
        foreach (var json in new[] { "null", "[]", "{\"id\":\"x\",\"definitions\":{}}",
            "{\"formatVersion\":1,\"id\":\"x\",\"definitions\":{\"source\":{}}}",
            "{\"formatVersion\":1,\"id\":\"x\",\"definitions\":{\"scripts\":[]}}",
            "{\"formatVersion\":1,\"id\":\"x\",\"definitions\":{\"equipmentTypes\":null}}",
            "{\"formatVersion\":1,\"id\":\"x\",\"definitions\":{\"equipmentTypes\":[null]}}",
            "{\"formatVersion\":1,\"id\":\"x\",\"id\":\"y\",\"definitions\":{}}" })
            Assert.Throws<JsonException>(() => ContentPackageJson.FromJson(json));
    }

    [Fact]
    public void MissingRewardAndInvalidMechanicReportPackageDefinitionAndField()
    {
        var missing = Package() with { Definitions = new() { LootCards = [new("gift", "Gift") { EquipmentTypeId = "absent" }] } };
        var error = Assert.Throws<ContentCompositionException>(() => ContentPackageCatalog.Compose([new(missing, "broken.json")]));
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal("missingReference", diagnostic.Code);
        Assert.Equal("equipmentTypeId", diagnostic.Path);
        Assert.Equal("definitions.lootCardDefinitions[0]", diagnostic.Source!.Path);
        Assert.Equal("broken.json", diagnostic.Source.Document);
        Assert.Equal("example", diagnostic.Source.Package);
        Assert.Equal(new(ContentFamily.Equipment, "absent"), diagnostic.Dependency);
        var invalid = Package() with { Definitions = new() { Equipment = [new("invalid", "Invalid")
            { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new HealMechanic(0))] }] } };
        Assert.Equal("localAbilities[0].mechanic.amount", Assert.Single(Assert.Throws<ContentCompositionException>(() =>
            ContentPackageCatalog.Compose([new(invalid)])).Diagnostics).Path);
    }

    [Fact]
    public void CrossPackageReferencesRequireDeclaredProvidersAndPermitMutualSummons()
    {
        UnitType Summon(string id, string target) => new(id, 1, 0, 0, 0, 1)
            { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(target, Posture.Lying))] };
        var a = new ContentPackage { Id = "a", Dependencies = ["b"], Definitions = new() { Units = [Summon("a-type", "b-type")] } };
        var b = new ContentPackage { Id = "b", Dependencies = ["a"], Definitions = new() { Units = [Summon("b-type", "a-type")] } };
        var graph = ContentPackageCatalog.Compose([new(b), new(a)]).Dependencies;
        Assert.Equal(2, graph.Closure([new(ContentFamily.Unit, "a-type")]).Length);
        var undeclared = a with { Dependencies = [] };
        Assert.Equal("undeclaredPackageDependency", Assert.Single(Assert.Throws<ContentCompositionException>(() =>
            ContentPackageCatalog.Compose([new(undeclared), new(b)])).Diagnostics).Code);
        Assert.Throws<ArgumentException>(() => ContentPackageCatalog.Compose([new(a)]));
    }

    [Fact]
    public void TerrainCompositionRestrictionsApplyToPackages()
    {
        var package = new ContentPackage { Id = "terrain", Definitions = new()
        { Assemblies = [new("cycle", "Cycle", "cycle")] } };
        Assert.Contains(Assert.Throws<ContentCompositionException>(() => ContentPackageCatalog.Compose([new(package)])).Diagnostics,
            d => d.Code == "invalidComposition");
    }

    [Fact]
    public void ExportIncludesTypedClosureAndRetainsStableIdentities()
    {
        var source = ContentPackageCatalog.Compose([new(Package())]);
        var exported = ContentPackageExport.Selected("copy", source, [new(ContentFamily.LootCard, "gift")]);
        Assert.Equal("bell", Assert.Single(exported.Definitions.Equipment).Id);
        Assert.Equal("gift", Assert.Single(exported.Definitions.LootCards).Id);
        Assert.Empty(ContentPackageCatalog.Compose([new(ContentPackageJson.FromJson(ContentPackageJson.ToJson(exported)))]).Dependencies.Diagnostics);
        var without = ContentPackageExport.Selected("thin", source, [new(ContentFamily.LootCard, "gift")], false, ["example"]);
        Assert.Empty(without.Definitions.Equipment); Assert.Equal("example", Assert.Single(without.Dependencies));
    }

    [Fact]
    public void FileAndDeclarationOrderingRemainDeterministic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "delve-packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cards = Package() with { Id = "cards", Dependencies = ["gear"], Definitions = new() { LootCards = Package().Definitions.LootCards } };
            var gear = Package() with { Id = "gear", Definitions = new() { Equipment = Package().Definitions.Equipment } };
            File.WriteAllText(Path.Combine(directory, "z.json"), ContentPackageJson.ToJson(cards));
            File.WriteAllText(Path.Combine(directory, "a.json"), ContentPackageJson.ToJson(gear));
            var loaded = ContentPackageCatalog.ReadDirectory(directory);
            Assert.Equal(new[] { "a.json", "z.json" }, loaded.Select(p => Path.GetFileName(p.Document)));
            var first = ContentPackageCatalog.Compose(loaded);
            var second = ContentPackageCatalog.Compose(loaded.Reverse());
            Assert.Equal(ContentPackageJson.ToJson(ContentPackageExport.Selected("export", first, [new(ContentFamily.LootCard, "gift")])),
                ContentPackageJson.ToJson(ContentPackageExport.Selected("export", second, [new(ContentFamily.LootCard, "gift")])));
        }
        finally { Directory.Delete(directory, true); }
    }
}
