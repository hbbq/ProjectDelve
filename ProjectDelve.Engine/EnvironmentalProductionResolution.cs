namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveEnvironmentalProduction(GameState state, IRandomProvider random, ResolutionEvents events)
    {
        var board = state.Physical.Board;
        // Resolve sources in the established top-to-bottom, left-to-right order.
        foreach (var source in board.Terrain.Where(t => board.TerrainDefinition(t).Production is not null)
            .OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X))
        {
            var terrain = board.TerrainDefinition(source);
            var production = terrain.Production as DirectionalResourceProduction
                ?? throw new ArgumentException("Unsupported environmental production mechanic.");
            var resource = ResourceTypes.Find(production.ResourceTypeId);
            var (rollEvent, actionId) = EnvironmentalProductionContent.EventIdentity(terrain);
            for (var attempt = 0; attempt < production.Attempts; attempt++)
            {
                var roll = random.RollD8();
                var (dx, dy) = roll switch
                {
                    1 => (0, -1), 2 => (1, -1), 3 => (1, 0), 4 => (1, 1),
                    5 => (0, 1), 6 => (-1, 1), 7 => (-1, 0), 8 => (-1, -1),
                    _ => throw new ArgumentException("Directional production requires a d8 result from 1 to 8.", nameof(random))
                };
                var rolled = new RulesEvent(rollEvent, DieRoll: roll) { Cell = source.Position, ActionId = actionId };
                if (terrain.LegacyProductionIdentity is null)
                    rolled = rolled with { ResourceType = resource, TerrainTypeId = terrain.Id, TerrainName = terrain.Name };
                events.Add(rolled);
                var destination = new Cell(source.Position.X + dx, source.Position.Y + dy);
                if (!ResourceRules.CanCreateAt(state, destination) ||
                    !AttackRules.HasGeometricLineOfSight(board, source.Position, destination,
                        ignoredTerrainCell: production.SourceLos == ProductionSourceLos.IgnoreProducingCell ? source.Position : null)) continue;
                state.PlaceResource(resource, destination);
                events.Add(new RulesEvent("ResourceCreated")
                {
                    Cell = destination, ResourceType = resource, ActionId = actionId
                });
            }
        }
    }
}
