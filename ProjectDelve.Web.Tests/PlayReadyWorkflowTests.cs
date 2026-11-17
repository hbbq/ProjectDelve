using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class PlayReadyWorkflowTests
{
    private static ScenarioDefinition Fixture()
    {
        var cells = Enumerable.Range(7, 2).SelectMany(y => Enumerable.Range(2, 5).Select(x => new Cell(x, y))).ToList();
        var core = cells.Where(c => c.X < 6).ToList();
        return new()
        {
            Board = new() { Width = 12, Height = 10, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(5, cells, core, core.Concat(cells.Except(core)).ToList(), new(6, 8), new(7, 8), 10, 8),
                [new("required-room", "fixture", "empty-building/room", ScenarioAccessClass.Ordinary, null,
                    [new("interior", ScenarioAccessCoverage.All, [new(9, 1)])])])
        };
    }

    [Fact]
    public void QuickPlayUsesPublishedOrderAndPreservesTheOriginalSetup()
    {
        var original = Fixture(); var before = ScenarioDefinitionJson.ToJson(original);
        var prepared = WorldQuickPlay.Prepare(original, ContentBundle.Canonical());
        Assert.Equal(original.Setup!.Deployment.PlacementOrder.Take(5), prepared.Units.Select(u => u.Anchor));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(original));
        Assert.Equal(ScenarioDefinitionJson.ToJson(prepared), ScenarioDefinitionJson.ToJson(WorldQuickPlay.Prepare(original, ContentBundle.Canonical())));
        Assert.Equal(JsonSerializer.Serialize(original.Setup), JsonSerializer.Serialize(prepared.Setup));
        Assert.True(original.Setup.Deployment.Cells.Count - prepared.Units.Count >= 5);
        Assert.DoesNotContain("requiredLocations", JsonSerializer.Serialize(GameEngine.CreateGame(prepared)));
        Assert.DoesNotContain("requiredLocations", JsonSerializer.Serialize(BrowserProjection.Create(new(GameEngine.CreateGame(prepared), [], null))));
    }

    [Fact]
    public void QuickPlayRejectsInsufficientCapacityAndOccupationWithoutLegacyFallback()
    {
        var definition = Fixture();
        var small = definition with { Setup = definition.Setup! with { Deployment = definition.Setup!.Deployment with { Capacity = 3, MinimumArea = 6, MinimumCore = 5 } } };
        Assert.Contains("capacity", Assert.Throws<ArgumentException>(() => WorldQuickPlay.Prepare(small, ContentBundle.Canonical())).Message);
        var requested = WorldQuickPlay.Prepare(small, ContentBundle.Canonical(), [UnitTypeIds.Cleric, UnitTypeIds.Rogue]);
        Assert.Equal(2, requested.Units.Count);
        var occupied = definition with { UnitTypeIds = [UnitTypeIds.Goblin], Units = [new(UnitTypeIds.Goblin, "red", new(2, 7))],
            Agency = [new(UnitTypeIds.Goblin, "red", ControllerKind.Automated)] };
        Assert.Contains("PlayReady setup is invalid", Assert.Throws<ArgumentException>(() => WorldQuickPlay.Prepare(occupied, ContentBundle.Canonical())).Message);
        var blocked = definition with { Board = definition.Board with { Cells = [new(new(2, 7), TerrainKind.SolidRock)] } };
        Assert.Contains("PlayReady setup is invalid", Assert.Throws<ArgumentException>(() => WorldQuickPlay.Prepare(blocked, ContentBundle.Canonical())).Message);
    }

    [Fact]
    public async Task DesignerSeparatesEngineValidityFromImportedSetupAndRetainsDeclarationsOnExport()
    {
        await using var host = await Host.Start();
        var definition = Fixture();
        var stale = definition with { Board = definition.Board with { Edges = Enumerable.Range(0, 12)
            .Select(x => new EdgeDefinition(new(x, 4), EdgeDirection.Down, EdgeKind.Wall)).ToList() } };
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = ScenarioDefinitionTransport.Encode(stale) });
        imported.EnsureSuccessStatusCode();
        var json = await imported.Content.ReadAsStringAsync();
        using var validated = await host.Post("/api/designer/validate", json);
        var result = (await validated.Content.ReadFromJsonAsync<DesignerValidation>())!;
        Assert.True(result.Valid); Assert.Equal("Invalid", result.PlayReady!.Status);
        Assert.Contains(result.PlayReady.Diagnostics, d => d.LocationId == "required-room");
        using var exported = await host.Post("/api/designer/export", json);
        exported.EnsureSuccessStatusCode();
        var output = await exported.Content.ReadFromJsonAsync<JsonElement>();
        var restored = ScenarioDefinitionTransport.Decode(output.GetProperty("transport").GetString()!);
        Assert.Equal(JsonSerializer.Serialize(stale.Setup), JsonSerializer.Serialize(restored.Setup));
        using var quick = await host.Post("/api/world-generation/quick-play", json);
        Assert.Equal(HttpStatusCode.BadRequest, quick.StatusCode);
        Assert.Contains("required-room", await quick.Content.ReadAsStringAsync());
        using var legacy = await host.Post("/api/designer/validate", ScenarioDefinitionJson.ToJson(definition with { Setup = null }));
        Assert.Equal("Legacy", (await legacy.Content.ReadFromJsonAsync<DesignerValidation>())!.PlayReady!.Status);
    }

    [Fact]
    public async Task NormalGenerationPublishesDeterministicSetupFromItsRecipe()
    {
        await using var host = await Host.Start();
        var recipe = new WorldGenerationRecipe(new(0, 0, 16, 14), new(50, 20, 30), 42, new(), new(), new(), new(PopulationDensity.Low), HeroCapacity: 8);
        async Task<JsonElement> Generate()
        {
            using var response = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = recipe.ToString() });
            response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var a = await Generate(); var b = await Generate();
        Assert.Equal("Valid", a.GetProperty("playReady").GetProperty("status").GetString());
        Assert.Equal(a.GetProperty("fingerprint").GetString(), b.GetProperty("fingerprint").GetString());
        Assert.Equal(recipe.ToString(), a.GetProperty("code").GetString());
        Assert.Equal(8, a.GetProperty("definition").GetProperty("setup").GetProperty("deployment").GetProperty("capacity").GetInt32());
        Assert.Contains("-HC8-S", a.GetProperty("code").GetString());
        Assert.False(a.TryGetProperty("mode", out _));
        Assert.True(a.GetProperty("definition").TryGetProperty("setup", out _));
        Assert.Equal(WorldGenerator.Generate(recipe).Fingerprint, a.GetProperty("fingerprint").GetString());
        using var quick = await host.Client.PostAsJsonAsync("/api/world-generation/quick-play", a.GetProperty("definition"));
        quick.EnsureSuccessStatusCode();
        var prepared = ScenarioDefinitionTransport.Decode((await quick.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!);
        Assert.Equal(prepared.Setup!.Deployment.PlacementOrder.Take(5), prepared.Units.Where(u => u.SideId == HeroesVsMonsters.Heroes).Select(u => u.Anchor));
    }

    private sealed class Host(WebApplication app, HttpClient client, string directory) : IAsyncDisposable
    {
        internal HttpClient Client => client;
        internal static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var directory = Path.Combine(Path.GetTempPath(), "delve-playready-" + Guid.NewGuid().ToString("N"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root,
                "--CanonicalContent:Directory", Path.Combine(directory, "canonical"), "--ContentWorkspace:Path", Path.Combine(directory, "workspace"),
                "--ContentLibrary:Path", Path.Combine(directory, "content-library"), "--Library:Path", Path.Combine(directory, "library")]);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, directory);
        }
        internal Task<HttpResponseMessage> Post(string path, string json) => Client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await app.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
