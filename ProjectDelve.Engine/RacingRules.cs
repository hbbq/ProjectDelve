using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// A physical d6 table. Machine operations can author any six Resource faces.
public sealed record ResourceOutputTable(ImmutableArray<ResourceType> Faces)
{
    public ResourceType Result(int roll) => roll is >= 1 and <= 6 && Faces.Length == 6
        ? Faces[roll - 1] : throw new ArgumentOutOfRangeException(nameof(roll));
}
public static class RacingSlotTable
{
    public static ResourceOutputTable Table { get; } = new([
        ResourceTypes.Banana, ResourceTypes.Banana, ResourceTypes.Banana,
        ResourceTypes.Mushroom, ResourceTypes.Mushroom, ResourceTypes.Shell]);
}

internal static class RacingRules
{
    internal static void Spend(GameState state, string unitId, ResourceType resource, int cost, string actionId, Action<RulesEvent> emit)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var index = state.StoredResources.FindIndex(r => r.SideId == unit.SideId && r.UnitTypeId == unit.TypeId && r.Type.Id == resource.Id);
        if (index < 0 || state.StoredResources[index].Quantity < cost) throw new InvalidOperationException("Insufficient stored Resources.");
        var stored = state.StoredResources[index] with { Quantity = state.StoredResources[index].Quantity - cost };
        state.StoredResources[index] = stored;
        emit(new RulesEvent("ResourceSpent", unitId, TypeId: unit.TypeId) { SideId = unit.SideId, SourceUnitId = unitId,
            ActionId = actionId, ResourceType = resource, StoredQuantity = stored.Quantity });
    }

    internal static void AwardWaypoint(GameState state, string unitId, IRandomProvider random, Action<RulesEvent> emit)
    {
        if (!state.WaypointAdvancedThisActivation || state.ActiveToken is not { } token ||
            state.Types.Single(t => t.Id == token.TypeId).Ability<WaypointSlotMachine>() is not { } declaration) return;
        var ability = new ResolvedAbility(declaration, AbilitySourceKind.Unit, token.TypeId, unitId);
        var roll = random.RollD6();
        var resource = ((WaypointSlotMachine)declaration.Mechanic).OutputTable.Result(roll);
        var capacity = state.Types.Single(t => t.Id == token.TypeId).ResourceStorage?.Capacities.SingleOrDefault(c => c.Type.Id == resource.Id)?.Capacity ?? 0;
        var quantity = state.StoredQuantity(token.SideId, token.TypeId, resource.Id);
        emit(new RulesEvent("RacingSlotRolled", unitId, TypeId: token.TypeId, DieRoll: roll)
            { SideId = token.SideId, ActionId = ability.ActionId, ResourceType = resource, Succeeded = quantity < capacity });
        if (quantity >= capacity) return;
        var index = state.StoredResources.FindIndex(r => r.SideId == token.SideId && r.UnitTypeId == token.TypeId && r.Type.Id == resource.Id);
        var stored = new StoredResource(token.SideId, token.TypeId, resource, quantity + 1);
        if (index < 0) state.StoredResources.Add(stored); else state.StoredResources[index] = stored;
        emit(new RulesEvent("ResourceStored", unitId, unitId, token.TypeId) { SideId = token.SideId,
            ActionId = ability.ActionId, ResourceType = resource, StoredQuantity = quantity + 1 });
    }
}
