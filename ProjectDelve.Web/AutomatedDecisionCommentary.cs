using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Presentation-only wording for concrete selected behaviors. Never selects a candidate or rolls dice.
internal static class AutomatedDecisionCommentary
{
    internal static string? For(DecisionRequest request, string? key, IGameplayQueries queries, GameState state)
    {
        var candidate = request.Candidates.FirstOrDefault(c => c.Key == key);
        if (candidate is null || request.UnitId is null) return null;
        var type = state.Types.Single(t => t.Id == request.TypeId);
        var behaviors = queries.BehaviorsOf(request.UnitId);
        if (type.DisplayName == "Labyrinth Shaper" && candidate.BonusAction?.ReshapeRoom is not null &&
            behaviors.HasFlag(UnitBehavior.PreferReshapeRoom)) return "The room shall take a new shape.";
        if (type.DisplayName == "Troll" && candidate.TryOpenDoor is not null) return "I'll smash this door!";
        if (candidate.Destination is not null && candidate.Path is { Count: > 1 } &&
            candidate.Kind == ActivationChoiceKind.Move)
        {
            if (type.DisplayName == "Goblin" && request.IsMoveAfterAttack && behaviors.HasFlag(UnitBehavior.BackAwayAfterAttack))
                return "Hit and run!";
            if (type.DisplayName == "Zombie" && behaviors.HasFlag(UnitBehavior.ApproachThroughClosedDoors))
            {
                var before = queries.DistanceToAttackPositionFrom(request.UnitId, queries.PositionOf(request.UnitId), true);
                var after = queries.DistanceToAttackPositionFrom(request.UnitId, candidate.Destination, true);
                if (after.HasValue && (!before.HasValue || after < before)) return "Closer... closer...";
            }
        }
        return null;
    }
}
