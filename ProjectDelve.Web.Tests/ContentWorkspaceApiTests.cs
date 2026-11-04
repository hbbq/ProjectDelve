using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentWorkspaceApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-workspace-api-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task CrudCatalogAndExplicitPromotionUseOneMutableScope()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", webRoot,
            "--ContentWorkspace:Path", Path.Combine(root, "workspace"), "--CanonicalContent:Directory", Path.Combine(root, "canonical"),
            "--Library:Path", Path.Combine(root, "scenarios"), "--ContentLibrary:Path", Path.Combine(root, "legacy")],
            null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var json = ContentPackageJson.CreateOptions();
        var beforeGame = await client.GetStringAsync("/api/game");
        var empty = (await client.GetFromJsonAsync<ContentWorkspaceSnapshot>("/api/content-workspace", json))!;
        var canonical = app.Services.GetRequiredService<CanonicalContent>();
        var original = canonical.Bundle.Units[0];
        var draft = new ContentBundle { Units = [original with { DisplayName = "Edited original" }, original with { Id = "workspace-copy", DisplayName = "Independent copy" }] };
        using var save = await client.PutAsJsonAsync("/api/content-workspace", new SaveContentWorkspace(draft, empty.Token), json);
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        var saved = (await save.Content.ReadFromJsonAsync<ContentWorkspaceSnapshot>(json))!;
        using var stale = await client.PutAsJsonAsync("/api/content-workspace", new SaveContentWorkspace(draft, empty.Token), json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var browser = (await client.GetFromJsonAsync<ContentBrowserResponse>("/api/content-workspace/catalog", json))!;
        Assert.Contains(browser.Entries, e => e.Id == original.Id && e.Origin == "workspace-override");
        Assert.Contains(browser.Entries, e => e.Id == "workspace-copy" && e.Origin == "workspace");
        using var previewResponse = await client.PostAsJsonAsync("/api/content-workspace/promotion/preview",
            new WorkspacePromotionRequest(saved.Token, [new(ContentFamily.Unit, original.Id), new(ContentFamily.Unit, "workspace-copy")]), json);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<WorkspacePromotionPreview>(json))!;
        using var publish = await client.PostAsJsonAsync("/api/content-workspace/promotion/publish", new ConfirmWorkspacePromotion(preview, true), json);
        Assert.True(publish.IsSuccessStatusCode, await publish.Content.ReadAsStringAsync());
        Assert.Equal("Edited original", canonical.Bundle.Units.Single(u => u.Id == original.Id).DisplayName);
        Assert.Equal(beforeGame, await client.GetStringAsync("/api/game"));
        var after = (await client.GetFromJsonAsync<ContentWorkspaceSnapshot>("/api/content-workspace", json))!;
        Assert.Empty(after.Definitions.Units);
        using var clear = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/content-workspace")
        { Content = JsonContent.Create(new ClearContentWorkspace(after.Token), options: json) });
        Assert.True(clear.IsSuccessStatusCode);
        using var duplicate = await client.PutAsync("/api/content-workspace", new StringContent("{\"expectedToken\":\"x\",\"expectedToken\":\"y\",\"definitions\":{}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task LootBatchEndpointsValidateAndSaveWithoutChangingTheRunningGame()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", webRoot,
            "--ContentWorkspace:Path", Path.Combine(root, "workspace"), "--CanonicalContent:Directory", Path.Combine(root, "canonical"),
            "--Library:Path", Path.Combine(root, "scenarios"), "--ContentLibrary:Path", Path.Combine(root, "legacy")],
            null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var json = ContentPackageJson.CreateOptions();
        var before = await client.GetStringAsync("/api/game");
        var workspace = app.Services.GetRequiredService<ContentWorkspace>();
        var saved = workspace.Save(new() { Equipment = [new("test-api-reward", "API Reward")] }, workspace.Snapshot().Token);
        using var relationships = await client.PostAsJsonAsync("/api/content/loot-management", new LootManagementRequest(), json);
        Assert.True(relationships.IsSuccessStatusCode, await relationships.Content.ReadAsStringAsync());
        var inspected = (await relationships.Content.ReadFromJsonAsync<LootManagementResponse>(json))!;
        Assert.Empty(inspected.Rewards.Single(r => r.TypeId == "test-api-reward").Cards);
        using var previewResponse = await client.PostAsJsonAsync("/api/content-workspace/loot/preview",
            new LootBatchRequest(saved.Token, [new(ContentFamily.Equipment, "test-api-reward")]), json);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<LootBatchPreview>(json))!;
        using var created = await client.PostAsJsonAsync("/api/content-workspace/loot/create", new ConfirmLootBatch(preview, true), json);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var result = (await created.Content.ReadFromJsonAsync<LootBatchResult>(json))!;
        Assert.Equal(workspace.Snapshot().Token, result.Workspace.Token);
        Assert.Equal("test-api-reward", Assert.Single(result.Definitions.LootCards).EquipmentTypeId);
        Assert.Equal(before, await client.GetStringAsync("/api/game"));
        using var stale = await client.PostAsJsonAsync("/api/content-workspace/loot/create", new ConfirmLootBatch(preview, true), json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
