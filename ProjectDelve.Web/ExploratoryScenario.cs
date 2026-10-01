using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Web-only exploratory content: opposing approaches to a shared central encounter.
internal static class ExploratoryScenario
{
    internal static GameState Create() => new()
    {
        Physical = new PhysicalState(new Board(10, 8,
            [// Zombie corridor: its only exit is a closed door toward Aria.
                new Edge(new Cell(0, 3), new Cell(0, 4), EdgeKind.ClosedDoor),
                new Edge(new Cell(0, 4), new Cell(1, 4), EdgeKind.Wall),
                new Edge(new Cell(0, 5), new Cell(1, 5), EdgeKind.Wall),
                new Edge(new Cell(0, 6), new Cell(1, 6), EdgeKind.Wall),
                new Edge(new Cell(0, 7), new Cell(1, 7), EdgeKind.Wall),
                // West barrier: two door shortcuts, or open routes around either end.
                new Edge(new Cell(3, 1), new Cell(4, 1), EdgeKind.Wall),
                new Edge(new Cell(3, 2), new Cell(4, 2), EdgeKind.ClosedDoor),
                new Edge(new Cell(3, 3), new Cell(4, 3), EdgeKind.WallWithWindow),
                new Edge(new Cell(3, 4), new Cell(4, 4), EdgeKind.Wall),
                new Edge(new Cell(3, 5), new Cell(4, 5), EdgeKind.ClosedDoor),
                // East barrier: an open passage above Bram's closed door, plus end routes.
                new Edge(new Cell(6, 2), new Cell(7, 2), EdgeKind.Wall),
                new Edge(new Cell(6, 3), new Cell(7, 3), EdgeKind.OpenDoor),
                new Edge(new Cell(6, 4), new Cell(7, 4), EdgeKind.Wall),
                new Edge(new Cell(6, 5), new Cell(7, 5), EdgeKind.ClosedDoor),
                new Edge(new Cell(6, 6), new Cell(7, 6), EdgeKind.Wall),
                // A short central divider changes north/south routes without forming a maze.
                new Edge(new Cell(4, 3), new Cell(4, 4), EdgeKind.ClosedDoor),
                new Edge(new Cell(5, 3), new Cell(5, 4), EdgeKind.Wall),
                new Edge(new Cell(6, 3), new Cell(6, 4), EdgeKind.Wall)])
            {
                Terrain = [new TerrainTile(new Cell(0, 0), TerrainKind.Grass),
                    new TerrainTile(new Cell(1, 0), TerrainKind.Grass),
                    new TerrainTile(new Cell(2, 0), TerrainKind.Tree),
                    new TerrainTile(new Cell(1, 3), TerrainKind.Water),
                    new TerrainTile(new Cell(2, 3), TerrainKind.Water),
                    new TerrainTile(new Cell(5, 2), TerrainKind.StoneFloorWithTable),
                    new TerrainTile(new Cell(8, 4), TerrainKind.StoneFloorWithTable),
                    new TerrainTile(new Cell(8, 7), TerrainKind.Grass),
                    new TerrainTile(new Cell(9, 7), TerrainKind.Tree)]
            },
            [new Figure("zombie-1", new Cell(0, 6)), new Figure("aria", new Cell(1, 2)), new Figure("bram", new Cell(8, 5)),
                new Figure("wolf-1", new Cell(4, 2)), new Figure("wolf-2", new Cell(5, 3)),
                new Figure("wolf-3", new Cell(4, 5)), new Figure("sentinel-1", new Cell(6, 2)),
                new Figure("sentinel-2", new Cell(6, 5))]),
        Types = [UnitType.Hero("aria-type", 3, 1, 2, 1, 4), UnitType.Hero("bram-type", 3, 2, 2, 1, 4),
            new UnitType("wolf-type", 3, 1, 1, 0, 1), new UnitType("sentinel-type", 2, 2, 1, 1, 1), UnitType.Zombie()],
        Units = [new Unit("zombie-1", "zombie-type", "red", 1), new Unit("aria", "aria-type", "blue", 4), new Unit("bram", "bram-type", "blue", 4),
            new Unit("wolf-1", "wolf-type", "red", 1), new Unit("wolf-2", "wolf-type", "red", 1),
            new Unit("wolf-3", "wolf-type", "red", 1), new Unit("sentinel-1", "sentinel-type", "red", 1),
            new Unit("sentinel-2", "sentinel-type", "red", 1)]
    };
}

internal sealed class SystemRandomProvider : IRandomProvider
{
    public string DrawToken(IReadOnlyList<string> bag) => bag[Random.Shared.Next(bag.Count)];
    public AttackFace RollAttackDie() => Random.Shared.Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public int RollD6() => Random.Shared.Next(1, 7);
    public DefenceFace RollDefenceDie() => Random.Shared.Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
}
