using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class SummoningLootPublicationTests
{
    private const string Collector = "publication-collector";
    private const string Child = "publication-child";
    private const string Hidden = "publication-hidden";
    private const string Unused = "publication-unused";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllDeckSummonRewardsPublishCapturedLargeFootprintWithoutHiddenContent(bool item)
    {
        var fixture = Fixture(item);
        using var game = Open(fixture);
        var ready = BeforePickup(game);
        Assert.DoesNotContain(ready.Result.State.Units, u => u.TypeId == Child);
        Assert.Equal(2, ready.Result.State.LootDeck!.DrawPile.Count);
        var pickup = MoveToLoot(game, ready);
        var owned = Assert.Single(pickup.Result.State.Units.Single(u => u.Id == Collector).Inventory);
        Assert.Equal("a-summon-reward", owned.LootCardId);
        Assert.Equal(item ? "summoning-item" : null, owned.ItemTypeId);
        Assert.Equal(item ? null : "summoning-equipment", owned.EquipmentTypeId);
        Assert.Single(pickup.Result.State.LootDeck!.DrawPile);
        Assert.Contains(pickup.Result.Events, e => e.Kind == "LootCardDrawn" && e.ObjectCard?.Id == owned.Id);
        Assert.Equal(Footprint.TwoByTwo, pickup.Result.State.Types.Single(t => t.Id == Child).Footprint);

        // The authoritative captured scenario override must supply geometry even
        // though the public state has no first instance or definition of Child.
        var browser = game.BrowserResponse(pickup);
        var transported = JsonSerializer.Deserialize<GameResponse>(Serialize(browser), Json)!;
        Assert.DoesNotContain(transported.Result.State.Types, t => t.Id == Child || t.Id == Hidden || t.Id == Unused);
        Assert.DoesNotContain(transported.Result.State.Units, u => u.Id == "unseen-unit");
        Assert.Null(transported.Result.State.LootDeck);
        var payload = Serialize(browser);
        Assert.DoesNotContain(Hidden, payload);
        Assert.DoesNotContain(Unused, payload);
        Assert.DoesNotContain("z-unused-reward", payload);
        Assert.DoesNotContain("unused-equipment", payload);
        foreach (var summon in pickup.Result.NextInput!.Candidates.Where(c => c.Ability?.Key == "call"))
        {
            var anchor = summon.Destination!;
            var preview = Assert.Single(transported.Presentation.Decision!.Candidates, c => c.Key == summon.Key);
            Assert.Equal(new[] { anchor, new Cell(anchor.X + 1, anchor.Y),
                new Cell(anchor.X, anchor.Y + 1), new Cell(anchor.X + 1, anchor.Y + 1) }, preview.PlacementCells);
        }
        Assert.Contains(transported.Presentation.Decision!.Candidates, c => c.PlacementCells?.Count == 4);
    }

    [Fact]
    public void DetachedPickupReplayAndPendingResumePreservePhysicalRewardAndRemainingDeck()
    {
        var fixture = Fixture(false);
        using var game = Open(fixture);
        var before = BeforePickup(game);
        var saved = Serialize(before.Result.State);
        var expectedCard = before.Result.State.LootDeck!.DrawPile[0].Id;
        var expectedRemainder = before.Result.State.LootDeck.DrawPile.Skip(1).Select(c => c.Id).ToArray();
        using var replay = new PlaytestGame(new Dice(), DeserializeState(saved), canonicalContent: fixture.Content);
        var replayReady = replay.SetRelevanceAutoChoice(0, false);
        var first = MoveToLoot(game, before);
        var repeated = MoveToLoot(replay, replayReady);
        Assert.Equal(Serialize(first.Result), Serialize(repeated.Result));
        Assert.Equal(saved, Serialize(before.Result.State));
        Assert.Equal(expectedCard, Assert.Single(first.Result.State.Units.Single(u => u.Id == Collector).Inventory).Id);
        Assert.Equal(expectedRemainder, first.Result.State.LootDeck!.DrawPile.Select(c => c.Id));

        var pendingSave = Serialize(game.Snapshot().Result.State);
        using var resumed = new PlaytestGame(new Dice(), DeserializeState(pendingSave), canonicalContent: fixture.Content);
        var pending = resumed.SetRelevanceAutoChoice(0, false);
        var summon = first.Result.NextInput!.Candidates.First(c => c.Ability?.Key == "call");
        var originalAfter = game.Decide(first.Revision, summon.Key);
        var resumedAfter = resumed.Decide(pending.Revision, summon.Key);
        Assert.Equal(Serialize(originalAfter.Result), Serialize(resumedAfter.Result));
        Assert.Single(resumedAfter.Result.State.Units.Where(u => u.TypeId == Child));
        Assert.Equal(expectedCard, Assert.Single(resumedAfter.Result.State.Units.Single(u => u.Id == Collector).Inventory).Id);
        Assert.Equal(expectedRemainder, resumedAfter.Result.State.LootDeck!.DrawPile.Select(c => c.Id));
    }

    [Fact]
    public void InvalidDecisionAndMissingSummonReferenceLeavePublishedGameUnchanged()
    {
        var fixture = Fixture(false);
        using var game = Open(fixture);
        var ready = MoveToLoot(game, BeforePickup(game));
        var before = Serialize(game.Snapshot());
        Assert.Throws<PlaytestRequestException>(() => game.Decide(ready.Revision, "not-a-legal-placement"));
        Assert.Equal(before, Serialize(game.Snapshot()));
        var broken = fixture.Scenario with
        {
            EquipmentTypes = [fixture.Content.Equipment.Single(e => e.Id == "summoning-equipment") with
            {
                LocalAbilities = [new("call", "Call", AbilityTiming.Action,
                    new AdjacentUnitSummonMechanic("missing-child", Posture.Lying)) { Usage = new(2) }]
            }]
        };
        Assert.Throws<PlaytestRequestException>(() => game.StartTransportScenario(ready.Revision, ScenarioDefinitionTransport.Encode(broken)));
        Assert.Equal(before, Serialize(game.Snapshot()));
    }

    [Fact]
    public void FailureAfterFirstPickupRestoresLastStableState()
    {
        var fixture = Fixture(false);
        fixture.Scenario = fixture.Scenario with
        {
            LootMarkers = [new("first-marker", new(2, 1)), new("second-marker", new(2, 1))]
        };
        using var game = Open(fixture, new Dice(failOnLootDraw: 2));
        var before = BeforePickup(game);
        var saved = Serialize(before.Result.State);
        var error = Assert.Throws<InvalidOperationException>(() => MoveToLoot(game, before));
        Assert.Equal("Injected second Loot draw failure.", error.Message);
        var after = game.Snapshot();
        Assert.Equal(saved, Serialize(after.Result.State));
        Assert.Equal(2, after.Result.State.LootMarkers.Count);
        Assert.Equal(2, after.Result.State.LootDeck!.DrawPile.Count);
        Assert.Empty(after.Result.State.Units.Single(u => u.Id == Collector).Inventory);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static GameState DeserializeState(string json) => JsonSerializer.Deserialize<GameState>(json, Json)!;

    private static PlaytestGame Open((ScenarioDefinition Scenario, ContentBundle Content) fixture, Dice? dice = null)
    {
        var game = new PlaytestGame(dice ?? new Dice(), GameEngine.CreateGame(fixture.Scenario, fixture.Content),
            canonicalContent: fixture.Content, canonicalProvider: () => fixture.Content);
        game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(fixture.Scenario));
        return game;
    }

    private static GameResponse BeforePickup(PlaytestGame game)
    {
        var response = game.SetRelevanceAutoChoice(game.Snapshot().Revision, false);
        response = game.StartRound(response.Revision);
        if (response.Result.NextInput!.Kind == DecisionKind.SelectUnit)
            response = game.Decide(response.Revision, Collector);
        return response;
    }

    private static GameResponse MoveToLoot(PlaytestGame game, GameResponse response)
    {
        var move = Assert.Single(response.Result.NextInput!.Candidates,
            c => c.Kind == ActivationChoiceKind.Move && c.Destination == new Cell(2, 1));
        return game.Decide(response.Revision, move.Key);
    }

    private static (ScenarioDefinition Scenario, ContentBundle Content) Fixture(bool item)
    {
        var bearer = new UnitType("publication-bearer", 1, 0, 0, 0, 4)
            { Unique = true, CanHaveEquipment = true, CanHaveItems = true };
        var child = new UnitType(Child, 0, 0, 0, 0, 1);
        var ability = new LocalAbilityDefinition("call", "Call", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic(Child, Posture.Lying)) { Usage = new(2) };
        var equipment = new EquipmentDefinition("summoning-equipment", "Summoning Equipment") { LocalAbilities = [ability] };
        var bottle = new ItemDefinition("summoning-item", "Summoning Item") { LocalAbilities = [ability with { Usage = new(MaxUses: null) }] };
        var reward = new LootCardDefinition("a-summon-reward", "Summon Reward")
            { EquipmentTypeId = item ? null : equipment.Id, ItemTypeId = item ? bottle.Id : null };
        var content = new ContentBundle
        {
            Units = [bearer, child, new(Hidden, 0, 0, 0, 0, 1), new(Unused, 0, 0, 0, 0, 1)],
            Equipment = [equipment, new("unused-equipment", "Unused Equipment")], Items = [bottle],
            LootCards = [reward, new("z-unused-reward", "Unused Reward") { EquipmentTypeId = "unused-equipment" }],
            LootPresets = [new(LootPresets.AllPresetId, "All", "All test rewards", []) { IsDynamic = true }],
            Terrain = [new("stone-floor", "Test floor", true, false) { LegacyKind = TerrainKind.StoneFloor }]
        };
        var scene = Scenario.Define(Scenario.Map(20, 12),
        [
            Scenario.Group(bearer.Id, "blue", ControllerKind.Human, Scenario.At(1, 1, unitId: Collector)),
            Scenario.Group(Hidden, "red", ControllerKind.Human, Scenario.At(19, 11, unitId: "unseen-unit"))
        ]) with
        {
            UnitTypes = [child with { Footprint = Footprint.TwoByTwo }], FogOfWarSides = ["blue"],
            Loot = new() { Preset = LootPresets.AllPresetId }, LootMarkers = [new("summon-marker", new(2, 1))],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
        };
        return (scene, content);
    }

    private sealed class Dice(int? failOnLootDraw = null) : IRandomProvider
    {
        private int lootDraws;
        public int LootDrawIndex(int exclusiveMax)
        {
            if (++lootDraws == failOnLootDraw)
                throw new InvalidOperationException("Injected second Loot draw failure.");
            return 0;
        }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "publication-bearer") ?? bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
}
