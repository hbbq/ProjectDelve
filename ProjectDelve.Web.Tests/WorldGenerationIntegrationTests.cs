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
    public async Task CivilizedPreviewExportsFinalEdgesAndCapturedFurnitureToDesigner()
    {
        await using var host = await Host.Start();
        var workspace = host.App.Services.GetRequiredService<ContentWorkspace>();
        var table = TerrainRoster.Assemblies.Single(a => a.Id == "stone-floor-with-table") with { Name = "Captured village table" };
        var saved = workspace.Save(new() { Assemblies = [table] }, workspace.Snapshot().Token);
        var code = Code.Replace("30x24", "120x100").Replace("-S", "-E1-I2-B1111-C1-N2-V1-P2-S");
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        workspace.Clear(saved.Token);
        var frames = result.GetProperty("preview").GetProperty("frames");
        var civilization = frames.EnumerateArray().Single(f => f.GetProperty("stage").GetString() == "Civilization");
        Assert.NotEmpty(civilization.GetProperty("sites").EnumerateArray());
        Assert.NotEmpty(civilization.GetProperty("construction").EnumerateArray());
        Assert.NotEmpty(civilization.GetProperty("changedEdges").EnumerateArray());
        var definition = ScenarioDefinitionTransport.Decode(result.GetProperty("transport").GetString()!);
        Assert.Equal(definition.Board.Edges.Count, frames[frames.GetArrayLength() - 1].GetProperty("edges").GetArrayLength());
        Assert.Contains(GameEngine.CreateGame(definition).Physical.Board.TerrainTypes, t => t.Id == table.Id && t.Name == table.Name);
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = result.GetProperty("transport").GetString() });
        imported.EnsureSuccessStatusCode(); Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var validated = await host.Client.PostAsJsonAsync("/api/designer/validate", result.GetProperty("definition"));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
    }

    [Fact]
    public async Task RouteRecipeExportsAcceptedFinalNetworkAndDesignerBoard()
    {
        await using var host = await Host.Start(); var before = await host.Client.GetStringAsync("/api/game");
        var code = Code.Replace("30x24", "120x100").Replace("-S", "-E1-I2-B1111-C1-N2-V1-P2-Q1-T2-JRoad-L1-A2-U1-Z2-S");
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var preview = result.GetProperty("preview"); var frames = preview.GetProperty("frames"); var final = frames[frames.GetArrayLength() - 1];
        Assert.NotEmpty(final.GetProperty("routeNetwork").GetProperty("routes").EnumerateArray());
        Assert.NotEmpty(final.GetProperty("constructionBatches").EnumerateArray());
        var definition = ScenarioDefinitionTransport.Decode(result.GetProperty("transport").GetString()!);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(result.GetProperty("definition").GetRawText())));
        var cells = definition.Board.Cells.ToDictionary(c => c.Position.Y * 120 + c.Position.X, c => c.TerrainTypeId);
        var palette = preview.GetProperty("terrainPalette").Deserialize<string[]>()!; var terrain = final.GetProperty("terrain").GetString()!;
        for (var i = 0; i < terrain.Length; i++) Assert.Equal(palette[terrain[i] - 'A'], cells.GetValueOrDefault(i, "grass"));
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = result.GetProperty("transport").GetString() });
        imported.EnsureSuccessStatusCode(); Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var validated = await host.Client.PostAsJsonAsync("/api/designer/validate", result.GetProperty("definition"));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
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

    [Fact]
    public async Task PopulationPreviewAndPortableDesignerScenarioPreserveGroupsOrdersAndRoutesWithoutReplacingGame()
    {
        await using var host = await Host.Start();
        var before = await host.Client.GetStringAsync("/api/game");
        var code = "WLD1-80x60-X-17-Y-13-F0-W0-R0-KBalanced-HAuto-C1-N2-V1-P2-Q1-T2-JRoad-L1-A1-U0-Z2-MHigh-D3-S0000000000003039";
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var definition = ScenarioDefinitionTransport.Decode(result.GetProperty("transport").GetString()!);
        Assert.NotEmpty(definition.Units); Assert.NotEmpty(definition.Orders!);
        Assert.Equal("Generated populated world", result.GetProperty("title").GetString());
        Assert.Null(definition.Objectives); Assert.Null(definition.FogOfWarSides); Assert.Null(definition.WorldEffects);
        Assert.Empty(definition.Loot!.Composition!);
        Assert.All(definition.Units, unit =>
        {
            Assert.Equal(HeroesVsMonsters.Monsters, unit.SideId);
            Assert.False(string.IsNullOrWhiteSpace(unit.GroupId)); Assert.False(string.IsNullOrWhiteSpace(unit.UnitId));
            Assert.Contains(definition.Agency, a => a.UnitTypeId == unit.UnitTypeId && a.SideId == unit.SideId && a.GroupId == unit.GroupId);
        });
        var frames = result.GetProperty("preview").GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal("Population", frames[^1].GetProperty("stage").GetString());
        Assert.All(frames[..^1], frame => Assert.True(!frame.TryGetProperty("population", out var p) || p.ValueKind == JsonValueKind.Null));
        var population = frames[^1].GetProperty("population");
        var encounterGroups = population.GetProperty("encounters").EnumerateArray()
            .SelectMany(e => e.GetProperty("groupIds").EnumerateArray().Select(g => g.GetString())).ToArray();
        Assert.Equal(encounterGroups.Length, encounterGroups.Distinct().Count());
        var previewUnitIds = population.GetProperty("encounters").EnumerateArray()
            .SelectMany(e => e.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("unitId").GetString())).Order().ToArray();
        Assert.Equal(definition.Units.Select(u => u.UnitId).Order(), previewUnitIds);
        Assert.Equal(definition.Units.Count, population.GetProperty("statistics").GetProperty("units").GetInt32());
        Assert.NotEmpty(result.GetProperty("unitDefinitions").EnumerateArray());
        Assert.DoesNotContain("encounterId", result.GetProperty("definition").GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(result.GetProperty("definition").GetRawText())));
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = result.GetProperty("transport").GetString() });
        imported.EnsureSuccessStatusCode(); Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var validated = await host.Client.PostAsJsonAsync("/api/designer/validate", result.GetProperty("definition"));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var repeated = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        repeated.EnsureSuccessStatusCode(); var second = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(result.GetProperty("fingerprint").GetString(), second.GetProperty("fingerprint").GetString());
        Assert.Equal(result.GetProperty("preview").GetRawText(), second.GetProperty("preview").GetRawText());
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("High")]
    public async Task TinyPopulationWorldsRemainValidAuthoringArtifacts(string density)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new
        { code = $"WLD1-2x2-X0-Y0-F0-W0-R0-KBalanced-HAuto-M{density}-D3-S0000000000003039" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var definition = ScenarioDefinitionTransport.Decode(result.GetProperty("transport").GetString()!);
        Assert.Empty(definition.Units); Assert.Null(definition.Objectives);
        _ = GameEngine.CreateGame(definition);
    }

    [Fact]
    public async Task QuickPlayPreparesOnlyATemporaryCopyThenUsesTheExistingPlaytestImport()
    {
        await using var host = await Host.Start();
        var code = "WLD1-40x35-X0-Y0-F0-W0-R0-KBalanced-HAuto-C1-N2-V0-P2-Q1-T1-JRoad-L1-A0-U0-Z2-MHigh-D3-S0000000000003039";
        using var generatedResponse = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        generatedResponse.EnsureSuccessStatusCode();
        var generated = await generatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var originalJson = generated.GetProperty("definition").GetRawText();
        var original = ScenarioDefinitionJson.FromJson(originalJson);
        var before = await host.Client.GetStringAsync("/api/game");
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation/quick-play", generated.GetProperty("definition"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var prepared = await response.Content.ReadFromJsonAsync<JsonElement>();
        var transport = prepared.GetProperty("transport").GetString()!;
        var temporary = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(original.Units.Count + 5, temporary.Units.Count);
        Assert.Equal(LootPresets.AllPresetId, temporary.Loot!.Preset);
        Assert.Equal(new[] { UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Wizard, UnitTypeIds.Cleric, UnitTypeIds.Beastmaster },
            temporary.Units.Skip(original.Units.Count).Select(unit => unit.UnitTypeId));
        Assert.All(temporary.Agency.Skip(original.Agency.Count), agency => Assert.Equal(ControllerKind.Human, agency.Controller));
        Assert.Equal(original.Units, temporary.Units.Take(original.Units.Count));
        Assert.Equal(original.Orders, temporary.Orders);
        Assert.Equal(original.Routes, temporary.Routes); Assert.Equal(original.Waypoints, temporary.Waypoints);
        Assert.Equal(JsonSerializer.Serialize(original.Board), JsonSerializer.Serialize(temporary.Board));
        Assert.All(temporary.Objectives!, o => Assert.Equal(ScenarioObjective.NoObjective, o.Objective));
        Assert.Contains(HeroesVsMonsters.Heroes, temporary.FogOfWarSides!);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
        using var repeated = await host.Client.PostAsJsonAsync("/api/world-generation/quick-play", generated.GetProperty("definition"));
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(transport, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString());
        Assert.Equal(originalJson, generated.GetProperty("definition").GetRawText());
        using var imported = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new
        { expectedRevision = JsonDocument.Parse(before).RootElement.GetProperty("revision").GetInt64(), transport });
        Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync());
        var state = host.App.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State;
        Assert.Equal(temporary.Units.Count, state.Units.Count);
        Assert.Equal(state.LootCardDefinitions.Count, state.LootDeck!.DrawPile.Count);
        Assert.Contains(HeroesVsMonsters.Heroes, state.FogOfWarSides);
        Assert.All(state.Objectives, objective => Assert.Equal(ScenarioObjective.NoObjective, objective.Objective));
        using var unchanged = await host.Client.PostAsJsonAsync("/api/world-generation", new { code });
        unchanged.EnsureSuccessStatusCode();
        var regenerated = await unchanged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(generated.GetProperty("fingerprint").GetString(), regenerated.GetProperty("fingerprint").GetString());
        Assert.Equal(generated.GetProperty("transport").GetString(), regenerated.GetProperty("transport").GetString());
    }

    [Fact]
    public async Task QuickPlayFailureDoesNotReplaceTheGame()
    {
        await using var host = await Host.Start(); var before = await host.Client.GetStringAsync("/api/game");
        var original = new ScenarioDefinition
        {
            Board = new() { Width = 2, Height = 2, DefaultTerrain = TerrainKind.SolidRock },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        };
        using var document = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(original));
        using var response = await host.Client.PostAsJsonAsync("/api/world-generation/quick-play", document.RootElement);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Hero", await response.Content.ReadAsStringAsync());
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
