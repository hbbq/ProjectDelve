using System.Collections.Concurrent;

namespace ProjectDelve.Engine;

internal sealed partial class GameplayQueries
{
    private readonly ConcurrentDictionary<string, Board> autonomousBoards = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Actor, Cell Anchor), IReadOnlySet<Cell>> discovery = new();
    private readonly ConcurrentDictionary<(string Actor, string Target, bool Doors), IReadOnlyDictionary<Cell, int>> followFields = new();
    private readonly ConcurrentDictionary<(string Actor, string Target), Cell[]> followGoals = new();
    private readonly ConcurrentDictionary<string, HashSet<Cell>> knownOccupants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Actor, bool Doors), IReadOnlyDictionary<Cell, int>> followOriginDistances = new();
    private readonly ConcurrentDictionary<(string Actor, Cell Origin, bool Doors), IReadOnlyDictionary<Cell, IReadOnlyList<Cell>>> autonomousPaths = new();
    private readonly ConcurrentDictionary<string, Cell?> explorationGoals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> sightDistances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FogOfWar.VisionGeometry> visionGeometry = new(StringComparer.Ordinal);

    public string? SummonerOf(string unitId) => world.Units.Single(u => u.Id == unitId).SummonerUnitId;

    public IReadOnlyList<string> KnownFriendlyUnitIds(string unitId) => world.Units
        .Where(u => u.Id != unitId && IsLivingFriendlyUnit(unitId, u.Id)).Select(u => u.Id).ToArray();

    public int CountAdjacentFriendliesFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var cells = FootprintGeometry.OccupiedCells(world, unitId, position);
        return KnownFriendlyUnitIds(unitId).Count(id =>
            SpatialRules.AreFootprintsAdjacent(AutonomousBoard(unitId), cells, OccupiedCellsOf(id)));
    }

    // The target is selected once by the policy. Free goals retain current figure
    // occupancy; terrain-route analysis ignores figures and Cell Markers.
    public int? DistanceToFollowPositionFrom(string unitId, Cell position, string targetId, bool closedDoorsTraversable = false)
    {
        if (unitId == targetId || !IsLivingFriendlyUnit(unitId, targetId)) return null;
        HypotheticalPosition.Validate(world, unitId, position);
        var goals = followGoals.GetOrAdd((unitId, targetId), key =>
        {
            var board = AutonomousBoard(key.Actor);
            var footprint = FootprintGeometry.FootprintOf(world, key.Actor);
            var target = OccupiedCellsOf(key.Target);
            var span = footprint == Footprint.TwoByTwo ? 2 : 1;
            return target.SelectMany(c => Enumerable.Range(-span, span + 2).SelectMany(dy =>
                    Enumerable.Range(-span, span + 2).Select(dx => new Cell(c.X + dx, c.Y + dy))))
                .Distinct().Where(p => SpatialRules.AreFootprintsAdjacent(board,
                    FootprintGeometry.OccupiedCells(footprint, p), target) && FreeAutonomousPlacement(key.Actor, p)).ToArray();
        });
        var type = world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId);
        if (type.MovementPattern is not null)
            return AutonomousPaths(unitId, position, closedDoorsTraversable)
                .Where(p => goals.Contains(p.Key))
                .Select(p => (int?)MovementRules.Cost(p.Value)).DefaultIfEmpty().Min();
        // Select among many friendly targets with one forward field. Only the
        // chosen target needs a reverse field for ranking legal Move candidates.
        if (position == PositionOf(unitId))
        {
            var distances = followOriginDistances.GetOrAdd((unitId, closedDoorsTraversable), key =>
                ApproachRules.Distances(AutonomousBoard(key.Actor), PositionOf(key.Actor),
                    traversal: MovementTraversal.For(world, key.Actor, key.Doors),
                    footprint: FootprintGeometry.FootprintOf(world, key.Actor)));
            return goals.Where(distances.ContainsKey).Select(p => (int?)distances[p]).DefaultIfEmpty().Min();
        }
        var field = followFields.GetOrAdd((unitId, targetId, closedDoorsTraversable), key =>
            ApproachRules.DistancesTo(AutonomousBoard(key.Actor), goals,
                MovementTraversal.For(world, key.Actor, key.Doors), FootprintGeometry.FootprintOf(world, key.Actor)));
        return field.TryGetValue(position, out var distance) ? distance : null;
    }

    public int? DistanceToFriendlyPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) =>
        KnownFriendlyUnitIds(unitId).Select(id => DistanceToFollowPositionFrom(unitId, position, id, closedDoorsTraversable))
            .Where(d => d.HasValue).DefaultIfEmpty().Min();

    public int PotentialDiscoveryAlongPath(string unitId, IReadOnlyList<Cell> path)
    {
        var cells = new HashSet<Cell>();
        foreach (var anchor in path) cells.UnionWith(PotentialDiscovery(unitId, anchor));
        return cells.Count;
    }

    private IReadOnlySet<Cell> PotentialDiscovery(string unitId, Cell anchor) => discovery.GetOrAdd((unitId, anchor), key =>
    {
        var unit = world.Units.Single(u => u.Id == key.Actor);
        var result = new HashSet<Cell>();
        if (!FogOfWar.IsEnabled(world, unit.SideId)) return result;
        var footprint = FootprintGeometry.OccupiedCells(world, key.Actor, key.Anchor);
        // Advisory forecasts retain the decision-time range, like the MOV budget.
        // Actual swept vision reevaluates effective auras at each real transit position.
        var range = sightDistances.GetOrAdd(key.Actor, world.EffectiveSightDistanceOf);
        var board = visionGeometry.GetOrAdd(unit.SideId, _ => new(AutonomousBoard(key.Actor))).Around(footprint, range);
        var known = world.ExploredCells.GetValueOrDefault(unit.SideId) ?? [];
        result.UnionWith(FogOfWar.VisionAreaLocal(board, footprint, range, world.HasPhase(key.Actor)));
        result.ExceptWith(known);
        return result;
    });

    public int? DistanceToExplorationPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        if (!FogOfWar.IsEnabled(world, world.Units.Single(u => u.Id == unitId).SideId)) return null;
        // Closed-door approach preferences never invent a productive exploration route.
        // Retain movement patterns and canonical routes while ignoring temporary figures.
        var goal = explorationGoals.GetOrAdd(unitId, actor => AutonomousPaths(actor, PositionOf(actor), false)
            .Where(p => FreeAutonomousPlacement(actor, p.Key) && PotentialDiscovery(actor, p.Key).Count > 0)
            .OrderBy(p => MovementRules.Cost(p.Value)).ThenBy(p => p.Key.Y).ThenBy(p => p.Key.X)
            .Select(p => p.Key).FirstOrDefault());
        if (goal is null) return null;
        var paths = AutonomousPaths(unitId, position, false);
        return paths.TryGetValue(goal, out var path) ? MovementRules.Cost(path) : null;
    }

    private IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> AutonomousPaths(string actor, Cell origin, bool doors) =>
        autonomousPaths.GetOrAdd((actor, origin, doors), key =>
        {
            var copy = world.Copy();
            var board = AutonomousBoard(key.Actor);
            if (key.Doors) board = board with { Edges = board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() };
            copy.Physical = new(board, [world.Physical.Figures.Single(f => f.Id == key.Actor)]);
            return MovementRules.FindPaths(copy, key.Actor, key.Origin, ignoreCellMarkers: true);
        });

    private bool FreeAutonomousPlacement(string actor, Cell anchor)
    {
        var unit = world.Units.Single(u => u.Id == actor);
        var type = world.Types.Single(t => t.Id == unit.TypeId);
        var cells = FootprintGeometry.OccupiedCells(type.Footprint, anchor);
        var known = MovementTraversal.For(world, actor).KnownCells;
        return cells.All(c => known is null || known.Contains(c)) &&
            SpatialRules.Fits(AutonomousBoard(actor), type.Footprint, anchor, MovementTraversal.OccupancyFor(world, actor)) &&
            !cells.Any(KnownOccupants(actor).Contains);
    }

    private HashSet<Cell> KnownOccupants(string actor) => knownOccupants.GetOrAdd(actor, id =>
        world.Physical.Figures.Where(f => f.Id != id && Knows(id, f.Id)).SelectMany(f => OccupiedCellsOf(f.Id)).ToHashSet());

    private Board AutonomousBoard(string unitId) => autonomousBoards.GetOrAdd(
        world.Units.Single(u => u.Id == unitId).SideId, BuildAutonomousBoard);

    private Board BuildAutonomousBoard(string side)
    {
        var original = world.Physical.Board;
        if (!FogOfWar.IsEnabled(world, side)) return original;
        var known = world.ExploredCells.GetValueOrDefault(side) ?? [];
        // Unknown geometry is never consulted, even for hypothetical vision. An opaque
        // placeholder allows discovery of the first unknown Cell, but not sight beyond it.
        var unknownId = "autonomous-unknown";
        while (original.TerrainTypes.Any(t => t.Id == unknownId)) unknownId += "-";
        var unknown = new TerrainDefinition(unknownId, "Unknown", false, true);
        var board = original with {
            TerrainTypes = original.TerrainTypes.Add(unknown),
            Terrain = original.Terrain.Where(t => known is null || known.Contains(t.Position)).ToList(),
            Edges = original.Edges.Where(e => known is null || known.Contains(e.A) || known.Contains(e.B)).ToList(),
            Machines = original.Machines.Where(m => known is null || known.Contains(m.Position)).ToList()
        };
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (!known.Contains(cell))
                board.Terrain.Add(new(cell) { TypeId = unknown.Id });
        }
        return board;
    }

}
