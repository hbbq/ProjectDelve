using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public sealed record MachineResource(ResourceType Type, int Quantity);
public sealed record MachineReveal(int Distance);
public sealed record MachineOperation(ImmutableArray<MachineResource> Costs, ResourceType? OutputType = null, int OutputQuantity = 1)
{
    public ResourceOutputTable? OutputTable { get; init; }
    public MachineReveal? Reveal { get; init; }
    public int? MaxUsesTotal { get; init; }
    public int? MaxUsesPerSide { get; init; }
    [JsonIgnore] public bool RequiresOutputCell => Reveal is null;
    public string OutputDescription => Reveal is { } reveal ? $"Reveal Distance {reveal.Distance} for the operator's Side" : OutputTable is null ? OutputType!.Name :
        "d6: " + string.Join(", ", OutputTable.Faces.Select((type, i) => $"{i + 1} {type.Name}"));
    [JsonIgnore] public string Description => (Costs.IsEmpty ? "" : "Spend " + string.Join(", ", Costs.Select(c => $"{c.Quantity} {c.Type.Name}")) + " -> ") +
        (Reveal is null ? $"Output {OutputQuantity} {OutputDescription} on the operator's Cell" : OutputDescription);
}
public sealed record MachineType(string Id, string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ResourceStorage? Storage, MachineOperation Operation)
{
    public string? RequiredTerrainTypeId { get; init; }
}
public sealed record Machine(string Id, Cell Position, MachineType Type)
{
    public string? OwnerSide { get; init; }
    public ImmutableArray<MachineResource> StoredResources { get; init; } = [];
    public ImmutableDictionary<string, int> UseCounts { get; init; } = ImmutableDictionary<string, int>.Empty;
    public long TotalUses => UseCounts.Values.Sum(count => (long)count);
    public int? RemainingUsesTotal => Type.Operation.MaxUsesTotal is { } max ? (int)Math.Max(0, max - TotalUses) : null;
    public int? RemainingUsesFor(string side) => Type.Operation.MaxUsesPerSide is { } max ? Math.Max(0, max - UseCounts.GetValueOrDefault(side)) : null;
    public bool HasUsesFor(string side) => RemainingUsesTotal is not 0 && RemainingUsesFor(side) is not 0;
    public int StoredQuantity(string resourceTypeId) => StoredResources.SingleOrDefault(r => r.Type.Id == resourceTypeId)?.Quantity ?? 0;
}

public static class MachineTypes
{
    public static MachineType DiamondPress { get; } = new("diamond-press", "Diamond Press",
        new([new(ResourceTypes.Coal, 3)]), new([new(ResourceTypes.Coal, 3)], ResourceTypes.Diamond, OutputQuantity: 1));
    public static MachineType SlotMachine { get; } = new("slot-machine", "Slot Machine",
        new([new(ResourceTypes.Fruit, 3)]), new([new(ResourceTypes.Fruit, 3)], null) { OutputTable = RacingSlotTable.Table });
    public static MachineType Beacon { get; } = new("beacon", "Beacon", null,
        new([]) { Reveal = new(15), MaxUsesPerSide = 1 }) { RequiredTerrainTypeId = "stone-floor" };
    public static ImmutableArray<MachineType> All { get; } = [DiamondPress, SlotMachine, Beacon];
}

internal static class MachineRules
{
    internal static void Validate(GameState state)
    {
        var board = state.Physical.Board;
        if (board.Machines.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Position is null ||
                !MovementRules.Inside(board, m.Position) ||
                m.Type is null || string.IsNullOrWhiteSpace(m.Type.Id) || string.IsNullOrWhiteSpace(m.Type.Name)) ||
            board.Machines.Select(m => m.Id).Distinct().Count() != board.Machines.Count ||
            board.Machines.Select(m => m.Position).Distinct().Count() != board.Machines.Count ||
            board.Machines.Any(m => state.Units.Any(u => u.Id == m.Id)))
            throw new ArgumentException("Machines require unique identities and distinct Cells on the Board.");
        foreach (var machine in board.Machines)
        {
            if (machine.OwnerSide is not null && !state.Objectives.Any(side => side.SideId == machine.OwnerSide))
                throw ScenarioValidationContext.At(new ArgumentException("Machine OwnerSide must reference a valid Side."),
                    $"machines[{board.Machines.IndexOf(machine)}].ownerSide");
            ValidateDefinition(machine.Type);
            if (machine.Type.RequiredTerrainTypeId is { } required && board.TerrainDefinitionAt(machine.Position).Id != required)
                throw new ArgumentException($"Machine requires {required} terrain.");
            if (machine.UseCounts is null || machine.UseCounts.Any(p => p.Value < 0 ||
                    !state.Objectives.Any(s => s.SideId == p.Key) ||
                    machine.Type.Operation.MaxUsesPerSide is { } perSide && p.Value > perSide) ||
                machine.Type.Operation.MaxUsesTotal is { } total && machine.TotalUses > total)
                throw new ArgumentException("Invalid Machine use counts.");
            if (machine.StoredResources.IsDefault || machine.StoredResources.Any(r => r is null ||
                    !ResourceRules.ValidType(r.Type) || r.Quantity < 0 || r.Quantity > Capacity(machine, r.Type.Id) ||
                    !(machine.Type.Storage?.Capacities.Any(c => c.Type.Id == r.Type.Id) ?? false)) ||
                machine.StoredResources.Select(r => r.Type.Id).Distinct().Count() != machine.StoredResources.Length)
                throw new ArgumentException("Invalid Machine Operation or stored Resources.");
        }
    }

    internal static void ValidateDefinition(MachineType type)
    {
        if (string.IsNullOrWhiteSpace(type.Id) || string.IsNullOrWhiteSpace(type.Name)) throw new ArgumentException("Machines require ID and name.");
        if (type.RequiredTerrainTypeId is { } required && string.IsNullOrWhiteSpace(required)) throw new ArgumentException("Invalid Machine terrain requirement.");
        if (type.Storage is not null) ResourceRules.ValidateCapacity(type.Storage);
        var operation = type.Operation;
        if (operation is null || operation.Costs.IsDefault || operation.MaxUsesTotal < 0 || operation.MaxUsesPerSide < 0 ||
            operation.OutputQuantity != 1 ||
            (operation.Reveal is { } reveal ? reveal.Distance < 0 || operation.OutputType is not null || operation.OutputTable is not null :
             type.Storage is null || operation.Costs.IsEmpty || (operation.OutputTable is null ? !ResourceRules.ValidType(operation.OutputType) :
                operation.OutputType is not null || operation.OutputTable.Faces.IsDefault || operation.OutputTable.Faces.Length != 6 ||
                operation.OutputTable.Faces.Any(t => !ResourceRules.ValidType(t)))) ||
            operation.Costs.Any(c => c is null || !ResourceRules.ValidType(c.Type) || c.Quantity < 1 ||
                c.Quantity > (type.Storage?.Capacities.SingleOrDefault(r => r.Type.Id == c.Type.Id)?.Capacity ?? 0)) ||
            operation.Costs.Select(c => c.Type.Id).Distinct().Count() != operation.Costs.Length)
            throw new ArgumentException("Invalid Machine Operation or stored Resources.");
    }

    private static int Capacity(Machine machine, string resourceTypeId) =>
        machine.Type.Storage?.Capacities.SingleOrDefault(c => c.Type.Id == resourceTypeId)?.Capacity ?? 0;

    internal static bool UsefulReveal(GameState state, Machine machine, string side, Board? knownBoard = null) =>
        machine.Type.Operation.Reveal is { } reveal && FogOfWar.IsEnabled(state, side) &&
        FogOfWar.VisionArea(knownBoard ?? state.Physical.Board, [machine.Position], reveal.Distance,
            excludedMachineId: machine.Id).Any(c => !FogOfWar.IsExplored(state, side, c));

    internal static bool CanOperateFrom(GameState state, Unit actor, Machine machine, Cell cell, Board? board = null,
        bool checkReveal = true) =>
        (machine.OwnerSide is null || machine.OwnerSide == actor.SideId) && machine.HasUsesFor(actor.SideId) &&
        Adjacent(board ?? state.Physical.Board, cell, machine) &&
        (board ?? state.Physical.Board).EdgeBetween(cell, machine.Position).Passable() &&
        (!machine.Type.Operation.RequiresOutputCell || !state.Resources.Any(r => r.Position == cell)) &&
        machine.Type.Operation.Costs.All(c => machine.StoredQuantity(c.Type.Id) >= c.Quantity) &&
        (!checkReveal || machine.Type.Operation.Reveal is null || UsefulReveal(state, machine, actor.SideId, board));

    private static bool Adjacent(Board board, Cell cell, Machine machine) =>
        Math.Max(Math.Abs(cell.X - machine.Position.X), Math.Abs(cell.Y - machine.Position.Y)) == 1 &&
        AttackRules.HasGeometricLineOfSight(board, cell, machine.Position, machine.Id);

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit actor) => state.AbilitiesOf(actor.Id)
        .Where(ability => ability.Definition.Mechanic is GiveResource or Operate && state.CanUseAbility(actor.Id, ability))
        .SelectMany(ability => Candidates(state, actor, ability));

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit actor, ResolvedAbility ability)
    {
        var type = state.Types.Single(t => t.Id == actor.TypeId);
        var board = state.Physical.Board;
        foreach (var cell in FootprintGeometry.OccupiedCells(state, actor.Id))
        foreach (var machine in board.Machines.Where(m => (m.OwnerSide is null || m.OwnerSide == actor.SideId) && Adjacent(board, cell, m)))
        {
            var source = type.Footprint == Footprint.OneByOne ? null : cell;
            var suffix = source is null ? "" : $":{cell.X},{cell.Y}";
            var resource = state.Resources.SingleOrDefault(r => r.Position == cell);
            if (ability.Definition.Mechanic is GiveResource && resource is not null &&
                machine.StoredQuantity(resource.Type.Id) < Capacity(machine, resource.Type.Id))
            {
                yield return new($"{ability.CandidatePrefix}:machine:{machine.Id}{suffix}", Destination: source,
                    Action: UnitAction.GiveResource) { MachineId = machine.Id, Ability = ability };
            }
            if (ability.Definition.Mechanic is Operate && CanOperateFrom(state, actor, machine, cell))
            {
                yield return new($"{ability.CandidatePrefix}:{machine.Id}{suffix}", Destination: source,
                    Action: UnitAction.Operate) { MachineId = machine.Id, Ability = ability };
            }
        }
    }

    internal static RulesEvent? Resolve(GameState state, string actorId, Candidate action, IRandomProvider random)
    {
        if (!Candidates(state, state.Units.Single(u => u.Id == actorId), action.Ability!).Any(c => c.Key == action.Key))
            return null;
        var machines = state.Physical.Board.Machines;
        var index = machines.FindIndex(m => m.Id == action.MachineId);
        var machine = machines[index];
        var cell = action.Destination ?? state.Physical.Figures.Single(f => f.Id == actorId).Position;
        if (action.Action == UnitAction.GiveResource)
        {
            var resource = state.Resources.Single(r => r.Position == cell);
            var quantity = machine.StoredQuantity(resource.Type.Id) + 1;
            machines[index] = machine with { StoredResources = [.. machine.StoredResources.Where(r => r.Type.Id != resource.Type.Id), new(resource.Type, quantity)] };
            state.Resources.Remove(resource);
            return new("ResourceStored", actorId) { MachineId = machine.Id, SourceUnitId = actorId, Cell = cell,
                ActionId = action.Ability!.ActionId, ResourceType = resource.Type, StoredQuantity = quantity };
        }
        var operation = machine.Type.Operation;
        var side = state.Units.Single(u => u.Id == actorId).SideId;
        var updated = machine with {
            UseCounts = machine.UseCounts.SetItem(side, checked(machine.UseCounts.GetValueOrDefault(side) + 1)),
            StoredResources = [.. machine.StoredResources.Select(r => r with
                { Quantity = r.Quantity - (operation.Costs.SingleOrDefault(c => c.Type.Id == r.Type.Id)?.Quantity ?? 0) })] };
        if (operation.Reveal is { } reveal)
        {
            var cells = FogOfWar.VisionArea(state.Physical.Board, [machine.Position], reveal.Distance, excludedMachineId: machine.Id);
            if (!state.ExploredCells.TryGetValue(side, out var explored)) state.ExploredCells[side] = explored = [];
            explored.UnionWith(cells);
            machines[index] = updated;
            return new("MachineOperated", actorId) { MachineId = machine.Id, SourceUnitId = actorId,
                SideId = side, Cell = machine.Position, ActionId = action.Ability!.ActionId, ConsumedResources = operation.Costs };
        }
        int? roll = operation.OutputTable is null ? null : random.RollD6();
        var output = roll is { } value ? operation.OutputTable!.Result(value) : operation.OutputType!;
        machines[index] = updated;
        state.Resources.Add(new(output, cell));
        return new("MachineOperated", actorId) { MachineId = machine.Id, SourceUnitId = actorId, Cell = cell, ActionId = action.Ability!.ActionId,
            ResourceType = output, DieRoll = roll, ConsumedResources = operation.Costs };
    }
}
