using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentLibraryApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-content-api-" + Guid.NewGuid().ToString("N"));
    private static ContentPackage Package(string name = "Independent bell") => new()
    {
        Id = "api-example", Title = "API example", Definitions = new()
        { Equipment = [new("api-bell", name) { LocalAbilities = [new("stand", "Stand", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())] }] }
    };
    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public WebApplication App => app;
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
    private async Task<Host> Start(string? storage = null, string? canonicalDirectory = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        List<string> args = ["--urls", "http://127.0.0.1:0", "--contentRoot", root,
            "--ContentLibrary:Path", storage ?? directory, "--Library:Path", Path.Combine(directory, "scenarios")];
        if (canonicalDirectory is not null) args.AddRange(["--CanonicalContent:Directory", canonicalDirectory]);
        var app = PlaytestHost.Build([.. args],
            null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        return new(app, new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
    }
    private static async Task<ContentLibraryRevision> ReadRevision(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ContentLibraryRevision>(Json))!;
        }
    }

    [Fact]
    public async Task ApiCreatesEditsListsArchivesAndReadsHistoryAcrossHostRestart()
    {
        string libraryId;
        string originalDigest;
        await using (var host = await Start())
        {
            var empty = (await host.Client.GetFromJsonAsync<ContentLibraryCatalog>("/api/content-library", Json))!;
            Assert.Empty(empty.Packages); libraryId = empty.LibraryId;
            var first = await ReadRevision(await host.Client.PostAsJsonAsync("/api/content-library/packages", new SaveContentLibraryPackage(Package(), 0), Json));
            originalDigest = first.Digest;
            var saved = await ReadRevision(await host.Client.PutAsJsonAsync("/api/content-library/packages/api-example", new SaveContentLibraryPackage(Package("Revised"), 1), Json));
            Assert.Equal(2, saved.Revision);
            using var stale = await host.Client.PutAsJsonAsync("/api/content-library/packages/api-example", new SaveContentLibraryPackage(Package(), 1), Json);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("current revision is 2", await stale.Content.ReadAsStringAsync());
            using var archiveStale = await host.Client.PostAsJsonAsync("/api/content-library/packages/api-example/archive", new { expectedRevision = 1 });
            Assert.Equal(HttpStatusCode.Conflict, archiveStale.StatusCode);
            using var archived = await host.Client.PostAsJsonAsync("/api/content-library/packages/api-example/archive", new { expectedRevision = 2 });
            Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
            Assert.Empty((await host.Client.GetFromJsonAsync<ContentLibraryCatalog>("/api/content-library", Json))!.Packages);
            Assert.True(Assert.Single((await host.Client.GetFromJsonAsync<ContentLibraryCatalog>("/api/content-library?includeArchived=true", Json))!.Packages).Archived);
        }
        await using var restarted = await Start();
        var catalog = (await restarted.Client.GetFromJsonAsync<ContentLibraryCatalog>("/api/content-library?includeArchived=true", Json))!;
        Assert.Equal(libraryId, catalog.LibraryId);
        var current = (await restarted.Client.GetFromJsonAsync<ContentLibraryRevision>("/api/content-library/packages/api-example", Json))!;
        Assert.Equal("Revised", current.Package.Definitions.Equipment[0].Name);
        var original = (await restarted.Client.GetFromJsonAsync<ContentLibraryRevision>("/api/content-library/packages/api-example?revision=1", Json))!;
        Assert.Equal(originalDigest, original.Digest);
        Assert.Equal(2, (await restarted.Client.GetFromJsonAsync<ContentLibraryRevision[]>("/api/content-library/packages/api-example/revisions", Json))!.Length);
    }

    [Fact]
    public async Task LibraryOperationsCannotMutateCanonicalDraftsScenariosOrRunningGameAndNeverEnterDefaultCatalog()
    {
        await using var host = await Start();
        var client = host.Client;
        var game = await client.GetStringAsync("/api/game");
        var authoritative = JsonSerializer.Serialize(host.App.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State);
        var browser = await client.GetStringAsync("/api/content/catalog");
        var designer = await client.GetStringAsync("/api/designer/catalog");
        var scenarios = await client.GetStringAsync("/api/library");
        var definition = Scenario.Define(Scenario.Map(3, 3), [Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(0, 0))]);
        var localDraft = ScenarioDefinitionJson.ToJson(definition with { EquipmentTypes = Package("Draft bell").Definitions.Equipment.ToList() });
        using var draftBefore = await client.PostAsync("/api/designer/validate", new StringContent(localDraft, Encoding.UTF8, "application/json"));
        draftBefore.EnsureSuccessStatusCode();
        var validated = await draftBefore.Content.ReadAsStringAsync();
        await ReadRevision(await client.PostAsJsonAsync("/api/content-library/packages", new SaveContentLibraryPackage(Package(), 0), Json));
        await ReadRevision(await client.PutAsJsonAsync("/api/content-library/packages/api-example", new SaveContentLibraryPackage(Package("Changed"), 1), Json));
        using var archive = await client.PostAsJsonAsync("/api/content-library/packages/api-example/archive", new { expectedRevision = 2 });
        archive.EnsureSuccessStatusCode();
        Assert.Equal(game, await client.GetStringAsync("/api/game"));
        Assert.Equal(authoritative, JsonSerializer.Serialize(host.App.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State));
        Assert.Equal(browser, await client.GetStringAsync("/api/content/catalog"));
        Assert.Equal(designer, await client.GetStringAsync("/api/designer/catalog"));
        Assert.Equal(scenarios, await client.GetStringAsync("/api/library"));
        using var draftAfter = await client.PostAsync("/api/designer/validate", new StringContent(localDraft, Encoding.UTF8, "application/json"));
        draftAfter.EnsureSuccessStatusCode(); Assert.Equal(validated, await draftAfter.Content.ReadAsStringAsync());
        Assert.DoesNotContain(host.App.Services.GetRequiredService<CanonicalContent>().Bundle.Equipment, e => e.Id == "api-bell");
        var unresolved = definition with { Loot = new() { Composition = [new() { EquipmentTypeId = "api-bell" }] } };
        using var scenario = await client.PostAsync("/api/designer/validate", new StringContent(ScenarioDefinitionJson.ToJson(unresolved), Encoding.UTF8, "application/json"));
        scenario.EnsureSuccessStatusCode();
        Assert.False((await scenario.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(unresolved, host.App.Services.GetRequiredService<CanonicalContent>().Bundle));
    }

    [Fact]
    public async Task ImportsExportsValidationDiagnosticsAndErrorsStayIndependentOfServerPaths()
    {
        await using var host = await Start();
        var client = host.Client;
        var first = await ReadRevision(await client.PostAsync("/api/content-library/import/package", new StringContent(ContentPackageJson.ToJson(Package()), Encoding.UTF8, "application/json")));
        var exported = await client.GetStringAsync("/api/content-library/packages/api-example/export");
        Assert.DoesNotContain(directory, exported, StringComparison.OrdinalIgnoreCase);
        using var duplicate = await client.PostAsync("/api/content-library/import", new StringContent(exported, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var portable = ContentPackageJson.FromJson(await client.GetStringAsync("/api/content-library/packages/api-example/export/package"));
        Assert.Equal("api-bell", Assert.Single(portable.Definitions.Equipment).Id);
        await using var destination = await Start(Path.Combine(directory, "destination"));
        var imported = await ReadRevision(await destination.Client.PostAsync("/api/content-library/import", new StringContent(exported, Encoding.UTF8, "application/json")));
        Assert.Equal(first.Digest, imported.Digest);
        var missing = new ContentPackage { Id = "missing", Definitions = new() { LootCards = [new("missing-card", "Missing") { EquipmentTypeId = "no-such-equipment" }] } };
        using var invalid = await client.PostAsJsonAsync("/api/content-library/packages", new SaveContentLibraryPackage(missing, 0), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var details = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = Assert.Single(details.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("equipmentTypeId", diagnostic.GetProperty("path").GetString());
        Assert.Equal("library:missing@1", diagnostic.GetProperty("source").GetProperty("document").GetString());
        Assert.DoesNotContain(directory, details.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var notFound = await client.GetAsync("/api/content-library/packages/no-such-package");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        using var malformed = await client.PostAsync("/api/content-library/packages", new StringContent("{broken", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var unknown = await client.PostAsync("/api/content-library/import/package", new StringContent("{\"formatVersion\":99,\"id\":\"x\",\"definitions\":{}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var largeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/content-library/import/package")
            { Content = new StringContent(new string('x', 8_000_001), Encoding.UTF8, "application/json") };
        largeRequest.Headers.ExpectContinue = true;
        using var oversized = await client.SendAsync(largeRequest);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var nullPackage = await client.PostAsync("/api/content-library/packages", new StringContent("{\"package\":null,\"expectedRevision\":0}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, nullPackage.StatusCode);
        var path = Path.Combine(directory, "library.json");
        File.WriteAllText(path, "{damaged");
        using var damaged = await client.GetAsync("/api/content-library");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, damaged.StatusCode);
        Assert.DoesNotContain(directory, await damaged.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{damaged", File.ReadAllText(path));
    }

    [Fact]
    public async Task BatchApiAcceptsLegalMutualCyclesAndCanonicalReferencesUseDeclaredProviders()
    {
        using var fixture = new CanonicalJsonFixture();
        await using var host = await Start(canonicalDirectory: fixture.DirectoryPath);
        ContentPackage Summon(string id, string other) => new() { Id = id, Dependencies = [other], Definitions = new()
            { Units = [new(id + "-unit", 1, 0, 0, 0, 1) { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(other + "-unit", Posture.Lying))] }] } };
        SaveContentLibraryPackage[] transaction = [new(Summon("one", "two"), 0, [new("two", 1)]), new(Summon("two", "one"), 0, [new("one", 1)])];
        using var response = await host.Client.PostAsJsonAsync("/api/content-library/transactions", transaction, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var package = Package() with { Id = "canonical-reference", Dependencies = [CanonicalJsonFixture.PackageId], Definitions = new()
            { LootCards = [new("canonical-reference-gift", "Gift") { EquipmentTypeId = CanonicalJsonFixture.EquipmentTypeId }] } };
        var saved = await ReadRevision(await host.Client.PostAsJsonAsync("/api/content-library/packages", new SaveContentLibraryPackage(package, 0), Json));
        Assert.Equal(CanonicalJsonFixture.PackageId, Assert.Single(saved.CanonicalPackages).Id);
        using var export = await host.Client.GetAsync("/api/content-library/packages/canonical-reference/export/package");
        export.EnsureSuccessStatusCode();
        Assert.Equal(CanonicalJsonFixture.EquipmentTypeId, Assert.Single(ContentPackageJson.FromJson(await export.Content.ReadAsStringAsync()).Definitions.Equipment).Id);
        Assert.Equal("no-store", export.Headers.CacheControl!.ToString());
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
