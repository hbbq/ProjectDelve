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
    public int DungeonsRequested { get; init; }
    public int DungeonsAccepted { get; init; }
    public int DungeonsOmitted => DungeonsRequested - DungeonsAccepted;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SiteConstructionAllocation>? ConstructionProfiles { get; init; }
}

public sealed record SiteConstructionAllocation(SiteConstructionProfile Profile, int Requested, int Accepted)
{
    public int Omitted => Requested - Accepted;
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
        var allocation = context.ExpeditionCivilization;
        var variedConstruction = allocation?.ConstructionVersion is
            ExpeditionWorldProfile.ConstructionV1 or ExpeditionWorldProfile.ConstructionV2 or ExpeditionWorldProfile.ConstructionV3;
        var settlementTarget = allocation?.SettlementTarget ?? DensityScaling.Settlements(context.Window, settings.Settlements);
        var campsiteTarget = allocation?.CampsiteTarget ?? DensityScaling.Campsites(context.Window, settings.Campsites);
        var dungeonTarget = allocation?.DungeonTarget ?? MountainDungeonGenerator.Target(context.Window, settings);
        var requestedProfiles = new Dictionary<SiteConstructionProfile, int>();
        var centerpieces = variedConstruction
            ? board.Content.Terrain.Where(SettlementGenerator.IsVillageCenterpiece)
                .Select(t => t.Id).Order(StringComparer.Ordinal).ToArray()
            : [];
        var usedRock = new HashSet<int>();
        for (var i = 0; i < dungeonTarget; i++)
        {
            var key = $"mountain-dungeon-{i}";
            HashSet<int>? submittedMass = null;
            if (Place(key, scope =>
            {
                var result = MountainDungeonGenerator.Plan(new(key, area, policy, ExcludedRockCells: usedRock,
                    OriginalAnalysis: context.OriginalAnalysis, OriginalSamples: context.OriginalSamples), scope);
                // Capture before excavation; only an accepted candidate consumes its original mass.
                if (result is Planned<SitePlan> planned && planned.Plan.Site.MountainDungeon is { } dungeon)
                {
                    var q = new Queue<int>(); submittedMass = [dungeon.RockMassAnchor]; q.Enqueue(dungeon.RockMassAnchor);
                    while (q.TryDequeue(out var cell)) foreach (var next in board.Neighbors(cell))
                        if (board.TerrainAt(next) == "solid-rock" && submittedMass.Add(next)) q.Enqueue(next);
                }
                return result;
            }) && submittedMass is not null) usedRock.UnionWith(submittedMass);
        }
        for (var i = 0; i < settlementTarget; i++)
        {
            var key = $"settlement-{i}";
            var profile = variedConstruction ? SettlementProfile(ProfileRandom(key).Next(12), settlementTarget) : null;
            CountRequest(profile);
            var centerpieceRandom = variedConstruction ? ProfileRandom(key, "centerpiece") : null;
            var centerpiece = profile == SiteConstructionProfile.VillageCenter && centerpieces.Length > 0 && centerpieceRandom!.Next(3) != 0
                ? centerpieces[centerpieceRandom.Next(centerpieces.Length)] : null;
            Place(key, scope =>
            {
                var rng = scope.Child(key, scope.Draft).Random("requirements");
                var routeAnchors = board.State.RouteCorridors.Keys.Order().ToArray();
                var preferences = new SitePlacementPreferences(RouteAnchors: routeAnchors);
                if (allocation?.Hub is { } hub)
                    preferences = preferences with { PreferredAnchor = hub.Y * board.Width + hub.X, AnchorWeight = allocation.AnchorWeight };
                if (profile == SiteConstructionProfile.Cottage)
                    return CottageGenerator.Plan(new(key, area, policy, Furniture: rng.Next(3) != 0,
                        Preferences: settlementTarget > 1 ? preferences : new(RouteAnchors: routeAnchors)), scope);
                var request = new SettlementRequest(key, area, policy,
                    profile == SiteConstructionProfile.VillageCenter
                        ? new(BuildingCount: 3 + rng.Next(2), AllowReducedBuildingCount: true)
                            { VillageCenter = true, CenterpieceTypeId = centerpiece }
                        : new(BuildingCount: 2 + rng.Next(3), AllowReducedBuildingCount: true), preferences);
                if (allocation is { Hub: { } center, AnchorWeight: >= 8 })
                {
                    // Try all available families near a strong hub before searching the whole board.
                    // Both passes share the existing work budget and publish only through Place.
                    var neighborhood = HubNeighborhood(area, center, allocation.AnchorWeight);
                    if (neighborhood != area)
                    {
                        var local = SettlementGenerator.Plan(request with { Area = neighborhood }, scope);
                        if (local is Planned<SitePlan> || local is Rejected<SitePlan>
                            { Failure.Code: PlacementFailureCode.BudgetExhausted or PlacementFailureCode.InvalidContent or PlacementFailureCode.ValidationFailed })
                            return local;
                    }
                }
                return SettlementGenerator.Plan(request, scope);
            });
        }
        for (var i = 0; i < campsiteTarget; i++)
        {
            var key = $"campsite-{i}";
            var profile = variedConstruction ? ProfileRandom(key).Next(4) switch
            {
                1 => SiteConstructionProfile.OpenCamp,
                2 => SiteConstructionProfile.ShelteredCamp,
                3 => SiteConstructionProfile.PrimitiveHut,
                _ => (SiteConstructionProfile?)null
            } : null;
            CountRequest(profile);
            Place(key, scope =>
            {
                var anchors = (context.CurrentAnalysis?.Opportunities ?? []).Concat(context.FeatureOpportunities)
                    .Where(o => o.Kind is "clearing" or "open-area").OrderByDescending(o => o.UsableArea)
                    .Select(o => o.AnchorIndex).Distinct().Take(16).ToArray();
                var routeAnchors = board.State.RouteCorridors.Keys.Order().ToArray();
                var requirements = new CampsiteRequirements(Radius: 2 + scope.Child(key, scope.Draft).Random("requirements").Next(3))
                { ConstructionProfile = profile };
                return CampsiteGenerator.Plan(new(key, area, policy, Requirements: requirements,
                    Preferences: new(OpportunityAnchors: anchors, RouteAnchors: routeAnchors)), scope);
            });
        }
        // Separate stream and allowance keep Machine placement independent of Site retries.
        var beacons = BeaconGenerator.Apply(context, step, board, area, path);
        if (allocation?.Remains is { } remains)
        {
            var routes = board.State.RouteCorridors.Keys.Order().ToArray();
            SitePlacementPreferences Preferences(Cell anchor) =>
                new(anchor.Y * board.Width + anchor.X, RouteAnchors: routes) { AnchorWeight = 4 };

            if (remains is { Ruin: { } ruin, RuinAnchor: { } ruinAnchor })
            {
                CountRequest(ruin);
                Place("ruin-0", scope => RuinGenerator.Plan(new("ruin-0", area, policy, ruin, Preferences(ruinAnchor)), scope));
            }
            if (remains.LandmarkAnchor is { } landmarkAnchor)
            {
                const string key = "landmark-0";
                var profileStream = WorldStepSeed.Stream(context.World.Seed, ExpeditionWorldProfile.ConstructionV2 + "/" + path + "/landmark-style");
                var profile = routes.Length > 0 && profileStream.Next(2) == 0
                    ? SiteConstructionProfile.RoadsideMarker : SiteConstructionProfile.IsolatedLandmark;
                CountRequest(profile);
                var materials = board.Content.Terrain.Where(LandmarkGenerator.IsLandmark)
                    .OrderBy(terrain => terrain.Id, StringComparer.Ordinal).ToArray();
                if (materials.Length > 0)
                {
                    var material = materials[WorldStepSeed.Stream(context.World.Seed,
                        ExpeditionWorldProfile.ConstructionV2 + "/" + path + "/landmark-material").Next(materials.Length)];
                    Place(key, scope => LandmarkGenerator.Plan(new(key, area, policy, profile, material.Id, Preferences(landmarkAnchor)), scope));
                }
                else
                {
                    const string reason = "The current Content has no eligible Landmark Terrain Assembly.";
                    Failure(new(path + "/" + key, "Landmark", PlacementFailureCode.InvalidContent, reason, 0));
                    context.Completion?.RecordOmission(path + "/" + key, step.Id, reason);
                }
            }
        }
        if (allocation?.Outpost?.Anchor is { } outpostAnchor)
        {
            const string key = "outpost-0";
            CountRequest(SiteConstructionProfile.FortifiedOutpost);
            var preferences = new SitePlacementPreferences(outpostAnchor.Y * board.Width + outpostAnchor.X,
                RouteAnchors: board.State.RouteCorridors.Keys.Order().ToArray()) { AnchorWeight = 4 };
            Place(key, scope => OutpostGenerator.Plan(new(key, area, policy, preferences), scope));
        }
        context.RefreshConstructionAnalysis();
        context.CampsiteDensity = new(campsiteTarget, campsiteTarget, context.Sites.Count(s => s.Kind == GeneratedSiteKind.Campsite));
        context.SettlementDensity = new(settlementTarget, settlementTarget, context.Sites.Count(s => s.Kind == GeneratedSiteKind.Settlement));
        context.CivilizationStats = new(budget.Attempts, budget.Work, context.Sites.Count,
            context.Sites.Count(s => s.Kind == GeneratedSiteKind.Campsite), context.Sites.Count(s => s.Kind == GeneratedSiteKind.Settlement),
            context.Sites.Sum(s => s.Buildings.Count), context.Sites.Sum(s => s.Buildings.Sum(b => b.Rooms.Count) + (s.MountainDungeon?.Rooms.Count ?? 0)),
            context.Sites.Sum(s => s.Preparation.ChangedTerrain) + beacons.ChangedTerrain, context.Sites.Sum(s => s.Preparation.ChangedEdges))
        {
            BeaconsPlaced = beacons.Placed, BeaconsOmitted = beacons.Target - beacons.Placed,
            BeaconAttempts = beacons.Attempts, BeaconWork = beacons.Work,
            DungeonsRequested = dungeonTarget, DungeonsAccepted = context.Sites.Count(s => s.Kind == GeneratedSiteKind.MountainDungeon),
            ConstructionProfiles = variedConstruction ? requestedProfiles.OrderBy(p => p.Key)
                .Select(p => new SiteConstructionAllocation(p.Key, p.Value, context.Sites.Count(s => s.ConstructionProfile == p.Key))).ToArray() : null
        };

        Pcg32 ProfileRandom(string key, string purpose = "profile") => WorldStepSeed.Stream(context.World.Seed,
            ExpeditionWorldProfile.ConstructionV1 + "/" + path + "/" + key + "/" + purpose);

        void CountRequest(SiteConstructionProfile? profile)
        {
            if (profile is { } value) requestedProfiles[value] = requestedProfiles.GetValueOrDefault(value) + 1;
        }

        bool Place(string key, Func<GenerationScope, PlacementResult<SitePlan>> planner)
        {
            // Alternative placement streams retain the requested world seed and stable object IDs.
            // Standalone authoring without a world completion guard makes one planner request.
            var attempts = context.Completion is null ? 1 : 4;
            for (var ordinal = 0; ordinal < attempts; ordinal++)
            {
                var draft = board.BeginDraft(area, path + "/" + key, budget);
                var seed = ordinal == 0 ? context.World.Seed : WorldStepSeed.Derive(context.World.Seed,
                    path + "/" + key, WorldStepKind.Civilization, "completion-placement-" + ordinal);
                var result = planner(new(seed, path, draft));
                if (result is Rejected<SitePlan> failed)
                {
                    Failure(failed.Failure);
                    context.Completion?.RecordOmission(path + "/" + key, step.Id, failed.Failure.Reason);
                    return false;
                }
                var plan = ((Planned<SitePlan>)result).Plan;
                if (!draft.Accept(plan.Patch, out var error))
                { Failure(new(plan.Site.Id, "Civilization", PlacementFailureCode.ValidationFailed, error!, budget.Attempts)); return false; }
                var labels = context.OriginalAnalysis!.FormationLabels;
                var formationIds = plan.Site.Footprint.Indices(board.Width).Select(index => labels[index]).Where(label => label >= 0)
                    .Distinct().Order().Select(label => context.OriginalAnalysis.Formations[label].Id).ToArray();
                var opportunity = context.CurrentAnalysis?.Opportunities.FirstOrDefault(o => plan.Site.Footprint.Contains(o.AnchorIndex, board.Width));
                var site = plan.Site with { SourceFormationIds = formationIds, SourceOpportunityId = opportunity?.Id };
                if (context.Completion is { } completion)
                {
                    if (!completion.TryPublish(draft.BuildPatch(), site.Id + "/placement-" + ordinal, step.Id, out var diagnostic, newSites: [site]))
                    {
                        Failure(new(site.Id, "Civilization", diagnostic.Status == CompletionStatus.Incomplete ? PlacementFailureCode.BudgetExhausted : PlacementFailureCode.DisconnectedSpace,
                            diagnostic.Detail ?? "Completion preflight rejected candidate.", budget.Attempts));
                        continue;
                    }
                }
                else if (!board.TryCommit(draft.BuildPatch(), out error))
                { Failure(new(site.Id, "Civilization", PlacementFailureCode.ValidationFailed, error ?? "Commit rejected.", budget.Attempts)); return false; }
                context.Sites.Add(site); context.Construction.AddRange(plan.Operations); return true;
            }
            context.Completion?.RecordOmission(path + "/" + key, step.Id, "Deterministic completion-safe placement alternatives exhausted.");
            return false;
        }
        void Failure(PlacementFailure failure) { if (context.PlanningFailures.Count < 64) context.PlanningFailures.Add(failure); }
    }

    private static SiteConstructionProfile? SettlementProfile(int roll, int target)
    {
        if (target <= 2) return roll < 8 ? SiteConstructionProfile.Cottage : null;
        if (roll < (target >= 5 ? 2 : 4)) return SiteConstructionProfile.Cottage;
        return roll < 8 ? SiteConstructionProfile.VillageCenter : null;
    }

    private static CellBounds HubNeighborhood(CellBounds board, Cell hub, int weight)
    {
        // Shift a neighborhood at a boundary inward before laying out complete Sites.
        var span = 48 - weight / 2;
        var width = Math.Min(board.Width, span);
        var height = Math.Min(board.Height, span);
        return new(Math.Clamp(hub.X - width / 2, board.X, board.X + board.Width - width),
            Math.Clamp(hub.Y - height / 2, board.Y, board.Y + board.Height - height), width, height);
    }
}
