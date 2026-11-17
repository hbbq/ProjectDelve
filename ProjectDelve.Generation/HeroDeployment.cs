using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

// Construction policy for published deployment. The generator does not place Heroes.
internal sealed record HeroDeploymentSettings(int Capacity = 5, int WorkBudget = 2_000_000)
{
    internal int MinimumArea => checked(2 * Capacity);
    internal int PreferredArea => checked((5 * Capacity + 1) / 2);
    internal int MinimumCore => Math.Max(4, checked((3 * Capacity + 1) / 2));

    internal void ValidateDimensions(int width, int height)
    {
        if (Capacity < 1 || Capacity > (long)width * height / 2 || WorkBudget < 0)
            throw new ArgumentException("Deployment requires a positive capacity that fits the board and a nonnegative work budget.");
        // A rectangular clearance witness plus connected extension is constructive on an empty board.
        // Unlike an area-only check this rejects one-Cell-wide windows and leaves a departure Cell.
        if (width < 2 || height < 2 || (long)width * height <= Math.Max(MinimumArea, MinimumCore) ||
            !Enumerable.Range(2, width - 1).Any(w =>
                Math.Max(2, (MinimumCore + w - 1) / w) <= height &&
                (long)w * Math.Max(2, (MinimumCore + w - 1) / w) < (long)width * height))
            throw new ArgumentException("Board dimensions cannot support the requested deployment maneuver core, capacity and departure.");
    }
}

internal sealed record HeroDeployment(int Capacity, GeneratedArea Area, GeneratedArea ManeuverCore,
    ImmutableArray<int> PlacementOrder, int DepartureFromIndex, int DepartureIndex, long Revision);
internal enum HeroDeploymentStatus { Established, InsufficientSpace, Incomplete, Error }
internal sealed record HeroDeploymentResult(HeroDeploymentStatus Status, HeroDeployment? Deployment,
    ImmutableArray<ConstructionConversion> Preparation, int WorkUsed, string? Detail);
internal sealed record DeploymentStepParameters(HeroDeploymentSettings Settings) : WorldStepParameters;

internal static class HeroDeploymentPlanner
{
    private const string Owner = "hero-deployment";
    private static readonly AccessibilityProfile Single = new(Footprint.OneByOne, [MovementKinds.Land], 1);
    private static readonly AccessibilityProfile Wide = new(Footprint.TwoByTwo, [MovementKinds.Land], 1);
    private sealed class Exhausted : Exception;

    // The caller declares provenance explicitly. This never treats arbitrary unreserved terrain as natural.
    internal static ImmutableArray<ConstructionConversion> NaturalConversions(PlanningBoard board, IEnumerable<int> naturalCells)
        => naturalCells.Distinct().Order().Select(i => new ConstructionConversion(i, board.TerrainAt(i), board.TerrainAt(i) switch
        {
            "tree" or "fruit-tree" => "grass",
            "solid-rock" => "stone-floor",
            "water" => "stepping-stones",
            _ => ""
        })).Where(c => c.After.Length > 0).ToImmutableArray();

    internal static HeroDeploymentResult Establish(PlanningBoard board, HeroDeploymentSettings settings,
        ulong seed, IEnumerable<ConstructionConversion> conversions)
    {
        settings.ValidateDimensions(board.Width, board.Height);
        var work = 0;
        void Spend() { if (work == settings.WorkBudget) throw new Exhausted(); work++; }
        HeroDeploymentResult Failure(HeroDeploymentStatus status, string detail) => new(status, null, [], work, detail);
        var capabilities = ConstructionCapabilities.Capture(board, Owner, conversions,
            conversionsComplete: true, interfacesComplete: true);
        if (capabilities.Error is not null) return Failure(HeroDeploymentStatus.Error, capabilities.Error);
        var state = GameEngine.CreateGame(board.Export(), board.Provider);
        var occupied = state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id))
            .Select(c => c.Y * board.Width + c.X).ToHashSet();
        try
        {
            // Existing geometry wins over every construction candidate, independent of seed ordering.
            var selected = Find(capabilities.CurrentBoard) ?? Find(capabilities.MaximalBoard);
            if (selected is null) return Failure(HeroDeploymentStatus.InsufficientSpace,
                "Complete deployment search found no free open region with sufficient capacity, maneuver core and departure in the declared construction graph.");
            var indices = selected.Area.Indices(board.Width).Append(selected.DepartureIndex).ToArray();
            var preparation = capabilities.ActionsFor(indices);
            // Keep preparation, reservation and publication atomic. No partially carved spawn is published.
            var budget = new PlanningBudget(maxWork: settings.WorkBudget - work);
            if (!capabilities.TryPrepareCells(indices, board, budget, out var draft, out var error))
            {
                work += checked((int)budget.Work);
                return Failure(budget.Work >= budget.MaxWork ? HeroDeploymentStatus.Incomplete : HeroDeploymentStatus.Error, error!);
            }
            work += checked((int)budget.Work);
            var prepared = GameEngine.CreateGame(draft!.Export(), board.Provider);
            if (Validate(prepared, selected) is { } invalid) return Failure(HeroDeploymentStatus.Error, invalid);
            draft.Reserve(indices);
            if (!board.TryCommit(draft.BuildPatch(), out error)) return Failure(HeroDeploymentStatus.Error, error!);
            return new(HeroDeploymentStatus.Established, selected with { Revision = board.Revision }, preparation, work, null);
        }
        catch (Exhausted) { return Failure(HeroDeploymentStatus.Incomplete, "Deployment search work budget exhausted; no impossibility claim."); }

        HeroDeployment? Find(Board geometry)
        {
            var w = board.Width; var h = board.Height; var n = w * h;
            var analysis = AccessibilityAnalysis.Capture(geometry, capabilities.Revision);
            var free = new bool[n]; var square = new bool[n];
            var labels = Enumerable.Repeat(-1, n).ToArray(); var sizes = new List<int>();
            for (var i = 0; i < n; i++)
            {
                Spend(); free[i] = !occupied.Contains(i) && !board.IsReserved(i) && !board.IsRouteCorridor(i) &&
                    analysis.CanStop(Single, Cell(i));
            }
            for (var i = 0; i < n; i++)
            {
                if (!free[i] || labels[i] >= 0) continue;
                var id = sizes.Count; var size = 0; var queue = new Queue<int>([i]); labels[i] = id;
                while (queue.TryDequeue(out var current))
                {
                    Spend(); size++;
                    foreach (var next in Neighbors(current))
                        if (free[next] && labels[next] < 0 && analysis.CanStep(Single, Cell(current), Cell(next)))
                        { labels[next] = id; queue.Enqueue(next); }
                }
                sizes.Add(size);
            }
            for (var i = 0; i < n; i++)
            {
                Spend();
                square[i] = i % w + 1 < w && i / w + 1 < h &&
                    Square(i).All(j => free[j]) && analysis.CanStop(Wide, Cell(i));
            }
            var components = Enumerable.Repeat(-1, n).ToArray(); var coreSizes = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (!square[i] || components[i] >= 0) continue;
                var id = coreSizes.Count; HashSet<int> covered = []; var queue = new Queue<int>([i]); components[i] = id;
                while (queue.TryDequeue(out var current))
                {
                    Spend(); covered.UnionWith(Square(current));
                    foreach (var next in Neighbors(current))
                        if (square[next] && components[next] < 0 && analysis.CanStep(Wide, Cell(current), Cell(next)))
                        { components[next] = id; queue.Enqueue(next); }
                }
                coreSizes.Add(covered.Count);
            }
            var actionCells = capabilities.Conversions.Select(c => c.Index).ToHashSet();
            foreach (var anchor in Enumerable.Range(0, n).Where(i => square[i] && coreSizes[components[i]] >= settings.MinimumCore &&
                         sizes[labels[i]] > Math.Max(settings.MinimumArea, settings.MinimumCore))
                     .OrderBy(i => Square(i).Count(actionCells.Contains)).ThenBy(i => Order(seed, i)).ThenBy(i => i))
            {
                Spend(); var core = new HashSet<int>(); var seen = new HashSet<int> { anchor }; var queue = new Queue<int>([anchor]);
                while (queue.TryDequeue(out var current))
                {
                    Spend(); core.UnionWith(Square(current));
                    if (core.Count >= settings.MinimumCore) break;
                    foreach (var next in Neighbors(current).OrderBy(i => Order(seed, i)).ThenBy(i => i))
                        if (square[next] && !seen.Contains(next) && analysis.CanStep(Wide, Cell(current), Cell(next)))
                        { seen.Add(next); queue.Enqueue(next); }
                }
                var minimum = Math.Max(settings.MinimumArea, core.Count);
                if (sizes[labels[anchor]] <= minimum) continue;
                var target = Math.Max(minimum, Math.Min(settings.PreferredArea, sizes[labels[anchor]] - 1));
                var area = core.ToHashSet(); var growth = new Queue<int>(core.Order());
                while (growth.TryDequeue(out var current) && area.Count < target)
                {
                    Spend();
                    foreach (var next in Neighbors(current).OrderBy(i => actionCells.Contains(i) ? 1 : 0).ThenBy(i => Order(seed, i)).ThenBy(i => i))
                    {
                        if (!free[next] || area.Contains(next) || !analysis.CanStep(Single, Cell(current), Cell(next))) continue;
                        area.Add(next); growth.Enqueue(next); if (area.Count == target) break;
                    }
                }
                (int From, int To) exit = area.Order().SelectMany(i => Neighbors(i).Where(j => free[j] && !area.Contains(j) &&
                        analysis.CanStep(Single, Cell(i), Cell(j))).Select(j => (From: i, To: j)))
                    .OrderBy(p => actionCells.Contains(p.To) ? 1 : 0).ThenBy(p => Order(seed, p.To)).ThenBy(p => p.From).FirstOrDefault((-1, -1));
                if (area.Count < minimum || exit.From < 0) continue;
                return new(settings.Capacity, Area(Owner, area, w), Area(Owner + "/maneuver-core", core, w),
                    core.OrderBy(i => Order(seed, i)).ThenBy(i => i).Concat(area.Except(core).Order()).ToImmutableArray(),
                    exit.From, exit.To, capabilities.Revision);
            }
            return null;

            Cell Cell(int i) => new(i % w, i / w);
            IEnumerable<int> Square(int i) => [i, i + 1, i + w, i + w + 1];
            IEnumerable<int> Neighbors(int i) => PlanningBoard.Adjacent(i, w, h);
        }
    }

    internal static string? Validate(GameState state, HeroDeployment deployment)
    {
        var board = state.Physical.Board; var w = board.Width; var h = board.Height;
        var settings = new HeroDeploymentSettings(deployment.Capacity); settings.ValidateDimensions(w, h);
        var area = deployment.Area.Indices(w).ToHashSet(); var core = deployment.ManeuverCore.Indices(w).ToHashSet();
        var analysis = AccessibilityAnalysis.Capture(board);
        var occupied = state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id)).ToHashSet();
        Cell Cell(int i) => new(i % w, i / w);
        if (area.Count < settings.MinimumArea || core.Count < settings.MinimumCore || !core.IsSubsetOf(area) ||
            area.Any(i => i < 0 || i >= w * h || !analysis.CanStop(Single, Cell(i)) || occupied.Contains(Cell(i))))
            return "Deployment capacity, placement or maneuver area is invalid.";
        if (deployment.PlacementOrder.Length != area.Count || !deployment.PlacementOrder.ToHashSet().SetEquals(area) ||
            !deployment.PlacementOrder.Take(deployment.Capacity).All(core.Contains)) return "Invalid deployment placement order.";
        var reached = new HashSet<int> { area.Min() }; var queue = new Queue<int>(reached);
        while (queue.TryDequeue(out var current)) foreach (var next in PlanningBoard.Adjacent(current, w, h))
            if (area.Contains(next) && !reached.Contains(next) && analysis.CanStep(Single, Cell(current), Cell(next)))
            { reached.Add(next); queue.Enqueue(next); }
        if (!reached.SetEquals(area)) return "Deployment is not connected through currently open paths.";
        var anchors = core.Where(i => i % w + 1 < w && i / w + 1 < h &&
            new[] { i, i + 1, i + w, i + w + 1 }.All(core.Contains) && analysis.CanStop(Wide, Cell(i))).ToHashSet();
        if (anchors.Count == 0) return "Deployment has no width-two maneuver core.";
        var wideSeen = new HashSet<int>(); var validCore = false;
        foreach (var anchor in anchors.Order())
        {
            if (!wideSeen.Add(anchor)) continue;
            var component = new HashSet<int> { anchor }; queue = new([anchor]);
            while (queue.TryDequeue(out var current)) foreach (var next in PlanningBoard.Adjacent(current, w, h))
                if (anchors.Contains(next) && !wideSeen.Contains(next) && analysis.CanStep(Wide, Cell(current), Cell(next)))
                { wideSeen.Add(next); component.Add(next); queue.Enqueue(next); }
            if (component.SelectMany(i => new[] { i, i + 1, i + w, i + w + 1 }).ToHashSet().SetEquals(core))
            { validCore = true; break; }
        }
        if (!validCore) return "Deployment maneuver core is not connected by width-two translations.";
        var departure = Cell(deployment.DepartureIndex);
        if (!area.Contains(deployment.DepartureFromIndex) || area.Contains(deployment.DepartureIndex) ||
            occupied.Contains(departure) || !analysis.CanStep(Single, Cell(deployment.DepartureFromIndex), departure))
            return "Deployment has no valid open departure.";
        return null;
    }

    private static GeneratedArea Area(string id, IEnumerable<int> cells, int width)
    {
        var area = GeneratedArea.From(id, cells, width); return area with { Spans = area.Spans.ToImmutableArray() };
    }
    private static ulong Order(ulong seed, int index)
    {
        var value = unchecked(seed + (ulong)index * 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
