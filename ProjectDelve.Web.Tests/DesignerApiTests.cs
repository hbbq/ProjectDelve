using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class DesignerApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CatalogProjectsOnlyCanonicalEditorMetadata()
    {
        await using var host = await Host.Start();
        var catalog = (await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog"));
        Assert.Equal(50, catalog.GetProperty("maxBoardSize").GetInt32());
        var machineType = Assert.Single(catalog.GetProperty("machineTypes").EnumerateArray());
        Assert.Equal(JsonSerializer.SerializeToElement(MachineTypes.DiamondPress, Json).GetRawText(), machineType.GetRawText());
        Assert.Equal(Enum.GetNames<RailDirection>(), catalog.GetProperty("railDirections").Deserialize<string[]>());
        Assert.Contains("Rail", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Asphalt", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Gravel", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Lava", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Equal(Enum.GetNames<OrderKind>(), catalog.GetProperty("orderKinds").Deserialize<string[]>());
        var projected = catalog.GetProperty("unitTypes").EnumerateArray().ToArray();
        Assert.Equal(CanonicalUnitTypes.All.Select(type => type.Id).Order(),
            projected.Select(type => type.GetProperty("unitTypeId").GetString()).Order());
        foreach (var entry in projected)
        {
            var type = CanonicalUnitTypes.Find(entry.GetProperty("unitTypeId").GetString()!)!;
            Assert.Equal(type.Hp, entry.GetProperty("maxHp").GetInt32());
            Assert.Equal(type.DisplayName, entry.GetProperty("displayName").GetString());
            Assert.Equal(FootprintGeometry.OccupiedCells(type.Footprint, new(0, 0)),
                entry.GetProperty("footprintOffsets").Deserialize<Cell[]>(Json));
            Assert.Equal(new[] { "unitTypeId", "displayName", "maxHp", "footprintOffsets", "cellSpan" }, entry.EnumerateObject().Select(p => p.Name));
        }
        var dragon = projected.Single(type => type.GetProperty("unitTypeId").GetString() == UnitTypeIds.RedDragon);
        Assert.Equal(2, dragon.GetProperty("cellSpan").GetInt32());
        Assert.Equal(4, dragon.GetProperty("footprintOffsets").GetArrayLength());
    }

    [Theory]
    [InlineData("railway-fixture")]
    [InlineData("serialization")]
    [InlineData("shaman")]
    [InlineData("shaper")]
    [InlineData("complete-data")]
    [InlineData("world-effects")]
    [InlineData("lava")]
    [InlineData("gravel")]
    [InlineData("asphalt")]
    [InlineData("machines")]
    public async Task DesignerRoundTripPreservesDefinitionsAndUsesExistingGameImport(string id)
    {
        await using var host = await Host.Start();
        var definition = id == "railway-fixture" ? RailwayProjectionTests.Fixture() : id is "complete-data" or "world-effects" or "lava" or "gravel" or "asphalt" or "machines" ? CompleteData() : WebScenarioFixtures.Named(id);
        if (id == "asphalt") definition = definition with { Board = definition.Board with { DefaultTerrain = TerrainKind.Asphalt, Cells = [new(new(4, 4), TerrainKind.Asphalt)] } };
        if (id == "gravel") definition = definition with { Board = definition.Board with { DefaultTerrain = TerrainKind.Gravel, Cells = [new(new(4, 4), TerrainKind.Gravel)] } };
        if (id == "machines") definition = definition with { Machines = [new("press", new(3, 0), MachineTypes.DiamondPress)
            { StoredResources = [new(ResourceTypes.Coal, 2)] }] };
        if (id == "world-effects") definition = definition with { WorldEffects = new(3, 2) };
        if (id == "lava") definition = definition with { Board = definition.Board with { Cells = [new(new(4, 4), TerrainKind.Lava)] } };
        var before = await host.Client.GetStringAsync("/api/game");
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var importedJson = await imported.Content.ReadAsStringAsync();
        var loaded = ScenarioDefinitionJson.FromJson(importedJson);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(loaded));
        using var validated = await host.PostDraft("validate", importedJson);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", importedJson);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
        using var restarted = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(WebScenarioFixtures.Combat()) });
        var fresh = (await restarted.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(GameEngine.CreateGame(WebScenarioFixtures.Combat()), fresh.Result.State);
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = fresh.Revision, transport });
        played.EnsureSuccessStatusCode();
        var game = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal("imported", game.ScenarioId);
        var publicSetup = GameEngine.CreateGame(definition);
        if (publicSetup.WorldDeck is not null)
        {
            using var publicJson = JsonDocument.Parse(await played.Content.ReadAsStringAsync());
            Assert.Equal(publicSetup.WorldDeck.DrawPile.Count, publicJson.RootElement.GetProperty("presentation").GetProperty("worldEffects").GetProperty("drawPileCount").GetInt32());
            publicSetup.WorldDeck.DrawPile = [];
        }
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(publicSetup, game.Result.State);
        using var restartImported = await host.Client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = game.Revision });
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(publicSetup,
            (await restartImported.Content.ReadFromJsonAsync<GameResponse>(Json))!.Result.State);
    }

    [Theory]
    [InlineData("placement", "units[1]")]
    [InlineData("hp", "units[0]")]
    [InlineData("agency", "units[0]")]
    [InlineData("cell", "board.cells[0]")]
    [InlineData("edge", "board.edges[0]")]
    [InlineData("unique", "units[1]")]
    [InlineData("world-cycling", "worldEffects")]
    [InlineData("world-draws", "worldEffects")]
    [InlineData("lava-unit", "units[0]")]
    [InlineData("lava-resource", "resources[0]")]
    public async Task InvalidDraftUsesAuthoritativeFailureAndNeverMutatesGame(string invalid, string path)
    {
        await using var host = await Host.Start();
        var definition = CompleteData();
        definition = invalid switch
        {
            "lava-unit" => definition with { Board = definition.Board with { Cells = [new(definition.Units[0].Anchor, TerrainKind.Lava)] } },
            "lava-resource" => definition with { Board = definition.Board with { Cells = [new(new(0, 0), TerrainKind.Lava)] },
                Resources = [new(ResourceTypes.Coal, new(0, 0))] },
            "world-cycling" => definition with { WorldEffects = new(1, 0) },
            "world-draws" => definition with { WorldEffects = new(-1, 1) },
            "placement" => definition with { Units = [definition.Units[0], definition.Units[0]] },
            "unique" => definition with { Units = [definition.Units[1], definition.Units[1]] },
            "hp" => definition with { Units = [definition.Units[0] with { InitialHp = 99 }] },
            "agency" => definition with { Agency = [] },
            "cell" => definition with { Board = definition.Board with { Cells = [new(new(99, 0), TerrainKind.Tree)] } },
            "edge" => definition with { Board = definition.Board with { Edges = [new(new(4, 0), EdgeDirection.Right, EdgeKind.Wall)] } },
            _ => throw new InvalidOperationException()
        };
        var expected = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        var before = await host.Client.GetStringAsync("/api/game");
        foreach (var operation in new[] { "validate", "export" })
        {
            using var response = await host.PostDraft(operation, ScenarioDefinitionJson.ToJson(definition));
            Assert.Equal(operation == "validate" ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            var validation = (await response.Content.ReadFromJsonAsync<DesignerValidation>())!;
            Assert.False(validation.Valid);
            Assert.Equal(expected.Message, validation.Errors[0].Message);
            Assert.Equal(path, validation.Errors[0].Path);
        }
        using var failedImport = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = ScenarioDefinitionTransport.Encode(definition) });
        Assert.Equal(HttpStatusCode.BadRequest, failedImport.StatusCode);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
    }

    [Theory]
    [InlineData(51, 1)]
    [InlineData(1, 51)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task HostLimitRejectsBeforeMaterializationOnDesignerAndPlayImport(int width, int height)
    {
        await using var host = await Host.Start();
        var definition = Scenario.Define(Scenario.Map(width, height, defaultTerrain: TerrainKind.Grass), []);
        var json = ScenarioDefinitionJson.ToJson(definition);
        using var validated = await host.PostDraft("validate", json);
        Assert.False((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", json);
        Assert.Equal(HttpStatusCode.BadRequest, exported.StatusCode);
        var transport = ScenarioDefinitionTransport.Encode(definition);
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        Assert.Equal(HttpStatusCode.BadRequest, imported.StatusCode);
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        Assert.Equal(HttpStatusCode.BadRequest, played.StatusCode);
        Assert.Contains("editor policy", await played.Content.ReadAsStringAsync());
        Assert.Equal(0, (await host.Client.GetFromJsonAsync<GameResponse>("/api/game", Json))!.Revision);
    }

    [Fact]
    public async Task DesignerUsesStrictScenarioJsonEnumsAndAllowsFiftyCellBoard()
    {
        await using var host = await Host.Start();
        var data = JsonNode.Parse(ScenarioDefinitionJson.ToJson(CompleteData()))!;
        data["board"]!["defaultTerrain"] = 0;
        using var rejected = await host.PostDraft("validate", data.ToJsonString());
        Assert.False((await rejected.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var accepted = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(Scenario.Define(Scenario.Map(50, 50), [])));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("future / side")]
    public async Task MachineOwnershipSurvivesDesignerImportValidateExportAndReload(string? owner)
    {
        await using var host = await Host.Start();
        var definition = CompleteData() with { Machines = [new("press", new(4, 0), MachineTypes.DiamondPress) { OwnerSide = owner }] };
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var json = await imported.Content.ReadAsStringAsync();
        Assert.Equal(owner, ScenarioDefinitionJson.FromJson(json).Machines!.Single().OwnerSide);
        using var validated = await host.PostDraft("validate", json);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", json);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        var restored = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(restored));
        Assert.Equal(owner, GameEngine.CreateGame(restored).Physical.Board.Machines.Single().OwnerSide);
    }

    [Fact]
    public async Task DanglingMachineOwnerIsReportedAtEditablePropertyAndCannotBeExported()
    {
        await using var host = await Host.Start();
        var definition = CompleteData() with { Machines = [new("press", new(4, 0), MachineTypes.DiamondPress) { OwnerSide = "removed-side" }] };
        var json = ScenarioDefinitionJson.ToJson(definition);
        using var validated = await host.PostDraft("validate", json);
        var validation = (await validated.Content.ReadFromJsonAsync<DesignerValidation>())!;
        Assert.False(validation.Valid);
        Assert.Contains(validation.Errors, error => error.Path == "machines[0].ownerSide" && error.Message.Contains("OwnerSide"));
        using var exported = await host.PostDraft("export", json);
        Assert.Equal(HttpStatusCode.BadRequest, exported.StatusCode);
    }

    private static ScenarioDefinition CompleteData() => Scenario.Define(
        Scenario.Map(5, 5, defaultTerrain: TerrainKind.Grass,
            cells: [Scenario.Tile(4, 4, TerrainKind.Grass), Scenario.Tile(3, 3, TerrainKind.StoneFloor)],
            edges: [Scenario.Edge(1, 1, EdgeDirection.Right, EdgeKind.None), Scenario.Edge(2, 1, EdgeDirection.Down, EdgeKind.OpenDoor)]),
        [Scenario.Group(UnitTypeIds.Grunt, "side / amber — blÅ", ControllerKind.Human, Scenario.At(0, 0)),
         Scenario.Group(UnitTypeIds.Wizard, "side / amber — blÅ", ControllerKind.Automated, Scenario.At(2, 2, Posture.Lying, 2)),
         Scenario.Group(UnitTypeIds.Goblin, "future / side", ControllerKind.Automated)],
        [UnitTypeIds.Goblin, UnitTypeIds.Wizard, UnitTypeIds.Grunt]);

    private sealed class Host(WebApplication app, HttpClient client, string contentRoot) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public string ContentRoot => contentRoot;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], random: null, initialState: GameEngine.CreateGame(WebScenarioFixtures.Combat()));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, root);
        }
        public Task<HttpResponseMessage> PostDraft(string operation, string json) => client.PostAsync($"/api/designer/{operation}", new StringContent(json, Encoding.UTF8, "application/json"));
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
