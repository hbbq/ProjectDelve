using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum WorldStepKind { NaturalTerrain, TerrainAnalysis, ForestEnrichment, RockEnrichment, WaterEnrichment, GrasslandEnrichment, Civilization, PostEnrichmentAnalysis, RouteNetwork, Population, HeroDeployment, MandatoryConnectivity }
public abstract record WorldStepParameters;
public sealed record NaturalStepParameters(NaturalTerrainSettings Settings) : WorldStepParameters;
public sealed record AnalysisStepParameters : WorldStepParameters;
public sealed record EnrichmentStepParameters(int Intensity = 2) : WorldStepParameters;
public sealed record CivilizationStepParameters(CivilizationSettings Settings) : WorldStepParameters;
internal sealed record MandatoryConnectivityStepParameters(MandatoryRepairSettings Settings) : WorldStepParameters;
public sealed record WorldGenerationStep(string Id, WorldStepKind Kind, WorldStepParameters Parameters, IReadOnlyList<string> DependsOn);

// Internal typed recipe. UI codes construct this explicit schedule; no runtime registry or executable content.
public sealed record WorldGenerationPlan(WorldDefinition World, WorldWindow Window, IReadOnlyList<WorldGenerationStep> Steps)
{
    public string LootPreset { get; init; } = "All";
    public LootAbundance LootAbundance { get; init; } = LootAbundance.Standard;
    internal (ulong WorldSeed, ExpeditionWorldProfile Profile)? ExpeditionNature { get; init; }
    internal ExpeditionCivilizationAllocation? ExpeditionCivilization { get; init; }
    // All WLD1 schedules establish deployment before accepting protected construction.
    private WorldGenerationPlan WithHeroDeployment(HeroDeploymentSettings settings)
    {
        settings.ValidateDimensions(Window.Width, Window.Height);
        var steps = Steps.ToList();
        var index = steps.FindIndex(s => s.Kind is WorldStepKind.RouteNetwork or WorldStepKind.Civilization or WorldStepKind.PostEnrichmentAnalysis);
        if (index <= 0) throw new ArgumentException("Deployment needs completed natural terrain/enrichment before construction.");
        var previous = steps[index - 1].Id;
        for (var i = index; i < steps.Count; i++)
            steps[i] = steps[i] with { DependsOn = steps[i].DependsOn.Select(id => id == previous ? "hero-deployment" : id).ToArray() };
        steps.Insert(index, new("hero-deployment", WorldStepKind.HeroDeployment, new DeploymentStepParameters(settings), [previous]));
        var finalization = steps.FindIndex(s => s.Kind == WorldStepKind.PostEnrichmentAnalysis);
        if (finalization < 0) throw new ArgumentException("Deployment requires final analysis.");
        previous = steps[finalization - 1].Id;
        steps[finalization] = steps[finalization] with { DependsOn = steps[finalization].DependsOn.Append("mandatory-connectivity").Distinct().ToArray() };
        steps.Insert(finalization, new("mandatory-connectivity", WorldStepKind.MandatoryConnectivity,
            new MandatoryConnectivityStepParameters(new()), [previous]));
        return (this with { Steps = steps }).Validate();
    }
    public static WorldGenerationPlan FromRecipe(WorldGenerationRecipe recipe)
    {
        recipe = recipe.Complete();
        List<WorldGenerationStep> steps =
        [new("natural", WorldStepKind.NaturalTerrain, new NaturalStepParameters(recipe.NaturalTerrain), []),
         new("formations", WorldStepKind.TerrainAnalysis, new AnalysisStepParameters(), ["natural"])];
        var last = "formations";
        if (recipe.Enrichment is { Enabled: true, Forest: true } e)
        {
            steps.Add(new("forest", WorldStepKind.ForestEnrichment, new EnrichmentStepParameters(e.Intensity), [last])); last = "forest";
        }
        if (recipe.Enrichment is { Enabled: true, Rock: true } r)
        {
            steps.Add(new("rock", WorldStepKind.RockEnrichment, new EnrichmentStepParameters(r.Intensity), [last])); last = "rock";
        }
        if (recipe.Enrichment is { Enabled: true, Water: true } water)
        {
            steps.Add(new("water", WorldStepKind.WaterEnrichment, new EnrichmentStepParameters(water.Intensity), [last])); last = "water";
        }
        if (recipe.Enrichment is { Enabled: true, Grassland: true } grass)
        {
            steps.Add(new("grassland", WorldStepKind.GrasslandEnrichment, new EnrichmentStepParameters(grass.Intensity), [last])); last = "grassland";
        }
        if (recipe.Routes is { Enabled: true, ThroughRoutes: not ContentDensity.None } routes)
        {
            steps.Add(new("primary-routes", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.PrimaryRoutes, routes), [last])); last = "primary-routes";
        }
        if (recipe.Civilization is { Enabled: true } civilization)
        {
            steps.Add(new("civilization", WorldStepKind.Civilization, new CivilizationStepParameters(civilization), [last])); last = "civilization";
        }
        if (recipe.Routes is { Enabled: true, ConnectSites: true } connections && recipe.Civilization is { Enabled: true })
        {
            steps.Add(new("site-connections", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.SiteConnections, connections), [last])); last = "site-connections";
        }
        if (recipe.Routes is { Enabled: true, Alternatives: not ContentDensity.None } links)
        {
            steps.Add(new("exploration-links", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, links), [last])); last = "exploration-links";
        }
        steps.Add(new("current", WorldStepKind.PostEnrichmentAnalysis, new AnalysisStepParameters(), [last]));
        if (recipe.Population is { Density: not PopulationDensity.Off } population)
            steps.Add(new("population", WorldStepKind.Population, new PopulationStepParameters(population), ["current"]));
        return new WorldGenerationPlan(recipe.World, recipe.Window, steps) { LootPreset = recipe.LootPreset, LootAbundance = recipe.LootAbundance }
            .WithHeroDeployment(new(recipe.HeroCapacity));
    }

    public WorldGenerationPlan Validate()
    {
        World.Validate(); Window.Validate(); _ = PopulationLoot.FromPreset(LootPreset);
        if (!Enum.IsDefined(LootAbundance)) throw new ArgumentException("Unknown Loot abundance.");
        if (Steps is null || Steps.Count is < 2 or > 24) throw new ArgumentException("A world plan needs 2..24 steps.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<WorldStepKind>> ancestry = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> ancestorIds = new(StringComparer.Ordinal);
        string? lastTerrainStep = null, lastPostAnalysisTerrain = null, lastPostAnalysisId = null;
        bool natural = false, analyzed = false, civilization = false, boardPlanning = false, deployment = false, repaired = false;
        foreach (var step in Steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.Id) || step.Id.Length > 48 ||
                !step.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || seen.Contains(step.Id))
                throw new ArgumentException("World Step IDs must be unique, nonempty ASCII identifiers of at most 48 characters.");
            if (step.DependsOn is null || step.DependsOn.Distinct(StringComparer.Ordinal).Count() != step.DependsOn.Count ||
                step.DependsOn.Any(id => !seen.Contains(id))) throw new ArgumentException("Step dependencies must name distinct earlier steps.");
            if (!Enum.IsDefined(step.Kind)) throw new ArgumentException("Unknown world step.");
            var ancestors = step.DependsOn.SelectMany(id => ancestry[id]).ToHashSet();
            var ids = step.DependsOn.Concat(step.DependsOn.SelectMany(id => ancestorIds[id])).ToHashSet(StringComparer.Ordinal);
            if (lastTerrainStep is not null && !ids.Contains(lastTerrainStep))
                throw new ArgumentException("A step consuming current terrain must depend on the preceding terrain-writing step.");
            switch (step.Kind)
            {
                case WorldStepKind.NaturalTerrain:
                    if (natural || seen.Count != 0 || step.Parameters is not NaturalStepParameters p || p.Settings != World.NaturalTerrain)
                        throw new ArgumentException("Natural Terrain must run once, first, with the world's settings.");
                    natural = true; break;
                case WorldStepKind.TerrainAnalysis:
                    if (!natural || analyzed || step.Parameters is not AnalysisStepParameters || !ancestors.Contains(WorldStepKind.NaturalTerrain))
                        throw new ArgumentException("Original analysis must run once after Natural Terrain.");
                    analyzed = true; break;
                case WorldStepKind.PostEnrichmentAnalysis:
                    if (!analyzed || !repaired || step.Parameters is not AnalysisStepParameters || !ancestors.Contains(WorldStepKind.MandatoryConnectivity))
                        throw new ArgumentException("Final analysis requires completed mandatory connectivity.");
                    lastPostAnalysisTerrain = lastTerrainStep; lastPostAnalysisId = step.Id; break;
                case WorldStepKind.Population:
                    if (step.Parameters is not PopulationStepParameters pp || !ancestors.Contains(WorldStepKind.PostEnrichmentAnalysis) ||
                        seen.Count != Steps.Count - 1 || lastPostAnalysisTerrain != lastTerrainStep || lastPostAnalysisId is null || !ids.Contains(lastPostAnalysisId)) throw new ArgumentException("Population runs last after final post analysis.");
                    pp.Settings.Validate(); break;
                case WorldStepKind.Civilization:
                    if (!analyzed || !deployment || civilization || repaired || step.Parameters is not CivilizationStepParameters cp || !ancestors.Contains(WorldStepKind.HeroDeployment))
                        throw new ArgumentException("Civilization runs once after original analysis.");
                    cp.Settings.Validate(); civilization = true; boardPlanning = true; break;
                case WorldStepKind.HeroDeployment:
                    if (!analyzed || deployment || boardPlanning || step.Parameters is not DeploymentStepParameters dp ||
                        !ancestors.Contains(WorldStepKind.TerrainAnalysis))
                        throw new ArgumentException("Deployment runs once after enrichment and before protected construction.");
                    dp.Settings.ValidateDimensions(Window.Width, Window.Height);
                    deployment = true; boardPlanning = true; break;
                case WorldStepKind.RouteNetwork:
                    if (!analyzed || !deployment || repaired || step.Parameters is not RouteNetworkStepParameters rp || !Enum.IsDefined(rp.Mode) || !ancestors.Contains(WorldStepKind.HeroDeployment))
                        throw new ArgumentException("Routes require original analysis and an explicit supported mode.");
                    rp.Settings.Validate(); boardPlanning = true; break;
                case WorldStepKind.MandatoryConnectivity:
                    if (!deployment || repaired || step.Parameters is not MandatoryConnectivityStepParameters ||
                        !ancestors.Contains(WorldStepKind.HeroDeployment))
                        throw new ArgumentException("Mandatory connectivity runs once after deployment and all protected construction.");
                    repaired = true; break;
                default:
                    if (!analyzed || boardPlanning || step.Parameters is not EnrichmentStepParameters { Intensity: >= 1 and <= 3 } || !ancestors.Contains(WorldStepKind.TerrainAnalysis))
                        throw new ArgumentException("Enrichment requires original analysis and intensity 1..3.");
                    break;
            }
            seen.Add(step.Id);
            ancestors.Add(step.Kind); ancestry.Add(step.Id, ancestors);
            ancestorIds.Add(step.Id, ids);
            if (step.Kind == WorldStepKind.NaturalTerrain || step.Parameters is EnrichmentStepParameters or CivilizationStepParameters or RouteNetworkStepParameters or DeploymentStepParameters or MandatoryConnectivityStepParameters) lastTerrainStep = step.Id;
        }
        if (!analyzed) throw new ArgumentException("World plans require original formation analysis.");
        if (!deployment || !repaired || lastPostAnalysisId is null || lastPostAnalysisTerrain != lastTerrainStep)
            throw new ArgumentException("WLD1 requires deployment, mandatory connectivity and final geographic validation.");
        return this;
    }
}

public sealed record WorldStageMeasurement(string StepId, WorldStepKind Stage, double Milliseconds, long AllocatedBytes);
public sealed record WorldGenerationMetrics(IReadOnlyList<WorldStageMeasurement> Stages, double FinalizationMilliseconds, long RetainedPreviewBytes)
{
    public double SamplingMilliseconds => Stages.Where(s => s.Stage == WorldStepKind.NaturalTerrain).Sum(s => s.Milliseconds);
    public double ConstructionValidationMilliseconds { get; init; }
    public int ConstructionValidations { get; init; }
    public double RouteSearchMilliseconds { get; init; }
    public double RouteMaterializationMilliseconds { get; init; }
}
public sealed record WorldStageDiagnostics(int ChangedCells, int FeaturesAdded, int WalkableArea, int ComponentCount, int LargestComponentArea,
    int RoutingSearches, int RoutingVisitedCells);
public sealed record WorldPreviewFrame(string StepId, string Stage, string Terrain, int[] ChangedCells, int[] WalkableComponents,
    int[] Chokepoints, IReadOnlyList<EntranceCandidate> Entrances, IReadOnlyList<TerrainOpportunity> Opportunities, WorldStageDiagnostics Diagnostics)
{
    public IReadOnlyList<EdgeDefinition> Edges { get; init; } = [];
    public IReadOnlyList<EdgeChange> ChangedEdges { get; init; } = [];
    public IReadOnlyList<ConstructionOperation> Construction { get; init; } = [];
    public IReadOnlyList<SiteInfo> Sites { get; init; } = [];
    public IReadOnlyList<PlacementFailure> PlanningFailures { get; init; } = [];
    public CivilizationStatistics? CivilizationStats { get; init; }
    public DensityAllocation? CampsiteDensity { get; init; }
    public DensityAllocation? SettlementDensity { get; init; }
    public DensityAllocation? ThroughRouteDensity { get; init; }
    public DensityAllocation? ExplorationLinkDensity { get; init; }
    public IReadOnlyList<ConstructionBatch> ConstructionBatches { get; init; } = [];
    public RouteNetworkSnapshot? RouteNetwork { get; init; }
    public PopulationSnapshot? Population { get; init; }
    public GeometryAccessibilityAudit? GeometryAccessibility { get; init; }
    public PopulationAccessibilityAudit? UnitAccessibility { get; init; }
    public IReadOnlyList<Machine> Machines { get; init; } = [];
    public IReadOnlyList<LootMarker> SiteLootMarkers { get; init; } = [];
    public SiteLootStatistics? SiteLootStatistics { get; init; }
}
public sealed record WorldPreview(IReadOnlyList<string> TerrainPalette, IReadOnlyList<WorldPreviewFrame> Frames,
    IReadOnlyList<NaturalFormation> Formations, int[] FormationLabels, int[] BoundaryCells)
{
    // Raw retained data estimate, excluding CLR/browser object overhead; measured payload is reported separately.
    public long EstimatedRetainedBytes => Frames.Sum(f => (long)f.Terrain.Length * 2 +
        4L * (f.ChangedCells.Length + f.WalkableComponents.Length + f.Chokepoints.Length) +
        f.Entrances.Count * 96L + f.Opportunities.Count * 192L + f.Edges.Count * 32L + f.ChangedEdges.Count * 32L + f.Machines.Count * 256L) +
        Frames.SelectMany(f => f.Construction).DistinctBy(o => o.Id).Sum(o => 256L + o.TerrainChanges.Count * 64L + o.EdgeChanges.Count * 32L) +
        Frames.SelectMany(f => f.Sites).DistinctBy(s => s.Id).Sum(s => 512L + s.Footprint.Spans.Count * 12L + s.Exterior.Spans.Count * 12L +
            s.Buildings.Sum(b => 256L + b.Footprint.Spans.Count * 12L + b.Rooms.Sum(r => 192L + (r.Interior.Spans.Count + r.Usable.Spans.Count) * 12L)) +
            (s.PopulationIntent is { } intent ? 128L + intent.PatrolPath.Count * 4L + intent.Areas.Sum(a => 128L + a.Area.Spans.Count * 12L) +
                intent.EncounterRegions.Sum(r => 128L + r.Area.Spans.Count * 12L + r.PatrolPath.Count * 4L + r.PreferredUnitTypes.Sum(t => t.Length * 2L)) : 0) +
            (s.MountainDungeon is { } dungeon ? 192L + dungeon.Rooms.Sum(r => 192L + (r.Interior.Spans.Count + r.Usable.Spans.Count) * 12L) +
                new[] { dungeon.Corridors, dungeon.ProtectedRock, dungeon.RequiredAccess, dungeon.Lava }.Concat(dungeon.TreasureAreas).Sum(a => 64L + a.Spans.Count * 12L) : 0) +
            (s.SettlementLayout is { } layout ? 128L + layout.Lots.Count * 128L + layout.Access.Spans.Count * 12L +
                layout.PublicSpaces.Sum(a => 128L + a.Area.Spans.Count * 12L) + layout.FallbackReasons.Sum(r => r.Length * 2L) : 0)) +
        Frames.SelectMany(f => f.RouteNetwork?.Routes ?? []).DistinctBy(r => r.Id).Sum(r => 512L + r.Corridor.Spans.Count * 12L +
            r.Geometry.Runs.Count * 16L + r.Crossings.Sum(c => c.Footprint.Spans.Count * 12L) + r.TunnelEntrances.Count * 32L) +
        Frames.Sum(f => (f.RouteNetwork?.Junctions.Sum(j => 128L + j.Contact.Spans.Count * 12L) ?? 0) +
            (f.RouteNetwork?.SharedSegments.Sum(s => 128L + s.Area.Spans.Count * 12L) ?? 0)) +
        Frames.Where(f => f.Population is not null).Sum(f => 96L + f.Population!.Encounters.Sum(e => 128L +
            2L * (e.Id.Length + e.Kind.Length + e.SourceId.Length + e.GroupIds.Sum(g => g.Length)) +
            e.Units.Sum(u => 96L + 2L * ((u.UnitId?.Length ?? 0) + (u.GroupId?.Length ?? 0) + u.UnitTypeId.Length + u.SideId.Length)) +
            e.Orders.Count * 128L + e.Agency.Count * 64L + e.Routes.Sum(r => 64L + r.Id.Length * 2L + r.WaypointIds.Sum(id => id.Length * 2L)) +
            e.Waypoints.Sum(p => 48L + p.Id.Length * 2L) + e.Roles.Sum(r => 64L + 2L * (r.UnitId.Length + r.Role.Length + (r.AreaId?.Length ?? 0)))) +
            f.Population.AlertComponents.Sum(c => 32L + c.UnitIds.Sum(id => 24L + id.Length * 2L)) +
            f.Population.LootMarkers.Sum(m => 48L + m.Id.Length * 2L) +
            f.Population.Failures.Sum(p => 64L + 2L * (p.RequestId.Length + p.Reason.Length + (p.Kind?.Length ?? 0) + (p.SourceId?.Length ?? 0)))) +
        Frames.Sum(f => f.SiteLootMarkers.Sum(m => 48L + m.Id.Length * 2L)) +
        4L * (FormationLabels.Length + BoundaryCells.Length) + Formations.Count * 256L;
}
public sealed record WorldGenerationExecution(WorldGenerationContext Context, WorldPreview Preview, IReadOnlyList<WorldStageMeasurement> Measurements);

public sealed partial class WorldGenerationContext
{
    internal ExpeditionGeometryPlan? Expedition { get; set; }
    internal ExpeditionCivilizationAllocation? ExpeditionCivilization { get; init; }
    internal HeroDeployment? Deployment { get; set; }
    internal CompletionGuard? Completion { get; set; }
    internal MandatoryRepairReport? MandatoryRepair { get; set; }
    internal ScenarioSetup? PublishedSetup { get; set; }
    public NaturalTerrainSample[] OriginalSamples { get; private set; } = [];
    public TerrainAnalysisSnapshot? OriginalAnalysis { get; internal set; }
    public TerrainAnalysisSnapshot? CurrentAnalysis { get; internal set; }
    public List<EnrichmentFeature> Features { get; } = [];
    public List<EntranceCandidate> Entrances { get; } = [];
    public List<TerrainOpportunity> FeatureOpportunities { get; } = [];
    public int Revision { get; internal set; }
    public int RoutingSearches { get; internal set; }
    public int RoutingVisitedCells { get; internal set; }
    public GeometryAccessibilityAudit? GeometryAccessibility { get; internal set; }
    public PopulationAccessibilityAudit? UnitAccessibility { get; internal set; }
}

public static class WorldStepSeed
{
    public static ulong Derive(ulong root, string stepId, WorldStepKind kind, string role = "step")
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"WLD1:Enrichment1:{root:X16}:{stepId}:{kind}:{role}")));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
    public static Pcg32 Stream(ulong seed, string role)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"WLD1:Feature1:{seed:X16}:{role}")));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash), BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8)));
    }
}

public static class WorldGenerationPipeline
{
    public static WorldGenerationExecution Execute(WorldGenerationPlan plan, ContentBundle? content = null)
        => Execute(plan, content, null);

    internal static WorldGenerationExecution Execute(WorldGenerationPlan plan, ContentBundle? content, ExpeditionGeometryPlan? expedition,
        Action<string>? beforeStage = null, ExpeditionNatureSampler? nature = null)
    {
        plan.Validate(); content ??= ContentBundle.Canonical();
        var context = new WorldGenerationContext(plan.World, plan.Window)
        {
            Loot = PopulationLoot.FromPreset(plan.LootPreset), LootAbundance = plan.LootAbundance, Expedition = expedition,
            ExpeditionCivilization = plan.ExpeditionCivilization
        };
        List<WorldPreviewFrame> frames = []; List<WorldStageMeasurement> measurements = [];
        List<string> palette = ["grass", "dirt", "tree", "solid-rock", "water", "stone-floor"];
        var paletteIndex = palette.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.Ordinal);
        string? previous = null; Dictionary<EdgeKey, EdgeKind> previousEdges = [];
        foreach (var step in plan.Steps)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            var featureStart = context.Features.Count; var searches = context.RoutingSearches; var visited = context.RoutingVisitedCells;
            var operationStart = context.Construction.Count;
            try
            {
            beforeStage?.Invoke(step.Id);
            switch (step.Kind)
            {
                case WorldStepKind.NaturalTerrain:
                    if (nature is null) context.SampleNaturalTerrain(content);
                    else context.SampleExpeditionNature(nature, content);
                    break;
                case WorldStepKind.TerrainAnalysis:
                    context.OriginalAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.OriginalSamples, content);
                    context.CurrentAnalysis = context.OriginalAnalysis; break;
                case WorldStepKind.PostEnrichmentAnalysis:
                    context.CurrentAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.Samples, context.ConstructionBoard?.Content ?? content, context.Revision, context.ConstructionEdges(), context.ConstructionBoard?.State.Machines);
                    DungeonSitePopulation.FinalizeGeometry(context);
                    if (context.Completion is { } geometryCompletion)
                    {
                        var registration = geometryCompletion.RegisterInteractives(context.Materialize(), step.Id);
                        if (registration.Status != CompletionStatus.Allowed)
                            throw new PlayReadyGenerationException(plan, step.Id, context.ConstructionBoard!.Revision,
                                $"Completion registration {registration.Status}: {registration.Detail}", context.MandatoryRepair, registration.Failures,
                                completion: registration);
                    }
                    context.GeometryAccessibility = WorldAccessibilityAudit.Geometry(context, context.ConstructionBoard?.Content ?? content); break;
                case WorldStepKind.ForestEnrichment: ForestEnricher.Apply(context, step, content); break;
                case WorldStepKind.RockEnrichment: RockEnricher.Apply(context, step, content); break;
                case WorldStepKind.WaterEnrichment: WaterEnricher.Apply(context, step, content); break;
                case WorldStepKind.GrasslandEnrichment: GrasslandEnricher.Apply(context, step, content); break;
                case WorldStepKind.HeroDeployment:
                    var deploymentBoard = context.EnsurePlanningBoard(content);
                    var deploymentSettings = ((DeploymentStepParameters)step.Parameters).Settings;
                    expedition?.PrepareEndpoints(deploymentBoard);
                    var deploymentResult = HeroDeploymentPlanner.Establish(deploymentBoard, deploymentSettings, expedition?.LocalSeed ?? context.World.Seed,
                        HeroDeploymentPlanner.NaturalConversions(deploymentBoard, Enumerable.Range(0, context.Samples.Length)), expedition?.DeploymentDomain);
                    if (deploymentResult.Status != HeroDeploymentStatus.Established)
                        throw new PlayReadyGenerationException(plan, step.Id, deploymentBoard.Revision,
                            $"Deployment {deploymentResult.Status}; capacity {deploymentSettings.Capacity}; work {deploymentResult.WorkUsed}: {deploymentResult.Detail}",
                            kind: deploymentResult.Status == HeroDeploymentStatus.Incomplete ? WorldGenerationFailureKind.IncompleteAnalysis :
                                deploymentResult.Status == HeroDeploymentStatus.InsufficientSpace ? WorldGenerationFailureKind.InsufficientDeployment : WorldGenerationFailureKind.ConstructionInvariantViolation);
                    context.Deployment = deploymentResult.Deployment!;
                    expedition?.ProtectEndpoints(deploymentBoard);
                    if (expedition?.StationsEnabled == true)
                        ExpeditionStationGenerator.EstablishArrival(context, deploymentBoard);
                    context.Completion = new(deploymentBoard, context.Deployment, Enumerable.Range(0, context.Samples.Length),
                        initialLocations: expedition?.RequiredLocations, expeditionEndpoints: expedition?.EndpointIndices.ToHashSet());
                    if (expedition is not null)
                        context.PopulationProtectedAreas.Add(GeneratedArea.From("expedition/endpoints", expedition.EndpointIndices, deploymentBoard.Width));
                    context.PopulationProtectedAreas.Add(context.Deployment.Area);
                    context.PopulationProtectedAreas.Add(GeneratedArea.From("hero-deployment/departure", [context.Deployment.DepartureIndex], deploymentBoard.Width));
                    context.Construction.Add(new("hero-deployment", null, "Hero deployment", "hero-deployment",
                        context.Deployment.Area.Bounds, deploymentResult.Preparation.Select(c => new TerrainChange(c.Index, c.Before, c.After)).ToArray(), []));
                    context.RefreshConstructionAnalysis();
                    break;
                case WorldStepKind.MandatoryConnectivity:
                    var repairBoard = context.EnsurePlanningBoard(content);
                    var report = MandatoryConnectivityRepair.Run(repairBoard, context.Completion!,
                        ((MandatoryConnectivityStepParameters)step.Parameters).Settings);
                    context.MandatoryRepair = report;
                    if (!report.Success)
                        throw new PlayReadyGenerationException(plan, step.Id, repairBoard.Revision,
                            $"Mandatory connectivity {report.Status}.", report);
                    context.Completion!.MarkActualConnectivityEstablished();
                    if (expedition?.StationsEnabled == true)
                        ExpeditionStationGenerator.PlaceAdditional(context, step, repairBoard);
                    context.RefreshConstructionAnalysis();
                    break;
                case WorldStepKind.Population:
                    PopulationGenerator.Apply(context, step, content);
                    if (context.Completion is { } populationCompletion)
                    {
                        var registration = populationCompletion.RegisterInteractives(context.Materialize(), step.Id);
                        if (registration.Status != CompletionStatus.Allowed)
                            throw new PlayReadyGenerationException(plan, step.Id, context.ConstructionBoard!.Revision,
                                $"Completion registration {registration.Status}: {registration.Detail}", context.MandatoryRepair, registration.Failures,
                                completion: registration);
                    }
                    context.UnitAccessibility = WorldAccessibilityAudit.Population(context, GameEngine.CreateGame(context.Materialize(), content));
                    break;
                case WorldStepKind.Civilization:
                    CivilizationGenerator.Apply(context, step, content);
                    DungeonSitePopulation.PublishIntents(context);
                    if (context.RouteNetwork is not null) context.RouteNetwork = RouteTopologyBuilder.Build(context);
                    break;
                case WorldStepKind.RouteNetwork: RouteNetworkGenerator.Apply(context, step, content); break;
                default: throw new ArgumentException($"Step {step.Kind} is not available.");
            }
            foreach (var id in context.Samples.Select(s => s.TerrainTypeId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                if (!paletteIndex.ContainsKey(id)) { paletteIndex.Add(id, palette.Count); palette.Add(id); }
            var terrain = new string(context.Samples.Select(s => (char)('A' + paletteIndex[s.TerrainTypeId])).ToArray());
            var changed = previous is null ? [] : Enumerable.Range(0, terrain.Length).Where(i => terrain[i] != previous[i]).ToArray();
            if (step.Parameters is EnrichmentStepParameters)
            {
                if (changed.Length > 0) context.Revision++;
                context.CurrentAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.Samples, content, context.Revision);
            }
            var analysis = context.CurrentAnalysis;
            if (context.Construction.Count > operationStart)
                context.ConstructionBatches.Add(new(step.Id, frames[^1].StepId, operationStart, context.Construction.Count));
            for (var opportunityIndex = 0; opportunityIndex < context.FeatureOpportunities.Count; opportunityIndex++)
            {
                var o = context.FeatureOpportunities[opportunityIndex];
                context.FeatureOpportunities[opportunityIndex] = o with
                {
                    WalkableComponent = analysis?.WalkableLabels[o.AnchorIndex] ?? -1,
                    RequiresConnection = o.RequiresConnection || analysis?.WalkableLabels[o.AnchorIndex] < 0
                };
            }
            var opportunities = (analysis?.Opportunities ?? []).Concat(context.FeatureOpportunities).ToArray();
            var connectivity = analysis?.Connectivity;
            var edges = context.ConstructionEdges();
            var edgeMap = edges.ToDictionary(e => new EdgeKey(e.Position.Y * plan.Window.Width + e.Position.X, e.Direction), e => e.Kind);
            var changedEdges = previousEdges.Keys.Concat(edgeMap.Keys).Distinct().OrderBy(k => k.Index).ThenBy(k => k.Direction)
                .Where(k => previousEdges.GetValueOrDefault(k, EdgeKind.None) != edgeMap.GetValueOrDefault(k, EdgeKind.None))
                .Select(k => new EdgeChange(k, previousEdges.GetValueOrDefault(k, EdgeKind.None), edgeMap.GetValueOrDefault(k, EdgeKind.None))).ToArray();
            frames.Add(new(step.Id, step.Parameters is RouteNetworkStepParameters routeParameters ? routeParameters.Mode.ToString() : step.Kind.ToString(), terrain, changed, analysis?.WalkableLabels ?? [], analysis?.Chokepoints ?? [],
                context.Entrances.ToArray(), opportunities,
                new(changed.Length, context.Features.Count - featureStart, connectivity?.WalkableArea ?? 0, connectivity?.ComponentCount ?? 0,
                    connectivity?.LargestComponentArea ?? 0, context.RoutingSearches - searches, context.RoutingVisitedCells - visited))
            {
                Edges = edges, ChangedEdges = changedEdges, Sites = context.Sites.ToArray(), Construction = context.Construction.ToArray(),
                PlanningFailures = context.PlanningFailures.ToArray(), CivilizationStats = context.CivilizationStats,
                CampsiteDensity = context.CampsiteDensity, SettlementDensity = context.SettlementDensity,
                ThroughRouteDensity = context.ThroughRouteDensity, ExplorationLinkDensity = context.ExplorationLinkDensity,
                ConstructionBatches = context.ConstructionBatches.ToArray(), RouteNetwork = context.RouteNetwork, Population = context.Population,
                GeometryAccessibility = context.GeometryAccessibility, UnitAccessibility = context.UnitAccessibility,
                Machines = context.ConstructionBoard?.State.Machines.ToArray() ?? [], SiteLootMarkers = context.SiteLootMarkers.ToArray()
            });
            previous = terrain; previousEdges = edgeMap;
            measurements.Add(new(step.Id, step.Kind, timer.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated));
            }
            catch (Exception error) when (error is not PlayReadyGenerationException)
            {
                throw new PlayReadyGenerationException(plan, step.Id, context.ConstructionBoard?.Revision ?? context.Revision,
                    error.Message, context.MandatoryRepair,
                    kind: error is ArgumentException ? WorldGenerationFailureKind.ConstructionInvariantViolation : WorldGenerationFailureKind.UnexpectedImplementationFailure,
                    inner: error);
            }
        }
        var original = context.OriginalAnalysis!;
        // The no-Population path still publishes a completed empty instance audit.
        if (context.UnitAccessibility is null)
        {
            context.UnitAccessibility = new([], true, 0);
            frames[^1] = frames[^1] with { UnitAccessibility = context.UnitAccessibility };
        }
        // Analysis is read-only, so its overlay also describes the preceding natural frame.
        frames[0] = frames[0] with
        {
            WalkableComponents = original.WalkableLabels, Chokepoints = original.Chokepoints, Opportunities = original.Opportunities,
            Diagnostics = frames[0].Diagnostics with { WalkableArea = original.Connectivity.WalkableArea,
                ComponentCount = original.Connectivity.ComponentCount, LargestComponentArea = original.Connectivity.LargestComponentArea }
        };
        var finalStage = "site-loot";
        try
        {
            // Nonblocking exploration rewards use final population occupancy, including when Population is Off.
            beforeStage?.Invoke(finalStage);
            SiteLootGenerator.Generate(context, context.ConstructionBoard?.Content ?? content);
            SitePopulationAllocation.FinalizeContent(context);
            var lootRegistration = context.Completion!.RegisterInteractives(context.Materialize(), "site-loot");
            if (lootRegistration.Status != CompletionStatus.Allowed)
                throw new PlayReadyGenerationException(plan, "site-loot", context.ConstructionBoard!.Revision,
                    $"Completion registration {lootRegistration.Status}: {lootRegistration.Detail}", context.MandatoryRepair,
                    lootRegistration.Failures, completion: lootRegistration);
            finalStage = "expedition-economy";
            beforeStage?.Invoke(finalStage);
            ExpeditionEconomyGenerator.Place(context);
            if (context.Expedition?.CreditsSettings is not null)
            {
                finalStage = "expedition-credits";
                beforeStage?.Invoke(finalStage);
                ExpeditionCreditsGenerator.Place(context);
            }
            frames[^1] = frames[^1] with { SiteLootMarkers = context.SiteLootMarkers.ToArray(),
                SiteLootStatistics = context.SiteLootStatistics, Population = context.Population,
                UnitAccessibility = context.UnitAccessibility,
                Machines = context.ConstructionBoard?.State.Machines.ToArray() ?? [] };
            finalStage = "final-materialization";
            context.PublishedSetup = PlayReadyScenarioSetup.Publish(context);
            var finalDefinition = context.Materialize();
            var finalState = GameEngine.CreateGame(finalDefinition, content);
            var outpostDoors = ExpeditionGeometryPlan.OutpostDoorCells(context.Sites).ToArray();
            if (expedition is not null && !expedition.HasOccupiedAccess(finalState, context.Deployment!, outpostDoors))
                throw new PlayReadyGenerationException(plan, "final-materialization", context.ConstructionBoard!.Revision,
                    expedition.StationsEnabled ? "Initial population disconnects required Expedition deployment, seam, exit-pad, Door or Station operating access."
                        : outpostDoors.Length == 0 ? "Initial population disconnects ordinary Expedition deployment, seam or exit-pad access."
                        : "Initial population disconnects required Expedition access, including the outpost gate or guardhouse Door.",
                    context.MandatoryRepair);
            var validation = PlayReadyScenarioSetup.Validate(finalDefinition, finalState);
            var failures = context.Completion!.ActualFailures(finalState);
            if (!validation.Valid || !failures.IsEmpty)
                throw new PlayReadyGenerationException(plan, "final-materialization", context.ConstructionBoard!.Revision,
                    string.Join("; ", validation.Diagnostics.Select(d => d.Message)), context.MandatoryRepair, failures);
        }
        catch (Exception error) when (error is not PlayReadyGenerationException)
        {
            throw new PlayReadyGenerationException(plan, finalStage, context.ConstructionBoard?.Revision ?? context.Revision,
                error.Message, context.MandatoryRepair,
                kind: finalStage == "site-loot" ? WorldGenerationFailureKind.UnexpectedImplementationFailure : WorldGenerationFailureKind.FailedFinalValidation,
                inner: error);
        }
        return new(context, new(palette, frames, original.Formations, original.FormationLabels,
            Enumerable.Range(0, original.Boundary.Length).Where(i => original.Boundary[i]).ToArray()), measurements);
    }

}
