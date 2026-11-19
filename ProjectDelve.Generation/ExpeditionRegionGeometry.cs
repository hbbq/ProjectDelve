using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Expedition adapter over the ordinary WLD1 construction pipeline.</summary>
public static class ExpeditionRegionGeometry
{
    public static ScenarioDefinition Generate(WorldGenerationRecipe recipe, Cell entrySeam, Cell exitSeam,
        ObjectiveArea exitArea, ulong localSeed, ContentBundle content, ExpeditionWorldProfile? profile = null)
        => Execute(recipe, entrySeam, exitSeam, exitArea, localSeed, content, profile: profile).Context.Materialize();

    internal static WorldGenerationExecution Execute(WorldGenerationRecipe recipe, Cell entrySeam, Cell exitSeam,
        ObjectiveArea exitArea, ulong localSeed, ContentBundle content, Action<string>? beforeStage = null,
        ExpeditionWorldProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(content);
        recipe.Validate();
        if (recipe.Seed is null) throw new ArgumentException("Expedition generation requires the recorded world seed.", nameof(recipe));

        var expedition = new ExpeditionGeometryPlan(recipe.Window, recipe.HeroCapacity, entrySeam, exitSeam, exitArea, localSeed);
        if (profile is not null)
        {
            var sampler = new ExpeditionNatureSampler(recipe.Seed.Value, profile);
            // Downstream WLD1 stages use their ordinary seed inputs, scoped to this region.
            // The pinned sampler owns the distinct global world coordinates and seed.
            var regionalRecipe = sampler.ResolveRegion(recipe) with { Seed = localSeed };
            ExpeditionCivilizationAllocation? civilization = null;
            if (profile.CivilizationVersion == ExpeditionWorldProfile.CivilizationV1 &&
                recipe.Civilization is { Enabled: true })
            {
                civilization = ExpeditionCivilization.Resolve(recipe, profile, sampler) with
                {
                    ConstructionVersion = profile.ConstructionVersion,
                    Remains = profile.ConstructionVersion is ExpeditionWorldProfile.ConstructionV2 or ExpeditionWorldProfile.ConstructionV3
                        ? ExpeditionCivilization.ResolveRemains(recipe, profile) : null,
                    Outpost = profile.ConstructionVersion == ExpeditionWorldProfile.ConstructionV3
                        ? ExpeditionCivilization.ResolveOutpost(recipe, profile) : null
                };
                regionalRecipe = regionalRecipe with { Civilization = recipe.Civilization };
            }
            var regionalPlan = WorldGenerationPlan.FromRecipe(regionalRecipe) with
            { ExpeditionNature = (recipe.Seed.Value, profile), ExpeditionCivilization = civilization };
            return WorldGenerationPipeline.Execute(regionalPlan, content, expedition, beforeStage, sampler);
        }

        var plan = WorldGenerationPlan.FromRecipe(recipe);
        // Local decoration/deployment streams are independent of the immutable world-nature seed.
        // Keep the ordinary pipeline and seed derivation unchanged for non-Expedition callers.
        var ids = plan.Steps.ToDictionary(s => s.Id, s => s.Parameters is EnrichmentStepParameters
            ? $"{s.Id}-{localSeed:X16}" : s.Id, StringComparer.Ordinal);
        plan = plan with { Steps = plan.Steps.Select(s => s with
        { Id = ids[s.Id], DependsOn = s.DependsOn.Select(id => ids[id]).ToArray() }).ToArray() };
        return WorldGenerationPipeline.Execute(plan, content, expedition, beforeStage);
    }
}

internal sealed class ExpeditionGeometryPlan
{
    internal ulong LocalSeed { get; }
    internal IReadOnlySet<int> DeploymentDomain { get; }
    internal ImmutableArray<int> EndpointIndices { get; }
    internal ImmutableArray<RequiredLocation> RequiredLocations { get; }

    internal ExpeditionGeometryPlan(WorldWindow window, int capacity, Cell entry, Cell exit, ObjectiveArea area, ulong localSeed)
    {
        var width = window.Width; var height = window.Height;
        int Side(Cell c)
        {
            if (c is null || c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= height ||
                (c.X == 0 || c.X == width - 1) && (c.Y == 0 || c.Y == height - 1))
                throw new ArgumentException("Expedition seams must be in bounds, on an edge and away from corners.");
            return c.X == 0 ? 0 : c.X == width - 1 ? 1 : c.Y == 0 ? 2 : c.Y == height - 1 ? 3 :
                throw new ArgumentException("Expedition seams must lie on Board edges.");
        }
        if (Side(entry) == Side(exit)) throw new ArgumentException("Entry and exit must lie on distinct Board edges.");
        if (area is null || area.X < 0 || area.Y < 0 || area.W < 1 || area.H < 1 ||
            (long)area.X + area.W > width || (long)area.Y + area.H > height)
            throw new ArgumentException("The Expedition exit pad must lie within the Board.");
        var pad = (from y in Enumerable.Range(area.Y, area.H) from x in Enumerable.Range(area.X, area.W)
                   select y * width + x).ToImmutableArray();
        var exitIndex = exit.Y * width + exit.X;
        // A seam may be part of the pad or immediately bordering it; disconnected pads are malformed plans.
        if (!pad.Contains(exitIndex) && !PlanningBoard.Adjacent(exitIndex, width, height).Any(pad.Contains))
            throw new ArgumentException("The exit pad must touch the outgoing seam.");
        var entryIndex = entry.Y * width + entry.X;
        if (pad.Contains(entryIndex)) throw new ArgumentException("The exit pad must not contain the incoming seam.");
        LocalSeed = localSeed;
        EndpointIndices = pad.Append(entryIndex).Append(exitIndex).Distinct().Order().ToImmutableArray();
        // Search/preparation stays local to the landing. No hypothetical through corridor is reserved.
        var reach = Math.Max(6, (int)Math.Ceiling(Math.Sqrt(checked(3 * capacity))) + 2);
        DeploymentDomain = (from y in Enumerable.Range(Math.Max(0, entry.Y - reach), Math.Min(height - 1, entry.Y + reach) - Math.Max(0, entry.Y - reach) + 1)
                            from x in Enumerable.Range(Math.Max(0, entry.X - reach), Math.Min(width - 1, entry.X + reach) - Math.Max(0, entry.X - reach) + 1)
                            select y * width + x).Except(EndpointIndices).ToImmutableHashSet();
        RequiredLocations = [Location("entry-seam", [entryIndex]), Location("exit-seam", [exitIndex]), Location("exit-pad", pad)];

        static RequiredLocation Location(string role, ImmutableArray<int> positions) => new("expedition/" + role,
            "expedition", role, [new("usable", RequiredAccessCoverage.All, positions)]);
    }

    internal void PrepareEndpoints(PlanningBoard board)
    {
        // This authorized natural preparation runs before protected construction. Both sides of every
        // handoff use the same ordinary open ground, even when natural Water/Rock crosses the seam.
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), "expedition/endpoints", new(maxWork: 200000));
        foreach (var index in EndpointIndices) draft.SetTerrain(index, "grass");
        if (!board.TryCommit(draft.BuildPatch(), out var error)) throw new InvalidOperationException(error);
    }

    internal void ProtectEndpoints(PlanningBoard board)
    {
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), "expedition/endpoints", new());
        draft.Reserve(EndpointIndices);
        if (!board.TryCommit(draft.BuildPatch(), out var error)) throw new InvalidOperationException(error);
    }

    internal static IEnumerable<int> OutpostDoorCells(IEnumerable<SiteInfo> sites) =>
        sites.Where(site => site.Outpost is not null)
            .SelectMany(site => site.Entrances.Concat(site.Buildings.SelectMany(building => building.Portals)))
            .SelectMany(portal => new[] { portal.InsideIndex, portal.OutsideIndex });

    internal bool HasOccupiedAccess(GameState state, HeroDeployment deployment, IEnumerable<int>? extraLocations = null)
    {
        // Initial figures are obstacles for this geographic guarantee: the route cannot depend
        // on their eventual movement or defeat. Use ordinary Land geometry with conditional
        // Door opening, exactly as mandatory repair does, and include complete footprints.
        var width = state.Physical.Board.Width;
        var occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id))
            .Select(c => c.Y * width + c.X).ToHashSet();
        var reached = MandatoryConnectivityRepair.ActualReachable(state.Physical.Board,
            [deployment.PlacementOrder[0]], occupied);
        return EndpointIndices.All(reached.Contains) && deployment.PlacementOrder.All(reached.Contains) &&
            reached.Contains(deployment.DepartureIndex) && (extraLocations?.All(reached.Contains) ?? true);
    }
}
