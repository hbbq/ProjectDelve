using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public sealed record EquipmentTransfer(string ObjectId, string FromUnitId, string ToUnitId);
public sealed record EquipmentAssignment(string ObjectId, string UnitId);
public sealed record EquipmentSale(string ObjectId, string FromUnitId, int SellValue);
public sealed record StationUpgrade(string TargetId, int CurrentLevel, int NextLevel, int Cost, bool Selected);
public sealed record EquipmentAllocation(ImmutableArray<EquipmentAssignment> Assignments)
{
    [JsonIgnore] public ImmutableArray<string> SoldObjectIds { get; init; } = [];
    [JsonPropertyName("soldObjectIds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? SerializedSoldObjectIds
    {
        get => SoldObjectIds.IsDefaultOrEmpty ? null : SoldObjectIds;
        init => SoldObjectIds = value ?? [];
    }
    [JsonIgnore] public ImmutableArray<string> UpgradedUnitIds { get; init; } = [];
    [JsonPropertyName("upgradedUnitIds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? SerializedUpgradedUnitIds
    {
        get => UpgradedUnitIds.IsDefaultOrEmpty ? null : UpgradedUnitIds;
        init => UpgradedUnitIds = value ?? [];
    }
    [JsonIgnore] public ImmutableArray<string> UpgradedEquipmentIds { get; init; } = [];
    [JsonPropertyName("upgradedEquipmentIds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? SerializedUpgradedEquipmentIds
    {
        get => UpgradedEquipmentIds.IsDefaultOrEmpty ? null : UpgradedEquipmentIds;
        init => UpgradedEquipmentIds = value ?? [];
    }
}
public sealed record EquipmentStationInteraction(ImmutableArray<string> EligibleUnitIds,
    EquipmentAllocation OriginalAllocation)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanRecruit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? EligibleUpgradeUnitIds { get; init; }
}
public sealed record PendingMachineInteraction(string MachineId, string OperatorId, string SideId,
    Candidate Operation, EquipmentStationInteraction? EquipmentStation = null)
{
    public PaidMachineInteraction? PaidServices { get; init; }
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
public sealed record EquipmentStationStats(int Mov, int Rng, int Atk, int Def, int Hp, int SightDistance)
{
    public int Moves { get; init; }
    public int Actions { get; init; }
    public int BonusActionUses { get; init; }
}
public sealed record EquipmentStationUnitPreview(string UnitId, ImmutableArray<OwnedObject> Allocation,
    int Mov, int Rng, int Atk, int Def, int Hp, int SightDistance,
    ImmutableArray<string> MovementCapabilities, bool HasPhase, ImmutableArray<ResolvedAbility> Abilities)
{
    public int CurrentHp { get; init; }
    public int ResultingHp { get; init; }
    public int CurrentLevel { get; init; }
    public int ResultingLevel { get; init; }
    public int Moves { get; init; }
    public int Actions { get; init; }
    public int BonusActionUses { get; init; }
    public bool ProposedAllocationAvailable { get; init; } = true;
    public bool BaselineHasPhase { get; init; }
    public ImmutableArray<string> GainedMovementCapabilities { get; init; } = [];
    public ImmutableArray<string> LostMovementCapabilities { get; init; } = [];
    public ImmutableArray<AbilitySourceReference> GainedAbilities { get; init; } = [];
    public ImmutableArray<AbilitySourceReference> LostAbilities { get; init; } = [];
    public EquipmentStationStats Baseline { get; init; } = new(0, 0, 0, 0, 0, 0);
    public EquipmentStationStats Difference { get; init; } = new(0, 0, 0, 0, 0, 0);
    public ImmutableArray<EquipmentStationAbilityPreview> AbilityPreviews { get; init; } = [];
    public ImmutableArray<EquipmentStationAbilityPreview> OpeningAbilityPreviews { get; init; } = [];
    public ImmutableArray<EquipmentStationAbilityPreview> LostAbilityPreviews { get; init; } = [];
}
public sealed record EquipmentStationPreview(bool CanConfirm, ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics,
    ImmutableArray<EquipmentStationUnitPreview> Units)
{
    public ImmutableArray<EquipmentSale> Sales { get; init; } = [];
    public long TotalProceeds { get; init; }
    public long TotalUpgradeCost { get; init; }
    public ImmutableArray<StationUpgrade> UnitUpgrades { get; init; } = [];
    public ImmutableArray<StationUpgrade> EquipmentUpgrades { get; init; } = [];
    public int CurrentCredits { get; init; }
    public long ResultingCredits { get; init; }
}

public static partial class GameEngine
{
    private static ImmutableArray<Unit> EquipmentStationUpgradeUnits(GameState state, string side) =>
        [.. state.Units.Where(u => u.SideId == side && u.CurrentHp > 0 && state.Physical.Figures.Any(f => f.Id == u.Id))];

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
            candidate, new([.. units.Select(u => u.Id)], allocation)
            { EligibleUpgradeUnitIds = [.. EquipmentStationUpgradeUnits(state, actor.SideId).Select(u => u.Id)],
              CanRecruit = state.Party?.SideId == actor.SideId });
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
            !payload.OriginalAllocation.SoldObjectIds.IsDefaultOrEmpty ||
            !payload.OriginalAllocation.UpgradedUnitIds.IsDefaultOrEmpty || !payload.OriginalAllocation.UpgradedEquipmentIds.IsDefaultOrEmpty ||
            payload.OriginalAllocation.Assignments.Any(a => a is null) ||
            !payload.OriginalAllocation.Assignments.OrderBy(a => a.ObjectId, StringComparer.Ordinal).SequenceEqual(original) ||
            state.Pending.MachineInteraction is not { } pending || pending.MachineId != interaction.MachineId ||
            pending.OperatorId != interaction.OperatorId || pending.SideId != interaction.SideId)
            throw new InvalidOperationException("The pending Equipment Station roster is stale or invalid.");
        if (payload.EligibleUpgradeUnitIds is { } upgradeIds &&
            (upgradeIds.IsDefault || !upgradeIds.SequenceEqual(EquipmentStationUpgradeUnits(state, interaction.SideId).Select(u => u.Id))))
            throw new InvalidOperationException("The pending Equipment Station upgrade roster is stale or invalid.");
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
        try { if (state.MachineInteraction.PaidServices is not null) RequirePaidMachine(state); else RequireEquipmentStation(state); }
        catch (InvalidOperationException error) { throw new ArgumentException("Invalid Machine interaction continuation.", error); }
    }

    private static bool Transferable(GameState state, OwnedObject obj) => state.EquipmentTypeId(obj) is not null || state.ItemTypeId(obj) is not null;

    public static int? SellValueOf(this GameState state, OwnedObject obj) => state.EquipmentTypeId(obj) is { } equipment
        ? state.Equipment(equipment).SellValue : state.ItemTypeId(obj) is { } item ? state.Item(item).SellValue : null;

    public static EquipmentStationPreview PreviewEquipmentStation(GameState state, EquipmentAllocation? allocation) =>
        ValidateEquipmentAllocation(state, allocation, includePreview: true);

    private static EquipmentStationPreview ValidateEquipmentAllocation(GameState state, EquipmentAllocation? allocation, bool includePreview)
        => ValidateEquipmentAllocation(state, allocation, includePreview, out _, out _);

    private static EquipmentStationPreview ValidateEquipmentAllocation(GameState state, EquipmentAllocation? allocation, bool includePreview,
        out GameState? proposed, out ImmutableArray<RulesEvent> accounted)
    {
        proposed = null;
        accounted = [];
        var interaction = RequireEquipmentStation(state);
        var units = EquipmentStationUnits(state, interaction.SideId);
        var upgradeUnits = EquipmentStationUpgradeUnits(state, interaction.SideId);
        var objects = units.SelectMany(u => u.Inventory.Where(o => Transferable(state, o) && o.Id != interaction.ReservedConsumptionObjectId)).ToArray();
        var original = units.SelectMany(u => u.Inventory.Where(o => Transferable(state, o) && o.Id != interaction.ReservedConsumptionObjectId)
            .Select(o => new EquipmentAssignment(o.Id, u.Id))).ToArray();
        var objectIndex = objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var unitIds = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var originalOwners = original.ToDictionary(a => a.ObjectId, a => a.UnitId, StringComparer.Ordinal);
        var requested = allocation?.Assignments ?? [.. original];
        var sold = allocation?.SoldObjectIds ?? [];
        var upgradedUnits = allocation?.UpgradedUnitIds ?? [];
        var upgradedEquipment = allocation?.UpgradedEquipmentIds ?? [];
        var selectedUnits = upgradedUnits.IsDefault ? [] : upgradedUnits.ToHashSet(StringComparer.Ordinal);
        var selectedEquipment = upgradedEquipment.IsDefault ? [] : upgradedEquipment.ToHashSet(StringComparer.Ordinal);
        var unitOffers = upgradeUnits.Select(unit => (Unit: unit, Cost: state.UnitUpgradeCostOf(unit)))
            .Where(entry => entry.Cost is >= 0)
            .Select(entry => new StationUpgrade(entry.Unit.Id, entry.Unit.Level, entry.Unit.Level + 1, entry.Cost!.Value, selectedUnits.Contains(entry.Unit.Id)))
            .OrderBy(entry => entry.TargetId, StringComparer.Ordinal).ToImmutableArray();
        var equipmentOffers = objects.Select(obj => (Object: obj, Cost: state.EquipmentUpgradeCostOf(obj)))
            .Where(entry => entry.Cost is >= 0)
            .Select(entry => new StationUpgrade(entry.Object.Id, entry.Object.Level, entry.Object.Level + 1, entry.Cost!.Value, selectedEquipment.Contains(entry.Object.Id)))
            .OrderBy(entry => entry.TargetId, StringComparer.Ordinal).ToImmutableArray();
        var previewUnits = upgradeUnits.Where(unit => unitIds.Contains(unit.Id) || unitOffers.Any(offer => offer.TargetId == unit.Id)).ToImmutableArray();
        var sales = ImmutableArray.CreateBuilder<EquipmentSale>();
        var currentCredits = state.SideCredits.GetValueOrDefault(interaction.SideId);
        long proceeds = 0, upgradeCost = 0;
        var diagnostics = ImmutableArray.CreateBuilder<EquipmentAllocationDiagnostic>();
        void Reject(string code, string message, string? objectId = null, string? unitId = null) =>
            diagnostics.Add(new(code, message, objectId, unitId));
        if (requested.IsDefault || sold.IsDefault || upgradedUnits.IsDefault || upgradedEquipment.IsDefault)
            Reject("invalid-allocation", "A complete Station proposal is required.");
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
            foreach (var objectId in sold)
            {
                if (objectId is null || !objectIndex.TryGetValue(objectId, out var obj))
                    Reject("invalid-sale", "The sale contains an unavailable object.");
                else if (!assignedIds.Add(objectId))
                    Reject("duplicate-assignment", "Each object must have exactly one final owner or sale.", objectId);
                else if (state.SellValueOf(obj) is not { } value || value < 0)
                    Reject("unsellable-object", "This object has no sell value.", objectId);
                else
                {
                    sales.Add(new(objectId, originalOwners[objectId], value));
                    proceeds += value;
                }
            }
            foreach (var entry in original)
                if (!assignedIds.Contains(entry.ObjectId))
                    Reject("missing-assignment", "Assign this object to a recipient or sale.", entry.ObjectId, entry.UnitId);
            ValidateUpgrades(upgradedUnits, unitOffers, equipment: false);
            ValidateUpgrades(upgradedEquipment, equipmentOffers, equipment: true);
        }
        void ValidateUpgrades(ImmutableArray<string> targets, ImmutableArray<StationUpgrade> offers, bool equipment)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in targets)
            {
                var offer = offers.FirstOrDefault(o => o.TargetId == id);
                if (id is null || offer is null)
                    Reject("unavailable-upgrade", "The target has no purchasable next Level.");
                else if (!seen.Add(id))
                    Reject("duplicate-upgrade", "Each instance can advance only one Level per Confirm.",
                        objectId: equipment ? id : null, unitId: equipment ? null : id);
                else if (equipment && sold.Contains(id))
                    Reject("sold-and-upgraded", "An object selected for sale cannot also be upgraded.", objectId: id);
                else upgradeCost = checked(upgradeCost + offer.Cost);
            }
        }
        EquipmentStationPreview Settlement(EquipmentStationPreview preview) => preview with
        {
            Sales = [.. sales.OrderBy(s => s.ObjectId, StringComparer.Ordinal)], TotalProceeds = proceeds,
            TotalUpgradeCost = upgradeCost, UnitUpgrades = unitOffers, EquipmentUpgrades = equipmentOffers,
            CurrentCredits = currentCredits, ResultingCredits = (long)currentCredits + proceeds - upgradeCost
        };
        if (diagnostics.Count > 0)
        {
            var rows = includePreview ? ValidateEquipmentAllocation(state, null, includePreview: true).Units
                .Select(u => u with { ProposedAllocationAvailable = false }).ToImmutableArray() : [];
            return Settlement(new(false, diagnostics.ToImmutable(), rows));
        }
        var draft = BuildStationFinalState(state, interaction, requested, selectedUnits, selectedEquipment, out accounted);
        var assignedByUnit = requested.ToLookup(a => a.UnitId, StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var equipped = assignedByUnit[unit.Id].Select(a => objectIndex[a.ObjectId]).ToImmutableArray();
            if (equipped.Any(o => !LootRules.CanOwn(state, unit with { Inventory = [] }, o)))
                Reject("ownership-capability", "The recipient cannot own that object category.", unitId: unit.Id);
            var equipmentTypes = equipped.Select(state.EquipmentTypeId).OfType<string>().ToArray();
            if (equipmentTypes.Distinct(StringComparer.Ordinal).Count() != equipmentTypes.Length)
                Reject("duplicate-equipment-type", "A Unit may own only one object of each Equipment Type.", unitId: unit.Id);
        }
        foreach (var unit in previewUnits)
        {
            var position = draft.Physical.Figures.Single(f => f.Id == unit.Id).Position;
            if (!SpatialRules.CanPlaceUnit(draft, unit.Id, position))
                Reject("illegal-occupancy", "The proposed Equipment does not permit a Unit's current placement.", unitId: unit.Id);
        }
        if (!requested.Any(a => originalOwners[a.ObjectId] != a.UnitId) && sales.Count == 0 && selectedUnits.Count == 0 && selectedEquipment.Count == 0)
            Reject("no-transfer", "Confirm requires an object transfer, sale or upgrade.");
        var balance = (long)currentCredits + proceeds - upgradeCost;
        if (balance < 0) Reject("insufficient-credits", "The Side has insufficient Credits for this complete proposal.");
        if (balance + state.CreditsPiles.Sum(p => (long)p.Amount) > int.MaxValue)
            Reject("credits-overflow", "The resulting Credits balance exceeds the supported range.");
        if (diagnostics.Count == 0)
        {
            if (sales.Count > 0 || upgradeCost > 0) draft.SideCredits[interaction.SideId] = checked((int)balance);
            proposed = draft;
        }
        if (!includePreview) return Settlement(new(diagnostics.Count == 0, diagnostics.ToImmutable(), []));
        var results = previewUnits.Select(unit =>
        {
            var bearer = draft.Units.Single(u => u.Id == unit.Id) with { CurrentHp = draft.EffectiveMaxHpOf(unit.Id) };
            var normalized = new GameState
            {
                Units = [bearer], Types = draft.Types, CurrentUnitId = unit.Id,
                Physical = new(new Board(4, 4, []), [new Figure(unit.Id, new Cell(1, 1), Posture.Upright)]),
                EquipmentTypes = draft.EquipmentTypes, ItemTypes = draft.ItemTypes, LootCardDefinitions = draft.LootCardDefinitions
            };
            var baselineState = normalized.Copy();
            baselineState.Units = [unit with { CurrentHp = state.EffectiveMaxHpOf(unit) }];
            EquipmentStationStats Stats(GameState value) => new(value.EffectiveMovOf(unit.Id), value.EffectiveRngOf(unit.Id),
                value.EffectiveAtkOf(unit.Id), value.EffectiveDefOf(unit.Id), value.EffectiveMaxHpOf(unit.Id), value.EffectiveSightDistanceOf(unit.Id))
            {
                Moves = value.EffectiveMovesPerActivation, Actions = value.EffectiveActionsPerActivation,
                BonusActionUses = value.EffectiveBonusActionUsesPerActivation
            };
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
                CurrentHp = unit.CurrentHp, ResultingHp = draft.Units.Single(u => u.Id == unit.Id).CurrentHp,
                CurrentLevel = unit.Level, ResultingLevel = bearer.Level,
                Baseline = baseline,
                Moves = current.Moves, Actions = current.Actions, BonusActionUses = current.BonusActionUses,
                BaselineHasPhase = baselineState.HasPhase(unit.Id),
                GainedMovementCapabilities = [.. capabilities.Except(baselineCapabilities, StringComparer.Ordinal)],
                LostMovementCapabilities = [.. baselineCapabilities.Except(capabilities, StringComparer.Ordinal)],
                GainedAbilities = [.. abilityRefs.Except(openingAbilities.Select(a => a.SourceReference))],
                LostAbilities = [.. lost.Select(a => a.SourceReference)],
                Difference = new(current.Mov - baseline.Mov, current.Rng - baseline.Rng, current.Atk - baseline.Atk,
                    current.Def - baseline.Def, current.Hp - baseline.Hp, current.SightDistance - baseline.SightDistance)
                {
                    Moves = current.Moves - baseline.Moves, Actions = current.Actions - baseline.Actions,
                    BonusActionUses = current.BonusActionUses - baseline.BonusActionUses
                },
                AbilityPreviews = [.. abilities.Select(a => Describe(normalized, a))],
                OpeningAbilityPreviews = [.. openingAbilities.Select(a => Describe(baselineState, a))],
                LostAbilityPreviews = [.. lost.Select(a => Describe(baselineState, a))]
            };
        }).ToImmutableArray();
        return Settlement(new(diagnostics.Count == 0, diagnostics.ToImmutable(), results));
    }

    private static GameState BuildStationFinalState(GameState state, PendingMachineInteraction interaction,
        ImmutableArray<EquipmentAssignment> assignments, HashSet<string> upgradedUnits, HashSet<string> upgradedEquipment,
        out ImmutableArray<RulesEvent> accounted)
    {
        var result = state.Copy();
        result.Pending = null;
        result.MachineInteraction = null;
        // Invocation is bound to the opening configuration. Resource/Posture
        // costs and Item consumption happen once before replacing any source.
        var events = new ResolutionEvents(result, StationAccountingRandom.Instance, null, captureState: false);
        var ability = interaction.Operation.Ability!;
        CommitAbilityInvocation(result, interaction.OperatorId, ability, events);
        EmitActionUsed(new(DecisionKind.Activation, result.Units.Single(u => u.Id == interaction.OperatorId).TypeId,
            interaction.OperatorId, [interaction.Operation], false), interaction.Operation.Key, events);
        accounted = [.. events.Events];
        var objects = EquipmentStationUnits(result, interaction.SideId).SelectMany(u => u.Inventory.Where(o => Transferable(result, o)))
            .ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var id in upgradedEquipment)
            objects[id] = AdvanceEquipmentLevel(result, objects[id]);
        var owners = assignments.ToLookup(a => a.UnitId, StringComparer.Ordinal);
        foreach (var original in EquipmentStationUpgradeUnits(result, interaction.SideId))
        {
            var unit = upgradedUnits.Contains(original.Id) ? AdvanceUnitLevel(result, original) : original;
            var retained = AllocateStationInventory(result, unit, owners[unit.Id], objects);
            unit = unit with { Inventory = [.. retained.Select(obj => objects.GetValueOrDefault(obj.Id, obj))] };
            if (ability.SourceKind == AbilitySourceKind.Unit && unit.Id == interaction.OperatorId && upgradedUnits.Contains(unit.Id))
            {
                var continuing = result.Types.Single(t => t.Id == unit.TypeId).AtLevel(unit.Level).LocalAbilities
                    .SingleOrDefault(a => a.Key == ability.Key && a.Mechanic is Operate && a.Usage.MaxUses is not null);
                if (ability.Definition.Usage.MaxUses is not null && continuing is not null)
                    unit = ChangeAbilityUseCount(unit, ability, _ => 1);
            }
            if (ability.SourceKind == AbilitySourceKind.Equipment && upgradedEquipment.Contains(ability.SourceInstanceId))
            {
                var source = unit.Inventory.SingleOrDefault(obj => obj.Id == ability.SourceInstanceId);
                if (source is not null && ability.Definition.Usage.MaxUses is not null &&
                    result.EquipmentConfiguration(source).LocalAbilities.Any(a => a.Key == ability.Key && a.Mechanic is Operate && a.Usage.MaxUses is not null))
                    unit = ChangeAbilityUseCount(unit, ability, _ => 1);
            }
            var maximum = result.EffectiveMaxHpOf(unit);
            unit = unit with { CurrentHp = upgradedUnits.Contains(unit.Id) ? maximum : Math.Min(unit.CurrentHp, maximum) };
            result.Units[result.Units.FindIndex(u => u.Id == unit.Id)] = unit;
        }
        return result;
    }

    // Preview and settlement must stay independent of gameplay randomness.
    private sealed class StationAccountingRandom : IRandomProvider
    {
        public static StationAccountingRandom Instance { get; } = new();
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => throw new InvalidOperationException("Station settlement cannot draw tokens.");
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Station settlement cannot roll dice.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Station settlement cannot roll dice.");
        public int RollD6() => throw new InvalidOperationException("Station settlement cannot roll dice.");
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
        var preview = ValidateEquipmentAllocation(previous, allocation, includePreview: false, out var proposed, out var accounting);
        if (!preview.CanConfirm) throw new EquipmentAllocationException(preview.Diagnostics);
        var state = proposed!;
        var originalOwners = interaction.EquipmentStation!.OriginalAllocation.Assignments
            .ToDictionary(a => a.ObjectId, a => a.UnitId, StringComparer.Ordinal);
        var candidate = interaction.Operation;
        var ability = candidate.Ability!;
        var machines = state.Physical.Board.Machines;
        var machineIndex = machines.FindIndex(m => m.Id == interaction.MachineId);
        var machine = machines[machineIndex];
        machines[machineIndex] = machine with { UseCounts = machine.UseCounts.SetItem(interaction.SideId,
            checked(machine.UseCounts.GetValueOrDefault(interaction.SideId) + 1)) };
        var events = new ResolutionEvents(state, random, observe);
        foreach (var accounted in accounting) events.Add(accounted);
        events.Add(new RulesEvent("MachineOperated", interaction.OperatorId) { MachineId = interaction.MachineId,
            SourceUnitId = interaction.OperatorId, SideId = interaction.SideId, ActionId = ability.ActionId, Cell = machine.Position,
            EquipmentSales = preview.Sales,
            UnitUpgrades = [.. preview.UnitUpgrades.Where(u => u.Selected)],
            EquipmentUpgrades = [.. preview.EquipmentUpgrades.Where(u => u.Selected)],
            UpgradeCost = preview.UnitUpgrades.Any(u => u.Selected) || preview.EquipmentUpgrades.Any(u => u.Selected) ? preview.TotalUpgradeCost : null,
            CreditsAmount = preview.Sales.IsEmpty && allocation.UpgradedUnitIds.IsEmpty && allocation.UpgradedEquipmentIds.IsEmpty
                ? null : checked((int)(preview.TotalProceeds - preview.TotalUpgradeCost)),
            CreditsBalance = preview.Sales.IsEmpty && allocation.UpgradedUnitIds.IsEmpty && allocation.UpgradedEquipmentIds.IsEmpty
                ? null : checked((int)preview.ResultingCredits),
            EquipmentTransfers = [.. allocation.Assignments.Where(a => originalOwners[a.ObjectId] != a.UnitId).OrderBy(a => a.ObjectId, StringComparer.Ordinal)
                .Select(a => new EquipmentTransfer(a.ObjectId, originalOwners[a.ObjectId], a.UnitId))] });
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    public static EngineResult CancelMachineInteraction(GameState previous, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        if (previous.MachineInteraction?.PaidServices is not null) RequirePaidMachine(previous); else RequireEquipmentStation(previous);
        var state = previous.Copy();
        state.Pending = null;
        state.MachineInteraction = null;
        var events = new ResolutionEvents(state, random, observe);
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }
}
