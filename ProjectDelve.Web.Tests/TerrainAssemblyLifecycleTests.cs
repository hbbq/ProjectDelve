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

public sealed class TerrainAssemblyLifecycleTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private const string AssemblyId = "fixture-moss-table";
    private const string SecretId = "fixture-hidden-monument";
    private const string SecretComponent = "fixture-hidden-sculpture";
    private const string SecretSurface = "fixture-hidden-basalt";
    private static readonly Cell SecretCell = new(7, 1);

    private const string FixtureJson = """
    {
      "fixedComponentTypes": [
        { "id": "fixture-cot", "name": "An Unexpected Cot", "blocksMovement": true, "blocksLos": false,
          "presentation": { "recipe": "unpublished-cot", "layer": "Object" } },
        { "id": "fixture-hidden-sculpture", "name": "A Hidden Sculpture", "blocksMovement": true, "blocksLos": true,
          "presentation": { "recipe": "unpublished-sculpture", "layer": "Object" } }
      ],
      "terrainTypes": [
        { "id": "fixture-hidden-basalt", "name": "Hidden Basalt", "passable": true, "blocksLos": false,
          "presentation": { "recipe": "StoneFloor" } }
      ],
      "terrainAssemblies": [
        { "id": "fixture-moss-table", "name": "Moss and Table", "surfaceId": "grass",
          "components": [{ "slot": "furniture", "componentId": "table" }] },
        { "id": "fixture-cot-tile", "name": "A Soft Place", "surfaceId": "stone-floor",
          "components": [{ "slot": "furniture", "componentId": "fixture-cot", "quarterTurns": 3 }] },
        { "id": "fixture-hidden-monument", "name": "Hidden Monument", "surfaceId": "fixture-hidden-basalt",
          "components": [{ "slot": "furniture", "componentId": "fixture-hidden-sculpture" }] }
      ],
      "board": { "width": 10, "height": 3, "cells": [
        { "position": { "x": 1, "y": 0 }, "terrainTypeId": "fixture-moss-table" },
        { "position": { "x": 3, "y": 2 }, "terrainTypeId": "fixture-cot-tile" },
        { "position": { "x": 7, "y": 1 }, "terrainTypeId": "fixture-hidden-monument" }
      ] },
      "unitTypeIds": ["assembly-observer"],
      "unitTypes": [{ "id": "assembly-observer", "mov": 2, "rng": 1, "atk": 1, "def": 1, "hp": 1 }],
      "units": [{ "unitTypeId": "assembly-observer", "sideId": "blue", "anchor": { "x": 0, "y": 1 }, "unitId": "observer" }],
      "agency": [{ "unitTypeId": "assembly-observer", "sideId": "blue", "controller": "Human" }],
      "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
      "loot": { "composition": [] }
    }
    """;

    private static ScenarioDefinition Fixture(bool fog = false) => ScenarioDefinitionJson.FromJson(FixtureJson)
        with { FogOfWarSides = fog ? ["blue"] : null };

    [Fact]
    public async Task DesignerDelve1AndPlaytestPreserveReferencesAndResolvedProperties()
    {
        await using var host = await Host.Start();
        var definition = Fixture();
        var authored = ScenarioDefinitionJson.ToJson(definition);
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(authored, await imported.Content.ReadAsStringAsync());
        var edited = ScenarioDefinitionJson.FromJson(authored);
        edited.Board.Cells.Add(new(new(4, 2), TerrainKind.StoneFloor) { TerrainTypeId = AssemblyId });
        using var validated = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(edited));
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(edited));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(edited), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        AssertAssembly(response.Result.State);
        Assert.Equal(AssemblyId, response.Result.State.Physical.Board.TerrainDefinitionAt(new(4, 2)).Id);
        Assert.NotNull(response.Result.State.Physical.Board.TerrainDefinitionAt(new(4, 2)).Assembly);
    }

    [Theory]
    [InlineData("missing-component")]
    [InlineData("missing-surface")]
    [InlineData("duplicate-slot")]
    [InlineData("unknown-property")]
    [InlineData("missing-boolean")]
    [InlineData("invalid-orientation")]
    public async Task InvalidAssemblyJsonCannotValidateOrExport(string failure)
    {
        await using var host = await Host.Start();
        var node = JsonNode.Parse(FixtureJson)!;
        var components = node["terrainAssemblies"]![0]!["components"]!.AsArray();
        switch (failure)
        {
            case "missing-component": components[0]!["componentId"] = "absent"; break;
            case "missing-surface": node["terrainAssemblies"]![0]!["surfaceId"] = "absent"; break;
            case "duplicate-slot": components.Add(components[0]!.DeepClone()); break;
            case "unknown-property": node["fixedComponentTypes"]![0]!["hitPoints"] = 5; break;
            case "missing-boolean": node["fixedComponentTypes"]![0]!.AsObject().Remove("blocksLos"); break;
            case "invalid-orientation": components[0]!["quarterTurns"] = 4; break;
        }
        using var validated = await host.PostDraft("validate", node.ToJsonString());
        if (validated.StatusCode == HttpStatusCode.OK)
            Assert.False((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        else Assert.Equal(HttpStatusCode.BadRequest, validated.StatusCode);
        using var exported = await host.PostDraft("export", node.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, exported.StatusCode);
    }

    [Fact]
    public void DetachedSaveResumeAndRestartKeepAssembliesAndVisualIdentity()
    {
        var definition = Fixture();
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition));
        var imported = game.StartTransportScenario(game.Snapshot().Revision, ScenarioDefinitionTransport.Encode(definition));
        var round = game.StartRound(imported.Revision);
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        var saved = PlaytestGame.Detach(moved);
        using var resumed = new PlaytestGame(new Dice(), saved.Result.State);
        AssertAssembly(resumed.Snapshot().Result.State);
        Assert.Equal(new Cell(2, 1), Assert.Single(resumed.Snapshot().Result.State.Physical.Figures).Position);
        Assert.Equal(JsonSerializer.Serialize(saved.Result.State), JsonSerializer.Serialize(resumed.Snapshot().Result.State));
        var restarted = game.Restart(moved.Revision);
        AssertAssembly(restarted.Result.State);
        Assert.Equal(new Cell(0, 1), Assert.Single(restarted.Result.State.Physical.Figures).Position);
        Assert.Equal(imported.VisualSeed, restarted.VisualSeed);
        Assert.DoesNotContain(TerrainCatalog.All, type => type.Id == AssemblyId);
    }

    [Fact]
    public async Task ObservationAndStreamReplayHideUndiscoveredComponentAndSurfaceDefinitions()
    {
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(Fixture(fog: true)));
        using var live = game.Subscribe(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var baseline = await live.Reader.ReadAsync(timeout.Token);
        var before = JsonSerializer.Serialize(game.BrowserUpdate(baseline), Json);
        Assert.DoesNotContain(SecretId, before);
        Assert.DoesNotContain(SecretComponent, before);
        Assert.DoesNotContain(SecretSurface, before);
        Assert.Contains(AssemblyId, before);
        var round = game.StartRound(baseline.Revision);
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        var discovered = game.BrowserResponse(moved);
        Assert.DoesNotContain(SecretCell, discovered.Presentation.UnknownCells);
        var visible = JsonSerializer.Serialize(discovered, Json);
        Assert.Contains(SecretId, visible);
        Assert.Contains(SecretComponent, visible);
        Assert.Contains(SecretSurface, visible);
        var streamed = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) streamed.Add(game.BrowserUpdate(update));
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(streamed.Select(u => JsonSerializer.Serialize(u, Json)), replayed.Select(u => JsonSerializer.Serialize(u, Json)));
        foreach (var update in streamed)
        {
            var state = update.Snapshot.Result.State;
            var known = state.ExploredCells["blue"].Contains(SecretCell);
            Assert.Equal(known, JsonSerializer.Serialize(state, Json).Contains(SecretComponent));
            foreach (var step in update.Snapshot.Result.ResolutionSteps)
                Assert.Equal(step.StateAfter.ExploredCells["blue"].Contains(SecretCell),
                    JsonSerializer.Serialize(step.StateAfter, Json).Contains(SecretComponent));
        }
    }

    private static void AssertAssembly(GameState state)
    {
        var terrain = state.Physical.Board.TerrainDefinitionAt(new(1, 0));
        Assert.Equal(AssemblyId, terrain.Id);
        Assert.Equal("Moss and Table", terrain.Name);
        Assert.False(terrain.Passable);
        Assert.False(terrain.BlocksLos);
        Assert.NotNull(terrain.Assembly);
        var serialized = JsonSerializer.Serialize(terrain, Json);
        Assert.Contains("grass", serialized);
        Assert.Contains("table", serialized);
        Assert.Contains("quarterTurns\":3", JsonSerializer.Serialize(state.Physical.Board.TerrainDefinitionAt(new(3, 2)), Json));
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
