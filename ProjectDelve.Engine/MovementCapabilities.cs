using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class MovementKinds
{
    public const string Land = "land", Water = "water";
    internal static readonly ImmutableArray<string> Default = [Land];
    internal static bool Valid(ImmutableArray<string> kinds) => !kinds.IsDefault &&
        kinds.All(k => TerrainContentKey.Valid(k) && k == k.ToLowerInvariant()) &&
        kinds.Distinct(StringComparer.Ordinal).Count() == kinds.Length;
    internal static bool Intersects(ImmutableArray<string> first, ImmutableArray<string> second)
    {
        foreach (var a in first)
        foreach (var b in second)
            if (a == b) return true;
        return false;
    }
}

internal static class MovementCapabilities
{
    internal static ImmutableArray<string> For(UnitType type, IEnumerable<EquipmentDefinition>? equipment = null)
    {
        HashSet<string> kinds = new(type.BaseMovementCapabilities, StringComparer.Ordinal);
        if (type.Amphibious) kinds.Add(MovementKinds.Water);
        void Include(IEnumerable<LocalAbilityDefinition> abilities)
        {
            foreach (var ability in abilities)
                if (ability.Mechanic is Amphibious) kinds.Add(MovementKinds.Water);
                else if (ability.Mechanic is MovementCapabilityGrant grant) kinds.UnionWith(grant.Kinds);
        }
        Include(type.LocalAbilities);
        foreach (var item in equipment ?? []) Include(item.LocalAbilities);
        return [.. kinds.Order(StringComparer.Ordinal)];
    }
    internal static ImmutableArray<string> For(GameState state, Unit unit) => For(
        state.Types.Single(t => t.Id == unit.TypeId),
        unit.Inventory.Select(state.EquipmentTypeId).OfType<string>().Select(state.Equipment));
}
