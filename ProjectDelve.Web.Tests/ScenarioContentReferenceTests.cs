using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioContentReferenceTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-pins-" + Guid.NewGuid().ToString("N"));
    private static string WebRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
    // Tests own their provider. Mutable authored publications may use the same
    // identities as these deliberate collision fixtures.
    private CanonicalContent Canonical => new(Path.Combine(directory, "canonical"));
    private ContentLibrary Store => new(directory, Canonical.Packages);
    private ScenarioReferenceService Service => new(Store, Canonical);
    private static ContentPackage Package(string name = "Knightly Boots", int uses = 2) => new()
    {
        Id = "knightly-kit", Definitions = new() {
            Equipment = [new("knightly-boots", name) { LocalAbilities =
                [new("encourage", "Encourage", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright)) { Usage = new(uses) }] },
                new("unselected-gear", "Not imported")],
            LootCards = [new("knightly-gift", "Knightly Gift") { EquipmentTypeId = "knightly-boots" }]
        }
    };
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 4, Height = 3 }, UnitTypeIds = ["bearer", "friend"],
        UnitTypes = [new("bearer", 1, 0, 0, 0, 1, Actions: UnitAction.None) { CanHaveEquipment = true }, new("friend", 0, 0, 0, 0, 1)],
        Units = [new("bearer", "blue", new(1, 1), UnitId: "bearer"), new("friend", "blue", new(2, 1), Posture.Lying, UnitId: "friend")],
        Agency = [new("bearer", "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };
    private ScenarioReferencePreview Pin(ScenarioDefinition? scenario = null, ContentFamily family = ContentFamily.LootCard, string id = "knightly-gift", long revision = 1)
        => Service.Preview(new(scenario ?? Scenario(), "knightly-kit", revision, [new(family, id)]));
    private static ScenarioDefinition Apply(ScenarioReferencePreview preview, ScenarioDefinition? scenario = null) => (scenario ?? Scenario()) with { ContentReferences = preview.References.ToList() };
    private static ScenarioDefinition WithLoot(ScenarioDefinition scenario) => scenario with
    { Loot = new() { Composition = [new() { LootCardId = "knightly-gift" }] }, LootMarkers = [new("gift", new(1, 1))] };

    [Fact]
    public void LootImportsOnlyItsRequiredEquipmentAndPreservesStableIdsAndAbilityKeys()
    {
        Store.Create(Package());
        var preview = Pin(); var pin = preview.Reference;
        Assert.Equal(new(ContentFamily.LootCard, "knightly-gift"), Assert.Single(pin.Selected));
        Assert.Equal(2, pin.Closure.Length); Assert.Single(pin.Snapshot.Equipment); Assert.Single(pin.Snapshot.LootCards);
        Assert.DoesNotContain(pin.Snapshot.Equipment, e => e.Id == "unselected-gear");
        Assert.Equal("encourage", pin.Snapshot.Equipment[0].LocalAbilities[0].Key);
        Assert.Empty(Scenario().ContentReferences ?? []);
        Assert.All(preview.Catalog.Entries.Where(e => e.Id is "knightly-boots" or "knightly-gift"), e => Assert.Equal("library-pinned", e.Origin));
        var equipment = Pin(family: ContentFamily.Equipment, id: "knightly-boots");
        Assert.Empty(equipment.Reference.Snapshot.LootCards);
        var both = Pin(Apply(equipment));
        Assert.Single(both.References); Assert.Equal(2, both.Reference.Selected.Length);
        _ = GameEngine.CreateGame(WithLoot(Apply(both)), Canonical.Bundle);
    }

    [Fact]
    public void LibraryEditsAndArchiveNeverChangeScenarioAndUpdateRequiresExplicitApply()
    {
        Store.Create(Package());
        var scenario = WithLoot(Apply(Pin())); var before = ScenarioDefinitionJson.ToJson(scenario);
        Store.Save("knightly-kit", new(Package("Revised Knightly Boots", 4), 1));
        Assert.Equal("Knightly Boots", GameEngine.CreateGame(scenario, Canonical.Bundle).Equipment("knightly-boots").Name);
        var update = Service.Preview(new(scenario, "knightly-kit", 2, [], scenario.ContentReferences![0].SnapshotDigest));
        Assert.Contains(update.Changes, change => change.Identity == new ContentIdentity(ContentFamily.Equipment, "knightly-boots") && change.Change == "Changed");
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(scenario));
        var revised = Apply(update, scenario);
        Assert.Equal(4, GameEngine.CreateGame(revised, Canonical.Bundle).Equipment("knightly-boots").LocalAbilities[0].Usage.MaxUses);
        Assert.Equal(2, GameEngine.CreateGame(scenario, Canonical.Bundle).Equipment("knightly-boots").LocalAbilities[0].Usage.MaxUses);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => Pin(scenario, revision: 2)).StatusCode);
        Store.Archive("knightly-kit", 2);
        Assert.Equal("Knightly Boots", GameEngine.CreateGame(scenario, Canonical.Bundle).Equipment("knightly-boots").Name);
        Assert.Equal("Revised Knightly Boots", GameEngine.CreateGame(revised, Canonical.Bundle).Equipment("knightly-boots").Name);
    }

    [Fact]
    public void EmbeddedReferencesSurviveDelveScenarioStorageAndUnavailableLibrary()
    {
        Store.Create(Package()); var original = WithLoot(Apply(Pin()));
        var transport = ScenarioDefinitionTransport.Encode(original);
        var scenarios = new ScenarioLibrary(Path.Combine(directory, "scenarios"), content: Canonical.Bundle);
        var saved = scenarios.SaveScenario(null, new("Pinned adventure", null, JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(original))));
        File.Move(Path.Combine(directory, "library.json"), Path.Combine(directory, "unavailable.json"));
        var portable = ScenarioDefinitionTransport.Decode(transport, null, Canonical.Bundle);
        var opened = ScenarioDefinitionJson.FromJson(scenarios.Scenario(saved.Id).Definition.GetRawText(), null, Canonical.Bundle);
        Assert.Equal(ScenarioDefinitionJson.ToJson(original), ScenarioDefinitionJson.ToJson(portable));
        Assert.Equal(ScenarioDefinitionJson.ToJson(original), ScenarioDefinitionJson.ToJson(opened));
        Assert.Equal("Knightly Boots", GameEngine.CreateGame(opened, Canonical.Bundle).Equipment("knightly-boots").Name);
        Assert.Throws<LibraryException>(() => Service.Preview(new(original, "knightly-kit", 1, [], original.ContentReferences![0].SnapshotDigest)));
    }

    [Fact]
    public void ConflictsCannotShadowAndLocalUnitOverrideRemainsExplicit()
    {
        var helper = new UnitType("helper", 1, 0, 0, 0, 2) { Unique = true };
        var package = Package() with { Definitions = new() { Units = [helper], Equipment = [new("call-helper", "Call Helper")
            { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic("helper", Posture.Lying))] }] } };
        Store.Create(package);
        var preview = Pin(family: ContentFamily.Equipment, id: "call-helper");
        var scenario = Apply(preview) with { UnitTypeIds = [.. Scenario().UnitTypeIds, helper.Id], UnitTypes = [.. Scenario().UnitTypes!, helper with { Hp = 7 }] };
        Assert.Equal(7, GameEngine.CreateGame(scenario, Canonical.Bundle).Types.Single(t => t.Id == "helper").Hp);
        Assert.NotNull(Pin(Scenario() with { EquipmentTypes = [package.Definitions.Equipment[0]] }, ContentFamily.Equipment, "call-helper"));
        Store.Save("knightly-kit", new(package with { Title = "Changed" }, 1));
        var second = Pin(family: ContentFamily.Equipment, id: "call-helper", revision: 2);
        Assert.Contains(Assert.Throws<ContentCompositionException>(() => ScenarioContentReferences.Compose(Canonical.Bundle, [preview.Reference, second.Reference])).Diagnostics,
            d => d.Code == "incompatibleRevision");
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("version")]
    [InlineData("missing")]
    [InlineData("closure")]
    [InlineData("origins")]
    [InlineData("array")]
    public void InvalidEmbeddedSnapshotsFailWithSourceAndFieldDiagnostics(string damage)
    {
        Store.Create(Package()); var pin = Pin().Reference;
        pin = damage switch {
            "digest" => pin with { SnapshotDigest = "broken" },
            "version" => pin with { FormatVersion = 99 },
            "missing" => pin with { Snapshot = pin.Snapshot with { Equipment = [] } },
            "origins" => pin with { Origins = [.. pin.Origins, pin.Origins[0]] },
            "array" => pin with { Snapshot = pin.Snapshot with { Equipment = default } },
            _ => pin with { Closure = [] }
        };
        if (damage is not ("digest" or "array")) pin = pin with { SnapshotDigest = ScenarioContentReferences.Digest(pin) };
        var error = Assert.Throws<ContentCompositionException>(() => ScenarioContentReferences.Compose(Canonical.Bundle, [pin]));
        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Source is not null && (diagnostic.Path.Contains("contentReferences") || diagnostic.Source.Path!.Contains("contentReferences")));
    }

    [Fact]
    public void CapturedCanonicalDependenciesRemainAuthoritativeButLibraryIdentityCollisionsFail()
    {
        var gear = new EquipmentDefinition("call-goblin", "Call Goblin") { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic(UnitTypeIds.Goblin, Posture.Lying))] };
        Store.Create(Package() with { Dependencies = [ContentPackageCatalog.CSharpPackageId], Definitions = new() { Equipment = [gear] } });
        var scene = Apply(Pin(family: ContentFamily.Equipment, id: gear.Id));
        var captured = scene.ContentReferences![0].Snapshot.Units.Single(t => t.Id == UnitTypeIds.Goblin);
        var changed = Canonical.Bundle with { Units = [.. Canonical.Bundle.Units.Select(t => t.Id == captured.Id ? t with { Atk = 99 } : t)] };
        Assert.Equal(captured.Atk, GameEngine.CreateGame(scene, changed).Types.Single(t => t.Id == captured.Id).Atk);
        var collision = Canonical.Bundle with { Equipment = [.. Canonical.Bundle.Equipment, gear] };
        Assert.Throws<ContentCompositionException>(() => ScenarioContentReferences.Compose(collision, scene.ContentReferences));
    }

    [Fact]
    public void MultiplePinsReuseTheSameImmutableClosureAndPermitMutualSummons()
    {
        UnitType Summon(string id, string target) => new(id, 1, 0, 0, 0, 1)
            { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic(target, Posture.Lying))] };
        Store.Create(new() { Id = "common", Definitions = new() { Units = [Summon("a", "b"), Summon("b", "a")],
            Equipment = [new("shared-call", "Shared Call") { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic("a", Posture.Lying))] }] } });
        foreach (var id in new[] { "one", "two" }) Store.Create(new ContentPackage { Id = id, Dependencies = ["common"], Definitions = new()
            { LootCards = [new(id + "-gift", id) { EquipmentTypeId = "shared-call" }] } }, [new("common", 1)]);
        var first = Service.Preview(new(Scenario(), "one", 1, [new(ContentFamily.LootCard, "one-gift")]));
        var both = Service.Preview(new(Apply(first), "two", 1, [new(ContentFamily.LootCard, "two-gift")]));
        var game = GameEngine.CreateGame(Apply(both), Canonical.Bundle);
        Assert.Single(game.EquipmentTypes, e => e.Id == "shared-call");
        Assert.Single(game.Types, t => t.Id == "a"); Assert.Single(game.Types, t => t.Id == "b");
        Assert.Equal(2, both.References.Count);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "bearer") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    [Fact]
    public void SaveResumeAndReplayUsePinnedPhysicalEquipmentAfterLibraryChanges()
    {
        Store.Create(Package()); var scenario = WithLoot(Apply(Pin()));
        var game = GameEngine.StartRound(GameEngine.CreateGame(scenario, Canonical.Bundle), new Dice(), false);
        var acquired = GameEngine.Advance(game.State, new Choice("stay"), new Dice(), false);
        Assert.Equal("knightly-boots", Assert.Single(acquired.State.Units.Single(u => u.Id == "bearer").Inventory).EquipmentTypeId);
        var save = JsonSerializer.Serialize(acquired.State);
        Store.Save("knightly-kit", new(Package("Changed outside game", 9), 1));
        var resumed = JsonSerializer.Deserialize<GameState>(save)!;
        var action = acquired.NextInput!.Candidates.Single(c => c.Ability?.SourceTypeId == "knightly-boots");
        var expected = GameEngine.Advance(acquired.State, new Choice(action.Key), new Dice(), false);
        var replay = GameEngine.Advance(resumed, new Choice(action.Key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(replay));
        Assert.True(replay.State.IsUpright("friend"));
        Assert.Equal(1, Assert.Single(replay.State.Units.Single(u => u.Id == "bearer").Inventory).AbilityUseCounts["encourage"]);
        Assert.Equal(2, replay.State.Equipment("knightly-boots").LocalAbilities[0].Usage.MaxUses);
        Assert.DoesNotContain("snapshotDigest", save, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameplayProjectionsNeverExposeHiddenPinnedDefinitionsOrProvenance(bool fog)
    {
        Store.Create(Package()); var scene = WithLoot(Apply(Pin())) with { Board = new() { Width = 10, Height = 10 },
            LootMarkers = [new("gift", new(9, 9))], FogOfWarSides = fog ? ["blue"] : [] };
        var state = GameEngine.CreateGame(scene, Canonical.Bundle);
        using var game = new PlaytestGame(new Dice(), state);
        var response = JsonSerializer.Serialize(game.BrowserResponse(game.Snapshot()));
        Assert.DoesNotContain("knightly-boots", response); Assert.DoesNotContain("knightly-gift", response);
        Assert.DoesNotContain(scene.ContentReferences![0].RevisionDigest, response);
        Assert.Contains("knightly-boots", JsonSerializer.Serialize(state));
    }

    [Fact]
    public async Task PreviewApiAndEditorContextAreReadOnlyAndPlaytestUsesPortableDefinitions()
    {
        Store.Create(Package());
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", WebRoot, "--ContentLibrary:Path", directory,
            "--CanonicalContent:Directory", Path.Combine(directory, "canonical")], new Dice(), GameEngine.CreateGame(Scenario(), Canonical.Bundle));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var before = await client.GetStringAsync("/api/game");
        using var response = await client.PostAsJsonAsync("/api/content-library/scenario-reference/preview", new PreviewScenarioReference(Scenario(), "knightly-kit", 1, [new(ContentFamily.LootCard, "knightly-gift")]), Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var preview = (await response.Content.ReadFromJsonAsync<ScenarioReferencePreview>(Json))!;
        Assert.Equal(before, await client.GetStringAsync("/api/game"));
        var scene = WithLoot(Apply(preview));
        var context = new ContentEditorContext { ContentReferences = scene.ContentReferences };
        using var inspected = await client.PostAsJsonAsync("/api/content/context", context, Json); inspected.EnsureSuccessStatusCode();
        var catalog = (await inspected.Content.ReadFromJsonAsync<ContentBrowserResponse>(Json))!;
        Assert.Equal("library-pinned", catalog.Entries.Single(e => e.Id == "knightly-boots").Origin);
        using var edit = await client.PostAsJsonAsync("/api/content/validate", new ContentEditorRequest("equipment", JsonSerializer.SerializeToElement(Package().Definitions.Equipment[0], Json), "knightly-boots", context), Json);
        Assert.False((await edit.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Valid);
        using var playtest = await client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(scene) });
        playtest.EnsureSuccessStatusCode();
        Assert.Equal("Knightly Boots", app.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State.Equipment("knightly-boots").Name);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
