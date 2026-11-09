namespace ProjectDelve.Engine;

// Summon declarations own the type, starting Posture and optional Side limit.
public static class SummoningRules
{
    public static AdjacentUnitSummonMechanic? Placement(AbilityMechanic mechanic) => mechanic as AdjacentUnitSummonMechanic;

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit) => state.AbilitiesOf(unit.Id)
        .Where(ability => Placement(ability.Definition.Mechanic) is not null && state.CanUseAbility(unit.Id, ability))
        .SelectMany(ability => Candidates(state, unit, ability));

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit, ResolvedAbility ability)
    {
        if (Placement(ability.Definition.Mechanic) is not { } summon) yield break;
        if (summon.SameTypeSideLimit is { } limit && state.Units.Count(recipient => recipient.CurrentHp > 0 &&
                recipient.SideId == unit.SideId && recipient.TypeId == summon.UnitTypeId &&
                state.Physical.Figures.Any(figure => figure.Id == recipient.Id)) >= limit) yield break;
        if (state.Types.SingleOrDefault(type => type.Id == summon.UnitTypeId) is not { } summonedType ||
            !state.CanPlaceUnitType(summonedType, unit.SideId)) yield break;
        for (var y = 0; y < state.Physical.Board.Height; y++)
        for (var x = 0; x < state.Physical.Board.Width; x++)
        {
            var cell = new Cell(x, y);
            var footprint = FootprintGeometry.OccupiedCells(summonedType.Footprint, cell);
            if (SpatialRules.CanPlaceUnit(state, summonedType, cell) &&
                footprint.All(c => TerrainCatalog.Accepts(summonedType, state.Physical.Board.TerrainDefinitionAt(c))) &&
                SpatialRules.AreFootprintsAdjacent(state.Physical.Board, FootprintGeometry.OccupiedCells(state, unit.Id), footprint))
                yield return new($"{ability.CandidatePrefix}:{x},{y}",
                    Destination: cell, Action: UnitAction.AdjacentUnitSummon) { Ability = ability };
        }
    }

}
