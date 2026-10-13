namespace ProjectDelve.Engine;

public interface IGameplayQueries
{
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
    int? RouteDistanceTo(string unitId, Cell position, Cell destination) => throw new NotSupportedException();
    IReadOnlyList<string> EligibleAttackOrderTargets(string unitId, OrderTarget target) => throw new NotSupportedException();
    bool CanAttackAnyFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds) => throw new NotSupportedException();
    int? DistanceToAttackPositionFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds) =>
        throw new NotSupportedException();
    bool IsLivingFriendlyUnit(string unitId, string targetId) => throw new NotSupportedException();
    bool AreUnitsAdjacent(string firstUnitId, string secondUnitId) => throw new NotSupportedException();
    int? DistanceToAdjacentPositionFrom(string unitId, Cell position, string targetId) => throw new NotSupportedException();
    bool IsClosedDoor(DoorTarget door) => throw new NotSupportedException();
    bool CanInteractWithDoors(string unitId) => throw new NotSupportedException();
    int? DistanceToDoorInteractionPositionFrom(string unitId, Cell position, DoorTarget door) =>
        throw new NotSupportedException();

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
    private readonly GameState world;

    // Keep a detached decision-time snapshot; expose no mutable world data.
    internal GameplayQueries(GameState state) => world = state.Copy();

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

    public int? RouteDistanceTo(string unitId, Cell position, Cell destination)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var distances = ApproachRules.Distances(world.Physical.Board, position, destination,
            traversal: MovementTraversal.For(world, unitId),
            footprint: FootprintGeometry.FootprintOf(world, unitId));
        return distances.TryGetValue(destination, out var distance) ? distance : null;
    }

    public IReadOnlyList<string> EligibleAttackOrderTargets(string unitId, OrderTarget target)
    {
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && u.SideId != side &&
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

    public int? DistanceToAttackPositionFrom(string unitId, Cell position, IReadOnlyCollection<string> targetIds)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId),
                footprint: FootprintGeometry.FootprintOf(world, unitId))
            .Where(pair => targetIds.Any(targetId => CanAttackTargetFrom(unitId, pair.Key, targetId)))
            .Select(pair => (int?)pair.Value).Min();
    }

    public bool IsLivingFriendlyUnit(string unitId, string targetId)
    {
        var unit = world.Units.Single(u => u.Id == unitId);
        return world.Units.SingleOrDefault(u => u.Id == targetId) is { CurrentHp: > 0 } target &&
            target.SideId == unit.SideId && world.Physical.Figures.Any(f => f.Id == targetId);
    }

    public bool AreUnitsAdjacent(string firstUnitId, string secondUnitId) =>
        SpatialRules.AreAdjacent(world, firstUnitId, secondUnitId);

    public int? DistanceToAdjacentPositionFrom(string unitId, Cell position, string targetId)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        var targetCells = OccupiedCellsOf(targetId);
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId), footprint: footprint)
            .Where(pair => SpatialRules.AreFootprintsAdjacent(world.Physical.Board,
                FootprintGeometry.OccupiedCells(footprint, pair.Key), targetCells))
            .Select(pair => (int?)pair.Value).Min();
    }

    public bool IsClosedDoor(DoorTarget door) =>
        world.Physical.Board.EdgeBetween(door.A, door.B) == EdgeKind.ClosedDoor;

    public bool CanInteractWithDoors(string unitId)
    {
        var type = world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId);
        return type.FreeActions.HasFlag(UnitFreeAction.OpenDoor) || type.TryOpenDoor is not null;
    }

    public int? DistanceToDoorInteractionPositionFrom(string unitId, Cell position, DoorTarget door)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        if (!IsClosedDoor(door) || !CanInteractWithDoors(unitId)) return null;
        var footprint = FootprintGeometry.FootprintOf(world, unitId);
        return ApproachRules.Distances(world.Physical.Board, position,
                traversal: MovementTraversal.For(world, unitId), footprint: footprint)
            .Where(pair => FootprintGeometry.OccupiedCells(footprint, pair.Key)
                .Any(cell => cell == door.A || cell == door.B))
            .Select(pair => (int?)pair.Value).Min();
    }

    private bool CanAttackTargetFrom(string unitId, Cell position, string targetId)
    {
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
                .Where(center => Math.Abs(center.X - source.X) + Math.Abs(center.Y - source.Y) <= range &&
                    AttackRules.HasUnitLineOfSight(world, unitId, source, center))
                .Any(center => targetCells.Any(targetCell =>
                    Math.Max(Math.Abs(targetCell.X - center.X), Math.Abs(targetCell.Y - center.Y)) <= 1 &&
                    AttackRules.HasGeometricLineOfSight(world.Physical.Board, center, targetCell))));
    }

    public int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId)
    {
        return SpatialRules.ManhattanDistance(OccupiedCellsOf(firstUnitId), OccupiedCellsOf(secondUnitId));
    }

    public bool CanAttackHostileFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return world.Units.Any(target => AttackRules.EvaluateAvailableApproachFrom(world, unitId, position, target.Id)
            == UnitTargetEvaluation.Possible);
    }

    public int? DistanceToNearestAttackableHostileFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return world.Units
            .Where(target => AttackRules.EvaluateFrom(world, unitId, position, target.Id)
                == UnitTargetEvaluation.Possible)
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
                world.Units.Any(target => AttackRules.EvaluateAvailableApproachFrom(world, unitId, pair.Key, target.Id)
                == UnitTargetEvaluation.Possible))
            .Select(pair => (int?)pair.Value).Min();
    }

    public bool HasNearbyHostileThreatFrom(string unitId, Cell position)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && u.SideId != side)
            .Any(hostile => SpatialRules.AreAdjacent(world, unitId, hostile.Id, position));
    }

    public int? DistanceToNearestHostileFrom(string unitId, Cell position, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        var side = world.Units.Single(u => u.Id == unitId).SideId;
        return world.Units.Where(u => u.CurrentHp > 0 && u.SideId != side)
            .SelectMany(u => FootprintGeometry.OccupiedCells(world, unitId, position)
                .SelectMany(from => OccupiedCellsOf(u.Id).Select(to =>
                    ApproachRules.Distance(world.Physical.Board, from, to,
                        traversal: MovementTraversal.For(world, unitId, closedDoorsTraversable)))))
            .Where(distance => distance.HasValue).Min();
    }
}
