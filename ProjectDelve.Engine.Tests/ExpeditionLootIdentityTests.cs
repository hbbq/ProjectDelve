using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionLootIdentityTests
{
    [Fact]
    public void FreshDeckSkipsReservedIdsInDeterministicOrderWithoutReducingSupply()
    {
        var settings = new LootSettings { Composition = [new(LootCardKind.SecondWind, 3)] };
        var cards = settings.ResolveObjects(reservedIds: ["loot-card-1", "loot-card-3", "unrelated"]);
        Assert.Equal(new[] { "loot-card-2", "loot-card-4", "loot-card-5" }, cards.Select(c => c.Id));
        Assert.All(cards, c => Assert.Equal("second-wind", c.ItemTypeId));
        Assert.Equal(new[] { "loot-card-1", "loot-card-2", "loot-card-3" }, settings.ResolveObjects().Select(c => c.Id));
    }

    [Fact]
    public void ScenarioReservesInventoryAndGroundObjectsButStillRejectsDuplicateAuthoredObjects()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["bearer"],
            UnitTypes = [new("bearer", 2, 1, 1, 1, 1) { CanHaveItems = true }],
            Units = [new("bearer", "blue", new(0, 0), UnitId: "owner") { Inventory = [new("loot-card-1", LootCardKind.SecondWind)] }],
            Agency = [new("bearer", "blue", ControllerKind.Human)],
            GroundObjects = [new(new("loot-card-2", LootCardKind.SecondWind), new(1, 0))],
            Loot = new() { Composition = [new(LootCardKind.SecondWind, 2)] }
        };
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(new[] { "loot-card-3", "loot-card-4" }, state.LootDeck!.DrawPile.Select(c => c.Id));
        Assert.Equal("loot-card-1", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { GroundObjects = [new(new("loot-card-1", LootCardKind.SecondWind), new(1, 0))] }));
    }
}
