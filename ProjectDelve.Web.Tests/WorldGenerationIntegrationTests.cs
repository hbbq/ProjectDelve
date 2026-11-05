using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class WorldGenerationIntegrationTests
{
    private const string Code = "WLD1-30x24-X-17-Y-13-F35-W12-R15-KBalanced-HMixed-S0123456789ABCDEF";

    [Fact]
    public async Task PreviewExportAndDesignerImportShareExactTerrainWithoutReplacingGame()
    {
        await using var host = await Host.Start();
        var before = await host.Client.GetStringAsync("/api/game");
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = Code });
        response.EnsureSuccessStatusCode();
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Code, result.GetProperty("code").GetString());
        Assert.Equal("Generated world terrain", result.GetProperty("title").GetString());
        Assert.Equal(-17, result.GetProperty("window").GetProperty("offsetX").GetInt32());
        var json = result.GetProperty("definition").GetRawText();
        var definition = ScenarioDefinitionJson.FromJson(json);
        Assert.Empty(definition.Units); Assert.Empty(definition.Agency); Assert.Empty(definition.UnitTypeIds);
        Assert.Null(definition.Objectives); Assert.Null(definition.FogOfWarSides); Assert.Null(definition.WorldEffects);
        Assert.Empty(definition.Loot!.Composition!);
        var transport = result.GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var validated = await host.Client.PostAsJsonAsync("/api/designer/validate", result.GetProperty("definition"));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
    }

    [Fact]
    public async Task StageFramesAreDeterministicAndFinalExportRemainsAuthoritative()
    {
        await using var host = await Host.Start();
        var code = Code.Replace("-S", "-E1-I2-B1111-S");
        using var first = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        first.EnsureSuccessStatusCode();
        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var second = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        second.EnsureSuccessStatusCode();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("preview").GetRawText(), b.GetProperty("preview").GetRawText());
        Assert.Equal(a.GetProperty("diagnostics").GetRawText(), b.GetProperty("diagnostics").GetRawText());
        var preview = a.GetProperty("preview"); var frames = preview.GetProperty("frames");
        Assert.Equal("natural", frames[0].GetProperty("stepId").GetString());
        Assert.Equal("current", frames[frames.GetArrayLength() - 1].GetProperty("stepId").GetString());
        foreach (var frame in frames.EnumerateArray()) Assert.Equal(720, frame.GetProperty("terrain").GetString()!.Length);
        var final = frames[frames.GetArrayLength() - 1].GetProperty("terrain").GetString()!;
        var palette = preview.GetProperty("terrainPalette").Deserialize<string[]>()!;
        var definition = ScenarioDefinitionTransport.Decode(a.GetProperty("transport").GetString()!);
        var overrides = definition.Board.Cells.ToDictionary(c => c.Position, c => c.TerrainTypeId);
        for (var i = 0; i < final.Length; i++) Assert.Equal(palette[final[i] - 'A'], overrides.GetValueOrDefault(new(i % 30, i / 30), "grass"));
        Assert.True(a.GetProperty("metrics").GetProperty("retainedPreviewBytes").GetInt64() < 4_000_000);
    }

    [Fact]
    public async Task CatalogAndSeedCompletionAndHostLimitUseSeparateWorldContract()
    {
        await using var host = await Host.Start();
        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/world-generation/catalog");
        Assert.Equal(120, catalog.GetProperty("maxBoardSize").GetInt32());
        Assert.Equal(WorldGenerationPresets.All.Select(p => p.Id), catalog.GetProperty("presets").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        using var fresh = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = Code[..Code.LastIndexOf("-S", StringComparison.Ordinal)] });
        fresh.EnsureSuccessStatusCode();
        var completed = (await fresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        Assert.NotNull(WorldGenerationRecipe.Parse(completed).Seed);
        foreach (var code in new[] { Code.Replace("30x24", "121x100"), Code.Replace("X-17", "X2147483647"), Code.Replace("F35", "F99"), "DNG1-invalid" })
        {
            using var rejected = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        var designer = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        Assert.DoesNotContain("Dirt", designer.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains(designer.GetProperty("terrainDefinitions").EnumerateArray(), t => t.GetProperty("id").GetString() == "dirt");
    }

    [Fact]
    public async Task WorkspaceTerrainIsResolvedAndCapturedWhileExistingGameStaysFrozen()
    {
        await using var host = await Host.Start();
        var workspace = host.App.Services.GetRequiredService<ContentWorkspace>();
        var soil = TerrainCatalog.Find(TerrainCatalog.All, "dirt") with { Name = "Workspace soil", Presentation = new("Grass") };
        var before = await host.Client.GetStringAsync("/api/game");
        var saved = workspace.Save(new() { Terrain = [soil] }, workspace.Snapshot().Token);
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = Code });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(result.GetProperty("terrainDefinitions").EnumerateArray(), t => t.GetProperty("id").GetString() == "dirt" && t.GetProperty("name").GetString() == "Workspace soil");
        workspace.Clear(saved.Token);
        var portable = ScenarioDefinitionTransport.Decode(result.GetProperty("transport").GetString()!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(portable)))), result.GetProperty("fingerprint").GetString());
        Assert.Equal("Workspace soil", GameEngine.CreateGame(portable).Physical.Board.TerrainTypes.Single(t => t.Id == "dirt").Name);
        Assert.Equal("Dirt", TerrainCatalog.Find(TerrainCatalog.All, "dirt").Name);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
    }

    private sealed class Host(WebApplication app, HttpClient client, string directory) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Client => client;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var directory = Path.Combine(Path.GetTempPath(), "delve-world-" + Guid.NewGuid().ToString("N"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root,
                "--CanonicalContent:Directory", Path.Combine(directory, "canonical"),
                "--ContentWorkspace:Path", Path.Combine(directory, "workspace"),
                "--ContentLibrary:Path", Path.Combine(directory, "content-library"), "--Library:Path", Path.Combine(directory, "library")]);
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) }, directory);
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose(); await app.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
