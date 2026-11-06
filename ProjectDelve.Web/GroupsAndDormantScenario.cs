using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Mutable hand-authored demonstration. Mechanic tests use their own small fixtures.
public static class GroupsAndDormantScenario
{
    public static ScenarioDefinition Definition()
    {
        List<CellDefinition> cells = [];
        for (var y = 11; y <= 14; y++)
        for (var x = 17; x <= 22; x++) cells.Add(new(new(x, y), TerrainKind.StoneFloor));
        foreach (var cell in new Cell[] { new(10, 3), new(11, 3), new(10, 4), new(11, 4) })
            cells.Add(new(cell, TerrainKind.Water));
        foreach (var cell in new Cell[] { new(3, 12), new(4, 12), new(5, 13), new(20, 2), new(21, 2), new(21, 3), new(22, 4), new(12, 13) })
            cells.Add(new(cell, TerrainKind.Tree));
        List<EdgeDefinition> edges = [];
        for (var y = 11; y <= 14; y++)
        {
            edges.Add(new(new(16, y), EdgeDirection.Right, y == 12 ? EdgeKind.ClosedDoor : EdgeKind.Wall));
            edges.Add(new(new(22, y), EdgeDirection.Right, EdgeKind.Wall));
        }
        for (var x = 17; x <= 22; x++)
        {
            edges.Add(new(new(x, 10), EdgeDirection.Down, EdgeKind.Wall));
            edges.Add(new(new(x, 14), EdgeDirection.Down, EdgeKind.Wall));
        }
        return new()
        {
            Board = new() { Width = 24, Height = 16, DefaultTerrain = TerrainKind.Grass, Cells = cells, Edges = edges },
            UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Goblin, UnitTypeIds.SkeletonArcher,
                UnitTypeIds.Shaman, UnitTypeIds.Zombie],
            Units =
            [
                new(UnitTypeIds.Barbarian, "Heroes", new(2, 7), UnitId: "barbarian"),
                new(UnitTypeIds.Rogue, "Heroes", new(2, 9), UnitId: "rogue"),
                new(UnitTypeIds.Goblin, "Monsters", new(13, 7), UnitId: "camp-sentry", GroupId: "camp"),
                new(UnitTypeIds.Goblin, "Monsters", new(15, 7), UnitId: "camp-east", GroupId: "camp", Dormant: true),
                new(UnitTypeIds.Goblin, "Monsters", new(14, 9), UnitId: "camp-south", GroupId: "camp", Dormant: true),
                new(UnitTypeIds.Goblin, "Monsters", new(7, 2), UnitId: "patrol-one", GroupId: "road"),
                new(UnitTypeIds.Goblin, "Monsters", new(9, 2), UnitId: "patrol-two", GroupId: "road"),
                new(UnitTypeIds.SkeletonArcher, "Monsters", new(15, 5), UnitId: "watch", GroupId: "watch"),
                new(UnitTypeIds.Shaman, "Monsters", new(16, 9), UnitId: "shaman", GroupId: "hut", Dormant: true, WakeDistance: 5),
                new(UnitTypeIds.Zombie, "Monsters", new(18, 12), UnitId: "zombie-west", GroupId: "house", Dormant: true),
                new(UnitTypeIds.Zombie, "Monsters", new(20, 13), UnitId: "zombie-east", GroupId: "house", Dormant: true)
            ],
            Agency =
            [
                new(UnitTypeIds.Barbarian, "Heroes", ControllerKind.Human), new(UnitTypeIds.Rogue, "Heroes", ControllerKind.Human),
                new(UnitTypeIds.Goblin, "Monsters", ControllerKind.Automated),
                new(UnitTypeIds.SkeletonArcher, "Monsters", ControllerKind.Automated),
                new(UnitTypeIds.Shaman, "Monsters", ControllerKind.Automated), new(UnitTypeIds.Zombie, "Monsters", ControllerKind.Automated),
                new(UnitTypeIds.Goblin, "Monsters", ControllerKind.Automated, "camp"),
                new(UnitTypeIds.Goblin, "Monsters", ControllerKind.Automated, "road"),
                new(UnitTypeIds.SkeletonArcher, "Monsters", ControllerKind.Automated, "watch"),
                new(UnitTypeIds.Shaman, "Monsters", ControllerKind.Automated, "hut"),
                new(UnitTypeIds.Zombie, "Monsters", ControllerKind.Automated, "house")
            ],
            Orders =
            [
                new(UnitTypeIds.Goblin, "Monsters", OrderKind.GuardArea, GroupId: "camp", Area: new(13, 6, 4, 4)),
                new(UnitTypeIds.Goblin, "Monsters", OrderKind.PatrolRoute, Route: new("road-loop"), GroupId: "road"),
                new(UnitTypeIds.SkeletonArcher, "Monsters", OrderKind.GuardArea, GroupId: "watch", Area: new(13, 4, 4, 3)),
                new(UnitTypeIds.Shaman, "Monsters", OrderKind.GuardArea, GroupId: "hut", Area: new(13, 6, 4, 4)),
                new(UnitTypeIds.Zombie, "Monsters", OrderKind.GuardArea, GroupId: "house", Area: new(17, 11, 6, 4))
            ],
            Waypoints = [new("north-west", new(6, 2, 2, 1)), new("north-east", new(15, 2, 2, 1)),
                new("south-east", new(15, 10, 2, 1)), new("south-west", new(6, 10, 2, 1))],
            Routes = [new("road-loop", ["north-west", "north-east", "south-east", "south-west"], RouteMode.Loop)],
            Objectives = [new("Heroes", ScenarioObjective.NoObjective), new("Monsters", ScenarioObjective.NoObjective)],
            FogOfWarSides = ["Heroes"], Loot = LootSettings.Empty
        };
    }
}
