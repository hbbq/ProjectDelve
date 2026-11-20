using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record Storage(string StorageId, string Name, string? SideId = null, ImmutableArray<OwnedObject> Contents = default);

public static class StorageRules
{
    public const string RepossessedEquipmentId = "authority-repossessed-equipment";
    public const string RepossessedEquipmentName = "Repossessed Equipment Storage";
    private static readonly Storage Authority = new(RepossessedEquipmentId, RepossessedEquipmentName, null, []);
    internal static List<Storage> Defaults() => [Authority];

    internal static List<Storage> Create(IEnumerable<Storage>? declarations = null)
    {
        List<Storage> storages = [.. declarations ?? []];
        if (!storages.Any(s => s?.StorageId == RepossessedEquipmentId)) storages.Add(Authority);
        ValidateStructure(storages);
        return storages;
    }

    internal static void ValidateStructure(List<Storage> storages)
    {
        if (storages is null || storages.Any(s => s is null || string.IsNullOrWhiteSpace(s.StorageId) ||
                string.IsNullOrWhiteSpace(s.Name) || s.Contents.IsDefault || s.Contents.Any(obj => obj is null)) ||
            storages.Select(s => s.StorageId).Distinct(StringComparer.Ordinal).Count() != storages.Count)
            throw new ArgumentException("Storages require distinct IDs, names and physical contents.");
        if (storages.SingleOrDefault(s => s.StorageId == RepossessedEquipmentId) is not { SideId: null } authority ||
            authority.Name != Authority.Name)
            throw new ArgumentException("Repossessed Equipment Storage requires its global identity and name.");
    }

    // Callers compose these moves on the Engine's resolution copy. Resolve every
    // reference and calculate the resulting inventory before publishing either location.
    internal static void Deposit(GameState state, Unit owner, string objectId, string storageId) =>
        Transfer(state, owner, objectId, storageId, deposit: true);

    internal static void Withdraw(GameState state, Unit recipient, string objectId, string storageId) =>
        Transfer(state, recipient, objectId, storageId, deposit: false);

    internal static bool CanRemove(GameState state, Unit owner, OwnedObject obj) =>
        owner.Inventory.Contains(obj) && CanOccupy(state, owner with { Inventory = owner.Inventory.Remove(obj) });

    private static bool CanOccupy(GameState state, Unit changed)
    {
        var position = state.Physical.Figures.Single(f => f.Id == changed.Id).Position;
        var traversal = new MovementTraversal(Capabilities: MovementCapabilities.For(state, changed));
        return SpatialRules.CanPlaceUnit(state, state.UnitConfiguration(changed).Footprint, position, changed.Id, traversal);
    }

    private static void Transfer(GameState state, Unit owner, string objectId, string storageId, bool deposit)
    {
        int storageIndex = state.Storages.FindIndex(s => s.StorageId == storageId);
        int unitIndex = state.Units.FindIndex(u => u.Id == owner.Id && u.CurrentHp > 0);
        if (storageIndex < 0 || unitIndex < 0)
            throw new ArgumentException("Storage transfer requires an existing Storage and living Unit.");
        var storage = state.Storages[storageIndex];
        owner = state.Units[unitIndex];
        var origin = deposit ? owner.Inventory : storage.Contents;
        var obj = origin.SingleOrDefault(o => o.Id == objectId)
            ?? throw new ArgumentException($"Physical object '{objectId}' is absent from its source.");
        if (!deposit && !LootRules.CanOwn(state, owner, obj))
            throw new ArgumentException($"Unit '{owner.Id}' cannot receive '{objectId}'.");
        var changed = state.WithInventory(owner, deposit ? owner.Inventory.Remove(obj) : owner.Inventory.Add(obj));
        if (!CanOccupy(state, changed))
            throw new ArgumentException($"The proposed inventory does not permit Unit '{owner.Id}' to occupy its Cell.");
        var contents = deposit ? storage.Contents.Add(obj) : storage.Contents.Remove(obj);
        state.Storages[storageIndex] = storage with { Contents = contents };
        state.Units[unitIndex] = changed;
    }
}
