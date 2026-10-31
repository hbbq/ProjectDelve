namespace ProjectDelve.Engine;

// Resource Types are content, independent of Side and Unit occupancy.
public sealed record ResourceType(string Id, string Name);
public static class ResourceTypes
{
    public static ResourceType Diamond { get; } = new("diamond", "Diamond");
    public static ResourceType Coal { get; } = new("coal", "Coal");
    public static ResourceType Fruit { get; } = new("fruit", "Fruit");
    public static ResourceType Banana { get; } = new("banana", "Banana");
    public static ResourceType Mushroom { get; } = new("mushroom", "Mushroom");
    public static ResourceType Shell { get; } = new("shell", "Shell");
    public static System.Collections.Immutable.ImmutableArray<ResourceType> All { get; } = [Coal, Diamond, Fruit, Banana, Mushroom, Shell];
    public static ResourceType Find(string id) => All.SingleOrDefault(type => type.Id == id)
        ?? throw new ArgumentException($"Unknown Resource Type ID '{id}'.");
}
public sealed record Resource(ResourceType Type, Cell Position);

public sealed record ResourceStorageCapacity(ResourceType Type, int Capacity);
public sealed record ResourceStorage(System.Collections.Immutable.ImmutableArray<ResourceStorageCapacity> Capacities)
{
    public bool Equals(ResourceStorage? other) => other is not null &&
        (Capacities.IsDefault || other.Capacities.IsDefault
            ? Capacities.IsDefault == other.Capacities.IsDefault
            : Capacities.SequenceEqual(other.Capacities));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (!Capacities.IsDefault) foreach (var capacity in Capacities) hash.Add(capacity);
        return hash.ToHashCode();
    }
}
// Sparse quantities on the Side + Unit Type card, independent of individual figures.
public sealed record StoredResource(string SideId, string UnitTypeId, ResourceType Type, int Quantity);

internal static class ResourceRules
{
    internal static bool CanCreateAt(GameState state, Cell cell) =>
        MovementRules.Inside(state.Physical.Board, cell) && state.Physical.Board.TerrainDefinitionAt(cell).Passable &&
        state.Physical.Board.TerrainDefinitionAt(cell).AllowsResourceMarkers &&
        !state.Physical.Board.Machines.Any(m => m.Position == cell) &&
        !state.Resources.Any(r => r.Position == cell);

    internal static bool ValidType(ResourceType? type) => type is not null &&
        !string.IsNullOrWhiteSpace(type.Id) && !string.IsNullOrWhiteSpace(type.Name);

    internal static void ValidateCapacity(ResourceStorage storage)
    {
        if (storage is null || storage.Capacities.IsDefaultOrEmpty ||
            storage.Capacities.Any(c => c is null || !ValidType(c.Type) || c.Capacity < 0) ||
            storage.Capacities.Select(c => c.Type.Id).Distinct().Count() != storage.Capacities.Length)
            throw new ArgumentException("Resource Storage requires unique Resource Types and nonnegative capacities.");
    }

    internal static void ValidateStorage(GameState state)
    {
        foreach (var type in state.Types)
            if (type.ResourceStorage is { } storage) ValidateCapacity(storage);
        if (state.StoredResources.Any(r => r is null || !ValidType(r.Type) || r.Quantity < 0 ||
                !state.Controllers.Any(c => c.Token.SideId == r.SideId && c.Token.TypeId == r.UnitTypeId) ||
                r.Quantity > (Capacity(state, r.UnitTypeId, r.Type.Id) ?? -1)) ||
            state.StoredResources.GroupBy(r => (r.SideId, r.UnitTypeId, r.Type.Id)).Any(g => g.Count() > 1))
            throw new ArgumentException("Stored Resources must belong to a declared group and fit its Resource Storage.");
    }

    private static int? Capacity(GameState state, string typeId, string resourceTypeId) =>
        state.Types.SingleOrDefault(t => t.Id == typeId)?.ResourceStorage?.Capacities
            .SingleOrDefault(c => c.Type.Id == resourceTypeId)?.Capacity;

    internal static bool CanReceive(GameState state, Unit recipient, Resource resource) =>
        state.StoredQuantity(recipient.SideId, recipient.TypeId, resource.Type.Id) <
            (Capacity(state, recipient.TypeId, resource.Type.Id) ?? 0);

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit actor)
    {
        var type = state.Types.Single(t => t.Id == actor.TypeId);
        foreach (var cell in FootprintGeometry.OccupiedCells(state, actor.Id))
        {
            var resource = state.Resources.SingleOrDefault(r => r.Position == cell);
            if (resource is null) continue;
            // One-Cell content retains its simple keys; larger figures choose an occupied source Cell.
            var sourceCell = type.Footprint == Footprint.OneByOne ? null : cell;
            var suffix = sourceCell is null ? "" : $":{cell.X},{cell.Y}";
            if (type.Ability<PickupResource>() is { } pickup && CanReceive(state, actor, resource))
            {
                var ability = new ResolvedAbility(pickup, AbilitySourceKind.Unit, type.Id, actor.Id);
                yield return new($"{ability.CandidatePrefix}{suffix}", Destination: sourceCell, Action: UnitAction.PickupResource) { Ability = ability };
            }
            if (type.Ability<GiveResource>() is { } give)
            {
                var ability = new ResolvedAbility(give, AbilitySourceKind.Unit, type.Id, actor.Id);
                foreach (var recipient in state.Units.Where(u => u.Id != actor.Id && u.SideId == actor.SideId &&
                    u.CurrentHp > 0 && SpatialRules.AreAdjacent(state, actor.Id, u.Id) && CanReceive(state, u, resource)))
                    yield return new($"{ability.CandidatePrefix}:{recipient.Id}{suffix}", Destination: sourceCell,
                        Action: UnitAction.GiveResource, TargetId: recipient.Id) { Ability = ability };
            }
        }
    }

    internal static RulesEvent Transfer(GameState state, string actorId, Candidate action)
    {
        if (!Candidates(state, state.Units.Single(u => u.Id == actorId)).Any(c => c == action))
            throw new InvalidOperationException("Resource transfer is unavailable.");
        var cell = action.Destination ?? state.Physical.Figures.Single(f => f.Id == actorId).Position;
        var resource = state.Resources.Single(r => r.Position == cell);
        var recipient = state.Units.Single(u => u.Id == (action.TargetId ?? actorId));
        var quantity = state.StoredQuantity(recipient.SideId, recipient.TypeId, resource.Type.Id) + 1;
        var index = state.StoredResources.FindIndex(r => r.SideId == recipient.SideId &&
            r.UnitTypeId == recipient.TypeId && r.Type.Id == resource.Type.Id);
        var stored = new StoredResource(recipient.SideId, recipient.TypeId, resource.Type, quantity);
        state.Resources.Remove(resource);
        if (index < 0) state.StoredResources.Add(stored); else state.StoredResources[index] = stored;
        return new("ResourceStored", actorId, recipient.Id, recipient.TypeId)
        {
            SourceUnitId = actorId, SideId = recipient.SideId, Cell = cell,
            ActionId = ContentDescriptions.EntryId(action), ResourceType = resource.Type, StoredQuantity = quantity
        };
    }

    internal static void Validate(Resource resource, Board board)
    {
        if (resource is null || resource.Type is null || string.IsNullOrWhiteSpace(resource.Type.Id) ||
            string.IsNullOrWhiteSpace(resource.Type.Name) || resource.Position is null ||
            !MovementRules.Inside(board, resource.Position) || !board.TerrainDefinitionAt(resource.Position).AllowsResourceMarkers)
            throw new ArgumentException("A Resource requires a Resource Type and a Cell permitting Resource markers.");
    }

    internal static void TransportDuringOwnMove(GameState state, string unitId, Cell from, Cell to)
    {
        var type = state.Types.Single(type => type.Id == state.Units.Single(unit => unit.Id == unitId).TypeId);
        // Larger-footprint transport has no established rules.
        if (from == to || type.Mechanic<TransportResource>() is null || type.Footprint != Footprint.OneByOne ||
            state.Resources.Any(resource => resource.Position == to)) return;
        var index = state.Resources.FindIndex(resource => resource.Position == from);
        if (index >= 0) state.Resources[index] = state.Resources[index] with { Position = to };
    }
}
