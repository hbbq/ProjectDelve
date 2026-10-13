namespace ProjectDelve.Engine;

public enum DefaultBehaviorPreviewStopReason
{
    DiceRequired,
    OtherControllerDecision,
    ActivationCompleted,
    StepLimit,
    UnexpectedRandomness,
    NoDecision
}

public sealed record DefaultBehaviorPreviewStep(DecisionKind DecisionKind, string? CandidateKey, string IntendedAction);
public sealed record DefaultBehaviorPreview(string UnitId, bool Advisory,
    IReadOnlyList<DefaultBehaviorPreviewStep> Steps, DefaultBehaviorPreviewStopReason StopReason);

public static partial class GameEngine
{
    public static DefaultBehaviorPreview PreviewDefaultBehaviorActivation(GameState authoritative, string unitId,
        int stepLimit = 8)
    {
        if (stepLimit < 1) throw new ArgumentOutOfRangeException(nameof(stepLimit));
        var selected = authoritative.Units.SingleOrDefault(unit => unit.Id == unitId)
            ?? throw new ArgumentException("The Unit does not exist.", nameof(unitId));
        var token = new ActivationToken(selected.TypeId, selected.SideId);
        var provider = new DefaultAutomatedProvider();
        var random = new PreviewRandomProvider();
        var steps = new List<DefaultBehaviorPreviewStep>();
        EngineResult result;
        if (authoritative.Pending is { Kind: DecisionKind.SelectUnit } selection)
        {
            if (!selection.Candidates.Any(candidate => candidate.Key == unitId))
                throw new ArgumentException("The Unit is not a selectable activation candidate.", nameof(unitId));
            try
            {
                result = Advance(authoritative, new FixedChoiceProvider(unitId), random, false);
            }
            catch (PreviewRandomnessException)
            {
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.UnexpectedRandomness);
            }
            if (result.Events.Any(occurrence => occurrence.Kind == "ActivationCompleted" && occurrence.UnitId == unitId) ||
                result.State.CurrentUnitId != unitId)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.ActivationCompleted);
        }
        else
        {
            if (authoritative.CurrentUnitId != unitId || authoritative.Pending is null)
                throw new ArgumentException("The Unit does not own the current activation decision.", nameof(unitId));
            var copy = authoritative.Copy();
            var canonical = CreateDecision(copy);
            copy.Pending = canonical;
            result = new(copy, [], canonical);
        }

        while (true)
        {
            var request = result.NextInput;
            if (request is null)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.NoDecision);
            if (request.Kind == DecisionKind.RollDice)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.DiceRequired);
            if (request.Token != token || request.UnitId != unitId ||
                request.Roll is { OwnerUnitId: var owner } && owner != unitId)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.OtherControllerDecision);

            var choice = SelectChoice(request, provider, new GameplayQueries(result.State), false);
            var candidate = choice is null ? null : request.Candidates.Single(candidate => candidate.Key == choice);
            steps.Add(new(request.Kind, choice, PreviewIntention(candidate)));

            if (candidate?.Kind == ActivationChoiceKind.EndTurn)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.ActivationCompleted);
            if (steps.Count >= stepLimit)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.StepLimit);

            try
            {
                result = Advance(result.State, new FixedChoiceProvider(choice), random, false);
            }
            catch (PreviewRandomnessException)
            {
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.UnexpectedRandomness);
            }
            if (result.Events.Any(occurrence => occurrence.Kind == "ActivationCompleted" && occurrence.UnitId == unitId) ||
                result.State.CurrentUnitId != unitId)
                return new(unitId, true, steps, DefaultBehaviorPreviewStopReason.ActivationCompleted);
        }
    }

    private static string PreviewIntention(Candidate? candidate)
    {
        if (candidate is null) return "Decline the optional choice";
        if (candidate.Kind == ActivationChoiceKind.Stay) return "Stay in place";
        if (candidate.Kind == ActivationChoiceKind.EndTurn) return "End the Unit activation";
        if (candidate.TryOpenDoor is not null) return $"Try to open the door at {candidate.Door}";
        if (candidate.Action is { } action)
            return candidate.TargetId is { } target ? $"Use {action} on {target}" : $"Use {action}";
        if (candidate.Kind == ActivationChoiceKind.Move)
            return $"Move to {candidate.Destination}";
        if (candidate.FreeAction is { } freeAction) return $"Use {freeAction}";
        if (candidate.BonusAction is { } bonusAction) return $"Use {bonusAction.DisplayName ?? bonusAction.Name}";
        return $"Choose {candidate.Kind}";
    }

    private sealed class FixedChoiceProvider(string? choice) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choice;
    }

    private sealed class PreviewRandomProvider : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => throw new PreviewRandomnessException();
        public int WorldShuffleIndex(int exclusiveMax) => throw new PreviewRandomnessException();
        public AttackFace RollAttackDie() => throw new PreviewRandomnessException();
        public DefenceFace RollDefenceDie() => throw new PreviewRandomnessException();
        public int RollD6() => throw new PreviewRandomnessException();
    }

    private sealed class PreviewRandomnessException : Exception { }
}
