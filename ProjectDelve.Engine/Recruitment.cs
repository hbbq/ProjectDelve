using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record ExpeditionPartyRoster(string SideId, int Capacity, ImmutableArray<string> MemberIds);
public sealed record RecruitmentOffer(string UnitTypeId, string Name, int Cost, bool CanRecruit,
    ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics, Cell? SpawnCell, UnitType Type);
public sealed record RecruitmentPreview(bool CanConfirm, ImmutableArray<EquipmentAllocationDiagnostic> Diagnostics,
    ImmutableArray<RecruitmentOffer> Offers, string? SelectedTypeId, Cell? SpawnCell,
    int PartySize, int PartyCapacity, int CurrentCredits, long ResultingCredits);

internal static class PartyRules
{
    internal static void Validate(GameState state)
    {
        if (state.Party is not { } party) return;
        if (string.IsNullOrWhiteSpace(party.SideId) || party.Capacity <= 0 ||
            party.MemberIds.IsDefault || party.MemberIds.Length > party.Capacity ||
            !state.Objectives.Any(o => o.SideId == party.SideId) ||
            party.MemberIds.Any(string.IsNullOrWhiteSpace) ||
            party.MemberIds.Distinct(StringComparer.Ordinal).Count() != party.MemberIds.Length ||
            party.MemberIds.Any(id => !state.Units.Any(u => u.Id == id && u.SideId == party.SideId)))
            throw ScenarioValidationContext.At(new ArgumentException("A Party requires distinct existing same-Side Unit identities within its capacity."), "party");
        var members = state.Units.Where(u => party.MemberIds.Contains(u.Id));
        if (members.GroupBy(u => u.TypeId).Any(g => state.Types.Single(t => t.Id == g.Key).Unique && g.Count() > 1))
            throw ScenarioValidationContext.At(new ArgumentException("The Party cannot retain duplicate Unique Unit Types."), "party.memberIds");
    }
}

public static partial class GameEngine
{
    private static Cell? RecruitmentSpawnCell(GameState state, string operatorId, UnitType type) =>
        RecruitmentSpawnCells(state, operatorId, type).FirstOrDefault();

    internal static IEnumerable<Cell> RecruitmentSpawnCells(GameState state, string operatorId, UnitType type)
    {
        const int maximumSteps = 3;
        var side = state.Units.Single(u => u.Id == operatorId).SideId;
        var known = FogOfWar.IsEnabled(state, side) ? state.ExploredCells.GetValueOrDefault(side) ?? [] : null;
        // Recruitment follows open routes even when the recruit ordinarily has
        // Phase. The operator's footprint supplies the starting Cells only.
        var traversal = MovementTraversal.For(type, known) with { IgnoreTerrainAndEdges = false };
        var board = state.Physical.Board;
        var occupied = new PlacementLookup(state);
        var seen = FootprintGeometry.OccupiedCells(state, operatorId).ToHashSet();
        var frontier = seen.ToArray();
        for (var distance = 1; distance <= maximumSteps; distance++)
        {
            var next = new HashSet<Cell>();
            foreach (var cell in frontier)
            foreach (var candidate in new Cell[] { new(cell.X, cell.Y - 1), new(cell.X - 1, cell.Y),
                new(cell.X + 1, cell.Y), new(cell.X, cell.Y + 1) })
                if (!seen.Contains(candidate) &&
                    MovementRules.CanStep(board, Footprint.OneByOne, cell, candidate, traversal) &&
                    !occupied.HasOtherFigure([candidate], null))
                    next.Add(candidate);
            foreach (var candidate in next.OrderBy(c => c.Y).ThenBy(c => c.X))
                if (SpatialRules.CanPlaceUnit(state, type, candidate, lookup: occupied) &&
                    FootprintGeometry.OccupiedCells(type.Footprint, candidate)
                        .All(c => FogOfWar.IsExplored(state, side, c) && TerrainCatalog.Accepts(type, board.TerrainDefinitionAt(c))))
                    yield return candidate;
            seen.UnionWith(next);
            frontier = next.ToArray();
        }
    }

    public static RecruitmentPreview PreviewRecruitment(GameState state, string? unitTypeId = null)
    {
        var interaction = RequireEquipmentStation(state);
        var party = state.Party;
        var balance = state.SideCredits.GetValueOrDefault(interaction.SideId);
        if (party is null || party.SideId != interaction.SideId)
            return new(false, [new("recruitment-unavailable", "Recruitment requires the operating Side's Expedition Party.")],
                [], unitTypeId, null, 0, 0, balance, balance);
        var offers = state.Types.Where(t => t.RecruitmentCost is not null)
            .OrderBy(t => t.Id, StringComparer.Ordinal).Select(type =>
        {
            var diagnostics = ImmutableArray.CreateBuilder<EquipmentAllocationDiagnostic>();
            if (party.MemberIds.Length >= party.Capacity)
                diagnostics.Add(new("party-full", "The Party is at its capacity."));
            if (type.Unique && party.MemberIds.Any(id => state.Units.Single(u => u.Id == id).TypeId == type.Id) ||
                !state.CanPlaceUnitType(type, party.SideId))
                diagnostics.Add(new("unique-recruit", "This Unique Unit Type is already represented on the Side or in the Party."));
            if (type.Footprint != Footprint.OneByOne)
                diagnostics.Add(new("unsupported-recruit-footprint", "Expedition deployment currently supports one-Cell recruits."));
            if (!MovementCapabilities.For(type).Contains(MovementKinds.Land) ||
                TerrainCatalog.All.Where(terrain => terrain.Passable).Any(terrain => !TerrainCatalog.Accepts(type, terrain)))
                diagnostics.Add(new("unsupported-recruit-terrain", "This Unit Type cannot use the Expedition's ordinary Land deployment surfaces."));
            var cost = type.RecruitmentCost!.Value;
            if (cost < 0) diagnostics.Add(new("invalid-recruitment-cost", "Recruitment cost must be nonnegative."));
            if (cost > balance) diagnostics.Add(new("insufficient-credits", "The Side has insufficient Credits to recruit this Unit."));
            var cell = RecruitmentSpawnCell(state, interaction.OperatorId, type);
            if (cell is null) diagnostics.Add(new("no-recruitment-cell", "There is no legal recruitment Cell near the operator."));
            return new RecruitmentOffer(type.Id, type.DisplayName ?? type.Id, cost, diagnostics.Count == 0,
                diagnostics.ToImmutable(), cell, type);
        }).ToImmutableArray();
        var chosen = offers.SingleOrDefault(o => o.UnitTypeId == unitTypeId);
        var errors = chosen?.Diagnostics ?? (unitTypeId is null
            ? ImmutableArray<EquipmentAllocationDiagnostic>.Empty
            : [new("unavailable-recruit", "This Unit Type has no recruitment price in the region's definitions.")]);
        return new(chosen?.CanRecruit == true, errors, offers, unitTypeId, chosen?.SpawnCell,
            party.MemberIds.Length, party.Capacity, balance, chosen is null ? balance : (long)balance - chosen.Cost);
    }

    public static EngineResult ConfirmRecruitment(GameState previous, string unitTypeId, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        var interaction = RequireEquipmentStation(previous);
        var preview = PreviewRecruitment(previous, unitTypeId);
        if (!preview.CanConfirm)
            throw new EquipmentAllocationException(preview.Diagnostics.IsEmpty
                ? [new("unavailable-recruit", "Select a recruitable Unit Type.")] : preview.Diagnostics);
        var offer = preview.Offers.Single(o => o.UnitTypeId == unitTypeId);
        var state = previous.Copy();
        state.Pending = null;
        state.MachineInteraction = null;
        var accounting = new ResolutionEvents(state, StationAccountingRandom.Instance, null, captureState: false);
        var ability = interaction.Operation.Ability!;
        CommitAbilityInvocation(state, interaction.OperatorId, ability, accounting);
        EmitActionUsed(new(DecisionKind.Activation, state.Units.Single(u => u.Id == interaction.OperatorId).TypeId,
            interaction.OperatorId, [interaction.Operation], false), interaction.Operation.Key, accounting);
        var prefix = offer.Type.Id.EndsWith("-type", StringComparison.Ordinal) ? offer.Type.Id[..^5] : offer.Type.Id;
        var number = 1;
        string id;
        do { id = $"{prefix}-{number++}"; }
        while (state.Units.Any(u => u.Id == id || u.SummonerUnitId == id) ||
            state.Physical.Board.Machines.Any(m => m.Id == id) || state.Controllers.Any(c => c.Token.GroupId == id));
        var token = new ActivationToken(offer.Type.Id, interaction.SideId, id);
        state.Controllers.Add(new(token, ControllerKind.Human));
        state.SideCredits[interaction.SideId] = checked((int)preview.ResultingCredits);
        state.PlaceUnit(offer.Type.Id, id, interaction.SideId, offer.SpawnCell!, groupId: id);
        state.Party = state.Party! with { MemberIds = state.Party.MemberIds.Add(id) };
        var machines = state.Physical.Board.Machines;
        var index = machines.FindIndex(m => m.Id == interaction.MachineId);
        var machine = machines[index];
        machines[index] = machine with { UseCounts = machine.UseCounts.SetItem(interaction.SideId,
            checked(machine.UseCounts.GetValueOrDefault(interaction.SideId) + 1)) };
        var events = new ResolutionEvents(state, random, observe);
        foreach (var entry in accounting.Events) events.Add(entry);
        events.Add(new RulesEvent("MachineOperated", interaction.OperatorId)
        {
            MachineId = interaction.MachineId, SourceUnitId = interaction.OperatorId, SideId = interaction.SideId,
            ActionId = ability.ActionId, Cell = machine.Position, CreditsAmount = -offer.Cost,
            CreditsBalance = checked((int)preview.ResultingCredits), RecruitedUnitId = id, RecruitmentCost = offer.Cost
        });
        events.Add(new RulesEvent("UnitCreated", id, TypeId: offer.Type.Id, AbilityName: ability.Definition.Name, Posture: Posture.Upright)
            { SourceUnitId = interaction.OperatorId, SideId = interaction.SideId, Cell = offer.SpawnCell, ActionId = ability.ActionId });
        ResolveArrival(state, id, events);
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }
}
