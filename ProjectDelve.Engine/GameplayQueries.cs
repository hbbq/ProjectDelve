namespace ProjectDelve.Engine;

public interface IGameplayQueries
{
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

    public Cell PositionOf(string unitId) =>
        world.Physical.Figures.Single(f => f.Id == unitId).Position;

    public IReadOnlyList<Cell> OccupiedCellsOf(string unitId) => FootprintGeometry.OccupiedCells(world, unitId);

    public UnitBehavior BehaviorsOf(string unitId) =>
        world.IsUpright(unitId)
            ? world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId).Behaviors
            : UnitBehavior.None;

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
