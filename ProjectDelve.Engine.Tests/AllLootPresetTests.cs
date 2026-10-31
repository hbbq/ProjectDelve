using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AllLootPresetTests
{
    [Fact]
    public void AllDerivesOneTypedRowPerEffectiveDefinitionInOrdinalOrder()
    {
        var preset = LootPresets.Find(LootPresets.AllPresetId);
        Assert.True(preset.IsDynamic);
        Assert.Empty(preset.Composition);
        var canonical = LootPresets.Resolve(preset.Id);
        Assert.Equal(LootCardCatalog.All.Select(card => card.Id).Order(StringComparer.Ordinal), canonical.Select(row => row.LootCardId));
        Assert.All(canonical, row =>
        {
            Assert.Equal(1, row.Count);
            Assert.Null(row.EquipmentTypeId);
            Assert.Null(row.ItemTypeId);
        });

        LootCardDefinition[] local =
        [
            new("z-reward", "Same reward, later card") { EquipmentTypeId = "goat-bell" },
            new("A-reward", "Same reward, separate card") { EquipmentTypeId = "goat-bell" },
            new("middle-reward", "Lunch") { Immediate = new HealCollectorMechanic(7) }
        ];
        var effective = LootCardCatalog.Effective(local);
        var settings = new LootSettings { Preset = preset.Id };
        var rows = settings.ResolveComposition(effective);
        Assert.Equal(effective.Select(card => card.Id).Order(StringComparer.Ordinal), rows.Select(row => row.LootCardId));
        Assert.Equal(LootCardCatalog.All.Count + local.Length, rows.Count);
        Assert.Equal(rows, settings.ResolveComposition(LootCardCatalog.Effective(local.Reverse())));
        var objects = settings.ResolveObjects(effective);
        Assert.Equal(rows.Select(row => row.LootCardId), objects.Select(card => card.LootCardId));
        Assert.Equal(Enumerable.Range(1, objects.Count).Select(index => $"loot-card-{index}"), objects.Select(card => card.Id));
        Assert.Equal(2, objects.Count(card => card.LootCardId is "A-reward" or "z-reward" && card.EquipmentTypeId == "goat-bell"));
    }

    [Fact]
    public void SetupExcludesShadowedCanonicalCardsAndAliasesSelectedLocalReferencesWithoutChangingSource()
    {
        var replacement = new LootCardDefinition("pocket-breakfast", "Historical local replacement") { EquipmentTypeId = "official-key" };
        var extra = new LootCardDefinition("local-loot:pocket-breakfast", "Independent local card") { Immediate = new HealCollectorMechanic(3) };
        var scenario = Scenario() with { LootCardDefinitions = [replacement, extra] };
        var sourceJson = ScenarioDefinitionJson.ToJson(scenario);
        var effective = LootCardCatalog.Effective(scenario.LootCardDefinitions);
        Assert.Same(replacement, Assert.Single(effective.Where(card => card.Id == replacement.Id)));
        var state = GameEngine.CreateGame(scenario);
        var resolvedReplacement = state.LootCardDefinitions.Single(card => card.Name == replacement.Name);
        Assert.NotEqual(replacement.Id, resolvedReplacement.Id);
        Assert.NotEqual(extra.Id, resolvedReplacement.Id);
        Assert.Equal(effective.Count, state.LootDeck!.DrawPile.Count);
        Assert.DoesNotContain(state.LootDeck.DrawPile, card => card.LootCardId == replacement.Id);
        var reward = Assert.Single(state.LootDeck.DrawPile.Where(card => card.LootCardId == resolvedReplacement.Id));
        Assert.Equal(replacement.EquipmentTypeId, reward.EquipmentTypeId);
        Assert.Contains(state.LootDeck.DrawPile, card => card.LootCardId == extra.Id);
        Assert.Contains(state.LootCardDefinitions, card => card.Id == replacement.Id); // Historical references remain representable.
        Assert.Equal(sourceJson, ScenarioDefinitionJson.ToJson(scenario));
        Assert.Equal(LootPresets.AllPresetId, scenario.Loot!.Preset);
        Assert.Null(scenario.Loot.Composition);

        // A fixed preset still selects its established canonical cards.
        var fixedState = GameEngine.CreateGame(scenario with { Loot = new() { Preset = "Classic" } });
        Assert.Contains(fixedState.LootDeck!.DrawPile, card => card.LootCardId == replacement.Id);
        Assert.DoesNotContain(fixedState.LootDeck.DrawPile, card => card.LootCardId == resolvedReplacement.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void LiveAllReferenceRoundTripsAndDoesNotTreatUnreferencedObjectsAsLootCards(int? multiplier)
    {
        var scenario = Scenario() with
        {
            Loot = new() { Preset = LootPresets.AllPresetId, PresetMultiplier = multiplier },
            EquipmentTypes = [new("not-a-loot-card", "Unreferenced Equipment")],
            ItemTypes = [new("also-not-a-loot-card", "Unreferenced Item") { LocalAbilities = [new("use", "Use", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] }],
            LootCardDefinitions = [new("local-card", "Local card") { Immediate = new HealCollectorMechanic(3) }]
        };
        var json = ScenarioDefinitionJson.ToJson(scenario);
        var loot = JsonNode.Parse(json)!["loot"]!;
        Assert.Equal(LootPresets.AllPresetId, loot["preset"]!.GetValue<string>());
        Assert.Null(loot["composition"]);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario));
        Assert.Equal(multiplier, restored.Loot!.PresetMultiplier);
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(LootCardCatalog.All.Count + 1, state.LootDeck!.DrawPile.Count);
        Assert.DoesNotContain(state.LootDeck.DrawPile, card => card.EquipmentTypeId == "not-a-loot-card" || card.ItemTypeId == "also-not-a-loot-card");
    }

    [Fact]
    public void AllRejectsScalingDuplicateDefinitionsAndExcessReservations()
    {
        Assert.Throws<ArgumentException>(() => new LootSettings { Preset = LootPresets.AllPresetId, PresetMultiplier = 2 }.ResolveComposition());
        var duplicate = new LootCardDefinition("duplicate", "Duplicate") { Immediate = new HealCollectorMechanic(1) };
        Assert.Throws<ArgumentException>(() => LootCardCatalog.Effective([duplicate, duplicate]));
        Assert.Throws<ArgumentException>(() => LootPresets.Resolve(LootPresets.AllPresetId, [duplicate, duplicate]));
        var scenario = Scenario() with
        {
            LootMarkers = Enumerable.Range(0, LootCardCatalog.All.Count + 1).Select(index => new LootMarker($"marker-{index}", new(1, 1))).ToList()
        };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario));
    }

    [Fact]
    public void ExistingRandomDrawPathUsesResolvedOrderAndResumeRetainsTheFiniteDeck()
    {
        var extra = new LootCardDefinition("zzzz-local-lunch", "Local lunch") { Immediate = new HealCollectorMechanic(3) };
        var scenario = Scenario() with { LootCardDefinitions = [extra], LootMarkers = [new("arrival-loot", new(0, 0))] };
        var initial = GameEngine.CreateGame(scenario);
        var expected = initial.LootDeck!.DrawPile.Last();
        var random = new LastCardRandom();
        var result = GameEngine.StartRound(initial, random, false);
        var drawn = Assert.Single(result.Events.Where(item => item.Kind == "LootCardDrawn"));
        Assert.Equal(expected, drawn.ObjectCard);
        Assert.Equal(new[] { initial.LootDeck.DrawPile.Count }, random.DrawSizes);
        Assert.Equal(initial.LootDeck.DrawPile.Count - 1, result.State.LootDeck!.DrawPile.Count);
        Assert.Equal(3, result.State.Units.Single().CurrentHp);

        // A saved catalog may contain additional declarations; resume retains
        // the actual finite draw/discard piles rather than re-resolving All.
        result.State.LootCardDefinitions.Add(new("later-definition", "Later definition") { Immediate = new HealCollectorMechanic(1) });
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        Assert.Equal(result.State.LootDeck.DrawPile, restored.LootDeck!.DrawPile);
        Assert.Equal(result.State.LootDeck.DiscardPile, restored.LootDeck.DiscardPile);
        Assert.DoesNotContain(restored.LootDeck.DrawPile, card => card.LootCardId == "later-definition");
        restored.LootDeck.DiscardPile.AddRange(restored.LootDeck.DrawPile);
        restored.LootDeck.DrawPile.Clear();
        Assert.Empty(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(restored))!.LootDeck!.DrawPile);
        var fresh = GameEngine.CreateGame(scenario with { LootCardDefinitions = [extra, new("later-definition", "Later definition") { Immediate = new HealCollectorMechanic(1) }] });
        Assert.Equal(initial.LootDeck.DrawPile.Count + 1, fresh.LootDeck!.DrawPile.Count);
    }

    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["collector"],
        UnitTypes = [new("collector", 2, 1, 1, 0, 3) { Unique = true, CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("collector", "blue", new(0, 0), InitialHp: 1, UnitId: "collector-1")],
        Agency = [new("collector", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Loot = new() { Preset = LootPresets.AllPresetId }
    };

    private sealed class LastCardRandom : IRandomProvider
    {
        public List<int> DrawSizes { get; } = [];
        public int LootDrawIndex(int exclusiveMax) { DrawSizes.Add(exclusiveMax); return exclusiveMax - 1; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
