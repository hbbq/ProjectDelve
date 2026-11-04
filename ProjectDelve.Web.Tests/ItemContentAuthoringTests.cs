using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ItemContentAuthoringTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-item-authoring-" + Guid.NewGuid().ToString("N"));
    private static ItemDefinition Fireball() => new("last-resort-fireball", "Last Resort Fireball")
        { LocalAbilities = [new("use", "Fireball", AbilityTiming.Action, new FireballMechanic())] };
    private static ItemDefinition Bottle(string target = UnitTypeIds.Goblin) => new("goblin-in-a-bottle", "Goblin in a Bottle")
        { LocalAbilities = [new("release", "Release Goblin", AbilityTiming.Action, new AdjacentUnitSummonMechanic(target, Posture.Lying))] };
    private static LootCardDefinition Loot(ItemDefinition item) => new(item.Id + "-gift", item.Name + " Gift") { ItemTypeId = item.Id };
    private static ContentEditorRequest Edit(ItemDefinition item, string? existing = null, ContentEditorContext? context = null) =>
        new("items", JsonSerializer.SerializeToElement(item, Json), existing, context);
    private static ContentPackage Package(params ItemDefinition[] items) => new()
    {
        Id = "bottles-and-scrolls", Dependencies = [ContentPackageCatalog.CSharpPackageId],
        Definitions = new() { Items = [.. items], LootCards = [.. items.Select(Loot)] }
    };
    private static ScenarioDefinition Scene() => new()
    {
        Board = new() { Width = 6, Height = 5 }, UnitTypeIds = ["item-collector", "item-target"],
        UnitTypes = [new("item-collector", 2, 3, 1, 0, 4) { Unique = true, CanHaveItems = true },
            new("item-target", 0, 0, 0, 0, 6) { Unique = true }],
        Units = [new("item-collector", "blue", new(1, 1), UnitId: "collector"), new("item-target", "red", new(3, 1), UnitId: "target")],
        Agency = [new("item-collector", "blue", ControllerKind.Human), new("item-target", "red", ControllerKind.Human)],
        Loot = LootSettings.Empty, Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    };

    [Fact]
    public void ScenarioItemValidationUsesItemContractsAndActionablePathsWithoutBoardConstruction()
    {
        var item = Fireball();
        var valid = ContentEditorApi.Validate(Edit(item));
        Assert.True(valid.Valid, string.Join(";", valid.Diagnostics));
        Assert.Equal(item.Id, valid.Entry!.Id);
        Assert.Contains("Consume this Item", valid.Entry.Summary);
        Assert.Null(Assert.Single(valid.Entry.Abilities).Usage.MaxUses);
        void Invalid(ItemDefinition candidate, string path, ContentEditorContext? context = null)
        {
            var result = ContentEditorApi.Validate(Edit(candidate, context: context));
            Assert.False(result.Valid);
            Assert.Contains(result.Diagnostics, d => d.Path == path);
        }
        Invalid(item with { Id = "" }, "definition.id");
        Invalid(item with { LocalAbilities = [] }, "definition.localAbilities");
        Invalid(item with { LocalAbilities = [item.LocalAbilities[0], item.LocalAbilities[0]] }, "definition.localAbilities[1].key");
        Invalid(item with { LocalAbilities = [item.LocalAbilities[0] with { Timing = AbilityTiming.Passive }] }, "definition.localAbilities[0].timing");
        Invalid(item with { LocalAbilities = [item.LocalAbilities[0] with { Usage = new(1) }] }, "definition.localAbilities[0].usage");
        Invalid(item with { LocalAbilities = [new("heal", "Heal", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 0))] }, "definition.localAbilities[0].mechanic.amount");
        Invalid(Bottle("missing-unit"), "definition.localAbilities[0].mechanic.unitTypeId");
        Invalid(item, "definition.id", new() { ItemTypes = [item] });
        var local = new UnitType("local-goblin", 1, 1, 1, 1, 1) { DisplayName = "Local Goblin" };
        var summoned = ContentEditorApi.Validate(Edit(Bottle(local.Id), context: new() { UnitTypes = [local] }));
        Assert.True(summoned.Valid);
        Assert.Contains(summoned.Entry!.Abilities, a => a.Description.Contains("Local Goblin"));
        Assert.Contains(summoned.Entry.References, r => r.Id == local.Id && r.Exists);
        var node = JsonSerializer.SerializeToNode(item, Json)!;
        node["localAbilities"] = new JsonArray((JsonNode?)null);
        Assert.False(ContentEditorApi.Validate(new("items", JsonSerializer.SerializeToElement(node))).Valid);
        var missingReward = ContentEditorApi.Validate(new("lootCards", JsonSerializer.SerializeToElement(Loot(Bottle()), Json)));
        Assert.Contains(missingReward.Diagnostics, d => d.Path == "definition.itemTypeId");
        Assert.DoesNotContain(ItemCatalog.All, i => i.Id == item.Id);
    }

    [Fact]
    public void LibraryPreviewLinkedLootHistoryCopyAndSelectedDependenciesPreserveDefinitions()
    {
        var store = new ContentLibrary(directory);
        var empty = Package() with { Definitions = new() };
        var preview = ContentLibraryAuthoring.Validate(store, new(new(empty, 0), Edit(Fireball())));
        Assert.True(preview.Valid);
        Assert.Equal("library-local", preview.Entry!.Origin);
        Assert.Empty(store.Catalog().Packages);
        var withItem = empty with { Definitions = new() { Items = [Fireball()] } };
        var reward = ContentLibraryAuthoring.Validate(store, new(new(withItem, 0),
            new("lootCards", JsonSerializer.SerializeToElement(Loot(Fireball()), Json))));
        Assert.True(reward.Valid);
        Assert.Contains(reward.Entry!.References, r => r.TargetKey == "items:" + Fireball().Id && r.Exists);
        var first = store.Create(Package(Fireball(), Bottle()));
        var changed = Fireball() with { Name = "Renamed Scroll", LocalAbilities = [Fireball().LocalAbilities[0] with { Name = "Renamed Flame" }] };
        var nextPackage = first.Package with { Definitions = first.Package.Definitions with { Items = [changed, Bottle()] } };
        Assert.True(ContentLibraryAuthoring.Validate(store, new(new(first.Package, 1), Edit(changed with { Name = "Changed after publication" }, changed.Id))).Valid);
        var second = store.Save(first.Package.Id, new(nextPackage, 1));
        Assert.Equal("use", second.Package.Definitions.Items[0].LocalAbilities[0].Key);
        Assert.Equal("Last Resort Fireball", store.Read(first.Package.Id, 1).Package.Definitions.Items[0].Name);
        Assert.Equal(2, store.History(first.Package.Id).Length);
        Assert.Throws<LibraryException>(() => store.Save(first.Package.Id, new(first.Package, 1)));
        var canonical = ItemCatalog.All[0];
        Assert.True(ContentEditorApi.Validate(Edit(canonical)).Valid);
        var copy = canonical with { Id = "independent-item", Name = "Independent Copy" };
        Assert.True(ContentEditorApi.Validate(Edit(copy)).Valid);
        Assert.True(ContentLibraryAuthoring.Validate(store, new(new(second.Package, 2), Edit(copy))).Valid);
        var dependent = new ContentPackage { Id = "gift-package", Dependencies = [first.Package.Id],
            Definitions = new() { LootCards = [new("external-gift", "External Gift") { ItemTypeId = Bottle().Id }] } };
        Assert.True(ContentLibraryAuthoring.Validate(store, new(new(dependent with { Definitions = new() }, 0, [new(first.Package.Id, 2)]),
            new("lootCards", JsonSerializer.SerializeToElement(dependent.Definitions.LootCards[0], Json)))).Valid);
        Assert.Throws<ArgumentException>(() => store.Inspect(new(dependent, 0)));
    }

    [Fact]
    public void ItemPinsUpdateClosurePortableScenarioStorageAndPromotionKeepOldRevisionsIsolated()
    {
        var canonicalDirectory = Path.Combine(directory, "canonical"); Directory.CreateDirectory(canonicalDirectory);
        var canonical = new CanonicalContent(canonicalDirectory);
        var store = new ContentLibrary(Path.Combine(directory, "library"), canonical.Packages, canonicalProvider: () => canonical.Packages);
        var first = store.Create(Package(Fireball(), Bottle()));
        var references = new ScenarioReferenceService(store, canonical);
        // Item roots are supported independently of a Loot root; an unrelated Item stays out.
        var direct = references.Preview(new(Scene(), first.Package.Id, 1, [new(ContentFamily.Item, Bottle().Id)]));
        Assert.Single(direct.Reference.Snapshot.Items);
        Assert.Empty(direct.Reference.Snapshot.LootCards);
        Assert.Contains(direct.Reference.Snapshot.Units, u => u.Id == UnitTypeIds.Goblin);
        var selected = ImmutableArray.Create(new ContentIdentity(ContentFamily.LootCard, Loot(Bottle()).Id));
        var preview = references.Preview(new(Scene(), first.Package.Id, 1, selected));
        Assert.Single(preview.Reference.Snapshot.Items);
        var scene = Scene() with { ContentReferences = preview.References.ToList() };
        var original = ScenarioDefinitionJson.ToJson(scene);
        Assert.False(ContentEditorApi.Validate(Edit(Bottle(), Bottle().Id, new() { ContentReferences = scene.ContentReferences }), canonical.Bundle).Valid);
        var changed = Bottle() with { Name = "A Revised Bottle" };
        var second = store.Save(first.Package.Id, new(Package(Fireball(), changed), 1));
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scene));
        var updated = references.Preview(new(scene, first.Package.Id, 2, selected, scene.ContentReferences![0].SnapshotDigest));
        Assert.Contains(updated.Changes, c => c.Identity == new ContentIdentity(ContentFamily.Item, changed.Id) && c.Change == "Changed");
        var newScene = scene with { ContentReferences = updated.References.ToList() };
        var transport = ScenarioDefinitionTransport.Encode(newScene);
        var restored = ScenarioDefinitionTransport.Decode(transport, null, canonical.Bundle);
        Assert.Equal(ScenarioDefinitionJson.ToJson(newScene), ScenarioDefinitionJson.ToJson(restored));
        var scenarios = new ScenarioLibrary(Path.Combine(directory, "scenarios"), content: canonical.Bundle);
        var saved = scenarios.SaveScenario(null, new("Item fixture", null, JsonSerializer.SerializeToElement(restored, Json)));
        Assert.Equal(ScenarioDefinitionJson.ToJson(restored), ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(scenarios.Scenario(saved.Id).Definition.GetRawText(), null, canonical.Bundle)));
        Assert.Empty(GameEngine.CreateGame(restored, canonical.Bundle).Units.Single(u => u.Id == "collector").Inventory);
        var promotion = new ContentPromotionService(store, canonical);
        var proposal = promotion.Preview(new(new("library", first.Package.Id, second.Revision), "published-items", [new(ContentFamily.Item, changed.Id)]));
        Assert.Single(proposal.Package.Definitions.Items);
        Assert.Contains(proposal.ReusedCanonical, id => id == new ContentIdentity(ContentFamily.Unit, UnitTypeIds.Goblin));
        promotion.Publish(new(proposal, true));
        Assert.Equal(changed.Name, canonical.Bundle.Items.Single(i => i.Id == changed.Id).Name);
        Assert.Single(GameEngine.CreateGame(restored, canonical.Bundle).ItemTypes, i => i.Id == changed.Id);
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(scene));
        Assert.False(ContentLibraryAuthoring.Validate(store, new(new(second.Package, 2), Edit(changed with { Name = "Changed after publication" }, changed.Id))).Valid);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public void AuthoredLinkedLootAcquiresTwoCopiesAndConsumesOnlySelectedCopyWithOrdinaryActionBudgetAndReplay(bool summon, bool secondAbility)
    {
        var item = summon ? Bottle() : Fireball();
        if (!summon) item = item with { LocalAbilities = [item.LocalAbilities[0], item.LocalAbilities[0] with { Key = "alternative", Name = "Alternative Flame" }] };
        var validated = ContentEditorApi.Validate(Edit(item));
        Assert.True(validated.Valid);
        item = validated.Definition!.Value.Deserialize<ItemDefinition>(Json)!;
        var rewardResponse = ContentEditorApi.Validate(new("lootCards", JsonSerializer.SerializeToElement(Loot(item), Json), Context: new() { ItemTypes = [item] }));
        Assert.True(rewardResponse.Valid);
        var reward = rewardResponse.Definition!.Value.Deserialize<LootCardDefinition>(Json)!;
        var scene = Scene() with { ItemTypes = [item], LootCardDefinitions = [reward],
            Loot = new() { Composition = [new(Count: 2) { LootCardId = reward.Id }] },
            LootMarkers = [new("gift-1", new(1, 1)), new("gift-2", new(1, 1))] };
        var restoredScene = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scene));
        var started = GameEngine.StartRound(GameEngine.CreateGame(restoredScene), new Dice(), false);
        if (started.NextInput!.Kind == DecisionKind.SelectUnit) started = Use(started.State, "collector");
        var ready = Use(started.State, "stay");
        var copies = ready.State.Units.Single(u => u.Id == "collector").Inventory;
        Assert.Equal(2, copies.Length);
        Assert.All(copies, o => Assert.Equal(item.Id, o.ItemTypeId));
        var selectedId = copies[0].Id; var remainingId = copies[1].Id;
        var candidate = ready.NextInput!.Candidates.First(c => c.ObjectId == selectedId &&
            c.Ability?.Key == (summon ? "release" : secondAbility ? "alternative" : "use") && (summon || c.Destination == new Cell(3, 1)));
        var saved = JsonSerializer.Serialize(ready.State, Json);
        var expected = Use(ready.State, candidate.Key);
        var replay = Use(JsonSerializer.Deserialize<GameState>(saved, Json)!, candidate.Key);
        Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(replay, Json));
        Assert.Equal(selectedId, Assert.Single(replay.State.LootDeck!.DiscardPile).Id);
        Assert.Equal(remainingId, Assert.Single(replay.State.Units.Single(u => u.Id == "collector").Inventory).Id);
        if (summon)
        {
            var goblin = Assert.Single(replay.State.Units.Where(u => u.TypeId == UnitTypeIds.Goblin));
            Assert.Equal(Posture.Lying, replay.State.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
            Assert.Equal("blue", goblin.SideId);
        }
        else
        {
            Assert.Equal(DecisionKind.RollDice, replay.NextInput!.Kind);
            var pendingSaved = JsonSerializer.Serialize(replay.State, Json);
            var continued = Use(replay.State, "roll-dice");
            var resumed = Use(JsonSerializer.Deserialize<GameState>(pendingSaved, Json)!, "roll-dice");
            Assert.Equal(JsonSerializer.Serialize(continued, Json), JsonSerializer.Serialize(resumed, Json));
            replay = continued;
        }
        Assert.DoesNotContain(replay.NextInput?.Candidates ?? [], c => c.ObjectId == remainingId);
        Assert.Contains(expected.Events, e => e.Kind == "ActionUsed");
    }

    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private sealed class Choice(string key) : IDecisionProvider { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "item-collector") ?? bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}


