using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record EquipmentTransfer(string ObjectId, string FromUnitId, string ToUnitId);
public sealed record EquipmentAssignment(string ObjectId, string UnitId);
public sealed record EquipmentAllocation(ImmutableArray<EquipmentAssignment> Assignments);
public sealed record EquipmentStationInteraction(ImmutableArray<string> EligibleUnitIds,
    EquipmentAllocation OriginalAllocation);
public sealed record PendingMachineInteraction(string MachineId, string OperatorId, string SideId,
    Candidate Operation, EquipmentStationInteraction EquipmentStation)
{
    public string? ReservedConsumptionObjectId => Operation.Ability?.SourceKind == AbilitySourceKind.Item
        ? Operation.Ability.SourceInstanceId : null;
}
public sealed record EquipmentAllocationDiagnostic(string Code, string Message, string? ObjectId = null, string? UnitId = null);
public sealed class EquipmentAllocationException(ImmutableArray<EquipmentAllocationDiagnostic> diagnostics)
    : ArgumentException("The Equipment and Item allocation is invalid.")
{
    public ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics { get; } = diagnostics;
}
public sealed record EquipmentStationAbilityPreview(ResolvedAbility Ability, int? RemainingUses, bool Applicable,
    CardEntryDescription Content);
public sealed record EquipmentStationStats(int Mov, int Rng, int Atk, int Def, int Hp, int SightDistance);
public sealed record EquipmentStationUnitPreview(string UnitId, ImmutableArray<OwnedObject> Allocation,
    int Mov, int Rng, int Atk, int Def, int Hp, int SightDistance,
    ImmutableArray<string> MovementCapabilities, bool HasPhase, ImmutableArray<ResolvedAbility> Abilities)
{
    public bool ProposedAllocationAvailable { get; init; } = true;
    public bool BaselineHasPhase { get; init; }
    public ImmutableArray<string> GainedMovementCapabilities { get; init; } = [];
    public ImmutableArray<string> LostMovementCapabilities { get; init; } = [];
    public ImmutableArray<AbilitySourceReference> GainedAbilities { get; init; } = [];
    public ImmutableArray<AbilitySourceReference> LostAbilities { get; init; } = [];
    public EquipmentStationStats Baseline { get; init; } = new(0, 0, 0, 0, 0, 0);
    public EquipmentStationStats Difference { get; init; } = new(0, 0, 0, 0, 0, 0);
    public ImmutableArray<EquipmentStationAbilityPreview> AbilityPreviews { get; init; } = [];
    public ImmutableArray<EquipmentStationAbilityPreview> LostAbilityPreviews { get; init; } = [];
}
public sealed record EquipmentStationPreview(bool CanConfirm, ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics,
    ImmutableArray<EquipmentStationUnitPreview> Units);

public static partial class GameEngine
{
    private static ImmutableArray<Unit> EquipmentStationUnits(GameState state, string side) =>
        [.. state.Units.Where(u => u.SideId == side && u.CurrentHp > 0 &&
            state.Physical.Figures.Any(f => f.Id == u.Id) && (state.Types.Single(t => t.Id == u.TypeId).CanHaveEquipment || state.Types.Single(t => t.Id == u.TypeId).CanHaveItems))];

    private static bool TryOpenEquipmentStation(GameState state, DecisionRequest request, Candidate candidate)
    {
        if (candidate.Ability?.Definition.Mechanic is not Operate || candidate.MachineId is null ||
            state.Physical.Board.Machines.Single(m => m.Id == candidate.MachineId).Type.Operation.EquipmentStation is null) return false;
        var actor = state.Units.Single(u => u.Id == request.UnitId);
        if (!state.CanUseAbility(actor.Id, candidate.Ability) ||
            !MachineRules.Candidates(state, actor, candidate.Ability).Any(c => c.Key == candidate.Key) ||
            !FogOfWar.Allows(state, actor.SideId, candidate))
            throw new InvalidOperationException("Machine operation is unavailable.");
        var units = EquipmentStationUnits(state, actor.SideId);
        var reserved = candidate.Ability.SourceKind == AbilitySourceKind.Item ? candidate.Ability.SourceInstanceId : null;
        var allocation = new EquipmentAllocation([.. units.SelectMany(u => u.Inventory
            .Where(o => Transferable(state, o) && o.Id != reserved).Select(o => new EquipmentAssignment(o.Id, u.Id)))]);
        state.MachineInteraction = new(candidate.MachineId, actor.Id, actor.SideId,
            candidate, new([.. units.Select(u => u.Id)], allocation));
        return true;
    }

    private static PendingMachineInteraction RequireEquipmentStation(GameState state)
    {
        if (state.IsCompleted || state.RoundComplete || state.Round <= 0 || state.MachineInteraction is not { } interaction ||
            state.Pending?.Kind != DecisionKind.MachineInteraction || state.CurrentUnitId != interaction.OperatorId)
            throw new InvalidOperationException("No Equipment Station interaction is pending.");
        var actor = state.Units.SingleOrDefault(u => u.Id == interaction.OperatorId);
        var operation = interaction.Operation;
        if (state.AttackInProgress is not null || state.DoorInProgress is not null || state.BreakInProgress is not null ||
            state.CleavePending || state.ActiveToken != actor?.Token || state.Pending.Token != actor?.Token ||
            actor is null || actor.SideId != interaction.SideId || actor.CurrentHp <= 0 || !state.IsUpright(actor.Id) ||
            operation?.Ability is null || operation.Ability.Definition.Mechanic is not Operate ||
            operation.MachineId != interaction.MachineId ||
            !state.Physical.Board.Machines.Any(m => m.Id == interaction.MachineId && m.Type.Operation.EquipmentStation is not null))
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
        var units = EquipmentStationUnits(state, interaction.SideId);
        var payload = interaction.EquipmentStation ?? throw new InvalidOperationException("The Station payload is required.");
        var original = units.SelectMany(u => u.Inventory.Where(o => Transferable(state, o) && o.Id != interaction.ReservedConsumptionObjectId)
            .Select(o => new EquipmentAssignment(o.Id, u.Id))).OrderBy(a => a.ObjectId, StringComparer.Ordinal).ToArray();
        if (payload.EligibleUnitIds.IsDefault || !payload.EligibleUnitIds.SequenceEqual(units.Select(u => u.Id)) ||
            payload.OriginalAllocation is null || payload.OriginalAllocation.Assignments.IsDefault ||
            payload.OriginalAllocation.Assignments.Any(a => a is null) ||
            !payload.OriginalAllocation.Assignments.OrderBy(a => a.ObjectId, StringComparer.Ordinal).SequenceEqual(original) ||
            state.Pending.MachineInteraction is not { } pending || pending.MachineId != interaction.MachineId ||
            pending.OperatorId != interaction.OperatorId || pending.SideId != interaction.SideId)
            throw new InvalidOperationException("The pending Equipment Station roster is stale or invalid.");
        interaction = interaction with { Operation = canonical };
        return interaction;
    }

    private static void ValidateMachineInteractionState(GameState state)
    {
        if (state.MachineInteraction is null)
        {
            if (state.Pending?.Kind == DecisionKind.MachineInteraction || state.Pending?.MachineInteraction is not null)
                throw new ArgumentException("A Machine interaction decision requires its authoritative continuation.");
            return;
        }
        try { RequireEquipmentStation(state); }
        catch (InvalidOperationException error) { throw new ArgumentException("Invalid Machine interaction continuation.", error); }
    }

    private static bool Transferable(GameState state, OwnedObject obj) => state.EquipmentTypeId(obj) is not null || state.ItemTypeId(obj) is not null;

    public static EquipmentStationPreview PreviewEquipmentStation(GameState state, EquipmentAllocation? allocation) =>
        ValidateEquipmentAllocation(state, allocation, includePreview: true);

    private static EquipmentStationPreview ValidateEquipmentAllocation(GameState state, EquipmentAllocation? allocation, bool includePreview)
    {
        var interaction = RequireEquipmentStation(state);
        var units = EquipmentStationUnits(state, interaction.SideId);
        var objects = units.SelectMany(u => u.Inventory.Where(o => Transferable(state, o) && o.Id != interaction.ReservedConsumptionObjectId)).ToArray();
        var original = units.SelectMany(u => u.Inventory.Where(o => Transferable(state, o) && o.Id != interaction.ReservedConsumptionObjectId)
            .Select(o => new EquipmentAssignment(o.Id, u.Id))).ToArray();
        var objectIndex = objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var unitIds = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var originalOwners = original.ToDictionary(a => a.ObjectId, a => a.UnitId, StringComparer.Ordinal);
        var requested = allocation?.Assignments ?? [.. original];
        var diagnostics = ImmutableArray.CreateBuilder<EquipmentAllocationDiagnostic>();
        void Reject(string code, string message, string? objectId = null, string? unitId = null) =>
            diagnostics.Add(new(code, message, objectId, unitId));
        if (requested.IsDefault) Reject("invalid-allocation", "A complete Equipment and Item allocation is required.");
        else
        {
            var assignedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in requested)
            {
                var objectId = assignment?.ObjectId;
                var unitId = assignment?.UnitId;
                var knownObject = objectId is not null && objectIndex.ContainsKey(objectId);
                var knownRecipient = unitId is not null && unitIds.Contains(unitId);
                if (!knownObject || !knownRecipient)
                    Reject("invalid-assignment", "The allocation contains an unavailable object or recipient.",
                        knownObject ? objectId : null, knownRecipient ? unitId : null);
                else if (!assignedIds.Add(objectId!))
                    Reject("duplicate-assignment", "Assign each object to exactly one recipient.", objectId, unitId);
            }
            foreach (var entry in original)
                if (!assignedIds.Contains(entry.ObjectId))
                    Reject("missing-assignment", "Assign this object to a recipient.", entry.ObjectId, entry.UnitId);
        }
        if (diagnostics.Count > 0)
        {
            var rows = includePreview ? ValidateEquipmentAllocation(state, null, includePreview: true).Units
                .Select(u => u with { ProposedAllocationAvailable = false }).ToImmutableArray() : [];
            return new(false, diagnostics.ToImmutable(), rows);
        }
        var draft = state.Copy();
        var assignedByUnit = requested.ToLookup(a => a.UnitId, StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var equipped = assignedByUnit[unit.Id].Select(a => objectIndex[a.ObjectId]).ToImmutableArray();
            if (equipped.Any(o => !LootRules.CanOwn(state, unit with { Inventory = [] }, o)))
                Reject("ownership-capability", "The recipient cannot own that object category.", unitId: unit.Id);
            var equipmentTypes = equipped.Select(state.EquipmentTypeId).OfType<string>().ToArray();
            if (equipmentTypes.Distinct(StringComparer.Ordinal).Count() != equipmentTypes.Length)
                Reject("duplicate-equipment-type", "A Unit may own only one object of each Equipment Type.", unitId: unit.Id);
            var index = draft.Units.FindIndex(u => u.Id == unit.Id);
            draft.Units[index] = unit with { Inventory = AllocateStationInventory(state, unit, assignedByUnit[unit.Id], objectIndex) };
        }
        foreach (var unit in units)
        {
            var position = draft.Physical.Figures.Single(f => f.Id == unit.Id).Position;
            if (!SpatialRules.CanPlaceUnit(draft, unit.Id, position))
                Reject("illegal-occupancy", "The proposed Equipment does not permit a Unit's current placement.", unitId: unit.Id);
        }
        if (!requested.Any(a => originalOwners[a.ObjectId] != a.UnitId))
            Reject("no-transfer", "Confirm requires at least one object transfer.");
        if (!includePreview) return new(diagnostics.Count == 0, diagnostics.ToImmutable(), []);
        var results = units.Select(unit =>
        {
            var type = draft.Types.Single(t => t.Id == unit.TypeId);
            var bearer = draft.Units.Single(u => u.Id == unit.Id) with { CurrentHp = type.Hp };
            var normalized = new GameState
            {
                Units = [bearer], Types = draft.Types,
                Physical = new(new Board(4, 4, []), [new Figure(unit.Id, new Cell(1, 1), Posture.Upright)]),
                EquipmentTypes = draft.EquipmentTypes, ItemTypes = draft.ItemTypes, LootCardDefinitions = draft.LootCardDefinitions
            };
            var baselineState = normalized.Copy();
            baselineState.Units = [unit with { CurrentHp = type.Hp }];
            EquipmentStationStats Stats(GameState value) => new(value.EffectiveMovOf(unit.Id), value.EffectiveRngOf(unit.Id),
                value.EffectiveAtkOf(unit.Id), value.EffectiveDefOf(unit.Id), type.Hp, value.EffectiveSightDistanceOf(unit.Id));
            var baseline = Stats(baselineState);
            var current = Stats(normalized);
            var capabilities = normalized.EffectiveMovementCapabilitiesOf(unit.Id);
            var baselineCapabilities = baselineState.EffectiveMovementCapabilitiesOf(unit.Id);
            var abilities = normalized.AbilitiesOf(unit.Id).ToImmutableArray();
            var openingAbilities = baselineState.AbilitiesOf(unit.Id).ToImmutableArray();
            var abilityRefs = abilities.Select(a => a.SourceReference).ToImmutableArray();
            var lost = openingAbilities.ExceptBy(abilityRefs, a => a.SourceReference).ToImmutableArray();
            EquipmentStationAbilityPreview Describe(GameState loadout, ResolvedAbility ability) => new(ability,
                ability.SourceKind == AbilitySourceKind.Item ? 1 : loadout.AbilityUsesOf(unit.Id, ability)?.RemainingUses,
                NormalizedAbilityApplicable(ability), ability.CardEntry(loadout.Types));
            return new EquipmentStationUnitPreview(unit.Id, [.. bearer.Inventory.Where(o => Transferable(draft, o))],
                current.Mov, current.Rng, current.Atk, current.Def, current.Hp, current.SightDistance,
                capabilities, normalized.HasPhase(unit.Id), abilities)
            {
                Baseline = baseline,
                BaselineHasPhase = baselineState.HasPhase(unit.Id),
                GainedMovementCapabilities = [.. capabilities.Except(baselineCapabilities, StringComparer.Ordinal)],
                LostMovementCapabilities = [.. baselineCapabilities.Except(capabilities, StringComparer.Ordinal)],
                GainedAbilities = [.. abilityRefs.Except(openingAbilities.Select(a => a.SourceReference))],
                LostAbilities = [.. lost.Select(a => a.SourceReference)],
                Difference = new(current.Mov - baseline.Mov, current.Rng - baseline.Rng, current.Atk - baseline.Atk,
                    current.Def - baseline.Def, current.Hp - baseline.Hp, current.SightDistance - baseline.SightDistance),
                AbilityPreviews = [.. abilities.Select(a => Describe(normalized, a))],
                LostAbilityPreviews = [.. lost.Select(a => Describe(baselineState, a))]
            };
        }).ToImmutableArray();
        return new(diagnostics.Count == 0, diagnostics.ToImmutable(), results);
    }

    private static ImmutableArray<OwnedObject> AllocateStationInventory(GameState state, Unit unit,
        IEnumerable<EquipmentAssignment> assignments, IReadOnlyDictionary<string, OwnedObject> objects)
    {
        var assigned = assignments.Select(a => a.ObjectId).ToHashSet(StringComparer.Ordinal);
        var existing = unit.Inventory.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        return [.. unit.Inventory.Where(o => !Transferable(state, o) || assigned.Contains(o.Id)),
            .. assigned.Where(id => !existing.Contains(id)).Order(StringComparer.Ordinal).Select(id => objects[id])];
    }

    private static bool NormalizedAbilityApplicable(ResolvedAbility ability)
    {
        return ability.Definition.Timing == AbilityTiming.Passive &&
            ability.Definition.Mechanic is not (AdjacentUnitsStatModifiersMechanic or Fury or Backstab);
    }

    public static EngineResult ConfirmEquipmentStation(GameState previous, EquipmentAllocation allocation, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        var interaction = RequireEquipmentStation(previous);
        var preview = ValidateEquipmentAllocation(previous, allocation, includePreview: false);
        if (!preview.CanConfirm) throw new EquipmentAllocationException(preview.Diagnostics);
        var state = previous.Copy();
        var accounting = new ResolutionEvents(state, random, null);
        var originalOwners = interaction.EquipmentStation.OriginalAllocation.Assignments
            .ToDictionary(a => a.ObjectId, a => a.UnitId, StringComparer.Ordinal);
        var candidate = interaction.Operation;
        var ability = candidate.Ability!;
        state.Pending = null;
        state.MachineInteraction = null;
        CommitAbilityInvocation(state, interaction.OperatorId, ability, accounting);
        EmitActionUsed(new(DecisionKind.Activation, state.Units.Single(u => u.Id == interaction.OperatorId).TypeId,
            interaction.OperatorId, [candidate], false), candidate.Key, accounting);
        // Accounting may update the physical source's counters. Transfer those updated objects.
        var eligible = EquipmentStationUnits(state, interaction.SideId);
        var objects = eligible.SelectMany(u => u.Inventory.Where(o => Transferable(state, o)))
            .ToDictionary(o => o.Id, StringComparer.Ordinal);
        var assignedByUnit = allocation.Assignments.ToLookup(a => a.UnitId, StringComparer.Ordinal);
        foreach (var unit in eligible)
        {
            var index = state.Units.FindIndex(u => u.Id == unit.Id);
            state.Units[index] = unit with { Inventory = AllocateStationInventory(state, unit, assignedByUnit[unit.Id], objects) };
        }
        var machines = state.Physical.Board.Machines;
        var machineIndex = machines.FindIndex(m => m.Id == interaction.MachineId);
        var machine = machines[machineIndex];
        machines[machineIndex] = machine with { UseCounts = machine.UseCounts.SetItem(interaction.SideId,
            checked(machine.UseCounts.GetValueOrDefault(interaction.SideId) + 1)) };
        var events = new ResolutionEvents(state, random, observe);
        foreach (var accounted in accounting.Events) events.Add(accounted);
        events.Add(new RulesEvent("MachineOperated", interaction.OperatorId) { MachineId = interaction.MachineId,
            SourceUnitId = interaction.OperatorId, SideId = interaction.SideId, ActionId = ability.ActionId, Cell = machine.Position,
            EquipmentTransfers = [.. allocation.Assignments.Where(a => originalOwners[a.ObjectId] != a.UnitId).OrderBy(a => a.ObjectId, StringComparer.Ordinal)
                .Select(a => new EquipmentTransfer(a.ObjectId, originalOwners[a.ObjectId], a.UnitId))] });
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    public static EngineResult CancelMachineInteraction(GameState previous, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        RequireEquipmentStation(previous);
        var state = previous.Copy();
        state.Pending = null;
        state.MachineInteraction = null;
        var events = new ResolutionEvents(state, random, observe);
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }
}
