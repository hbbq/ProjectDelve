namespace ProjectDelve.Engine;

internal sealed partial class GameplayQueries
{
    public bool HasFailedRepossessionThisActivation(string unitId) =>
        world.CurrentUnitId == unitId && world.RepossessionFailedThisActivation;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Actor, string Target, bool Doors),
        Lazy<IReadOnlyDictionary<Cell, int>>> repossessionFields = new();
    public IReadOnlyList<string> KnownEquippedHostileUnitIds(string unitId) =>
        KnownHostileUnitIds(unitId).Where(id => RepossessionRules.EligibleEquipment(world, id).Any()).ToArray();

    public int? DistanceToRepossessionPositionFrom(string unitId, Cell position, string targetId,
        bool closedDoorsTraversable = false)
    {
        if (!Knows(unitId, targetId)) return null;
        HypotheticalPosition.Validate(world, unitId, position, OccupancyTraversal(unitId));
        var distances = repossessionFields.GetOrAdd((unitId, targetId, closedDoorsTraversable), key => new(() =>
        {
            var board = world.Physical.Board;
            var targetWorld = world.Copy();
            var actorIndex = targetWorld.Physical.Figures.FindIndex(f => f.Id == key.Actor);
            var figure = targetWorld.Physical.Figures[actorIndex];
            var closedDoors = board.Edges.Where(e => key.Doors && e.Kind == EdgeKind.ClosedDoor).ToArray();
            var footprint = FootprintGeometry.FootprintOf(world, key.Actor);
            var traversal = Traversal(key.Actor, key.Doors);
            var occupancy = OccupancyTraversal(key.Actor);
            var side = world.Units.Single(u => u.Id == key.Actor).SideId;
            var occupants = world.Physical.Figures.Where(f => f.Id != key.Actor && Knows(key.Actor, f.Id))
                .SelectMany(f => OccupiedCellsOf(f.Id).Select(c => (Cell: c, Side: world.Units.Single(u => u.Id == f.Id).SideId)))
                .ToDictionary(p => p.Cell, p => p.Side);
            var targetCells = OccupiedCellsOf(key.Target);
            IEnumerable<Cell> Goals()
            {
                foreach (var anchor in RangeOriginCandidates(key.Actor, targetCells, world.EffectiveRngOf(key.Actor), HasPositionalRangeModifiers()))
                {
                    if (!SpatialRules.Fits(board, footprint, anchor, traversal) || !SpatialRules.Fits(board, footprint, anchor, occupancy)) continue;
                    var cells = FootprintGeometry.OccupiedCells(footprint, anchor);
                    if (cells.Any(occupants.ContainsKey)) continue;

                    targetWorld.Physical.Figures[actorIndex] = figure with { Position = anchor };
                    targetWorld.Physical = targetWorld.Physical with { Board = board };
                    if (RepossessionRules.CanTarget(targetWorld, key.Actor, anchor, key.Target))
                    {
                        yield return anchor;
                        continue;
                    }
                    // For a blocked sightline, assume only the Doors the actor
                    // could open from this footprint have opened.
                    bool Borders(Edge edge) => cells.Contains(edge.A) || cells.Contains(edge.B);
                    if (!closedDoors.Any(Borders)) continue;
                    var opened = board with { Edges = board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor && Borders(e)
                        ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() };
                    targetWorld.Physical = targetWorld.Physical with { Board = opened };
                    if (RepossessionRules.CanTarget(targetWorld, key.Actor, anchor, key.Target)) yield return anchor;
                }
            }
            // Reverse ordinary cardinal traversal. Friendly figures permit transit;
            // other Sides block transit and every figure blocks a stopping goal.
            Dictionary<Cell, int> field = []; Queue<Cell> queue = new();
            foreach (var goal in Goals()) if (field.TryAdd(goal, 0)) queue.Enqueue(goal);
            while (queue.TryDequeue(out var current))
                foreach (var previous in MovementRules.Neighbors(current))
                {
                    if (field.ContainsKey(previous) || !MovementRules.Inside(board, previous) ||
                        !MovementRules.CanStep(board, footprint, previous, current, traversal) ||
                        FootprintGeometry.OccupiedCells(footprint, current).Any(c => occupants.TryGetValue(c, out var owner) && owner != side)) continue;
                    field[previous] = field[current] + 1;
                    queue.Enqueue(previous);
                }
            return field;
        })).Value;
        return distances.TryGetValue(position, out var distance) ? distance : null;
    }
}
