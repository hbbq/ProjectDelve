using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Ephemeral authoring query shared by world entrances and Quick Play.
/// Doors are openable; figures do not change the selected geographic component.</summary>
public static class PlayableWorldComponent
{
    /// <summary>The current region of a previously selected root; never reselects by size.</summary>
    public static HashSet<int> FromRoot(IPlanningBoardView board, int root)
    {
        if (root < 0 || root >= board.Width * board.Height || !board.Passable(root)) return [];
        HashSet<int> result = [root]; Queue<int> queue = new([root]);
        while (queue.TryDequeue(out var current))
            foreach (var next in PlanningBoard.Adjacent(current, board.Width, board.Height))
                if (!result.Contains(next) && board.Passable(next) &&
                    Openable(board.EdgeAt(EdgeKey.Between(current, next, board.Width))))
                { result.Add(next); queue.Enqueue(next); }
        return result;
    }

    public static HashSet<int> Select(IPlanningBoardView board) => Select(board.Width, board.Height,
        board.Passable, (a, b) => Openable(board.EdgeAt(EdgeKey.Between(a, b, board.Width))));

    public static HashSet<int> Select(Board board)
    {
        var terrain = board.Terrain.ToDictionary(t => t.Position);
        var machines = board.Machines.Select(m => m.Position.Y * board.Width + m.Position.X).ToHashSet();
        var blocked = board.Edges.Where(e => !Openable(e.Kind))
            .Select(e => EdgeKey.Between(e.A.Y * board.Width + e.A.X, e.B.Y * board.Width + e.B.X, board.Width)).ToHashSet();
        return Select(board.Width, board.Height, i => !machines.Contains(i) &&
            board.TerrainDefinition(terrain.GetValueOrDefault(new(i % board.Width, i / board.Width))).Passable,
            (a, b) => !blocked.Contains(EdgeKey.Between(a, b, board.Width)));
    }

    private static bool Openable(EdgeKind edge) => edge.Passable() || edge == EdgeKind.ClosedDoor;

    private static HashSet<int> Select(int width, int height, Func<int, bool> passable, Func<int, int, bool> linked)
    {
        var ground = Enumerable.Range(0, width * height).Select(passable).ToArray();
        var seen = new bool[ground.Length]; HashSet<int> largest = [];
        for (var start = 0; start < ground.Length; start++)
        {
            if (!ground[start] || seen[start]) continue;
            HashSet<int> component = [start]; Queue<int> queue = new([start]); seen[start] = true;
            while (queue.TryDequeue(out var i))
                foreach (var j in PlanningBoard.Adjacent(i, width, height))
                    if (ground[j] && !seen[j] && linked(i, j))
                    { seen[j] = true; component.Add(j); queue.Enqueue(j); }
            // Ascending start index is the deterministic tie breaker.
            if (component.Count > largest.Count) largest = component;
        }
        return largest;
    }
}
