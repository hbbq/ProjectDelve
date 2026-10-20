using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Web;

internal static class ADayAtTheRaceTrackScenario
{
    internal static ScenarioDefinition Definition()
    {
        var route = new FollowRoute("circuit");

        return new()
        {
            Waypoints = [new("west", new(1, 4, 2, 4)), new("south", new(3, 10, 4, 2)),
                new("east", new(12, 7, 2, 4)), new("north", new(7, 3, 4, 2))],
            Routes = [new("circuit", ["west", "south", "east", "north"], RouteMode.Loop)],
            Board = Map(15, 15, defaultTerrain: TerrainKind.Grass, cells: [
                .. Tiles(TerrainKind.Asphalt,
                    At(2, 3), At(3, 3), At(4, 3), At(5, 3), At(6, 3), At(7, 3), At(8, 3), At(9, 3), At(10, 3), At(11, 3),
                    At(1, 4), At(2, 4), At(3, 4), At(4, 4), At(5, 4), At(6, 4), At(7, 4), At(8, 4), At(9, 4), At(10, 4), At(11, 4), At(12, 4),
                    At(1, 5), At(2, 5), At(11, 5), At(12, 5), At(13, 5),
                    At(1, 6), At(2, 6), At(12, 6), At(13, 6),
                    At(1, 7), At(2, 7), At(4, 7), At(5, 7), At(6, 7), At(7, 7), At(12, 7), At(13, 7),
                    At(2, 8), At(3, 8), At(4, 8), At(7, 8), At(8, 8), At(12, 8), At(13, 8),
                    At(7, 9), At(8, 9), At(12, 9), At(13, 9),
                    At(2, 10), At(3, 10), At(4, 10), At(5, 10), At(6, 10), At(7, 10), At(8, 10), At(11, 10), At(12, 10), At(13, 10),
                    At(1, 11), At(2, 11), At(3, 11), At(4, 11), At(5, 11), At(6, 11), At(7, 11), At(10, 11), At(11, 11), At(12, 11),
                    At(1, 12), At(2, 12), At(9, 12), At(10, 12), At(11, 12),
                    At(1, 13), At(2, 13), At(3, 13), At(4, 13), At(5, 13), At(6, 13), At(7, 13), At(8, 13), At(9, 13), At(10, 13)),
                .. Tiles(TerrainKind.Tree,
                    At(1, 1), At(13, 2), At(4, 5), At(9, 6), At(0, 10), At(8, 12)),
                .. Tiles(TerrainKind.Water,
                    At(14, 12), At(13, 13), At(14, 13), At(12, 14), At(13, 14), At(14, 14)),
                .. Tiles(TerrainKind.FruitTree, At(4, 9), At(10, 9))
            ]),
            UnitTypeIds = [UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic],
            Units = [
                new(UnitTypeIds.DiamondKart, "side-1", new(10, 3)),
                new(UnitTypeIds.DiamondKart, "side-2", new(10, 4)),
                new(UnitTypeIds.ChiefMechanic, "side-1", new(5, 1)),
                new(UnitTypeIds.ChiefMechanic, "side-2", new(9, 1)),
                //new(UnitTypeIds.Geomancer, "side-1", new(3, 1)),
                //new(UnitTypeIds.Geomancer, "side-2", new(11, 1))
            ],
            Agency = [
                new(UnitTypeIds.DiamondKart, "side-1", ControllerKind.Automated),
                new(UnitTypeIds.DiamondKart, "side-2", ControllerKind.Automated),
                new(UnitTypeIds.ChiefMechanic, "side-1", ControllerKind.Automated),
                new(UnitTypeIds.ChiefMechanic, "side-2", ControllerKind.Automated),
                //new(UnitTypeIds.Geomancer, "side-2", ControllerKind.Automated),
                //new(UnitTypeIds.Geomancer, "side-1", ControllerKind.Automated)
            ],
            Orders = [
                new(UnitTypeIds.DiamondKart, "side-1", OrderKind.FollowRoute, Route: route),
                new(UnitTypeIds.DiamondKart, "side-2", OrderKind.FollowRoute, Route: route)
            ],
            Machines = [
                new("machine-1", new(4, 1), MachineTypes.SlotMachine)
                    { OwnerSide = "side-1", StoredResources = [new(ResourceTypes.Fruit, 0)] },
                new("machine-2", new(10, 1), MachineTypes.SlotMachine)
                    { OwnerSide = "side-2", StoredResources = [new(ResourceTypes.Fruit, 0)] }
            ]
        };
    }
}
