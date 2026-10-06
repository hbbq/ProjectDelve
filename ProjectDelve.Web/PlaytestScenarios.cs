using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;
using static ProjectDelve.Engine.EdgeDirection;

namespace ProjectDelve.Web;

public sealed record ScenarioDescription(string Id, string Name, string Description);

// Catalog presentation stays separate from initial setup definitions.
public static class PlaytestScenarios
{
    public const string DefaultId = "basic-combat";
    public static IReadOnlyList<ScenarioDescription> Catalog { get; } = Array.AsReadOnly<ScenarioDescription>([
        new(DefaultId, "Basic Combat", "Barbarian against Grunts: movement, Normal Attack and activation flow."),
        new("goblins", "Goblins", "Two Heroes, flanking opportunities and space for Goblins to retreat."),
        new("archers", "Archers", "Mixed melee and ranged enemies around broken sight lines."),
        new("wizard-doors", "Wizard / Doors", "Wizard control and ranged encounters beside a sealed room: Zombies try Doors while a Ghost phases through Walls to reach normal attack positions."),
        new("full-party-trolls", "Full Party / Trolls", "All four Heroes, Undying Trolls and encounters across rooms and Doors."),
        new("shaman-hunt", "Shaman Hunt", "Pursue a fleeing Shaman as it spawns Goblins, with a 2x2 Red Dragon guarding the broad eastern approach.")
    ]);

    public static GameState Create(string id) => GameEngine.CreateGame(Definition(id));

    public static ScenarioDefinition Definition(string id) => id switch
    {
        DefaultId => BasicCombat(),
        "goblins" => Goblins(),
        "archers" => Archers(),
        "wizard-doors" => WizardDoors(),
        "full-party-trolls" => FullParty(),
        "shaman-hunt" => ShamanHunt(),
        _ => throw new ArgumentException("Unknown playtest scenario.", nameof(id))
    };

    private static ScenarioDefinition BasicCombat() => Define(
        board: Map(8, 8, cells: [
            Tile(3, 2, TerrainKind.Tree),
            Tile(3, 3, TerrainKind.Grass),
            Tile(4, 5, TerrainKind.StoneFloorWithTable)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 4)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(4, 4), At(4, 3), At(6, 6))
        ]);

    private static ScenarioDefinition Goblins() => Define(
        board: Map(10, 10, cells: [
            Tile(4, 2, TerrainKind.Tree),
            Tile(4, 3, TerrainKind.Grass),
            Tile(5, 7, TerrainKind.Water),
            Tile(6, 7, TerrainKind.Water),
            Tile(2, 6, TerrainKind.StoneFloorWithTable)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 4)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 5)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 4), At(6, 6)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(4, 5), At(7, 3))
        ]);

    private static ScenarioDefinition Archers() => Define(
        board: Map(12, 12, cells: [
            .. Tiles(TerrainKind.Tree, At(5, 3), At(5, 4)),
            .. Tiles(TerrainKind.StoneFloorWithTable, At(7, 8), At(8, 8))
        ], edges: [
            .. Vertical(6, 5, 7, EdgeKind.Wall),
            Edge(6, 6, Right, EdgeKind.WallWithWindow)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 5)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 5), At(7, 7)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(5, 6), At(8, 3)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(6, 2), At(8, 6))
        ]);

    private static ScenarioDefinition WizardDoors() => Define(
        board: Map(15, 15, cells: [
            Tile(5, 5, TerrainKind.Tree),
            Tile(7, 9, TerrainKind.StoneFloorWithTable),
            Tile(11, 11, TerrainKind.Tree)
        ], edges: [
            .. Room(8, 1, 12, 5),
            Edge(7, 3, Right, EdgeKind.ClosedDoor),
            Edge(10, 5, Down, EdgeKind.ClosedDoor),
            .. Horizontal(9, 1, 5, EdgeKind.Wall),
            Edge(3, 9, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(3, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(6, 6), At(7, 8)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(7, 4), At(10, 8)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 3), At(10, 5)),
            Group(UnitTypeIds.Ghost, "red", ControllerKind.Automated, At(8, 5))
        ]);

    private static ScenarioDefinition FullParty() => Define(
        board: Map(15, 15, cells: [
            .. Tiles(TerrainKind.Tree, At(5, 4), At(3, 12)),
            Tile(6, 10, TerrainKind.StoneFloorWithTable)
        ], edges: [
            .. Room(8, 1, 12, 5),
            Edge(7, 3, Right, EdgeKind.ClosedDoor),
            Edge(10, 5, Down, EdgeKind.ClosedDoor),
            .. Room(9, 9, 13, 13),
            Edge(8, 11, Right, EdgeKind.ClosedDoor),
            Edge(11, 13, Down, EdgeKind.ClosedDoor),
            .. Vertical(4, 0, 3, EdgeKind.Wall),
            Edge(4, 2, Right, EdgeKind.ClosedDoor),
            .. Horizontal(9, 0, 4, EdgeKind.Wall),
            Edge(2, 9, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(3, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(2, 7)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(6, 6), At(7, 7)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(6, 3), At(8, 8)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 3), At(10, 5)),
            Group(UnitTypeIds.Troll, "red", ControllerKind.Automated, At(9, 11), At(11, 9))
        ]);

    private static ScenarioDefinition ShamanHunt() => Define(
        board: Map(15, 15, cells: [
            .. Tiles(TerrainKind.Tree, At(6, 6), At(7, 6)),
            Tile(6, 7, TerrainKind.StoneFloorWithTable),
            Tile(10, 10, TerrainKind.Tree),
            .. Tiles(TerrainKind.Water, At(3, 11), At(4, 11))
        ], edges: [
            .. Vertical(4, 0, 4, EdgeKind.Wall),
            Edge(4, 2, Right, EdgeKind.OpenDoor),
            .. Vertical(9, 8, 14, EdgeKind.Wall),
            Edge(9, 11, Right, EdgeKind.OpenDoor),
            .. Horizontal(4, 7, 13, EdgeKind.Wall),
            Edge(10, 4, Down, EdgeKind.OpenDoor),
            .. Horizontal(10, 0, 6, EdgeKind.Wall),
            Edge(2, 10, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 7)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(1, 7)),
            Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, At(7, 5)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 8)),
            Group(UnitTypeIds.RedDragon, "red", ControllerKind.Automated, At(11, 5))
        ]);

}
