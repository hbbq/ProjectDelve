using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class TerrainLifecycleTests
{
    private const string FloorId = "fixture-cobalt-paper";
    private const string ObstacleId = "fixture-unexpected-plinth";
    private const string SecretId = "fixture-unseen-obsidian";
    private static readonly Cell ObstacleCell = new(1, 0);
    private static readonly Cell SecretCell = new(7, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Test-owned content: no regression assertions depend on a mutable library scenario.
    private static ScenarioDefinition Fixture(bool fog = false) => new()
    {
        TerrainTypes =
        [
            new(FloorId, "Cobalt Paper", true, false) { Presentation = new("Grass") },
            new(ObstacleId, "A Most Unexpected Plinth", false, false)
                { Presentation = new("unpublished-test-recipe") },
            new(SecretId, "Unseen Obsidian", false, true) { Presentation = new("SolidRock") }
        ],
        Board = new()
        {
            Width = 10, Height = 3, DefaultTerrainTypeId = FloorId,
            Cells =
            [
                new(ObstacleCell, TerrainKind.StoneFloor) { TerrainTypeId = ObstacleId },
                new(SecretCell, TerrainKind.StoneFloor) { TerrainTypeId = SecretId }
            ]
        },
        UnitTypeIds = ["terrain-observer"],
        UnitTypes = [new("terrain-observer", 2, 1, 1, 1, 1)],
        Units = [new("terrain-observer", "blue", new(0, 1), UnitId: "observer")],
        Agency = [new("terrain-observer", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        FogOfWarSides = fog ? ["blue"] : null,
        Loot = LootSettings.Empty
    };

    [Fact]
    public async Task DesignerImportExportAndPlayUseTypedCellsWithoutLegacyPlaceholders()
    {
        await using var host = await Host.Start();
        var definition = Fixture();
        var authored = ScenarioDefinitionJson.ToJson(definition);
        var before = await host.Client.GetStringAsync("/api/game");
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var importedJson = await imported.Content.ReadAsStringAsync();
        Assert.Equal(authored, importedJson);
        using (var document = JsonDocument.Parse(importedJson))
        {
            var board = document.RootElement.GetProperty("board");
            Assert.Equal(FloorId, board.GetProperty("defaultTerrainTypeId").GetString());
            foreach (var cell in board.GetProperty("cells").EnumerateArray())
            {
                Assert.True(cell.TryGetProperty("terrainTypeId", out _));
                Assert.False(cell.TryGetProperty("terrain", out _));
            }
        }

        // Editing the board retains its local catalog and changes only the authored placement.
        var edited = ScenarioDefinitionJson.FromJson(importedJson);
        edited.Board.Cells.Add(new(new(3, 2), TerrainKind.StoneFloor) { TerrainTypeId = ObstacleId });
        using var validated = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(edited));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(edited));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(edited), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));

        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        AssertTerrain(response.Result.State);
        Assert.Equal(ObstacleId, response.Result.State.Physical.Board.TerrainDefinitionAt(new(3, 2)).Id);
        Assert.False(response.Result.State.Physical.Board.TerrainDefinitionAt(new(3, 2)).Passable);
    }

    [Fact]
    public async Task DesignerRejectsAnUnresolvedLocalTerrainReference()
    {
        await using var host = await Host.Start();
        var definition = Fixture() with { TerrainTypes = [] };
        using var validated = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(definition));
        var result = (await validated.Content.ReadFromJsonAsync<DesignerValidation>())!;
        Assert.False(result.Valid);
        Assert.NotEmpty(result.Errors);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(definition));
        Assert.Equal(HttpStatusCode.BadRequest, exported.StatusCode);
    }

    [Fact]
    public void ImportedTerrainSurvivesProgressionDetachedSaveAndRestart()
    {
        var fixture = Fixture();
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(fixture));
        var imported = game.StartTransportScenario(game.Snapshot().Revision, ScenarioDefinitionTransport.Encode(fixture));
        AssertTerrain(imported.Result.State);
        var round = game.StartRound(imported.Revision);
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        Assert.Equal(new Cell(2, 1), Assert.Single(moved.Result.State.Physical.Figures).Position);
        AssertTerrain(moved.Result.State);
        var saved = PlaytestGame.Detach(moved);
        using var resumed = new PlaytestGame(new Dice(), saved.Result.State);
        Assert.Equal(new Cell(2, 1), Assert.Single(resumed.Snapshot().Result.State.Physical.Figures).Position);
        AssertTerrain(resumed.Snapshot().Result.State);

        var restarted = game.Restart(moved.Revision);
        Assert.NotEqual(imported.SessionId, restarted.SessionId);
        Assert.Equal(0, restarted.Result.State.Round);
        Assert.Equal(new Cell(0, 1), Assert.Single(restarted.Result.State.Physical.Figures).Position);
        AssertTerrain(restarted.Result.State);
        Assert.Equal(imported.VisualSeed, restarted.VisualSeed);
    }

    [Fact]
    public async Task StreamReplayAndResolutionSnapshotsRevealTerrainOnlyAtDiscovery()
    {
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(Fixture(fog: true)));
        using var live = game.Subscribe(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var baseline = await live.Reader.ReadAsync(timeout.Token);
        var before = game.BrowserUpdate(baseline);
        Assert.DoesNotContain(SecretId, JsonSerializer.Serialize(before, Json));
        Assert.DoesNotContain("Unseen Obsidian", JsonSerializer.Serialize(before, Json));
        Assert.Contains(before.Snapshot.Result.State.Physical.Board.TerrainTypes, t => t.Id == ObstacleId);
        Assert.Contains(SecretCell, before.Snapshot.Presentation.UnknownCells);

        var round = game.StartRound(baseline.Revision);
        Assert.DoesNotContain(SecretId, JsonSerializer.Serialize(game.BrowserResponse(round), Json));
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        var discovered = game.BrowserResponse(moved);
        Assert.DoesNotContain(SecretCell, discovered.Presentation.UnknownCells);
        Assert.Equal(SecretId, discovered.Result.State.Physical.Board.TerrainDefinitionAt(SecretCell).Id);
        Assert.Contains(discovered.Result.ResolutionSteps,
            s => s.StateAfter.Physical.Board.TerrainTypes.Any(t => t.Id == SecretId));

        var streamed = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) streamed.Add(game.BrowserUpdate(update));
        Assert.NotEmpty(streamed);
        Assert.Contains(streamed, u => u.Kind == "event");
        Assert.All(streamed, AssertObservation);
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(streamed.Select(u => u.Sequence), replayed.Select(u => u.Sequence));
        Assert.Equal(streamed.Select(u => JsonSerializer.Serialize(u, Json)), replayed.Select(u => JsonSerializer.Serialize(u, Json)));
        Assert.All(replayed, AssertObservation);
    }

    private static void AssertObservation(GameUpdate update)
    {
        void Check(GameState state)
        {
            var board = state.Physical.Board;
            var discovered = state.ExploredCells["blue"].Contains(SecretCell);
            Assert.Equal(discovered, board.TerrainTypes.Any(t => t.Id == SecretId));
            Assert.Equal(discovered, board.Terrain.Any(t => t.TypeId == SecretId));
            Assert.Equal("A Most Unexpected Plinth", board.TerrainDefinitionAt(ObstacleCell).Name);
        }
        Check(update.Snapshot.Result.State);
        foreach (var step in update.Snapshot.Result.ResolutionSteps) Check(step.StateAfter);
    }

    private static void AssertTerrain(GameState state)
    {
        var board = state.Physical.Board;
        var obstacle = board.TerrainDefinitionAt(ObstacleCell);
        Assert.Equal(ObstacleId, obstacle.Id);
        Assert.Equal("A Most Unexpected Plinth", obstacle.Name);
        Assert.False(obstacle.Passable);
        Assert.False(obstacle.BlocksLos);
        Assert.Equal("unpublished-test-recipe", obstacle.Presentation.Recipe);
        Assert.Equal(FloorId, board.TerrainDefinitionAt(new(0, 1)).Id);
        Assert.True(board.TerrainDefinitionAt(new(0, 1)).Passable);
        Assert.True(board.TerrainDefinitionAt(SecretCell).BlocksLos);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root],
                random: new Dice(), initialState: GameEngine.CreateGame(Fixture()));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }
        public Task<HttpResponseMessage> PostDraft(string operation, string json) => client.PostAsync($"/api/designer/{operation}",
            new StringContent(json, Encoding.UTF8, "application/json"));
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
