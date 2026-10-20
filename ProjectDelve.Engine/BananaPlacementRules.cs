namespace ProjectDelve.Engine;

internal static class BananaPlacementRules
{
    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        if (!type.Actions.HasFlag(UnitAction.PlaceBananaPeel) || type.PlaceBananaPeel is not { } ability ||
            state.StoredQuantity(unit.SideId, unit.TypeId, ResourceTypes.Diamond.Id) < ability.DiamondCost) yield break;
        var board = state.Physical.Board;
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (board.TerrainAt(cell).Passable() && !board.Machines.Any(m => m.Position == cell) &&
                FootprintGeometry.UnitAtCell(state, cell) is null && !state.CellMarkers.Any(m => m.Position == cell) &&
                FootprintGeometry.OccupiedCells(state, unit.Id).Any(c => SpatialRules.AreAdjacent(board, c, cell)))
                yield return new($"place-banana-peel:{x},{y}", Destination: cell, Action: UnitAction.PlaceBananaPeel);
        }
    }

    internal static void Resolve(GameState state, string unitId, Candidate action, Action<RulesEvent> emit)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        if (!Candidates(state, unit).Contains(action)) throw new InvalidOperationException("Banana Peel placement is unavailable.");
        var cost = state.Types.Single(t => t.Id == unit.TypeId).PlaceBananaPeel!.DiamondCost;
        var index = state.StoredResources.FindIndex(r => r.SideId == unit.SideId && r.UnitTypeId == unit.TypeId &&
            r.Type.Id == ResourceTypes.Diamond.Id);
        var stored = state.StoredResources[index];
        state.StoredResources[index] = stored with { Quantity = stored.Quantity - cost };
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, action.Destination!);
        emit(new RulesEvent("ResourceSpent", unitId, TypeId: unit.TypeId)
        {
            SourceUnitId = unitId, SideId = unit.SideId, ActionId = "place-banana-peel",
            ResourceType = stored.Type, StoredQuantity = stored.Quantity - cost
        });
        emit(new RulesEvent("CellMarkerPlaced", unitId)
            { Cell = action.Destination, CellMarkerType = CellMarkerTypes.BananaPeel, ActionId = "place-banana-peel" });
    }

    internal static string? Preferred(DecisionRequest request, IGameplayQueries queries)
    {
        if (!queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferBananaPeel)) return null;
        var candidates = request.Candidates.Where(c => c.Action == UnitAction.PlaceBananaPeel).ToList();
        if (candidates.Count == 0) return null;
        var asphalt = candidates.Where(c => queries.TerrainAt(c.Destination!) == TerrainKind.Asphalt).ToList();
        if (asphalt.Count > 0) candidates = asphalt;
        var route = queries.EffectiveOrderFor(request.UnitId!).Route;
        var next = route is null ? null : RouteRules.Next(route);
        var closedDoors = queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        var ranked = candidates.Select(c => new { Candidate = c,
            Distance = next is null ? null : queries.RouteDistanceToArea(request.UnitId!, c.Destination!, next.Waypoints[next.CurrentIndex], closedDoors) }).ToList();
        // Unreachable targets do not acquire an artificial preference over comparable routes.
        var compare = ranked.All(x => x.Distance.HasValue);
        return ranked.OrderByDescending(x => compare ? x.Distance : null)
            .ThenBy(x => x.Candidate.Destination!.Y).ThenBy(x => x.Candidate.Destination!.X)
            .ThenBy(x => x.Candidate.Key, StringComparer.Ordinal).First().Candidate.Key;
    }
}
