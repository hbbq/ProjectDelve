namespace ProjectDelve.Engine;

public sealed record CellMarkerType(string Id, string Name);
public static class CellMarkerTypes
{
    public static CellMarkerType BananaPeel { get; } = new("banana-peel", "Banana Peel");
    public static System.Collections.Immutable.ImmutableArray<CellMarkerType> All { get; } = [BananaPeel];

    // The initial Cell Marker has fixed Engine behavior, not an authored effect payload.
    public static string Describe(CellMarkerType type) => type == BananaPeel
        ? "May be placed on a passable Cell without a Unit, Machine or Cell Marker; loose Resources may remain. " +
          "Does not change Terrain or block Line of Sight. An ordinary Move may end here but cannot pass through this Cell. " +
          "When a Unit arrives by movement or placement, remove the Banana Peel, lay the Unit down, and end its remaining activation if it is currently activating. " +
          "Starting an activation on the marker does not trigger it. Hypothetical route planning ignores the marker."
        : throw new ArgumentException($"Unsupported Cell Marker Type '{type?.Id}'.", nameof(type));
}
public sealed record CellMarker(CellMarkerType Type, Cell Position);

internal static class CellMarkerRules
{
    internal static void Validate(CellMarker marker, Board board)
    {
        if (marker is null || marker.Type != CellMarkerTypes.BananaPeel || marker.Position is null ||
            !MovementRules.Inside(board, marker.Position) || !board.TerrainDefinitionAt(marker.Position).Passable ||
            board.Machines.Any(m => m.Position == marker.Position))
            throw new ArgumentException("A Cell Marker requires a supported Type and a passable Cell without a Machine.");
    }

    internal static bool ContainsBananaPeel(GameState state, Footprint footprint, Cell anchor) =>
        FootprintGeometry.OccupiedCells(footprint, anchor).Any(cell =>
            state.CellMarkers.Any(m => m.Position == cell && m.Type == CellMarkerTypes.BananaPeel));
}

public static partial class GameEngine
{
    // Called only after an actual relocation/entry, never for Stay or activation start.
    private static void ResolveArrival(GameState state, string unitId, ResolutionEvents events)
    {
        events.LootArrivals.Add(unitId);
        var cells = FootprintGeometry.OccupiedCells(state, unitId);
        var markers = state.CellMarkers.Where(m => cells.Contains(m.Position)).ToArray();
        foreach (var marker in markers)
        {
            state.CellMarkers.Remove(marker);
            events.Add(new RulesEvent("CellMarkerConsumed", unitId)
                { Cell = marker.Position, CellMarkerType = marker.Type });
        }
        if (markers.Length > 0 && state.IsUpright(unitId))
            ChangePosture(state, unitId, Posture.Lying, events, actionId: "banana-peel");
        // The ordinary activation flow completes a Lying active Unit. Rail resolution
        // observes Posture between steps and completes through its existing boundary.
    }
}
