using System.Reflection;

namespace ProjectDelve.Engine.Tests;

// Frozen pre-memoization attack-query implementation. Unrelated queries delegate to a
// fresh normal snapshot so provider comparisons isolate reuse of attack evaluations.
public class ReferenceAttackQueries : DispatchProxy
{
    private GameState world = null!;
    private IGameplayQueries forward = null!;
    public static IGameplayQueries Create(GameState state)
    {
        var proxy = DispatchProxy.Create<IGameplayQueries, ReferenceAttackQueries>();
        var implementation = (ReferenceAttackQueries)proxy;
        implementation.world = state.Copy();
        implementation.forward = new GameplayQueries(state);
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        args ??= [];
        var unitId = args.Length > 0 ? args[0] as string : null;
        return method!.Name switch
        {
            nameof(IGameplayQueries.CanAttackAnyFrom) => CanAttackAnyFrom(unitId!, (Cell)args[1]!, (IReadOnlyCollection<string>)args[2]!),
            nameof(IGameplayQueries.CanAttackHostileFrom) => CanAttackHostileFrom(unitId!, (Cell)args[1]!),
            nameof(IGameplayQueries.DistanceToNearestAttackableHostileFrom) => DistanceToNearestAttackableHostileFrom(unitId!, (Cell)args[1]!),
            nameof(IGameplayQueries.DistanceToAttackPositionFrom) when args.Length == 3 => DistanceToAttackPositionFrom(unitId!, (Cell)args[1]!, (bool)args[2]!),
            nameof(IGameplayQueries.DistanceToAttackPositionFrom) => DistanceToAttackPositionFrom(unitId!, (Cell)args[1]!, (IReadOnlyCollection<string>)args[2]!, (bool)args[3]!),
            _ => method.Invoke(forward, args)
        };
    }
    private bool Knows(string unitId, string targetId) => FogOfWar.IsUnitObservable(world,
        world.Units.Single(u => u.Id == unitId).SideId, targetId);
    private IReadOnlyList<Cell> OccupiedCellsOf(string unitId) => FootprintGeometry.OccupiedCells(world, unitId);

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

    private bool CanAttackTargetFrom(string unitId, Cell position, string targetId)
    {
        var attacker = world.Units.Single(u => u.Id == unitId);
        if (!Knows(unitId, targetId) || world.Units.SingleOrDefault(u => u.Id == targetId) is not { CurrentHp: > 0 } target ||
            !world.AreHostile(attacker.SideId, target.SideId) || !world.Physical.Figures.Any(f => f.Id == targetId)) return false;
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
}
