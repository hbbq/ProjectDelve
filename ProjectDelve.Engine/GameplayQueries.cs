using System.Collections.Immutable;
using System.Collections.Concurrent;

namespace ProjectDelve.Engine;

public interface IGameplayQueries
{
    int CountAdjacentFriendliesFrom(string unitId, Cell position) => throw new NotSupportedException();
    int? DistanceToFriendlyPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    int? DistanceToExplorationPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    IReadOnlyList<string> KnownHostileUnitIds(string unitId) => throw new NotSupportedException();
    int FootprintDistanceToArea(string unitId, Cell position, ObjectiveArea area) => throw new NotSupportedException();
    int? WholeFootprintRouteDistanceToArea(string unitId, Cell position, ObjectiveArea area, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    Route RouteDefinition(string id) => throw new NotSupportedException();
    ObjectiveArea WaypointArea(string id) => throw new NotSupportedException();
    TerrainKind TerrainAt(Cell cell) => throw new NotSupportedException();
    TerrainDefinition TerrainDefinitionAt(Cell cell) => throw new NotSupportedException();
    int? RouteDistanceToArea(string unitId, Cell position, ObjectiveArea area, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    int StoredQuantityFor(string unitId, ResourceType resource) => StoredQuantityFor(unitId, resource.Id);
    int StoredQuantityFor(string unitId, string resourceTypeId) => throw new NotSupportedException();
    IReadOnlyList<Resource> Resources => throw new NotSupportedException();
    bool CanTransportResource(string unitId) => throw new NotSupportedException();
    IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> MovementPathsFrom(string unitId, Cell position) => throw new NotSupportedException();
    // Hypothetical routes ignore Cell Markers, retaining movement's other traversal and placement checks.
    // These paths are for planning only; actual Move choices come from authoritative candidates.
    IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> PlanningPathsFrom(string unitId, Cell position) => throw new NotSupportedException();
    // Analyze existing Resource Actions at a legal placement with the supplied source marker.
    IReadOnlyList<Candidate> ResourceActionsFrom(string unitId, Cell position, ResourceType? source) => throw new NotSupportedException();
    Cell ResourceRecipientPosition(Candidate action) => throw new NotSupportedException();
    IReadOnlyList<SideObjective> Objectives { get; }
    Cell PositionOf(string unitId);
    IReadOnlyList<Cell> OccupiedCellsOf(string unitId);
    // Content metadata only; the provider decides whether to use these preferences.
    UnitBehavior BehaviorsOf(string unitId);
    int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId);
    // Any supported hostile-target Attack, using authoritative Action targeting.
    bool CanAttackHostileFrom(string unitId, Cell position);
    // Manhattan distance to the nearest legal NormalAttack target; null if none exists.
    int? DistanceToNearestAttackableHostileFrom(string unitId, Cell position);
    // Eight-cell adjacency and ordinary geometric LOS, independent of hostile attack legality.
    bool HasNearbyHostileThreatFrom(string unitId, Cell position);
    GroupOrder EffectiveOrderFor(string unitId) => throw new NotSupportedException();
    int? RouteDistanceTo(string unitId, Cell position, Cell destination, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    IReadOnlyList<string> EligibleAttackOrderTargets(string unitId, OrderTarget target) => throw new NotSupportedException();
    bool CanAttackAnyFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds) => throw new NotSupportedException();
    int? DistanceToAttackPositionFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds,
        bool closedDoorsTraversable = false) =>
        throw new NotSupportedException();
    bool IsLivingFriendlyUnit(string unitId, string targetId) => throw new NotSupportedException();
    bool AreUnitsAdjacent(string firstUnitId, string secondUnitId) => throw new NotSupportedException();
    bool AreUnitsAdjacentFrom(string unitId, Cell position, string targetId) => throw new NotSupportedException();
    int? DistanceToAdjacentPositionFrom(string unitId, Cell position, string targetId,
        bool closedDoorsTraversable = false) => throw new NotSupportedException();
    bool IsClosedDoor(DoorTarget door) => throw new NotSupportedException();
    bool CanInteractWithDoors(string unitId) => throw new NotSupportedException();
    int? DistanceToDoorInteractionPositionFrom(string unitId, Cell position, DoorTarget door,
        bool closedDoorsTraversable = false) =>
        throw new NotSupportedException();
    int EffectiveMovOf(string unitId) => throw new NotSupportedException();
    int EffectiveDefOf(string unitId) => throw new NotSupportedException();
    int EffectiveRngOf(string unitId) => throw new NotSupportedException();
    int? DistanceToUnitFrom(string unitId, Cell position, string targetId,
        bool closedDoorsTraversable = false) => throw new NotSupportedException();
    bool IsWithinEffectiveRangeAndLos(string unitId, Cell position, string targetId) => throw new NotSupportedException();
    int CountAdjacentHostilesFrom(string unitId, Cell position) => throw new NotSupportedException();
    bool IsHostileTo(string unitId, string targetId) => throw new NotSupportedException();
    int EffectiveAttackDice(string unitId, Candidate candidate) => throw new NotSupportedException();
    bool IsUnlimitedAttack(string unitId, Candidate candidate) => throw new NotSupportedException();
    int AttackDefinitionOrder(string unitId, Candidate candidate) => throw new NotSupportedException();
    int? OpenRouteDistanceFromSummonPlacement(string unitId, Cell placement,
        IReadOnlyCollection<string> targetIds) => throw new NotSupportedException();

    // Approach distance ignores Units and this activation's MOV. null means no supported attack
    // position is reachable under the Unit's supported Attack targeting rules.
    // Uses the Unit's traversal capabilities; the caller may additionally permit Closed Doors
    // for approach analysis. That extra preference never changes actual movement legality.
    int? DistanceToAttackPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false);

    // Flee ranking: minimum reachable 1x1 terrain-route distance between occupied Cells,
    // ignoring figure obstacles. This distance path does not use either Unit's footprint.
    // null means no hostile Cell endpoint is reachable under the supplied edge analysis.
    int? DistanceToNearestHostileFrom(string unitId, Cell position, bool closedDoorsTraversable = false);
}

internal sealed class GameplayQueries : IGameplayQueries
{
    private readonly ConcurrentDictionary<(string Actor, bool ClosedDoors, bool Exploration), Lazy<IReadOnlyDictionary<Cell, int>>> autonomousFields = new();
    private readonly ConcurrentDictionary<(string Actor, Cell Position), Lazy<IReadOnlyDictionary<Cell, IReadOnlyList<Cell>>>> explorationPaths = new();
    private readonly ConcurrentDictionary<string, Board> autonomousBoards = new(StringComparer.Ordinal);

    public int CountAdjacentFriendliesFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var board = AutonomousBoard(unitId);
        var cells = FootprintGeometry.OccupiedCells(world, unitId, position);
        return world.Units.Count(u => u.Id != unitId && IsLivingFriendlyUnit(unitId, u.Id) &&
            SpatialRules.AreFootprintsAdjacent(board, cells, OccupiedCellsOf(u.Id)));
    }

    public int? DistanceToFriendlyPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) =>
        AutonomousDistance(unitId, position, closedDoorsTraversable, exploration: false);

    public int? DistanceToExplorationPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) =>
        AutonomousDistance(unitId, position, closedDoorsTraversable, exploration: true);

    private int? AutonomousDistance(string unitId, Cell position, bool closedDoors, bool exploration)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var field = autonomousFields.GetOrAdd((unitId, closedDoors, exploration), static (key, queries) =>
            new(() => queries.BuildAutonomousField(key.Actor, key.ClosedDoors, key.Exploration)), this).Value;
        if (exploration)
        {
            var goal = field.Keys.SingleOrDefault();
            if (goal is null) return null;
            var paths = explorationPaths.GetOrAdd((unitId, position), static (key, queries) =>
                new(() => queries.PlanningPathsFrom(key.Actor, key.Position)), this).Value;
            return paths.TryGetValue(goal, out var path) ? MovementRules.Cost(path) : null;
        }
        return field.TryGetValue(position, out var distance) ? distance : null;
    }

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

    private IReadOnlyDictionary<Cell, int> BuildAutonomousField(string unitId, bool closedDoors, bool exploration)
    {
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        if (exploration && !FogOfWar.IsEnabled(world, side)) return new Dictionary<Cell, int>();
        var known = MovementTraversal.For(world, unitId).KnownCells;
        var board = AutonomousBoard(unitId);
        IReadOnlyList<Cell> unknownCells = exploration ? FogOfWar.UnknownCells(world, side) : [];
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        var traversal = MovementTraversal.For(world, unitId, closedDoors);
        var occupancy = MovementTraversal.OccupancyFor(world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId));
        var friendlies = world.Units.Where(u => u.Id != unitId && IsLivingFriendlyUnit(unitId, u.Id))
            .Select(u => OccupiedCellsOf(u.Id)).ToArray();
        var occupied = world.Physical.Figures.Where(f => f.Id != unitId && Knows(unitId, f.Id))
            .SelectMany(f => OccupiedCellsOf(f.Id)).ToHashSet();
        // Exploration needs a reachable vantage, not merely a terrain route behind
        // an impassable line of hostile figures. Planning retains movement patterns
        // and occupancy while ignoring Banana Peels as hypothetical route barriers.
        var reachableVantages = exploration ? PlanningPathsFrom(unitId, PositionOf(unitId)) : null;
        var phase = world.HasPhase(unitId);
        IEnumerable<Cell> Goals()
        {
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var anchor = new Cell(x, y);
                if (reachableVantages is not null && !reachableVantages.ContainsKey(anchor)) continue;
                var cells = FootprintGeometry.OccupiedCells(footprint, anchor);
                if (cells.Any(c => known is not null && !known.Contains(c) || occupied.Contains(c)) ||
                    !SpatialRules.Fits(board, footprint, anchor, occupancy) || !SpatialRules.Fits(board, footprint, anchor, traversal)) continue;
                if (exploration
                    ? cells.Any(from => unknownCells.Any(to => Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) <= 5 &&
                        AttackRules.HasGeometricVision(board, from, to, ignoreObstacles: phase)))
                    : friendlies.Any(target => !cells.Intersect(target).Any() && SpatialRules.AreFootprintsAdjacent(board, cells, target)))
                    yield return anchor;
            }
        }
        if (exploration)
        {
            // Commit only for this decision to the closest productive vantage. Score
            // every supplied placement with the same patterned, occupancy-aware route
            // calculation; no goal or route survives the decision boundary.
            var goal = Goals().OrderBy(c => MovementRules.Cost(reachableVantages![c]))
                .ThenBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault();
            return goal is null ? new Dictionary<Cell, int>() : new Dictionary<Cell, int> { [goal] = 0 };
        }
        return ApproachRules.DistancesTo(board, Goals(), traversal, footprint);
    }

    public IReadOnlyList<string> KnownHostileUnitIds(string unitId)
    {
        var actor = world.Units.Single(u => u.Id == unitId);
        return world.Units.Where(u => u.CurrentHp > 0 && world.AreHostile(actor.SideId, u.SideId) &&
                Knows(unitId, u.Id) && world.Physical.Figures.Any(f => f.Id == u.Id))
            .Select(u => u.Id).ToArray();
    }
    public int FootprintDistanceToArea(string unitId, Cell position, ObjectiveArea area) =>
        FootprintGeometry.OccupiedCells(FootprintGeometry.FootprintOf(world, unitId), position)
            .Max(c => Math.Max(area.X - c.X, Math.Max(0, c.X - (area.X + area.W - 1))) +
                Math.Max(area.Y - c.Y, Math.Max(0, c.Y - (area.Y + area.H - 1))));
    public int? WholeFootprintRouteDistanceToArea(string unitId, Cell position, ObjectiveArea area, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var distances = areaReturnFields.GetOrAdd((unitId, area, closedDoorsTraversable), static (key, queries) => new(() =>
        {
            var board = queries.world.Physical.Board;
            var footprint = FootprintGeometry.FootprintOf(queries.world, key.Actor);
            var traversal = MovementTraversal.For(queries.world, key.Actor, key.ClosedDoors);
            IEnumerable<Cell> Goals()
            {
                for (var y = key.Area.Y; y < (long)key.Area.Y + key.Area.H; y++)
                for (var x = key.Area.X; x < (long)key.Area.X + key.Area.W; x++)
                {
                    var anchor = new Cell(x, y);
                    if (FootprintGeometry.OccupiedCells(footprint, anchor).All(c => RouteRules.Contains(key.Area, c) &&
                            (traversal.KnownCells is null || traversal.KnownCells.Contains(c))) &&
                        SpatialRules.Fits(board, footprint, anchor, MovementTraversal.OccupancyFor(
                            queries.world.Types.Single(t => t.Id == queries.world.Units.Single(u => u.Id == key.Actor).TypeId))) &&
                        SpatialRules.Fits(board, footprint, anchor, traversal))
                        yield return anchor;
                }
            }
            return ApproachRules.DistancesTo(board, Goals(), traversal, footprint);
        }), this).Value;
        return distances.TryGetValue(position, out var distance) ? distance : null;
    }
    public int EffectiveDefOf(string unitId) => world.EffectiveDefOf(unitId);
    public Route RouteDefinition(string id) => world.Routes.Single(r => r.Id == id);
    public ObjectiveArea WaypointArea(string id) => world.Waypoints.Single(w => w.Id == id).Area;
    public TerrainKind TerrainAt(Cell cell) => world.Physical.Board.TerrainAt(cell);
    public TerrainDefinition TerrainDefinitionAt(Cell cell) => world.Physical.Board.TerrainDefinitionAt(cell);
    public int? RouteDistanceToArea(string unitId, Cell position, ObjectiveArea area, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        var distances = ApproachRules.Distances(world.Physical.Board, position,
            traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable), footprint: footprint);
        return distances.Where(p => FootprintGeometry.OccupiedCells(footprint, p.Key)
                .Any(c => RouteRules.Contains(area, c)))
            .Select(p => (int?)p.Value).Min();
    }
    private readonly GameState world;
    // Results belong only to this detached decision snapshot. Keep target-specific
    // answers separate from the all-hostiles aggregate: Orders may supply a subset.
    // No paths, candidate ordering, RNG or mutable rules state are cached here.
    private readonly ConcurrentDictionary<(string Actor, Cell Position, string Target), bool> attackTargets = new();
    private readonly ConcurrentDictionary<(string Actor, Cell Position), bool> attackPositions = new();
    private readonly ConcurrentDictionary<(string Actor, string Target), bool> attackAvailability = new();
    private readonly ConcurrentDictionary<(string Actor, bool ClosedDoors), Lazy<IReadOnlyDictionary<Cell, int>>> approachFields = new();
    private readonly ConcurrentDictionary<(string Actor, bool ClosedDoors, string Target), Lazy<IReadOnlyDictionary<Cell, int>>> targetedApproachFields = new();
    // A returning guard scores many legal destinations against one immutable area.
    // Reverse traversal once per decision snapshot, rather than flood once per candidate.
    private readonly ConcurrentDictionary<(string Actor, ObjectiveArea Area, bool ClosedDoors), Lazy<IReadOnlyDictionary<Cell, int>>> areaReturnFields = new();

    // Keep a detached decision-time snapshot; expose no mutable world data.
    internal GameplayQueries(GameState state) => world = state.Copy();

    public IReadOnlyList<Resource> Resources => world.Resources.Where(r =>
        FogOfWar.IsExplored(world, world.Pending?.Token?.SideId ?? world.ActiveToken?.SideId, r.Position)).ToArray();
    private bool Knows(string unitId, string targetId) => FogOfWar.IsUnitObservable(world,
        world.Units.Single(u => u.Id == unitId).SideId, targetId);
    public int StoredQuantityFor(string unitId, ResourceType resource) => StoredQuantityFor(unitId, resource.Id);
    public int StoredQuantityFor(string unitId, string resourceTypeId)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return world.StoredQuantity(unit.SideId, unit.TypeId, resourceTypeId);
    }

    public bool CanTransportResource(string unitId) =>
        FootprintGeometry.FootprintOf(world, unitId) == Footprint.OneByOne &&
        world.ActivePassives<TransportResource>(unitId).Any();

    public IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> MovementPathsFrom(string unitId, Cell position) =>
        MovementRules.FindPaths(world, unitId, position);

    public IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> PlanningPathsFrom(string unitId, Cell position) =>
        MovementRules.FindPaths(world, unitId, position, ignoreCellMarkers: true);

    public IReadOnlyList<Candidate> ResourceActionsFrom(string unitId, Cell position, ResourceType? source)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        if (!world.IsUpright(unitId)) return [];
        var projected = world.Copy();
        var index = projected.Physical.Figures.FindIndex(f => f.Id == unitId);
        projected.Physical.Figures[index] = projected.Physical.Figures[index] with { Position = position };
        projected.Resources.RemoveAll(r => r.Position == position);
        if (source is not null) projected.Resources.Add(new(source, position));
        var actor = projected.Units.Single(u => u.Id == unitId);
        return ResourceRules.Candidates(projected, actor).Concat(MachineRules.Candidates(projected, actor))
            .Where(c => FogOfWar.Allows(projected, actor.SideId, c)).ToList().AsReadOnly();
    }

    public Cell ResourceRecipientPosition(Candidate action) => action.MachineId is { } machineId
        ? world.Physical.Board.Machines.Single(m => m.Id == machineId).Position
        : PositionOf(action.TargetId!);

    public IReadOnlyList<SideObjective> Objectives => world.Objectives.AsReadOnly();

    public Cell PositionOf(string unitId) =>
        world.Physical.Figures.Single(f => f.Id == unitId).Position;

    public IReadOnlyList<Cell> OccupiedCellsOf(string unitId) => FootprintGeometry.OccupiedCells(world, unitId);

    public UnitBehavior BehaviorsOf(string unitId) =>
        world.IsUpright(unitId)
            ? world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId).Behaviors
            : UnitBehavior.None;

    public GroupOrder EffectiveOrderFor(string unitId)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return world.OrderFor(unit.Token);
    }

    public int? RouteDistanceTo(string unitId, Cell position, Cell destination, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var distances = ApproachRules.Distances(world.Physical.Board, position, destination,
            traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable),
            footprint: FootprintGeometry.FootprintOf(world, unitId));
        return distances.TryGetValue(destination, out var distance) ? distance : null;
    }

    public IReadOnlyList<string> EligibleAttackOrderTargets(string unitId, OrderTarget target)
    {
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && Knows(unitId, u.Id) && world.AreHostile(side, u.SideId) &&
                world.Physical.Figures.Any(f => f.Id == u.Id) && target.Kind switch
                {
                    OrderTargetKind.Unit => u.Id == target.UnitId,
                    OrderTargetKind.Group => u.Token == target.Group!,
                    OrderTargetKind.Side => u.SideId == target.SideId,
                    _ => false
                })
            .Select(u => u.Id).ToList().AsReadOnly();
    }

    public bool CanAttackAnyFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return targetIds.Any(targetId => CanAttackTargetFrom(unitId, position, targetId));
    }

    public int? DistanceToAttackPositionFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds,
        bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        int? best = null;
        foreach (var targetId in targetIds)
        {
            var distances = targetedApproachFields.GetOrAdd((unitId, closedDoorsTraversable, targetId),
                static (key, queries) => new(() => queries.BuildApproachField(key.Actor, key.ClosedDoors,
                    key.Target, targeted: true)), this).Value;
            if (distances.TryGetValue(position, out var distance) && (!best.HasValue || distance < best.Value)) best = distance;
        }
        return best;
    }

    private IReadOnlyDictionary<Cell, int> BuildApproachField(string unitId, bool closedDoorsTraversable,
        string? targetId = null, bool targeted = false)
    {
        var board = world.Physical.Board;
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        IEnumerable<Cell> AttackPositions()
        {
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var position = new Cell(x, y);
                // The general query requires normally occupiable attack positions.
                // The explicit-target query historically accepts traversal-only
                // positions (e.g. Phase inside rock). Preserve that distinction.
                if (targeted
                    ? FootprintGeometry.OccupiedCells(footprint, position).All(c => MovementRules.Inside(board, c)) &&
                        CanAttackTargetFrom(unitId, position, targetId!)
                    : SpatialRules.Fits(board, footprint, position, MovementTraversal.OccupancyFor(
                        world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId))) && CanAttackFrom(unitId, position))
                    yield return position;
            }
        }
        return ApproachRules.DistancesTo(board, AttackPositions(),
            MovementTraversal.For(world, unitId, closedDoorsTraversable), footprint);
    }

    public bool IsLivingFriendlyUnit(string unitId, string targetId)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return Knows(unitId, targetId) && world.Units.SingleOrDefault(u => u.Id == targetId) is { CurrentHp: > 0 } target &&
            target.SideId == unit.SideId && world.Physical.Figures.Any(f => f.Id == targetId);
    }

    public bool AreUnitsAdjacent(string firstUnitId, string secondUnitId) =>
        SpatialRules.AreAdjacent(world, firstUnitId, secondUnitId);

    public bool AreUnitsAdjacentFrom(string unitId, Cell position, string targetId)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return SpatialRules.AreFootprintsAdjacent(world.Physical.Board,
            FootprintGeometry.OccupiedCells(world, unitId, position), OccupiedCellsOf(targetId));
    }

    public int? DistanceToAdjacentPositionFrom(string unitId, Cell position, string targetId,
        bool closedDoorsTraversable = false)
    {
        if (!Knows(unitId, targetId)) return null;
        HypotheticalPosition.Validate(world, unitId, position);
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        var targetCells = OccupiedCellsOf(targetId);
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable), footprint: footprint)
            .Where(pair => SpatialRules.AreFootprintsAdjacent(world.Physical.Board,
                FootprintGeometry.OccupiedCells(footprint, pair.Key), targetCells))
            .Select(pair => (int?)pair.Value).Min();
    }

    public bool IsClosedDoor(DoorTarget door) =>
        world.Physical.Board.EdgeBetween(door.A, door.B) == EdgeKind.ClosedDoor;

    public bool CanInteractWithDoors(string unitId)
    {
        return world.AbilitiesOf(unitId).Any(ability => ability.Definition.Mechanic is OpenDoor or TryOpenDoor &&
            world.CanUseAbility(unitId, ability));
    }

    public int? DistanceToDoorInteractionPositionFrom(string unitId, Cell position, DoorTarget door,
        bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        if (!IsClosedDoor(door) || !CanInteractWithDoors(unitId)) return null;
        if (!FogOfWar.IsEdgeObservable(world, world.Units.Single(u => u.Id == unitId).SideId,
            new(door.A, door.B, EdgeKind.ClosedDoor))) return null;
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable), footprint: footprint)
            .Where(pair => FootprintGeometry.OccupiedCells(footprint, pair.Key)
                .Any(cell => cell == door.A || cell == door.B))
            .Select(pair => (int?)pair.Value).Min();
    }

    private bool CanAttackTargetFrom(string unitId, Cell position, string targetId)
    {
        // Knowledge, life, placement and Side are fixed independently of the proposed
        // position. Do not retain a per-Cell false entry for every friendly/unknown Unit.
        if (!attackAvailability.GetOrAdd((unitId, targetId), static (key, queries) =>
            queries.IsAttackTargetAvailable(key.Actor, key.Target), this)) return false;
        return attackTargets.GetOrAdd((unitId, position, targetId), static (key, queries) =>
            queries.EvaluateAttackTargetFrom(key.Actor, key.Position, key.Target), this);
    }

    private bool IsAttackTargetAvailable(string unitId, string targetId)
    {
        var attacker = world.Units.Single(u => u.Id == unitId);
        return Knows(unitId, targetId) && world.Units.SingleOrDefault(u => u.Id == targetId) is { CurrentHp: > 0 } target &&
            world.AreHostile(attacker.SideId, target.SideId) && world.Physical.Figures.Any(f => f.Id == targetId);
    }

    private bool CanAttackFrom(string unitId, Cell position) =>
        attackPositions.GetOrAdd((unitId, position), static (key, queries) =>
            queries.world.Units.Any(target => queries.CanAttackTargetFrom(key.Actor, key.Position, target.Id)), this);

    private bool EvaluateAttackTargetFrom(string unitId, Cell position, string targetId)
    {
        var attacker = world.Units.Single(u => u.Id == unitId);
        if (AttackRules.EvaluateApproachFrom(world, unitId, position, targetId) == UnitTargetEvaluation.Possible ||
            world.AbilitiesOf(unitId).Where(ability => ability.Definition.Mechanic is ClawAttack or FireBreath or TargetUnitAttackMechanic)
                .Any(ability => AttackRules.EvaluateAbilityFrom(world, unitId, position, targetId, ability)
                    == UnitTargetEvaluation.Possible)) return true;

        // Approach planning asks whether a source can supply the Attack, independently
        // of this activation's spent Action budget. Native and acquired grants share
        // the same remaining-use and Posture checks as current Action candidates.
        if (!world.AbilitiesOf(unitId).Any(grant => grant.Definition.Mechanic is FireballMechanic &&
                world.CanUseAbility(unitId, grant))) return false;
        var targetCells = OccupiedCellsOf(targetId);
        var range = world.EffectiveRngOf(unitId);
        return FootprintGeometry.OccupiedCells(world, unitId, position).Any(source =>
            Enumerable.Range(0, world.Physical.Board.Width).SelectMany(x =>
                    Enumerable.Range(0, world.Physical.Board.Height).Select(y => new Cell(x, y)))
                .Where(center => FogOfWar.IsExplored(world, attacker.SideId, center) && Math.Abs(center.X - source.X) + Math.Abs(center.Y - source.Y) <= range &&
                    AttackRules.HasUnitLineOfSight(world, unitId, source, center))
                .Any(center => targetCells.Any(targetCell =>
                    Math.Max(Math.Abs(targetCell.X - center.X), Math.Abs(targetCell.Y - center.Y)) <= 1 &&
                    AttackRules.HasGeometricLineOfSight(world.Physical.Board, center, targetCell))));
    }

    public int EffectiveMovOf(string unitId) => world.EffectiveMovOf(unitId);
    public int EffectiveRngOf(string unitId) => world.EffectiveRngOf(unitId);

    public int? DistanceToUnitFrom(string unitId, Cell position, string targetId,
        bool closedDoorsTraversable = false)
    {
        if (!Knows(unitId, targetId)) return null;
        HypotheticalPosition.Validate(world, unitId, position);
        var distances = ApproachRules.Distances(world.Physical.Board, position,
            traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable),
            footprint: FootprintGeometry.FootprintOf(world, unitId));
        return OccupiedCellsOf(targetId).Select(cell =>
                distances.TryGetValue(cell, out var distance) ? (int?)distance : null)
            .Where(distance => distance.HasValue).Min();
    }

    public bool IsWithinEffectiveRangeAndLos(string unitId, Cell position, string targetId)
    {
        if (!Knows(unitId, targetId)) return false;
        HypotheticalPosition.Validate(world, unitId, position);
        var range = world.EffectiveRngOf(unitId);
        return range > 0 && FootprintGeometry.OccupiedCells(world, unitId, position).Any(from =>
            OccupiedCellsOf(targetId).Any(to => from != to &&
                (range == 1 ? Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) == 1 :
                    Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) <= range) &&
                AttackRules.HasUnitLineOfSight(world, unitId, from, to, targetId)));
    }

    public int CountAdjacentHostilesFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Count(u => u.CurrentHp > 0 && Knows(unitId, u.Id) && world.AreHostile(side, u.SideId) &&
            SpatialRules.AreAdjacent(world, unitId, u.Id, position));
    }

    public bool IsHostileTo(string unitId, string targetId)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return Knows(unitId, targetId) && world.Units.SingleOrDefault(u => u.Id == targetId) is { CurrentHp: > 0 } target &&
            world.AreHostile(unit.SideId, target.SideId) && world.Physical.Figures.Any(f => f.Id == targetId);
    }

    public int EffectiveAttackDice(string unitId, Candidate candidate)
    {
        if (candidate.Ability?.Definition.Mechanic is TargetUnitAttackMechanic shell) return shell.Atk;
        if (candidate.Action is not (UnitAction.NormalAttack or UnitAction.ClawAttack or
            UnitAction.FireBreath or UnitAction.Fireball)) return -1;
        var targets = CandidateTargets(candidate).Where(id => IsHostileTo(unitId, id)).ToList();
        if (targets.Count == 0)
            return AttackRules.AttackDice(world, unitId, null, candidate.Action.Value, candidate.Ability?.Definition.Mechanic);
        return targets.Max(target => AttackRules.AttackDice(world, unitId,
            candidate.Action is UnitAction.FireBreath or UnitAction.Fireball ? null : target, candidate.Action.Value,
            candidate.Ability?.Definition.Mechanic));
    }

    public bool IsUnlimitedAttack(string unitId, Candidate candidate) => candidate.Ability is { } grant
        ? grant.Definition.Usage.MaxUses is null
        : true; // The baseline Attack has no use limit.

    public int AttackDefinitionOrder(string unitId, Candidate candidate)
    {
        if (candidate.Action is not { } action) return int.MaxValue;
        var type = world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId);
        var order = type.AttackDefinitionOrder.IsDefaultOrEmpty
            ? ImmutableArray.Create(UnitAction.NormalAttack, UnitAction.FireBreath,
                UnitAction.ClawAttack, UnitAction.Fireball)
            : type.AttackDefinitionOrder;
        var index = order.IndexOf(action);
        return index >= 0 ? index : int.MaxValue;
    }

    public int? OpenRouteDistanceFromSummonPlacement(string unitId, Cell placement,
        IReadOnlyCollection<string> targetIds)
    {
        return targetIds.Where(targetId => Knows(unitId, targetId)).SelectMany(targetId => OccupiedCellsOf(targetId).Select(targetCell =>
                ApproachRules.Distance(world.Physical.Board, placement, targetCell,
                    traversal: new(KnownCells: MovementTraversal.For(world, unitId).KnownCells))))
            .Where(distance => distance.HasValue).Min();
    }

    private static IEnumerable<string> CandidateTargets(Candidate candidate) =>
        (candidate.TargetId is null ? candidate.TargetIds : candidate.TargetIds.Prepend(candidate.TargetId))
        .Distinct(StringComparer.Ordinal);

    public int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId)
    {
        return SpatialRules.ManhattanDistance(OccupiedCellsOf(firstUnitId), OccupiedCellsOf(secondUnitId));
    }

    public bool CanAttackHostileFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return CanAttackFrom(unitId, position);
    }

    public int? DistanceToNearestAttackableHostileFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return world.Units
            .Where(target => CanAttackTargetFrom(unitId, position, target.Id))
            .Select(target => (int?)SpatialRules.ManhattanDistance(
                FootprintGeometry.OccupiedCells(world, unitId, position), OccupiedCellsOf(target.Id)))
            .Min();
    }

    public int? DistanceToAttackPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var distances = approachFields.GetOrAdd((unitId, closedDoorsTraversable), static (key, queries) =>
            new(() => queries.BuildApproachField(key.Actor, key.ClosedDoors)), this).Value;
        return distances.TryGetValue(position, out var distance) ? distance : null;
    }

    public bool HasNearbyHostileThreatFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && Knows(unitId, u.Id) && world.AreHostile(side, u.SideId))
            .Any(hostile => SpatialRules.AreAdjacent(world, unitId, hostile.Id, position));
    }

    public int? DistanceToNearestHostileFrom(string unitId, Cell position, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && Knows(unitId, u.Id) && world.AreHostile(side, u.SideId))
            .SelectMany(u => FootprintGeometry.OccupiedCells(world, unitId, position)
                .SelectMany(from => OccupiedCellsOf(u.Id).Select(to =>
                    ApproachRules.Distance(world.Physical.Board, from, to,
                        traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable)))))
            .Where(distance => distance.HasValue).Min();
    }
}
