namespace ProjectDelve.Engine;

// Short-lived physical occupancy lookup. Callers must discard it before changing the state.
// It is deliberately not attached to GameState: public mutable lists remain authoritative.
internal sealed class PlacementLookup(GameState state)
{
    private bool initialized;
    private Dictionary<Cell, List<string>>? occupants;

    internal bool IsIndexed => Occupants is not null;

    private Dictionary<Cell, List<string>>? Occupants
    {
        get
        {
            if (initialized) return occupants;
            initialized = true;
            if (state.Units.Any(u => u is null) || state.Types.Any(t => t is null) ||
                state.Physical.Figures.Any(f => f is null)) return null;
            var units = state.Units.ToLookup(u => u.Id);
            var types = state.Types.ToLookup(t => t.Id);
            HashSet<string> figureIds = [];
            // Invalid references retain the original ordered Single/short-circuit behavior.
            if (state.Physical.Figures.Any(f => f.Id is null || !figureIds.Add(f.Id) || units[f.Id].Count() != 1 ||
                    types[units[f.Id].Single().TypeId].Count() != 1 ||
                    !Enum.IsDefined(types[units[f.Id].Single().TypeId].Single().Footprint))) return null;
            occupants = [];
            foreach (var figure in state.Physical.Figures)
            foreach (var cell in FootprintGeometry.OccupiedCells(types[units[figure.Id].Single().TypeId].Single().Footprint,
                         figure.Position))
            {
                if (!occupants.TryGetValue(cell, out var ids)) occupants[cell] = ids = [];
                ids.Add(figure.Id);
            }
            return occupants;
        }
    }

    internal bool HasOtherFigure(IReadOnlyList<Cell> cells, string? excludedId)
    {
        if (Occupants is not { } index)
            return state.Physical.Figures.Any(f => f.Id != excludedId &&
                FootprintGeometry.OccupiedCells(state, f.Id).Intersect(cells).Any());
        return cells.Any(cell => index.TryGetValue(cell, out var ids) && ids.Any(id => id != excludedId));
    }

    internal bool SpansEdge(Cell a, Cell b)
    {
        if (Occupants is not { } index)
            return state.Physical.Figures.Any(f =>
            {
                var cells = FootprintGeometry.OccupiedCells(state, f.Id);
                return cells.Contains(a) && cells.Contains(b);
            });
        return index.TryGetValue(a, out var first) && index.TryGetValue(b, out var second) &&
            first.Any(second.Contains);
    }
}
