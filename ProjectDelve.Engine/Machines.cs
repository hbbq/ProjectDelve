using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record MachineResource(ResourceType Type, int Quantity);
public sealed record MachineOperation(ImmutableArray<MachineResource> Costs, ResourceType? OutputType = null, int OutputQuantity = 1)
{
    public ResourceOutputTable? OutputTable { get; init; }
    public string OutputDescription => OutputTable is null ? OutputType!.Name :
        "d6: " + string.Join(", ", OutputTable.Faces.Select((type, i) => $"{i + 1} {type.Name}"));
}
public sealed record MachineType(string Id, string Name, ResourceStorage Storage, MachineOperation Operation);
public sealed record Machine(string Id, Cell Position, MachineType Type)
{
    public string? OwnerSide { get; init; }
    public ImmutableArray<MachineResource> StoredResources { get; init; } = [];
    public int StoredQuantity(string resourceTypeId) => StoredResources.SingleOrDefault(r => r.Type.Id == resourceTypeId)?.Quantity ?? 0;
}

public static class MachineTypes
{
    public static MachineType DiamondPress { get; } = new("diamond-press", "Diamond Press",
        new([new(ResourceTypes.Coal, 3)]), new([new(ResourceTypes.Coal, 3)], ResourceTypes.Diamond, OutputQuantity: 1));
    public static MachineType SlotMachine { get; } = new("slot-machine", "Slot Machine",
        new([new(ResourceTypes.Fruit, 3)]), new([new(ResourceTypes.Fruit, 3)], null) { OutputTable = RacingSlotTable.Table });
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
            ResourceRules.ValidateCapacity(machine.Type.Storage);
            var operation = machine.Type.Operation;
            if (operation is null || operation.Costs.IsDefaultOrEmpty || operation.OutputQuantity != 1 ||
                (operation.OutputTable is null ? !ResourceRules.ValidType(operation.OutputType) :
                    operation.OutputType is not null || operation.OutputTable.Faces.IsDefault || operation.OutputTable.Faces.Length != 6 ||
                    operation.OutputTable.Faces.Any(t => !ResourceRules.ValidType(t))) ||
                operation.Costs.Any(c => c is null || !ResourceRules.ValidType(c.Type) || c.Quantity < 1 ||
                    c.Quantity > Capacity(machine, c.Type.Id)) ||
                operation.Costs.Select(c => c.Type.Id).Distinct().Count() != operation.Costs.Length ||
                machine.StoredResources.IsDefault || machine.StoredResources.Any(r => r is null ||
                    !ResourceRules.ValidType(r.Type) || r.Quantity < 0 || r.Quantity > Capacity(machine, r.Type.Id) ||
                    !machine.Type.Storage.Capacities.Any(c => c.Type.Id == r.Type.Id)) ||
                machine.StoredResources.Select(r => r.Type.Id).Distinct().Count() != machine.StoredResources.Length)
                throw new ArgumentException("Invalid Machine Operation or stored Resources.");
        }
    }

    private static int Capacity(Machine machine, string resourceTypeId) =>
        machine.Type.Storage.Capacities.SingleOrDefault(c => c.Type.Id == resourceTypeId)?.Capacity ?? 0;

    private static bool Adjacent(Board board, Cell cell, Machine machine) =>
        Math.Max(Math.Abs(cell.X - machine.Position.X), Math.Abs(cell.Y - machine.Position.Y)) == 1 &&
        AttackRules.HasGeometricLineOfSight(board, cell, machine.Position, machine.Id);

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit actor)
    {
        var type = state.Types.Single(t => t.Id == actor.TypeId);
        var board = state.Physical.Board;
        foreach (var cell in FootprintGeometry.OccupiedCells(state, actor.Id))
        foreach (var machine in board.Machines.Where(m => (m.OwnerSide is null || m.OwnerSide == actor.SideId) && Adjacent(board, cell, m)))
        {
            var source = type.Footprint == Footprint.OneByOne ? null : cell;
            var suffix = source is null ? "" : $":{cell.X},{cell.Y}";
            var resource = state.Resources.SingleOrDefault(r => r.Position == cell);
            if (type.Ability<GiveResource>() is { } give && resource is not null &&
                machine.StoredQuantity(resource.Type.Id) < Capacity(machine, resource.Type.Id))
            {
                var ability = new ResolvedAbility(give, AbilitySourceKind.Unit, type.Id, actor.Id);
                yield return new($"{ability.CandidatePrefix}:machine:{machine.Id}{suffix}", Destination: source,
                    Action: UnitAction.GiveResource) { MachineId = machine.Id, Ability = ability };
            }
            if (type.Ability<Operate>() is { } operate && resource is null &&
                board.EdgeBetween(cell, machine.Position).Passable() &&
                machine.Type.Operation.Costs.All(c => machine.StoredQuantity(c.Type.Id) >= c.Quantity))
            {
                var ability = new ResolvedAbility(operate, AbilitySourceKind.Unit, type.Id, actor.Id);
                yield return new($"{ability.CandidatePrefix}:{machine.Id}{suffix}", Destination: source,
                    Action: UnitAction.Operate) { MachineId = machine.Id, Ability = ability };
            }
        }
    }

    internal static RulesEvent Resolve(GameState state, string actorId, Candidate action, IRandomProvider random)
    {
        if (!Candidates(state, state.Units.Single(u => u.Id == actorId)).Contains(action))
            throw new InvalidOperationException("Machine interaction is unavailable.");
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
        int? roll = operation.OutputTable is null ? null : random.RollD6();
        var output = roll is { } value ? operation.OutputTable!.Result(value) : operation.OutputType!;
        machines[index] = machine with { StoredResources = [.. machine.StoredResources.Select(r => r with
            { Quantity = r.Quantity - (operation.Costs.SingleOrDefault(c => c.Type.Id == r.Type.Id)?.Quantity ?? 0) })] };
        state.Resources.Add(new(output, cell));
        return new("MachineOperated", actorId) { MachineId = machine.Id, SourceUnitId = actorId, Cell = cell, ActionId = action.Ability!.ActionId,
            ResourceType = output, DieRoll = roll, ConsumedResources = operation.Costs };
    }
}
