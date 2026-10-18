using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class RailwayScenario
{
    internal static ScenarioDefinition Definition()
    {
        List<CellDefinition> rails = [];
        for (var x = 1; x < 10; x++) rails.Add(new(new(x, 2), TerrainKind.Rail, RailDirection.East));
        for (var y = 2; y < 8; y++) rails.Add(new(new(10, y), TerrainKind.Rail, RailDirection.South));
        for (var x = 10; x > 1; x--) rails.Add(new(new(x, 8), TerrainKind.Rail, RailDirection.West));
        for (var y = 8; y > 2; y--) rails.Add(new(new(1, y), TerrainKind.Rail, RailDirection.North));
        return new()
        {
            Board = new() { Width = 13, Height = 11, Cells = rails },
            UnitTypeIds = [UnitTypeIds.Locomotive, UnitTypeIds.Barbarian, UnitTypeIds.Rogue,
                UnitTypeIds.Grunt, UnitTypeIds.Zombie, UnitTypeIds.RedDragon],
            NeutralSides = ["Railway"],
            Objectives = [new("Railway", ScenarioObjective.NoObjective)],
            Units = [new(UnitTypeIds.Locomotive, "Railway", new(1, 2)),
                new(UnitTypeIds.Barbarian, "Heroes", new(3, 2)),
                new(UnitTypeIds.Rogue, "Heroes", new(4, 5)),
                new(UnitTypeIds.Grunt, "Monsters", new(6, 2), Posture.Lying),
                new(UnitTypeIds.Zombie, "Monsters", new(10, 4)),
                new(UnitTypeIds.RedDragon, "Monsters", new(8, 7))],
            Agency = [new(UnitTypeIds.Locomotive, "Railway", ControllerKind.Automated),
                new(UnitTypeIds.Barbarian, "Heroes", ControllerKind.Human),
                new(UnitTypeIds.Rogue, "Heroes", ControllerKind.Human),
                new(UnitTypeIds.Grunt, "Monsters", ControllerKind.Human),
                new(UnitTypeIds.Zombie, "Monsters", ControllerKind.Human),
                new(UnitTypeIds.RedDragon, "Monsters", ControllerKind.Human)],
            Resources = [new(ResourceTypes.Coal, new(1, 2)), new(ResourceTypes.Coal, new(10, 4))]
        };
    }
}
