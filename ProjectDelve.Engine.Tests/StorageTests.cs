using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public class StorageTests
{
    private const string Locker = "blue-locker";
    private static readonly OwnedObject Gear = new("loot-card-1", "test-gear")
    {
        Level = 2, AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("sprint", 1)
    };
    private static readonly OwnedObject Item = new("tonic") { ItemTypeId = "second-wind" };
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["bearer"],
        UnitTypes = [new("bearer", 2, 1, 1, 1, 2) { Unique = true, CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("bearer", "blue", new(0, 0), UnitId: "owner") { Inventory = [Gear, Item] }],
        Agency = [new("bearer", "blue", ControllerKind.Human)],
        EquipmentTypes = [new("test-gear", "Test gear") { AdditionalLevels = [new() {
            MaxHpModifier = 2, LocalAbilities = [new("sprint", "Sprint", AbilityTiming.BonusAction,
                new SelfExtraMovesMechanic(1)) { Usage = new(3) }] }] }],
        Storages = [new(Locker, "Locker", "blue", [])],
        Loot = new() { Composition = [new(LootCardKind.SecondWind, 1)] }
    };

    [Fact]
    public void PhysicalCardsMoveOnceAndSurviveCopyAndRestore()
    {
        var original = GameEngine.CreateGame(Scenario());
        var state = original.DetachedCopy();
        var owner = Assert.Single(state.Units);
        Assert.Equal(4, owner.CurrentHp);
        Assert.Null(Assert.Single(state.Storages, s => s.StorageId == StorageRules.RepossessedEquipmentId).SideId);
        Assert.Equal("blue", Assert.Single(state.Storages, s => s.StorageId == Locker).SideId);
        StorageRules.Deposit(state, owner, Gear.Id, Locker);
        StorageRules.Deposit(state, owner, Item.Id, Locker);
        Assert.Equal(2, state.Units[0].CurrentHp);
        Assert.Empty(state.Units[0].Inventory);
        Assert.Empty(state.GroundObjects);
        Assert.Equal(2, original.Units[0].Inventory.Length);
        Assert.Empty(original.Storages.Single(s => s.StorageId == Locker).Contents);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateRestoredState(restored);
        Assert.Empty(FogOfWar.Observe(restored, "blue").Storages);
        Assert.Equal(2, FogOfWar.Observe(restored, null).Storages.Single(s => s.StorageId == Locker).Contents.Length);
        foreach (var obj in new[] { Gear, Item })
        {
            Assert.Single(LootRules.PhysicalObjects(restored), o => o.Id == obj.Id);
            StorageRules.Withdraw(restored, restored.Units[0], obj.Id, Locker);
            Assert.Throws<ArgumentException>(() => StorageRules.Withdraw(restored, restored.Units[0], obj.Id, Locker));
            Assert.Single(LootRules.PhysicalObjects(restored), o => o.Id == obj.Id);
        }
        var gear = restored.Units[0].Inventory.Single(o => o.Id == Gear.Id);
        Assert.Equal(2, gear.Level);
        Assert.Equal(1, gear.AbilityUseCounts["sprint"]);
        Assert.Equal(Item.ItemTypeId, restored.Units[0].Inventory.Single(o => o.Id == Item.Id).ItemTypeId);
        Assert.Equal(4, restored.EffectiveMaxHpOf("owner"));
        Assert.Equal(2, restored.Units[0].CurrentHp);
        Assert.Empty(restored.Storages.Single(s => s.StorageId == Locker).Contents);
    }

    [Fact]
    public void AuthoredAndRestoredStorageSharesThePhysicalIdentityNamespace()
    {
        var scene = Scenario();
        var locker = scene.Storages![0];
        var stocked = locker with { Contents = [Item with { Id = "loot-card-2" }] };
        var state = GameEngine.CreateGame(scene with { Storages = [stocked] });
        Assert.Equal("loot-card-3", Assert.Single(state.LootDeck!.DrawPile).Id);
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("bearer", "new-bearer", "red", new(2, 1),
            inventory: [stocked.Contents[0]]));
        Assert.Single(state.Units);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Storages = [locker, locker] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Storages = [locker with { SideId = "missing" }] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Storages = [locker with { Contents = [Gear] }] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Storages = [locker with { Contents = [new("immediate")] }] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Storages = [locker with { StorageId = StorageRules.RepossessedEquipmentId }] }));
        state.Storages[0] = stocked with { Contents = [stocked.Contents[0] with { Level = 5 }] };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateRestoredState(state));
    }

    [Fact]
    public void RejectedTransfersPreserveBothLocations()
    {
        var scene = Scenario();
        var locker = scene.Storages![0] with { Contents = [Gear with { Id = "second-copy" }] };
        var state = GameEngine.CreateGame(scene with { Storages = [locker] });
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => StorageRules.Withdraw(state, state.Units[0], "second-copy", Locker));
        Assert.Throws<ArgumentException>(() => StorageRules.Deposit(state, state.Units[0], Gear.Id, "missing"));
        Assert.Throws<ArgumentException>(() => StorageRules.Deposit(state, state.Units[0], "second-copy", Locker));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }
}
