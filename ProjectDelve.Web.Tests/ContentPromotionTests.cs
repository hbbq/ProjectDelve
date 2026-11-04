using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentPromotionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-promote-" + Guid.NewGuid().ToString("N"));
    private readonly CanonicalContent canonical;
    private readonly ContentLibrary library;
    private readonly ContentPromotionService service;
    public ContentPromotionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "canonical"));
        canonical = new(Path.Combine(root, "canonical"));
        library = new(Path.Combine(root, "library"), canonical.Packages, canonicalProvider: () => canonical.Packages);
        service = new(library, canonical);
    }
    private static readonly ContentIdentity Boots = new(ContentFamily.Equipment, "knightly-boots");
    private static readonly ContentIdentity Gift = new(ContentFamily.LootCard, "knightly-gift");
    private static ContentPackage Kit(string name = "Knightly Boots") => new() { Id = "knightly-kit", Dependencies = [ContentPackageCatalog.CSharpPackageId], Definitions = new() {
        Equipment = [new(Boots.TypeId, name) { LocalAbilities = [new("encourage", "Encourage", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright)) { Usage = new(2) }] }, new("unrelated", "Unrelated")],
        LootCards = [new(Gift.TypeId, "Knightly Gift") { EquipmentTypeId = Boots.TypeId }] } };
    private static ScenarioDefinition Scenario() => new() { Board = new() { Width = 4, Height = 3 }, UnitTypeIds = ["bearer"],
        UnitTypes = [new("bearer", 1, 0, 0, 0, 1) { CanHaveEquipment = true }], Units = [new("bearer", "blue", new(1, 1))],
        Agency = [new("bearer", "blue", ControllerKind.Human)], Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty };
    private PromotionRequest Request(string destination = "published-kit", params ContentIdentity[] selected) => new(new("library", "knightly-kit", 1), destination, [.. selected.Length == 0 ? [Gift] : selected]);

    [Fact]
    public void SelectiveLootClosurePublishesOnePackageAndRefreshesCanonicalAcrossRestart()
    {
        library.Create(Kit()); var before = canonical.Snapshot.Revision;
        var preview = service.Preview(Request());
        Assert.Single(preview.Additional, Boots); Assert.Empty(preview.ReusedCanonical);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "canonical"), "*.json"));
        Assert.DoesNotContain(preview.Package.Definitions.Equipment, e => e.Id == "unrelated");
        Assert.Throws<ArgumentException>(() => service.Publish(new(preview, false)));
        var result = service.Publish(new(preview, true));
        Assert.NotEqual(before, result.CanonicalRevision); Assert.Equal(2, result.Published.Length);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "canonical"), "*.json"));
        var restarted = new CanonicalContent(Path.Combine(root, "canonical"));
        Assert.Equal(result.CanonicalRevision, restarted.Snapshot.Revision);
        Assert.Contains(ContentBrowserCatalog.Canonical(restarted.Bundle).Entries, e => e.Id == Boots.TypeId && e.Origin == "canonical");
        Assert.Equal(1, library.Read("knightly-kit").Revision);
        Assert.Throws<ArgumentException>(() => library.Save("knightly-kit", new(Kit("Changed"), 1)));
        Assert.Single(library.History("knightly-kit"));
    }
    [Fact]
    public void StandaloneEquipmentAndCanonicalDependenciesAreHandledWithoutDuplicatingProviders()
    {
        var gear = Kit().Definitions.Equipment[0] with { LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic(UnitTypeIds.Goblin, Posture.Lying))] };
        library.Create(Kit() with { Definitions = new() { Equipment = [gear] } });
        var preview = service.Preview(Request("equipment-only", Boots));
        Assert.Contains(new(ContentFamily.Unit, UnitTypeIds.Goblin), preview.ReusedCanonical);
        Assert.Empty(preview.Package.Definitions.Units);
        Assert.Contains(ContentPackageCatalog.CSharpPackageId, preview.Package.Dependencies);
        service.Publish(new(preview, true));
        Assert.Single(canonical.Bundle.Equipment.Where(e => e.Id == Boots.TypeId));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ScenarioLocalAndPinnedPromotionPreservesScenarioAndGameSnapshots(bool pinned)
    {
        library.Create(Kit()); var scene = Scenario();
        if (pinned) scene = scene with { ContentReferences = new ScenarioReferenceService(library, canonical).Preview(new(scene, "knightly-kit", 1, [Gift])).References.ToList() };
        else scene = scene with { EquipmentTypes = [Kit().Definitions.Equipment[0]], LootCardDefinitions = [Kit().Definitions.LootCards[0]] };
        scene = scene with { Loot = new() { Composition = [new() { LootCardId = Gift.TypeId }] }, LootMarkers = [new("gift", new(1, 1))] };
        var serialized = ScenarioDefinitionJson.ToJson(scene);
        var oldGame = GameEngine.CreateGame(scene, canonical.Bundle);
        var preview = service.Preview(new(new(pinned ? "scenario-pinned" : "scenario-local", Scenario: scene), "from-scenario", [Gift]));
        service.Publish(new(preview, true));
        var loaded = ScenarioDefinitionJson.FromJson(serialized, null, canonical.Bundle);
        Assert.Equal(serialized, ScenarioDefinitionJson.ToJson(loaded));
        Assert.Equal("Knightly Boots", GameEngine.CreateGame(loaded, canonical.Bundle).Equipment(Boots.TypeId).Name);
        var transport = ScenarioDefinitionTransport.Encode(loaded);
        Assert.Equal(serialized, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport, null, canonical.Bundle)));
        var newScene = Scenario() with { Loot = scene.Loot, LootMarkers = scene.LootMarkers };
        Assert.Equal("Knightly Boots", GameEngine.CreateGame(newScene, canonical.Bundle).Equipment(Boots.TypeId).Name);
        var saved = JsonSerializer.Serialize(oldGame);
        Assert.Equal(saved, JsonSerializer.Serialize(JsonSerializer.Deserialize<GameState>(saved)!));
        var replayA = Replay(oldGame); var replayB = Replay(JsonSerializer.Deserialize<GameState>(saved)!);
        Assert.Equal(replayA, replayB);
    }
    private static string Replay(GameState state)
    {
        var round = GameEngine.StartRound(state, new Dice(), false);
        return JsonSerializer.Serialize(GameEngine.Advance(round.State, new Choice("stay"), new Dice(), false));
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    [Fact]
    public void StaleSourceHeadOrDestinationCannotPublish()
    {
        library.Create(Kit()); var preview = service.Preview(Request());
        library.Save("knightly-kit", new(Kit("Revision two"), 1));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Publish(new(preview, true))).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "canonical"), "*.json"));
        preview = service.Preview(Request()); // explicitly choose historical revision against current head
        service.Publish(new(service.Preview(Request("gear-first", Boots)), true));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Publish(new(preview, true))).StatusCode);
    }
    [Fact]
    public void MissingReferencesAndCanonicalCSharpOrJsonCollisionsFail()
    {
        var scene = Scenario() with { LootCardDefinitions = [new("missing-reward", "Missing") { EquipmentTypeId = "absent" }] };
        Assert.Throws<ContentCompositionException>(() => service.Preview(new(new("scenario-local", Scenario: scene), "missing", [new(ContentFamily.LootCard, "missing-reward")])));
        library.Create(Kit() with { Dependencies = [], Definitions = new() { Equipment = [new(canonical.Bundle.Equipment[0].Id, "Unrelated")] } });
        Assert.Throws<ContentCompositionException>(() => service.Preview(Request("csharp-collision", new ContentIdentity(ContentFamily.Equipment, canonical.Bundle.Equipment[0].Id))));
    }
    [Fact]
    public void InterruptedPublicationLeavesNoPartialCatalogAndCanBeRetried()
    {
        library.Create(Kit()); var preview = service.Preview(Request());
        canonical.PublicationReady = () => throw new IOException("simulated interrupted write");
        Assert.Throws<IOException>(() => service.Publish(new(preview, true)));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "canonical"), "*.json"));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "canonical"), "*.tmp"));
        File.WriteAllText(Path.Combine(root, "canonical", "orphan.json.tmp"), "interrupted");
        Assert.Equal(preview.DestinationRevision, new CanonicalContent(Path.Combine(root, "canonical")).Snapshot.Revision);
        canonical.PublicationReady = null; service.Publish(new(preview, true));
        Assert.Throws<LibraryException>(() => service.Preview(Request()));
        var broken = preview.Package with { Id = "duplicate-json" };
        Assert.Throws<ContentCompositionException>(() => ContentPackageCatalog.Compose(canonical.Packages.Append(broken).Select(p => new ContentPackageDocument(p))));
    }
    [Fact]
    public async Task HttpPublicationRefreshesBrowserDesignerAndNewPlaytestWithoutChangingRunningState()
    {
        library.Create(Kit());
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", webRoot,
            "--CanonicalContent:Directory", Path.Combine(root, "canonical"), "--ContentLibrary:Path", Path.Combine(root, "library"),
            "--Library:Path", Path.Combine(root, "scenarios")], new Dice(), GameEngine.CreateGame(Scenario(), canonical.Bundle));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var json = ContentPackageJson.CreateOptions();
        var before = await client.GetStringAsync("/api/game");
        var response = await client.PostAsJsonAsync("/api/content-library/promotion/preview", Request(), json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var preview = (await response.Content.ReadFromJsonAsync<PromotionPreview>(json))!;
        response = await client.PostAsJsonAsync("/api/content-library/promotion/publish", new PublishPromotion(preview, true), json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await client.GetStringAsync("/api/game"));
        Assert.Contains("knightly-boots", await client.GetStringAsync("/api/content/catalog"));
        Assert.Contains("knightly-boots", await client.GetStringAsync("/api/designer/catalog"));
        response = await client.PostAsJsonAsync("/api/content/context", new ContentEditorContext(), json);
        Assert.Contains("knightly-boots", await response.Content.ReadAsStringAsync());
        var scene = Scenario() with { Loot = new() { Composition = [new() { LootCardId = Gift.TypeId }] }, LootMarkers = [new("gift", new(1, 1))], FogOfWarSides = ["blue"] };
        response = await client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(scene) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("Knightly Boots", app.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State.Equipment(Boots.TypeId).Name);
        Assert.DoesNotContain("sourceDigest", await client.GetStringAsync("/api/game"));
    }
    [Fact]
    public void ChangedScenarioOrPreviewCannotBePublishedAndInvalidPublicationDigestsFailLoading()
    {
        var scene = Scenario() with { EquipmentTypes = [Kit().Definitions.Equipment[0]] };
        var preview = service.Preview(new(new("scenario-local", Scenario: scene), "local-gear", [Boots]));
        var changed = preview with { Request = preview.Request with { Source = preview.Request.Source with { Scenario = scene with { LootMarkers = [] } } } };
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Publish(new(changed, true))).StatusCode);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Publish(new(preview with { PreviewDigest = "changed" }, true))).StatusCode);
        var invalid = preview.Package with { Publications = [preview.Package.Publications!.Value[0] with { PayloadDigest = "invalid" }] };
        Assert.Throws<ArgumentException>(() => ContentPackageCatalog.Compose(canonical.Packages.Append(invalid).Select(p => new ContentPackageDocument(p))));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

