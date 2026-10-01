using System.Collections.ObjectModel;

namespace ProjectDelve.Engine;

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
        // Relocate only the mover: its original cell is now unoccupied.
        var occupants = state.Physical.Figures.Where(f => f.Id != moverId)
            .ToDictionary(f => f.Position);
        var paths = new Dictionary<Cell, List<Cell>> { [from] = [from] };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (paths[current].Count - 1 == maxSteps) continue;
            foreach (var to in Neighbors(current)) // top, left, right, bottom
            {
                if (!Inside(state.Physical.Board, to) || paths.ContainsKey(to) ||
                    !state.Physical.Board.TerrainAt(to).Passable() ||
                    !state.Physical.Board.EdgeBetween(current, to).Passable())
                    continue;
                if (occupants.TryGetValue(to, out var occupant) &&
                    state.Units.Single(u => u.Id == occupant.Id).SideId != unit.SideId)
                    continue;
                paths[to] = [.. paths[current], to];
                queue.Enqueue(to);
            }
        }

        // Friendly cells may be traversed but cannot be destinations.
        return new ReadOnlyDictionary<Cell, IReadOnlyList<Cell>>(paths
            .Where(pair => !occupants.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<Cell>)pair.Value.AsReadOnly()));
    }

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
        bool closedDoorsTraversable = false) =>
        Distances(board, from, goal, closedDoorsTraversable).TryGetValue(goal, out var distance) ? distance : null;

    internal static IReadOnlyDictionary<Cell, int> Distances(Board board, Cell from, Cell? goal = null,
        bool closedDoorsTraversable = false)
    {
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
                if (!MovementRules.Inside(board, next) || distances.ContainsKey(next) ||
                    !(board.EdgeBetween(current, next).Passable() ||
                        board.EdgeBetween(current, next) == EdgeKind.ClosedDoor &&
                        closedDoorsTraversable) ||
                    next != goal && !board.TerrainAt(next).Passable()) continue;
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
        var board = state.Physical.Board;
        if (from.X < 0 || from.X >= board.Width || from.Y < 0 || from.Y >= board.Height ||
            state.Physical.Figures.Any(f => f.Id != unitId && f.Position == from))
            throw new ArgumentException("The hypothetical position must be on the board and unoccupied by other figures.", nameof(from));
    }
}
