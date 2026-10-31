using ProjectDelve.Engine;

namespace ProjectDelve.Web.Tests;

internal static class WebAbilityTestQueries
{
    internal static ResolvedAbility Ability(GameState state, string unitId, string key) =>
        state.AbilitiesOf(unitId).Single(ability => ability.SourceKind == AbilitySourceKind.Unit && ability.Key == key);

    internal static AbilityUses? Uses(GameState state, string unitId, string key) =>
        state.AbilityUsesOf(unitId, Ability(state, unitId, key));
}
