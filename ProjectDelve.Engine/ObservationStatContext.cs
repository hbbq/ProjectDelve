namespace ProjectDelve.Engine;

// One observation's derived work only. Never retained by authoritative state or
// reused after mutation; gameplay queries keep deriving from their own state.
internal sealed class ObservationStatContext
{
    private readonly GameState state;
    private readonly Dictionary<string, (Unit Source, AbilityMechanic[] Mechanics)> providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<Stat, int>> modifiers = new(StringComparer.Ordinal);

    internal ObservationStatContext(GameState state)
    {
        this.state = state;
        var placed = state.Physical.Figures.Where(f => f.Posture == Posture.Upright)
            .Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var types = state.Types.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var equipment = state.EquipmentTypes.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var unit in state.Units.Where(u => u.CurrentHp > 0 && placed.Contains(u.Id)))
        {
            var mechanics = types[unit.TypeId].LocalAbilities
                .Concat(unit.Inventory.Select(state.EquipmentTypeId).OfType<string>()
                    .SelectMany(id => equipment[id].LocalAbilities))
                .Where(a => a.Timing == AbilityTiming.Passive &&
                    a.Mechanic is SelfModifiersMechanic or AdjacentUnitsStatModifiersMechanic)
                .Select(a => a.Mechanic).ToArray();
            if (mechanics.Length > 0) providers.Add(unit.Id, (unit, mechanics));
        }
    }

    internal int Modifier(string unitId, Stat stat)
    {
        if (providers.Count == 0) return 0;
        if (!modifiers.TryGetValue(unitId, out var totals))
        {
            totals = new();
            var target = state.Units.Single(u => u.Id == unitId);
            if (target.CurrentHp > 0 && state.Physical.Figures.Any(f => f.Id == unitId))
                foreach (var (source, mechanics) in providers.Values)
                {
                    bool? adjacent = null;
                    foreach (var mechanic in mechanics)
                    {
                        var applicable = mechanic switch
                        {
                            SelfModifiersMechanic self when source.Id == unitId => self.Modifiers,
                            AdjacentUnitsStatModifiersMechanic aura when
                                AbilityQueries.MatchesRelationship(state, source, target, aura.Selector) &&
                                (adjacent ??= SpatialRules.AreAdjacent(state, source.Id, unitId)) == true => aura.Modifiers,
                            _ => []
                        };
                        // Match AbilityModifier's checked sum within each mechanic,
                        // followed by ordinary accumulation between mechanics.
                        foreach (var group in applicable.GroupBy(modifier => modifier.Stat))
                            totals[group.Key] = totals.GetValueOrDefault(group.Key) + group.Sum(modifier => modifier.Amount);
                    }
                }
            modifiers.Add(unitId, totals);
        }
        return totals.GetValueOrDefault(stat);
    }

    internal (int Mov, int Rng, int Atk, int Def, int SightDistance) Values(string unitId) =>
        (state.EffectiveMovOf(unitId, this), state.EffectiveRngOf(unitId, this),
            state.EffectiveAtkOf(unitId, this), state.EffectiveDefOf(unitId, this), state.EffectiveSightDistanceOf(unitId, this));
}
