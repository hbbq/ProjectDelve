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

public sealed class ContentEditorApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task EquipmentAuthoringUsesTypedModifiersCapabilitiesActionsAndStableIdentityWithoutADestination()
    {
        await using var host = await Host.Start();
        var definition = Equipment();
        var result = await host.Validate("equipment", definition);
        Assert.True(result.Valid);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(definition.Id, result.Definition!.Value.GetProperty("id").GetString());
        var restored = result.Definition.Value.Deserialize<EquipmentDefinition>(Json)!;
        Assert.Equal(definition.Name, restored.Name);
        Assert.Equal(4, restored.LocalAbilities.Length);
        Assert.Equal(new[] { new ModifierThisTurn(Stat.Mov, 2), new ModifierThisTurn(Stat.Def, -1) },
            Assert.IsType<SelfModifiersMechanic>(restored.LocalAbilities[0].Mechanic).Modifiers);
        Assert.IsType<Phase>(restored.LocalAbilities[1].Mechanic);
        Assert.IsType<OpenDoor>(restored.LocalAbilities[2].Mechanic);
        Assert.Equal(3, Assert.IsType<HealMechanic>(restored.LocalAbilities[3].Mechanic).Amount);
        Assert.All(result.Entry!.References, reference => Assert.True(reference.Exists));
        Assert.Contains(result.Entry.Abilities, ability => ability.Key == "mend" && ability.Name == "Mend" && ability.Timing == "Action" && ability.Description.Contains('3'));
        Assert.Equal("local", result.Entry.Origin);

        var otherDestination = new ContentEditorContext { EquipmentTypes = [Equipment("other-kit", "Other kit")] };
        var elsewhere = await host.Validate("equipment", definition, context: otherDestination);
        Assert.True(elsewhere.Valid);
        Assert.Equal(result.Definition.Value.GetRawText(), elsewhere.Definition!.Value.GetRawText());
        Assert.Equal(result.Entry.Key, elsewhere.Entry!.Key);
        Assert.DoesNotContain("board", result.Definition.Value.GetRawText());
    }

    [Fact]
    public async Task LootRewardsResolveCanonicalAndLocalObjectsAndImmediateAmounts()
    {
        await using var host = await Host.Start();
        var equipment = Equipment();
        var context = new ContentEditorContext { EquipmentTypes = [equipment] };
        var reward = await host.Validate("lootCards", new LootCardDefinition("gift", "A useful gift") { EquipmentTypeId = equipment.Id }, context: context);
        Assert.True(reward.Valid);
        Assert.Contains(reward.Entry!.References, reference => reference.TargetKey == "equipment:" + equipment.Id && reference.Exists);
        var item = await host.Validate("lootCards", new LootCardDefinition("breather", "Take a breath") { ItemTypeId = "second-wind" });
        Assert.True(item.Valid);
        Assert.Contains(item.Entry!.References, reference => reference.TargetKey == "items:second-wind" && reference.Exists);
        var immediate = await host.Validate("lootCards", new LootCardDefinition("lunch", "Packed lunch") { Immediate = new HealCollectorMechanic(4) });
        Assert.True(immediate.Valid);
        Assert.Equal(4, immediate.Definition!.Value.GetProperty("immediate").GetProperty("amount").GetInt32());
        Assert.Equal("Restore 4 HP, without exceeding maximum HP.", immediate.Entry!.Summary);
        var absent = await host.Validate("lootCards", new LootCardDefinition("missing", "Missing") { EquipmentTypeId = "absent" });
        AssertError(absent, "definition.equipmentTypeId");
        var mixed = await host.Validate("lootCards", new LootCardDefinition("mixed", "Mixed") { EquipmentTypeId = equipment.Id, Immediate = new HealCollectorMechanic(1) }, context: context);
        AssertError(mixed, "definition");
        var invalidAmount = await host.Validate("lootCards", new LootCardDefinition("invalid", "Invalid") { Immediate = new HealCollectorMechanic(0) });
        AssertError(invalidAmount, "definition.immediate.amount");
    }

    [Fact]
    public async Task CopiesRequireNewIdsWhileEditsReplaceOnlyAnExistingLocalRowAndPreserveItsId()
    {
        await using var host = await Host.Start();
        var canonical = EquipmentCatalog.All.Single(type => type.Id == "goat-bell");
        AssertError(await host.Validate("equipment", canonical), "definition.id");
        var copied = await host.Validate("equipment", canonical with { Id = "my-bell", Name = "My Bell" });
        Assert.True(copied.Valid);
        Assert.Equal(canonical.LocalAbilities[0].Key, Assert.Single(copied.Entry!.Abilities).Key);
        var breakfast = LootCardCatalog.All.Single(card => card.Id == "pocket-breakfast");
        AssertError(await host.Validate("lootCards", breakfast), "definition.id");
        Assert.True((await host.Validate("lootCards", breakfast with { Id = "my-breakfast" })).Valid);

        var original = Equipment();
        var context = new ContentEditorContext { EquipmentTypes = [original] };
        var snapshot = JsonSerializer.Serialize(context, Json);
        var changed = original with { Name = "Renamed kit", LocalAbilities = [new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 5)]))] };
        var edited = await host.Validate("equipment", changed, original.Id, context);
        Assert.True(edited.Valid);
        Assert.Equal("Renamed kit", edited.Entry!.Name);
        Assert.Equal(5, edited.Entry.Abilities.Single().Parameters.GetProperty("modifiers")[0].GetProperty("amount").GetInt32());
        Assert.Equal(snapshot, JsonSerializer.Serialize(context, Json));
        AssertError(await host.Validate("equipment", changed, "absent", context), "existingId");
        AssertError(await host.Validate("equipment", changed with { Id = "renamed-id" }, original.Id, context), "definition.id");
        AssertError(await host.Validate("equipment", changed, original.Id), "existingId");
        AssertError(await host.Validate("equipment", original, context: context), "definition.id");
    }

    [Fact]
    public async Task SharedEngineValidationReportsOwnerKeysParametersSourceProfilesAndDependencyPaths()
    {
        await using var host = await Host.Start();
        var duplicate = Equipment() with { LocalAbilities = [new("same", "First", AbilityTiming.Action, new HealMechanic(1)), new("same", "Second", AbilityTiming.Action, new HealMechanic(2))] };
        AssertError(await host.Validate("equipment", duplicate), "definition.localAbilities[1].key");
        var charges = Equipment() with { LocalAbilities = [new("mend", "Mend", AbilityTiming.Action, new HealMechanic(1)) { Usage = new(2) }] };
        Assert.True((await host.Validate("equipment", charges)).Valid);
        var invalidCharges = charges with { LocalAbilities = [charges.LocalAbilities[0] with { Usage = new(0) }] };
        AssertError(await host.Validate("equipment", invalidCharges), "definition.localAbilities[0].usage");
        var wrongTiming = Equipment() with { LocalAbilities = [new("phase", "Phase", AbilityTiming.Action, new Phase())] };
        AssertError(await host.Validate("equipment", wrongTiming), "definition.localAbilities[0].timing");
        var wrongAmount = Equipment() with { LocalAbilities = [new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 101)]))] };
        AssertError(await host.Validate("equipment", wrongAmount), "definition.localAbilities[0].mechanic.modifiers[0].amount");
        var invalidItem = new ItemDefinition("passive-item", "Passive Item") { LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] };
        AssertError(await host.Validate("equipment", Equipment(), context: new() { ItemTypes = [invalidItem] }), "context.itemTypes[0].localAbilities[0].timing");
        var unresolved = new LootCardDefinition("unresolved", "Unresolved") { ItemTypeId = "absent" };
        AssertError(await host.Validate("equipment", Equipment(), context: new() { LootCardDefinitions = [unresolved] }), "context.lootCardDefinitions[0].itemTypeId");
        AssertError(await host.Validate("equipment", Equipment(), context: new() { EquipmentTypes = [Equipment("duplicate"), Equipment("duplicate")] }), "context.equipmentTypes[1].id");
    }

    [Fact]
    public async Task DependencyCatalogWorksWithoutScenarioFieldsAndNeitherEndpointTouchesTheRunningGame()
    {
        await using var host = await Host.Start();
        var before = await host.Client.GetStringAsync("/api/game");
        var equipment = Equipment();
        var context = new ContentEditorContext
        {
            EquipmentTypes = [equipment],
            LootCardDefinitions = [new("gift", "Gift") { EquipmentTypeId = equipment.Id }]
        };
        using var response = await host.Client.PostAsJsonAsync("/api/content/context", context, Json);
        response.EnsureSuccessStatusCode();
        var catalog = (await response.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!;
        Assert.Equal("definitions", catalog.Context.Kind);
        Assert.Equal(new[] { "units", "equipment", "items", "lootCards", "mechanics", "resources" }, catalog.Categories.Select(category => category.Id));
        Assert.Contains(catalog.Entries, entry => entry.Id == equipment.Id && entry.Origin == "local");
        Assert.All(catalog.Entries.SelectMany(entry => entry.References), reference => Assert.True(reference.Exists));
        Assert.Contains(catalog.Entries, entry => entry.Family == "units" && entry.Id == UnitTypeIds.Grunt);
        Assert.True((await host.Validate("equipment", Equipment("other"))).Valid);
        AssertError(await host.Validate("lootCards", new LootCardDefinition("bad", "Bad") { EquipmentTypeId = "missing" }), "definition.equipmentTypeId");
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));

        using var missing = await host.Client.PostAsJsonAsync("/api/content/context", new ContentEditorContext
        { LootCardDefinitions = [new("bad", "Bad") { EquipmentTypeId = "missing" }] }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("lootCardDefinitions[0].equipmentTypeId", Assert.Single((await missing.Content.ReadFromJsonAsync<ContentBrowserFailure>(Json))!.Diagnostics).Path);
    }

    [Fact]
    public async Task SummoningEquipmentUsesEffectiveUnitReferencesWithoutPlacementOrGlobalMutation()
    {
        await using var host = await Host.Start();
        var local = new UnitType("paper-helper", 1, 1, 1, 0, 1) { DisplayName = "Paper Helper" };
        var overridden = CanonicalUnitTypes.Find(UnitTypeIds.Grunt)! with { DisplayName = "Scarlet Helper" };
        var equipment = new EquipmentDefinition("helper-bell", "Helper Bell") { LocalAbilities =
        [
            new("paper", "Fold", AbilityTiming.Action, new SummonAdjacent(local.Id, Posture.Lying)) { Usage = new(2) },
            new("scarlet", "Ring", AbilityTiming.Action, new SummonAdjacent(overridden.Id, Posture.Upright))
        ] };
        var context = new ContentEditorContext { UnitTypes = [local, overridden] };
        var result = await host.Validate("equipment", equipment, context: context);
        Assert.True(result.Valid);
        Assert.Contains(result.Entry!.Abilities, ability => ability.Key == "paper" && ability.Description.Contains("Lying Paper Helper"));
        Assert.Contains(result.Entry.Abilities, ability => ability.Key == "scarlet" && ability.Description.Contains("Upright Scarlet Helper"));
        Assert.All(result.Entry.References, reference => Assert.True(reference.Exists));
        Assert.Contains(result.Entry.References, reference => reference.TargetKey == "units:" + local.Id);

        using var response = await host.Client.PostAsJsonAsync("/api/content/context", context, Json);
        response.EnsureSuccessStatusCode();
        var catalog = (await response.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!;
        Assert.Equal("Paper Helper", Assert.Single(catalog.Entries, entry => entry.Family == "units" && entry.Id == local.Id).Name);
        var scoped = Assert.Single(catalog.Entries, entry => entry.Family == "units" && entry.Id == overridden.Id);
        Assert.Equal("Scarlet Helper", scoped.Name);
        Assert.Equal("scenario-override", scoped.Origin);
        Assert.NotEqual("Scarlet Helper", CanonicalUnitTypes.Find(overridden.Id)!.DisplayName);

        AssertError(await host.Validate("equipment", equipment), "definition.localAbilities[0].mechanic.unitTypeId");
        var missing = equipment with { LocalAbilities = [equipment.LocalAbilities[0] with { Mechanic = new SummonAdjacent("missing", Posture.Upright) }] };
        AssertError(await host.Validate("equipment", missing, context: context), "definition.localAbilities[0].mechanic.unitTypeId");
        AssertError(await host.Validate("equipment", equipment, context: new() { UnitTypes = [local, local] }), "context.unitTypes[1].id");
    }

    [Fact]
    public async Task HistoricalLootCollisionRemainsInspectibleWithoutBlockingUnrelatedAuthoringOrRenamingTheSource()
    {
        await using var host = await Host.Start();
        var historical = new LootCardDefinition("pocket-breakfast", "Historical equipment reward") { EquipmentTypeId = "goat-bell" };
        var context = new ContentEditorContext { LootCardDefinitions = [historical] };
        var before = JsonSerializer.Serialize(context, Json);
        using var response = await host.Client.PostAsJsonAsync("/api/content/context", context, Json);
        response.EnsureSuccessStatusCode();
        var catalog = (await response.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!;
        var pair = catalog.Entries.Where(entry => entry.Family == "lootCards" && entry.Id == historical.Id).ToArray();
        Assert.Equal(2, pair.Length);
        Assert.Equal(2, pair.Select(entry => entry.Key).Distinct().Count());
        var local = pair.Single(entry => entry.Name == historical.Name);
        Assert.Equal("local", local.Origin);
        Assert.Equal("legacy-collision", local.Availability);
        Assert.Equal(historical.Id, local.Definition.GetProperty("id").GetString());
        Assert.True((await host.Validate("equipment", Equipment(), context: context)).Valid);
        Assert.True((await host.Validate("lootCards", historical with { Id = "copied-historical-reward" }, context: context)).Valid);
        AssertError(await host.Validate("lootCards", historical, historical.Id, context), "definition.id");
        Assert.Equal(before, JsonSerializer.Serialize(context, Json));

        var scenario = Scenario.Define(Scenario.Map(2, 2),
            [Scenario.Group(UnitTypeIds.Grunt, "inspectors", ControllerKind.Human, Scenario.At(0, 0))]) with
        { LootCardDefinitions = [historical], Loot = LootSettings.Empty };
        using var inspection = await host.Client.PostAsync("/api/content/inspect", new StringContent(ScenarioDefinitionJson.ToJson(scenario), Encoding.UTF8, "application/json"));
        inspection.EnsureSuccessStatusCode();
        var resolved = (await inspection.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!.Entries.Single(entry => entry.Name == historical.Name);
        Assert.NotEqual(historical.Id, resolved.Id);
        Assert.Equal(historical.Id, resolved.AuthoredDefinition!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task TypedJsonBoundaryRejectsUnknownMechanicsLegacyFieldsAndMalformedRequests()
    {
        await using var host = await Host.Start();
        var request = JsonSerializer.SerializeToNode(new { family = "equipment", definition = Equipment() }, Json)!;
        request["definition"]!["localAbilities"]![0]!["mechanic"]!["kind"] = "imaginary";
        using var unknown = await host.Post(request.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.False((await unknown.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Valid);
        request = JsonSerializer.SerializeToNode(new { family = "equipment", definition = Equipment() }, Json)!;
        request["definition"]!["modifiers"] = new JsonArray();
        using var legacy = await host.Post(request.ToJsonString());
        AssertError((await legacy.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!, "definition.modifiers");
        using var malformed = await host.Post("{");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("json", Assert.Single((await malformed.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Diagnostics).Code);
        using var wholeScenario = await host.Client.PostAsync("/api/content/context", new StringContent("{\"board\":{\"width\":1,\"height\":1}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, wholeScenario.StatusCode);
    }

    [Theory]
    [InlineData("equipment", "equipmentTypes[0].name")]
    [InlineData("item", "itemTypes[0].name")]
    [InlineData("loot", "lootCardDefinitions[0].immediate.amount")]
    [InlineData("legacy-loot", "lootCardDefinitions[0].immediate.amount")]
    public async Task ScenarioValidationRebasesCatalogConstructionFailuresToAuthoredRows(string family, string expectedPath)
    {
        await using var host = await Host.Start();
        var scenario = Scenario.Define(Scenario.Map(2, 2),
            [Scenario.Group(UnitTypeIds.Grunt, "inspectors", ControllerKind.Human, Scenario.At(0, 0))]) with { Loot = LootSettings.Empty };
        scenario = family switch
        {
            "equipment" => scenario with { EquipmentTypes = [Equipment() with { Name = "" }] },
            "item" => scenario with { ItemTypes = [new("local-item", "") { LocalAbilities = [new("use", "Use", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] }] },
            _ => scenario with { LootCardDefinitions = [new(family == "legacy-loot" ? "pocket-breakfast" : "local-lunch", "Lunch") { Immediate = new HealCollectorMechanic(0) }] }
        };
        using var response = await host.Client.PostAsync("/api/designer/validate", new StringContent(ScenarioDefinitionJson.ToJson(scenario), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var validation = (await response.Content.ReadFromJsonAsync<DesignerValidation>(Json))!;
        Assert.False(validation.Valid);
        var error = Assert.Single(validation.Errors);
        Assert.Equal(expectedPath, error.Path);
        if (family is "loot" or "legacy-loot") Assert.Contains(expectedPath, error.Message);
    }

    private static EquipmentDefinition Equipment(string id = "mist-kit", string name = "Mist kit") => new(id, name)
    {
        LocalAbilities =
        [
            new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2), new(Stat.Def, -1)])),
            new("phase", "Phase", AbilityTiming.Passive, new Phase()),
            new("door", "Open Door", AbilityTiming.FreeAction, new OpenDoor()),
            new("mend", "Mend", AbilityTiming.Action, new HealMechanic(3))
        ]
    };
    private static void AssertError(ContentEditorResponse response, string path)
    {
        Assert.False(response.Valid);
        Assert.Contains(response.Diagnostics, diagnostic => diagnostic.Path == path);
        Assert.Null(response.Definition);
        Assert.Null(response.Entry);
    }
    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], random: null,
                initialState: GameEngine.CreateGame(WebScenarioFixtures.Combat()));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }
        public Task<HttpResponseMessage> Post(string json) => client.PostAsync("/api/content/validate", new StringContent(json, Encoding.UTF8, "application/json"));
        public async Task<ContentEditorResponse> Validate<T>(string family, T definition, string? existingId = null, ContentEditorContext? context = null)
        {
            using var response = await client.PostAsJsonAsync("/api/content/validate", new { family, definition, existingId, context }, Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!;
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
