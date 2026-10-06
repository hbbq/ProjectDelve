using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;
using static ProjectDelve.Engine.EdgeDirection;

namespace ProjectDelve.Web.Tests;

// Early courtyard encounters, a Zombie crypt, and a Troll vault with three gates.
internal static class CourtyardFixture
{
    internal static GameState Create() => GameEngine.CreateGame(Definition());

    internal static ScenarioDefinition Definition()
    {
        var board = Map(15, 15, edges: [
            // Crypt interior: x=1..3, y=2..5. Its only exit is the eastern door.
            .. Room(1, 2, 3, 5),
            Edge(3, 4, Right, EdgeKind.ClosedDoor),
            // Two-cell approach beside the crypt, with a window into the courtyard.
            .. Vertical(5, 1, 5, EdgeKind.Wall),
            Edge(5, 3, Right, EdgeKind.WallWithWindow),
            // Vault sealed by the board's north/east boundaries. Three door Actions delay arrival.
            .. Vertical(9, 0, 4, EdgeKind.Wall),
            .. Horizontal(1, 10, 14, EdgeKind.Wall),
            Edge(11, 1, Down, EdgeKind.ClosedDoor),
            .. Horizontal(2, 10, 14, EdgeKind.Wall),
            Edge(11, 2, Down, EdgeKind.ClosedDoor),
            .. Horizontal(4, 10, 14, EdgeKind.Wall),
            Edge(11, 4, Down, EdgeKind.ClosedDoor),
            // Southern ruin: a doorway and an open end give two routes through.
            .. Horizontal(11, 0, 5, EdgeKind.Wall),
            Edge(3, 11, Down, EdgeKind.OpenDoor)
        ], cells: [
                // A stream with a stone crossing at y=3; space to go around below.
                new(new(8, 0), TerrainKind.Water), new(new(8, 1), TerrainKind.Water),
                new(new(8, 2), TerrainKind.Water), new(new(8, 4), TerrainKind.Water),
                new(new(8, 5), TerrainKind.Water),
                new(new(11, 1), TerrainKind.Grass), new(new(12, 1), TerrainKind.Tree),
                new(new(13, 1), TerrainKind.Grass), new(new(13, 2), TerrainKind.Tree),
                new(new(11, 12), TerrainKind.Grass), new(new(12, 12), TerrainKind.Grass),
                new(new(13, 12), TerrainKind.Tree), new(new(12, 13), TerrainKind.Grass),
                new(new(2, 2), TerrainKind.StoneFloorWithTable),
                new(new(9, 10), TerrainKind.StoneFloorWithTable)]);
        return Define(board, groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(4, 7)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(4, 10)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(3, 9)),
            // A wounded Hero beside Cleric makes Heal immediately useful.
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 8, initialHp: 2)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(7, 11), At(7, 8)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(2, 3), At(1, 5)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(6, 7), At(9, 3)),
            Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, At(7, 10)),
            Group(UnitTypeIds.Troll, "red", ControllerKind.Automated, At(11, 0), At(13, 0))
        ], unitTypeIds: [
            UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Cleric, UnitTypeIds.Wizard,
            UnitTypeIds.Grunt, UnitTypeIds.Zombie, UnitTypeIds.SkeletonArcher,
            UnitTypeIds.Goblin, UnitTypeIds.Shaman, UnitTypeIds.Troll
        ]);
    }
}
