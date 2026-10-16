using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ResourceStorageTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "courier") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState World(bool give = false)
    {
        var store = new UnitType("store", 3, 1, 0, 0, 1, UnitAction.PickupResource)
        {
            ResourceStorage = new([new(ResourceTypes.Coal, 3)]), TransportResource = new()
        };
        var courier = new UnitType("courier", 3, 1, 0, 0, 1, UnitAction.GiveResource) { TransportResource = new() };
        var actor = give ? courier : store;
        var state = new GameState
        {
            Types = [store, courier],
            Units = [actor.CreateUnit("actor", "blue"), store.CreateUnit("friend", "blue"),
                store.CreateUnit("other", "red")],
            Physical = new(new Board(7, 5, []), [new("actor", new(1, 1)), new("friend", new(2, 1)), new("other", new(5, 3))])
        };
        TestGame.Author(state);
        state.PlaceResource(ResourceTypes.Coal, new(1, 1));
        return state;
    }
    private static EngineResult Start(GameState state) => TestGame.StartRound(state, new Dice(), false);
    private static EngineResult Pick(EngineResult result, string key) => TestGame.Advance(result.State, new Choice(key), new Dice(), false);
    // A shared store has two eligible figures: explicitly choose the actor.
    private static EngineResult Actor(GameState state, bool finishMove = true)
    {
        var start = Start(state);
        if (start.NextInput!.Kind == DecisionKind.SelectUnit) start = Pick(start, "actor");
        return finishMove && start.NextInput!.Candidates.Any(c => c.Key == "stay") ? Pick(start, "stay") : start;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferIsAtomicUsesOrdinaryActionAndNeverMoves(bool give)
    {
        var world = World(give);
        var start = Actor(world);
        var result = Pick(start, give ? "give-resource:friend" : "pickup-resource");
        Assert.Empty(result.State.Resources);
        Assert.Single(start.State.Resources);
        Assert.Single(world.Resources);
        Assert.Equal(1, result.State.StoredQuantity("blue", "store", "coal"));
        Assert.Equal(0, result.State.StoredQuantity("red", "store", "coal"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
        Assert.Contains(result.Events, e => e.Kind == "ActionUsed" && e.Category == ActivationChoiceKind.Action);
        var ev = Assert.Single(result.Events.Where(e => e.Kind == "ResourceStored"));
        Assert.Equal(ResourceTypes.Coal, ev.ResourceType);
        Assert.Equal(1, ev.StoredQuantity);
        Assert.Equal(give ? "friend" : "actor", ev.TargetId);
        var step = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ResourceStored");
        Assert.Empty(step.StateAfter.Resources);
        Assert.Equal(1, step.StateAfter.StoredQuantity("blue", "store", "coal"));
        Assert.Equal(start.State.Physical.Figures, step.StateAfter.Physical.Figures);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action is UnitAction.PickupResource or UnitAction.GiveResource);
        result.State.StoredResources.Clear();
        Assert.Single(step.StateAfter.StoredResources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapacityIsSharedAndLastSlotIsLegal(bool give)
    {
        var world = World(give);
        world.StoredResources.Add(new("blue", "store", ResourceTypes.Coal, 2));
        var result = Pick(Actor(world), give ? "give-resource:friend" : "pickup-resource");
        Assert.Equal(3, result.State.StoredQuantity("blue", "store", "coal"));
        var fullWorld = World();
        fullWorld.StoredResources = [.. result.State.StoredResources];
        fullWorld.Resources[0] = new(ResourceTypes.Coal, new(2, 1));
        var friend = Pick(Pick(Start(fullWorld), "friend"), "stay");
        Assert.DoesNotContain(friend.NextInput!.Candidates, c => c.Action == UnitAction.PickupResource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableTransfersLeaveBothLocationsUntouched(bool give)
    {
        foreach (var reason in new[] { "full", "wrong-type", "no-storage", "no-action", "no-resource" })
        {
            var world = World(give);
            if (reason == "full") world.StoredResources.Add(new("blue", "store", ResourceTypes.Coal, 3));
            if (reason == "wrong-type") world.Resources[0] = new(new("iron", "Iron"), new(1, 1));
            if (reason == "no-storage") world.Types[0] = world.Types[0] with { ResourceStorage = null };
            if (reason == "no-action") world.Types[give ? 1 : 0] = world.Types[give ? 1 : 0] with { Actions = UnitAction.None };
            if (reason == "no-resource") world.Resources.Clear();
            var start = Actor(world);
            Assert.DoesNotContain(start.NextInput!.Candidates, c => c.Action is UnitAction.PickupResource or UnitAction.GiveResource);
            Assert.Throws<ArgumentException>(() => Pick(start, give ? "give-resource:friend" : "pickup-resource"));
            Assert.Equal(world.Resources, start.State.Resources);
            Assert.Equal(world.StoredResources, start.State.StoredResources);
        }
    }

    [Fact]
    public void GiveUsesNormalFriendlyAdjacencyAndOffersEachRecipient()
    {
        var world = World(true);
        world.PlaceUnit("store", "second", "blue", new(1, 2));
        Assert.Equal(new[] { "friend", "second" }, Actor(world).NextInput!.Candidates
            .Where(c => c.Action == UnitAction.GiveResource).Select(c => c.TargetId));
        world.Units[1] = world.Units[1] with { SideId = "red" };
        Assert.DoesNotContain(Actor(world).NextInput!.Candidates, c => c.TargetId == "friend");
        world.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.Wall));
        Assert.DoesNotContain(Actor(world).NextInput!.Candidates, c => c.Action == UnitAction.GiveResource);
    }

    [Fact]
    public void IndependentSidesAndTypesAndMoveTransportDoNotChangeStores()
    {
        var world = World(true);
        world.Types[1] = world.Types[1] with { ResourceStorage = new([new(ResourceTypes.Coal, 2)]) };
        world.StoredResources = [new("blue", "store", ResourceTypes.Coal, 3), new("red", "store", ResourceTypes.Coal, 1),
            new("blue", "courier", ResourceTypes.Coal, 1)];
        var moved = Pick(Actor(world, false), "1,2");
        Assert.Equal(new(1, 2), moved.State.Resources.Single().Position);
        Assert.Equal(world.StoredResources, moved.State.StoredResources);
        world.Units[0] = world.Units[0] with { SideId = "red" };
        world.Physical.Figures[0] = new("actor", new(4, 3));
        world.Resources[0] = new(ResourceTypes.Coal, new(4, 3));
        var given = Pick(Actor(world), "give-resource:other");
        Assert.Equal(2, given.State.StoredQuantity("red", "store", "coal"));
        Assert.Equal(3, given.State.StoredQuantity("blue", "store", "coal"));
    }

    [Fact]
    public void GiveRecipientNeedsStorageButDoesNotNeedPickupOrUprightPosture()
    {
        var world = World(true);
        world.Types[0] = world.Types[0] with { Actions = UnitAction.None };
        world.Physical.Figures[1] = world.Physical.Figures[1] with { Posture = Posture.Lying };
        var result = Pick(Actor(world), "give-resource:friend");
        Assert.Empty(result.State.Resources);
        Assert.Equal(1, result.State.StoredQuantity("blue", "store", "coal"));
    }

    [Fact]
    public void TransportThenPickupUsesTheResourceAtTheFinalCell()
    {
        var moved = Pick(Actor(World(), false), "1,2");
        Assert.Equal(new(1, 2), moved.State.Resources.Single().Position);
        var picked = Pick(moved, "pickup-resource");
        Assert.Empty(picked.State.Resources);
        Assert.Equal(1, picked.State.StoredQuantity("blue", "store", "coal"));
        Assert.DoesNotContain(picked.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void ScenarioAndStateRoundTripStorageAndCustomActions()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 4 }, UnitTypeIds = ["store", "courier"],
            UnitTypes = World(true).Types,
            Units = [new("store", "blue", new(1, 1)), new("courier", "blue", new(2, 1))],
            Agency = [new("store", "blue", ControllerKind.Human), new("courier", "blue", ControllerKind.Human)],
            StoredResources = [new("blue", "store", ResourceTypes.Coal, 2)],
            Resources = [new(ResourceTypes.Coal, new(2, 1))]
        };
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(decoded);
        Assert.Equal(2, state.StoredQuantity("blue", "store", "coal"));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.StoredResources, restored.StoredResources);
        Assert.True(restored.Types[1].Actions.HasFlag(UnitAction.GiveResource));
        state.StoredResources.Clear();
        Assert.Equal(2, GameEngine.CreateGame(definition).StoredQuantity("blue", "store", "coal"));
        Assert.Empty(GameEngine.CreateGame(definition with { StoredResources = null }).StoredResources);
    }

    [Fact]
    public void InvalidStorageContentAndQuantitiesAreRejected()
    {
        foreach (var quantity in new[] { -1, 4 })
        {
            var world = World();
            world.StoredResources.Add(new("blue", "store", ResourceTypes.Coal, quantity));
            Assert.Throws<ArgumentException>(() => Start(world));
        }
        var invalid = World();
        invalid.StoredResources = [new("blue", "store", new("iron", "Iron"), 1)];
        Assert.Throws<ArgumentException>(() => Start(invalid));
        invalid = World();
        invalid.StoredResources = [new("blue", "store", ResourceTypes.Coal, 1), new("blue", "store", ResourceTypes.Coal, 1)];
        Assert.Throws<ArgumentException>(() => Start(invalid));
        invalid = World();
        invalid.Types[0] = invalid.Types[0] with { ResourceStorage = new([new(ResourceTypes.Coal, -1)]) };
        Assert.Throws<ArgumentException>(() => Start(invalid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargerFiguresChooseOneResourceFromTheirOccupiedCells(bool give)
    {
        var world = World(give);
        world.Types[give ? 1 : 0] = world.Types[give ? 1 : 0] with { Footprint = Footprint.TwoByTwo };
        world.Physical.Figures[1] = new("friend", new(give ? 3 : 4, 1));
        world.Resources = [new(ResourceTypes.Coal, new(1, 2)), new(ResourceTypes.Coal, new(2, 2))];
        var start = Actor(world);
        var candidates = start.NextInput!.Candidates.Where(c => c.Action ==
            (give ? UnitAction.GiveResource : UnitAction.PickupResource)).ToArray();
        Assert.Equal(2, candidates.Length);
        var choice = candidates.Single(c => c.Destination == new Cell(2, 2));
        var result = Pick(start, choice.Key);
        Assert.Equal(new(1, 2), result.State.Resources.Single().Position);
        Assert.Equal(1, result.State.StoredQuantity("blue", "store", "coal"));
    }

    [Fact]
    public void StaleChoicesAreRevalidatedWithoutChangingBoardOrStorage()
    {
        var pending = Actor(World(true));
        Assert.Contains(pending.NextInput!.Candidates, c => c.Key == "give-resource:friend");
        pending.State.StoredResources.Add(new("blue", "store", ResourceTypes.Coal, 3));
        var result = Pick(pending, "give-resource:friend");
        Assert.DoesNotContain(result.Events, e => e.Kind == "ResourceStored");
        Assert.Single(result.State.Resources);
        Assert.Single(pending.State.Resources);
        Assert.Equal(3, pending.State.StoredQuantity("blue", "store", "coal"));
    }

    [Fact]
    public void EachAcceptedTypeHasItsOwnCapacityAndStorageDoesNotGrantActions()
    {
        var iron = new ResourceType("iron", "Iron");
        var world = World();
        world.Types[0] = world.Types[0] with { ResourceStorage = new([new(ResourceTypes.Coal, 3), new(iron, 1)]) };
        world.StoredResources.Add(new("blue", "store", ResourceTypes.Coal, 3));
        world.Resources[0] = new(iron, new(1, 1));
        var picked = Pick(Actor(world), "pickup-resource");
        Assert.Equal(1, picked.State.StoredQuantity("blue", "store", "iron"));
        Assert.Equal(3, picked.State.StoredQuantity("blue", "store", "coal"));
        world.Types[0] = world.Types[0] with { Actions = UnitAction.None };
        Assert.DoesNotContain(Actor(world).NextInput!.Candidates, c => c.Action == UnitAction.PickupResource);
    }

    [Fact]
    public void AutomatedSummonerDoesNotCollectWhenNoSummonPlacementExists()
    {
        var world = World();
        world.Types[0] = UnitRoster.Shaman() with { Id = "store" };
        // Surround the actor with Water while leaving its own Cell legal.
        for (var y = 0; y <= 2; y++)
        for (var x = 0; x <= 2; x++)
            if (x != 1 || y != 1) world.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.Water));
        // Keep the existing friendly figure on legal terrain outside the enclosure.
        world.Physical.Figures[1] = new("friend", new(4, 1));
        var start = Actor(world);
        Assert.Contains(start.NextInput!.Candidates, c => c.Action == UnitAction.PickupResource);
        Assert.Equal("end-turn", new DefaultAutomatedProvider().Choose(start.NextInput!, new GameplayQueries(start.State)));
    }

    [Fact]
    public void ShamanKeepsExistingBehaviorAndSummonAlongsidePickup()
    {
        var shaman = UnitRoster.Shaman();
        Assert.Equal(3, shaman.ResourceStorage!.Capacities.Single().Capacity);
        Assert.Equal(UnitAction.SummonAdjacent | UnitAction.PickupResource, shaman.Actions);
        Assert.Equal(UnitBehavior.Flee | UnitBehavior.UseSummon, shaman.Behaviors);
        Assert.Contains(shaman.CardEntries(), e => e.Id == "pickup-resource");
        var world = World();
        world.Types[0] = shaman with { Id = "store" };
        var start = Actor(world);
        Assert.StartsWith("spawn-goblin:", new DefaultAutomatedProvider().Choose(start.NextInput! with
        { Kind = DecisionKind.Act, AllowsNone = true, Candidates = start.NextInput!.Candidates.Where(c => c.Action is not null).ToList() }, new GameplayQueries(start.State)));
    }
}
