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
                    !Passable(state.Physical.Board, current, to))
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

    private static IEnumerable<Cell> Neighbors(Cell cell)
    {
        yield return new(cell.X, cell.Y - 1);
        yield return new(cell.X - 1, cell.Y);
        yield return new(cell.X + 1, cell.Y);
        yield return new(cell.X, cell.Y + 1);
    }

    private static bool Inside(Board board, Cell cell) =>
        cell.X >= 0 && cell.X < board.Width && cell.Y >= 0 && cell.Y < board.Height;

    private static bool Passable(Board board, Cell a, Cell b) =>
        board.Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind
            is null or EdgeKind.OpenDoor;
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
