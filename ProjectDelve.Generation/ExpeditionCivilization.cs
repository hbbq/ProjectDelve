using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record ExpeditionCivilizationAllocation(double DensitySignal, double ClusteringSignal,
    int SettlementTarget, int CampsiteTarget, int DungeonTarget, Cell? Hub, int AnchorWeight)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ConstructionVersion { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ExpeditionRemainsAllocation? Remains { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ExpeditionOutpostAllocation? Outpost { get; init; }
}

internal sealed record ExpeditionRemainsAllocation(double RuinSignal, double LandmarkSignal,
    SiteConstructionProfile? Ruin, Cell? RuinAnchor, Cell? LandmarkAnchor);
internal sealed record ExpeditionOutpostAllocation(double Signal, Cell? Anchor);

/// <summary>Allocation and placement preferences for ExpeditionCivilization1.</summary>
internal static class ExpeditionCivilization
{
    internal static ExpeditionCivilizationAllocation Resolve(WorldGenerationRecipe recipe,
        ExpeditionWorldProfile profile, ExpeditionNatureSampler nature)
    {
        var window = recipe.Window;
        var settings = recipe.Civilization!;
        var variation = profile.Spatial.RegionalVariation;
        var settlements = DensityScaling.Settlements(window, settings.Settlements);
        var camps = DensityScaling.Campsites(window, settings.Campsites);
        var dungeons = MountainDungeonGenerator.Target(window, settings);
        var density = RegionalMean(nature, window);
        if (variation == 0)
            return new(density, 0, settlements, camps, dungeons, null, 0);

        var seed = recipe.Seed!.Value;
        var campField = RegionalMean(IndependentField(seed, profile, "camps"), window);
        var clustering = RegionalMean(IndependentField(seed, profile, "clustering"), window);
        // The recipe's nominal count is reached at the middle of the signal.
        // The lower range can round to zero and each additional request has its own threshold.
        var scaled = Scale(density);
        var demand = scaled <= .5
            ? settlements * Math.Max(0, (scaled - .21) / .29)
            : settlements + Math.Min(24 - settlements, settlements + 1) * (scaled - .5) * 2;
        settlements = settlements == 0 ? 0 : Round(demand);
        camps = Round(camps * (.15 + .85 * Scale(campField)));

        // A low score leaves the usual WLD1 placement behavior available even in dense regions.
        // At high scores this is a soft ranking preference. Geometry and access still decide acceptance.
        var weight = Round(26 * Math.Max(0, (Scale(clustering) - .35) / .65));
        var hub = settlements > 0 && weight > 0
            ? PreferredHub(IndependentField(seed, profile, "hubs", 72), window)
            : null;
        return new(density, clustering, settlements, camps, dungeons, hub, weight);

        double Scale(double signal) => Math.Clamp(.5 + (signal - .5) * variation, 0, 1);
    }

    // This allocation adds optional requests alongside the original civilization targets.
    // All signals use pinned sampling with distinct seed domains, including the local anchors.
    internal static ExpeditionRemainsAllocation ResolveRemains(WorldGenerationRecipe recipe, ExpeditionWorldProfile profile)
    {
        var seed = recipe.Seed!.Value;
        var window = recipe.Window;
        ExpeditionNatureSampler Field(string role, double scale) => new(
            WorldStepSeed.Derive(seed, ExpeditionWorldProfile.ConstructionV2, WorldStepKind.Civilization, role),
            profile with { Spatial = profile.Spatial with { RegionScale = scale } });

        var ruins = RegionalMean(Field("ruin-geography", profile.Spatial.RegionScale), window);
        var landmarks = RegionalMean(Field("landmark-geography", profile.Spatial.RegionScale), window);
        var style = WorldStepSeed.Stream(seed, FormattableString.Invariant(
            $"{ExpeditionWorldProfile.ConstructionV2}/ruin-style/{window.OffsetX}/{window.OffsetY}")).Next(2);
        SiteConstructionProfile? ruin = ruins switch
        {
            < .56 => null,
            >= .76 => SiteConstructionProfile.RuinedHamlet,
            _ => style == 0 ? SiteConstructionProfile.RuinedCottage : SiteConstructionProfile.CollapsedBuilding
        };
        return new(ruins, landmarks, ruin,
            ruin is null ? null : PreferredHub(Field("ruin-anchors", 84), window),
            landmarks < .57 ? null : PreferredHub(Field("landmark-anchors", 92), window));
    }

    internal static ExpeditionOutpostAllocation ResolveOutpost(WorldGenerationRecipe recipe, ExpeditionWorldProfile profile)
    {
        var seed = recipe.Seed!.Value;
        var signal = RegionalMean(Field("outpost-geography", profile.Spatial.RegionScale), recipe.Window);
        return new(signal, signal < .69 ? null : PreferredHub(Field("outpost-anchors", 88), recipe.Window));

        ExpeditionNatureSampler Field(string role, double scale) => new(
            WorldStepSeed.Derive(seed, ExpeditionWorldProfile.ConstructionV3, WorldStepKind.Civilization, role),
            profile with { Spatial = profile.Spatial with { RegionScale = scale } });
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static ExpeditionNatureSampler IndependentField(ulong seed, ExpeditionWorldProfile profile, string role,
        double? scale = null) => new(WorldStepSeed.Derive(seed, ExpeditionWorldProfile.CivilizationV1, WorldStepKind.Civilization, role),
        scale is { } wavelength ? profile with { Spatial = profile.Spatial with { RegionScale = wavelength } } : profile);

    private static double RegionalMean(ExpeditionNatureSampler sampler, WorldWindow window)
    {
        double sum = 0;
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            sum += sampler.FieldAt(ExpeditionField.Civilization,
                window.OffsetX + (2 * x + 1) * window.Width / 6, window.OffsetY + (2 * y + 1) * window.Height / 6);
        return sum / 9;
    }

    private static Cell PreferredHub(ExpeditionNatureSampler sampler, WorldWindow window)
    {
        // Sample the same absolute-coordinate field on a regular grid. Edge hubs can
        // influence both neighboring boards without a shared reservation or construction.
        var best = new Cell(0, 0);
        var score = double.NegativeInfinity;
        for (var y = 0; y < 6; y++)
        for (var x = 0; x < 6; x++)
        {
            var cell = new Cell((2 * x + 1) * window.Width / 12, (2 * y + 1) * window.Height / 12);
            var value = sampler.FieldAt(ExpeditionField.Civilization, window.OffsetX + cell.X, window.OffsetY + cell.Y);
            if (value <= score) continue;
            (score, best) = (value, cell);
        }
        return best;
    }
}
