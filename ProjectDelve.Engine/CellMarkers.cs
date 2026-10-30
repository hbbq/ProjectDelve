namespace ProjectDelve.Engine;

public sealed record CellMarkerType(string Id, string Name);
public static class CellMarkerTypes
{
    public static CellMarkerType BananaPeel { get; } = new("banana-peel", "Banana Peel");
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
