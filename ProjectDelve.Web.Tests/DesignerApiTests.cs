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
    [Fact]
    public async Task CatalogCopiesLoadAsValidDefinitionsWithoutChangingTheRunningGame()
    {
        await using var host = await Host.Start();
        var before = await host.Client.GetStringAsync("/api/game");
        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        foreach (var scenario in catalog.GetProperty("scenarios").EnumerateArray())
        {
            var json = await host.Client.GetStringAsync($"/api/designer/scenarios/{scenario.GetProperty("id").GetString()}");
            _ = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        }
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/designer/scenarios/missing")).StatusCode);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task AllLootMetadataResolvesCanonicalAndScopedCardsWithoutExpandingAuthoringIdentities()
    {
        await using var host = await Host.Start();
        var canonical = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        var canonicalAll = Preset(canonical, "All");
        var canonicalIds = LootCardCatalog.All.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.True(canonicalAll.GetProperty("isDynamic").GetBoolean());
        Assert.Equal(canonicalIds, CardIds(canonicalAll));
        Assert.All(canonicalAll.GetProperty("composition").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("count").GetInt32()));
        Assert.Equal(LootPresets.Default, canonical.GetProperty("defaultLootPreset").GetString());
        foreach (var preset in LootPresets.All.Where(preset => !preset.IsDynamic))
        {
            var projected = Preset(canonical, preset.Id).Deserialize<LootPreset>(Json)!;
            Assert.False(projected.IsDynamic);
            Assert.Equal(preset.Composition, projected.Composition);
        }

        LootCardDefinition[] local =
        [
            new(canonicalIds[0], "Local shadow reward") { Immediate = new HealCollectorMechanic(7) },
            new("metadata-orchard", "Orchard reward") { Immediate = new HealCollectorMechanic(3) }
        ];
        var authored = JsonSerializer.Serialize(local, Json);
        using var response = await host.Client.PostAsJsonAsync("/api/designer/metadata", new
        { unitTypes = Array.Empty<UnitType>(), lootCardDefinitions = local }, Json);
        response.EnsureSuccessStatusCode();
        var scoped = Preset(await response.Content.ReadFromJsonAsync<JsonElement>(), "All");
        var expected = canonicalIds.Append("metadata-orchard").OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, CardIds(scoped));
        Assert.Equal(expected.Length, CardIds(scoped).Distinct(StringComparer.Ordinal).Count());
        Assert.All(scoped.GetProperty("composition").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("count").GetInt32()));
        Assert.Equal(authored, JsonSerializer.Serialize(local, Json));
        var canonicalAgain = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        Assert.Equal(canonicalAll.GetRawText(), Preset(canonicalAgain, "All").GetRawText());
        return;

        static JsonElement Preset(JsonElement response, string id) => response.GetProperty("lootPresets").EnumerateArray()
            .Single(preset => preset.GetProperty("id").GetString() == id);
        static string?[] CardIds(JsonElement preset) => preset.GetProperty("composition").EnumerateArray()
            .Select(row => row.GetProperty("lootCardId").GetString()).ToArray();
    }

    [Fact]
    public async Task FixedAssemblyMetadataAndDesignerTransportRetainReferencesAndEffectiveProperties()
    {
        await using var host = await Host.Start();
        var definition = ScenarioDefinitionJson.FromJson("""
        {
          "fixedComponentTypes": [
            { "id": "fixture-bed", "name": "Folded Bed", "blocksMovement": true, "blocksLos": false,
              "presentation": { "recipe": "unknown-bed-recipe", "layer": "Object" } }
          ],
          "terrainAssemblies": [
            { "id": "fixture-grassy-table", "name": "Grassy Table", "surfaceId": "grass",
              "components": [{ "slot": "furniture", "componentId": "table" }] },
            { "id": "fixture-stone-bed", "name": "Stone Bedroom", "surfaceId": "stone-floor",
              "components": [{ "slot": "furniture", "componentId": "fixture-bed", "quarterTurns": 1 }] }
          ],
          "board": { "width": 4, "height": 4, "cells": [
            { "position": { "x": 2, "y": 1 }, "terrainTypeId": "fixture-grassy-table" },
            { "position": { "x": 2, "y": 2 }, "terrainTypeId": "fixture-stone-bed" }
          ] },
          "unitTypeIds": ["wizard-type"],
          "units": [{ "unitTypeId": "wizard-type", "sideId": "blue", "anchor": { "x": 0, "y": 0 }, "unitId": "observer" }],
          "agency": [{ "unitTypeId": "wizard-type", "sideId": "blue", "controller": "Human" }],
          "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
          "loot": { "composition": [] }
        }
        """);
        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        Assert.Contains(catalog.GetProperty("fixedComponentDefinitions").EnumerateArray(), component =>
            component.GetProperty("id").GetString() == "table" && component.GetProperty("blocksMovement").GetBoolean() &&
            !component.GetProperty("blocksLos").GetBoolean());
        using var metadata = await host.Client.PostAsJsonAsync("/api/designer/metadata", new
        {
            unitTypes = Array.Empty<UnitType>(), terrainAssemblies = definition.TerrainAssemblies,
            fixedComponentTypes = definition.FixedComponentTypes
        }, Json);
        metadata.EnsureSuccessStatusCode();
        var projected = await metadata.Content.ReadFromJsonAsync<JsonElement>();
        var table = projected.GetProperty("terrainDefinitions").EnumerateArray().Single(type => type.GetProperty("id").GetString() == "fixture-grassy-table");
        Assert.False(table.GetProperty("passable").GetBoolean());
        Assert.False(table.GetProperty("blocksLos").GetBoolean());
        Assert.Equal("grass", table.GetProperty("assembly").GetProperty("surface").GetProperty("id").GetString());
        Assert.Equal("table", table.GetProperty("assembly").GetProperty("components")[0].GetProperty("definition").GetProperty("id").GetString());
        Assert.Contains(projected.GetProperty("fixedComponentDefinitions").EnumerateArray(), type => type.GetProperty("id").GetString() == "fixture-bed");
        Assert.DoesNotContain(FixedComponentCatalog.All, type => type.Id == "fixture-bed");

        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var draft = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(draft));
        draft.Board.Cells.Add(new(new(3, 3), TerrainKind.StoneFloor) { TerrainTypeId = "fixture-grassy-table" });
        using var valid = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(draft));
        Assert.True((await valid.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(draft));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(draft), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var state = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!.Result.State;
        var bed = state.Physical.Board.TerrainDefinitionAt(new(2, 2));
        Assert.False(bed.Passable); Assert.False(bed.BlocksLos);
        Assert.Equal("fixture-bed", Assert.Single(bed.Assembly!.Components).Definition.Id);
        Assert.Equal(1, Assert.Single(bed.Assembly.Components).QuarterTurns);
        Assert.Equal("fixture-grassy-table", state.Physical.Board.TerrainDefinitionAt(new(3, 3)).Id);
    }

    [Theory]
    [InlineData("surfaceId", "unknown-surface")]
    [InlineData("componentId", "unknown-component")]
    public async Task FixedAssemblyMetadataRejectsUnresolvedReferencesWithoutAValidBoard(string property, string value)
    {
        await using var host = await Host.Start();
        var request = JsonNode.Parse("""
        { "unitTypes": [], "terrainAssemblies": [
          { "id": "fixture-assembly", "name": "Assembly", "surfaceId": "grass",
            "components": [{ "slot": "furniture", "componentId": "table" }] }
        ] }
        """)!;
        if (property == "surfaceId") request["terrainAssemblies"]![0]![property] = value;
        else request["terrainAssemblies"]![0]!["components"]![0]![property] = value;
        using var response = await host.Client.PostAsync("/api/designer/metadata", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
    }

    [Fact]
    public async Task CanonicalCampfireIsAvailableWithoutLocalDefinitionsAndSurvivesDesignerPlaytest()
    {
        await using var host = await Host.Start();
        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        var campfire = Assert.Single(catalog.GetProperty("terrainDefinitions").Deserialize<TerrainDefinition[]>(Json)!, t => t.Id == "campfire");
        Assert.Equal("Campfire", campfire.Name);
        Assert.False(campfire.Passable);
        Assert.False(campfire.BlocksLos);
        Assert.Null(campfire.Production);
        Assert.Equal("grass", campfire.Assembly!.Surface.Id);
        Assert.Equal("Campfire", Assert.Single(campfire.Assembly.Components).Definition.Presentation.Recipe);

        var definition = CompleteData();
        definition.Board.Cells.Add(new(new(4, 3), TerrainKind.StoneFloor) { TerrainTypeId = campfire.Id });
        var json = ScenarioDefinitionJson.ToJson(definition);
        using var validated = await host.PostDraft("validate", json);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", json);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(json, await imported.Content.ReadAsStringAsync());
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var board = response.Result.State.Physical.Board;
        Assert.Equal(JsonSerializer.Serialize(campfire, Json), JsonSerializer.Serialize(board.TerrainDefinitionAt(new(4, 3)), Json));
    }

    [Fact]
    public async Task CatalogProjectsOnlyCanonicalEditorMetadata()
    {
        await using var host = await Host.Start();
        var catalog = (await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog"));
        Assert.Equal(50, catalog.GetProperty("maxBoardSize").GetInt32());
        var machineType = Assert.Single(catalog.GetProperty("machineTypes").EnumerateArray(), m => m.GetProperty("id").GetString() == "diamond-press");
        Assert.Equal(JsonSerializer.SerializeToElement(MachineTypes.DiamondPress, Json).GetRawText(), machineType.GetRawText());
        Assert.Contains(catalog.GetProperty("machineTypes").EnumerateArray(), m => m.GetProperty("id").GetString() == "slot-machine");
        Assert.Equal(Enum.GetNames<RailDirection>(), catalog.GetProperty("railDirections").Deserialize<string[]>());
        Assert.Contains("Rail", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Asphalt", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Gravel", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        Assert.Contains("Lava", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        var terrain = catalog.GetProperty("terrainDefinitions").Deserialize<TerrainDefinition[]>(Json)!;
        Assert.Equal(TerrainCatalog.All.Select(t => t.Id), terrain.Select(t => t.Id));
        Assert.Contains(terrain, t => t.LegacyKind == TerrainKind.StoneFloorWithTable && !t.Passable && !t.BlocksLos);
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
            Assert.Equal(new[] { "unitTypeId", "displayName", "maxHp", "footprintOffsets", "cellSpan", "resourceStorage", "definition" }, entry.EnumerateObject().Select(p => p.Name));
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(type, Json), entry.GetProperty("definition")));
            Assert.Equal(JsonSerializer.SerializeToElement(type.ResourceStorage, Json).GetRawText(), entry.GetProperty("resourceStorage").GetRawText());
        }
        var dragon = projected.Single(type => type.GetProperty("unitTypeId").GetString() == UnitTypeIds.RedDragon);
        Assert.Equal(2, dragon.GetProperty("cellSpan").GetInt32());
        Assert.Equal(4, dragon.GetProperty("footprintOffsets").GetArrayLength());
    }

    [Fact]
    public async Task EffectiveMetadataUsesScenarioOverridesWithoutRequiringAValidBoard()
    {
        await using var host = await Host.Start();
        var custom = UnitRoster.RedDragon() with { Id = "custom-large", Hp = 12, DisplayName = "Scenario giant" };
        var overridden = UnitRoster.Grunt() with { Hp = 9, Footprint = Footprint.TwoByTwo,
            ResourceStorage = new([new(ResourceTypes.Shell, 4)]) };
        using var response = await host.Client.PostAsJsonAsync("/api/designer/metadata", new { unitTypes = new[] { custom, overridden } }, Json);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var type in new[] { custom, overridden })
        {
            var entry = result.GetProperty("unitTypes").EnumerateArray().Single(t => t.GetProperty("unitTypeId").GetString() == type.Id);
            Assert.Equal(UnitContent.Find(type.Id, [custom, overridden])!.Hp, entry.GetProperty("maxHp").GetInt32());
            Assert.Equal(4, entry.GetProperty("footprintOffsets").GetArrayLength());
            Assert.Equal(2, entry.GetProperty("cellSpan").GetInt32());
        }
        using var duplicate = await host.Client.PostAsJsonAsync("/api/designer/metadata", new { unitTypes = new[] { custom, custom } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task TerrainMetadataUsesTheCatalogResolverWithoutRequiringAValidBoard()
    {
        await using var host = await Host.Start();
        var local = new TerrainDefinition("unfamiliar-satin", "Satin Obstruction", false, false)
            { Presentation = new("StoneFloorWithTable") };
        using var response = await host.Client.PostAsJsonAsync("/api/designer/metadata",
            new { unitTypes = Array.Empty<UnitType>(), terrainTypes = new[] { local } }, Json);
        response.EnsureSuccessStatusCode();
        var metadata = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = Assert.Single(metadata.GetProperty("terrainDefinitions").EnumerateArray(),
            t => t.GetProperty("id").GetString() == local.Id);
        Assert.Equal(local.Name, result.GetProperty("name").GetString());
        Assert.False(result.GetProperty("passable").GetBoolean());
        using var duplicate = await host.Client.PostAsJsonAsync("/api/designer/metadata",
            new { unitTypes = Array.Empty<UnitType>(), terrainTypes = new[] { local, local } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task ProductionMetadataAndDesignerTransportUseTheTypedEngineDefinition()
    {
        await using var host = await Host.Start();
        var local = new TerrainDefinition("unfamiliar-cupboard", "Unexpected Cupboard", false, false)
        {
            Production = new DirectionalResourceProduction(ResourceTypes.Banana.Id, 3,
                SourceLos: ProductionSourceLos.RespectTerrain)
        };
        using var metadataResponse = await host.Client.PostAsJsonAsync("/api/designer/metadata",
            new { unitTypes = Array.Empty<UnitType>(), terrainTypes = new[] { local } }, Json);
        metadataResponse.EnsureSuccessStatusCode();
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(local.ProductionDescription, metadata.GetProperty("productionDescriptions").GetProperty(local.Id).GetString());
        var projected = metadata.GetProperty("terrainDefinitions").EnumerateArray()
            .Single(type => type.GetProperty("id").GetString() == local.Id).Deserialize<TerrainDefinition>(Json)!;
        Assert.Equal(local.Production, projected.Production);

        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        var lavaMappings = catalog.GetProperty("legacyTerrainMappings").EnumerateArray()
            .Where(mapping => mapping.GetProperty("kind").GetString() == "Lava").ToArray();
        Assert.Contains(lavaMappings, mapping => !mapping.TryGetProperty("coalGeneration", out _) &&
            mapping.GetProperty("terrainTypeId").GetString() == TerrainCompatibility.CoalLavaId);
        Assert.Contains(lavaMappings, mapping => mapping.TryGetProperty("coalGeneration", out var flag) && !flag.GetBoolean() &&
            mapping.GetProperty("terrainTypeId").GetString() == TerrainCompatibility.OrdinaryLavaId);

        var definition = CompleteData() with { TerrainTypes = [local] };
        definition.Board.Cells.Add(new(new(4, 3), TerrainKind.StoneFloor) { TerrainTypeId = local.Id });
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var json = await imported.Content.ReadAsStringAsync();
        using var exported = await host.PostDraft("export", json);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        var decoded = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(decoded));
        Assert.Equal(local.Production, GameEngine.CreateGame(decoded).Physical.Board.TerrainDefinitionAt(new(4, 3)).Production);

        using var invalid = await host.Client.PostAsJsonAsync("/api/designer/metadata",
            new { unitTypes = Array.Empty<UnitType>(), terrainTypes = new[] { local with { Production = new DirectionalResourceProduction("missing-resource", 3) } } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    public static IEnumerable<object[]> ObjectiveCases()
    {
        foreach (var objective in Enum.GetValues<ScenarioObjective>())
        foreach (var count in new[] { 0, 1, 2 })
        foreach (var selector in new[] { "Unit", "SideAtStart", "Group", "Union" })
            yield return [objective, count, selector];
    }

    [Theory]
    [MemberData(nameof(ObjectiveCases))]
    public async Task ObjectiveAndLogisticsAuthoringRoundTripsAndPlays(ScenarioObjective kind, int count, string selection)
    {
        await using var host = await Host.Start();
        var selectors = selection switch
        {
            "Unit" => new[] { UnitSelector.ForUnit("target") },
            "SideAtStart" => [UnitSelector.SideAtStart("red")],
            "Group" => [UnitSelector.Group(UnitTypeIds.Grunt, "red")],
            _ => [UnitSelector.ForUnit("target"), UnitSelector.SideAtStart("red"), UnitSelector.Group(UnitTypeIds.Grunt, "red")]
        };
        var objective = new SideObjective("blue", kind) { Count = count, Selectors = [.. selectors],
            Area = kind == ScenarioObjective.ReachArea ? new(2, 2, 2, 2) : null };
        var definition = Scenario.Define(Scenario.Map(5, 5),
            [Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(0, 0)),
             Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, Scenario.At(4, 4)),
             Scenario.Group(UnitTypeIds.Geomancer, "blue", ControllerKind.Human)]) with
        {
            Objectives = [objective, new("red", ScenarioObjective.NoObjective)],
            Resources = [new(ResourceTypes.Shell, new(1, 1)), new(ResourceTypes.Diamond, new(1, 2))],
            StoredResources = [new("blue", UnitTypeIds.Geomancer, ResourceTypes.Diamond, 1)]
        };
        definition.Units[1] = definition.Units[1] with { UnitId = "target" };
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var loaded = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(loaded));
        // A focused edit changes authored geometry/count while retaining selectors and logistics.
        loaded.Objectives![0] = objective with { Count = count + 1, Area = kind == ScenarioObjective.ReachArea ? new(1, 2, 3, 2) : null };
        using var valid = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(loaded));
        Assert.True((await valid.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(loaded));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(loaded), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var state = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!.Result.State;
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(GameEngine.CreateGame(loaded), state);
    }

    [Theory]
    [InlineData("railway-fixture")]
    [InlineData("designer-phase2")]
    [InlineData("serialization")]
    [InlineData("shaman")]
    [InlineData("shaper")]
    [InlineData("complete-data")]
    [InlineData("world-effects")]
    [InlineData("local-world-cards")]
    [InlineData("lava")]
    [InlineData("lava-no-coal")]
    [InlineData("gravel")]
    [InlineData("asphalt")]
    [InlineData("machines")]
    [InlineData("route")]
    [InlineData("chess-phase2")]
    public async Task DesignerRoundTripPreservesDefinitionsAndUsesExistingGameImport(string id)
    {
        await using var host = await Host.Start();
        var definition = id == "designer-phase2" ? ScenarioDefinitionJson.FromJson(await File.ReadAllTextAsync(
            Path.Combine(host.ContentRoot, "../ProjectDelve.Web.Tests/fixtures/designer-phase2.json"))) : id == "chess-phase2" ? Scenario.Define(Scenario.Map(6, 6),
            [Scenario.Group(UnitTypeIds.RoyalBishop, "red", ControllerKind.Human, Scenario.At(0, 0)),
             Scenario.Group(UnitTypeIds.GoblinBishop, "blue", ControllerKind.Automated, Scenario.At(5, 5)),
             Scenario.Group(UnitTypeIds.RoyalKnight, "red", ControllerKind.Human, Scenario.At(1, 0)),
             Scenario.Group(UnitTypeIds.HobbyKnight, "blue", ControllerKind.Automated, Scenario.At(4, 5))]) :
            id == "railway-fixture" ? RailwayProjectionTests.Fixture() : id is "complete-data" or "world-effects" or "local-world-cards" or "lava" or "lava-no-coal" or "gravel" or "asphalt" or "machines" or "route" ? CompleteData() : WebScenarioFixtures.Named(id);
        if (id == "route") definition = definition with { Orders = [new(definition.Agency[0].UnitTypeId,
            definition.Agency[0].SideId, OrderKind.FollowRoute,
            Route: new("test-route", 1, -1))],
            Waypoints = [new("first", new(0, 0, 2, 2)), new("second", new(3, 3, 2, 2))],
            Routes = [new("test-route", ["first", "second"], RouteMode.PingPong)] };
        if (id == "asphalt") definition = definition with { Board = definition.Board with { DefaultTerrain = TerrainKind.Asphalt, Cells = [new(new(4, 4), TerrainKind.Asphalt)] } };
        if (id == "gravel") definition = definition with { Board = definition.Board with { DefaultTerrain = TerrainKind.Gravel, Cells = [new(new(4, 4), TerrainKind.Gravel)] } };
        if (id == "machines") definition = definition with { Machines = [new("press", new(3, 0), MachineTypes.DiamondPress)
            { StoredResources = [new(ResourceTypes.Coal, 2)] }] };
        if (id == "world-effects") definition = definition with { WorldEffects = new(3, 2) };
        if (id == "local-world-cards")
        {
            var local = ScenarioDefinitionJson.FromJson("""
            { "board": { "width": 2, "height": 2 }, "unitTypeIds": [], "units": [], "agency": [],
              "worldCardDefinitions": [
                { "id": "unknown-wind", "name": "An Unfamiliar Wind", "lifecycle": "Continuous", "modifiers": [{ "target": "Mov", "amount": 2 }] },
                { "id": "unknown-rain", "name": "An Unfamiliar Rain", "lifecycle": "Immediate", "immediate": { "kind": "healDamagedUnits", "amount": 2 } }
              ],
              "worldEffects": { "cardsPerRound": 2, "cycling": 2,
                "composition": [{ "typeId": "unknown-wind", "count": 2 }, { "typeId": "unknown-rain", "count": 1 }] }
            }
            """);
            definition = definition with { WorldCardDefinitions = local.WorldCardDefinitions, WorldEffects = local.WorldEffects };
        }
        if (id == "lava") definition = definition with { Board = definition.Board with { Cells = [new(new(4, 4), TerrainKind.Lava)] } };
        if (id == "lava-no-coal") definition = definition with { Board = definition.Board with { Cells = [new(new(4, 4), TerrainKind.Lava) { CoalGeneration = false }] } };
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
        // Browser snapshots expose resolved references; the raw scenario transport
        // equivalence assertions above continue protecting legacy authored meaning.
        publicSetup.Physical = publicSetup.Physical with { Board = publicSetup.Physical.Board with
            { Terrain = BrowserProjection.TerrainForPresentation(publicSetup.Physical.Board) } };
        if (publicSetup.WorldDeck is not null)
        {
            using var publicJson = JsonDocument.Parse(await played.Content.ReadAsStringAsync());
            Assert.Equal(publicSetup.WorldDeck.DrawPile.Count, publicJson.RootElement.GetProperty("presentation").GetProperty("worldEffects").GetProperty("drawPileCount").GetInt32());
            publicSetup.WorldDeck.DrawPile = [];
            // The imported/exported definition above retains composition; player state hides deck contents.
            publicSetup.WorldEffects = publicSetup.WorldEffects! with { Composition = null };
        }
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(publicSetup, game.Result.State);
        using var restartImported = await host.Client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = game.Revision });
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(publicSetup,
            (await restartImported.Content.ReadFromJsonAsync<GameResponse>(Json))!.Result.State);
    }

    [Fact]
    public async Task LocalEquipmentSurvivesDesignerValidationExportImportAndPlaytest()
    {
        await using var host = await Host.Start();
        var equipment = new EquipmentDefinition("copper-cloud", "Cloud Compass")
        {
            LocalAbilities = [
                new("wind", "Tailwind", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2), new(Stat.Def, 1)])),
                new("phase", "Phase", AbilityTiming.Passive, new Phase()),
                new("door", "Open Door", AbilityTiming.FreeAction, new OpenDoor()),
                new("wake", "Awaken", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())]
        };
        var definition = WebScenarioFixtures.Combat();
        definition = definition with
        {
            EquipmentTypes = [equipment],
            Units = definition.Units.Select(u => u.UnitId == "hero" ? u with { Inventory = [new("starting-compass", equipment.Id)] } : u).ToList(),
            GroundObjects = [new(new("ground-compass", equipment.Id), new(0, 0))],
            Loot = new() { Composition = [new(equipment.Id, 2)] }
        };
        using var validation = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(definition));
        Assert.True((await validation.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(definition));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal("starting-compass", Assert.Single(response.Result.State.Units.Single(u => u.Id == "hero").Inventory).Id);
        Assert.Equal(equipment.Name, Assert.Single(response.Presentation.Cards["hero"].Inventory).Name);
        Assert.Equal(equipment.Name, Assert.Single(response.Presentation.GroundObjects).Object.Content.Name);
        Assert.Contains(response.Presentation.Cards["hero"].Entries, e => e.Content.Name == "Awaken" && e.SourceTypeId == equipment.Id);
    }

    [Theory]
    [InlineData("fold-sky")]
    [InlineData("holy-wave")]
    public async Task LocalAbilitiesItemsAndLootSurviveDesignerRoundTripAndRestart(string abilityKey)
    {
        await using var host = await Host.Start();
        var ability = new LocalAbilityDefinition(abilityKey, "Fold the Sky", AbilityTiming.Action, new HolyWaveMechanic());
        var item = new ItemDefinition("paper-moon", "Paper Moon") { LocalAbilities = [ability] };
        var loot = new LootCardDefinition("moon-reward", "A Folded Reward") { ItemTypeId = item.Id };
        var definition = WebScenarioFixtures.Combat();
        definition = definition with
        {
            ItemTypes = [item], LootCardDefinitions = [loot],
            Units = definition.Units.Select(u => u.UnitId == "hero" ? u with
            {
                Inventory = [new("moon-one") { ItemTypeId = item.Id }, new("moon-two") { ItemTypeId = item.Id }]
            } : u).ToList(),
            GroundObjects = [new(new("ground-moon") { ItemTypeId = item.Id }, new(0, 0))],
            Loot = new() { Composition = [new() { LootCardId = loot.Id, Count = 2 }] }
        };
        using var validation = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(definition));
        Assert.True((await validation.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", ScenarioDefinitionJson.ToJson(definition));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), await imported.Content.ReadAsStringAsync());
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal(new[] { "moon-one", "moon-two" }, response.Result.State.Units.Single(u => u.Id == "hero").Inventory.Select(o => o.Id));
        var granted = Assert.Single(response.Presentation.Cards["hero"].Entries.Where(e => e.Source == "Item"));
        Assert.Equal(ability.Name, granted.Content.Name);
        Assert.Equal(item.Name, granted.SourceName);
        Assert.Equal(2, granted.CopyCount);
        Assert.Equal("Action", granted.Content.Category);
        Assert.Equal(item.Name, Assert.Single(response.Presentation.GroundObjects).Object.Content.Name);
        using var restarted = await host.Client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = response.Revision });
        restarted.EnsureSuccessStatusCode();
        var fresh = (await restarted.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var restoredAbility = Assert.Single(fresh.Result.State.ItemTypes.Single(i => i.Id == item.Id).LocalAbilities);
        Assert.Equal(ability, restoredAbility);
        Assert.Equal("Holy Wave", UnitType.Cleric().LocalAbilities.Single(a => a.Mechanic is HolyWaveMechanic).Name);
        Assert.Equal(new[] { "moon-one", "moon-two" }, fresh.Result.State.Units.Single(u => u.Id == "hero").Inventory.Select(o => o.Id));
        Assert.Equal(item.Name, Assert.Single(fresh.Presentation.GroundObjects).Object.Content.Name);
    }

    [Fact]
    public async Task OwnedAbilitiesRemainInlineAcrossMetadataDesignerExportPlaytestAndRestart()
    {
        await using var host = await Host.Start();
        var definition = OwnedAbilityWebFixture.Definition();
        var authoredJson = ScenarioDefinitionJson.ToJson(definition);
        Assert.DoesNotContain("abilityDefinitions", authoredJson);
        using var metadata = await host.Client.PostAsJsonAsync("/api/designer/metadata", new
        {
            unitTypes = definition.UnitTypes,
            units = definition.Units
        }, Json);
        metadata.EnsureSuccessStatusCode();
        var metadataJson = await metadata.Content.ReadFromJsonAsync<JsonElement>();
        var keeper = metadataJson.GetProperty("unitTypes").EnumerateArray()
            .Single(type => type.GetProperty("unitTypeId").GetString() == "woven-keeper");
        Assert.Equal(new[] { "pulse", "shade" }, keeper.GetProperty("definition").GetProperty("localAbilities")
            .EnumerateArray().Select(ability => ability.GetProperty("key").GetString()));

        using var validation = await host.PostDraft("validate", authoredJson);
        Assert.True((await validation.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", authoredJson);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(authoredJson, await imported.Content.ReadAsStringAsync());
        var restored = ScenarioDefinitionTransport.Decode(transport);
        Assert.DoesNotContain("abilityDefinitions", ScenarioDefinitionJson.ToJson(restored));
        Assert.Equal("Silver Stitch", restored.UnitTypes!.Single(type => type.Id == "woven-keeper").LocalAbilities[0].Name);
        Assert.Equal("Lantern Hush", restored.EquipmentTypes!.Single(type => type.Id == "hollow-lantern").LocalAbilities[0].Name);
        Assert.Equal("Paper Stitch", restored.ItemTypes!.Single(type => type.Id == "folded-remedy").LocalAbilities[0].Name);

        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var entries = response.Presentation.Cards["actor"].Entries;
        Assert.Equal(new AbilityUses(2, 2), Assert.Single(entries, entry => entry.Content.Name == "Silver Stitch").Uses);
        Assert.Equal(2, Assert.Single(entries, entry => entry.Content.Name == "Paper Stitch").CopyCount);
        Assert.Equal(2, Assert.Single(entries, entry => entry.Content.Name == "Borrow a Moment").CopyCount);
        Assert.DoesNotContain("abilityDefinitions", JsonSerializer.Serialize(response.Result.State, Json));
        using var restarted = await host.Client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = response.Revision });
        restarted.EnsureSuccessStatusCode();
        var fresh = (await restarted.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal(new[] { "pulse", "shade" }, fresh.Result.State.Types.Single(type => type.Id == "woven-keeper")
            .LocalAbilities.Select(ability => ability.Key));
        Assert.Equal(5, fresh.Result.State.Units.Single(unit => unit.Id == "actor").Inventory.Length);
        Assert.Equal(2, Assert.Single(fresh.Presentation.Cards["actor"].Entries, entry => entry.Content.Name == "Paper Stitch").CopyCount);
    }

    [Fact]
    public async Task ImmediateLootDefinitionsAndDeckReferencesSurviveDesignerRoundTrip()
    {
        await using var host = await Host.Start();
        var card = new LootCardDefinition("restorative-letter", "A Restorative Letter")
        { Immediate = new HealCollectorMechanic(3) };
        var definition = WebScenarioFixtures.Combat() with
        {
            LootCardDefinitions = [card],
            Loot = new() { Composition = [new() { LootCardId = card.Id, Count = 2 }] }
        };
        var json = ScenarioDefinitionJson.ToJson(definition);
        using var validation = await host.PostDraft("validate", json);
        Assert.True((await validation.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", json);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        Assert.Equal(json, await imported.Content.ReadAsStringAsync());
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var restored = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(card, Assert.Single(restored.LootCardDefinitions!));
        Assert.All(GameEngine.CreateGame(restored).LootDeck!.DrawPile, physical => Assert.Equal(card.Id, physical.LootCardId));
    }

    [Theory]
    [InlineData("unitTypes", "unitTypes[0].localAbilities[0].mechanic.amount")]
    [InlineData("equipmentTypes", "equipmentTypes[1].localAbilities[0].mechanic.amount")]
    [InlineData("itemTypes", "itemTypes[1].localAbilities[0].mechanic.amount")]
    public async Task OwnedAbilityErrorsUseAuthoredOwnerIndexesInsteadOfRuntimeCatalogIndexes(string owner, string path)
    {
        await using var host = await Host.Start();
        var definition = OwnedAbilityWebFixture.Definition();
        if (owner == "unitTypes")
        {
            // Runtime Unit order follows UnitTypeIds, not the local definitions list.
            definition = definition with { UnitTypeIds = [.. definition.UnitTypeIds.Skip(1), definition.UnitTypeIds[0]] };
            var type = definition.UnitTypes![0];
            definition.UnitTypes[0] = type with
            { LocalAbilities = type.LocalAbilities.SetItem(0, type.LocalAbilities[0] with { Mechanic = new HealMechanic(0) }) };
        }
        else if (owner == "equipmentTypes")
        {
            var type = definition.EquipmentTypes![1];
            definition.EquipmentTypes[1] = type with
            { LocalAbilities = type.LocalAbilities.SetItem(0, type.LocalAbilities[0] with { Mechanic = new HealMechanic(0) }) };
        }
        else
        {
            var type = definition.ItemTypes![1];
            definition.ItemTypes[1] = type with
            { LocalAbilities = type.LocalAbilities.SetItem(0, type.LocalAbilities[0] with { Mechanic = new ExtraActionsMechanic(0) }) };
        }
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        Assert.Equal(path, ScenarioValidationContext.PathOf(error));
        Assert.StartsWith(path + ":", error.Message);
        foreach (var operation in new[] { "validate", "export" })
        {
            using var response = await host.PostDraft(operation, ScenarioDefinitionJson.ToJson(definition));
            Assert.Equal(operation == "validate" ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            var validation = (await response.Content.ReadFromJsonAsync<DesignerValidation>())!;
            Assert.False(validation.Valid);
            var diagnostic = Assert.Single(validation.Errors);
            Assert.Equal(path, diagnostic.Path);
            Assert.Equal(error.Message, diagnostic.Message);
        }
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
    [InlineData("coal-on-grass", "board.cells[0]")]
    public async Task InvalidDraftUsesAuthoritativeFailureAndNeverMutatesGame(string invalid, string path)
    {
        await using var host = await Host.Start();
        var definition = CompleteData();
        definition = invalid switch
        {
            "coal-on-grass" => definition with { Board = definition.Board with { Cells = [new(new(4, 4), TerrainKind.Grass) { CoalGeneration = false }] } },
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

    [Fact]
    public async Task FogOfWarSurvivesDesignerJsonAndDelveImportValidationAndExport()
    {
        await using var host = await Host.Start();
        var definition = ProjectDelve.Testing.FogOfWarFixture.SleepingDungeon();
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(definition) });
        imported.EnsureSuccessStatusCode();
        var json = await imported.Content.ReadAsStringAsync();
        Assert.Equal("explorers", Assert.Single(ScenarioDefinitionJson.FromJson(json).FogOfWarSides!));
        using var validated = await host.PostDraft("validate", json);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", json);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        var restored = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(restored));
        Assert.DoesNotContain("exploredCells", json);
        using var invalid = await host.PostDraft("validate", ScenarioDefinitionJson.ToJson(definition with { FogOfWarSides = ["missing"] }));
        var validation = (await invalid.Content.ReadFromJsonAsync<DesignerValidation>())!;
        Assert.Contains(validation.Errors, error => error.Path == "fogOfWarSides");
    }

    private static ScenarioDefinition CompleteData() => Scenario.Define(
        Scenario.Map(5, 5, defaultTerrain: TerrainKind.Grass,
            cells: [Scenario.Tile(4, 4, TerrainKind.Grass), Scenario.Tile(3, 3, TerrainKind.StoneFloor)],
            edges: [Scenario.Edge(1, 1, EdgeDirection.Right, EdgeKind.None), Scenario.Edge(2, 1, EdgeDirection.Down, EdgeKind.OpenDoor)]),
        [Scenario.Group(UnitTypeIds.Grunt, "side / amber â€” blÃ…", ControllerKind.Human, Scenario.At(0, 0)),
         Scenario.Group(UnitTypeIds.Wizard, "side / amber â€” blÃ…", ControllerKind.Automated, Scenario.At(2, 2, Posture.Lying, 2)),
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
