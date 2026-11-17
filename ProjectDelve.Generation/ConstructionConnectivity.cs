using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal enum ConstructionProofStatus { Found, Impossible, Incomplete, Error }
internal sealed record ConstructionBlocker(int FromIndex, int ToIndex, string? OwnerId, string Reason)
{
    public string? SourceId { get; init; }
}
internal sealed record ConstructionProof(ConstructionProofStatus Status, long Revision,
    ImmutableArray<int> Path, ImmutableArray<ConstructionConversion> Actions,
    ImmutableArray<ConstructionBlocker> Frontier, int WorkUsed, string? Detail)
{
    internal ConstructionCapabilities? Snapshot { get; init; }
    public ImmutableArray<int> Reachable { get; init; } = [];
    public bool RegionComplete { get; init; }
    public ImmutableArray<int> Starts { get; init; } = [];
    public ImmutableArray<int> Goals { get; init; } = [];
    internal ImmutableDictionary<int, int> Parents { get; init; } = ImmutableDictionary<int, int>.Empty;
}

/// <summary>Bounded ordinary-land connectivity on the simultaneous maximal monotone patch.
/// A complete negative proof is possible only with explicitly complete operation and interface metadata.</summary>
internal static class ConstructionConnectivity
{
    internal static ConstructionProof Prove(ConstructionCapabilities snapshot, IEnumerable<int> starts,
        IEnumerable<int> goals, int workBudget = 100000)
        => Search(snapshot, starts, goals, workBudget, false);

    internal static ConstructionProof Explore(ConstructionCapabilities snapshot, IEnumerable<int> starts, int workBudget = 100000)
    {
        var roots = starts.ToArray();
        return Search(snapshot, roots, roots, workBudget, true);
    }

    private static ConstructionProof Search(ConstructionCapabilities snapshot, IEnumerable<int> starts,
        IEnumerable<int> goals, int workBudget, bool exploreRegion)
    {
        var work = 0;
        var reached = new HashSet<int>();
        var parents = new Dictionary<int, int>();
        var regionComplete = false;
        ImmutableArray<int> requestedStarts = [], requestedGoals = [];
        ConstructionProof Finish(ConstructionProofStatus status, IEnumerable<int>? path = null,
            IEnumerable<ConstructionBlocker>? frontier = null, string? detail = null)
        {
            var cells = (path ?? []).ToImmutableArray();
            return new(status, snapshot.Revision, cells, snapshot.ActionsFor(cells),
                (frontier ?? []).ToImmutableArray(), work, detail)
                { Snapshot = snapshot, Reachable = reached.Order().ToImmutableArray(), RegionComplete = regionComplete,
                    Starts = requestedStarts, Goals = requestedGoals, Parents = parents.ToImmutableDictionary() };
        }
        if (workBudget < 0) return Finish(ConstructionProofStatus.Error, detail: "Negative work budget.");
        if (snapshot.Error is not null) return Finish(ConstructionProofStatus.Error, detail: snapshot.Error);
        if (!snapshot.IsCurrent(snapshot.Source)) return Finish(ConstructionProofStatus.Error, detail: "Stale construction snapshot.");
        var sources = starts.Distinct().Order().ToArray(); var targets = goals.ToHashSet();
        requestedStarts = sources.ToImmutableArray(); requestedGoals = targets.Order().ToImmutableArray();
        if (sources.Length == 0 || targets.Count == 0)
            return Finish(ConstructionProofStatus.Error, detail: "Connectivity requires explicit nonempty endpoint sets.");
        if (sources.Concat(targets).Any(i => i < 0 || i >= snapshot.Width * snapshot.Height))
            return Finish(ConstructionProofStatus.Error, detail: "Connectivity endpoint outside board.");
        if (workBudget == 0) return Finish(ConstructionProofStatus.Incomplete, detail: "Connectivity work budget exhausted.");
        var queue = new Queue<int>();
        var frontier = new List<ConstructionBlocker>();
        List<int>? foundPath = null;
        foreach (var source in sources)
            if (snapshot.MaximalGeometry.CanStop(ConstructionCapabilities.OrdinaryLand, snapshot.CellAt(source)))
            { parents[source] = -1; reached.Add(source); queue.Enqueue(source); }
            else frontier.Add(new(source, source, snapshot.ReservationOwner(source),
                snapshot.HasMachine(source) ? "Immutable Machine at source" : "Source has no compatible declared land preparation"));
        while (queue.TryDequeue(out var current))
        {
            if (work == workBudget) return Finish(ConstructionProofStatus.Incomplete, frontier: frontier,
                detail: "Connectivity work budget exhausted.");
            work++;
            if (foundPath is null && targets.Contains(current))
            {
                var path = new List<int>(); for (var i = current; i >= 0; i = parents[i]) path.Add(i);
                path.Reverse(); foundPath = path;
                if (!exploreRegion) return Finish(ConstructionProofStatus.Found, path);
            }
            // Explicit top-left neighbor order gives stable shortest-path parents.
            foreach (var next in PlanningBoard.Adjacent(current, snapshot.Width, snapshot.Height).Order())
            {
                if (parents.ContainsKey(next)) continue;
                if (snapshot.MaximalGeometry.CanStep(ConstructionCapabilities.OrdinaryLand, snapshot.CellAt(current), snapshot.CellAt(next)))
                { parents.Add(next, current); reached.Add(next); queue.Enqueue(next); }
                else
                {
                    var edge = snapshot.EdgeAt(EdgeKey.Between(current, next, snapshot.Width));
                    var protection = snapshot.CellProtection(next);
                    var edgeProtection = snapshot.EdgeProtection(EdgeKey.Between(current, next, snapshot.Width));
                    var reason = snapshot.HasMachine(next) ? "Immutable Machine" : edge is EdgeKind.Wall or EdgeKind.WallWithWindow
                        ? edgeProtection?.Reason ?? "Immutable structural Edge" : protection is not null ? protection.Reason : snapshot.ReservationOwner(next) is not null
                            ? "Protected terrain without a compatible declared conversion" : "Terrain without a compatible declared conversion";
                    frontier.Add(new(current, next, protection?.OwnerId ?? edgeProtection?.OwnerId ??
                        snapshot.ReservationOwner(next) ?? snapshot.ReservationOwner(current), reason)
                        { SourceId = protection?.SourceId ?? edgeProtection?.SourceId });
                }
            }
        }
        regionComplete = true;
        if (foundPath is not null) return Finish(ConstructionProofStatus.Found, foundPath,
            frontier.OrderBy(b => b.FromIndex).ThenBy(b => b.ToIndex));
        return Finish(snapshot.Complete ? ConstructionProofStatus.Impossible : ConstructionProofStatus.Incomplete,
            frontier: frontier.OrderBy(b => b.FromIndex).ThenBy(b => b.ToIndex),
            detail: snapshot.Complete ? "No path in the complete maximal declared construction graph." : "Construction operation or interface metadata is incomplete.");
    }

    internal static bool TryPrepare(ConstructionProof proof, PlanningBoard board, PlanningBudget budget,
        out BoardPatch? patch, out string? error)
    {
        patch = null;
        if (proof.Status != ConstructionProofStatus.Found || proof.Snapshot is null)
        { error = "Only a successful construction witness can be prepared."; return false; }
        if (proof.Revision != proof.Snapshot.Revision || proof.Path.IsDefaultOrEmpty ||
            !proof.Starts.Contains(proof.Path[0]) || !proof.Goals.Contains(proof.Path[^1]) ||
            !proof.Actions.SequenceEqual(proof.Snapshot.ActionsFor(proof.Path)))
        { error = "Invalid construction witness."; return false; }
        if (!proof.Snapshot.TryPrepareCells(proof.Path, board, budget, out var draft, out error)) return false;
        var materialized = GameEngine.CreateGame(draft!.Export(), board.Provider).Physical.Board;
        var potential = AccessibilityAnalysis.Capture(materialized with
            { Edges = materialized.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        if (!potential.CanStop(ConstructionCapabilities.OrdinaryLand, proof.Snapshot.CellAt(proof.Path[0])) ||
            proof.Path.Zip(proof.Path.Skip(1)).Any(pair =>
                !potential.CanStep(ConstructionCapabilities.OrdinaryLand, proof.Snapshot.CellAt(pair.First), proof.Snapshot.CellAt(pair.Second))))
        { error = "Materialized witness is not a legal ordinary-land path."; return false; }
        patch = draft!.BuildPatch(); return true;
    }
}
