namespace ProjectDelve.Generation;

/// <summary>Soft authoring preferences only; permissions and movement remain in RouteGenerator.</summary>
internal static class RouteCostField
{
    internal static int[] Build(RouteRequest request, GenerationScope scope, string[] terrain, bool[] reuse, int attempt)
    {
        var w = scope.Board.Width; var h = scope.Board.Height; var field = new int[terrain.Length];
        var waterTypes = scope.Content.Terrain.Where(t => t.Categories.Contains(ProjectDelve.Engine.TerrainCategories.Water))
            .Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var random = RouteSeed.Stream(scope.RootSeed, scope.ObjectId(request.Key), "guide", attempt);
        var phase = random.Next(628) / 100.0;
        var sx = request.Start.Cells.Average(i => i % w); var sy = request.Start.Cells.Average(i => i / w);
        var ex = request.Destination.Cells.Average(i => i % w); var ey = request.Destination.Cells.Average(i => i / w);
        var dx = ex - sx; var dy = ey - sy; var length = Math.Sqrt(dx * dx + dy * dy);
        // A chord has exact Network endpoints too. Large network goal sets have no single guide destination.
        var guided = request.Start.Cells.Count <= 5 && request.Destination.Cells.Count <= 5 && length > 8;
        var amplitude = request.ProfileKind == RouteProfileKind.ForestTrail ? Math.Min(8, length * .10) :
            request.ProfileKind == RouteProfileKind.MinorPath ? Math.Min(3, length * .035) : 0;
        // Local terrain contacts distinguish natural openings, forest edges, shores and rock passes.
        for (var i = 0; i < field.Length; i++)
        {
            var x = i % w; var y = i / w; var trees = 0; var rock = 0; var water = 0;
            for (var yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++)
            for (var xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++)
            {
                var kind = terrain[yy * w + xx];
                if (kind is "tree" or "fruit-tree") trees++;
                else if (kind == "solid-rock") rock++;
                else if (waterTypes.Contains(kind)) water++;
            }
            var natural = request.ProfileKind == RouteProfileKind.ForestTrail ? (trees is > 0 and < 7 ? 0 : 3) :
                request.ProfileKind == RouteProfileKind.RockPassage ? rock * 2 : water is > 0 and < 5 ? 0 : 1;
            var edge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
            // Boundary ports still work; the penalty discourages long travel along the crop itself.
            var boundary = Math.Max(0, Math.Min(4, Math.Min(w, h) / 5) - edge) * 6;
            var guide = 0;
            if (guided && amplitude > 0)
            {
                var t = Math.Clamp(((x - sx) * dx + (y - sy) * dy) / (length * length), 0, 1);
                var offset = amplitude * Math.Sin(Math.PI * t) * Math.Sin(2 * Math.PI * t + phase);
                var perpendicular = ((x - sx) * -dy + (y - sy) * dx) / length;
                guide = (int)Math.Round(Math.Min(48, Math.Abs(perpendicular - offset) * 4));
            }
            var parallel = 0;
            if (!reuse[i])
                foreach (var j in scope.Board.Neighbors(i)) if (reuse[j]) { parallel = 4; break; }
            field[i] = natural + boundary + guide + parallel;
        }
        return field;
    }
}
