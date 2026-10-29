using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class BundledLibraryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-bundle-test-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(directory, "local");
    private string Bundle => Path.Combine(directory, "bundle");
    private ScenarioLibrary Store => new(Local, Bundle);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static LibraryScenario Scenario(string id = "fixture-scenario", string name = "Bundled doorway", long revision = 1)
    {
        var definition = ScenarioDefinitionJson.ToJson(ProjectDelve.Engine.Scenario.Define(ProjectDelve.Engine.Scenario.Map(5, 5),
            [ProjectDelve.Engine.Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, ProjectDelve.Engine.Scenario.At(0, 0))]));
        return new(id, name, null, revision, JsonSerializer.Deserialize<JsonElement>(definition));
    }
    private static LibraryCampaign Campaign(string id = "fixture-campaign", params string[] ids)
        => new(id, "Bundled campaign", null, null, ids.Length == 0 ? ["fixture-scenario"] : ids, 1);
    private static SaveLibraryScenario Edit(LibraryScenario scenario, string name)
        => new(name, scenario.CoverArt, scenario.Definition, scenario.Revision, scenario.ChangeToken);
    private void WriteBundle(LibraryScenario[] scenarios, LibraryCampaign[] campaigns)
    {
        Directory.CreateDirectory(Bundle);
        File.WriteAllText(Path.Combine(Bundle, "library.json"), JsonSerializer.Serialize(new { version = 1, scenarios, campaigns }, Json));
    }
    private string WriteLegacy(LibraryScenario[] scenarios, LibraryCampaign[] campaigns, string[] registered, bool registeredCampaign = false)
    {
        Directory.CreateDirectory(Local);
        var text = JsonSerializer.Serialize(new { version = 1, registeredScenarios = registered, registeredTestCampaign = registeredCampaign, scenarios, campaigns }, Json);
        File.WriteAllText(Path.Combine(Local, "library.json"), text); return text;
    }

    [Fact]
    public void SourcesMergeOnceByIdAndShareCrossSourceReferences()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var bundled = Store.Scenario("fixture-scenario");
        var local = Store.SaveScenario(null, Edit(bundled, "Local independent"));
        var campaign = Store.SaveCampaign(null, new("Mixed campaign", null, null, [local.Id, bundled.Id]));
        Assert.Equal("local", local.Source); Assert.Equal("bundled", bundled.Source);
        var edited = Store.SaveScenario(bundled.Id, Edit(bundled, "Local override"));
        Assert.Equal("override", edited.Source); Assert.Equal(2, edited.Revision);
        Assert.Single(Store.Catalog().Scenarios.Where(s => s.Id == bundled.Id));
        Assert.Equal(new[] { local.Id, bundled.Id }, Store.Campaign(campaign.Id).ScenarioIds);
        Assert.Equal("Local override", Store.Scenario(Store.Campaign("fixture-campaign").ScenarioIds[0]).Name);
        Assert.Contains("Bundled doorway", File.ReadAllText(Path.Combine(Bundle, "library.json")));
        Assert.DoesNotContain("Local override", File.ReadAllText(Path.Combine(Bundle, "library.json")));
    }

    [Fact]
    public void BundleUpdatesRemainVisibleUntilOverriddenAndStaleTokensConflictEvenAtSameRevision()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var original = Store.Scenario("fixture-scenario");
        WriteBundle([Scenario(name: "Bundle changed without bump")], [Campaign()]);
        var updated = Store.Scenario(original.Id);
        Assert.Equal("Bundle changed without bump", updated.Name);
        Assert.Equal(original.Revision, updated.Revision); Assert.NotEqual(original.ChangeToken, updated.ChangeToken);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveScenario(original.Id, Edit(original, "Stale"))).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.DeleteScenario(original.Id, original.Revision, original.ChangeToken)).StatusCode);
        var local = Store.SaveScenario(updated.Id, Edit(updated, "Intentional local work"));
        WriteBundle([Scenario(name: "Newest bundle", revision: 3)], [Campaign()]);
        var visible = Store.Scenario(local.Id);
        Assert.Equal("Intentional local work", visible.Name); Assert.Equal(local.Revision, visible.Revision);
        Assert.NotEqual(local.ChangeToken, visible.ChangeToken);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveScenario(local.Id, Edit(local, "Old editor"))).StatusCode);
        var restored = Store.RestoreScenario(visible.Id, new(visible.Revision, visible.ChangeToken));
        Assert.Equal("Newest bundle", restored.Name); Assert.Equal("bundled", restored.Source);
    }

    [Fact]
    public void RestorePreventsOldDraftBecomingValidAgainAtTheSameBundledRevision()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var original = Store.Scenario("fixture-scenario");
        var edited = Store.SaveScenario(original.Id, Edit(original, "Edited"));
        var restored = Store.RestoreScenario(edited.Id, new(edited.Revision, edited.ChangeToken));
        Assert.Equal(original.Revision, restored.Revision); Assert.NotEqual(original.ChangeToken, restored.ChangeToken);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveScenario(original.Id, Edit(original, "Old draft"))).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveScenario(original.Id, new("No token", null, original.Definition, original.Revision))).StatusCode);
    }

    [Fact]
    public void HideAndRestoreBundledContentSurvivesRestartAndDoesNotCascadeReferences()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var original = Store.Scenario("fixture-scenario");
        Store.DeleteScenario(original.Id, original.Revision, original.ChangeToken);
        Assert.Equal(404, Assert.Throws<LibraryException>(() => Store.Scenario(original.Id)).StatusCode);
        Assert.Equal(original.Id, Assert.Single(Store.Campaign("fixture-campaign").ScenarioIds));
        WriteBundle([Scenario(name: "Updated while hidden", revision: 2)], [Campaign()]);
        var hidden = Assert.Single(Store.Catalog().HiddenScenarios);
        Assert.DoesNotContain(Store.Catalog().Scenarios, s => s.Id == original.Id);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.RestoreScenario(hidden.Id, new(hidden.Revision, original.ChangeToken))).StatusCode);
        Assert.Equal("Updated while hidden", Store.RestoreScenario(hidden.Id, new(hidden.Revision, hidden.ChangeToken)).Name);
        var campaign = Store.Campaign("fixture-campaign");
        Store.DeleteCampaign(campaign.Id, campaign.Revision, campaign.ChangeToken);
        Assert.DoesNotContain(Store.Catalog().Campaigns, c => c.Id == campaign.Id);
        var removed = Assert.Single(Store.Catalog().HiddenCampaigns);
        Assert.Equal(campaign.Id, Store.RestoreCampaign(removed.Id, new(removed.Revision, removed.ChangeToken)).Id);
        Assert.NotNull(Store.Scenario(original.Id));
    }

    [Fact]
    public void CampaignOverridesRequireTokensAndRestoreBundleOrdering()
    {
        WriteBundle([Scenario(), Scenario("second")], [Campaign(ids: ["fixture-scenario", "second"])]);
        var original = Store.Campaign("fixture-campaign");
        var edited = Store.SaveCampaign(original.Id, new("Local name", null, null, ["second", "fixture-scenario"], original.Revision, original.ChangeToken));
        Assert.Equal("override", edited.Source); Assert.Equal(2, edited.Revision);
        Assert.Equal(new[] { "second", "fixture-scenario" }, Store.Campaign(original.Id).ScenarioIds);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Store.SaveCampaign(original.Id, new("Stale", null, null, [], original.Revision, original.ChangeToken))).StatusCode);
        var restored = Store.RestoreCampaign(edited.Id, new(edited.Revision, edited.ChangeToken));
        Assert.Equal(new[] { "fixture-scenario", "second" }, restored.ScenarioIds);
        Assert.Equal("bundled", restored.Source);
    }

    [Fact]
    public void MigrationPreservesLocalDataRevisionAndDeletionsAndBacksUpTheExactV1File()
    {
        var baseline = new ScenarioLibrary(Path.Combine(directory, "never-created"), readOnly: true);
        var builtin = baseline.Scenario(PlaytestScenarios.Catalog[0].Id);
        var untouched = baseline.Scenario(PlaytestScenarios.Catalog[1].Id);
        var deleted = PlaytestScenarios.Catalog[2].Id;
        var local = Scenario("authored-local", "Authored local", 7);
        var edit = builtin with { Name = "Edited before upgrade", Revision = 4, Source = null, ChangeToken = null };
        var pristine = untouched with { Source = null, ChangeToken = null };
        var originalText = WriteLegacy([edit, pristine, local], [], [builtin.Id, untouched.Id, deleted], true);
        var catalog = Store.Catalog();
        Assert.Equal("override", Store.Scenario(builtin.Id).Source); Assert.Equal(4, Store.Scenario(builtin.Id).Revision);
        Assert.Equal("bundled", Store.Scenario(untouched.Id).Source); Assert.Equal(1, Store.Scenario(untouched.Id).Revision);
        Assert.Equal("local", Store.Scenario(local.Id).Source); Assert.Equal(7, Store.Scenario(local.Id).Revision);
        Assert.Contains(catalog.HiddenScenarios, s => s.Id == deleted); Assert.Contains(catalog.HiddenCampaigns, c => c.Id == "test-scenarios");
        Assert.Equal(originalText, File.ReadAllText(Path.Combine(Local, "library.v1.backup.json")));
        var migrated = JsonNode.Parse(File.ReadAllText(Path.Combine(Local, "library.json")))!;
        Assert.Equal(2, migrated["version"]!.GetValue<int>());
        Assert.Equal(2, migrated["scenarios"]!.AsArray().Count);
        Assert.Equal(Store.Scenario(local.Id).ChangeToken, new ScenarioLibrary(Local, Bundle).Scenario(local.Id).ChangeToken);
    }

    [Fact]
    public void MigrationDemotesOnlyExactUntouchedContentAndKeepsAnExplicitSave()
    {
        var baseline = new ScenarioLibrary(Path.Combine(directory, "never-created"), readOnly: true);
        var original = baseline.Scenario(PlaytestScenarios.Catalog[0].Id) with { Source = null, ChangeToken = null };
        var campaign = baseline.Campaign("test-scenarios") with { Source = null, ChangeToken = null };
        WriteLegacy([original with { Revision = 2 }], [campaign], [original.Id], true);
        Assert.Equal("override", Store.Scenario(original.Id).Source);
        Assert.Equal("bundled", Store.Campaign(campaign.Id).Source);
    }

    [Fact]
    public void ReadOnlyPreviewDoesNotCreateOrMigrateFiles()
    {
        var preview = new ScenarioLibrary(Local, Bundle, readOnly: true);
        Assert.NotEmpty(preview.Catalog().Scenarios); Assert.False(Directory.Exists(Local));
        var text = WriteLegacy([Scenario("authored")], [], []);
        Assert.Equal("local", preview.Scenario("authored").Source);
        Assert.Equal(text, File.ReadAllText(Path.Combine(Local, "library.json")));
        Assert.False(File.Exists(Path.Combine(Local, ".library.lock")));
        Assert.False(File.Exists(Path.Combine(Local, "library.v1.backup.json")));
        Assert.Throws<InvalidOperationException>(() => preview.SaveCampaign(null, new("No write", null, null, [])));
    }

    [Fact]
    public void PromotionReleasesOnlyCapturedEquivalentRecordsAndRetainsConcurrentLocalEdits()
    {
        var source = Scenario();
        var one = Store.SaveScenario(null, new(source.Name, null, source.Definition));
        var two = Store.SaveScenario(null, new("Second", null, source.Definition));
        var campaign = Store.SaveCampaign(null, new("Promoted", null, null, [one.Id, two.Id]));
        WriteBundle([one with { Source = null, ChangeToken = null }, two with { Source = null, ChangeToken = null }], [campaign with { Source = null, ChangeToken = null }]);
        var changed = Store.Scenario(two.Id);
        Store.SaveScenario(two.Id, Edit(changed, "Concurrent change"));
        Assert.Equal(2, Store.ReleasePromoted([one, two], [campaign]));
        Assert.Equal("bundled", Store.Scenario(one.Id).Source); Assert.Equal("bundled", Store.Campaign(campaign.Id).Source);
        Assert.Equal("override", Store.Scenario(two.Id).Source); Assert.Equal("Concurrent change", Store.Scenario(two.Id).Name);
        Assert.Single(Store.Catalog().Scenarios.Where(s => s.Id == one.Id));
        Assert.Equal(0, Store.ReleasePromoted([one, two], [campaign]));
    }

    [Fact]
    public void PromotionDoesNotReleaseRawLocalChangesEvenIfRevisionWasNotBumped()
    {
        var source = Scenario();
        var captured = Store.SaveScenario(null, new(source.Name, null, source.Definition));
        WriteBundle([captured with { Source = null, ChangeToken = null }], []);
        var path = Path.Combine(Local, "library.json");
        var local = JsonNode.Parse(File.ReadAllText(path))!;
        local["scenarios"]![0]!["name"] = "Edited outside the application";
        File.WriteAllText(path, local.ToJsonString());
        Assert.Equal(0, Store.ReleasePromoted([captured], []));
        var preserved = Store.Scenario(captured.Id);
        Assert.Equal(captured.Revision, preserved.Revision);
        Assert.Equal("Edited outside the application", preserved.Name);
        Assert.Equal("override", preserved.Source);
    }

    [Fact]
    public void HiddenContentWhoseBundleWasRemovedCannotBeRestoredUntilItReturns()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var scenario = Store.Scenario("fixture-scenario");
        var campaign = Store.Campaign("fixture-campaign");
        Store.DeleteScenario(scenario.Id, scenario.Revision, scenario.ChangeToken);
        Store.DeleteCampaign(campaign.Id, campaign.Revision, campaign.ChangeToken);
        WriteBundle([], []);
        var hiddenScenario = Assert.Single(Store.Catalog().HiddenScenarios);
        var hiddenCampaign = Assert.Single(Store.Catalog().HiddenCampaigns);
        var path = Path.Combine(Local, "library.json"); var before = File.ReadAllText(path);
        Assert.Equal(404, Assert.Throws<LibraryException>(() => Store.RestoreScenario(hiddenScenario.Id, new(hiddenScenario.Revision, hiddenScenario.ChangeToken))).StatusCode);
        Assert.Equal(404, Assert.Throws<LibraryException>(() => Store.RestoreCampaign(hiddenCampaign.Id, new(hiddenCampaign.Revision, hiddenCampaign.ChangeToken))).StatusCode);
        Assert.Equal(before, File.ReadAllText(path));
        WriteBundle([Scenario()], [Campaign()]);
        var returned = Assert.Single(Store.Catalog().HiddenScenarios);
        Assert.Equal("bundled", Store.RestoreScenario(returned.Id, new(returned.Revision, returned.ChangeToken)).Source);
    }

    [Fact]
    public void LocalAndBundledDirectoriesCannotBeTheSame()
    {
        Assert.Throws<ArgumentException>(() => new ScenarioLibrary(Local, Local));
        Assert.False(Directory.Exists(Local));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":9,\"scenarios\":[],\"campaigns\":[]}")]
    [InlineData("[]")]
    public void CorruptBundleNeverOverwritesLocalData(string content)
    {
        var source = Scenario(); var saved = Store.SaveScenario(null, new(source.Name, null, source.Definition));
        var path = Path.Combine(Local, "library.json"); var before = File.ReadAllText(path);
        Directory.CreateDirectory(Bundle); File.WriteAllText(Path.Combine(Bundle, "library.json"), content);
        Assert.Equal(503, Assert.Throws<LibraryException>(() => Store.Catalog()).StatusCode);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task HttpBundledEditDeleteAndRestoreUseReturnedTokens()
    {
        WriteBundle([Scenario()], [Campaign()]);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root, "--Library:Path", Local, "--Library:BundlePath", Bundle]);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var original = (await client.GetFromJsonAsync<LibraryScenario>("/api/library/scenarios/fixture-scenario"))!;
        var editedResponse = await client.PutAsJsonAsync($"/api/library/scenarios/{original.Id}", Edit(original, "API override"));
        editedResponse.EnsureSuccessStatusCode(); var edited = (await editedResponse.Content.ReadFromJsonAsync<LibraryScenario>())!;
        Assert.Equal("override", edited.Source);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/library/scenarios/{edited.Id}?revision={edited.Revision}")).StatusCode);
        (await client.DeleteAsync($"/api/library/scenarios/{edited.Id}?revision={edited.Revision}&changeToken={edited.ChangeToken}")).EnsureSuccessStatusCode();
        var catalog = (await client.GetFromJsonAsync<LibraryCatalog>("/api/library"))!; var hidden = Assert.Single(catalog.HiddenScenarios);
        var restore = await client.PostAsJsonAsync($"/api/library/scenarios/{hidden.Id}/restore", new RestoreLibraryItem(hidden.Revision, hidden.ChangeToken));
        restore.EnsureSuccessStatusCode(); Assert.Equal("bundled", (await restore.Content.ReadFromJsonAsync<LibraryScenario>())!.Source);
        await app.StopAsync();
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
