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

public sealed class ContentBrowserApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task CanonicalCatalogCoversEveryFamilyAndUsesTypedAbilityAndMechanicData()
    {
        await using var host = await Host.Start();
        var catalog = await host.Canonical();
        Assert.Equal("canonical", catalog.Context.Kind);
        Assert.Empty(catalog.Diagnostics);
        Assert.Equal(14, catalog.Categories.Count);
        Assert.All(catalog.Categories, category => Assert.Equal(category.Count, catalog.Entries.Count(entry => entry.Family == category.Id)));
        Assert.Equal(CanonicalUnitTypes.All.Count(), Count("units"));
        Assert.Equal(EquipmentCatalog.All.Count, Count("equipment"));
        Assert.Equal(ItemCatalog.All.Count, Count("items"));
        Assert.Equal(LootCardCatalog.All.Count, Count("lootCards"));
        Assert.Equal(LootPresets.All.Count, Count("lootPresets"));
        Assert.Equal(WorldCardCatalog.All.Count, Count("worldCards"));
        Assert.Equal(1, Count("worldDecks"));
        Assert.Equal(TerrainCatalog.All.Length, Count("terrain"));
        Assert.Equal(FixedComponentCatalog.All.Length, Count("fixedComponents"));
        Assert.Equal(TerrainRoster.Assemblies.Length, Count("terrainAssemblies"));
        Assert.Equal(MechanicCatalog.All.Length, Count("mechanics"));
        Assert.Equal(ResourceTypes.All.Length, Count("resources"));
        Assert.Equal(MachineTypes.All.Length, Count("machines"));
        Assert.Equal(CellMarkerTypes.All.Length, Count("cellMarkers"));
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(entry => entry.Key).Distinct().Count());
        Assert.All(catalog.Entries.SelectMany(entry => entry.References), reference => Assert.True(reference.Exists, reference.TargetKey));

        var cleric = Entry(catalog, "units", UnitTypeIds.Cleric);
        var declared = CanonicalUnitTypes.Find(UnitTypeIds.Cleric)!.LocalAbilities.Single(a => a.Mechanic is HealMechanic);
        var heal = cleric.Abilities.Single(a => a.Key == declared.Key);
        Assert.Equal(declared.Name, heal.Name);
        Assert.Equal(declared.Usage.MaxUses, heal.Usage.MaxUses);
        Assert.Equal(((HealMechanic)declared.Mechanic).Amount, heal.Parameters.GetProperty("amount").GetInt32());
        Assert.Equal(cleric.Key, heal.OwnerKey);
        Assert.Equal("Action", heal.Timing);
        Assert.DoesNotContain(cleric.Sections.SelectMany(section => section.Fields), field => field.Label == "Local Abilities");
        Assert.Equal(new[] { "MOV", "RNG", "ATK", "DEF", "HP" }, cleric.Sections.Single(section => section.Title == "Stats").Fields.Select(field => field.Label));
        var mechanic = Entry(catalog, "mechanics", "holyWave");
        Assert.Equal(JsonSerializer.Serialize(MechanicCatalog.Find("holyWave"), Json), JsonSerializer.Serialize(mechanic.Mechanic, Json));
        Assert.Contains(mechanic.Sections, section => section.Title == "Fixed Engine behavior" && section.Fields.Any(field => field.Label == "Targeting"));
        Assert.Contains("Consume this Item.", Entry(catalog, "items", "second-wind").Summary);
        var equipment = Entry(catalog, "equipment", "goat-bell");
        Assert.DoesNotContain(equipment.Sections.SelectMany(section => section.Fields), field => field.Label is "Id" or "Name");
        Assert.Equal("goat-bell", equipment.Definition.GetProperty("id").GetString());
        var defaultQuantity = WorldCardRoster.Composition.First();
        var worldCard = Entry(catalog, "worldCards", defaultQuantity.TypeId);
        Assert.Equal(defaultQuantity.Count, worldCard.Sections.Single(section => section.Title == "Deck quantities").Fields.Single().Value.GetInt32());
        Assert.Contains(worldCard.References, reference => reference.TargetKey == "worldDecks:default" && reference.Exists);
        return;
        int Count(string family) => catalog.Categories.Single(category => category.Id == family).Count;
    }

    [Fact]
    public async Task AllLootPresetLinksEveryCanonicalCardAndScenarioDeckUsesEffectiveLocalCardsExactlyOnce()
    {
        await using var host = await Host.Start();
        var canonical = await host.Canonical();
        var all = Entry(canonical, "lootPresets", "All");
        var canonicalIds = LootCardCatalog.All.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.True(all.Definition.GetProperty("isDynamic").GetBoolean());
        Assert.Equal(canonicalIds, CardIds(all));
        Assert.Equal(canonicalIds.Select(id => "lootCards:" + id), all.References.Select(reference => reference.TargetKey));
        Assert.All(all.References, reference => Assert.True(reference.Exists));
        Assert.All(all.Definition.GetProperty("composition").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("count").GetInt32()));

        var scenario = Fixture() with
        {
            LootCardDefinitions =
            [
                new(canonicalIds[0], "Scoped replacement card") { Immediate = new HealCollectorMechanic(7) },
                new("browser-orchard", "Orchard card") { Immediate = new HealCollectorMechanic(3) }
            ],
            Loot = new() { Preset = "All" }
        };
        var original = ScenarioDefinitionJson.ToJson(scenario);
        var scoped = await host.Inspect(scenario);
        var local = scoped.Entries.Single(entry => entry.Family == "lootCards" && entry.Name == "Scoped replacement card");
        Assert.NotEqual(canonicalIds[0], local.Id);
        Assert.Equal(canonicalIds[0], local.AuthoredDefinition!.Value.GetProperty("id").GetString());
        var scopedAll = Entry(scoped, "lootPresets", "All");
        var actualDeck = Entry(scoped, "lootPresets", "scenario");
        var expectedIds = canonicalIds.Where(id => id != canonicalIds[0]).Append(local.Id).Append("browser-orchard")
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal("resolved", scopedAll.Origin);
        foreach (var entry in new[] { scopedAll, actualDeck })
        {
            Assert.Equal(expectedIds, CardIds(entry).OrderBy(id => id, StringComparer.Ordinal));
            Assert.All(entry.Definition.GetProperty("composition").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("count").GetInt32()));
            Assert.Equal(expectedIds.Select(id => "lootCards:" + id), entry.References.Select(reference => reference.TargetKey).OrderBy(key => key, StringComparer.Ordinal));
            Assert.All(entry.References, reference => Assert.True(reference.Exists));
            Assert.DoesNotContain(entry.References, reference => reference.TargetKey == "lootCards:" + canonicalIds[0]);
        }
        Assert.Equal("All", actualDeck.AuthoredDefinition!.Value.GetProperty("preset").GetString());
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scenario));
        Assert.Equal(canonicalIds, CardIds(Entry(await host.Canonical(), "lootPresets", "All")));
        return;

        static string?[] CardIds(ContentBrowserEntry entry) => entry.Definition.GetProperty("composition").EnumerateArray()
            .Select(row => row.GetProperty("lootCardId").GetString()).ToArray();
    }

    [Fact]
    public async Task ScenarioCatalogUsesScopedNamesPayloadsLootOverridesAndOwnerLocalKeys()
    {
        await using var host = await Host.Start();
        var unit = CanonicalUnitTypes.Find(UnitTypeIds.Grunt)! with
        {
            DisplayName = "Context healer", Mov = 5,
            LocalAbilities = [new("restore", "Unit remedy", AbilityTiming.Action, new HealMechanic(4)) { Usage = new(2) }]
        };
        var scenario = Fixture(unit) with
        {
            LootUnitOverrides = new() { [unit.Id] = new(DropSuccesses: 5, CanHaveItems: true, CanHaveEquipment: true) },
            EquipmentTypes = [new("context-kit", "Context kit") { LocalAbilities = [new("restore", "Equipment remedy", AbilityTiming.Action, new HealMechanic(3))] }],
            ItemTypes = [new("context-dose", "Context dose") { LocalAbilities = [new("restore", "Item remedy", AbilityTiming.Action, new HealMechanic(1))] }]
        };
        var original = ScenarioDefinitionJson.ToJson(scenario);
        var catalog = await host.Inspect(scenario);
        var projected = Entry(catalog, "units", unit.Id);
        Assert.Equal("scenario-override", projected.Origin);
        Assert.Equal("declared", projected.Availability);
        Assert.Equal("Context healer", projected.Name);
        Assert.Equal(5, projected.Definition.GetProperty("mov").GetInt32());
        Assert.Equal(5, projected.Definition.GetProperty("lootDropSuccesses").GetInt32());
        Assert.True(projected.Definition.GetProperty("canHaveItems").GetBoolean());
        Assert.Equal(5, projected.AuthoredDefinition!.Value.GetProperty("lootOverride").GetProperty("dropSuccesses").GetInt32());
        var abilities = new[] { projected, Entry(catalog, "equipment", "context-kit"), Entry(catalog, "items", "context-dose") }
            .Select(entry => Assert.Single(entry.Abilities)).ToArray();
        Assert.All(abilities, ability => Assert.Equal("restore", ability.Key));
        Assert.Equal(new[] { "Unit remedy", "Equipment remedy", "Item remedy" }, abilities.Select(ability => ability.Name));
        Assert.Equal(new[] { 4, 3, 1 }, abilities.Select(ability => ability.Parameters.GetProperty("amount").GetInt32()));
        Assert.Equal(3, abilities.Select(ability => ability.OwnerKey).Distinct().Count());
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scenario));
        Assert.DoesNotContain(catalog.Entries, entry => entry.Family == "units" && entry.Id == UnitTypeIds.Wizard);
        Assert.NotEqual("Context healer", Entry(await host.Canonical(), "units", unit.Id).Name);
    }

    [Fact]
    public async Task AliasedLootAssemblyAndDecksRetainAuthoredAndResolvedDefinitionsWithoutInstances()
    {
        await using var host = await Host.Start();
        var assembly = new TerrainAssemblyDefinition("context-table", "Context table", "grass")
        { Components = [new("furniture", "table", 1)] };
        var scenario = Fixture() with
        {
            TerrainAssemblies = [assembly],
            LootCardDefinitions = [new("pocket-breakfast", "Borrowed identity") { EquipmentTypeId = "goat-bell" }],
            Loot = new() { Composition = [new(Count: 2) { LootCardId = "pocket-breakfast" }] },
            WorldCardDefinitions = [new("context-rain", "Context rain", WorldCardLifecycle.Immediate) { Immediate = new HealDamagedUnitsMechanic(3) }],
            WorldEffects = new(1, 2) { Composition = [new("context-rain", 2)] }
        };
        var catalog = await host.Inspect(scenario);
        var aliased = catalog.Entries.Single(entry => entry.Family == "lootCards" && entry.Name == "Borrowed identity");
        Assert.NotEqual("pocket-breakfast", aliased.Id);
        Assert.Equal("pocket-breakfast", aliased.AuthoredDefinition!.Value.GetProperty("id").GetString());
        Assert.Equal("scenario-local", aliased.Origin);
        var loot = Entry(catalog, "lootPresets", "scenario");
        Assert.Contains(loot.References, reference => reference.TargetKey == aliased.Key && reference.Exists);
        Assert.Equal(2, loot.Definition.GetProperty("composition")[0].GetProperty("count").GetInt32());
        var composed = Entry(catalog, "terrain", assembly.Id);
        Assert.Equal("scenario-local", composed.Origin);
        Assert.False(composed.Definition.GetProperty("passable").GetBoolean());
        Assert.Equal("grass", composed.AuthoredDefinition!.Value.GetProperty("surfaceId").GetString());
        var assemblyEntry = Entry(catalog, "terrainAssemblies", assembly.Id);
        Assert.Contains(assemblyEntry.References, reference => reference.TargetKey == "fixedComponents:table" && reference.Exists);
        var resolvedFields = assemblyEntry.Sections.Single(section => section.Title == "Resolved Terrain").Fields;
        Assert.Equal(new[] { "Passable", "Blocks LOS", "Allows Resource Markers", "Categories", "Production", "Production Description", "Presentation" },
            resolvedFields.Select(field => field.Label));
        Assert.False(resolvedFields.Single(field => field.Label == "Passable").Value.GetBoolean());
        Assert.Equal("grass", composed.Definition.GetProperty("assembly").GetProperty("surface").GetProperty("id").GetString());
        Assert.Contains(Entry(catalog, "worldDecks", "scenario").References, reference => reference.TargetKey == "worldCards:context-rain" && reference.Exists);
        var rain = Entry(catalog, "worldCards", "context-rain");
        Assert.Equal(2, rain.Sections.Single(section => section.Title == "Deck quantities").Fields.Single(field => field.Label == "Scenario World Deck").Value.GetInt32());
        Assert.Contains(rain.References, reference => reference.TargetKey == "worldDecks:scenario" && reference.Exists);
        var json = JsonSerializer.Serialize(catalog, Json);
        Assert.DoesNotContain("loot-card-1", json);
        Assert.DoesNotContain("world-local:", json);
        Assert.DoesNotContain("drawPile", json);
    }

    [Fact]
    public async Task SummonLinksResolveActualFallbackDefinitionsAndScopedOverrides()
    {
        await using var host = await Host.Start();
        var summoner = new UnitType("context-summoner", 2, 1, 1, 1, 1)
        { LocalAbilities = [new("call", "Call ally", AbilityTiming.Action, new SummonAdjacent(UnitTypeIds.Grunt, Posture.Lying))] };
        var fallback = await host.Inspect(Fixture(summoner));
        Assert.Equal("referenced", Entry(fallback, "units", UnitTypeIds.Grunt).Availability);
        Assert.Contains(Entry(fallback, "units", summoner.Id).References, reference => reference.TargetKey == "units:" + UnitTypeIds.Grunt && reference.Exists);
        Assert.DoesNotContain(fallback.Entries, entry => entry.Family == "units" && entry.Id == UnitTypeIds.Wizard);
        var replacement = CanonicalUnitTypes.Find(UnitTypeIds.Grunt)! with { DisplayName = "Scoped summoned ally", Mov = 5 };
        var source = Fixture(summoner) with
        {
            UnitTypeIds = [summoner.Id, replacement.Id], UnitTypes = [summoner, replacement]
        };
        var overridden = await host.Inspect(source);
        Assert.Equal("Scoped summoned ally", Entry(overridden, "units", replacement.Id).Name);
        Assert.Contains("Scoped summoned ally", Assert.Single(Entry(overridden, "units", summoner.Id).Abilities).Description);
        var workerCaller = new UnitType("worker-caller", 2, 1, 1, 1, 1)
        { LocalAbilities = [new("call-worker", "Call Worker", AbilityTiming.Action, new SummonWorker(2))] };
        var workers = await host.Inspect(Fixture(workerCaller));
        Assert.Contains(Entry(workers, "units", workerCaller.Id).References, reference => reference.TargetKey == "units:" + UnitTypeIds.Worker && reference.Exists);
    }

    [Fact]
    public async Task InlineResourceAndMachineVariantsKeepTheirActualDefinitionsAndReferenceTargets()
    {
        await using var host = await Host.Start();
        var coal = ResourceTypes.Coal with { Name = "Context fuel" };
        var unnamedCoal = new ResourceType(ResourceTypes.Coal.Id, null!);
        var unit = new UnitType("context-carrier", 2, 1, 1, 1, 1)
        { ResourceStorage = new([new(coal, 4)]) };
        var machine = MachineTypes.DiamondPress with
        {
            Name = "Context press", Storage = new([new(coal, 4)]),
            Operation = new([new(coal, 2)], ResourceTypes.Diamond)
        };
        var catalog = await host.Inspect(Fixture(unit) with { Machines = [new("private-press-instance", new(3, 3), machine)] });
        var variant = catalog.Entries.Single(entry => entry.Family == "resources" && entry.Name == coal.Name);
        Assert.NotEqual("resources:coal", variant.Key);
        Assert.Equal("scenario-override", variant.Origin);
        Assert.Contains(Entry(catalog, "units", unit.Id).References, reference => reference.TargetKey == variant.Key && reference.Exists);
        var machineEntry = catalog.Entries.Single(entry => entry.Family == "machines" && entry.Name == machine.Name);
        Assert.Equal(2, machineEntry.Definition.GetProperty("operation").GetProperty("costs")[0].GetProperty("quantity").GetInt32());
        Assert.Contains(machineEntry.References, reference => reference.Label == "Input" && reference.TargetKey == variant.Key && reference.Exists);
        Assert.DoesNotContain("private-press-instance", JsonSerializer.Serialize(catalog, Json));

        // The typed mechanic permits an omitted display name. Scenario JSON has
        // a stricter constructor-presence boundary, so exercise this C# input directly.
        var typed = ContentBrowserCatalog.Scenario(Fixture(unit with
        {
            LocalAbilities = [new("slot", "Slot", AbilityTiming.EndActivation,
                new WaypointSlotMachine(new([unnamedCoal, unnamedCoal, unnamedCoal, unnamedCoal, unnamedCoal, unnamedCoal])))]
        }));
        var unnamed = typed.Entries.Single(entry => entry.Family == "resources" && entry.Definition.GetProperty("name").ValueKind == JsonValueKind.Null);
        Assert.Equal(ResourceTypes.Coal.Id, unnamed.Name);
        Assert.Contains(Entry(typed, "units", unit.Id).References, reference => reference.TargetKey == unnamed.Key && reference.Exists);
    }

    [Fact]
    public async Task InspectionIsDetachedAndInvalidInputReturnsDiagnosticsWithoutFallback()
    {
        await using var host = await Host.Start();
        var before = await host.Client.GetStringAsync("/api/game");
        var canonical = await host.Client.GetStringAsync("/api/content/catalog");
        Assert.DoesNotContain("live-private-type", canonical);
        Assert.DoesNotContain("live-private-instance", canonical);
        var inspected = JsonSerializer.Serialize(await host.Inspect(Fixture()), Json);
        Assert.DoesNotContain("live-private-type", inspected);
        Assert.DoesNotContain("live-private-instance", inspected);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
        var invalid = Fixture() with { WorldEffects = new(1, 1) { Composition = [new("unknown-card", 1)] } };
        using var response = await host.Post(ScenarioDefinitionJson.ToJson(invalid));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var failure = (await response.Content.ReadFromJsonAsync<ContentBrowserFailure>(Json))!;
        Assert.Equal("worldEffects.composition", Assert.Single(failure.Diagnostics).Path);
        Assert.DoesNotContain("entries", await response.Content.ReadAsStringAsync());
        using var malformed = await host.Post("{");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("json", Assert.Single((await malformed.Content.ReadFromJsonAsync<ContentBrowserFailure>(Json))!.Diagnostics).Code);
        var unsupported = ScenarioDefinitionJson.ToJson(Fixture()).Replace("\"unitTypes\":", "\"abilityDefinitions\": [], \"unitTypes\":", StringComparison.Ordinal);
        using var oldShape = await host.Post(unsupported);
        Assert.Equal(HttpStatusCode.BadRequest, oldShape.StatusCode);
        Assert.Contains("abilityDefinitions", Assert.Single((await oldShape.Content.ReadFromJsonAsync<ContentBrowserFailure>(Json))!.Diagnostics).Message);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
    }

    private static ContentBrowserEntry Entry(ContentBrowserResponse catalog, string family, string id) =>
        catalog.Entries.Single(entry => entry.Family == family && entry.Id == id);
    private static ScenarioDefinition Fixture(UnitType? type = null)
    {
        type ??= CanonicalUnitTypes.Find(UnitTypeIds.Grunt)!;
        return Scenario.Define(Scenario.Map(4, 4), [Scenario.Group(type.Id, "inspectors", ControllerKind.Human, Scenario.At(0, 0))]) with
        { UnitTypes = [type], Loot = LootSettings.Empty, Objectives = [new("inspectors", ScenarioObjective.NoObjective)] };
    }
    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var initial = Fixture(new("live-private-type", 1, 1, 1, 1, 1));
            initial = initial with { Units = [initial.Units.Single() with { UnitId = "live-private-instance" }] };
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], random: null,
                initialState: GameEngine.CreateGame(initial));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }
        public async Task<ContentBrowserResponse> Canonical() =>
            (await client.GetFromJsonAsync<ContentBrowserResponse>("/api/content/catalog", Json))!;
        public Task<HttpResponseMessage> Post(string json) => client.PostAsync("/api/content/inspect", new StringContent(json, Encoding.UTF8, "application/json"));
        public async Task<ContentBrowserResponse> Inspect(ScenarioDefinition definition)
        {
            using var response = await Post(ScenarioDefinitionJson.ToJson(definition));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!;
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
