namespace ProjectDelve.Engine;

internal static class BananaPlacementRules
{
    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit, PlaceBananaPeel ability)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var source = new ResolvedAbility(type.Ability<PlaceBananaPeel>()!, AbilitySourceKind.Unit, type.Id, unit.Id);
        if (state.StoredQuantity(unit.SideId, unit.TypeId, ResourceTypes.Banana.Id) < ability.BananaCost) yield break;
        var board = state.Physical.Board;
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (board.TerrainDefinitionAt(cell).Passable && !board.Machines.Any(m => m.Position == cell) &&
                FootprintGeometry.UnitAtCell(state, cell) is null && !state.CellMarkers.Any(m => m.Position == cell) &&
                FootprintGeometry.OccupiedCells(state, unit.Id).Any(c => SpatialRules.AreAdjacent(board, c, cell)))
                yield return new($"{source.CandidatePrefix}:{x},{y}", Destination: cell, Action: UnitAction.PlaceBananaPeel,
                    Kind: ActivationChoiceKind.BonusAction) { Ability = source };
        }
    }

    internal static void Resolve(GameState state, string unitId, Candidate action, Action<RulesEvent> emit)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var ability = (PlaceBananaPeel)action.Ability!.Definition.Mechanic;
        if (!Candidates(state, unit, ability).Any(c => c.Destination == action.Destination)) throw new InvalidOperationException("Banana Peel placement is unavailable.");
        var cost = ability.BananaCost;
        var index = state.StoredResources.FindIndex(r => r.SideId == unit.SideId && r.UnitTypeId == unit.TypeId &&
            r.Type.Id == ResourceTypes.Banana.Id);
        var stored = state.StoredResources[index];
        state.StoredResources[index] = stored with { Quantity = stored.Quantity - cost };
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, action.Destination!);
        emit(new RulesEvent("ResourceSpent", unitId, TypeId: unit.TypeId)
        {
            SourceUnitId = unitId, SideId = unit.SideId, ActionId = action.Ability.ActionId,
            ResourceType = stored.Type, StoredQuantity = stored.Quantity - cost
        });
        emit(new RulesEvent("CellMarkerPlaced", unitId)
            { Cell = action.Destination, CellMarkerType = CellMarkerTypes.BananaPeel, ActionId = action.Ability.ActionId });
    }

    internal static string? Preferred(DecisionRequest request, IGameplayQueries queries)
    {
        if (!queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferBananaPeel)) return null;
        var candidates = request.Candidates.Where(c => c.Action == UnitAction.PlaceBananaPeel).ToList();
        if (candidates.Count == 0) return null;
        var asphalt = candidates.Where(c => queries.TerrainAt(c.Destination!) == TerrainKind.Asphalt).ToList();
        if (asphalt.Count > 0) candidates = asphalt;
        var route = queries.EffectiveOrderFor(request.UnitId!).Route;
        var definition = route is null ? null : queries.RouteDefinition(route.RouteId);
        var next = route is null ? null : RouteRules.Next(route, definition!);
        var closedDoors = queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        var ranked = candidates.Select(c => new { Candidate = c,
            Distance = next is null ? null : queries.RouteDistanceToArea(request.UnitId!, c.Destination!, queries.WaypointArea(definition!.WaypointIds[next.CurrentIndex]), closedDoors) }).ToList();
        // Unreachable targets do not acquire an artificial preference over comparable routes.
        var compare = ranked.All(x => x.Distance.HasValue);
        return ranked.OrderByDescending(x => compare ? x.Distance : null)
            .ThenBy(x => x.Candidate.Destination!.Y).ThenBy(x => x.Candidate.Destination!.X)
            .ThenBy(x => x.Candidate.Key, StringComparer.Ordinal).First().Candidate.Key;
    }
}
