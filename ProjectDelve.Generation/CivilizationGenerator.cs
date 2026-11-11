using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record CivilizationSettings(bool Enabled = true, ContentDensity Campsites = ContentDensity.Low, ContentDensity Settlements = ContentDensity.Medium, int PreparationIntensity = 2)
{
    public CivilizationSettings Validate()
    {
        if (!Enum.IsDefined(Campsites) || !Enum.IsDefined(Settlements) || PreparationIntensity is < 1 or > 3)
            throw new ArgumentException("Civilization requires known site densities and preparation intensity 1..3.");
        return this;
    }
}

public sealed record CivilizationStatistics(int Attempts, long Work, int Sites, int Campsites, int Settlements,
    int Buildings, int Rooms, int ChangedTerrain, int ChangedEdges)
{
    public int BeaconsPlaced { get; init; }
    public int BeaconsOmitted { get; init; }
    public int BeaconAttempts { get; init; }
    public long BeaconWork { get; init; }
}

public sealed partial class WorldGenerationContext
{
    public PlanningBoard? ConstructionBoard { get; internal set; }
    public List<SiteInfo> Sites { get; } = [];
    public List<ConstructionOperation> Construction { get; } = [];
    public List<PlacementFailure> PlanningFailures { get; } = [];
    public CivilizationStatistics? CivilizationStats { get; internal set; }
    public DensityAllocation? CampsiteDensity { get; internal set; }
    public DensityAllocation? SettlementDensity { get; internal set; }

    // Terrain and Edge changes both affect the same current-topology revision.
    public void RefreshConstructionAnalysis()
    {
        var board = ConstructionBoard ?? throw new InvalidOperationException("No construction board.");
        for (var i = 0; i < Samples.Length; i++) Samples[i] = Samples[i] with { TerrainTypeId = board.TerrainAt(i) };
        Revision++;
        CurrentAnalysis = TerrainAnalyzer.Analyze(Window, Samples, board.Content, Revision, ConstructionEdges(), board.State.Machines);
        Entrances.RemoveAll(e => !board.Passable(e.InsideIndex) || !board.Passable(e.OutsideIndex) ||
            !(board.EdgeAt(EdgeKey.Between(e.InsideIndex, e.OutsideIndex, Window.Width)) is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor));
        FeatureOpportunities.RemoveAll(o => !board.Passable(o.AnchorIndex) || board.IsReserved(o.AnchorIndex));
    }

    internal EdgeDefinition[] ConstructionEdges() => ConstructionBoard?.State.Edges
        .Where(p => p.Value != EdgeKind.None).OrderBy(p => p.Key.Index).ThenBy(p => p.Key.Direction)
        .Select(p => new EdgeDefinition(new(p.Key.Index % Window.Width, p.Key.Index / Window.Width), p.Key.Direction, p.Value)).ToArray() ?? [];
}

/// <summary>Concrete parent orchestration. Only complete, Engine-valid Sites enter shared state.</summary>
public static class CivilizationGenerator
{
    public static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var settings = ((CivilizationStepParameters)step.Parameters).Settings.Validate();
        if (!settings.Enabled) return;
        var board = context.EnsurePlanningBoard(content);
        var area = new CellBounds(0, 0, board.Width, board.Height);
        var budget = new PlanningBudget(512, 2_000_000);
        var path = FormattableString.Invariant($"{step.Id}/x{context.Window.OffsetX}-y{context.Window.OffsetY}-w{board.Width}-h{board.Height}");
        var policy = settings.PreparationIntensity switch
        {
            1 => new PreparationPolicy(MaxChangedCells: 320, MaxCost: 1600),
            3 => new PreparationPolicy(MaxChangedCells: 1100, MaxCost: 9000),
            _ => new PreparationPolicy(MaxChangedCells: 650, MaxCost: 4500)
        };
        context.Construction.Add(new(path, null, "Civilization", path, area, [], []));
        // Stable request keys, never accepted-object counts. Larger plots precede small camps.
        var settlementTarget = DensityScaling.Settlements(context.Window, settings.Settlements);
        var campsiteTarget = DensityScaling.Campsites(context.Window, settings.Campsites);
        for (var i = 0; i < settlementTarget; i++)
        {
            var key = $"settlement-{i}";
            var draft = board.BeginDraft(area, path + "/" + key, budget);
            var scope = new GenerationScope(context.World.Seed, path, draft);
            var rng = scope.Child(key, draft).Random("requirements");
            var routeAnchors = board.State.RouteCorridors.Keys.Order().ToArray();
            Accept(SettlementGenerator.Plan(new(key, area, policy, new(BuildingCount: 2 + rng.Next(3), AllowReducedBuildingCount: true), new(RouteAnchors: routeAnchors)), scope), draft);
        }
        for (var i = 0; i < campsiteTarget; i++)
        {
            var key = $"campsite-{i}";
            var draft = board.BeginDraft(area, path + "/" + key, budget);
            var scope = new GenerationScope(context.World.Seed, path, draft);
            var anchors = (context.CurrentAnalysis?.Opportunities ?? []).Concat(context.FeatureOpportunities)
                .Where(o => o.Kind is "clearing" or "open-area").OrderByDescending(o => o.UsableArea)
                .Select(o => o.AnchorIndex).Distinct().Take(16).ToArray();
            var routeAnchors = board.State.RouteCorridors.Keys.Order().ToArray();
            var requirements = new CampsiteRequirements(Radius: 2 + scope.Child(key, draft).Random("requirements").Next(3));
            Accept(CampsiteGenerator.Plan(new(key, area, policy, Requirements: requirements,
                Preferences: new(OpportunityAnchors: anchors, RouteAnchors: routeAnchors)), scope), draft);
        }
        // Separate stream and allowance keep Machine placement independent of Site retries.
        var beacons = BeaconGenerator.Apply(context, step, board, area, path);
        context.RefreshConstructionAnalysis();
        context.CampsiteDensity = new(campsiteTarget, campsiteTarget, context.Sites.Count(s => s.Kind == GeneratedSiteKind.Campsite));
        context.SettlementDensity = new(settlementTarget, settlementTarget, context.Sites.Count(s => s.Kind == GeneratedSiteKind.Settlement));
        context.CivilizationStats = new(budget.Attempts, budget.Work, context.Sites.Count,
            context.Sites.Count(s => s.Kind == GeneratedSiteKind.Campsite), context.Sites.Count(s => s.Kind == GeneratedSiteKind.Settlement),
            context.Sites.Sum(s => s.Buildings.Count), context.Sites.Sum(s => s.Buildings.Sum(b => b.Rooms.Count)),
            context.Sites.Sum(s => s.Preparation.ChangedTerrain) + beacons.ChangedTerrain, context.Sites.Sum(s => s.Preparation.ChangedEdges))
        {
            BeaconsPlaced = beacons.Placed, BeaconsOmitted = beacons.Target - beacons.Placed,
            BeaconAttempts = beacons.Attempts, BeaconWork = beacons.Work
        };

        void Accept(PlacementResult<SitePlan> result, BoardDraft draft)
        {
            if (result is Rejected<SitePlan> failed) { Failure(failed.Failure); return; }
            var plan = ((Planned<SitePlan>)result).Plan;
            if (!draft.Accept(plan.Patch, out var error) || !board.TryCommit(draft.BuildPatch(), out error))
            {
                Failure(new(plan.Site.Id, "Civilization", PlacementFailureCode.ValidationFailed, error ?? "Commit rejected.", budget.Attempts)); return;
            }
            var labels = context.OriginalAnalysis!.FormationLabels;
            var formationIds = plan.Site.Footprint.Indices(board.Width).Select(index => labels[index]).Where(label => label >= 0)
                .Distinct().Order().Select(label => context.OriginalAnalysis.Formations[label].Id).ToArray();
            var opportunity = context.CurrentAnalysis?.Opportunities.FirstOrDefault(o => plan.Site.Footprint.Contains(o.AnchorIndex, board.Width));
            context.Sites.Add(plan.Site with { SourceFormationIds = formationIds, SourceOpportunityId = opportunity?.Id });
            context.Construction.AddRange(plan.Operations);
        }
        void Failure(PlacementFailure failure) { if (context.PlanningFailures.Count < 64) context.PlanningFailures.Add(failure); }
    }
}
