using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record PaidMachineInteraction(ImmutableArray<string> ServiceIds);
public sealed record PaidMachineOffer(string ServiceId, string Name, int Cost, bool CanConfirm,
    ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics, int ResultingHp, long ResultingCredits, string? ItemTypeId)
{
    public string? ItemName { get; init; }
}
public sealed record PaidMachinePreview(string MachineId, string OperatorId, int CurrentCredits, int CurrentHp,
    int EffectiveMaxHp, ImmutableArray<PaidMachineOffer> Offers, string? SelectedServiceId, bool CanConfirm,
    ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics);

public static partial class GameEngine
{
    private static bool TryOpenPaidMachine(GameState state, DecisionRequest request, Candidate candidate)
    {
        if (candidate.Ability?.Definition.Mechanic is not Operate || candidate.MachineId is null ||
            state.Physical.Board.Machines.Single(m => m.Id == candidate.MachineId).Type.Operation.PaidServices is not { } paid) return false;
        var actor = state.Units.Single(u => u.Id == request.UnitId);
        if (!state.CanUseAbility(actor.Id, candidate.Ability) ||
            !MachineRules.Candidates(state, actor, candidate.Ability).Any(c => c.Key == candidate.Key) ||
            !FogOfWar.Allows(state, actor.SideId, candidate))
            throw new InvalidOperationException("Machine operation is unavailable.");
        state.MachineInteraction = new(candidate.MachineId, actor.Id, actor.SideId, candidate, null)
            { PaidServices = new([.. paid.Services.Select(s => s.Id)]) };
        return true;
    }

    private static PendingMachineInteraction RequirePaidMachine(GameState state)
    {
        if (state.IsCompleted || state.RoundComplete || state.Round <= 0 || state.MachineInteraction is not { } interaction ||
            interaction.EquipmentStation is not null || interaction.PaidServices is not { } payload ||
            state.Pending?.Kind != DecisionKind.MachineInteraction || state.CurrentUnitId != interaction.OperatorId)
            throw new InvalidOperationException("No paid Machine interaction is pending.");
        var actor = state.Units.SingleOrDefault(u => u.Id == interaction.OperatorId);
        var operation = interaction.Operation;
        var machine = state.Physical.Board.Machines.SingleOrDefault(m => m.Id == interaction.MachineId);
        if (state.AttackInProgress is not null || state.DoorInProgress is not null || state.BreakInProgress is not null || state.RepossessionInProgress is not null ||
            state.CleavePending || actor is null || state.ActiveToken != actor.Token || state.Pending.Token != actor.Token ||
            actor.SideId != interaction.SideId || actor.CurrentHp <= 0 || !state.IsUpright(actor.Id) ||
            operation?.Ability is null || operation.Ability.Definition?.Mechanic is not Operate || string.IsNullOrWhiteSpace(operation.Key) ||
            operation.MachineId != interaction.MachineId || machine?.Type.Operation.PaidServices is not { } services ||
            payload.ServiceIds.IsDefault || !payload.ServiceIds.SequenceEqual(services.Services.Select(s => s.Id)) ||
            state.Pending.MachineInteraction is not { } pending || pending.MachineId != interaction.MachineId ||
            pending.OperatorId != interaction.OperatorId || pending.SideId != interaction.SideId ||
            pending.EquipmentStation is not null || pending.PaidServices is not { } pendingPaid ||
            pendingPaid.ServiceIds.IsDefault || !pendingPaid.ServiceIds.SequenceEqual(payload.ServiceIds) ||
            pending.Operation is null || pending.Operation.Key != operation.Key || pending.Operation.MachineId != operation.MachineId ||
            pending.Operation.Ability?.Definition is null ||
            pending.Operation.Ability.SourceReference != operation.Ability.SourceReference)
            throw new InvalidOperationException("Machine operation is unavailable.");
        var ability = state.AbilitiesOf(actor.Id).SingleOrDefault(a => a.Key == operation.Ability.Key &&
            a.SourceKind == operation.Ability.SourceKind && a.SourceTypeId == operation.Ability.SourceTypeId &&
            a.SourceInstanceId == operation.Ability.SourceInstanceId);
        if (ability is null || ability.Definition.Mechanic is not Operate || !state.CanUseAbility(actor.Id, ability) ||
            ability.Definition.Timing != AbilityTiming.Action || !state.MoveDone || state.ActionsRemaining <= 0)
            throw new InvalidOperationException("Machine operation is unavailable.");
        var canonical = MachineRules.Candidates(state, actor, ability).Select(c => BindAbilityCandidate(ability, c))
            .SingleOrDefault(c => c.Key == operation.Key && c.MachineId == interaction.MachineId && c.Action == UnitAction.Operate);
        if (canonical is null || !FogOfWar.Allows(state, actor.SideId, canonical))
            throw new InvalidOperationException("Machine operation is unavailable.");
        return interaction with { Operation = canonical };
    }

    public static PaidMachinePreview PreviewPaidMachine(GameState state, string? serviceId = null)
    {
        var interaction = RequirePaidMachine(state);
        var actor = state.Units.Single(u => u.Id == interaction.OperatorId);
        var maximum = state.EffectiveMaxHpOf(actor);
        var balance = state.SideCredits.GetValueOrDefault(actor.SideId);
        var machine = state.Physical.Board.Machines.Single(m => m.Id == interaction.MachineId);
        var services = machine.Type.Operation.PaidServices!.Services;
        var offers = services.Select(service =>
        {
            var diagnostics = ImmutableArray.CreateBuilder<EquipmentAllocationDiagnostic>();
            if (machine.UseCounts.GetValueOrDefault(actor.SideId) == int.MaxValue)
                diagnostics.Add(new("machine-counter-full", "The Machine use counter cannot record another use."));
            if (balance < service.Cost) diagnostics.Add(new("insufficient-credits", "The Side has insufficient Credits."));
            var hp = actor.CurrentHp;
            if (service.FullHeal || service.HealAmount is not null)
            {
                hp = service.FullHeal ? maximum : (int)Math.Min(maximum, (long)hp + service.HealAmount!.Value);
                if (hp <= actor.CurrentHp) diagnostics.Add(new("no-healing", "The operator would regain no HP."));
            }
            if (service.ItemTypeId is { } item)
            {
                if (!state.ItemTypes.Any(t => t.Id == item)) diagnostics.Add(new("missing-item-type", "The Item Type is unavailable in this game."));
                if (!LootRules.CanOwn(state, actor, new("preview-purchase") { ItemTypeId = item }))
                    diagnostics.Add(new("cannot-receive-item", "The operator cannot own Items."));
            }
            return new PaidMachineOffer(service.Id, service.Name, service.Cost, diagnostics.Count == 0,
                diagnostics.ToImmutable(), hp, (long)balance - service.Cost, service.ItemTypeId)
                { ItemName = state.ItemTypes.SingleOrDefault(t => t.Id == service.ItemTypeId)?.Name };
        }).ToImmutableArray();
        var selected = offers.SingleOrDefault(o => o.ServiceId == serviceId);
        return new(interaction.MachineId, actor.Id, balance, actor.CurrentHp, maximum, offers, serviceId,
            selected?.CanConfirm == true, selected?.Diagnostics ?? (serviceId is null ? [] :
                [new("unavailable-service", "Select a service offered by this Machine.")]));
    }

    public static EngineResult ConfirmPaidMachine(GameState previous, string serviceId, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        var interaction = RequirePaidMachine(previous);
        var preview = PreviewPaidMachine(previous, serviceId);
        if (!preview.CanConfirm) throw new EquipmentAllocationException(preview.Diagnostics);
        var offer = preview.Offers.Single(o => o.ServiceId == serviceId);
        var state = previous.Copy();
        state.Pending = null;
        state.MachineInteraction = null;
        var accounting = new ResolutionEvents(state, StationAccountingRandom.Instance, null, captureState: false);
        var ability = interaction.Operation.Ability!;
        CommitAbilityInvocation(state, interaction.OperatorId, ability, accounting);
        EmitActionUsed(new(DecisionKind.Activation, state.Units.Single(u => u.Id == interaction.OperatorId).TypeId,
            interaction.OperatorId, [interaction.Operation], false), interaction.Operation.Key, accounting);
        var index = state.Units.FindIndex(u => u.Id == interaction.OperatorId);
        var actor = state.Units[index];
        string? itemId = null;
        if (offer.ItemTypeId is { } item)
        {
            var ids = LootRules.PhysicalObjects(state).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            // Machine use counters give each purchase a stable physical identity,
            // including after earlier purchases have been consumed or sold.
            var sourceMachine = state.Physical.Board.Machines.Single(m => m.Id == interaction.MachineId);
            var number = 1;
            do { itemId = $"purchase:{sourceMachine.Id}:{sourceMachine.TotalUses + 1}:{number++}"; } while (ids.Contains(itemId));
            actor = actor with { Inventory = actor.Inventory.Add(new(itemId) { ItemTypeId = item }) };
        }
        state.Units[index] = actor with { CurrentHp = offer.ResultingHp };
        state.SideCredits[interaction.SideId] = checked((int)offer.ResultingCredits);
        var machines = state.Physical.Board.Machines;
        var machineIndex = machines.FindIndex(m => m.Id == interaction.MachineId);
        var machine = machines[machineIndex];
        machines[machineIndex] = machine with { UseCounts = machine.UseCounts.SetItem(interaction.SideId,
            checked(machine.UseCounts.GetValueOrDefault(interaction.SideId) + 1)) };
        var events = new ResolutionEvents(state, random, observe);
        foreach (var entry in accounting.Events) events.Add(entry);
        events.Add(new RulesEvent("MachineOperated", interaction.OperatorId)
        {
            MachineId = interaction.MachineId, SourceUnitId = interaction.OperatorId, SideId = interaction.SideId,
            ActionId = ability.ActionId, Cell = machine.Position, CreditsAmount = -offer.Cost,
            CreditsBalance = checked((int)offer.ResultingCredits), PaidServiceId = serviceId, PaidServiceCost = offer.Cost,
            PurchasedItemId = itemId, PurchasedItemTypeId = offer.ItemTypeId
        });
        if (offer.ResultingHp > preview.CurrentHp)
            events.Add(new RulesEvent("HealResolved", interaction.OperatorId, interaction.OperatorId,
                AbilityName: offer.Name, Healing: offer.ResultingHp - preview.CurrentHp)
                { SourceUnitId = interaction.OperatorId, ActionId = ability.ActionId });
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }
}
