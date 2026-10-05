using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ScenarioDescription(string Id, string Name, string Description);

// Editable playtest content, with factories so mutable boards and lists are never shared.
public static class PlaytestScenarios
{
    public const string DefaultId = "basic-combat";
    public static IReadOnlyList<ScenarioDescription> Catalog { get; } = Array.AsReadOnly<ScenarioDescription>([
        new(DefaultId, "Basic Combat", "Barbarian against Grunts: movement, Normal Attack and activation flow."),
        new("goblins", "Goblins", "Two Heroes, flanking opportunities and space for Goblins to retreat."),
        new("archers", "Archers", "Mixed melee and ranged enemies around broken sight lines."),
        new("wizard-doors", "Wizard / Doors", "Wizard control and ranged encounters beside a sealed room: Zombies try Doors while a Ghost phases through Walls to reach normal attack positions."),
        new("full-party-trolls", "Full Party / Trolls", "All four Heroes, Undying Trolls and encounters across rooms and Doors."),
        new("shaman-hunt", "Shaman Hunt", "Pursue a fleeing Shaman through multiple routes as it spawns Goblins.")
    ]);

    public static GameState Create(string id) => id switch
    {
        DefaultId => BasicCombat(),
        "goblins" => Goblins(),
        "archers" => Archers(),
        "wizard-doors" => WizardDoors(),
        "full-party-trolls" => FullParty(),
        "shaman-hunt" => ShamanHunt(),
        _ => throw new ArgumentException("Unknown playtest scenario.", nameof(id))
    };

    private static GameState BasicCombat()
    {
        var board = new Board(8, 8, []) { Terrain = [
            new(new(3, 2), TerrainKind.Tree), new(new(3, 3), TerrainKind.Grass),
            new(new(4, 5), TerrainKind.StoneFloorWithTable)] };
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 1, 4),
            (UnitType.Grunt(), "grunt-1", 4, 4), (UnitType.Grunt(), "grunt-2", 4, 3),
            (UnitType.Grunt(), "grunt-3", 6, 6)]);
    }

    private static GameState Goblins()
    {
        var board = new Board(10, 10, []) { Terrain = [
            new(new(4, 2), TerrainKind.Tree), new(new(4, 3), TerrainKind.Grass),
            new(new(5, 7), TerrainKind.Water), new(new(6, 7), TerrainKind.Water),
            new(new(2, 6), TerrainKind.StoneFloorWithTable)] };
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 2, 4), (UnitType.Rogue(), "rogue", 2, 5),
            (UnitType.Grunt(), "grunt-1", 5, 4), (UnitType.Grunt(), "grunt-2", 6, 6),
            (UnitType.Goblin(), "goblin-1", 4, 5), (UnitType.Goblin(), "goblin-2", 7, 3)]);
    }

    private static GameState Archers()
    {
        var board = new Board(12, 12, []) { Terrain = [
            new(new(5, 3), TerrainKind.Tree), new(new(5, 4), TerrainKind.Tree),
            new(new(7, 8), TerrainKind.StoneFloorWithTable), new(new(8, 8), TerrainKind.StoneFloorWithTable)] };
        Vertical(board, 6, 5, 7, 6, EdgeKind.WallWithWindow);
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 2, 5), (UnitType.Rogue(), "rogue", 2, 6),
            (UnitType.Grunt(), "grunt-1", 5, 5), (UnitType.Grunt(), "grunt-2", 7, 7),
            (UnitType.Goblin(), "goblin-1", 5, 6), (UnitType.Goblin(), "goblin-2", 8, 3),
            (UnitType.SkeletonArcher(), "archer-1", 6, 2), (UnitType.SkeletonArcher(), "archer-2", 8, 6)]);
    }

    private static GameState WizardDoors()
    {
        var board = new Board(15, 15, []) { Terrain = [
            new(new(5, 5), TerrainKind.Tree), new(new(7, 9), TerrainKind.StoneFloorWithTable),
            new(new(11, 11), TerrainKind.Tree)] };
        // Five-by-five room: two gates let Zombies emerge on different routes.
        Room(board, 8, 1, 12, 5, 3, 10);
        Horizontal(board, 9, 1, 5, 3, EdgeKind.OpenDoor);
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 3, 6), (UnitType.Rogue(), "rogue", 3, 7),
            (UnitType.Wizard(), "wizard", 2, 6),
            (UnitType.Goblin(), "goblin-1", 6, 6), (UnitType.Goblin(), "goblin-2", 7, 8),
            (UnitType.SkeletonArcher(), "archer-1", 7, 4), (UnitType.SkeletonArcher(), "archer-2", 10, 8),
            (UnitType.Zombie(), "zombie-1", 8, 3), (UnitType.Zombie(), "zombie-2", 10, 5),
            (UnitType.Ghost(), "ghost-1", 8, 5)]);
    }

    private static GameState FullParty()
    {
        var board = new Board(15, 15, []) { Terrain = [
            new(new(5, 4), TerrainKind.Tree), new(new(6, 10), TerrainKind.StoneFloorWithTable),
            new(new(3, 12), TerrainKind.Tree)] };
        Room(board, 8, 1, 12, 5, 3, 10);
        Room(board, 9, 9, 13, 13, 11, 11);
        Vertical(board, 4, 0, 3, 2, EdgeKind.ClosedDoor);
        Horizontal(board, 9, 0, 4, 2, EdgeKind.OpenDoor);
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 3, 6), (UnitType.Rogue(), "rogue", 3, 7),
            (UnitType.Wizard(), "wizard", 2, 6), (UnitType.Cleric(), "cleric", 2, 7),
            (UnitType.Goblin(), "goblin-1", 6, 6), (UnitType.Goblin(), "goblin-2", 7, 7),
            (UnitType.SkeletonArcher(), "archer-1", 6, 3), (UnitType.SkeletonArcher(), "archer-2", 8, 8),
            (UnitType.Zombie(), "zombie-1", 8, 3), (UnitType.Zombie(), "zombie-2", 10, 5),
            (UnitType.Troll(), "troll-1", 9, 11), (UnitType.Troll(), "troll-2", 11, 9)]);
    }

    private static GameState ShamanHunt()
    {
        var board = new Board(15, 15, []) { Terrain = [
            new(new(6, 6), TerrainKind.Tree), new(new(7, 6), TerrainKind.Tree),
            new(new(6, 7), TerrainKind.StoneFloorWithTable), new(new(10, 10), TerrainKind.Tree),
            new(new(3, 11), TerrainKind.Water), new(new(4, 11), TerrainKind.Water)] };
        // Staggered partitions end in broad openings. No Closed Doors isolate Flee.
        Vertical(board, 4, 0, 4, 2, EdgeKind.OpenDoor);
        Vertical(board, 9, 8, 14, 11, EdgeKind.OpenDoor);
        Horizontal(board, 4, 7, 13, 10, EdgeKind.OpenDoor);
        Horizontal(board, 10, 0, 6, 2, EdgeKind.OpenDoor);
        return Setup(board, [
            (UnitType.Barbarian(), "barbarian", 2, 6), (UnitType.Rogue(), "rogue", 3, 7),
            (UnitType.Wizard(), "wizard", 2, 7), (UnitType.Cleric(), "cleric", 1, 7),
            (UnitType.Shaman(), "shaman-1", 7, 5), (UnitType.Grunt(), "grunt-1", 5, 8)]);
    }

    private static GameState Setup(Board board, (UnitType Type, string Id, int X, int Y)[] units) => new()
    {
        Physical = new(board, units.Select(u => new Figure(u.Id, new(u.X, u.Y))).ToList()),
        Types = units.Select(u => u.Type).DistinctBy(t => t.Id).ToList(),
        Units = units.Select(u => u.Type.CreateUnit(u.Id,
            u.Type.Id is "barbarian-type" or "rogue-type" or "wizard-type" or "cleric-type" ? "blue" : "red")).ToList()
    };

    // Only map drawing helpers, not scenario rules or a scripting language.
    private static void Vertical(Board board, int x, int top, int bottom, int gate, EdgeKind kind)
    {
        for (var y = top; y <= bottom; y++)
            board.Edges.Add(new(new(x, y), new(x + 1, y), y == gate ? kind : EdgeKind.Wall));
    }
    private static void Horizontal(Board board, int y, int left, int right, int gate, EdgeKind kind)
    {
        for (var x = left; x <= right; x++)
            board.Edges.Add(new(new(x, y), new(x, y + 1), x == gate ? kind : EdgeKind.Wall));
    }
    private static void Room(Board board, int left, int top, int right, int bottom, int westGate, int southGate)
    {
        Vertical(board, left - 1, top, bottom, westGate, EdgeKind.ClosedDoor);
        Vertical(board, right, top, bottom, -1, EdgeKind.Wall);
        Horizontal(board, top - 1, left, right, -1, EdgeKind.Wall);
        Horizontal(board, bottom, left, right, southGate, EdgeKind.ClosedDoor);
    }
}
