using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class LootPlaytestScenario
{
    internal static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 8, Height = 6, Edges = [new(new(3, 1), EdgeDirection.Right, EdgeKind.ClosedDoor)] },
        UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Grunt, UnitTypeIds.BombImp],
        UnitTypes = [UnitType.Barbarian() with { CanHaveItems = true, CanHaveEquipment = true },
            UnitType.Grunt() with { CanHaveItems = true, CanHaveEquipment = true, LootDropSuccesses = 6 },
            UnitType.BombImp() with { LootDropSuccesses = 6 }],
        Units = [new(UnitTypeIds.Barbarian, "Blue", new(1, 1), InitialHp: 3)
            { Inventory = [new("starting-bell", LootCardKind.GoatBell), new("starting-wind", LootCardKind.SecondWind)] },
            new(UnitTypeIds.Grunt, "Blue", new(1, 2), Posture.Lying),
            new(UnitTypeIds.Grunt, "Red", new(5, 1)), new(UnitTypeIds.Grunt, "Red", new(5, 4)),
            new(UnitTypeIds.BombImp, "Red", new(6, 2))],
        Agency = [new(UnitTypeIds.Barbarian, "Blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "Blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "Red", ControllerKind.Automated), new(UnitTypeIds.BombImp, "Red", ControllerKind.Automated)],
        Loot = new(LootCards.All.SelectMany(c => new[] { c.Kind, c.Kind }).ToList()),
        LootMarkers = [new("start-1", new(2, 1)), new("start-2", new(2, 1)), new("start-3", new(3, 3)), new("start-4", new(5, 3))],
        GroundObjects = [new(new("ground-key", LootCardKind.OfficialKey), new(1, 1)),
            new(new("ground-boots", LootCardKind.LuckyBoots), new(2, 1))],
        Objectives = [new("Blue", ScenarioObjective.NoObjective), new("Red", ScenarioObjective.NoObjective)]
    };
}
