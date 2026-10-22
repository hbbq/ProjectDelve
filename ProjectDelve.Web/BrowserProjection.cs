using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public enum InteractionKind { Direct, Unit, Position, Door }
public sealed record ChoiceInteraction(InteractionKind Kind, string? UnitId = null,
    Cell? Position = null, Edge? Door = null);
public sealed record BrowserChoice(string? Key, string Label, string? EntryId,
    bool Relevant, ChoiceInteraction Interaction, IReadOnlyList<string> AffectedUnitIds)
{
    public IReadOnlyList<Cell> PlacementCells { get; init; } = [];
}
public sealed record FigureGeometry(Cell Anchor, IReadOnlyList<Cell> OccupiedCells, int CellSpan);
public sealed record BrowserDecision(string? UnitId, string Prompt,
    IReadOnlyList<BrowserChoice> Candidates, BrowserChoice? NoneChoice)
{
    public DicePool? Roll { get; init; }
}
public sealed record CardEntry(CardEntryDescription Content, AbilityUses? Uses);
public sealed record MachineDisplay(string Id, Cell Position, string Name, string Operation, IReadOnlyList<StoredResourceDisplay> ResourceStorage, string? OwnerSide = null)
{
    public string? TypeId { get; init; }
}
public sealed record StoredResourceDisplay(string ResourceTypeId, string Name, int Quantity, int Capacity);
public sealed record UnitCard(string DisplayName, IReadOnlyList<CardEntry> Entries)
{
    public string? IndividualName { get; init; }
    public IReadOnlyList<StoredResourceDisplay> ResourceStorage { get; init; } = [];
    public string? FootprintLabel { get; init; }
}
public sealed record BrowserObjective(string SideId, string Name)
{
    public string Meaning { get; init; } = "";
}
public sealed record BrowserWorldCard(string Id, string Name, string Text, bool Continuous);
public sealed record BrowserWorldEffects(int CardsPerRound, int Cycling, int DrawPileCount, int DiscardPileCount,
    IReadOnlyList<BrowserWorldCard> ActiveContinuous, BrowserWorldCard? ResolvingCard);
public enum OutcomeRole { Notice, Movement, AttackTarget, AttackSummary, Damage, Healing, Defeat, DoorAttempt, DoorOpened, DoorClosed }
public sealed record BrowserOutcome(OutcomeRole Role, string Text, string? UnitId = null,
    string? TargetId = null, List<Cell>? Path = null, Edge? Door = null,
    int Hits = 0, int Blocks = 0, int Damage = 0);
public sealed record BrowserResolutionStep(int EventIndex, IReadOnlyDictionary<string, UnitCard> Cards)
{
    public GameCompletion? Completion { get; init; }
    public IReadOnlyList<BrowserObjective> Objectives { get; init; } = [];
    public BrowserWorldEffects? WorldEffects { get; init; }
    public IReadOnlyDictionary<string, FigureGeometry> Figures { get; init; } = new Dictionary<string, FigureGeometry>();
    public IReadOnlyList<MachineDisplay> Machines { get; init; } = [];
    public IReadOnlyList<GroupOrder> Orders { get; init; } = [];
}
public sealed record BrowserPresentation(IReadOnlyDictionary<string, UnitCard> Cards,
    BrowserDecision? Decision, IReadOnlyList<BrowserOutcome> Events,
    IReadOnlyList<BrowserResolutionStep> ResolutionSteps)
{
    public GameCompletion? Completion { get; init; }
    public IReadOnlyList<BrowserObjective> Objectives { get; init; } = [];
    public BrowserWorldEffects? WorldEffects { get; init; }
    public IReadOnlyDictionary<string, FigureGeometry> Figures { get; init; } = new Dictionary<string, FigureGeometry>();
    public IReadOnlyList<MachineDisplay> Machines { get; init; } = [];
    public IReadOnlyList<GroupOrder> Orders { get; init; } = [];
}

// An adapter over authoritative engine results; never generates or filters legal choices.
// Internal engine results remain authoritative; transport boundaries sanitize future draw information.
public static class BrowserProjection
{
    public static BrowserPresentation Create(EngineResult result, IReadOnlyDictionary<string, string>? individualNames = null) => new(
        Cards(result.State, individualNames), Decision(result.NextInput, result.State),
        result.Events.Select(e => Outcome(e, result.State)).ToArray(),
        result.ResolutionSteps.Select(s => new BrowserResolutionStep(s.EventIndex, Cards(s.StateAfter, individualNames))
            { Machines = Machines(s.StateAfter), Figures = Figures(s.StateAfter), WorldEffects = WorldEffects(s.StateAfter), Objectives = Objectives(s.StateAfter),
                Completion = s.StateAfter.Completion, Orders = s.StateAfter.Orders.ToArray() }).ToArray())
        { Machines = Machines(result.State), Figures = Figures(result.State), WorldEffects = WorldEffects(result.State), Objectives = Objectives(result.State), Completion = result.State.Completion,
            Orders = result.State.Orders.ToArray() };

    public static IReadOnlyList<MachineDisplay> Machines(GameState state) => state.Physical.Board.Machines.Select(m =>
        new MachineDisplay(m.Id, m.Position, m.Type.Name,
            "Spend " + string.Join(", ", m.Type.Operation.Costs.Select(c => $"{c.Quantity} {c.Type.Name}")) +
            $" -> Output {m.Type.Operation.OutputQuantity} {m.Type.Operation.OutputDescription} on the operator's Cell",
            m.Type.Storage.Capacities.Select(c => new StoredResourceDisplay(c.Type.Id, c.Type.Name, m.StoredQuantity(c.Type.Id), c.Capacity)).ToArray(), m.OwnerSide) { TypeId = m.Type.Id }).ToArray();

    public static IReadOnlyList<BrowserObjective> Objectives(GameState state) => state.Objectives.Select(o =>
        new BrowserObjective(o.SideId, ObjectivePresentation.Name(o)) { Meaning = ObjectivePresentation.Meaning(o) }).ToArray();

    public static BrowserWorldCard WorldCard(WorldCard card)
    {
        var content = WorldCards.Content(card.Effect);
        return new(card.Id, content.Name, content.Text, content.Continuous);
    }
    public static BrowserWorldEffects? WorldEffects(GameState state) =>
        state.WorldEffects is not { } settings || state.WorldDeck is not { } deck ? null :
        new(settings.CardsPerRound, settings.Cycling, deck.DrawPile.Count, deck.DiscardPile.Count,
            deck.ActiveContinuous.Select(WorldCard).ToArray(), deck.ResolvingCard is null ? null : WorldCard(deck.ResolvingCard));

    public static IReadOnlyDictionary<string, FigureGeometry> Figures(GameState state) =>
        state.Physical.Figures.ToDictionary(f => f.Id, f =>
        {
            var cells = FootprintGeometry.OccupiedCells(state, f.Id);
            return new FigureGeometry(f.Position, cells, cells.Max(c => c.X) - f.Position.X + 1);
        });

    public static IReadOnlyDictionary<string, UnitCard> Cards(GameState state, IReadOnlyDictionary<string, string>? individualNames = null) => state.Units.ToDictionary(u => u.Id, u =>
    {
        var type = state.Types.Single(t => t.Id == u.TypeId);
        return new UnitCard(type.DisplayName ?? type.Id,
            type.CardEntries(state.Types).Select(entry => new CardEntry(entry, type.UsesFor(u, entry.Id))).ToArray())
        {
            IndividualName = individualNames?.GetValueOrDefault(u.Id),
            ResourceStorage = type.ResourceStorage?.Capacities.Select(c => new StoredResourceDisplay(c.Type.Id, c.Type.Name,
                state.StoredQuantity(u.SideId, u.TypeId, c.Type.Id), c.Capacity)).ToArray() ?? [],
            FootprintLabel = type.Footprint == Footprint.TwoByTwo ? "Footprint 2 × 2" : "Footprint 1 × 1"
        };
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
                : c.MachineId is not null ? new(InteractionKind.Position, Position: state.Physical.Board.Machines.Single(m => m.Id == c.MachineId).Position)
                : c.Action == UnitAction.GiveResource ? new(InteractionKind.Unit, UnitId: c.TargetId)
                : c.Destination is not null ? new(InteractionKind.Position, Position: c.Destination)
                : c.TargetId is not null ? new(InteractionKind.Unit, UnitId: c.TargetId)
                : new ChoiceInteraction(InteractionKind.Direct);
            var label = c.Kind switch
            {
                ActivationChoiceKind.RollDice => "Roll Dice",
                ActivationChoiceKind.Stay => "Stay here",
                ActivationChoiceKind.EndTurn => "End Turn",
                ActivationChoiceKind.SelectUnit => $"Select {c.Key}",
                ActivationChoiceKind.Move => $"Move to ({c.Destination!.X},{c.Destination.Y})",
                _ when c.Action is UnitAction.PickupResource or UnitAction.GiveResource or UnitAction.Operate =>
                    $"{entry!.Name} ({entry.Category})" + (c.MachineId is not null ? $" to {state.Physical.Board.Machines.Single(m => m.Id == c.MachineId).Type.Name} ({c.MachineId})" : c.TargetId is null ? "" : $" to {c.TargetId}") +
                    (c.Destination is null ? "" : $" from ({c.Destination.X},{c.Destination.Y})"),
                _ => entry is null ? "Choose option" : $"{entry.Name} ({entry.Category})" +
                    (c.TargetId is not null && c.Destination is not null ? $" → {c.TargetId} to ({c.Destination.X},{c.Destination.Y})" :
                     c.TargetId is not null ? $" → {c.TargetId}" :
                     c.Destination is not null ? $" → ({c.Destination.X},{c.Destination.Y})" :
                     c.Door is not null ? $" → ({c.Door.A.X},{c.Door.A.Y})–({c.Door.B.X},{c.Door.B.Y})" : "")
            };
            IReadOnlyList<string> affected = !c.TargetIds.IsEmpty ? c.TargetIds.ToArray()
                : c.TargetId is not null ? [c.TargetId] : [];
            return new BrowserChoice(c.Key, label, entryId, c.Relevant, interaction, affected)
            {
                PlacementCells = c.Destination is null ? [] :
                    c.Kind == ActivationChoiceKind.Move
                        ? FootprintGeometry.OccupiedCells(state, request.UnitId!, c.Destination)
                        : c.Action is { } summonAction && SummoningRules.Placement(type, summonAction) is { } summon
                            ? FootprintGeometry.PlacementCells(state, summon.UnitTypeId, c.Destination)
                            : c.BonusAction?.Displace is not null
                                ? FootprintGeometry.OccupiedCells(state, c.TargetId!, c.Destination)
                            : []
            };
        }).ToArray();
        var continuation = request.Kind == DecisionKind.Cleave ? entries.GetValueOrDefault("cleave")
            : request.IsMoveAfterAttack ? entries.GetValueOrDefault(state.AdvanceOnKillCells is not null
                ? "advance-on-kill" : "move-after-attack") : null;
        var prompt = request.Roll is { } pool ? $"{pool.OwnerUnitId}: roll {pool.Count} {pool.Family} dice ({pool.Purpose}, {pool.SourceActionId})"
                + (pool.TargetId is null ? "" : $" against {pool.TargetId}")
                + (pool.Door is null ? "" : $" at door {pool.Door.A.X},{pool.Door.A.Y} – {pool.Door.B.X},{pool.Door.B.Y} ({pool.SuccessCount}/6)")
            : request.Kind == DecisionKind.SelectUnit ? "Choose a Unit"
            : continuation?.Name ?? "Choose an activation choice";
        BrowserChoice? none = !request.AllowsNone ? null : new(null,
            request.Kind == DecisionKind.Move ? "Stay here" : continuation is not null ? $"Decline {continuation.Name}" : "Take no action",
            null, true, request.Kind == DecisionKind.Move
                ? new(InteractionKind.Unit, UnitId: request.UnitId) : new(InteractionKind.Direct), []);
        return new(request.UnitId, prompt, candidates, none) { Roll = request.Roll };
    }

    public static BrowserOutcome Outcome(RulesEvent e, GameState state)
    {
        string CellLabel(Cell cell) => $"{cell.X},{cell.Y}";
        var subject = e.WorldCard is { } worldCard ? WorldCards.Content(worldCard.Effect).Name
            : e.AbilityName is null ? e.UnitId : $"{e.UnitId} · {e.AbilityName}";
        if (e.Kind == "ExplosionDamageResolved" && e.WorldCard is not null)
            subject = $"{e.UnitId} · {e.AbilityName} (during {subject})";
        var sourceTypeId = state.Units.FirstOrDefault(u => u.Id == e.UnitId)?.TypeId;
        var actionName = e.AbilityName ?? state.Types.FirstOrDefault(t => t.Id == sourceTypeId)?
            .CardEntries(state.Types).FirstOrDefault(entry => entry.Id == e.ActionId || entry.Id == $"bonus:{e.ActionId}")?.Name ?? e.ActionId;
        var role = OutcomeRole.Notice;
        string description;
        switch (e.Kind)
        {
            case "GameCompleted":
                description = "Game over - " + (e.Completion!.AchievedSideIds.Length == 1
                    ? $"Victory: {e.Completion.AchievedSideIds[0]} achieved its Objective"
                    : $"Simultaneous victory: {string.Join(", ", e.Completion.AchievedSideIds)} each achieved their Objective");
                break;
            case "WorldDeckShuffled":
                description = "World Deck shuffled";
                break;
            case "WorldDeckReshuffled":
                description = "World discard pile shuffled into the draw pile; active Continuous cards remain in play";
                break;
            case "WorldDrawSkipped":
                description = "No World Card drawn: draw and discard piles are empty";
                break;
            case "WorldCardDrawn":
                var content = WorldCards.Content(e.WorldCard!.Effect);
                description = $"World Card drawn: {content.Name} ({(content.Continuous ? "Continuous" : "Immediate")}) — {content.Text}";
                break;
            case "WorldContinuousChanged":
                description = $"{subject} is now active" + (e.CycledWorldCard is { } cycled
                    ? $"; {WorldCards.Content(cycled.Effect).Name} simultaneously cycles out to discard" : "");
                break;
            case "WorldCardDiscarded":
                description = $"{subject} finished resolving, including automatic consequences; card discarded";
                break;
            case "AbilityUsesReplenished":
                description = $"{subject} → {e.UnitId}: replenish 1 use of each limited-use Ability, up to its maximum";
                break;
            case "RoundStarted":
                description = $"Round {e.Round} started";
                break;
            case "ActivationBagPopulated":
                description = "Activation Bag populated from Units now in play";
                break;
            case "RoundCompleted":
                description = $"Round {e.Round} completed";
                break;
            case "ActivationStarted":
            case "ActivationCompleted":
                description = $"{e.UnitId}: {(e.Kind == "ActivationStarted" ? "activation started" : "activation completed")} ({e.Token?.TypeId}, {e.Token?.SideId})";
                break;
            case "ActionUsed":
                var category = e.Category switch
                {
                    ActivationChoiceKind.BonusAction => "Bonus Action",
                    ActivationChoiceKind.FreeAction => "Free Action",
                    _ => "Action"
                };
                description = $"{e.UnitId} used {actionName} ({category})";
                break;
            case "AttackStarted":
                description = $"{e.UnitId}: {actionName} started against {string.Join(", ", e.AttackContext!.Targets.Select(t => t.TargetId))}";
                break;
            case "DiceRolled":
                var resultLabel = e.Dice!.Pool.Family switch
                {
                    DiceFamily.Attack => "Hits",
                    DiceFamily.Defence => "Blocks",
                    _ => "successes"
                };
                description = $"{e.UnitId}: {e.Dice.Pool.Family} dice [{string.Join(", ", e.Dice.Faces)}], {e.Dice.Successes} {resultLabel} ({e.Dice.Pool.SourceUnitId}, {e.ActionId})";
                break;
            case "LavaCoalRolled":
                description = $"Lava at {e.Cell?.X},{e.Cell?.Y}: rolled d8 {e.DieRoll} for Coal direction";
                break;
            case "FruitTreeRolled":
                description = $"Fruit Tree at {e.Cell?.X},{e.Cell?.Y}: rolled d8 {e.DieRoll} for Fruit direction";
                break;
            case "WaypointReached":
                description = $"{e.UnitId}: reached {e.WaypointId} on Route {e.RouteId}";
                break;
            case "RacingSlotRolled":
                description = $"{e.UnitId}: waypoint slot d6 {e.DieRoll}: {e.ResourceType?.Name}" + (e.Succeeded == true ? " awarded" : " discarded (storage full)");
                break;
            case "CellMarkerConsumed":
                description = $"{e.UnitId}: consumed {e.CellMarkerType?.Name} at {e.Cell?.X},{e.Cell?.Y}";
                break;
            case "CellMarkerPlaced":
                description = $"{e.UnitId}: placed {e.CellMarkerType?.Name} at {e.Cell?.X},{e.Cell?.Y}";
                break;
            case "RouteAdvanced":
                description = $"{e.UnitId}: advanced the group's route waypoint";
                break;
            case "RouteCompleted":
                description = $"{e.UnitId}: completed the group's route";
                break;
            case "ResourceCreated":
                description = $"{e.ResourceType?.Name} created at {e.Cell?.X},{e.Cell?.Y}";
                break;
            case "MachineOperated":
                description = $"{e.UnitId}: operated {e.MachineId}, spent {string.Join(", ", e.ConsumedResources.Select(r => $"{r.Quantity} {r.Type.Name}"))}, output {e.ResourceType?.Name} at {e.Cell?.X},{e.Cell?.Y}";
                break;
            case "ResourceStored" when e.MachineId is not null:
                description = $"{e.UnitId}: {e.ResourceType?.Name} stored in {e.MachineId} ({e.StoredQuantity} stored)";
                break;
            case "ResourceSpent":
                description = $"{e.UnitId}: spent {e.ResourceType?.Name} for {actionName} ({e.StoredQuantity} stored)";
                break;
            case "ResourceStored":
                description = $"{e.UnitId}: {e.ResourceType?.Name} from {e.Cell?.X},{e.Cell?.Y} stored for {e.SideId} / {e.TypeId} ({e.StoredQuantity} stored)";
                break;
            case "UnitCreated":
                description = $"{e.SourceUnitId} created {e.UnitId} ({e.SideId}) at {e.Cell?.X},{e.Cell?.Y}, {e.Posture}";
                break;
            case "PostureChanged":
                description = $"{e.UnitId}: {e.Posture}" + (e.WorldCard is null ? "" : $" ({subject})");
                break;
            case "MovementCompleted":
                role = OutcomeRole.Movement;
                description = $"{e.UnitId} {(e.IsMoveAfterAttack ? "moved after attack" : "moved")}: {string.Join(" → ", e.Path!.Select(CellLabel))}";
                break;
            case "UnitRepositioned":
                role = OutcomeRole.Movement;
                description = e.ActionId == "run-rails"
                    ? $"{e.UnitId} ran Rails: {string.Join(" -> ", e.Path!.Select(CellLabel))}"
                    : $"{e.SourceUnitId} displaced {e.UnitId}: {string.Join(" → ", e.Path!.Select(CellLabel))}";
                break;
            case "PlacesSwapped":
                description = $"{e.UnitId} swapped places with {e.TargetId}";
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
            case "WorldDamageResolved":
            case "ExplosionDamageResolved":
            case "CleaveResolved":
                role = OutcomeRole.Damage;
                description = $"{subject} → {e.TargetId}: {e.Damage} Damage";
                break;
            case "HealResolved":
                role = OutcomeRole.Healing;
                description = $"{subject} → {e.TargetId}: {e.Healing} HP restored";
                break;
            case "UnitDefeated":
                role = OutcomeRole.Defeat;
                description = $"{e.UnitId} was defeated";
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
                description = $"{(e.WorldCard is null ? e.UnitId : subject)} opened door {CellLabel(e.Door!.A)} ↔ {CellLabel(e.Door.B)}";
                break;
            case "DoorClosed":
                role = OutcomeRole.DoorClosed;
                description = $"{subject} closed door {CellLabel(e.Door!.A)} ↔ {CellLabel(e.Door.B)}";
                break;
            case "TokenDrawn":
                description = $"Token drawn: {state.Types.Single(t => t.Id == e.TypeId).DisplayName ?? e.TypeId} ({e.Token?.SideId})";
                break;
            default:
                description = e.Kind;
                break;
        }
        return new(role, description, e.UnitId, e.TargetId, e.Path, e.Door, e.Hits, e.Blocks, e.Damage);
    }
}
