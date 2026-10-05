using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Early courtyard encounters, a Zombie crypt, and a Troll vault with three gates.
internal static class ExploratoryScenario
{
    internal static GameState Create()
    {
        var edges = new List<Edge>();
        // Crypt interior: x=1..3, y=2..5. Its only exit is the eastern door.
        for (var x = 1; x <= 3; x++)
        {
            edges.Add(new(new(x, 1), new(x, 2), EdgeKind.Wall));
            edges.Add(new(new(x, 5), new(x, 6), EdgeKind.Wall));
        }
        for (var y = 2; y <= 5; y++)
        {
            edges.Add(new(new(0, y), new(1, y), EdgeKind.Wall));
            edges.Add(new(new(3, y), new(4, y), y == 4 ? EdgeKind.ClosedDoor : EdgeKind.Wall));
        }
        // Two-cell approach beside the crypt, with a window into the courtyard.
        for (var y = 1; y <= 5; y++)
            edges.Add(new(new(5, y), new(6, y), y == 3 ? EdgeKind.WallWithWindow : EdgeKind.Wall));
        // Troll vault: x=10..14, y=0..4, sealed by the board's north/east edges.
        // Two internal partitions and the southern exit force three door Actions
        // in sequence. Even successful rolls delay their arrival in the courtyard.
        for (var y = 0; y <= 4; y++)
            edges.Add(new(new(9, y), new(10, y), EdgeKind.Wall));
        foreach (var y in new[] { 1, 2, 4 })
            for (var x = 10; x <= 14; x++)
                edges.Add(new(new(x, y), new(x, y + 1), x == 11 ? EdgeKind.ClosedDoor : EdgeKind.Wall));
        // Southern ruin: a doorway and an open end give two routes through.
        for (var x = 0; x <= 5; x++)
            edges.Add(new(new(x, 11), new(x, 12), x == 3 ? EdgeKind.OpenDoor : EdgeKind.Wall));

        var board = new Board(15, 15, edges)
        {
            Terrain = [
                // A stream with a stone crossing at y=3; space to go around below.
                new(new(8, 0), TerrainKind.Water), new(new(8, 1), TerrainKind.Water),
                new(new(8, 2), TerrainKind.Water), new(new(8, 4), TerrainKind.Water),
                new(new(8, 5), TerrainKind.Water),
                new(new(11, 1), TerrainKind.Grass), new(new(12, 1), TerrainKind.Tree),
                new(new(13, 1), TerrainKind.Grass), new(new(13, 2), TerrainKind.Tree),
                new(new(11, 12), TerrainKind.Grass), new(new(12, 12), TerrainKind.Grass),
                new(new(13, 12), TerrainKind.Tree), new(new(12, 13), TerrainKind.Grass),
                new(new(2, 2), TerrainKind.StoneFloorWithTable),
                new(new(9, 10), TerrainKind.StoneFloorWithTable)]
        };
        return new()
        {
            Physical = new(board, [
                new("barbarian", new(4, 7)), new("rogue", new(4, 10)),
                new("cleric", new(3, 9)), new("wizard", new(2, 8)),
                // Barbarian can reach (6,8) beside Archer and Grunt for Fury/Cleave;
                // Wizard at (4,8) can Fireball both without hitting a Hero.
                new("grunt-1", new(7, 11)), new("grunt-2", new(7, 8)),
                new("zombie-1", new(2, 3)), new("zombie-2", new(1, 5)),
                new("archer-1", new(6, 7)), new("archer-2", new(9, 3)),
                new("goblin-1", new(7, 10)),
                new("troll-1", new(11, 0)), new("troll-2", new(13, 0))]),
            Types = [UnitType.Barbarian(), UnitType.Rogue(), UnitType.Cleric(), UnitType.Wizard(), UnitType.Grunt(), UnitType.Zombie(),
                UnitType.SkeletonArcher(), UnitType.Goblin(), UnitType.Troll()],
            Units = [UnitType.Barbarian().CreateUnit("barbarian", "blue"), UnitType.Rogue().CreateUnit("rogue", "blue"),
                UnitType.Cleric().CreateUnit("cleric", "blue"),
                // A wounded Hero beside Cleric makes Heal immediately useful.
                UnitType.Wizard().CreateUnit("wizard", "blue") with { CurrentHp = 2 },
                new("grunt-1", "grunt-type", "red", 1), new("grunt-2", "grunt-type", "red", 1),
                new("zombie-1", "zombie-type", "red", 1), new("zombie-2", "zombie-type", "red", 1),
                new("archer-1", "skeleton-archer-type", "red", 1), new("archer-2", "skeleton-archer-type", "red", 1),
                new("goblin-1", "goblin-type", "red", 1),
                UnitType.Troll().CreateUnit("troll-1", "red"), UnitType.Troll().CreateUnit("troll-2", "red")]
        };
    }
}

internal sealed class SystemRandomProvider : IRandomProvider
{
    public string DrawToken(IReadOnlyList<string> bag) => bag[Random.Shared.Next(bag.Count)];
    public AttackFace RollAttackDie() => Random.Shared.Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public int RollD6() => Random.Shared.Next(1, 7);
    public DefenceFace RollDefenceDie() => Random.Shared.Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
}
