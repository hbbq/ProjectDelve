namespace ProjectDelve.Engine;

// The only footprint expansion. Position is always the top-left occupied Cell.
public static class FootprintGeometry
{
    public static IReadOnlyList<Cell> OccupiedCells(Footprint footprint, Cell anchor) => footprint switch
    {
        Footprint.OneByOne => [anchor],
        Footprint.TwoByTwo => [anchor, new(anchor.X + 1, anchor.Y),
            new(anchor.X, anchor.Y + 1), new(anchor.X + 1, anchor.Y + 1)],
        _ => throw new ArgumentOutOfRangeException(nameof(footprint))
    };

    public static Footprint FootprintOf(GameState state, string unitId) =>
        state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == unitId).TypeId).Footprint;

    public static IReadOnlyList<Cell> OccupiedCells(GameState state, string unitId, Cell? anchor = null) =>
        OccupiedCells(FootprintOf(state, unitId),
            anchor ?? state.Physical.Figures.Single(f => f.Id == unitId).Position);

    public static string? UnitAtCell(GameState state, Cell cell) =>
        state.Physical.Figures.FirstOrDefault(f => OccupiedCells(state, f.Id).Contains(cell))?.Id;

    public static IReadOnlyList<Cell> PlacementCells(GameState state, string typeId, Cell anchor) =>
        OccupiedCells((UnitContent.Find(typeId, state.Types)
            ?? throw new ArgumentException("Unit Type is not defined.", nameof(typeId))).Footprint, anchor);
}

internal static class SpatialRules
{
    internal static IEnumerable<(Cell A, Cell B)> InternalEdges(Footprint footprint, Cell anchor)
    {
        var cells = FootprintGeometry.OccupiedCells(footprint, anchor);
        foreach (var a in cells)
        foreach (var b in cells)
            if (b.X == a.X + 1 && b.Y == a.Y || b.Y == a.Y + 1 && b.X == a.X)
                yield return (a, b);
    }

    internal static bool Fits(Board board, Footprint footprint, Cell anchor, MovementTraversal traversal = default) =>
        FootprintGeometry.OccupiedCells(footprint, anchor)
            .All(c => MovementRules.Inside(board, c) && traversal.CanTraverse(board.TerrainAt(c))) &&
        InternalEdges(footprint, anchor).All(e => traversal.CanTraverse(board.EdgeBetween(e.A, e.B)));

    internal static bool CanPlaceUnit(GameState state, Cell cell) => CanPlaceUnit(state, Footprint.OneByOne, cell);

    internal static bool CanPlaceUnit(GameState state, Footprint footprint, Cell anchor, string? excludedId = null) =>
        Fits(state.Physical.Board, footprint, anchor) &&
        !state.Physical.Figures.Any(f => f.Id != excludedId &&
            FootprintGeometry.OccupiedCells(state, f.Id).Intersect(FootprintGeometry.OccupiedCells(footprint, anchor)).Any());

    // Eight surrounding cells with normal geometric LOS; independent of Side and attack legality.
    internal static bool AreAdjacent(Board board, Cell first, Cell second) =>
        Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)) == 1 &&
        AttackRules.HasGeometricLineOfSight(board, first, second);

    internal static bool AreAdjacent(GameState state, string firstId, string secondId, Cell? firstAnchor = null) =>
        firstId != secondId && AreFootprintsAdjacent(state.Physical.Board,
            FootprintGeometry.OccupiedCells(state, firstId, firstAnchor), FootprintGeometry.OccupiedCells(state, secondId));

    internal static bool AreFootprintsAdjacent(Board board, IReadOnlyList<Cell> first, IReadOnlyList<Cell> second) =>
        first.Any(a => second.Any(b => AreAdjacent(board, a, b)));

    internal static int ManhattanDistance(IReadOnlyList<Cell> first, IReadOnlyList<Cell> second) =>
        first.Min(a => second.Min(b => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)));
}
