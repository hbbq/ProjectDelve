using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public interface IGameplayQueries
{
    Route RouteDefinition(string id) => throw new NotSupportedException();
    ObjectiveArea WaypointArea(string id) => throw new NotSupportedException();
    TerrainKind TerrainAt(Cell cell) => throw new NotSupportedException();
    int? RouteDistanceToArea(string unitId, Cell position, ObjectiveArea area, bool closedDoorsTraversable = false) => throw new NotSupportedException();
    int StoredQuantityFor(string unitId, ResourceType resource) => throw new NotSupportedException();
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
    public int EffectiveDefOf(string unitId) => world.EffectiveDefOf(unitId);
    public Route RouteDefinition(string id) => world.Routes.Single(r => r.Id == id);
    public ObjectiveArea WaypointArea(string id) => world.Waypoints.Single(w => w.Id == id).Area;
    public TerrainKind TerrainAt(Cell cell) => world.Physical.Board.TerrainAt(cell);
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

    // Keep a detached decision-time snapshot; expose no mutable world data.
    internal GameplayQueries(GameState state) => world = state.Copy();

    public IReadOnlyList<Resource> Resources => world.Resources.Where(r =>
        FogOfWar.IsExplored(world, world.Pending?.Token?.SideId ?? world.ActiveToken?.SideId, r.Position)).ToArray();
    private bool Knows(string unitId, string targetId) => FogOfWar.IsUnitObservable(world,
        world.Units.Single(u => u.Id == unitId).SideId, targetId);
    public int StoredQuantityFor(string unitId, ResourceType resource)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return world.StoredQuantity(unit.SideId, unit.TypeId, resource.Id);
    }

    public bool CanTransportResource(string unitId) =>
        world.IsUpright(unitId) && FootprintGeometry.FootprintOf(world, unitId) == Footprint.OneByOne &&
        world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId).TransportResource is not null;

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
        return world.OrderFor(new(unit.TypeId, unit.SideId));
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
                    OrderTargetKind.Group => u.TypeId == target.Group!.TypeId && u.SideId == target.Group.SideId,
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
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable),
                footprint: FootprintGeometry.FootprintOf(world, unitId))
            .Where(pair => targetIds.Any(targetId => CanAttackTargetFrom(unitId, pair.Key, targetId)))
            .Select(pair => (int?)pair.Value).Min();
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
        var type = world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId);
        return world.HasOpenDoor(unitId) || type.TryOpenDoor is not null;
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
        var attacker = world.Units.Single(u => u.Id == unitId);
        if (!Knows(unitId, targetId) || world.Units.SingleOrDefault(u => u.Id == targetId) is not { CurrentHp: > 0 } target ||
            !world.AreHostile(attacker.SideId, target.SideId) || !world.Physical.Figures.Any(f => f.Id == targetId)) return false;
        if (new[] { UnitAction.NormalAttack, UnitAction.ClawAttack, UnitAction.FireBreath }
            .Any(action => AttackRules.EvaluateApproachFrom(world, unitId, position, targetId, action)
                == UnitTargetEvaluation.Possible)) return true;

        var unit = world.Units.Single(u => u.Id == unitId);
        var type = world.Types.Single(t => t.Id == unit.TypeId);
        if (!world.IsUpright(unitId) || !type.Actions.HasFlag(UnitAction.Fireball) || type.Fireball is null ||
            unit.FireballUses is not { RemainingUses: > 0 }) return false;
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
        if (candidate.Action == UnitAction.ShootShell) return world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId).ShootShell!.Atk;
        if (candidate.Action is not (UnitAction.NormalAttack or UnitAction.ClawAttack or
            UnitAction.FireBreath or UnitAction.Fireball)) return -1;
        var targets = CandidateTargets(candidate).Where(id => IsHostileTo(unitId, id)).ToList();
        if (targets.Count == 0)
            return AttackRules.AttackDice(world, unitId, null, candidate.Action.Value);
        return targets.Max(target => AttackRules.AttackDice(world, unitId,
            candidate.Action is UnitAction.FireBreath or UnitAction.Fireball ? null : target, candidate.Action.Value));
    }

    public bool IsUnlimitedAttack(string unitId, Candidate candidate) => candidate.Action != UnitAction.Fireball;

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
        var type = world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId);
        if (type.SummonAdjacent is null) return null;
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
        return world.Units.Any(target => CanAttackTargetFrom(unitId, position, target.Id));
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
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable),
                footprint: FootprintGeometry.FootprintOf(world, unitId))
            .Where(pair => SpatialRules.Fits(world.Physical.Board, FootprintGeometry.FootprintOf(world, unitId), pair.Key) &&
                world.Units.Any(target => CanAttackTargetFrom(unitId, pair.Key, target.Id)))
            .Select(pair => (int?)pair.Value).Min();
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
