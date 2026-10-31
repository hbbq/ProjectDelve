using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LootContentTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int LootDrawIndex(int exclusiveMax) => 0;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["collector"],
        UnitTypes = [new("collector", 2, 1, 1, 0, 5) { Unique = true, CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("collector", "blue", new(0, 0), UnitId: "collector-1") { InitialHp = 1 }],
        Agency = [new("collector", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1; state.ActiveToken = new("collector", "blue"); state.CurrentUnitId = "collector-1";
        state.Pending = new(DecisionKind.Activation, "collector", "collector-1", [], false);
        return state;
    }
    private static EngineResult Collect(GameState state) => GameEngine.Advance(state, new Choice("stay"), new Random(), false);

    [Fact]
    public void EveryCanonicalLootDefinitionRoundTripsAsTypedDataAndLegacyMetadataIsProjected()
    {
        LootCardCatalog.Validate(LootCardCatalog.All);
        var restored = JsonSerializer.Deserialize<List<LootCardDefinition>>(JsonSerializer.Serialize(LootCardCatalog.All, Json), Json)!;
        LootCardCatalog.Validate(restored);
        Assert.Equal(JsonSerializer.Serialize(LootCardCatalog.All, Json), JsonSerializer.Serialize(restored, Json));
        foreach (var kind in Enum.GetValues<LootCardKind>())
        {
            var definition = restored.Single(d => d.Id == LootCompatibility.TypeId(kind));
            var content = LootCards.Content(kind);
            Assert.Equal(definition.Category, content.Category);
            Assert.Equal(kind, content.LegacyKind);
            if (definition.Immediate is HealCollectorMechanic heal)
            {
                Assert.Equal(heal.Amount, content.Healing);
                Assert.Equal(definition.Description, content.Text);
            }
        }
    }

    [Fact]
    public void CanonicalRewardDeclarationsDriveInstancesWithoutChangingPresetOrderOrPhysicalIds()
    {
        var settings = new LootSettings { Preset = "Fun" };
        var rows = settings.ResolveComposition();
        var objects = settings.ResolveObjects();
        Assert.Equal(rows.SelectMany(r => Enumerable.Repeat(r.Kind, r.Count)), objects.Select(o => o.Kind));
        Assert.Equal(Enumerable.Range(1, objects.Count).Select(i => $"loot-card-{i}"), objects.Select(o => o.Id));
        foreach (var obj in objects)
        {
            var definition = LootCardCatalog.All.Single(d => d.Id == LootCompatibility.TypeId(obj.Kind));
            Assert.Equal(definition.ItemTypeId, obj.ItemTypeId);
            Assert.Equal(definition.EquipmentTypeId, obj.EquipmentTypeId);
            Assert.Equal(definition.Id, obj.LootCardId);
        }
        var changed = LootCardCatalog.All.Select(d => d.Id == "lucky-boots"
            ? d with { EquipmentTypeId = "official-key" } : d).ToArray();
        var changedObjects = settings.ResolveObjects(changed);
        Assert.Equal("official-key", Assert.Single(changedObjects.Where(o => o.Kind == LootCardKind.LuckyBoots)).EquipmentTypeId);
        Assert.Equal(objects.Select(o => o.Id), changedObjects.Select(o => o.Id));
        changed = LootCardCatalog.All.Select(d => d.Id == "lucky-boots"
            ? d with { EquipmentTypeId = null, Immediate = new HealCollectorMechanic(3) } : d).ToArray();
        var state = Ready(Scenario());
        state.LootCardDefinitions = [.. changed];
        state.LootDeck = new() { DrawPile = new LootSettings([LootCardKind.LuckyBoots]).ResolveObjects(changed) };
        state.LootMarkers = [new("changed-reward", new(0, 0))];
        var result = Collect(state);
        Assert.Equal(4, result.State.Units[0].CurrentHp);
        Assert.Empty(result.State.Units[0].Inventory);
    }

    [Fact]
    public void UnfamiliarImmediateCardUsesDeclaredAmountDespiteNameAndLegacyKindAndCapsHealing()
    {
        var card = new LootCardDefinition("unexpected-supply", "Second Wind") { Immediate = new HealCollectorMechanic(3) };
        var scenario = Scenario() with
        {
            LootCardDefinitions = [card],
            Loot = new() { Composition = [new(LootCardKind.GoatBell, 2) { LootCardId = card.Id }] },
            LootMarkers = [new("first", new(0, 0)), new("second", new(0, 0))]
        };
        var initial = Ready(scenario);
        var result = Collect(initial);
        Assert.Equal(5, result.State.Units[0].CurrentHp);
        Assert.Empty(result.State.Units[0].Inventory);
        Assert.Empty(result.State.GroundObjects);
        Assert.Equal(2, result.State.LootDeck!.DiscardPile.Count);
        Assert.Equal(new[] { "MovementCompleted", "LootCardDrawn", "HealResolved", "LootCardDrawn", "HealResolved" },
            result.Events.Take(5).Select(e => e.Kind));
        Assert.Equal(new[] { 3, 1 }, result.Events.Where(e => e.Kind == "HealResolved").Select(e => e.Healing));
        Assert.All(result.Events.Where(e => e.Kind == "HealResolved"), e =>
        {
            Assert.Equal("Second Wind", e.AbilityName);
            Assert.Equal("loot:unexpected-supply", e.ActionId);
        });
        Assert.Equal(1, initial.Units[0].CurrentHp);
        var content = initial.ObjectContent(initial.LootDeck!.DrawPile[0]);
        Assert.Equal("Restore 3 HP, without exceeding maximum HP.", content.Text);
        Assert.Null(content.LegacyKind);
        Assert.Equal(card.Id, content.LootCardId);
    }

    [Fact]
    public void CanonicalImmediateUsesRuntimeDefinitionAndKeepsHistoricalEventIdentity()
    {
        var state = Ready(Scenario() with
        {
            Loot = new([LootCardKind.PocketBreakfast]), LootMarkers = [new("one", new(0, 0))]
        });
        var index = state.LootCardDefinitions.FindIndex(d => d.Id == "pocket-breakfast");
        state.LootCardDefinitions[index] = state.LootCardDefinitions[index] with { Immediate = new HealCollectorMechanic(1), Name = "Renamed breakfast" };
        var result = Collect(state);
        var healing = Assert.Single(result.Events.Where(e => e.Kind == "HealResolved"));
        Assert.Equal(1, healing.Healing); Assert.Equal("pocket-breakfast", healing.ActionId);
        Assert.Equal("Renamed breakfast", healing.AbilityName);
        Assert.Equal(LootCardKind.PocketBreakfast, state.ObjectContent(state.LootDeck!.DrawPile[0]).LegacyKind);
    }

    [Fact]
    public void LocalImmediateDefinitionsSurviveScenarioTransportSaveAndDeterministicReplay()
    {
        var definition = Scenario() with
        {
            LootCardDefinitions = [new("restorative-soup", "Restorative Soup") { Immediate = new HealCollectorMechanic(3) }],
            Loot = new() { Composition = [new() { LootCardId = "restorative-soup" }] },
            LootMarkers = [new("one", new(0, 0))]
        };
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(loaded));
        var state = Ready(loaded);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(restored);
        var original = Collect(state); var replayed = Collect(restored);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(replayed));
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(original.State))!;
        Assert.Empty(resumed.LootDeck!.DrawPile);
        Assert.Equal(original.State.LootDeck!.DiscardPile, resumed.LootDeck.DiscardPile);
        Assert.Equal(4, resumed.Units[0].CurrentHp);
    }

    [Fact]
    public void OldExplicitEmptyCatalogLoadsWithoutRefillingDeckOrChangingPhysicalObjects()
    {
        var state = Ready(Scenario());
        state.LootDeck = new() { DrawPile = [new("legacy-breakfast", LootCardKind.PocketBreakfast)],
            DiscardPile = [new("legacy-wind", LootCardKind.SecondWind)] };
        state.LootMarkers = [new("one", new(0, 0))];
        var json = JsonNode.Parse(JsonSerializer.Serialize(state))!;
        json["LootCardDefinitions"] = new JsonArray();
        var restored = JsonSerializer.Deserialize<GameState>(json.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(state.LootDeck.DrawPile, restored.LootDeck!.DrawPile);
        Assert.Equal(state.LootDeck.DiscardPile, restored.LootDeck.DiscardPile);
        Assert.Equal(new[] { "pocket-breakfast", "second-wind" }, restored.LootCardDefinitions.Select(d => d.Id));
        var result = Collect(restored);
        Assert.Equal(3, result.State.Units[0].CurrentHp);
        Assert.Equal("legacy-breakfast", result.State.LootDeck!.DiscardPile.Last().Id);
        Assert.Equal("pocket-breakfast", Assert.Single(result.Events.Where(e => e.Kind == "HealResolved")).ActionId);
    }

    [Fact]
    public void LegacyMigrationPreservesExistingDefinitionsAndDoesNotPopulateObservedCatalogs()
    {
        var state = Ready(Scenario());
        state.LootDeck = new() { DrawPile = [new("legacy-breakfast", LootCardKind.PocketBreakfast)] };
        state.LootCardDefinitions = [new("pocket-breakfast", "Saved value") { Immediate = new HealCollectorMechanic(4) }];
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(4, Assert.IsType<HealCollectorMechanic>(Assert.Single(restored.LootCardDefinitions).Immediate).Amount);
        var observed = FogOfWar.Observe(state, null);
        Assert.Empty(observed.LootCardDefinitions); Assert.Null(observed.LootDeck);
        var observedAgain = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(observed))!;
        Assert.Empty(observedAgain.LootCardDefinitions); Assert.Null(observedAgain.LootDeck);
    }

    [Fact]
    public void HistoricalLocalCanonicalIdsKeepExplicitRewardsDistinctFromEnumRowsWithoutMutatingAuthoring()
    {
        var localObject = new OwnedObject("owned-local", LootCardKind.SecondWind)
            { LootCardId = "pocket-breakfast", ItemTypeId = "second-wind" };
        var scenario = Scenario();
        scenario = scenario with
        {
            LootCardDefinitions = [new("pocket-breakfast", "Local envelope") { ItemTypeId = "second-wind" },
                new("local-loot:pocket-breakfast", "Already occupied alias") { EquipmentTypeId = "lucky-boots" }],
            Loot = new() { Composition = [new(LootCardKind.PocketBreakfast), new() { LootCardId = "pocket-breakfast" }] },
            Units = [scenario.Units[0] with { Inventory = [localObject] }],
            GroundObjects = [new(localObject with { Id = "ground-local" }, new(1, 0))],
            LootMarkers = [new("legacy", new(0, 0)), new("explicit", new(0, 0))]
        };
        var before = ScenarioDefinitionJson.ToJson(scenario);
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(loaded));
        var state = Ready(loaded);
        const string alias = "local-loot:local-loot:pocket-breakfast";
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(scenario));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(loaded));
        Assert.Equal("second-wind", state.LootCardDefinitions.Single(d => d.Id == alias).ItemTypeId);
        Assert.Equal("lucky-boots", state.LootCardDefinitions.Single(d => d.Id == "local-loot:pocket-breakfast").EquipmentTypeId);
        Assert.Equal(new[] { "pocket-breakfast", alias }, state.LootDeck!.DrawPile.Select(c => c.LootCardId));
        Assert.Equal(alias, Assert.Single(state.Units[0].Inventory).LootCardId);
        Assert.Equal("owned-local", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Equal(alias, Assert.Single(state.GroundObjects).Object.LootCardId);
        Assert.Equal("ground-local", Assert.Single(state.GroundObjects).Object.Id);
        var second = Ready(scenario);
        Assert.Equal(state.LootDeck.DrawPile, second.LootDeck!.DrawPile);
        var result = Collect(state);
        Assert.Equal(3, result.State.Units[0].CurrentHp);
        Assert.Equal(2, result.State.Units[0].Inventory.Length);
        Assert.Equal(alias, result.State.Units[0].Inventory.Last().LootCardId);
        Assert.Equal("pocket-breakfast", Assert.Single(result.Events.Where(e => e.Kind == "HealResolved")).ActionId);
    }

    [Fact]
    public void OldSavedLocalCanonicalIdDoesNotInterceptSourceLessLegacyCardsOrRewriteSavedReferences()
    {
        var state = Ready(Scenario());
        state.LootCardDefinitions = [new("pocket-breakfast", "Old local reward") { ItemTypeId = "second-wind" }];
        state.LootDeck = new()
        {
            DrawPile = [new("old-breakfast", LootCardKind.PocketBreakfast),
                new("old-local", LootCardKind.SecondWind) { LootCardId = "pocket-breakfast", ItemTypeId = "second-wind" }]
        };
        state.LootMarkers = [new("legacy", new(0, 0)), new("local", new(0, 0))];
        var saved = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<GameState>(saved)!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(saved, JsonSerializer.Serialize(restored));
        Assert.Equal("second-wind", Assert.Single(restored.LootCardDefinitions).ItemTypeId);
        var result = Collect(restored);
        Assert.Equal(3, result.State.Units[0].CurrentHp);
        var owned = Assert.Single(result.State.Units[0].Inventory);
        Assert.Equal("old-local", owned.Id); Assert.Equal("pocket-breakfast", owned.LootCardId);
        Assert.Equal("old-breakfast", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.Null(Assert.Single(result.State.LootDeck.DiscardPile).LootCardId);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Collect(JsonSerializer.Deserialize<GameState>(saved)!)));
        Assert.Equal("Pocket Breakfast", restored.ObjectContent(state.LootDeck.DrawPile[0]).Name);
        Assert.Equal("Second Wind", restored.ObjectContent(state.LootDeck.DrawPile[1]).Name);
    }

    [Fact]
    public void CompatibilityAliasesDoNotPermitDuplicateLocalDefinitions()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with
        {
            LootCardDefinitions = [new("pocket-breakfast", "First") { ItemTypeId = "second-wind" },
                new("pocket-breakfast", "Second") { EquipmentTypeId = "lucky-boots" }]
        }));
    }

    [Fact]
    public void CompatibilityAliasesDoNotMakeUnresolvedAuthoredReferencesValid()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with
        {
            LootCardDefinitions = [new("pocket-breakfast", "Local") { ItemTypeId = "second-wind" }],
            Loot = new() { Composition = [new() { LootCardId = "local-loot:pocket-breakfast" }] }
        }));
    }

    [Fact]
    public void LegacyItemMetadataAcceptsMultipleAndDifferentSupportedGrants()
    {
        var items = ItemCatalog.All.Select(i => i with
        { LocalAbilities = [], Grants = [new("heal", "heal-example"), new("actions", "actions-example")] }).ToArray();
        AbilityDefinition[] abilities = [.. AbilityCatalog.All, new("heal-example", "Restore", AbilityTiming.Action, new HealMechanic(2)),
            new("actions-example", "Again", AbilityTiming.BonusAction, new ExtraActionsMechanic(3))];
        var cards = LootCompatibility.ProjectLegacyCards(LootCardCatalog.All, items, EquipmentCatalog.All, abilities);
        var item = Assert.Single(cards.Where(c => c.Kind == LootCardKind.SecondWind));
        Assert.Contains("heal an adjacent damaged friendly Unit for 2 HP", item.Text);
        Assert.Contains("gain +3 Action this activation", item.Text);
        Assert.Equal(0, item.ExtraActions); // Legacy scalar is only meaningful for one extra-Actions grant.
    }

    [Fact]
    public void ImmediateLootCannotBecomeAnOwnedOrGroundObject()
    {
        var immediate = new OwnedObject("not-an-item") { LootCardId = "pocket-breakfast" };
        var scenario = Scenario();
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
        { Units = [scenario.Units[0] with { Inventory = [immediate] }] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
        { GroundObjects = [new(immediate, new(0, 0))] }));
    }

    [Theory]
    [InlineData("{\"kind\":\"healCollector\",\"amount\":0}")]
    [InlineData("{\"kind\":\"healCollector\",\"amount\":101}")]
    public void UnsupportedImmediateAmountsAreRejectedAtScenarioCreation(string mechanic)
    {
        var json = JsonNode.Parse(ScenarioDefinitionJson.ToJson(Scenario()))!;
        json["lootCardDefinitions"] = JsonNode.Parse($$"""[{"id":"invalid","name":"Invalid","immediate":{{mechanic}}}]""");
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void LootDefinitionsRejectUnknownMechanicsMissingAmountsAndConflictingRewards()
    {
        var definition = new LootCardDefinition("bad", "Bad") { Immediate = new HealCollectorMechanic(2), ItemTypeId = "second-wind" };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with { LootCardDefinitions = [definition] }));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LootCardDefinition>(
            """{"id":"bad","name":"Bad","immediate":{"kind":"executeScript"}}""", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LootCardDefinition>(
            """{"id":"bad","name":"Bad","immediate":{"kind":"healCollector"}}""", Json));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with
        { Loot = new() { Composition = [new() { LootCardId = "missing" }] } }));
    }
}
