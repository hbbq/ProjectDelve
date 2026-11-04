using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentLibraryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-content-library-" + Guid.NewGuid().ToString("N"));
    private ContentLibrary Store => new(directory, []);
    private static ContentPackage Gear(string id = "gear", string name = "Bell") => new()
    {
        Id = id, Title = "Gear package", Description = "Independent content",
        Definitions = new() { Equipment = [new("bell", name) { LocalAbilities =
            [new("stand", "Stand", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright)) { Usage = new(2) }] }] }
    };
    private static ContentPackage Gift(string targetPackage = "gear") => new()
    { Id = "gift", Dependencies = [targetPackage], Definitions = new() { LootCards = [new("gift-card", "Gift") { EquipmentTypeId = "bell" }] } };
    private static ContentPackage Summoner(string packageId, string typeId, string targetPackage, string targetType) => new()
    {
        Id = packageId, Dependencies = [targetPackage], Definitions = new() { Units = [new(typeId, 1, 0, 0, 0, 1)
            { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic(targetType, Posture.Lying))] }] }
    };

    [Fact]
    public void CreateListReadAndRestartRetainImmutableRevisionsAndStableLibraryIdentity()
    {
        var identity = Store.Catalog().LibraryId;
        var first = Store.Create(Gear());
        var original = ContentLibraryJson.ToJson(Store.Export("gear"));
        var second = Store.Save("gear", new(Gear(name: "Edited bell"), 1));
        Assert.Equal(1, first.Revision); Assert.Equal(2, second.Revision);
        Assert.NotEqual(first.Digest, second.Digest);
        Assert.Equal(identity, first.LibraryId);
        Assert.Equal(identity, Store.Catalog().LibraryId);
        Assert.Equal(second.Digest, Assert.Single(Store.Catalog().Packages).Digest);
        Assert.Equal("Edited bell", Store.Read("gear").Package.Definitions.Equipment[0].Name);
        Assert.Equal("Bell", Store.Read("gear", 1).Package.Definitions.Equipment[0].Name);
        Assert.Equal(original, ContentLibraryJson.ToJson(Store.Export("gear", 1)));
        Assert.Equal(new long[] { 1, 2 }, Store.History("gear").Select(r => r.Revision));
    }

    [Fact]
    public void StaleCreatesSavesAndArchivesDoNotChangeCommittedBytes()
    {
        Store.Create(Gear());
        Store.Save("gear", new(Gear(name: "Winner"), 1));
        var path = Path.Combine(directory, "library.json"); var before = File.ReadAllBytes(path);
        var stale = Assert.Throws<LibraryException>(() => Store.Save("gear", new(Gear(name: "Stale"), 1)));
        Assert.Equal(409, stale.StatusCode); Assert.Contains("current revision is 2", stale.Message);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.Create(Gear())).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.Archive("gear", 1)).StatusCode);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal("Winner", Store.Read("gear").Package.Definitions.Equipment[0].Name);
    }

    [Fact]
    public async Task IndependentWritersSerializeAndExactlyOneExpectedRevisionWins()
    {
        Store.Create(Gear());
        var writers = Enumerable.Range(0, 4).Select(n => Task.Run(() =>
        {
            try { Store.Save("gear", new(Gear(name: "Writer " + n), 1)); return 200; }
            catch (LibraryException error) { return error.StatusCode; }
        }));
        var results = await Task.WhenAll(writers);
        Assert.Single(results, code => code == 200); Assert.Equal(3, results.Count(code => code == 409));
        Assert.Equal(2, Store.Read("gear").Revision); Assert.Equal(2, Store.History("gear").Length);
    }

    [Fact]
    public void FailedWriteAndOrphanTemporaryFilesRecoverLastCompleteCommit()
    {
        Store.Create(Gear());
        var path = Path.Combine(directory, "library.json"); var before = File.ReadAllBytes(path);
        var limited = new ContentLibrary(directory, [], before.LongLength + 10);
        Assert.Throws<ArgumentException>(() => limited.Save("gear", new(Gear(name: new string('x', 300)), 1)));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        File.WriteAllText(path + ".interrupted.tmp", "{incomplete");
        Assert.Equal(1, Store.Read("gear").Revision);
        Store.Save("gear", new(Gear(name: "After recovery"), 1));
        Assert.Equal(2, Store.Read("gear").Revision);
        Assert.Equal("{incomplete", File.ReadAllText(path + ".interrupted.tmp"));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("digest")]
    [InlineData("version")]
    public void InvalidCommittedDocumentFailsClosedAndDoesNotOverwriteIt(string damage)
    {
        Store.Create(Gear());
        var path = Path.Combine(directory, "library.json"); var backup = File.ReadAllText(path);
        var json = JsonNode.Parse(backup)!;
        if (damage == "digest") json["revisions"]![0]!["package"]!["title"] = "Tampered";
        if (damage == "version") json["formatVersion"] = 99;
        var corrupt = damage == "corrupt" ? "{not json" : json.ToJsonString();
        File.WriteAllText(path, corrupt);
        Assert.Equal(503, Assert.Throws<LibraryException>(() => Store.Create(Gear("other"))).StatusCode);
        Assert.Equal(corrupt, File.ReadAllText(path));
        File.WriteAllText(path, backup);
        Assert.Equal(1, Store.Read("gear").Revision);
    }

    [Fact]
    public void DependenciesArePinnedAndArchivingRetainsEveryReferencedRevision()
    {
        Store.Create(Gear());
        Store.Create(Gift(), [new("gear", 1)]);
        var before = ContentLibraryJson.ToJson(Store.Export("gift"));
        Store.Save("gear", new(Gear(name: "New revision"), 1));
        Store.Archive("gear", 2);
        Assert.DoesNotContain(Store.Catalog().Packages, p => p.PackageId == "gear");
        Assert.True(Store.Catalog(true).Packages.Single(p => p.PackageId == "gear").Archived);
        Assert.Equal(before, ContentLibraryJson.ToJson(Store.Export("gift")));
        Assert.Equal("Bell", Store.ExportPackage("gift").Definitions.Equipment[0].Name);
        Assert.Equal(2, Store.Read("gear").Revision);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.Save("gear", new(Gear(), 2))).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.Create(Gear())).StatusCode);
    }

    [Fact]
    public void PlainPackageAndRevisionArchiveImportsPreserveDefinitionsAndIdentity()
    {
        Store.Import(ContentPackageJson.FromJson(ContentPackageJson.ToJson(Gear())));
        Store.Save("gear", new(Gear(name: "Latest"), 1));
        Store.Create(Gift(), [new("gear", 1)]);
        var exported = Store.Export("gift");
        var text = ContentLibraryJson.ToJson(exported);
        Assert.Equal(text, ContentLibraryJson.ToJson(ContentLibraryJson.FromJson(text)));
        var portable = Store.ExportPackage("gift");
        Assert.Empty(portable.Dependencies);
        Assert.Single(portable.Definitions.Equipment); Assert.Single(portable.Definitions.LootCards);
        Assert.Empty(ContentPackageCatalog.Compose([new(portable)]).Dependencies.Diagnostics);
        var destination = new ContentLibrary(Path.Combine(directory, "destination"), []);
        var imported = destination.Import(ContentLibraryJson.FromJson(text));
        Assert.Equal(exported.Revisions.Single(r => r.Package.Id == "gift").Digest, imported.Digest);
        Assert.Equal(Store.Read("gift").LibraryId, imported.LibraryId);
        Assert.Equal(text, ContentLibraryJson.ToJson(destination.Export("gift")));
        Assert.NotEqual(Store.Catalog().LibraryId, destination.Catalog().LibraryId);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => destination.Import(exported)).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => destination.Import(Gear())).StatusCode);
    }

    [Fact]
    public void ArchiveImportReusesOnlyAnExactDependencyRevisionAndRejectsConflictingHistory()
    {
        Store.Create(Gear()); Store.Create(Gift(), [new("gear", 1)]);
        var destination = new ContentLibrary(Path.Combine(directory, "destination"), []);
        destination.Import(Store.Export("gear"));
        var before = ContentLibraryJson.ToJson(destination.Export("gear"));
        destination.Import(Store.Export("gift"));
        Assert.Equal(before, ContentLibraryJson.ToJson(destination.Export("gear")));
        var other = new ContentLibrary(Path.Combine(directory, "conflicting"), []);
        other.Create(Gear(name: "Different"));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => other.Import(Store.Export("gift"))).StatusCode);
        Assert.DoesNotContain(other.Catalog().Packages, p => p.PackageId == "gift");
    }

    [Fact]
    public void InvalidAndMissingTypedDependenciesUseEngineDiagnosticsWithoutImplicitLibraryLoading()
    {
        Store.Create(Gear());
        Assert.Throws<ArgumentException>(() => Store.Create(Gift()));
        Assert.Throws<LibraryException>(() => Store.Create(Gift(), [new("gear", 99)]));
        var missing = Gift() with { Dependencies = [] };
        var error = Assert.Throws<ContentCompositionException>(() => Store.Create(missing));
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal("missingReference", diagnostic.Code); Assert.Equal("equipmentTypeId", diagnostic.Path);
        Assert.Equal("library:gift@1", diagnostic.Source!.Document);
        Assert.Equal("definitions.lootCardDefinitions[0]", diagnostic.Source.Path);
        Assert.DoesNotContain(Store.Catalog().Packages, p => p.PackageId == "gift");
        var invalid = Gear("invalid") with { Definitions = new() { Equipment = [new("broken", "Broken")
            { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 0))] }] } };
        Assert.Equal("localAbilities[0].mechanic.amount", Assert.Single(Assert.Throws<ContentCompositionException>(() => Store.Create(invalid)).Diagnostics).Path);
        Assert.Throws<ArgumentException>(() => Store.Create(Gear("unsupported") with { FormatVersion = 2 }));
    }

    [Fact]
    public void SelectedProviderCollisionsAndIncompatiblePinnedRevisionsNeverShadow()
    {
        Store.Create(Gear()); Store.Save("gear", new(Gear(name: "Second"), 1));
        Store.Create(Gift(), [new("gear", 1)]);
        var root = new ContentPackage { Id = "root", Dependencies = ["gift", "gear"] };
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.Create(root, [new("gift", 1), new("gear", 2)])).StatusCode);
        var collision = Gift() with { Id = "collision", Definitions = Gear().Definitions };
        Assert.Equal("conflictingDefinition", Assert.Single(Assert.Throws<ContentCompositionException>(() => Store.Create(collision, [new("gear", 1)])).Diagnostics).Code);
        var mismatched = new ContentPackage { Id = "mismatch", Definitions = new() { Items = [new("bell", "Wrong family")
            { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1))] }], LootCards = Gift().Definitions.LootCards } };
        Assert.Equal("familyMismatch", Assert.Single(Assert.Throws<ContentCompositionException>(() => Store.Create(mismatched)).Diagnostics).Code);
    }

    [Fact]
    public void AtomicBatchCreatesLegalMutualUnitCyclesAndRollsBackEveryPackageOnFailure()
    {
        var a = Summoner("a", "a-unit", "b", "b-unit");
        var b = Summoner("b", "b-unit", "a", "a-unit");
        var result = Store.SaveBatch([new(b, 0, [new("a", 1)]), new(a, 0, [new("b", 1)])]);
        Assert.Equal(new[] { "a", "b" }, result.Select(r => r.Package.Id));
        Assert.Equal(2, Store.Export("a").Revisions.Length);
        Assert.Equal(2, Store.ExportPackage("b").Definitions.Units.Length);
        var original = ContentLibraryJson.ToJson(Store.Export("a"));
        Store.SaveBatch([new(a with { Title = "Revised A" }, 1, [new("b", 2)]), new(b, 1, [new("a", 2)])]);
        Assert.Equal(2, Store.Read("a").Revision);
        Assert.Equal(original, ContentLibraryJson.ToJson(Store.Export("a", 1)));
        var before = File.ReadAllBytes(Path.Combine(directory, "library.json"));
        Assert.Throws<LibraryException>(() => Store.SaveBatch([new(Gear(), 0), new(Gift(), 0, [new("gear", 99)])]));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(directory, "library.json")));
        var destination = new ContentLibrary(Path.Combine(directory, "destination"), []);
        destination.Import(Store.Export("a"));
        Assert.Equal(2, destination.ExportPackage("a").Definitions.Units.Length);
    }

    [Fact]
    public void CanonicalDependenciesAreCapturedAndDoNotFollowLaterProviderChanges()
    {
        var provider = Gear("canonical-gear");
        var store = new ContentLibrary(directory, [provider]);
        var first = store.Create(Gift("canonical-gear"));
        Assert.Equal("canonical-gear", Assert.Single(first.CanonicalPackages).Id);
        var changed = new ContentLibrary(directory, [Gear("canonical-gear", "Changed provider")]);
        Assert.Equal("Bell", changed.ExportPackage("gift").Definitions.Equipment[0].Name);
        Assert.Equal(first.Digest, changed.Read("gift").Digest);
        changed.Save("gift", new(Gift("canonical-gear"), 1));
        Assert.Equal("Changed provider", changed.ExportPackage("gift", 2).Definitions.Equipment[0].Name);
        Assert.Equal("Bell", changed.ExportPackage("gift", 1).Definitions.Equipment[0].Name);
        var absent = new ContentLibrary(directory, []);
        Assert.Equal("Bell", absent.ExportPackage("gift", 1).Definitions.Equipment[0].Name);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => store.Create(provider)).StatusCode);
    }

    [Fact]
    public void ConflictingCanonicalContextsAndInvalidTerrainAreRejectedBeforeCommit()
    {
        var first = new ContentLibrary(directory, [Gear("canonical-gear")]);
        first.Create(Gift("canonical-gear"));
        var changed = new ContentLibrary(directory, [Gear("canonical-gear", "Changed")]);
        changed.Create(Gift("canonical-gear") with { Id = "other-gift", Definitions = new()
            { LootCards = [new("other-card", "Other gift") { EquipmentTypeId = "bell" }] } });
        Assert.Equal(409, Assert.Throws<LibraryException>(() => changed.Create(new ContentPackage
            { Id = "both", Dependencies = ["gift", "other-gift"] }, [new("gift", 1), new("other-gift", 1)])).StatusCode);
        var terrain = new ContentPackage { Id = "invalid-terrain", Definitions = new()
            { Assemblies = [new("cycle", "Cycle", "cycle")] } };
        Assert.Contains(Assert.Throws<ContentCompositionException>(() => Store.Create(terrain)).Diagnostics, d => d.Code == "invalidComposition");
        Assert.DoesNotContain(changed.Catalog().Packages, p => p.PackageId == "both" || p.PackageId == "invalid-terrain");
    }

    [Fact]
    public void CanonicalClosureExportContainsRequiredDefinitionsAndPreservesParameters()
    {
        var store = new ContentLibrary(directory);
        var package = new ContentPackage { Id = "canonical-call", Dependencies = [ContentPackageCatalog.CSharpPackageId], Definitions = new()
        { Equipment = [new("call-goblin", "Call Goblin") { LocalAbilities =
            [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic(UnitTypeIds.Goblin, Posture.Lying)) { Usage = new(3) }] }] } };
        var saved = store.Create(package);
        Assert.Equal(ContentPackageCatalog.CSharpPackageId, Assert.Single(saved.CanonicalPackages).Id);
        var exported = store.ExportPackage("canonical-call");
        Assert.Equal(UnitTypeIds.Goblin, Assert.Single(exported.Definitions.Units).Id);
        Assert.Equal(3, Assert.Single(exported.Definitions.Equipment).LocalAbilities[0].Usage.MaxUses);
        Assert.Empty(ContentPackageCatalog.Compose([new(exported)]).Dependencies.Diagnostics);
        var destination = new ContentLibrary(Path.Combine(directory, "destination"), []);
        destination.Import(store.Export("canonical-call"));
        Assert.Equal(ContentPackageJson.ToJson(exported), ContentPackageJson.ToJson(destination.ExportPackage("canonical-call")));
    }

    [Fact]
    public void ReturnedAndInputMutableCollectionsCannotChangeStoredRevision()
    {
        List<LootCardQuantity> composition = [new() { LootCardId = "gift-card" }];
        var package = Gear() with { Definitions = Gear().Definitions with
        { LootCards = [new("gift-card", "Gift") { EquipmentTypeId = "bell" }], LootPresets = [new("custom", "Custom", "Test preset", composition)] } };
        var saved = Store.Create(package);
        composition.Clear();
        var returned = saved.Package.Definitions.LootPresets[0].Composition!;
        if (returned is IList<LootCardQuantity> mutable) mutable.Clear();
        Assert.Single(Store.Read("gear").Package.Definitions.LootPresets[0].Composition!);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
