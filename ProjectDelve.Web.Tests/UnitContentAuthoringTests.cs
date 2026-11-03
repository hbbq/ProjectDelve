using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class UnitContentAuthoringTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-unit-authoring-" + Guid.NewGuid().ToString("N"));
    private static UnitType Unit(string id = "expansion-scout") => new(id, 3, 1, 2, 1, 3)
    {
        DisplayName = "Expansion Scout", Unique = true, CanHaveItems = true, CanHaveEquipment = true,
        LootDropSuccesses = 2, Behaviors = UnitBehavior.MaximizeAttackDistance | UnitBehavior.ApproachThroughClosedDoors,
        MovementPattern = MovementPattern.StraightOrthogonal, TerrainRestriction = new(["racing-ground"]),
        ResourceStorage = new([new(new("scout-fuel", "Scout Fuel"), 2)]),
        LocalAbilities = [new("door", "Open Door", AbilityTiming.FreeAction, new OpenDoor()),
            new("mend", "Field Medicine", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(2) }],
        AttackDefinitionOrder = [UnitAction.NormalAttack]
    };
    private static ContentEditorRequest Edit(UnitType type, string? existing = null, ContentEditorContext? context = null) =>
        new("units", JsonSerializer.SerializeToElement(type, Json), existing, context);
    private static ContentPackage Package(params UnitType[] types) => new()
        { Id = "scouts", Dependencies = [ContentPackageCatalog.CSharpPackageId], Definitions = new() { Units = [.. types] } };
    private static ScenarioDefinition Scene(string typeId) => new()
    {
        Board = new() { Width = 6, Height = 5, DefaultTerrainTypeId = "asphalt" },
        UnitTypeIds = [typeId], Units = [new(typeId, "blue", new(1, 1), UnitId: "scout")],
        Agency = [new(typeId, "blue", ControllerKind.Human)], Loot = LootSettings.Empty,
        Objectives = [new("blue", ScenarioObjective.NoObjective)]
    };

    [Theory]
    [InlineData("mov")] [InlineData("rng")] [InlineData("atk")] [InlineData("def")]
    [InlineData("hp")] [InlineData("unique")] [InlineData("footprint")] [InlineData("behaviors")]
    [InlineData("movementPattern")] [InlineData("terrainRestrictedMovement.terrain")]
    [InlineData("terrainRestriction.categories")] [InlineData("resourceStorage")]
    [InlineData("lootDropSuccesses")] [InlineData("attackDefinitionOrder")]
    public void InvalidUnitPropertiesHaveDefinitionPaths(string field)
    {
        var type = field switch
        {
            "mov" => Unit() with { Mov = -1 }, "rng" => Unit() with { Rng = -1 },
            "atk" => Unit() with { Atk = -1 }, "def" => Unit() with { Def = -1 },
            "hp" => Unit() with { Hp = 0 }, "unique" => Unit() with { Unique = false },
            "footprint" => Unit() with { Footprint = (Footprint)999 },
            "behaviors" => Unit() with { Behaviors = (UnitBehavior)32 },
            "movementPattern" => Unit() with { MovementPattern = (MovementPattern)999 },
            "terrainRestrictedMovement.terrain" => Unit() with { TerrainRestriction = null, TerrainRestrictedMovement = new(TerrainKind.Tree) },
            "terrainRestriction.categories" => Unit() with { TerrainRestriction = new([]) },
            "resourceStorage" => Unit() with { ResourceStorage = new([new(new("fuel", "Fuel"), -1)]) },
            "lootDropSuccesses" => Unit() with { LootDropSuccesses = 7 },
            _ => Unit() with { AttackDefinitionOrder = [(UnitAction)999] }
        };
        Assert.Contains(ContentDefinitions.Validate(new(type)), d => d.Path == field);
        var request = field is "footprint" or "behaviors" or "movementPattern" or "attackDefinitionOrder"
            ? InvalidEnumRequest(field) : Edit(type);
        var result = ContentEditorApi.Validate(request);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Path == "definition." + field + (field == "attackDefinitionOrder" ? "[0]" : ""));
    }

    private static ContentEditorRequest InvalidEnumRequest(string field)
    {
        var node = JsonSerializer.SerializeToNode(Unit(), Json)!;
        node[field] = field == "attackDefinitionOrder" ? new JsonArray("unsupported") : JsonValue.Create("unsupported");
        return new("units", JsonSerializer.SerializeToElement(node));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MalformedAbilityCollectionsReturnDiagnosticsRatherThanThrowing(bool nullEntry)
    {
        var node = JsonSerializer.SerializeToNode(Unit(), Json)!;
        node["localAbilities"] = nullEntry ? new JsonArray((JsonNode?)null) : null;
        var result = ContentEditorApi.Validate(new("units", JsonSerializer.SerializeToElement(node)));
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Path.StartsWith("definition.localAbilities", StringComparison.Ordinal));
    }

    [Fact]
    public void ScenarioPreviewPreservesCompleteTypedDeclarationAndEmbeddedResourceReferences()
    {
        var result = ContentEditorApi.Validate(Edit(Unit()));
        Assert.True(result.Valid, string.Join(";", result.Diagnostics));
        var restored = result.Definition!.Value.Deserialize<UnitType>(Json)!;
        Assert.Equal(JsonSerializer.Serialize(Unit(), Json), JsonSerializer.Serialize(restored, Json));
        Assert.Equal("scenario-local", result.Entry!.Origin);
        Assert.Equal(new[] { "door", "mend" }, result.Entry.Abilities.Select(a => a.Key));
        Assert.Equal(2, result.Entry.Abilities[1].Usage.MaxUses);
        Assert.Contains(result.Entry.Abilities, a => a.Description.Contains("2"));
        Assert.Contains(result.Entry.References, r => r.Id == "scout-fuel" && r.Exists);
        Assert.DoesNotContain(CanonicalUnitTypes.All, t => t.Id == restored.Id);
        var invalid = Unit() with { LocalAbilities = [Unit().LocalAbilities[0], Unit().LocalAbilities[0]] };
        Assert.Contains(ContentEditorApi.Validate(Edit(invalid)).Diagnostics, d => d.Path == "definition.localAbilities[1].key");
        invalid = Unit() with { LocalAbilities = [new("heal", "Heal", AbilityTiming.Passive, new HealMechanic(2))] };
        Assert.Contains(ContentEditorApi.Validate(Edit(invalid)).Diagnostics, d => d.Path == "definition.localAbilities[0].timing");
        invalid = Unit() with { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(0) }] };
        Assert.False(ContentEditorApi.Validate(Edit(invalid)).Valid);
    }

    [Fact]
    public void SelfMutualSummonsAndExistingScenarioOverridesUseEffectiveDefinitions()
    {
        UnitType Summon(string id, string target) => new(id, 1, 1, 1, 1, 1)
            { DisplayName = id, LocalAbilities = [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(target, Posture.Lying))] };
        Assert.True(ContentEditorApi.Validate(Edit(Summon("self", "self"))).Valid);
        var a = Summon("a", "b"); var b = Summon("b", "a");
        Assert.True(ContentEditorApi.Validate(Edit(a, context: new() { UnitTypes = [b] })).Valid);
        var missing = ContentEditorApi.Validate(Edit(a));
        Assert.Contains(missing.Diagnostics, d => d.Path == "definition.localAbilities[0].mechanic.unitTypeId");
        var source = UnitRoster.Grunt();
        Assert.False(ContentEditorApi.Validate(Edit(source)).Valid);
        var overridden = source with { Mov = source.Mov + 1 };
        var edited = ContentEditorApi.Validate(Edit(overridden, source.Id, new() { UnitTypes = [source] }));
        Assert.True(edited.Valid); Assert.Equal("scenario-override", edited.Entry!.Origin);
        Assert.Equal(source.Mov, UnitRoster.Grunt().Mov);
        Assert.False(ContentEditorApi.Validate(Edit(a with { Id = "renamed" }, "a", new() { UnitTypes = [a, b] })).Valid);
    }

    [Fact]
    public void LibraryPreviewHistoryCopiesAndRenameReorderKeepIdentityAndRevisionSemantics()
    {
        var store = new ContentLibrary(directory);
        var preview = ContentLibraryAuthoring.Validate(store, new(new(Package(), 0), Edit(Unit())));
        Assert.True(preview.Valid); Assert.Empty(store.Catalog().Packages);
        Assert.Equal("library-local", preview.Entry!.Origin);
        var first = store.Create(Package(Unit()));
        var type = Unit() with { DisplayName = "Revised Scout", LocalAbilities = [Unit().LocalAbilities[1] with { Name = "Renamed medicine" }, Unit().LocalAbilities[0]] };
        Assert.True(ContentLibraryAuthoring.Validate(store, new(new(first.Package, 1), Edit(type, type.Id))).Valid);
        var second = store.Save("scouts", new(Package(type), 1));
        Assert.Equal(2, second.Revision);
        Assert.Equal("Expansion Scout", store.Read("scouts", 1).Package.Definitions.Units[0].DisplayName);
        Assert.Equal(new[] { "mend", "door" }, second.Package.Definitions.Units[0].LocalAbilities.Select(a => a.Key));
        Assert.Throws<LibraryException>(() => store.Save("scouts", new(first.Package, 1)));
        var copy = UnitRoster.Grunt() with { Id = "independent-grunt", DisplayName = "Independent" };
        Assert.True(ContentLibraryAuthoring.Validate(store, new(new(second.Package, 2), Edit(copy))).Valid);
        Assert.False(ContentLibraryAuthoring.Validate(store, new(new(second.Package, 2), Edit(UnitRoster.Grunt()))).Valid);
        Assert.Equal(2, new ContentLibrary(directory).Read("scouts").Revision);
    }

    [Fact]
    public void UnitPinsUpdateAndPromoteIncludingOwnedResourcesWithoutChangingExistingSnapshots()
    {
        var canonicalDirectory = Path.Combine(directory, "canonical"); Directory.CreateDirectory(canonicalDirectory);
        var canonical = new CanonicalContent(canonicalDirectory);
        var store = new ContentLibrary(Path.Combine(directory, "library"), canonical.Packages, canonicalProvider: () => canonical.Packages);
        var unit = Unit(); var first = store.Create(Package(unit));
        var references = new ScenarioReferenceService(store, canonical);
        var selected = ImmutableArray.Create(new ContentIdentity(ContentFamily.Unit, unit.Id));
        var preview = references.Preview(new(Scene(unit.Id), "scouts", 1, selected));
        var scene = Scene(unit.Id) with { ContentReferences = preview.References.ToList() };
        var original = ScenarioDefinitionJson.ToJson(scene);
        Assert.Single(GameEngine.CreateGame(scene, canonical.Bundle).Units);
        Assert.Equal("library-pinned", preview.Catalog.Entries.Single(e => e.Id == unit.Id).Origin);
        Assert.False(ContentEditorApi.Validate(Edit(unit, unit.Id, new() { ContentReferences = scene.ContentReferences }), canonical.Bundle).Valid);
        store.Save("scouts", new(Package(unit with { DisplayName = "Updated Scout" }), 1));
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scene));
        var updated = references.Preview(new(scene, "scouts", 2, selected, scene.ContentReferences![0].SnapshotDigest));
        Assert.Contains(updated.Changes, c => c.Identity.TypeId == unit.Id && c.Change == "Changed");
        var newScene = scene with { ContentReferences = updated.References.ToList() };
        var transport = ScenarioDefinitionTransport.Encode(newScene);
        Assert.Equal(ScenarioDefinitionJson.ToJson(newScene), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport, null, canonical.Bundle)));
        var promotion = new ContentPromotionService(store, canonical);
        var proposal = promotion.Preview(new(new("library", "scouts", 2), "published-scouts", selected));
        Assert.Equal("Scout Fuel", proposal.Package.Definitions.Units[0].ResourceStorage!.Capacities[0].Type.Name);
        Assert.Empty(proposal.Package.Definitions.Resources); // Owned payloads never become global definitions.
        promotion.Publish(new(proposal, true));
        Assert.Equal("Updated Scout", new CanonicalContent(canonicalDirectory).Bundle.Units.Single(u => u.Id == unit.Id).DisplayName);
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scene));
        Assert.Single(GameEngine.CreateGame(newScene, canonical.Bundle).Types, t => t.Id == unit.Id);
        Assert.Equal("Updated Scout", GameEngine.CreateGame(Scene(unit.Id), canonical.Bundle).Types.Single(t => t.Id == unit.Id).DisplayName);
        Assert.False(ContentLibraryAuthoring.Validate(store, new(new(store.Read("scouts").Package, 2), Edit(unit, unit.Id))).Valid);
    }

    [Fact]
    public void PromotionKeepsSameIdOwnedResourceVariantsDistinctFromNamedCanonicalResources()
    {
        var canonicalDirectory = Path.Combine(directory, "canonical"); Directory.CreateDirectory(canonicalDirectory);
        var canonical = new CanonicalContent(canonicalDirectory);
        var store = new ContentLibrary(Path.Combine(directory, "library"), canonical.Packages);
        var type = Unit() with { ResourceStorage = new([new(ResourceTypes.Coal with { Name = "Scout's own Coal label" }, 4)]) };
        store.Create(Package(type));
        var promotion = new ContentPromotionService(store, canonical);
        var proposal = promotion.Preview(new(new("library", "scouts", 1), "owned-coal-scout", [new(ContentFamily.Unit, type.Id)]));
        promotion.Publish(new(proposal, true));
        Assert.Equal("Scout's own Coal label", canonical.Bundle.Units.Single(t => t.Id == type.Id).ResourceStorage!.Capacities[0].Type.Name);
        Assert.Equal(ResourceTypes.Coal.Name, canonical.Bundle.Resources.Single(r => r.Id == ResourceTypes.Coal.Id).Name);
        Assert.Empty(proposal.Package.Definitions.Resources);
    }

    [Fact]
    public void FootprintChangesAreCheckedAgainstExistingScenarioPlacement()
    {
        var type = new UnitType("large", 1, 1, 1, 1, 1) { DisplayName = "Large" };
        var scene = Scene(type.Id) with { UnitTypes = [type], Units = [new(type.Id, "blue", new(5, 4))] };
        _ = GameEngine.CreateGame(scene);
        var changed = type with { Footprint = Footprint.TwoByTwo };
        Assert.True(ContentEditorApi.Validate(Edit(changed, type.Id, new() { UnitTypes = [type] })).Valid);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { UnitTypes = [changed] }));
    }

    [Fact]
    public async Task AuthoredCustomSummonsReachPlaytestAndReplayWhileHiddenTypesStayOutOfObservations()
    {
        var caller = new UnitType("authored-caller", 2, 1, 1, 1, 1) { DisplayName = "Caller",
            LocalAbilities = [new("call", "Call Scout", AbilityTiming.Action, new SummonAdjacent("authored-child", Posture.Lying)) { Usage = new(2) }] };
        var child = new UnitType("authored-child", 1, 1, 1, 1, 1) { DisplayName = "Child" };
        var context = new ContentEditorContext { UnitTypes = [child] };
        Assert.True(ContentEditorApi.Validate(Edit(caller, context: context)).Valid);
        var scene = Scene(caller.Id) with { UnitTypeIds = [caller.Id, child.Id], UnitTypes = [caller, child] };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root,
            "--Library:Path", Path.Combine(directory, "scenarios"), "--ContentLibrary:Path", Path.Combine(directory, "library")],
            new Dice(), GameEngine.CreateGame(scene));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var validated = await client.PostAsJsonAsync("/api/content/validate", Edit(caller, context: context), Json);
        Assert.True((await validated.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Valid);
        var catalog = await client.GetFromJsonAsync<ContentBrowserResponse>("/api/content/catalog", Json);
        Assert.Contains(catalog!.UnitAuthoring.Behaviors, b => b.Value == "Flee");
        Assert.Equal(0, catalog.UnitAuthoring.Stats.Single(s => s.Name == "mov").Minimum);
        using var imported = await client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(scene) });
        Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync());
        var metadataResponse = await client.PostAsJsonAsync("/api/designer/metadata", new DesignerMetadataRequest([caller, child]), Json);
        metadataResponse.EnsureSuccessStatusCode();
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(metadata.GetProperty("unitTypes").EnumerateArray(), u => u.GetProperty("unitTypeId").GetString() == child.Id);
        var started = GameEngine.StartRound(app.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State, new Dice(), false);
        var moved = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var summon = moved.NextInput!.Candidates.First(c => c.Ability?.Key == "call");
        var saved = JsonSerializer.Serialize(moved.State);
        var expected = GameEngine.Advance(moved.State, new Choice(summon.Key), new Dice(), false);
        var resumed = GameEngine.Advance(JsonSerializer.Deserialize<GameState>(saved)!, new Choice(summon.Key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(resumed));
        Assert.Contains(resumed.State.Units, u => u.TypeId == child.Id);
        var hidden = new UnitType("secret-authored-type", 1, 1, 1, 1, 1) { DisplayName = "Secret authored name" };
        var fog = scene with { Board = new() { Width = 20, Height = 20 }, UnitTypeIds = [caller.Id, child.Id, hidden.Id],
            UnitTypes = [caller, child, hidden], Units = [new(caller.Id, "blue", new(0, 0)), new(hidden.Id, "red", new(19, 19))],
            Agency = [new(caller.Id, "blue", ControllerKind.Human), new(hidden.Id, "red", ControllerKind.Automated)], FogOfWarSides = ["blue"] };
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(fog));
        var observation = JsonSerializer.Serialize(game.BrowserResponse(game.Snapshot()));
        Assert.DoesNotContain(hidden.Id, observation); Assert.DoesNotContain(hidden.DisplayName, observation);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
