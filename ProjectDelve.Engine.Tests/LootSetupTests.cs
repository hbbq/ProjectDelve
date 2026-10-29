using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LootSetupTests
{
    private static ScenarioDefinition Setup() => new()
    {
        Board = new() { Width = 20, Height = 3 },
        UnitTypeIds = ["collector"],
        UnitTypes = [new("collector", 3, 1, 1, 0, 1) { CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("collector", "blue", new(0, 0)) { Inventory = [new("boots", LootCardKind.LuckyBoots)] },
            new("collector", "red", new(19, 0)) { Inventory = [new("bell", LootCardKind.GoatBell)] }],
        Agency = [new("collector", "blue", ControllerKind.Human), new("collector", "red", ControllerKind.Automated)],
        Loot = new([LootCardKind.PocketBreakfast, LootCardKind.SecondWind]),
        LootMarkers = [new("one", new(1, 0)), new("two", new(1, 0))],
        GroundObjects = [new(new("key", LootCardKind.OfficialKey), new(19, 0))]
    };

    [Fact]
    public void RejectsMoreMarkersThanDrawCardsEvenWithKnownOrOwnedCards()
    {
        var definition = Setup() with { Loot = new([LootCardKind.PocketBreakfast]) };
        Assert.Contains("available draw card", Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition)).Message);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Loot = LootSettings.Empty }));
    }

    [Fact]
    public void SharedCellMarkersAreValidAndIndividualInventoriesAreIndependentOfDeck()
    {
        var state = GameEngine.CreateGame(Setup());
        Assert.Equal(2, state.LootMarkers.Count);
        Assert.Equal(2, state.LootDeck!.DrawPile.Count);
        Assert.Equal(LootCardKind.LuckyBoots, Assert.Single(state.Units[0].Inventory).Kind);
        Assert.Equal(LootCardKind.GoatBell, Assert.Single(state.Units[1].Inventory).Kind);
        var withoutDeck = GameEngine.CreateGame(Setup() with { Loot = LootSettings.Empty, LootMarkers = null });
        Assert.Single(withoutDeck.Units[0].Inventory);
        Assert.Single(withoutDeck.GroundObjects);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void RejectsInvalidDropChances(int chance)
    {
        var definition = Setup();
        definition.UnitTypes![0] = definition.UnitTypes[0] with { LootDropSuccesses = chance };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Fact]
    public void OwnershipCapabilitiesAreIndependentAndImmediateCardsCannotBeOwned()
    {
        var definition = Setup();
        definition.UnitTypes![0] = definition.UnitTypes[0] with { CanHaveItems = false };
        GameEngine.CreateGame(definition); // Equipment is still allowed.
        definition.Units[0] = definition.Units[0] with { Inventory = [new("item", LootCardKind.SecondWind)] };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        definition.UnitTypes[0] = definition.UnitTypes[0] with { CanHaveItems = true };
        GameEngine.CreateGame(definition);
        definition.Units[0] = definition.Units[0] with { Inventory = [new("food", LootCardKind.PocketBreakfast)] };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Fact]
    public void RejectsDuplicateObjectIdentityAcrossOwnersAndGround()
    {
        var definition = Setup();
        definition.GroundObjects![0] = new(new("boots", LootCardKind.OfficialKey), new(1, 0));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Fact]
    public void TransportAndStateSerializationPreserveCopiesAndOwnership()
    {
        var scenario = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Setup()));
        var state = GameEngine.CreateGame(scenario);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.LootMarkers, restored.LootMarkers);
        Assert.Equal(state.LootDeck!.DrawPile, restored.LootDeck!.DrawPile);
        Assert.Equal(state.Units[0].Inventory.ToArray(), restored.Units[0].Inventory.ToArray());
        var copy = state.Copy();
        copy.LootDeck!.DrawPile.Clear(); copy.LootMarkers.Clear(); copy.GroundObjects.Clear();
        Assert.Equal(2, state.LootDeck.DrawPile.Count);
        Assert.Equal(2, state.LootMarkers.Count);
        Assert.Single(state.GroundObjects);
    }

    [Fact]
    public void ObservationsHideDrawPileAndUnexploredObjectsAndInventories()
    {
        var definition = Setup() with { FogOfWarSides = ["blue"] };
        definition.LootMarkers![1] = new("two", new(19, 1));
        var state = GameEngine.CreateGame(definition);
        var observation = FogOfWar.Observe(state, "blue");
        Assert.Null(observation.LootDeck);
        Assert.Single(observation.LootMarkers);
        Assert.Empty(observation.GroundObjects);
        Assert.Single(observation.Units);
        Assert.Equal("boots", Assert.Single(observation.Units[0].Inventory).Id);
        Assert.Null(FogOfWar.Observe(state, null).LootDeck);
        Assert.Equal(2, state.LootDeck!.DrawPile.Count);
    }
}
