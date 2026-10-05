using System.Collections.ObjectModel;

namespace ProjectDelve.Engine;

// Shared terrain/edge traversal parameters. Occupancy and stopping legality stay separate.
internal readonly record struct MovementTraversal(bool IgnoreTerrainAndEdges = false,
    bool ClosedDoorsTraversable = false)
{
    internal static MovementTraversal For(GameState state, string unitId, bool closedDoorsTraversable = false) =>
        new(state.IsUpright(unitId) &&
            state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == unitId).TypeId).Phase is not null,
            closedDoorsTraversable);

    internal bool CanTraverse(TerrainKind terrain) => IgnoreTerrainAndEdges || terrain.Passable();
    internal bool CanTraverse(EdgeKind edge) => IgnoreTerrainAndEdges || edge.Passable() ||
        ClosedDoorsTraversable && edge == EdgeKind.ClosedDoor;
}

internal static class MovementRules
{
    // Paths include the origin, so movement distance is path.Count - 1.
    // Only valid stopping cells are returned, including the origin at distance 0.
    // null searches the current board without an activation's MOV limit.
    internal static IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> FindPaths(
        GameState state, string moverId, Cell from, int? maxSteps = null)
    {
        HypotheticalPosition.Validate(state, moverId, from);
        if (maxSteps < 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var unit = state.Units.Single(u => u.Id == moverId);
        var traversal = MovementTraversal.For(state, moverId);
        var footprint = FootprintGeometry.FootprintOf(state, moverId);
        // Relocate only the mover: its entire original footprint is now unoccupied.
        var occupants = state.Physical.Figures.Where(f => f.Id != moverId)
            .SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id).Select(c => (Cell: c, Figure: f)))
            .ToDictionary(p => p.Cell, p => p.Figure);
        var paths = new Dictionary<Cell, List<Cell>> { [from] = [from] };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (paths[current].Count - 1 == maxSteps) continue;
            foreach (var to in Neighbors(current)) // top, left, right, bottom
            {
                if (paths.ContainsKey(to) || !CanStep(state.Physical.Board, footprint, current, to, traversal))
                    continue;
                if (FootprintGeometry.OccupiedCells(footprint, to).Any(c => occupants.TryGetValue(c, out var occupant) &&
                    state.Units.Single(u => u.Id == occupant.Id).SideId != unit.SideId))
                    continue;
                paths[to] = [.. paths[current], to];
                queue.Enqueue(to);
            }
        }

        // Friendly and impassable terrain cells may be traversed but cannot be destinations.
        return new ReadOnlyDictionary<Cell, IReadOnlyList<Cell>>(paths
            .Where(pair => SpatialRules.Fits(state.Physical.Board, footprint, pair.Key) &&
                FootprintGeometry.OccupiedCells(footprint, pair.Key).All(c => !occupants.ContainsKey(c)))
            .ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<Cell>)pair.Value.AsReadOnly()));
    }

    internal static bool CanStep(Board board, Footprint footprint, Cell from, Cell to, MovementTraversal traversal) =>
        SpatialRules.Fits(board, footprint, to, traversal) &&
        FootprintGeometry.OccupiedCells(footprint, from).All(c =>
            traversal.CanTraverse(board.EdgeBetween(c, new(c.X + to.X - from.X, c.Y + to.Y - from.Y))));

    internal static IEnumerable<Cell> Neighbors(Cell cell)
    {
        yield return new(cell.X, cell.Y - 1);
        yield return new(cell.X - 1, cell.Y);
        yield return new(cell.X + 1, cell.Y);
        yield return new(cell.X, cell.Y + 1);
    }

    internal static bool Inside(Board board, Cell cell) =>
        cell.X >= 0 && cell.X < board.Width && cell.Y >= 0 && cell.Y < board.Height;

}

// Approach queries describe the terrain route, independently of figures and MOV.
internal static class ApproachRules
{
    internal static int? Distance(Board board, Cell from, Cell goal,
        bool closedDoorsTraversable = false, MovementTraversal traversal = default) =>
        Distances(board, from, goal, closedDoorsTraversable, traversal).TryGetValue(goal, out var distance) ? distance : null;

    internal static IReadOnlyDictionary<Cell, int> Distances(Board board, Cell from, Cell? goal = null,
        bool closedDoorsTraversable = false, MovementTraversal traversal = default,
        Footprint footprint = Footprint.OneByOne)
    {
        traversal = traversal with { ClosedDoorsTraversable = traversal.ClosedDoorsTraversable || closedDoorsTraversable };
        if (!MovementRules.Inside(board, from) || goal is not null && !MovementRules.Inside(board, goal))
            throw new ArgumentException("Approach endpoints must be on the board.");
        var distances = new Dictionary<Cell, int> { [from] = 0 };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out var current))
        {
            // An impassable goal is an endpoint, never a bridge to other cells.
            if (current == goal) continue;
            foreach (var next in MovementRules.Neighbors(current))
            {
                if (distances.ContainsKey(next)) continue;
                // Exceptional goal Cells belong to the 1x1 distance calculation used by Flee.
                if (next == goal && footprint == Footprint.OneByOne)
                {
                    if (!MovementRules.Inside(board, next) || !traversal.CanTraverse(board.EdgeBetween(current, next))) continue;
                }
                else if (!MovementRules.CanStep(board, footprint, current, next, traversal)) continue;
                distances[next] = distances[current] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }
}

// Shared precondition for evaluating a placed, living unit at another position.
// This validates the hypothetical placement, not reachability or phase eligibility.
internal static class HypotheticalPosition
{
    internal static void Validate(GameState state, string unitId, Cell from)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        if (unit.CurrentHp <= 0 || !state.Physical.Figures.Any(f => f.Id == unitId))
            throw new ArgumentException("The querying unit must be alive and placed.", nameof(unitId));
        if (!SpatialRules.CanPlaceUnit(state, FootprintGeometry.FootprintOf(state, unitId), from, unitId))
            throw new ArgumentException("The hypothetical footprint must be a normally legal placement, free of other figures.", nameof(from));
    }
}
