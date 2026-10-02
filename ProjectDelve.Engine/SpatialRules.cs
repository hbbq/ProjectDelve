namespace ProjectDelve.Engine;

internal static class SpatialRules
{
    // Eight surrounding cells with normal geometric LOS; independent of Side and attack legality.
    internal static bool AreAdjacent(Board board, Cell first, Cell second) =>
        Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)) == 1 &&
        AttackRules.HasGeometricLineOfSight(board, first, second);
}
