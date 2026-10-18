using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ResourceProjectionTests
{
    [Fact]
    public void LavaGenerationProjectsRollPlacementsAndDetachedPlaybackStates()
    {
        var definition = Scenario.Define(Scenario.Map(5, 4, cells: [Scenario.Tile(1, 1, TerrainKind.Lava)]),
            [Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(0, 0)),
                Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(4, 3))]);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new FirstDice(), false);
        var browser = BrowserProjection.Create(result);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "LavaCoalRolled"));
        Assert.Contains(browser.Events, e => e.Text.Contains("Lava at 1,1: rolled d8 8"));
        Assert.Contains(browser.Events, e => e.Text.Contains("Coal created at 0,0"));
        var occurrence = Assert.Single(result.Events, e => e.Kind == "ResourceCreated");
        var projected = GambitProjection.Event(occurrence);
        Assert.Equal(ResourceTypes.Coal, projected.ResourceType);
        Assert.Equal(new Cell(0, 0), projected.Cell);
        var snapshot = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ResourceCreated");
        Assert.Single(snapshot.StateAfter.Resources);
        Assert.Empty(snapshot.StateAfter.Bag);
        var observation = GambitProjection.Observation(result.State);
        Assert.Equal(result.State.Resources, observation.Resources);
        result.State.Resources.Clear();
        Assert.Single(snapshot.StateAfter.Resources);
        Assert.Single(observation.Resources);
    }

    [Fact]
    public void GeomancerSummonsProjectChoicesPlacementsCardsAndAtomicStorageSnapshots()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 5, Height = 5 },
            UnitTypeIds = [UnitTypeIds.Geomancer, UnitTypeIds.Grunt],
            Units = [new(UnitTypeIds.Geomancer, "blue", new(2, 2), UnitId: "geo"),
                new(UnitTypeIds.Grunt, "red", new(4, 4), UnitId: "enemy")],
            Agency = [new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human),
                new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
            StoredResources = [new("blue", UnitTypeIds.Geomancer, ResourceTypes.Diamond, 1)]
        };
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var start = GameEngine.StartRound(GameEngine.CreateGame(decoded), new FirstDice(), false);
        var ready = GameEngine.Advance(start.State, new Select("stay"), new FirstDice(), false);
        var browser = BrowserProjection.Create(ready);
        foreach (var id in new[] { "summon-worker", "summon-troll" })
        {
            Assert.Contains(browser.Cards["geo"].Entries, e => e.Content.Id == id);
            var choice = browser.Decision!.Candidates.Single(c => c.Key == $"{id}:1,1");
            Assert.Equal(id, choice.EntryId);
            Assert.Equal(InteractionKind.Position, choice.Interaction.Kind);
            Assert.Equal(new Cell(1, 1), choice.Interaction.Position);
            Assert.Equal(new[] { new Cell(1, 1) }, choice.PlacementCells);
        }
        var summoned = GameEngine.Advance(ready.State, new Select("summon-troll:1,1"), new FirstDice(), false);
        var projected = BrowserProjection.Create(summoned);
        var snapshot = projected.ResolutionSteps.Single(s => summoned.Events[s.EventIndex].Kind == "ResourceSpent");
        Assert.Equal(0, snapshot.Cards["geo"].ResourceStorage.Single().Quantity);
        var createdId = Assert.Single(summoned.Events, e => e.Kind == "UnitCreated").UnitId!;
        Assert.Contains(createdId, snapshot.Cards.Keys);
        Assert.Contains(projected.Events, e => e.Text.Contains("spent Diamond"));
    }

    [Fact]
    public void ResourceScenarioPreservesAuthoredMarkersAndRestartsFresh()
    {
        var definition = WebScenarioFixtures.Resources();
        var state = GameEngine.CreateGame(WebScenarioFixtures.Resources());
        Assert.NotEmpty(state.Resources);
        Assert.Equal(definition.Resources, state.Resources);
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(state.Resources, GameEngine.CreateGame(decoded).Resources);
        state.Resources.Clear();
        Assert.Equal(definition.Resources, GameEngine.CreateGame(WebScenarioFixtures.Resources()).Resources);
    }

    [Fact]
    public void BrowserAndGambitDeliverAuthoritativeResourcesAndCapability()
    {
        var state = GameEngine.CreateGame(WebScenarioFixtures.Resources());
        var observed = GambitProjection.Observation(state);
        Assert.Equal(state.Resources, observed.Resources);
        state.Resources.Clear();
        Assert.Equal(WebScenarioFixtures.Resources().Resources, observed.Resources);
        var browser = new GameResponse(1, new(GameEngine.CreateGame(WebScenarioFixtures.Resources()), [], null), false, "fixture-resources");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(browser));
        Assert.Equal(observed.Resources.Count, json.RootElement.GetProperty("Result").GetProperty("State").GetProperty("Resources").GetArrayLength());
        var carrierSetup = GameEngine.CreateGame(Scenario.Define(Scenario.Map(2, 2),
            [Scenario.Group(UnitTypeIds.Zombie, "red", ControllerKind.Human, Scenario.At(0, 0, unitId: "carrier"))]));
        Assert.Contains(BrowserProjection.Cards(carrierSetup)["carrier"].Entries, entry => entry.Content.Id == "transport-resource");
    }
    [Fact]
    public void StorageChoicesCardsEventsAndSnapshotsProjectAuthoritativeState()
    {
        var shaman = new UnitType("store", 2, 0, 0, 3, 1, UnitAction.PickupResource)
        { ResourceStorage = new([new(ResourceTypes.Coal, 3)]) };
        var courier = new UnitType("courier", 2, 1, 0, 0, 1, UnitAction.GiveResource);
        var state = new GameState
        {
            Types = [courier, shaman],
            Units = [courier.CreateUnit("giver", "blue"), shaman.CreateUnit("one", "blue"),
                shaman.CreateUnit("two", "blue"), shaman.CreateUnit("enemy", "red")],
            Physical = new(new Board(6, 5, []), [new("giver", new(1, 1)), new("one", new(2, 1)),
                new("two", new(2, 2)), new("enemy", new(5, 4))]),
            Controllers = [new(new(courier.Id, "blue"), ControllerKind.Human),
                new(new(shaman.Id, "blue"), ControllerKind.Human), new(new(shaman.Id, "red"), ControllerKind.Human)],
            Resources = [new(ResourceTypes.Coal, new(1, 1))]
        };
        var start = GameEngine.StartRound(state, new FirstDice(), false);
        var active = start.NextInput!.Kind == DecisionKind.SelectUnit
            ? GameEngine.Advance(start.State, new Select("giver"), new FirstDice(), false) : start;
        var choices = GameEngine.Advance(active.State, new Select("stay"), new FirstDice(), false);
        var decision = BrowserProjection.Create(choices).Decision!;
        var give = decision.Candidates.Single(c => c.Key == "give-resource:one");
        Assert.Equal("give-resource", give.EntryId);
        Assert.Equal(InteractionKind.Unit, give.Interaction.Kind);
        Assert.Equal("one", give.Interaction.UnitId);
        var given = GameEngine.Advance(choices.State, new Select(give.Key!), new FirstDice(), false);
        var browser = BrowserProjection.Create(given);
        Assert.Equal(1, browser.Cards["one"].ResourceStorage.Single().Quantity);
        Assert.Equal(1, browser.Cards["two"].ResourceStorage.Single().Quantity);
        Assert.Equal(0, browser.Cards["enemy"].ResourceStorage.Single().Quantity);
        Assert.Equal(3, browser.Cards["one"].ResourceStorage.Single().Capacity);
        Assert.Contains(browser.Events, e => e.Text.Contains("Coal") && e.Text.Contains("1 stored"));
        var snapshot = browser.ResolutionSteps.Single(s => given.Events[s.EventIndex].Kind == "ResourceStored");
        Assert.Equal(1, snapshot.Cards["two"].ResourceStorage.Single().Quantity);
        var observed = GambitProjection.Observation(given.State);
        Assert.Equal(given.State.StoredResources, observed.StoredResources);
        var ev = GambitProjection.Event(given.Events.Single(e => e.Kind == "ResourceStored"));
        Assert.Equal(ResourceTypes.Coal, ev.ResourceType);
        Assert.Equal(1, ev.StoredQuantity);
        given.State.StoredResources.Clear();
        Assert.Single(observed.StoredResources);
        Assert.Equal(1, snapshot.Cards["two"].ResourceStorage.Single().Quantity);
    }

    private sealed class FirstDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
        public int RollD8() => 8;
    }
    private sealed class Select(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
}
