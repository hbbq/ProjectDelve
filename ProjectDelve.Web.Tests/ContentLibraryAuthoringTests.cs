using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentLibraryAuthoringTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-library-authoring-" + Guid.NewGuid().ToString("N"));
    private ContentLibrary Store => new(directory);
    private static ContentPackage Empty(string id = "workshop") => new() { Id = id, Title = "Workshop", Dependencies = [ContentPackageCatalog.CSharpPackageId] };
    private static EquipmentDefinition Gear(string id = "workshop-bell") => new(id, "Workshop Bell")
    { LocalAbilities = [new("encourage", "Encourage", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright)) { Usage = new(2) }] };
    private static ContentEditorRequest Edit(object value, string family = "equipment", string? existing = null)
        => new(family, JsonSerializer.SerializeToElement(value, value.GetType(), Json), existing);

    [Fact]
    public void DefinitionPreviewAndPackageInspectionDoNotPersistCandidatesOrCreateScenarios()
    {
        var draft = new SaveContentLibraryPackage(Empty(), 0);
        var before = Store.Catalog();
        var preview = ContentLibraryAuthoring.Validate(Store, new(draft, Edit(Gear())));
        Assert.True(preview.Valid); Assert.Empty(preview.Diagnostics);
        Assert.Equal("Stand up one adjacent friendly Unit.", Assert.Single(preview.Entry!.Abilities).Description);
        Assert.Equal(2, preview.Entry.Abilities[0].Usage.MaxUses);
        Assert.Equal("library-local", preview.Entry.Origin); Assert.Equal("workshop", preview.Entry.PackageId);
        Assert.Empty(Store.Catalog().Packages);
        Assert.Equal(before.LibraryId, Store.Catalog().LibraryId);
        Assert.Throws<LibraryException>(() => Store.Read("workshop"));
    }

    [Fact]
    public void StandaloneEquipmentAndLinkedLootCanBeSavedRevisedReopenedAndPreviewedHistorically()
    {
        var package = Empty() with { Definitions = new() { Equipment = [Gear()] } };
        var loot = new LootCardDefinition("workshop-gift", "Workshop Gift") { EquipmentTypeId = Gear().Id };
        Assert.True(ContentLibraryAuthoring.Validate(Store, new(new(package, 0), Edit(loot, "lootCards"))).Valid);
        package = package with { Definitions = package.Definitions with { LootCards = [loot] } };
        Store.Create(package);
        var original = Store.Read(package.Id);
        var edited = Gear() with { Name = "Revised Bell", LocalAbilities = [Gear().LocalAbilities[0] with { Usage = new(4) }] };
        var draft = new SaveContentLibraryPackage(original.Package, original.Revision);
        Assert.True(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(edited, existing: edited.Id))).Valid);
        Store.Save(package.Id, draft with { Package = package with { Definitions = package.Definitions with { Equipment = [edited] } } });
        var old = ContentLibraryAuthoring.Catalog(Store.Inspect(package.Id, 1));
        var current = ContentLibraryAuthoring.Catalog(Store.Inspect(package.Id));
        Assert.Equal("Workshop Bell", old.Entries.Single(e => e.Id == edited.Id).Name);
        Assert.Equal("Revised Bell", current.Entries.Single(e => e.Id == edited.Id).Name);
        Assert.Equal(1, old.Entries.Single(e => e.Id == edited.Id).PackageRevision);
        Assert.Equal(2, current.Entries.Single(e => e.Id == edited.Id).PackageRevision);
        var stale = Assert.Throws<LibraryException>(() => ContentLibraryAuthoring.Validate(Store, new(draft, Edit(edited, existing: edited.Id))));
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal(original.Digest, Store.Read(package.Id, 1).Digest);
    }

    [Fact]
    public void ExplicitExternalRevisionsSupplySearchableReadOnlyReferencesAndMissingTargetsHaveFieldPaths()
    {
        Store.Create(Empty("external") with { Definitions = new() { Equipment = [Gear()] } });
        var package = Empty() with { Dependencies = [ContentPackageCatalog.CSharpPackageId, "external"] };
        var draft = new SaveContentLibraryPackage(package, 0, [new("external", 1)]);
        var catalog = ContentLibraryAuthoring.Catalog(Store.Inspect(draft));
        var external = catalog.Entries.Single(e => e.Id == Gear().Id);
        Assert.Equal("library-dependency", external.Origin); Assert.Equal("external", external.PackageId); Assert.Equal(1, external.PackageRevision);
        var card = new LootCardDefinition("gift", "Gift") { EquipmentTypeId = Gear().Id };
        Assert.True(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(card, "lootCards"))).Valid);
        var invalid = card with { EquipmentTypeId = "missing" };
        var result = ContentLibraryAuthoring.Validate(Store, new(draft, Edit(invalid, "lootCards")));
        Assert.False(result.Valid); Assert.Equal("definition.equipmentTypeId", Assert.Single(result.Diagnostics).Path);
        Assert.Equal("missingReference", result.Diagnostics[0].Code);
        var copy = Gear("independent-copy");
        Assert.True(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(copy))).Valid);
        Assert.False(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(Gear(), existing: Gear().Id))).Valid);
    }

    [Fact]
    public void CanonicalCopyHasIndependentIdentityAndCannotOverrideCanonicalOrChangeAnOwnedId()
    {
        var source = EquipmentCatalog.All[0];
        var draft = new SaveContentLibraryPackage(Empty(), 0);
        Assert.False(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(source))).Valid);
        Assert.False(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(source, existing: source.Id))).Valid);
        var copy = source with { Id = "independent-copy" };
        Assert.True(ContentLibraryAuthoring.Validate(Store, new(draft, Edit(copy))).Valid);
        var package = Empty() with { Definitions = new() { Equipment = [copy] } };
        Assert.False(ContentLibraryAuthoring.Validate(Store, new(new(package, 0), Edit(copy with { Id = "changed-id" }, existing: copy.Id))).Valid);
        Assert.Equal(source.Id, EquipmentCatalog.All[0].Id);
    }

    [Fact]
    public async Task AuthoringEndpointsExposeProvidersAndValidationWithoutMutatingPackageOrRunningGame()
    {
        using var fixture = new CanonicalJsonFixture();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root, "--ContentLibrary:Path", directory,
            "--CanonicalContent:Directory", fixture.DirectoryPath],
            null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var before = await client.GetStringAsync("/api/game");
        var providers = await client.GetFromJsonAsync<JsonElement>("/api/content-library/providers");
        Assert.Contains(providers.EnumerateArray(), p => p.GetProperty("id").GetString() == CanonicalJsonFixture.PackageId);
        var draft = new SaveContentLibraryPackage(Empty(), 0);
        using var context = await client.PostAsJsonAsync("/api/content-library/authoring/context", draft, Json); context.EnsureSuccessStatusCode();
        using var preview = await client.PostAsJsonAsync("/api/content-library/authoring/validate", new ValidateLibraryContent(draft, Edit(Gear())), Json);
        preview.EnsureSuccessStatusCode(); Assert.True((await preview.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Valid);
        Assert.Empty((await client.GetFromJsonAsync<ContentLibraryCatalog>("/api/content-library", Json))!.Packages);
        Assert.Equal(before, await client.GetStringAsync("/api/game"));
        var saved = app.Services.GetRequiredService<ContentLibrary>().Create(Empty() with { Definitions = new() { Equipment = [Gear()] } });
        using var history = await client.GetAsync("/api/content-library/packages/workshop/context?revision=1"); history.EnsureSuccessStatusCode();
        app.Services.GetRequiredService<ContentLibrary>().Save("workshop", new(saved.Package with { Title = "New title" }, 1));
        using var stale = await client.PostAsJsonAsync("/api/content-library/authoring/context", new SaveContentLibraryPackage(saved.Package, 1), Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
