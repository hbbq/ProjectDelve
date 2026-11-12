using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record SettlementLot(string Key, string Purpose, CellBounds Bounds, CardinalSide Facing,
    int Entrance, int MinPreferredRooms, int MaxPreferredRooms, IReadOnlyList<int> Approach);
internal sealed record SettlementSpace(string Key, string Role, IReadOnlyList<int> Cells);
internal sealed record SettlementLayout(SettlementFamily Family, IReadOnlyList<SettlementLot> Lots,
    SettlementSpace Space, IReadOnlyList<int> EntryPath, IReadOnlyList<int> Road,
    IReadOnlyList<(int Inside, int Outside)> Contacts, CellBounds Bounds, int Cost);

internal static class SettlementLayouts
{
    private static readonly CardinalSide[] Sides = Enum.GetValues<CardinalSide>();

    internal static IReadOnlyList<SettlementLayout>? Candidates(SettlementFamily family, int count, SettlementRequest request,
        GenerationScope scope, IReadOnlyList<int> route)
    {
        var area = request.Area;
        var samples = family == SettlementFamily.RoadsideHamlet ? Math.Min(12, route.Count) : 16;
        if (samples == 0) return [];
        List<SettlementLayout> candidates = [];
        var exhausted = false;
        bool Spend(int work)
        {
            if (scope.Budget.Spend(work)) return true;
            exhausted = true;
            return false;
        }
        var road = family == SettlementFamily.RoadsideHamlet ? route.ToHashSet() : [];
        var shift = family == SettlementFamily.RoadsideHamlet ? scope.Random("route-sample").Next(route.Count) : 0;
        for (var attempt = 0; attempt < samples; attempt++)
        {
            if (!Spend(1)) return null;
            var random = scope.Random("geometry", attempt);
            var layout = family == SettlementFamily.RoadsideHamlet
                ? Roadside(route[(int)((shift + (long)attempt * route.Count / samples) % route.Count)], road, count, request, scope.Draft, random, Spend)
                : Centered(family, count, request, scope.Draft, random, attempt, route, Spend);
            if (exhausted) return null;
            if (layout is not null && SiteGeometry.Fits(layout.Bounds, area)) candidates.Add(layout);
        }
        return candidates;
    }

    private static SettlementLayout? Centered(SettlementFamily family, int count, SettlementRequest request,
        BoardDraft board, Pcg32 random, int attempt, IReadOnlyList<int> route, Func<int, bool> spend)
    {
        var area = request.Area;
        var width = board.Width;
        var compact = Math.Min(area.Width, area.Height) < 24 || attempt % 4 == 3;
        var (spaceWidth, spaceHeight, spaceKey) = family switch
        {
            SettlementFamily.MarketVillage => (compact ? 5 : 5 + 2 * random.Next(2), compact ? 5 : 5 + 2 * random.Next(2), "market-square"),
            SettlementFamily.Farmstead => (compact ? 5 : 7 + random.Next(3), compact ? 4 : 5 + random.Next(2), "farmyard"),
            _ => (compact ? 5 : 7 + random.Next(2), compact ? 4 : 5 + random.Next(2), "service-yard")
        };
        var publicSpace = new CellBounds(-spaceWidth / 2, -spaceHeight / 2, spaceWidth, spaceHeight);
        var rotation = random.Next(4);
        CardinalSide Rotate(CardinalSide side) => (CardinalSide)(((int)side + rotation) % 4);
        Cell RotateCell(Cell cell) => rotation switch
        {
            1 => new(-cell.Y, cell.X),
            2 => new(-cell.X, -cell.Y),
            3 => new(cell.Y, -cell.X),
            _ => cell
        };
        var transformedSpace = Bounds(Cells(publicSpace).Select(RotateCell));
        var slots = family switch
        {
            SettlementFamily.MarketVillage => new[]
            {
                (CardinalSide.North, "market-hall", 2, 3), (CardinalSide.South, "house", 1, 2),
                (CardinalSide.West, "workshop", 2, 2), (CardinalSide.East, "shop", 1, 1)
            },
            SettlementFamily.Farmstead => new[]
            {
                (CardinalSide.West, "farmhouse", 2, 3), (CardinalSide.South, "barn", 1, 1),
                (CardinalSide.East, "storehouse", 1, 1), (CardinalSide.North, "workshop", 1, 1)
            },
            _ => new[]
            {
                (CardinalSide.North, "inn", 3, 3), (CardinalSide.South, "stable", 1, 1),
                (CardinalSide.East, "storehouse", 1, 1), (CardinalSide.West, "kitchen", 2, 2)
            }
        };
        if (family == SettlementFamily.MarketVillage)
        {
            var options = slots.Skip(1).ToList();
            random.Shuffle(options);
            slots = [slots[0], .. options];
        }
        var relativeLots = new List<(string Purpose, int MinRooms, int MaxRooms, CellBounds Bounds, CardinalSide Outward, Cell Door, Cell[] Path)>();
        foreach (var (side, purpose, minRooms, maxRooms) in slots.Take(count))
        {
            var (span, depth) = purpose switch
            {
                "inn" => (compact ? 8 : 9 + random.Next(3), compact ? 6 : 7 + random.Next(2)),
                "farmhouse" => (compact ? 6 : 7 + random.Next(2), compact ? 5 : 6 + random.Next(2)),
                "market-hall" => (compact ? 6 : 7 + random.Next(2), compact ? 5 : 5 + random.Next(3)),
                _ => (compact ? 4 + random.Next(2) : 4 + random.Next(3), compact ? 4 + random.Next(2) : 4 + random.Next(3))
            };
            if (request.Requirements?.MinimumRooms == 3)
            {
                span = Math.Max(span, 5);
                depth = Math.Max(depth, 5);
            }
            var setback = compact ? 1 : 1 + random.Next(3);
            var frontage = Face(publicSpace, side, random.Next(3) - 1);
            var delta = Step(side);
            var door = Offset(frontage, delta, setback);
            var plot = Plot(door, side, span + 2, depth + 2);
            relativeLots.Add((purpose, minRooms, maxRooms, Bounds(Cells(plot).Select(RotateCell)), Rotate(side), RotateCell(door),
                Enumerable.Range(1, setback).Select(i => RotateCell(Offset(frontage, delta, i))).ToArray()));
        }
        var bounds = Bounds(relativeLots.SelectMany(l => Cells(l.Bounds)).Concat(Cells(transformedSpace)));
        var xMin = area.X - bounds.X;
        var xMax = area.X + area.Width - bounds.Width - bounds.X;
        var yMin = area.Y - bounds.Y;
        var yMax = area.Y + area.Height - bounds.Height - bounds.Y;
        if (xMin > xMax || yMin > yMax) return null;
        var preferred = request.Preferences?.PreferredAnchor ?? (route.Count > 0 ? route[random.Next(route.Count)] : -1);
        var x = attempt % 3 != 2 && preferred >= 0 ? Math.Clamp(preferred % width + (attempt == 0 ? 0 : random.Next(21) - 10), xMin, xMax) : xMin + random.Next(xMax - xMin + 1);
        var y = attempt % 3 != 2 && preferred >= 0 ? Math.Clamp(preferred / width + (attempt == 0 ? 0 : random.Next(21) - 10), yMin, yMax) : yMin + random.Next(yMax - yMin + 1);
        int Index(Cell c) => (c.Y + y) * width + c.X + x;
        CellBounds Translate(CellBounds b) => new(b.X + x, b.Y + y, b.Width, b.Height);
        var lots = relativeLots.Select(l => new SettlementLot(l.Purpose, l.Purpose, Translate(l.Bounds), Opposite(l.Outward),
            Index(l.Door), l.MinRooms, l.MaxRooms, l.Path.Select(Index).ToArray())).ToArray();
        var space = new SettlementSpace(spaceKey, spaceKey, Cells(transformedSpace).Select(Index).ToArray());
        var plotCells = lots.SelectMany(l => SiteGeometry.Cells(l.Bounds, width)).ToArray();
        var access = space.Cells.Concat(lots.SelectMany(l => l.Approach)).ToHashSet();
        var blocked = plotCells.ToHashSet();
        if (blocked.Count != plotCells.Length || space.Cells.Any(blocked.Contains)) return null;
        var occupied = access.Concat(plotCells).Distinct().ToArray();
        if (!spend(occupied.Length) || occupied.Any(i => !Suitable(board, i, request.Preparation))) return null;
        var entry = Entry(board, area, space.Cells, blocked, access, request.Preferences?.PreferredApproach, request.Preparation, random);
        if (entry.Length == 0) return null;
        var siteBounds = Bounds(occupied.Concat(entry).Select(i => At(i, width)));
        if (!spend(route.Count)) return null;
        var proximity = SiteRouteAccess.ProximityCost(siteBounds, route, width);
        return new(family, lots, space, entry, [], [], siteBounds, occupied.Concat(entry).Distinct().Sum(i => PreparationCost(board, i)) + proximity);
    }

    private static SettlementLayout? Roadside(int start, HashSet<int> road, int count, SettlementRequest request,
        BoardDraft board, Pcg32 random, Func<int, bool> spend)
    {
        var width = board.Width;
        var area = request.Area;
        var parents = new Dictionary<int, (int Parent, int Distance)> { [start] = (-1, 0) };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            if (!spend(1)) return null;
            var distance = parents[cell].Distance;
            if (distance >= 16) continue;
            foreach (var next in board.Neighbors(cell))
                if (road.Contains(next) && board.EdgeAt(EdgeKey.Between(cell, next, width)).Passable() &&
                    parents.TryAdd(next, (cell, distance + 1))) queue.Enqueue(next);
        }
        var frontages = parents.Keys.SelectMany(i => Sides.Select(side => (Route: i, Side: side)))
            .Where(f => TryIndex(Offset(At(f.Route, width), Step(f.Side), 1), area, width, out var outside) &&
                !road.Contains(outside) && Suitable(board, outside, request.Preparation) &&
                board.EdgeAt(EdgeKey.Between(f.Route, outside, width)).Passable()).ToList();
        random.Shuffle(frontages);
        var lots = new List<SettlementLot>();
        var contacts = new List<(int Inside, int Outside)>();
        var chosen = new List<(int Route, CardinalSide Side)>();
        var occupied = new HashSet<int>();
        SettlementSpace? space = null;
        var purposes = new[] { "house", "workshop", "shop", "storehouse" };
        foreach (var purpose in purposes.Take(count))
        {
            var span = 4 + random.Next(3);
            var depth = 4 + random.Next(3);
            if (request.Requirements?.MinimumRooms == 3) { span = Math.Max(5, span); depth = Math.Max(5, depth); }
            var setback = lots.Count == 0 ? 2 + random.Next(2) : 2 + (lots.Count + random.Next(2)) % 3;
            var ordered = chosen.Count == 0
                ? frontages.OrderBy(f => parents[f.Route].Distance)
                : frontages.OrderBy(f => chosen.Count(c => c.Side == f.Side))
                    .ThenBy(f => Math.Abs(8 - chosen.Min(c => Distance(c.Route, f.Route, width))));
            foreach (var frontage in ordered.Take(96))
            {
                var routeCell = At(frontage.Route, width);
                var delta = Step(frontage.Side);
                var door = Offset(routeCell, delta, setback);
                var plot = Plot(door, frontage.Side, span + 2, depth + 2);
                if (!SiteGeometry.Fits(plot, area)) continue;
                if (!spend(plot.Width * plot.Height + setback)) return null;
                var approach = Enumerable.Range(1, setback).Select(i => Offset(routeCell, delta, i)).Select(c => c.Y * width + c.X).ToArray();
                var cells = SiteGeometry.Cells(plot, width).Concat(approach).Distinct().ToArray();
                if (cells.Any(i => occupied.Contains(i) || !Suitable(board, i, request.Preparation))) continue;
                SettlementSpace? green = null;
                if (space is null)
                {
                    var tangent = Step((CardinalSide)(((int)frontage.Side + 1) % 4));
                    foreach (var sign in new[] { 1, -1 })
                    {
                        var pocket = Enumerable.Range(1, setback - 1).SelectMany(d =>
                            Enumerable.Range(0, 3).Select(t => Offset(Offset(routeCell, delta, d), tangent, t * sign))).ToArray();
                        if (pocket.Any(c => !TryIndex(c, area, width, out var index) || !Suitable(board, index, request.Preparation))) continue;
                        green = new("roadside-green", "roadside-green", pocket.Select(c => c.Y * width + c.X).ToArray());
                        break;
                    }
                    if (green is null) continue;
                }
                occupied.UnionWith(cells);
                if (green is not null) { space = green; occupied.UnionWith(green.Cells); }
                lots.Add(new(purpose, purpose, plot, Opposite(frontage.Side), door.Y * width + door.X,
                    1, purpose == "house" ? 2 : 1, approach));
                contacts.Add((approach[0], frontage.Route));
                chosen.Add(frontage);
                break;
            }
        }
        if (lots.Count != count || space is null) return null;
        // Join actual frontage contacts through the existing road graph, including any bends
        // or branches between them. Road Cells are never sent to preparation or reservation.
        var usedRoad = new HashSet<int>();
        foreach (var contact in contacts)
            for (var cell = contact.Outside; cell >= 0 && usedRoad.Add(cell); cell = parents[cell].Parent) { }
        var siteBounds = Bounds(occupied.Concat(usedRoad).Select(i => At(i, width)));
        return new(SettlementFamily.RoadsideHamlet, lots, space, [], usedRoad.Order().ToArray(), contacts, siteBounds,
            occupied.Sum(i => PreparationCost(board, i)));
    }

    private static int[] Entry(BoardDraft board, CellBounds area, IReadOnlyList<int> space, HashSet<int> lots,
        HashSet<int> access, CardinalSide? preferred, PreparationPolicy policy, Pcg32 random)
    {
        var width = board.Width;
        var choices = new List<(CardinalSide Direction, int[] Path)>();
        var publicCells = space.ToHashSet();
        foreach (var start in space)
        foreach (var direction in Sides)
        {
            var path = new List<int> { start };
            var point = At(start, width);
            var step = Step(direction);
            for (var length = 1; length <= 7; length++)
            {
                point = Offset(point, step, 1);
                if (!TryIndex(point, area, width, out var index) || publicCells.Contains(index) || lots.Contains(index) ||
                    access.Contains(index) || !Suitable(board, index, policy)) break;
                path.Add(index);
            }
            if (path.Count >= 2) choices.Add((direction, path.ToArray()));
        }
        random.Shuffle(choices);
        return choices.OrderBy(p => preferred.HasValue && p.Direction != preferred ? 1 : 0)
            .ThenByDescending(p => p.Path.Length).Select(p => p.Path).FirstOrDefault() ?? [];
    }

    private static bool Suitable(BoardDraft board, int i, PreparationPolicy policy)
    {
        if (!board.CanWrite(i)) return false;
        var terrain = board.TerrainAt(i);
        var definition = TerrainCatalog.Find(board.Content.Terrain, terrain);
        if (definition.Categories.Contains(TerrainCategories.Water)) return policy.FillWater &&
            (terrain == "water" || definition is { Passable: true, Assembly: null, Production: null, LegacyKind: not TerrainKind.Rail });
        return terrain switch
        {
            "tree" or "fruit-tree" => policy.ClearTrees,
            "solid-rock" => policy.ExcavateRock,
            "water" => policy.FillWater,
            _ => TerrainCatalog.Find(board.Content.Terrain, terrain) is
                { Passable: true, Assembly: null, Production: null, LegacyKind: not TerrainKind.Rail }
        };
    }
    private static int PreparationCost(BoardDraft board, int i) =>
        TerrainCatalog.Find(board.Content.Terrain, board.TerrainAt(i)).Categories.Contains(TerrainCategories.Water) ? 6 : board.TerrainAt(i) switch
        {
            "tree" or "fruit-tree" => 2, "solid-rock" => 4, _ => 0
        };
    private static Cell Face(CellBounds b, CardinalSide side, int offset) => side switch
    {
        CardinalSide.North => new(b.X + b.Width / 2 + offset, b.Y),
        CardinalSide.South => new(b.X + b.Width / 2 + offset, b.Y + b.Height - 1),
        CardinalSide.East => new(b.X + b.Width - 1, b.Y + b.Height / 2 + offset),
        _ => new(b.X, b.Y + b.Height / 2 + offset)
    };
    private static CellBounds Plot(Cell door, CardinalSide outward, int span, int depth) => outward switch
    {
        CardinalSide.North => new(door.X - span / 2, door.Y - depth + 1, span, depth),
        CardinalSide.South => new(door.X - span / 2, door.Y, span, depth),
        CardinalSide.East => new(door.X, door.Y - span / 2, depth, span),
        _ => new(door.X - depth + 1, door.Y - span / 2, depth, span)
    };
    private static CardinalSide Opposite(CardinalSide side) => (CardinalSide)(((int)side + 2) % 4);
    private static Cell Step(CardinalSide side) => side switch
    {
        CardinalSide.North => new(0, -1), CardinalSide.East => new(1, 0), CardinalSide.South => new(0, 1), _ => new(-1, 0)
    };
    private static Cell Offset(Cell cell, Cell delta, int distance) => new(cell.X + delta.X * distance, cell.Y + delta.Y * distance);
    private static Cell At(int i, int width) => new(i % width, i / width);
    private static int Distance(int a, int b, int width) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
    private static bool TryIndex(Cell c, CellBounds b, int width, out int index)
    {
        index = c.Y * width + c.X;
        return c.X >= b.X && c.X < b.X + b.Width && c.Y >= b.Y && c.Y < b.Y + b.Height;
    }
    private static IEnumerable<Cell> Cells(CellBounds b) => Enumerable.Range(b.Y, b.Height)
        .SelectMany(y => Enumerable.Range(b.X, b.Width).Select(x => new Cell(x, y)));
    private static CellBounds Bounds(IEnumerable<Cell> cells)
    {
        var points = cells.ToArray();
        var x = points.Min(c => c.X);
        var y = points.Min(c => c.Y);
        return new(x, y, points.Max(c => c.X) - x + 1, points.Max(c => c.Y) - y + 1);
    }
}
