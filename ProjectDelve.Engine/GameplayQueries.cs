namespace ProjectDelve.Engine;

public interface IGameplayQueries
{
    Cell PositionOf(string unitId);
    // Content metadata only; the provider decides whether to use these preferences.
    UnitBehavior BehaviorsOf(string unitId);
    int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId);
    bool CanAttackHostileFrom(string unitId, Cell position);

    // Approach distance ignores Units and this activation's MOV. null means no supported attack
    // position is reachable; undefined LOS does not count as a possible attack.
    // The caller selects edge traversal for its analysis; this never changes movement legality.
    int? DistanceToAttackPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false);
}

internal sealed class GameplayQueries : IGameplayQueries
{
    private readonly GameState world;

    // Keep a detached decision-time snapshot; expose no mutable world data.
    internal GameplayQueries(GameState state) => world = state.Copy();

    public Cell PositionOf(string unitId) =>
        world.Physical.Figures.Single(f => f.Id == unitId).Position;

    public UnitBehavior BehaviorsOf(string unitId) =>
        world.Types.Single(t => t.Id == world.Units.Single(u => u.Id == unitId).TypeId).Behaviors;

    public int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId)
    {
        var first = PositionOf(firstUnitId);
        var second = PositionOf(secondUnitId);
        return Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
    }

    public bool CanAttackHostileFrom(string unitId, Cell position) =>
        world.Units.Any(target => AttackRules.EvaluateFrom(world, unitId, position, target.Id)
            == NormalAttackEvaluation.Possible);

    public int? DistanceToAttackPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false)
    {
        HypotheticalPosition.Validate(world, unitId, position);
        return ApproachRules.Distances(world.Physical.Board, position, closedDoorsTraversable: closedDoorsTraversable)
            .Where(pair => world.Units.Any(target => AttackRules.EvaluateApproachFrom(world, unitId, pair.Key, target.Id)
                == NormalAttackEvaluation.Possible))
            .Select(pair => (int?)pair.Value).Min();
    }
}
