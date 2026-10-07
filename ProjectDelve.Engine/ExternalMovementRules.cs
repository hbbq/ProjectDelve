namespace ProjectDelve.Engine;

// Position changes caused by another Unit are distinct from the affected Unit's Move.
// No MOV, Posture or movement capability is consulted here.
internal static class ExternalMovementRules
{
    internal static bool CanAffect(GameState state, string sourceId, string affectedId) =>
        state.Units.Any(u => u.Id == affectedId && u.CurrentHp > 0) &&
        state.Physical.Figures.Any(f => f.Id == affectedId) &&
        (sourceId == affectedId || FootprintGeometry.FootprintOf(state, affectedId) == Footprint.OneByOne);

    internal static bool CanSwap(GameState state, string sourceId, string affectedId) =>
        sourceId != affectedId && CanAffect(state, sourceId, affectedId) &&
        FootprintGeometry.FootprintOf(state, sourceId) == Footprint.OneByOne;

    internal static IEnumerable<Cell> Destinations(GameState state, string sourceId, string affectedId, int maxMove)
    {
        if (maxMove is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(maxMove));
        if (!CanAffect(state, sourceId, affectedId)) yield break;
        var figure = state.Physical.Figures.Single(f => f.Id == affectedId);
        var footprint = FootprintGeometry.FootprintOf(state, affectedId);
        yield return figure.Position;
        if (maxMove == 0) yield break;
        foreach (var destination in MovementRules.Neighbors(figure.Position))
            if (MovementRules.CanStep(state.Physical.Board, footprint, figure.Position, destination, default) &&
                SpatialRules.CanPlaceUnit(state, footprint, destination, affectedId))
                yield return destination;
    }

    // Both figures change before any resulting state is observable.
    internal static void SwapPlaces(GameState state, string firstId, string secondId)
    {
        var first = state.Physical.Figures.FindIndex(f => f.Id == firstId);
        var second = state.Physical.Figures.FindIndex(f => f.Id == secondId);
        var firstFigure = state.Physical.Figures[first];
        var secondFigure = state.Physical.Figures[second];
        state.Physical.Figures[first] = firstFigure with { Position = secondFigure.Position };
        state.Physical.Figures[second] = secondFigure with { Position = firstFigure.Position };
    }
}
