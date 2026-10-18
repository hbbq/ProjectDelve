namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveLavaCoalGeneration(GameState state, IRandomProvider random, ResolutionEvents events)
    {
        var board = state.Physical.Board;
        foreach (var lava in board.Terrain.Where(t => t.Kind == TerrainKind.Lava)
            .OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var roll = random.RollD8();
                var (dx, dy) = roll switch
                {
                    1 => (0, -1), 2 => (1, -1), 3 => (1, 0), 4 => (1, 1),
                    5 => (0, 1), 6 => (-1, 1), 7 => (-1, 0), 8 => (-1, -1),
                    _ => throw new ArgumentException("Lava generation requires a d8 result from 1 to 8.", nameof(random))
                };
                events.Add(new RulesEvent("LavaCoalRolled", DieRoll: roll) { Cell = lava.Position, ActionId = "lava-coal" });
                var destination = new Cell(lava.Position.X + dx, lava.Position.Y + dy);
                if (!ResourceRules.CanCreateAt(state, destination) ||
                    !SpatialRules.AreAdjacent(board, lava.Position, destination)) continue;
                state.PlaceResource(ResourceTypes.Coal, destination);
                events.Add(new RulesEvent("ResourceCreated")
                {
                    Cell = destination, ResourceType = ResourceTypes.Coal, ActionId = "lava-coal"
                });
            }
        }
    }
}
