namespace ProjectDelve.Engine;

/// <summary>Replaces an explicit set of shared board Edges without changing figures or terrain.</summary>
public static class EdgeReplacementRules
{
    public static bool CanReplace(GameState state, Edge replacement)
    {
        var board = state.Physical.Board;
        if (!MovementRules.Inside(board, replacement.A) || !MovementRules.Inside(board, replacement.B) ||
            Math.Abs(replacement.A.X - replacement.B.X) + Math.Abs(replacement.A.Y - replacement.B.Y) != 1 ||
            !Enum.IsDefined(replacement.Kind)) return false;
        if (replacement.Kind is EdgeKind.Wall or EdgeKind.WallWithWindow &&
            board.TerrainAt(replacement.A) == TerrainKind.Water && board.TerrainAt(replacement.B) == TerrainKind.Water)
            return false;
        // Figure placement is physical: neither Lying nor Phase bypasses internal-edge restrictions.
        return replacement.Kind.Passable() || !state.Physical.Figures.Any(f =>
        {
            var cells = FootprintGeometry.OccupiedCells(state, f.Id);
            return cells.Contains(replacement.A) && cells.Contains(replacement.B);
        });
    }

    public static void ReplaceEdges(GameState state, IEnumerable<Edge> replacements)
    {
        var requested = replacements.ToList();
        if (requested.Select(e => Key(e)).Distinct().Count() != requested.Count)
            throw new ArgumentException("An explicit replacement set must specify each Edge once.", nameof(replacements));
        // Evaluate all legality before mutating any feature, then apply in stable board order.
        var legal = requested.Where(e => CanReplace(state, e)).OrderBy(e => Key(e).A.Y)
            .ThenBy(e => Key(e).A.X).ThenBy(e => Key(e).B.Y).ThenBy(e => Key(e).B.X).ToList();
        var keys = legal.Select(e => Key(e)).ToHashSet();
        state.Physical.Board.Edges.RemoveAll(e => keys.Contains(Key(e)));
        state.Physical.Board.Edges.AddRange(legal.Where(e => e.Kind != EdgeKind.None)
            .Select(e => new Edge(Key(e).A, Key(e).B, e.Kind)));
    }

    private static (Cell A, Cell B) Key(Edge e) => e.A.Y < e.B.Y || e.A.Y == e.B.Y && e.A.X < e.B.X
        ? (e.A, e.B) : (e.B, e.A);
}

internal static class ReshapeRoomRules
{
    internal static void Apply(GameState state, string unitId)
    {
        var center = state.Physical.Figures.Single(f => f.Id == unitId).Position;
        List<Edge> replacements = [];
        // Each row/column contributes its two perimeter Edges and its two interior Edges.
        for (var offset = -1; offset <= 1; offset++)
        for (var boundary = -2; boundary <= 1; boundary++)
        {
            var kind = boundary is -2 or 1 && offset != 0 ? EdgeKind.Wall : EdgeKind.None;
            Add(new(center.X + boundary, center.Y + offset), new(center.X + boundary + 1, center.Y + offset), kind);
            Add(new(center.X + offset, center.Y + boundary), new(center.X + offset, center.Y + boundary + 1), kind);
        }
        EdgeReplacementRules.ReplaceEdges(state, replacements);

        void Add(Cell a, Cell b, EdgeKind kind)
        {
            if (MovementRules.Inside(state.Physical.Board, a) && MovementRules.Inside(state.Physical.Board, b))
                replacements.Add(new(a, b, kind));
        }
    }
}
