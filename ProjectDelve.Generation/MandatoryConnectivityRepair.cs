using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record MandatoryRepairSettings(int AnalysisWorkBudget = 2_000_000,
    int StyledAttemptsPerPass = 1, int MinimalConversionLimit = 8,
    int ModerateConversionLimit = 64, int AggressiveConversionLimit = 256);
internal enum MandatoryRepairStatus { Success, Impossible, Incomplete, WitnessMismatch, InvariantError, UnexpectedError }
internal sealed record MandatoryRepairAttempt(string Stage, long Revision, MandatoryRepairStatus Status,
    ImmutableArray<ConstructionConversion> Actions, ImmutableArray<ConstructionInterface> Interfaces,
    ImmutableArray<ConstructionBlocker> Frontier, int WorkUsed, string? Detail)
{
    public ImmutableArray<int> WitnessCells { get; init; } = [];
    public ImmutableArray<int> WitnessSupportCells { get; init; } = [];
    public bool SearchComplete { get; init; }
    public bool MetadataComplete { get; init; }
    public long PreparationWork { get; init; }
    public bool Accepted => Status == MandatoryRepairStatus.Success;
}
internal sealed record MandatoryRepairReport(MandatoryRepairStatus Status, ImmutableArray<MandatoryRepairAttempt> Attempts,
    int Conversions, long WorkUsed, bool UsedFallback, ImmutableArray<CompletionFailure> Failures,
    ImmutableArray<string> Diagnostics)
{
    internal bool Success => Status == MandatoryRepairStatus.Success;
    public int AcceptedConnections { get; init; }
    public ImmutableSortedDictionary<string, int> ConvertedByTerrain { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
    public int TotalDisturbance => Conversions;
}

/// <summary>Materializes explicit monotone permissions, independently of optional route policy.
/// Every candidate is private until actual ordinary coverage and protected publication both succeed.</summary>
internal static class MandatoryConnectivityRepair
{
    internal static MandatoryRepairReport Run(PlanningBoard board, CompletionGuard guard, MandatoryRepairSettings? settings = null)
    {
        settings ??= new();
        var attempts = new List<MandatoryRepairAttempt>();
        var diagnostics = new List<string>();
        var conversions = 0; long work = 0; var fallback = false; var connections = 0;
        var convertedByTerrain = new Dictionary<string, int>(StringComparer.Ordinal);
        ImmutableArray<CompletionFailure> knownFailures = [];
        MandatoryRepairReport Finish(MandatoryRepairStatus status) => new(status, attempts.ToImmutableArray(),
            conversions, work, fallback, knownFailures, diagnostics.ToImmutableArray())
            { AcceptedConnections = connections, ConvertedByTerrain = convertedByTerrain.ToImmutableSortedDictionary(StringComparer.Ordinal) };
        try
        {
            if (settings.AnalysisWorkBudget < 0 || settings.StyledAttemptsPerPass < 0 ||
                settings.MinimalConversionLimit < 0 || settings.ModerateConversionLimit < 0 || settings.AggressiveConversionLimit < 0)
            { diagnostics.Add("Repair settings require nonnegative bounds."); return Finish(MandatoryRepairStatus.InvariantError); }
            if (!guard.Registry.Errors.IsEmpty)
            { diagnostics.AddRange(guard.Registry.Errors); return Finish(MandatoryRepairStatus.InvariantError); }
            var initial = GameEngine.CreateGame(board.Export(), board.Provider);
            if (HeroDeploymentPlanner.Validate(initial, guard.Deployment) is { } deploymentError)
            { diagnostics.Add("Protected deployment invariant: " + deploymentError); return Finish(MandatoryRepairStatus.InvariantError); }
            var roots = new[] { guard.Deployment.PlacementOrder[0] };
            knownFailures = guard.ActualFailures(initial);
            if (knownFailures.IsEmpty) return Finish(MandatoryRepairStatus.Success);
            foreach (var (stage, limit, weight) in new[] { ("minimal", settings.MinimalConversionLimit, 100),
                         ("moderate", settings.ModerateConversionLimit, 20), ("aggressive", settings.AggressiveConversionLimit, 4) })
            for (var attempt = 0; attempt < settings.StyledAttemptsPerPass; attempt++)
            {
                var capabilities = guard.CaptureCapabilities();
                if (capabilities.Error is { } invalid)
                { diagnostics.Add(invalid); return Finish(MandatoryRepairStatus.InvariantError); }
                var search = WeightedTree(capabilities, roots, settings.AnalysisWorkBudget, weight, attempt, allowWater: stage != "minimal");
                work += search.Work;
                if (!TrySelect(guard.Registry, search.Parents, search.Costs, out var selected, out var support))
                {
                    attempts.Add(new(stage, board.Revision, MandatoryRepairStatus.Incomplete, [], capabilities.Interfaces, [], search.Work,
                        "Bounded styled search did not cover every required group.")
                        { SearchComplete = search.Complete, MetadataComplete = capabilities.Complete });
                    continue;
                }
                var actions = capabilities.ActionsFor(selected);
                if (actions.Length > limit || stage == "minimal" && actions.Any(a => a.Before == "water"))
                {
                    attempts.Add(new(stage, board.Revision, MandatoryRepairStatus.Incomplete, actions, capabilities.Interfaces, [], search.Work,
                        "Styled preparation policy exceeded; escalating mandatory preparation.")
                        { WitnessCells = selected.Except(support).Order().ToImmutableArray(), WitnessSupportCells = support,
                            SearchComplete = search.Complete, MetadataComplete = capabilities.Complete });
                    continue;
                }
                return Finish(Publish(capabilities, selected, support, stage, search.Work, [], search.Complete));
            }
            // Exhaust the declared graph once. There is no connection-count, water, excavation,
            // enrichment-preservation or optional route-work limit on this final parent-tree union.
            fallback = true;
            var snapshot = guard.CaptureCapabilities();
            var proof = ConstructionConnectivity.Explore(snapshot, roots, settings.AnalysisWorkBudget);
            work += proof.WorkUsed;
            if (proof.Status == ConstructionProofStatus.Error)
            {
                diagnostics.Add(proof.Detail ?? "Invalid construction snapshot.");
                attempts.Add(new("fallback", snapshot.Revision, MandatoryRepairStatus.InvariantError, [], snapshot.Interfaces,
                    proof.Frontier, proof.WorkUsed, proof.Detail) { SearchComplete = proof.RegionComplete, MetadataComplete = snapshot.Complete });
                return Finish(MandatoryRepairStatus.InvariantError);
            }
            if (!proof.RegionComplete || !TrySelect(guard.Registry, proof.Parents, null, out var union, out var fallbackSupport))
            {
                var status = snapshot.Complete && proof.RegionComplete ? MandatoryRepairStatus.Impossible : MandatoryRepairStatus.Incomplete;
                var detail = !proof.RegionComplete ? "Mandatory region exploration exhausted its analysis budget."
                    : snapshot.Complete ? "Required groups are unreachable in the complete declared graph."
                    : "Incomplete permission metadata cannot establish impossibility.";
                diagnostics.Add(detail);
                attempts.Add(new("fallback", snapshot.Revision, status, [], snapshot.Interfaces, proof.Frontier, proof.WorkUsed, detail)
                    { SearchComplete = proof.RegionComplete, MetadataComplete = snapshot.Complete });
                return Finish(status);
            }
            return Finish(Publish(snapshot, union, fallbackSupport, "fallback", proof.WorkUsed, proof.Frontier, proof.RegionComplete));
        }
        catch (ArgumentException exception)
        { diagnostics.Add(exception.Message); return Finish(MandatoryRepairStatus.InvariantError); }
        catch (Exception exception)
        { diagnostics.Add(exception.GetType().FullName + ": " + exception.Message); return Finish(MandatoryRepairStatus.UnexpectedError); }

        MandatoryRepairStatus Publish(ConstructionCapabilities snapshot, HashSet<int> selected, ImmutableArray<int> support, string stage,
            int searchWork, ImmutableArray<ConstructionBlocker> frontier, bool searchComplete)
        {
            var actions = snapshot.ActionsFor(selected); var revision = board.Revision;
            var preparationBudget = new PlanningBudget(maxWork: board.Width * (long)board.Height);
            MandatoryRepairStatus Result(MandatoryRepairStatus status, string? detail)
            {
                work += preparationBudget.Work;
                attempts.Add(new(stage, revision, status, actions, snapshot.Interfaces, frontier, searchWork, detail)
                { WitnessCells = selected.Except(support).Order().ToImmutableArray(), WitnessSupportCells = support, SearchComplete = searchComplete,
                    MetadataComplete = snapshot.Complete, PreparationWork = preparationBudget.Work });
                if (detail is not null) diagnostics.Add(detail);
                return status;
            }
            try
            {
                // This preparation allowance is owned solely by mandatory repair.
                if (!snapshot.TryPrepareCells(selected.Order(), board, preparationBudget, out var draft, out var error))
                    return Result(MandatoryRepairStatus.WitnessMismatch, "Permission witness replay failed: " + error);
                var actual = GameEngine.CreateGame(draft!.Export(), board.Provider);
                if (!guard.ActualFailures(actual).IsEmpty)
                    return Result(MandatoryRepairStatus.WitnessMismatch, "Materialized witness does not provide actual ordinary coverage.");
                if (!guard.TryPublish(draft.BuildPatch(), "mandatory-repair/" + stage, "mandatory-repair", out var diagnostic))
                    return Result(diagnostic.Status == CompletionStatus.Incomplete ? MandatoryRepairStatus.Incomplete :
                        diagnostic.Status == CompletionStatus.Impossible ? MandatoryRepairStatus.WitnessMismatch : MandatoryRepairStatus.InvariantError,
                        "Atomic repair publication rejected: " + diagnostic.Detail);
                conversions += actions.Length;
                connections += knownFailures.Length;
                foreach (var action in actions) convertedByTerrain[action.Before] = convertedByTerrain.GetValueOrDefault(action.Before) + 1;
                knownFailures = guard.ActualFailures(GameEngine.CreateGame(board.Export(), board.Provider));
                return Result(MandatoryRepairStatus.Success, null);
            }
            catch (ArgumentException exception)
            { return Result(MandatoryRepairStatus.InvariantError, exception.GetType().FullName + ": " + exception.Message); }
            catch (Exception exception)
            { return Result(MandatoryRepairStatus.UnexpectedError, exception.GetType().FullName + ": " + exception.Message); }
        }
    }

    internal static ImmutableHashSet<int> ActualReachable(Board board, IEnumerable<int> roots)
    {
        var potential = AccessibilityAnalysis.Capture(board with { Edges = board.Edges.Select(e =>
            e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        Cell Cell(int i) => new(i % board.Width, i / board.Width);
        var reached = new HashSet<int>(); var queue = new Queue<int>();
        foreach (var root in roots.Distinct().Order())
            if (root >= 0 && root < board.Width * board.Height && potential.CanStop(ConstructionCapabilities.OrdinaryLand, Cell(root)) && reached.Add(root))
                queue.Enqueue(root);
        while (queue.TryDequeue(out var current))
        foreach (var next in PlanningBoard.Adjacent(current, board.Width, board.Height).Order())
            if (!reached.Contains(next) && potential.CanStep(ConstructionCapabilities.OrdinaryLand, Cell(current), Cell(next)))
            { reached.Add(next); queue.Enqueue(next); }
        return reached.ToImmutableHashSet();
    }

    internal static ImmutableArray<CompletionFailure> Failures(RequiredLocationRegistry registry, IReadOnlySet<int> reached,
        Func<int, bool> supportAvailable) =>
        registry.Locations.Where(l => l.AccessClass == RequiredAccessClass.Ordinary).SelectMany(l => l.Groups
            .Where(g => g.Coverage == RequiredAccessCoverage.All ? !g.Positions.All(i => Available(g, i, reached, supportAvailable))
                : !g.Positions.Any(i => Available(g, i, reached, supportAvailable)))
            .Select(g => new CompletionFailure(l.Id, g.Id, g.Positions.Where(i => !Available(g, i, reached, supportAvailable)).Order().ToImmutableArray())))
            .ToImmutableArray();

    private static bool Available(RequiredAccessGroup group, int position, IReadOnlySet<int> reached, Func<int, bool> supportAvailable) =>
        reached.Contains(position) && group.Preparation.Where(p => p.Position == position)
            .SelectMany(p => p.Cells).All(supportAvailable);

    private static bool TrySelect(RequiredLocationRegistry registry, IReadOnlyDictionary<int, int> parents,
        IReadOnlyDictionary<int, long>? costs, out HashSet<int> selected, out ImmutableArray<int> support)
    {
        selected = []; support = []; var supportCells = new HashSet<int>();
        foreach (var location in registry.Locations.Where(l => l.AccessClass == RequiredAccessClass.Ordinary))
        foreach (var group in location.Groups)
        {
            var reachable = group.Positions.Where(parents.ContainsKey).OrderBy(i => costs?.GetValueOrDefault(i) ?? 0).ThenBy(i => i).ToArray();
            if (reachable.Length == 0 || group.Coverage == RequiredAccessCoverage.All && reachable.Length != group.Positions.Length) return false;
            foreach (var goal in group.Coverage == RequiredAccessCoverage.All ? reachable : reachable.Take(1))
            {
                for (var i = goal; i >= 0 && selected.Add(i); i = parents[i]) { }
                foreach (var preparation in group.Preparation.Where(p => p.Position == goal)) supportCells.UnionWith(preparation.Cells);
            }
        }
        // Supports need not be reachable destinations. Add them only after every parent
        // chain has been reconstructed, so they cannot truncate a later branch.
        support = supportCells.Except(selected).Order().ToImmutableArray();
        selected.UnionWith(supportCells);
        return true;
    }

    private static (Dictionary<int, int> Parents, Dictionary<int, long> Costs, int Work, bool Complete) WeightedTree(
        ConstructionCapabilities snapshot, IEnumerable<int> roots, int budget, int conversionWeight, int variation, bool allowWater)
    {
        var parents = new Dictionary<int, int>(); var costs = new Dictionary<int, long>();
        var queue = new PriorityQueue<int, (long Cost, int Index)>(); var finalized = new HashSet<int>();
        var conversion = snapshot.Conversions.ToDictionary(c => c.Index); var work = 0; var exhausted = false;
        foreach (var root in roots.Order())
            if (snapshot.MaximalGeometry.CanStop(ConstructionCapabilities.OrdinaryLand, snapshot.CellAt(root)))
            { costs[root] = 0; parents[root] = -1; queue.Enqueue(root, (0, root)); }
        while (queue.TryDequeue(out var current, out var priority))
        {
            if (finalized.Contains(current) || costs[current] != priority.Cost) continue;
            if (work == budget) { exhausted = true; break; }
            finalized.Add(current); work++;
            foreach (var next in PlanningBoard.Adjacent(current, snapshot.Width, snapshot.Height).Order())
            {
                if (!allowWater && conversion.TryGetValue(next, out var prohibited) && prohibited.Before == "water") continue;
                if (finalized.Contains(next) || !snapshot.MaximalGeometry.CanStep(ConstructionCapabilities.OrdinaryLand,
                    snapshot.CellAt(current), snapshot.CellAt(next))) continue;
                var penalty = conversion.TryGetValue(next, out var action) ? conversionWeight * (action.Before == "water" ? 4 : action.Before == "solid-rock" ? 3 : 1) : 0;
                var cost = costs[current] + 1L + penalty + (variation == 0 ? 0 : (next + (long)variation) % 3);
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost; parents[next] = current; queue.Enqueue(next, (cost, next));
            }
        }
        // Discovered but unexpanded nodes still have valid parent paths; bounded search can use them.
        return (parents, costs, work, !exhausted);
    }
}
