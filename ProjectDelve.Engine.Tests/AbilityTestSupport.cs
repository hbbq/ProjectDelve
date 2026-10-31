namespace ProjectDelve.Engine.Tests;

internal static class AbilityTestSupport
{
    internal static UnitType WithLocal(this UnitType type, LocalAbilityDefinition ability) =>
        type with { LocalAbilities = type.LocalAbilities.Add(ability) };
    internal static UnitType Without<T>(this UnitType type) where T : AbilityMechanic =>
        type with { LocalAbilities = [.. type.LocalAbilities.Where(a => a.Mechanic is not T)] };
    // Test fixtures state their expected authored maximum explicitly; the
    // production counter records only uses consumed under the local source key.
    internal static AbilityUses Uses(this Unit unit, string key, int maxUses = 2) =>
        new(maxUses, maxUses - unit.AbilityUseCounts.GetValueOrDefault($"unit:{Uri.EscapeDataString(key)}"));
}
