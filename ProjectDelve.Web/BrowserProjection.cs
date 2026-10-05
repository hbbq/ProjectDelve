using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public enum InteractionKind { Direct, Unit, Position, Door }
public sealed record ChoiceInteraction(InteractionKind Kind, string? UnitId = null,
    Cell? Position = null, Edge? Door = null);
public sealed record BrowserChoice(string? Key, string Label, string? EntryId,
    bool Relevant, ChoiceInteraction Interaction, IReadOnlyList<string> AffectedUnitIds);
public sealed record BrowserDecision(string? UnitId, string Prompt,
    IReadOnlyList<BrowserChoice> Candidates, BrowserChoice? NoneChoice);
public sealed record CardEntry(CardEntryDescription Content, AbilityUses? Uses);
public sealed record UnitCard(string DisplayName, IReadOnlyList<CardEntry> Entries);
public enum OutcomeRole { Notice, Movement, AttackTarget, AttackSummary, Damage, Healing, Death, DoorAttempt, DoorOpened }
public sealed record BrowserOutcome(OutcomeRole Role, string Text, string? UnitId = null,
    string? TargetId = null, List<Cell>? Path = null, Edge? Door = null,
    int Hits = 0, int Blocks = 0, int Damage = 0);
public sealed record BrowserResolutionStep(int EventIndex, IReadOnlyDictionary<string, UnitCard> Cards);
public sealed record BrowserPresentation(IReadOnlyDictionary<string, UnitCard> Cards,
    BrowserDecision? Decision, IReadOnlyList<BrowserOutcome> Events,
    IReadOnlyList<BrowserResolutionStep> ResolutionSteps);

// An adapter over authoritative engine results; never generates or filters legal choices.
// The raw EngineResult remains available, including all progressive StateAfter snapshots.
public static class BrowserProjection
{
    public static BrowserPresentation Create(EngineResult result) => new(
        Cards(result.State), Decision(result.NextInput, result.State),
        result.Events.Select(e => Outcome(e, result.State)).ToArray(),
        result.ResolutionSteps.Select(s => new BrowserResolutionStep(s.EventIndex, Cards(s.StateAfter))).ToArray());

    public static IReadOnlyDictionary<string, UnitCard> Cards(GameState state) => state.Units.ToDictionary(u => u.Id, u =>
    {
        var type = state.Types.Single(t => t.Id == u.TypeId);
        return new UnitCard(type.DisplayName ?? type.Id,
            type.CardEntries(state.Types).Select(entry => new CardEntry(entry, type.UsesFor(u, entry.Id))).ToArray());
    });

    public static BrowserDecision? Decision(DecisionRequest? request, GameState state)
    {
        if (request is null) return null;
        var type = state.Types.Single(t => t.Id == request.TypeId);
        var entries = type.CardEntries(state.Types).ToDictionary(e => e.Id);
        var candidates = request.Candidates.Select(c =>
        {
            var entryId = ContentDescriptions.EntryId(c);
            var entry = entryId is null ? null : entries.GetValueOrDefault(entryId);
            var interaction = c.Kind == ActivationChoiceKind.SelectUnit
                ? new ChoiceInteraction(InteractionKind.Unit, UnitId: c.Key)
                : c.Door is not null ? new(InteractionKind.Door, Door: c.Door)
                : c.Kind == ActivationChoiceKind.Stay ? new(InteractionKind.Unit, UnitId: request.UnitId)
                : c.Destination is not null ? new(InteractionKind.Position, Position: c.Destination)
                : c.TargetId is not null ? new(InteractionKind.Unit, UnitId: c.TargetId)
                : new ChoiceInteraction(InteractionKind.Direct);
            var label = c.Kind switch
            {
                ActivationChoiceKind.Stay => "Stay here",
                ActivationChoiceKind.EndTurn => "End Turn",
                ActivationChoiceKind.SelectUnit => $"Select {c.Key}",
                ActivationChoiceKind.Move => $"Move to ({c.Destination!.X},{c.Destination.Y})",
                _ => entry is null ? "Choose option" : $"{entry.Name} ({entry.Category})" +
                    (c.TargetId is not null ? $" → {c.TargetId}" :
                     c.Destination is not null ? $" → ({c.Destination.X},{c.Destination.Y})" :
                     c.Door is not null ? $" → ({c.Door.A.X},{c.Door.A.Y})–({c.Door.B.X},{c.Door.B.Y})" : "")
            };
            IReadOnlyList<string> affected = !c.TargetIds.IsEmpty ? c.TargetIds.ToArray()
                : c.TargetId is not null ? [c.TargetId] : [];
            return new BrowserChoice(c.Key, label, entryId, c.Relevant, interaction, affected);
        }).ToArray();
        var continuation = request.Kind == DecisionKind.Cleave ? entries.GetValueOrDefault("cleave")
            : request.IsMoveAfterAttack ? entries.GetValueOrDefault("move-after-attack") : null;
        var prompt = request.Kind == DecisionKind.SelectUnit ? "Choose a Unit"
            : continuation?.Name ?? "Choose an activation choice";
        BrowserChoice? none = !request.AllowsNone ? null : new(null,
            request.Kind == DecisionKind.Move ? "Stay here" : continuation is not null ? $"Decline {continuation.Name}" : "Take no action",
            null, true, request.Kind == DecisionKind.Move
                ? new(InteractionKind.Unit, UnitId: request.UnitId) : new(InteractionKind.Direct), []);
        return new(request.UnitId, prompt, candidates, none);
    }

    public static BrowserOutcome Outcome(RulesEvent e, GameState state)
    {
        string CellLabel(Cell cell) => $"{cell.X},{cell.Y}";
        var subject = e.AbilityName is null ? e.UnitId : $"{e.UnitId} · {e.AbilityName}";
        var role = OutcomeRole.Notice;
        string description;
        switch (e.Kind)
        {
            case "PostureChanged":
                description = $"{e.UnitId}: {e.Posture}";
                break;
            case "MovementCompleted":
                role = OutcomeRole.Movement;
                description = $"{e.UnitId} {(e.IsMoveAfterAttack ? "moved after attack" : "moved")}: {string.Join(" → ", e.Path!.Select(CellLabel))}";
                break;
            case "AttackResolved" when e.TargetId is null && e.Attack is { } attack:
                role = OutcomeRole.AttackSummary;
                description = $"{subject}: {attack.AttackDice} Attack Dice, {attack.Hits} shared Hits; " +
                    string.Join("; ", attack.Targets.Select(t => $"{t.TargetId}: {t.Blocks} Blocks, {t.Damage} Damage"));
                break;
            case "AttackResolved":
            case "AttackTargetResolved":
                role = OutcomeRole.AttackTarget;
                description = $"{subject} → {e.TargetId}: {e.Hits} Hits, {e.Blocks} Blocks, {e.Damage} Damage";
                break;
            case "CleaveResolved":
                role = OutcomeRole.Damage;
                description = $"{subject} → {e.TargetId}: {e.Damage} Damage";
                break;
            case "HealResolved":
                role = OutcomeRole.Healing;
                description = $"{subject} → {e.TargetId}: {e.Healing} HP restored";
                break;
            case "UnitDied":
                role = OutcomeRole.Death;
                description = $"{e.UnitId} died";
                break;
            case "AbilityUsed":
                description = $"{e.UnitId} used {e.AbilityName}";
                break;
            case "DoorOpeningAttemptResolved":
                role = OutcomeRole.DoorAttempt;
                description = $"{e.UnitId} tried door {CellLabel(e.Door!.A)} ↔ {CellLabel(e.Door.B)}: D6 {e.DieRoll}, {e.SuccessCount}/6 → {(e.Succeeded == true ? "success" : "failed; door stays closed")} (Action consumed)";
                break;
            case "DoorOpened":
                role = OutcomeRole.DoorOpened;
                description = $"{e.UnitId} opened door {CellLabel(e.Door!.A)} ↔ {CellLabel(e.Door.B)}";
                break;
            case "TokenDrawn":
                description = $"Token drawn: {state.Types.Single(t => t.Id == e.TypeId).DisplayName ?? e.TypeId}";
                break;
            default:
                description = e.Kind;
                break;
        }
        return new(role, description, e.UnitId, e.TargetId, e.Path, e.Door, e.Hits, e.Blocks, e.Damage);
    }
}
