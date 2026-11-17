using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal enum CompletionStatus { Allowed, Impossible, Incomplete, Error, Omitted }
internal sealed record CompletionFailure(string LocationId, string GroupId, ImmutableArray<int> Unreachable);
internal sealed record CompletionDiagnostic(string CandidateId, string ProducerId, CompletionStatus Status,
    string? Detail, ImmutableArray<CompletionFailure> Failures, ImmutableArray<ConstructionBlocker> Frontier,
    ImmutableArray<ConstructionInterface> Interfaces, int WorkUsed, bool ReusedWitness);
internal sealed record CompletionWitness(ImmutableHashSet<int> Cells, ImmutableArray<EdgeKey> Edges);
internal sealed record CompletionEvaluation(CompletionDiagnostic Diagnostic, RequiredLocationRegistry Registry,
    ImmutableArray<SiteInfo> Sites, CompletionWitness? Witness, ImmutableHashSet<string> InteractiveIds);

/// <summary>Internal publication policy. Certifies legal completion, never repairs geometry itself.
/// Only TryPublish changes accepted state; rejected private candidates retain diagnostics alone.</summary>
internal sealed class CompletionGuard
{
    private readonly PlanningBoard source;
    private readonly HeroDeployment deployment;
    private readonly ImmutableHashSet<int> naturalCells;
    private readonly int workBudget;
    private CompletionWitness? witness;
    private ImmutableHashSet<string> interactiveIds = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    private long placementRevision = -1;
    private bool actualConnectivityEstablished;
    private ImmutableHashSet<int> placementDomain = [];
    private readonly List<CompletionDiagnostic> diagnostics = [];
    internal RequiredLocationRegistry Registry { get; private set; }
    internal ImmutableArray<SiteInfo> Sites { get; private set; } = [];
    internal IReadOnlyList<CompletionDiagnostic> Diagnostics => diagnostics.AsReadOnly();
    internal CompletionWitness? Witness => witness;
    internal HeroDeployment Deployment => deployment;
    internal ConstructionCapabilities CaptureCapabilities() => CompletionCapabilities.Capture(source, Sites, naturalCells, deployment);

    // Late nonblocking content must stay in the real, repaired region. The potential
    // domain remains available to Phase 2 standalone authoring before repair runs.
    internal void MarkActualConnectivityEstablished()
    {
        var failures = ActualFailures(GameEngine.CreateGame(source.Export(), source.Provider));
        if (!failures.IsEmpty) throw new InvalidOperationException("Cannot certify actual connectivity with unreachable requirements.");
        actualConnectivityEstablished = true; placementRevision = -1;
    }

    internal ImmutableArray<CompletionFailure> ActualFailures(GameState state) => ActualFailures(state, Registry, interactiveIds);

    private ImmutableArray<CompletionFailure> ActualFailures(GameState state, RequiredLocationRegistry registry, IReadOnlySet<string> identities)
    {
        // Machine positions declared on the maximal board may still depend on
        // unprepared LOS terrain. Re-evaluate their legal positions on the real board.
        var actual = RequiredLocations.ForInteractives(state, "interactive").ToDictionary(l => l.Id, StringComparer.Ordinal);
        var locations = registry.Locations.Select(l => identities.Contains(l.Id) && actual.TryGetValue(l.Id, out var current)
            ? l with { Groups = current.Groups } : l);
        var refreshed = RequiredLocationRegistry.Create(locations, source.Width, source.Height);
        var reached = MandatoryConnectivityRepair.ActualReachable(state.Physical.Board, [deployment.PlacementOrder[0]]);
        var board = state.Physical.Board;
        var geometry = AccessibilityAnalysis.Capture(board with { Edges = board.Edges.Select(e =>
            e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        return MandatoryConnectivityRepair.Failures(refreshed, reached, i =>
            geometry.CanStop(ConstructionCapabilities.OrdinaryLand, new(i % source.Width, i / source.Width)));
    }

    internal IReadOnlySet<int> InteractivePlacementDomain()
    {
        if (placementRevision == source.Revision) return placementDomain;
        if (actualConnectivityEstablished)
        {
            placementDomain = MandatoryConnectivityRepair.ActualReachable(
                GameEngine.CreateGame(source.Export(), source.Provider).Physical.Board, [deployment.PlacementOrder[0]]);
            placementRevision = source.Revision;
            return placementDomain;
        }
        var capabilities = CompletionCapabilities.Capture(source, Sites, naturalCells, deployment);
        var proof = ConstructionConnectivity.Explore(capabilities, [deployment.PlacementOrder[0]], workBudget);
        if (proof.Status != ConstructionProofStatus.Found || !proof.RegionComplete)
            throw new InvalidOperationException($"Interactive placement domain {proof.Status}: {proof.Detail}");
        placementDomain = proof.Reachable.ToImmutableHashSet(); placementRevision = source.Revision;
        return placementDomain;
    }

    internal CompletionGuard(PlanningBoard board, HeroDeployment deployment, IEnumerable<int> naturalCells, int workBudget = 2_000_000)
    {
        if (workBudget < 0) throw new ArgumentException("Completion work budget must be nonnegative.");
        source = board; this.deployment = deployment; this.naturalCells = naturalCells.ToImmutableHashSet(); this.workBudget = workBudget;
        foreach (var index in this.naturalCells) board.CheckIndex(index);
        Registry = RequiredLocationRegistry.Create([], board.Width, board.Height);
    }

    internal CompletionEvaluation Check(PlanningBoard candidate, string candidateId, string producerId,
        IEnumerable<RequiredLocation>? additions = null, IEnumerable<SiteInfo>? newSites = null, ScenarioDefinition? interactiveSnapshot = null)
    {
        var sites = Sites.Concat(newSites ?? []).OrderBy(s => s.Id, StringComparer.Ordinal).ToImmutableArray();
        var registry = Registry.Merge(additions ?? []);
        var proposedInteractiveIds = interactiveIds;
        CompletionEvaluation Fail(CompletionStatus status, string detail, ConstructionProof? proof = null,
            IEnumerable<CompletionFailure>? failures = null, ConstructionCapabilities? capabilities = null) =>
            new(new(candidateId, producerId, status, detail, (failures ?? []).ToImmutableArray(), proof?.Frontier ?? [],
                capabilities?.Interfaces ?? [], proof?.WorkUsed ?? 0, false), registry, sites, null, proposedInteractiveIds);
        if (candidate.Width != source.Width || candidate.Height != source.Height || candidate.Revision != source.Revision)
            return Fail(CompletionStatus.Error, "Candidate does not match the accepted board revision/dimensions.");
        if (sites.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != sites.Length)
            return Fail(CompletionStatus.Error, "Duplicate protected Site identity.");
        if (!registry.Errors.IsEmpty) return Fail(CompletionStatus.Error, string.Join("; ", registry.Errors));
        if (ProtectedChange(candidate) is { } changed) return Fail(CompletionStatus.Error, changed);
        var state = GameEngine.CreateGame(candidate.Export(), candidate.Provider);
        if (HeroDeploymentPlanner.Validate(state, deployment) is { } invalid)
            return Fail(CompletionStatus.Error, "Protected deployment invariant: " + invalid);
        var capabilities = CompletionCapabilities.Capture(candidate, sites, naturalCells, deployment);
        if (capabilities.Error is { } capabilityError) return Fail(CompletionStatus.Error, capabilityError, capabilities: capabilities);
        foreach (var site in newSites ?? []) registry = registry.Merge(RequiredLocations.ForSite(site, producerId, capabilities.MaximalBoard));

        // Recompute Machine geometry when Walls/objects change. Keep every previously published
        // interactive identity; it may not disappear to make a candidate pass.
        var known = Registry.Locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var interactive = RequiredLocations.ForInteractives(interactiveSnapshot ?? candidate.Export(), "interactive", capabilities.MaximalBoard, capabilities.CurrentBoard)
            .Select(l => l with { ProducerId = known.TryGetValue(l.Id, out var original) ? original.ProducerId : producerId }).ToImmutableArray();
        proposedInteractiveIds = interactive.Select(l => l.Id).ToImmutableHashSet(StringComparer.Ordinal);
        if (!interactiveIds.IsSubsetOf(proposedInteractiveIds))
            return Fail(CompletionStatus.Error, "Candidate removes a previously required interactive location.", capabilities: capabilities);
        registry = RequiredLocationRegistry.Create(registry.Locations.Where(l => !interactiveIds.Contains(l.Id)).Concat(interactive), source.Width, source.Height);
        if (!registry.Errors.IsEmpty) return Fail(CompletionStatus.Error, string.Join("; ", registry.Errors), capabilities: capabilities);
        foreach (var location in registry.Locations.Where(l => l.AccessClass == RequiredAccessClass.Ordinary))
        foreach (var group in location.Groups)
        foreach (var preparation in group.Preparation)
        foreach (var index in preparation.Cells)
            if (!capabilities.MaximalGeometry.CanStop(ConstructionCapabilities.OrdinaryLand, Cell(index)))
                return Fail(CompletionStatus.Error,
                    $"Location '{location.Id}' group '{group.Id}' has no permitted preparation for support Cell {index}.", capabilities: capabilities);
        var ordinary = registry.Locations.Where(l => l.AccessClass == RequiredAccessClass.Ordinary).ToArray();
        if (witness is { } previous && previous.Cells.All(i => capabilities.MaximalGeometry.CanStop(ConstructionCapabilities.OrdinaryLand, Cell(i))) &&
            previous.Edges.All(e => capabilities.MaximalGeometry.CanStep(ConstructionCapabilities.OrdinaryLand,
                Cell(e.Index), Cell(e.Index + (e.Direction == EdgeDirection.Right ? 1 : source.Width)))) &&
            ordinary.All(l => l.Groups.All(g => Satisfied(g, previous.Cells))))
            return new(new(candidateId, producerId, CompletionStatus.Allowed, null, [], [], capabilities.Interfaces, 0, true), registry, sites, previous, proposedInteractiveIds);

        var proof = ConstructionConnectivity.Explore(capabilities, [deployment.PlacementOrder[0]], workBudget);
        if (proof.Status == ConstructionProofStatus.Error) return Fail(CompletionStatus.Error, proof.Detail!, proof, capabilities: capabilities);
        if (!proof.RegionComplete) return Fail(CompletionStatus.Incomplete, proof.Detail ?? "Completion region search is incomplete.", proof, capabilities: capabilities);
        var reached = proof.Reachable.ToImmutableHashSet();
        var failures = ordinary.SelectMany(l => l.Groups.Where(g => !Satisfied(g, reached))
            .Select(g => new CompletionFailure(l.Id, g.Id, g.Positions.Where(i => !reached.Contains(i)).Order().ToImmutableArray()))).ToArray();
        if (failures.Length > 0)
            return Fail(capabilities.Complete ? CompletionStatus.Impossible : CompletionStatus.Incomplete,
                capabilities.Complete ? "Required ordinary locations have no permitted completion." : "Missing complete conversion/interface metadata; no impossibility claim.",
                proof, failures, capabilities);

        var selected = ordinary.SelectMany(l => l.Groups.SelectMany(g => g.Coverage == RequiredAccessCoverage.All
            ? g.Positions : [g.Positions.Where(reached.Contains).Min()])).Distinct().Order();
        var cells = new HashSet<int> { deployment.PlacementOrder[0] }; var edges = new HashSet<EdgeKey>();
        foreach (var goal in selected)
            for (var i = goal; !cells.Contains(i); i = proof.Parents[i])
            {
                cells.Add(i); var parent = proof.Parents[i];
                if (parent >= 0) edges.Add(EdgeKey.Between(i, parent, source.Width));
            }
        var certificate = new CompletionWitness(cells.ToImmutableHashSet(), edges.OrderBy(e => e.Index).ThenBy(e => e.Direction).ToImmutableArray());
        return new(new(candidateId, producerId, CompletionStatus.Allowed, null, [], [], capabilities.Interfaces, proof.WorkUsed, false), registry, sites, certificate, proposedInteractiveIds);

        Cell Cell(int i) => new(i % source.Width, i / source.Width);
    }

    internal bool TryPublish(BoardPatch patch, string candidateId, string producerId, out CompletionDiagnostic diagnostic,
        IEnumerable<RequiredLocation>? additions = null, IEnumerable<SiteInfo>? newSites = null)
    {
        CompletionEvaluation evaluation;
        try { evaluation = Check(source.Preview(patch), candidateId, producerId, additions, newSites); }
        catch (ArgumentException exception)
        {
            diagnostic = new(candidateId, producerId, CompletionStatus.Error, exception.Message, [], [], [], 0, false);
            diagnostics.Add(diagnostic); return false;
        }
        diagnostic = evaluation.Diagnostic;
        if (diagnostic.Status != CompletionStatus.Allowed) { diagnostics.Add(diagnostic); return false; }
        if (!source.TryCommit(patch, out var error))
        {
            diagnostic = diagnostic with { Status = CompletionStatus.Error, Detail = error };
            diagnostics.Add(diagnostic); return false;
        }
        Registry = evaluation.Registry; Sites = evaluation.Sites; witness = evaluation.Witness; interactiveIds = evaluation.InteractiveIds;
        diagnostics.Add(diagnostic); return true;
    }

    // Nonblocking post-geometry producers add declarations through the same proof boundary.
    internal CompletionDiagnostic RegisterInteractives(ScenarioDefinition definition, string producerId)
    {
        var candidate = source.PreviewState(source.State);
        var evaluation = Check(candidate, producerId, producerId, interactiveSnapshot: definition);
        var diagnostic = evaluation.Diagnostic;
        if (diagnostic.Status == CompletionStatus.Allowed && actualConnectivityEstablished)
        {
            var state = GameEngine.CreateGame(definition, source.Provider);
            var failures = ActualFailures(state, evaluation.Registry, evaluation.InteractiveIds);
            if (!failures.IsEmpty) diagnostic = diagnostic with { Status = CompletionStatus.Error,
                Detail = "Late ordinary location is not actually reachable after mandatory repair.", Failures = failures };
        }
        if (diagnostic.Status == CompletionStatus.Allowed)
        { Registry = evaluation.Registry; witness = evaluation.Witness; interactiveIds = evaluation.InteractiveIds; }
        diagnostics.Add(diagnostic); return diagnostic;
    }

    internal void RecordRejection(CompletionDiagnostic diagnostic) => diagnostics.Add(diagnostic);
    internal void RecordOmission(string id, string producer, string detail) => diagnostics.Add(
        new(id, producer, CompletionStatus.Omitted, "Optional placement omitted: " + detail, [], [], [], 0, false));

    private static bool Satisfied(RequiredAccessGroup group, IReadOnlySet<int> reached) =>
        group.Coverage == RequiredAccessCoverage.All ? group.Positions.All(reached.Contains) : group.Positions.Any(reached.Contains);

    private string? ProtectedChange(PlanningBoard candidate)
    {
        var protectedCells = Sites.SelectMany(s => s.Footprint.Indices(source.Width))
            .Concat(deployment.Area.Indices(source.Width)).Append(deployment.DepartureIndex).ToHashSet();
        foreach (var i in protectedCells)
        {
            if (candidate.TerrainAt(i) != source.TerrainAt(i) || candidate.State.Reservations.GetValueOrDefault(i) != source.State.Reservations.GetValueOrDefault(i))
                return $"Previously protected Cell {i} was altered.";
            foreach (var j in source.Neighbors(i))
                if (protectedCells.Contains(j) && candidate.EdgeAt(EdgeKey.Between(i, j, source.Width)) != source.EdgeAt(EdgeKey.Between(i, j, source.Width)))
                    return $"Previously protected Edge at Cell {i} was altered.";
        }
        foreach (var edge in source.State.Edges.Where(e => e.Value != EdgeKind.None))
            if (candidate.EdgeAt(edge.Key) != edge.Value) return $"Previously structural Edge {edge.Key} was altered.";
        foreach (var machine in source.State.Machines)
            if (!candidate.State.Machines.Contains(machine)) return $"Previously protected Machine '{machine.Id}' was altered.";
        return null;
    }
}
