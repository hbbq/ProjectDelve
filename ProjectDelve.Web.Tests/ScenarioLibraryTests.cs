using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioLibraryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-library-test-" + Guid.NewGuid().ToString("N"));
    private ScenarioLibrary Store => new(directory);
    private static SaveLibraryScenario Draft(string name = "A quiet doorway")
    {
        var definition = Scenario.Define(Scenario.Map(5, 5),
            [Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(0, 0))]);
        return new(name, null, JsonDocument.Parse(ScenarioDefinitionJson.ToJson(definition)).RootElement.Clone());
    }

    [Fact]
    public void ExistingScenariosAreRegisteredOnceAndRemainLoadable()
    {
        var first = Store.Catalog();
        var testCampaign = Assert.Single(first.Campaigns, c => c.Name == "Test Scenarios");
        Assert.Equal(PlaytestScenarios.Catalog.Select(s => s.Id), testCampaign.ScenarioIds);
        foreach (var scenario in first.Scenarios)
            _ = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(Store.Scenario(scenario.Id).Definition.GetRawText()));
        Assert.Equal(first.Scenarios.Count, Store.Catalog().Scenarios.Count);
        Assert.Single(Store.Catalog().Campaigns);
    }

    [Fact]
    public void IndependentScenariosKeepIdentityAndExactDefinitionAcrossReloadAndRename()
    {
        var draft = Draft();
        var original = Store.SaveScenario(null, draft);
        var renamed = Store.SaveScenario(original.Id, draft with { Name = "Renamed", CoverArt = "https://example.org/forest.webp", Revision = original.Revision });
        var restored = Store.Scenario(original.Id);
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(2, restored.Revision);
        Assert.Equal("Renamed", restored.Name);
        Assert.Equal(ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(draft.Definition.GetRawText())),
            ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(restored.Definition.GetRawText())));
        Assert.DoesNotContain(Store.Catalog().Campaigns, c => c.ScenarioIds.Contains(original.Id));
        Assert.Equal("https://example.org/forest.webp", restored.CoverArt);
    }

    [Fact]
    public void CampaignsShareDefinitionsAndPreserveOrderAcrossReload()
    {
        var first = Store.SaveScenario(null, Draft("First"));
        var second = Store.SaveScenario(null, Draft("Second"));
        var one = Store.SaveCampaign(null, new("One", null, null, [first.Id, second.Id]));
        var two = Store.SaveCampaign(null, new("Two", "Atmosphere only", "/art/cover.webp", [first.Id]));
        var changed = Store.SaveCampaign(one.Id, new("One", null, null, [second.Id, first.Id], one.Revision));
        Store.SaveScenario(first.Id, Draft("Shared rename") with { Revision = first.Revision });
        Assert.Equal(new[] { second.Id, first.Id }, Store.Campaign(one.Id).ScenarioIds);
        Assert.Equal(first.Id, Assert.Single(Store.Campaign(two.Id).ScenarioIds));
        Assert.Equal("Shared rename", Store.Scenario(first.Id).Name);
        Assert.Equal(2, changed.Revision);
    }

    [Fact]
    public void DeletedScenariosLeaveRepairableReferencesAndDoNotReappear()
    {
        var saved = Store.SaveScenario(null, Draft());
        var campaign = Store.SaveCampaign(null, new("Missing entry", null, null, [saved.Id]));
        Store.DeleteScenario(saved.Id, saved.Revision);
        Assert.Equal(404, Assert.Throws<LibraryException>(() => Store.Scenario(saved.Id)).StatusCode);
        Assert.Equal(saved.Id, Assert.Single(Store.Campaign(campaign.Id).ScenarioIds));
        var updated = Store.SaveCampaign(campaign.Id, new("Still missing", null, null, [saved.Id], campaign.Revision));
        Assert.Throws<ArgumentException>(() => Store.SaveCampaign(null, new("Unknown", null, null, [saved.Id])));
        Store.SaveCampaign(campaign.Id, new("Repaired", null, null, [], updated.Revision));
        Assert.Empty(Store.Campaign(campaign.Id).ScenarioIds);
        var builtin = Store.Catalog().Scenarios.First();
        Store.DeleteScenario(builtin.Id, builtin.Revision, builtin.ChangeToken);
        Assert.DoesNotContain(Store.Catalog().Scenarios, s => s.Id == builtin.Id);
    }

    [Fact]
    public void StaleEditsAndDeletesCannotOverwriteOtherWriters()
    {
        var saved = Store.SaveScenario(null, Draft());
        Store.SaveScenario(saved.Id, Draft("Winner") with { Revision = saved.Revision });
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveScenario(saved.Id, Draft("Stale") with { Revision = saved.Revision })).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.DeleteScenario(saved.Id, saved.Revision)).StatusCode);
        Assert.Equal("Winner", Store.Scenario(saved.Id).Name);
        var campaign = Store.SaveCampaign(null, new("Original", null, null, []));
        Store.SaveCampaign(campaign.Id, new("Updated", null, null, [], campaign.Revision));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.DeleteCampaign(campaign.Id, campaign.Revision)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentInstancesDoNotLoseWrites()
    {
        Store.Catalog();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => Store.SaveScenario(null, Draft("Concurrent " + i)))));
        Assert.Equal(10, Store.Catalog().Scenarios.Count(s => s.Name.StartsWith("Concurrent ")));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("C:\\other")]
    [InlineData("foo/bar")]
    [InlineData("foo.json")]
    public void IdentityNeverBecomesAFilePath(string id)
    {
        Assert.Throws<ArgumentException>(() => Store.Scenario(id));
        Assert.Throws<ArgumentException>(() => Store.DeleteCampaign(id, 1));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml,<svg/>")]
    [InlineData("//example.org/a.png")]
    [InlineData("/\\example.org/a.png")]
    [InlineData("http://example.org/a.png")]
    public void UnsafeCoversAreRejected(string cover)
    {
        Assert.Throws<ArgumentException>(() => Store.SaveScenario(null, Draft() with { CoverArt = cover }));
        Assert.Throws<ArgumentException>(() => Store.SaveCampaign(null, new("Cover", null, cover, [])));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"registeredScenarios\":[],\"registeredTestCampaign\":false,\"scenarios\":[],\"campaigns\":[]}")]
    public void CorruptStorageIsReportedWithoutOverwritingIt(string invalid)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "library.json");
        File.WriteAllText(path, invalid);
        Assert.Equal(503, Assert.Throws<LibraryException>(() => Store.Catalog()).StatusCode);
        Assert.Equal(503, Assert.Throws<LibraryException>(() => Store.SaveScenario(null, Draft())).StatusCode);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void InvalidPersistedDefinitionCannotBeSilentlyRewritten()
    {
        var saved = Store.SaveScenario(null, Draft());
        var path = Path.Combine(directory, "library.json");
        var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var record = data["scenarios"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == saved.Id)!;
        record["definition"]!["board"]!["width"] = 0;
        var invalid = data.ToJsonString();
        File.WriteAllText(path, invalid);
        Assert.Equal(503, Assert.Throws<LibraryException>(() => Store.SaveScenario(null, Draft())).StatusCode);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public async Task ApiCatalogIsSpoilerFreeAndSavedTransportPlaysWithoutMutatingLibrary()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root, "--Library:Path", directory]);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var response = await client.PostAsJsonAsync("/api/library/scenarios", Draft());
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<LibraryScenario>())!;
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/library");
        var summary = catalog.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == saved.Id);
        Assert.Equal(new[] { "id", "name", "coverArt", "revision", "source", "changeToken" }, summary.EnumerateObject().Select(p => p.Name));
        var before = await client.GetStringAsync($"/api/library/scenarios/{saved.Id}");
        var transport = await client.GetFromJsonAsync<JsonElement>($"/api/library/scenarios/{saved.Id}/transport");
        var game = await client.GetFromJsonAsync<JsonElement>("/api/game");
        var played = await client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = game.GetProperty("revision").GetInt64(), transport = transport.GetProperty("transport").GetString() });
        played.EnsureSuccessStatusCode();
        Assert.Equal(before, await client.GetStringAsync($"/api/library/scenarios/{saved.Id}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/library/scenarios/{saved.Id}", Draft())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/library/campaigns", new { name = "Bad reference", scenarioIds = new[] { "missing" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/library/campaigns", new { name = "Missing references", scenarioIds = (string[]?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/library/scenarios", new { name = "Missing definition" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/library/scenarios", Draft() with { Name = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/library/scenarios", new StringContent("{broken", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/library/scenarios", new StringContent("{\"name\":\"" + new string('x', 2_200_000) + "\"}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/library/scenarios/missing")).StatusCode);
        await app.StopAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
