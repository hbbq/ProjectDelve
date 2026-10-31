namespace ProjectDelve.Engine;

internal static class PassiveAbilities
{
    // Continuous effects follow the current bearer and share ordinary Posture rules.
    // Items have no defined passive lifetime and are excluded by content validation.
    internal static IEnumerable<T> ActivePassives<T>(this GameState state, string unitId) where T : AbilityMechanic =>
        state.IsUpright(unitId) && state.Units.Single(unit => unit.Id == unitId).CurrentHp > 0
            ? state.AbilitiesOf(unitId).Where(ability => ability.Definition.Timing == AbilityTiming.Passive)
                .Select(ability => ability.Definition.Mechanic).OfType<T>()
            : [];
}
