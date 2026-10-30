using System.Collections.ObjectModel;

namespace ProjectDelve.Engine;

// Shared terrain/edge traversal parameters. Occupancy and stopping legality stay separate.
internal readonly record struct MovementTraversal(bool IgnoreTerrainAndEdges = false,
    bool ClosedDoorsTraversable = false, TerrainKind? RequiredTerrain = null, IReadOnlySet<Cell>? KnownCells = null,
    TerrainRestriction? Restriction = null)
{
    internal static MovementTraversal For(GameState state, string unitId, bool closedDoorsTraversable = false) =>
        new(state.IsUpright(unitId) &&
            state.HasPhase(unitId),
            closedDoorsTraversable,
            state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == unitId).TypeId).TerrainRestrictedMovement?.Terrain,
            FogOfWar.IsEnabled(state, state.Units.Single(u => u.Id == unitId).SideId)
                ? state.ExploredCells.GetValueOrDefault(state.Units.Single(u => u.Id == unitId).SideId) ?? [] : null,
            state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == unitId).TypeId).TerrainRestriction);

    internal bool CanTraverse(TerrainDefinition terrain) =>
        (!RequiredTerrain.HasValue || TerrainCatalog.AcceptsLegacy(RequiredTerrain.Value, terrain)) &&
        (Restriction is null || Restriction.Accepts(terrain)) && (IgnoreTerrainAndEdges || terrain.Passable);
    internal bool CanTraverse(EdgeKind edge) => IgnoreTerrainAndEdges || edge.Passable() ||
        ClosedDoorsTraversable && edge == EdgeKind.ClosedDoor;
}

internal static class MovementRules
{
    // Ordinary paths contain cardinal steps; a leap contains only its endpoints.
    // Sum the nominal distance rather than treating every recorded hop as MOV 1.
    internal static int Cost(IReadOnlyList<Cell> path) => path.Zip(path.Skip(1),
        (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum();

    // Paths include the origin and actual transit Cells. A leap records only
    // origin and landing; its nominal cost is their Manhattan distance (3).
    // Only valid stopping cells are returned, including the origin at distance 0.
    // null searches the current board without an activation's MOV limit.
    // Planning may ignore Cell Markers; paths from that analysis are not legal Move paths.
    internal static IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> FindPaths(
        GameState state, string moverId, Cell from, int? maxSteps = null, bool ignoreCellMarkers = false,
        bool ignoreMovementPattern = false)
    {
        HypotheticalPosition.Validate(state, moverId, from);
        if (maxSteps < 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var unit = state.Units.Single(u => u.Id == moverId);
        var pattern = ignoreMovementPattern ? null : state.Types.Single(t => t.Id == unit.TypeId).MovementPattern;
        return FindPatternPaths(state, moverId, from, maxSteps, ignoreCellMarkers, pattern);
    }

    private static IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> FindPatternPaths(
        GameState state, string moverId, Cell from, int? maxSteps, bool ignoreCellMarkers, MovementPattern? pattern)
    {
        if (pattern is MovementPattern.StraightOrthogonalOrDiagonalCorridor or MovementPattern.Adjacent)
        {
            // Compose complete paths, never concatenate modes. Adjacent uses the
            // same corner machinery, limited to a single diagonal segment.
            var adjacent = pattern == MovementPattern.Adjacent;
            int? Limit(int allowance) => maxSteps is null ? allowance : Math.Min(maxSteps.Value, allowance);
            var cardinal = FindPatternPaths(state, moverId, from, adjacent ? Limit(1) : maxSteps,
                ignoreCellMarkers, MovementPattern.StraightOrthogonal);
            var diagonal = FindPatternPaths(state, moverId, from, adjacent ? Limit(2) : maxSteps,
                ignoreCellMarkers, MovementPattern.DiagonalCorridor);
            return new ReadOnlyDictionary<Cell, IReadOnlyList<Cell>>(cardinal.Concat(diagonal)
                .DistinctBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value));
        }
        var unit = state.Units.Single(u => u.Id == moverId);
        var straight = pattern == MovementPattern.StraightOrthogonal;
        var traversal = MovementTraversal.For(state, moverId);
        var footprint = FootprintGeometry.FootprintOf(state, moverId);
        // Relocate only the mover: its entire original footprint is now unoccupied.
        var occupants = state.Physical.Figures.Where(f => f.Id != moverId)
            .SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id).Select(c => (Cell: c, Figure: f)))
            .ToDictionary(p => p.Cell, p => p.Figure);
        var paths = new Dictionary<Cell, List<Cell>> { [from] = [from] };
        bool CanEnter(Cell current, Cell to) => CanStep(state.Physical.Board, footprint, current, to, traversal) &&
            FootprintGeometry.OccupiedCells(footprint, to).All(c => !occupants.TryGetValue(c, out var occupant) ||
                state.Units.Single(u => u.Id == occupant.Id).SideId == unit.SideId);
        bool CanTransit(Cell cell) => ignoreCellMarkers || !CellMarkerRules.ContainsBananaPeel(state, footprint, cell);

        if (pattern == MovementPattern.LShape)
        {
            if (maxSteps is null or >= 3)
                foreach (var dx in new[] { -2, -1, 1, 2 })
                foreach (var dy in new[] { -2, -1, 1, 2 })
                {
                    var landing = new Cell(from.X + dx, from.Y + dy);
                    if (Math.Abs(dx) + Math.Abs(dy) == 3 &&
                        FogOfWar.KnownFootprint(state, unit.SideId, footprint, landing) &&
                        SpatialRules.Fits(state.Physical.Board, footprint, landing, traversal))
                        paths[landing] = [from, landing];
                }
        }
        else if (pattern == MovementPattern.DiagonalCorridor)
        {
            foreach (var dx in new[] { -1, 1 })
            foreach (var dy in new[] { -1, 1 })
            {
                var current = from;
                for (var cost = 2; maxSteps is null || cost <= maxSteps; cost += 2)
                {
                    if (current != from && !CanTransit(current)) break;
                    var next = new Cell(current.X + dx, current.Y + dy);
                    // Same top/left/right/bottom ordering as ordinary BFS. The
                    // choice is local to a segment; corners are never arrivals.
                    var corner = Neighbors(current).Where(c => c == new Cell(next.X, current.Y) || c == new Cell(current.X, next.Y))
                        .Where(c => CanEnter(current, c) && CanTransit(c) && CanEnter(c, next))
                        .Select(c => (Cell?)c).FirstOrDefault();
                    if (corner is null) break;
                    paths[next] = [.. paths[current], corner, next];
                    current = next;
                }
            }
        }
        else
        {
            var queue = new Queue<Cell>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (paths[current].Count - 1 == maxSteps || current != from && !CanTransit(current)) continue;
                foreach (var to in Neighbors(current)) // top, left, right, bottom
                {
                    // Restrict the path itself: filtering BFS endpoints would allow detours.
                    if (straight && current != from &&
                        (to.X - current.X != Math.Sign(current.X - from.X) ||
                         to.Y - current.Y != Math.Sign(current.Y - from.Y))) continue;
                    if (paths.ContainsKey(to) || !CanEnter(current, to)) continue;
                    paths[to] = [.. paths[current], to];
                    queue.Enqueue(to);
                }
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
        FootprintGeometry.OccupiedCells(footprint, to).All(c => traversal.KnownCells is null || traversal.KnownCells.Contains(c)) &&
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
                    if (!MovementRules.Inside(board, next) || traversal.KnownCells is not null && !traversal.KnownCells.Contains(next) ||
                        !traversal.CanTraverse(board.EdgeBetween(current, next))) continue;
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
