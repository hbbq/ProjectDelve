namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveLavaCoalGeneration(GameState state, IRandomProvider random, ResolutionEvents events)
    {
        var board = state.Physical.Board;
        // Both sources share the exact attempt order, d8 geometry and placement checks.
        foreach (var lava in board.Terrain.Where(t => t.Kind == TerrainKind.FruitTree || t.Kind == TerrainKind.Lava && t.CoalGeneration != false)
            .OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X))
        {
            var fruit = lava.Kind == TerrainKind.FruitTree;
            var resource = fruit ? ResourceTypes.Fruit : ResourceTypes.Coal;
            var actionId = fruit ? "fruit-tree" : "lava-coal";
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var roll = random.RollD8();
                var (dx, dy) = roll switch
                {
                    1 => (0, -1), 2 => (1, -1), 3 => (1, 0), 4 => (1, 1),
                    5 => (0, 1), 6 => (-1, 1), 7 => (-1, 0), 8 => (-1, -1),
                    _ => throw new ArgumentException("Lava generation requires a d8 result from 1 to 8.", nameof(random))
                };
                events.Add(new RulesEvent(fruit ? "FruitTreeRolled" : "LavaCoalRolled", DieRoll: roll) { Cell = lava.Position, ActionId = actionId });
                var destination = new Cell(lava.Position.X + dx, lava.Position.Y + dy);
                if (!ResourceRules.CanCreateAt(state, destination) ||
                    !AttackRules.HasGeometricLineOfSight(board, lava.Position, destination, ignoredTerrainCell: lava.Position)) continue;
                state.PlaceResource(resource, destination);
                events.Add(new RulesEvent("ResourceCreated")
                {
                    Cell = destination, ResourceType = resource, ActionId = actionId
                });
            }
        }
    }
}
