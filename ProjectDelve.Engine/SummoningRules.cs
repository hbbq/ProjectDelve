namespace ProjectDelve.Engine;

// Summon declarations own the type, starting Posture and optional population limits.
public static class SummoningRules
{
    public static AdjacentUnitSummonMechanic? Placement(AbilityMechanic mechanic) => mechanic as AdjacentUnitSummonMechanic;

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit)
    {
        var lookup = new PlacementLookup(state);
        foreach (var ability in state.AbilitiesOf(unit.Id)
                     .Where(ability => Placement(ability.Definition.Mechanic) is not null && state.CanUseAbility(unit.Id, ability)))
        foreach (var candidate in Candidates(state, unit, ability, lookup))
            yield return candidate;
    }

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit, ResolvedAbility ability)
    {
        foreach (var candidate in Candidates(state, unit, ability, new PlacementLookup(state)))
            yield return candidate;
    }

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit, ResolvedAbility ability, PlacementLookup lookup)
    {
        if (Placement(ability.Definition.Mechanic) is not { } summon) yield break;
        if (summon.SameTypeSideLimit is { } limit && state.Units.Count(recipient => recipient.CurrentHp > 0 &&
                recipient.SideId == unit.SideId && recipient.TypeId == summon.UnitTypeId &&
                state.Physical.Figures.Any(figure => figure.Id == recipient.Id)) >= limit) yield break;
        if (summon.SameTypeSummonerLimit is { } summonerLimit && state.Units.Count(recipient => recipient.CurrentHp > 0 &&
                recipient.SummonerUnitId == unit.Id && recipient.TypeId == summon.UnitTypeId &&
                state.Physical.Figures.Any(figure => figure.Id == recipient.Id)) >= summonerLimit) yield break;
        if (state.Types.SingleOrDefault(type => type.Id == summon.UnitTypeId) is not { } summonedType ||
            !state.CanPlaceUnitType(summonedType, unit.SideId)) yield break;
        var board = state.Physical.Board;
        var placement = MovementTraversal.OccupancyFor(summonedType);
        var minX = 0;
        var minY = 0;
        var maxX = board.Width - 1;
        var maxY = board.Height - 1;
        IReadOnlyList<Cell>? sourceCells = null;
        // Preserve legacy lazy validation for malformed source references.
        var sources = state.Units.Where(u => u.Id == unit.Id).ToArray();
        var figures = state.Physical.Figures.Where(f => f.Id == unit.Id).ToArray();
        var sourceTypes = sources.Length == 1 ? state.Types.Where(t => t.Id == sources[0].TypeId).ToArray() : [];
        if (sources.Length == 1 && figures.Length == 1 && sourceTypes.Length == 1 &&
            Enum.IsDefined(sourceTypes[0].Footprint) && Enum.IsDefined(summonedType.Footprint) &&
            board.Width > 0 && board.Height > 0 && lookup.IsIndexed)
        {
            sourceCells = FootprintGeometry.OccupiedCells(sourceTypes[0].Footprint, figures[0].Position);
            var offsets = FootprintGeometry.OccupiedCells(summonedType.Footprint, new(0, 0));
            // Some summoned occupied Cell must be within one Cell of some source Cell.
            // Include the whole superset, then retain the existing authoritative predicates.
            minX = Math.Max(0, sourceCells.Min(c => c.X) - 1 - offsets.Max(c => c.X));
            minY = Math.Max(0, sourceCells.Min(c => c.Y) - 1 - offsets.Max(c => c.Y));
            maxX = Math.Min(maxX, sourceCells.Max(c => c.X) + 1);
            maxY = Math.Min(maxY, sourceCells.Max(c => c.Y) + 1);
        }
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var cell = new Cell(x, y);
            var footprint = FootprintGeometry.OccupiedCells(summonedType.Footprint, cell);
            if (SpatialRules.CanPlaceUnit(state, summonedType.Footprint, cell, placement: placement, lookup: lookup) &&
                footprint.All(c => TerrainCatalog.Accepts(summonedType, state.Physical.Board.TerrainDefinitionAt(c))) &&
                SpatialRules.AreFootprintsAdjacent(board, sourceCells ?? FootprintGeometry.OccupiedCells(state, unit.Id), footprint))
                yield return new($"{ability.CandidatePrefix}:{x},{y}",
                    Destination: cell, Action: UnitAction.AdjacentUnitSummon) { Ability = ability };
        }
    }

}
