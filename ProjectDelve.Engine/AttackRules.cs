namespace ProjectDelve.Engine;

// Undefined LOS is not a rule that the encountered feature blocks LOS.
internal enum NormalAttackEvaluation { NotPossible, Possible, UndefinedLineOfSight }

internal static class AttackRules
{
    // This evaluates a normal attack in the current world, independently of phase.
    // Only the attacker is relocated; its original cell is vacated.
    internal static NormalAttackEvaluation EvaluateFrom(
        GameState state, string attackerId, Cell from, string targetId)
    {
        HypotheticalPosition.Validate(state, attackerId, from);
        var attacker = state.Units.Single(u => u.Id == attackerId);
        var target = state.Units.Single(u => u.Id == targetId);
        var stats = state.Types.Single(t => t.Id == attacker.TypeId);
        if (stats.Rng == 0 || stats.Atk == 0 || target.CurrentHp == 0 || target.SideId == attacker.SideId)
            return NormalAttackEvaluation.NotPossible;

        var to = state.Physical.Figures.Single(f => f.Id == target.Id).Position;
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        if (stats.Rng == 1 ? Math.Max(dx, dy) != 1 : dx + dy > stats.Rng)
            return NormalAttackEvaluation.NotPossible;

        // Preserve target-local uncertainty: unrelated unresolved LOS does not
        // remove otherwise legal choices. Friendly figures do not block LOS,
        // including the attacker whose actual figure is at its original position.
        if (HasUnsupportedFeaturedEdgeLos(state.Physical.Board, from, to) ||
            state.Units.Where(u => u.CurrentHp > 0 && u.SideId != attacker.SideId && u.Id != target.Id)
                .Any(u => CrossesInterior(from, to, state.Physical.Figures.Single(f => f.Id == u.Id).Position)))
            return NormalAttackEvaluation.UndefinedLineOfSight;

        return NormalAttackEvaluation.Possible;
    }

    private static bool HasUnsupportedFeaturedEdgeLos(Board board, Cell from, Cell to)
    {
        if (board.Edges.Any(edge => CrossesEdgeInterior(from, to, edge)))
            return true;

        // At an exact corner there are two possible passages. The defined
        // corner rule makes LOS unambiguous when either passage has no featured
        // edge; otherwise the effects of the encountered features are unknown.
        var minX = Math.Min(from.X, to.X);
        var maxX = Math.Max(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxY = Math.Max(from.Y, to.Y);
        for (var x = minX; x < maxX; x++)
        for (var y = minY; y < maxY; y++)
        {
            var cornerX = 2 * x + 1;
            var cornerY = 2 * y + 1;
            if ((cornerX - 2 * from.X) * (to.Y - from.Y) !=
                (cornerY - 2 * from.Y) * (to.X - from.X))
                continue;

            var stepX = Math.Sign(to.X - from.X);
            var stepY = Math.Sign(to.Y - from.Y);
            var before = new Cell(x + (stepX < 0 ? 1 : 0), y + (stepY < 0 ? 1 : 0));
            var after = new Cell(x + (stepX > 0 ? 1 : 0), y + (stepY > 0 ? 1 : 0));
            var horizontalFirst = new Cell(after.X, before.Y);
            var verticalFirst = new Cell(before.X, after.Y);
            if (!PassageHasFeaturedEdge(board, before, horizontalFirst, after) ||
                !PassageHasFeaturedEdge(board, before, verticalFirst, after))
                continue;
            return true;
        }
        return false;
    }

    private static bool PassageHasFeaturedEdge(Board board, Cell first, Cell middle, Cell last) =>
        HasFeaturedEdge(board, first, middle) || HasFeaturedEdge(board, middle, last);

    private static bool HasFeaturedEdge(Board board, Cell a, Cell b) =>
        board.Edges.Any(edge => edge.A == a && edge.B == b || edge.A == b && edge.B == a);

    private static bool CrossesEdgeInterior(Cell from, Cell to, Edge edge)
    {
        var fromX = 2 * from.X;
        var fromY = 2 * from.Y;
        var dx = 2 * (to.X - from.X);
        var dy = 2 * (to.Y - from.Y);
        if (edge.A.Y == edge.B.Y)
        {
            if (dx == 0) return false;
            var denominator = Math.Abs(dx);
            var numerator = Math.Sign(dx) * (edge.A.X + edge.B.X - fromX);
            var scaledY = fromY * denominator + dy * numerator;
            return numerator > 0 && numerator < denominator &&
                (2 * edge.A.Y - 1) * denominator < scaledY &&
                scaledY < (2 * edge.A.Y + 1) * denominator;
        }

        if (dy == 0) return false;
        var horizontalDenominator = Math.Abs(dy);
        var horizontalNumerator = Math.Sign(dy) * (edge.A.Y + edge.B.Y - fromY);
        var scaledX = fromX * horizontalDenominator + dx * horizontalNumerator;
        return horizontalNumerator > 0 && horizontalNumerator < horizontalDenominator &&
            (2 * edge.A.X - 1) * horizontalDenominator < scaledX &&
            scaledX < (2 * edge.A.X + 1) * horizontalDenominator;
    }

    // A center-to-center segment intersects a cell only when it enters its interior.
    private static bool CrossesInterior(Cell from, Cell to, Cell cell)
    {
        double low = 0, high = 1;
        foreach (var (start, delta, center) in new[] {
            ((double)from.X, (double)(to.X - from.X), (double)cell.X),
            ((double)from.Y, (double)(to.Y - from.Y), (double)cell.Y) })
        {
            if (delta == 0)
            {
                if (Math.Abs(start - center) >= 0.5) return false;
                continue;
            }
            var a = (center - 0.5 - start) / delta;
            var b = (center + 0.5 - start) / delta;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
        }
        return high > low;
    }
}
