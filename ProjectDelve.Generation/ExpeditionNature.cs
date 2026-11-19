using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Persisted wavelengths in world Cells and bounded variation amplitudes.</summary>
public sealed record ExpeditionSpatialSettings
{
    [JsonRequired] public double NatureScale { get; init; } = 240;
    [JsonRequired] public double RegionScale { get; init; } = 360;
    [JsonRequired] public double CoverageVariation { get; init; } = .75;
    [JsonRequired] public double RegionalVariation { get; init; } = 1;

    public ExpeditionSpatialSettings Validate()
    {
        if (!double.IsFinite(NatureScale) || NatureScale is < 8 or > 1_000_000 ||
            !double.IsFinite(RegionScale) || RegionScale is < 8 or > 1_000_000 ||
            !double.IsFinite(CoverageVariation) || CoverageVariation is < 0 or > 1 ||
            !double.IsFinite(RegionalVariation) || RegionalVariation is < 0 or > 4)
            throw new ArgumentException("Expedition spatial settings require finite scales 8..1000000, coverage variation 0..1 and regional variation 0..4.");
        return this;
    }
}

public sealed record ExpeditionWorldProfile(string NatureVersion, string FieldVersion, string Coordinates,
    NaturalTerrainSettings Nature, ExpeditionSpatialSettings Spatial)
{
    internal const string NatureV1 = "ExpeditionNature1";
    internal const string FieldsV1 = "ExpeditionFields1";
    private const string CoordinatesV1 = "SignedCellXY-RightDown1";
    public const string CurrentNatureVersion = NatureV1;
    public const string CurrentFieldVersion = FieldsV1;
    public const string CoordinateConvention = CoordinatesV1;
    internal const string CivilizationV1 = "ExpeditionCivilization1";
    public const string CurrentCivilizationVersion = CivilizationV1;
    internal const string ConstructionV1 = "ExpeditionConstruction1";
    internal const string ConstructionV2 = "ExpeditionConstruction2";
    internal const string ConstructionV3 = "ExpeditionConstruction3";
    public const string CurrentConstructionVersion = ConstructionV3;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CivilizationVersion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConstructionVersion { get; init; }

    public static ExpeditionWorldProfile Create(NaturalTerrainSettings nature, ExpeditionSpatialSettings? spatial = null) =>
        CreateCivilizationV1(nature, spatial) with { ConstructionVersion = CurrentConstructionVersion };

    public static ExpeditionWorldProfile CreateConstructionV1(NaturalTerrainSettings nature, ExpeditionSpatialSettings? spatial = null) =>
        CreateCivilizationV1(nature, spatial) with { ConstructionVersion = ConstructionV1 };

    public static ExpeditionWorldProfile CreateConstructionV2(NaturalTerrainSettings nature, ExpeditionSpatialSettings? spatial = null) =>
        CreateCivilizationV1(nature, spatial) with { ConstructionVersion = ConstructionV2 };

    public static ExpeditionWorldProfile CreateCivilizationV1(NaturalTerrainSettings nature, ExpeditionSpatialSettings? spatial = null) =>
        CreateV1(nature, spatial) with { CivilizationVersion = CivilizationV1 };

    /// <summary>Explicit legacy migration target; remains V1 when Create gains a newer default.</summary>
    public static ExpeditionWorldProfile CreateV1(NaturalTerrainSettings nature, ExpeditionSpatialSettings? spatial = null) =>
        new ExpeditionWorldProfile(NatureV1, FieldsV1, CoordinatesV1, nature,
            spatial ?? new() { NatureScale = 240, RegionScale = 360, CoverageVariation = .75, RegionalVariation = 1 }).Validate();

    public ExpeditionWorldProfile Validate()
    {
        if (NatureVersion != NatureV1)
            throw new ArgumentException($"Unsupported Expedition nature version '{NatureVersion}'.");
        if (FieldVersion != FieldsV1)
            throw new ArgumentException($"Unsupported Expedition field version '{FieldVersion}'.");
        if (Coordinates != CoordinatesV1)
            throw new ArgumentException($"Unsupported Expedition coordinate convention '{Coordinates}'.");
        if (CivilizationVersion is not (null or CivilizationV1))
            throw new ArgumentException($"Unsupported Expedition civilization version '{CivilizationVersion}'.");
        if (ConstructionVersion is not (null or ConstructionV1 or ConstructionV2 or ConstructionV3))
            throw new ArgumentException($"Unsupported Expedition construction version '{ConstructionVersion}'.");
        if (ConstructionVersion is not null && CivilizationVersion != CivilizationV1)
            throw new ArgumentException("Expedition construction requires the ExpeditionCivilization1 allocation.");
        if (Nature is null || Spatial is null) throw new ArgumentException("Expedition nature and spatial settings are required.");
        Nature.Validate(); Spatial.Validate();
        return this;
    }
}

// These numeric identities are part of the V1 hash domains.
public enum ExpeditionField { Forest = 0, Rock = 1, Water = 2, Enrichment = 3, Civilization = 4, Routes = 5, Population = 6, Difficulty = 7, Loot = 8 }
public sealed record ExpeditionCoverage(double Forest, double Rock, double Water);

/// <summary>
/// Version-pinned, catalog-independent base nature. Every result depends only on the
/// world seed, persisted profile and absolute signed Cell; +X is right, +Y is down.
/// Local enrichment/construction may subsequently alter the sampled terrain.
/// </summary>
public sealed class ExpeditionNatureSampler
{
    private readonly ulong seed;
    private readonly ExpeditionWorldProfile profile;

    public ExpeditionNatureSampler(ulong worldSeed, ExpeditionWorldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        this.profile = profile.Validate();
        seed = worldSeed;
    }

    public NaturalTerrainSample Sample(int x, int y) => profile.NatureVersion switch
    {
        ExpeditionWorldProfile.NatureV1 => SampleV1(x, y),
        _ => throw new InvalidOperationException("Unsupported Expedition nature version.")
    };

    public double FieldAt(ExpeditionField field, int x, int y)
    {
        if (!Enum.IsDefined(field)) throw new ArgumentException("Unknown Expedition field.", nameof(field));
        return profile.FieldVersion switch
        {
            ExpeditionWorldProfile.FieldsV1 => FieldV1(field, x, y),
            _ => throw new InvalidOperationException("Unsupported Expedition field version.")
        };
    }

    /// <summary>
    /// Continuous percentages before priority classification. Zero coverage remains
    /// disabled; a sole 100% category remains saturated. Overlapping budgets are
    /// proportionally normalized per Cell, never against a crop's observed counts.
    /// </summary>
    public ExpeditionCoverage CoverageAt(int x, int y) => profile.FieldVersion switch
    {
        ExpeditionWorldProfile.FieldsV1 => CoverageV1(x, y),
        _ => throw new InvalidOperationException("Unsupported Expedition field version.")
    };

    private ExpeditionCoverage CoverageV1(int x, int y)
    {
        var n = profile.Nature;
        double Amount(int baseline, ExpeditionField field) => baseline is 0 or 100 ? baseline :
            baseline * (1 + profile.Spatial.CoverageVariation * (2 * FieldAt(field, x, y) - 1));
        var forest = Amount(n.ForestCoverage, ExpeditionField.Forest);
        var rock = Amount(n.RockCoverage, ExpeditionField.Rock);
        var water = Amount(n.WaterCoverage, ExpeditionField.Water);
        var normalization = Math.Max(1, (forest + rock + water) / 100);
        return new(forest / normalization, rock / normalization, water / normalization);
    }

    /// <summary>
    /// Resolve existing local controls at the floor of the crop's geometric center.
    /// Null/disabled stages and explicit None/Off densities remain disabled. Each
    /// channel independently moves its baseline by rounded, bounded integer steps.
    /// Zero regional variation preserves the complete baseline recipe.
    /// </summary>
    public WorldGenerationRecipe ResolveRegion(WorldGenerationRecipe baseline) => profile.FieldVersion switch
    {
        ExpeditionWorldProfile.FieldsV1 => ResolveRegionV1(baseline),
        _ => throw new InvalidOperationException("Unsupported Expedition field version.")
    };

    private WorldGenerationRecipe ResolveRegionV1(WorldGenerationRecipe baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        baseline.Validate();
        if (baseline.NaturalTerrain != profile.Nature || baseline.Seed is { } baselineSeed && baselineSeed != seed)
            throw new ArgumentException("The regional recipe must use the pinned baseline nature and Expedition world seed.", nameof(baseline));
        var x = (int)((long)baseline.Window.OffsetX + (baseline.Window.Width - 1) / 2);
        var y = (int)((long)baseline.Window.OffsetY + (baseline.Window.Height - 1) / 2);
        int Map(int value, int min, int max, ExpeditionField field) => Math.Clamp(value +
            (int)Math.Round((FieldAt(field, x, y) * 2 - 1) * profile.Spatial.RegionalVariation,
                MidpointRounding.AwayFromZero), min, max);
        ContentDensity Density(ContentDensity value, ExpeditionField field) => value == ContentDensity.None ? value :
            (ContentDensity)Map((int)value, 1, 4, field);
        var result = baseline with
        {
            Enrichment = baseline.Enrichment is { Enabled: true } e ? e with
                { Intensity = Map(e.Intensity, 1, 3, ExpeditionField.Enrichment) } : baseline.Enrichment,
            Civilization = baseline.Civilization is { Enabled: true } c ? c with
                { Campsites = Density(c.Campsites, ExpeditionField.Civilization),
                  Settlements = Density(c.Settlements, ExpeditionField.Civilization),
                  PreparationIntensity = Map(c.PreparationIntensity, 1, 3, ExpeditionField.Civilization) } : baseline.Civilization,
            Routes = baseline.Routes is { Enabled: true } r ? r with
                { ThroughRoutes = Density(r.ThroughRoutes, ExpeditionField.Routes),
                  Alternatives = Density(r.Alternatives, ExpeditionField.Routes),
                  Intensity = Map(r.Intensity, 1, 3, ExpeditionField.Routes) } : baseline.Routes,
            Population = baseline.Population is { Density: not PopulationDensity.Off } p ? p with
                { Density = (PopulationDensity)Map((int)p.Density, 1, 3, ExpeditionField.Population),
                  Difficulty = Map(p.Difficulty, 1, 5, ExpeditionField.Difficulty) } : baseline.Population,
            LootAbundance = (LootAbundance)Map((int)baseline.LootAbundance, 0, 2, ExpeditionField.Loot)
        };
        return result.Validate();
    }

    /// <summary>Reject changes to the stable roles rather than silently changing frontier nature.</summary>
    public void ValidateTerrain(ContentBundle content)
    {
        if (profile.NatureVersion != ExpeditionWorldProfile.NatureV1) throw new InvalidOperationException("Unsupported Expedition nature version.");
        ValidateTerrainV1(content);
    }

    private static void ValidateTerrainV1(ContentBundle content)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Pin mechanics explicitly. Comparing with TerrainRoster would accidentally
        // accept future catalog edits as the meaning of an existing nature version.
        TerrainDefinition[] roles =
        [
            new("grass", "", ["land"], false) { LegacyKind = TerrainKind.Grass, Categories = ["land", "vegetation"] },
            new("dirt", "", ["land"], false) { Categories = ["land"] },
            new("tree", "", [], true) { LegacyKind = TerrainKind.Tree, Categories = ["land", "vegetation"], BlocksVision = false, DestroyedTerrainTypeId = "grass" },
            new("solid-rock", "", [], true) { LegacyKind = TerrainKind.SolidRock, Categories = ["rock"], DestroyedTerrainTypeId = "gravel" },
            new("water", "", ["water"], false) { LegacyKind = TerrainKind.Water, Categories = ["water"] }
        ];
        foreach (var expected in roles)
        {
            var actual = content.Terrain.Where(t => t.Id == expected.Id).ToArray();
            if (actual.Length != 1 || !Compatible(actual[0], expected))
                throw new ArgumentException($"Expedition nature requires compatible stable natural terrain role '{expected.Id}'.", nameof(content));
        }

        static bool Compatible(TerrainDefinition a, TerrainDefinition b) =>
            a.LegacyKind == b.LegacyKind && a.MovementPermissions.Order(StringComparer.Ordinal).SequenceEqual(b.MovementPermissions.Order(StringComparer.Ordinal)) &&
            a.Categories.Order(StringComparer.Ordinal).SequenceEqual(b.Categories.Order(StringComparer.Ordinal)) &&
            a.BlocksLos == b.BlocksLos && a.BlocksVision == b.BlocksVision &&
            a.AllowsResourceMarkers == b.AllowsResourceMarkers && a.DestroyedTerrainTypeId == b.DestroyedTerrainTypeId &&
            a.Production == b.Production && a.LegacyProductionIdentity == b.LegacyProductionIdentity;
    }

    // Everything below is private V1 behavior. Do not replace these primitives with
    // standalone WLD1 helpers: WLD1 deliberately evolves independently of this version.
    private NaturalTerrainSample SampleV1(int x, int y)
    {
        var scale = profile.Nature.Scale switch { FeatureScale.Fine => 3d, FeatureScale.Balanced => 5d, FeatureScale.Broad => 8d,
            _ => throw new InvalidOperationException("Unknown pinned nature scale.") };
        var forest = Fractal(Domain(101), x, y, scale * 2.5);
        var rock = Fractal(Domain(102), x, y, scale * 1.8);
        var openness = Fractal(Domain(103), x, y, scale * 4);
        var soil = Fractal(Domain(104), x, y, scale * 1.25);
        var coverage = CoverageAt(x, y);
        var waterField = Fractal(Domain(105), x, y, scale * 8);
        // Isolines of a smooth field create bounded-cost sinuous ribbons. Their
        // locations and widths belong to absolute coordinates, without crop owners
        // or searches, unbounded rivers, or catalog-dependent mixed terrain.
        var ribbon = Math.Abs(2 * Probability(Fractal(Domain(106), x, y, scale * 12)) - 1);
        var waterForm = profile.Nature.WaterForm;
        if (waterForm == WaterForm.Auto) waterForm = Unit(Domain(107)) < .5 ? WaterForm.Lakes : WaterForm.River;
        var waterScore = waterForm switch
        {
            WaterForm.River => ribbon,
            WaterForm.Mixed => 1 - Math.Pow(1 - Math.Min(Probability(waterField), ribbon), 2),
            _ => Probability(waterField)
        };
        var land = 100 - coverage.Water;
        var forestLand = land - coverage.Rock;
        var terrain = Below(waterScore, coverage.Water / 100) ? "water" :
            Below(Probability(rock), land <= 0 ? 0 : coverage.Rock / land) ? "solid-rock" :
            Below(Probability(forest * .85 + openness * .15), forestLand <= 0 ? 0 : coverage.Forest / forestLand) ? "tree" :
            soil > .64 && openness < .62 ? "dirt" : "grass";
        return new(terrain, forest, rock, openness, soil);
    }

    private double FieldV1(ExpeditionField field, int x, int y)
    {
        var scale = field is ExpeditionField.Forest or ExpeditionField.Rock or ExpeditionField.Water
            ? profile.Spatial.NatureScale : profile.Spatial.RegionScale;
        // Spread the bounded smooth field so rounded local controls vary at useful
        // geographic scales. Values remain continuous and always in [0,1].
        return Math.Clamp(.5 + 2 * (Fractal(Domain(1 + (ulong)field), x, y, scale) - .5), 0, 1);
    }

    private ulong Domain(ulong domain) => Mix(seed ^ Mix(unchecked(0x4558504E41545531UL + domain)));
    private static bool Below(double field, double amount) => amount >= 1 || amount > 0 && field < amount;
    private static double Probability(double value) => 1 / (1 + Math.Exp(-1.7 * (value - .5) / .19));
    private static double Fractal(ulong domain, double x, double y, double scale) =>
        .60 * Noise(domain, x / scale, y / scale) +
        .30 * Noise(Mix(domain), x / (scale * .3), y / (scale * .3)) +
        .10 * Noise(Mix(unchecked(domain + 1)), x / (scale * .075), y / (scale * .075));

    private static double Noise(ulong domain, double x, double y)
    {
        var ix = (long)Math.Floor(x); var iy = (long)Math.Floor(y);
        static double Smooth(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        var tx = Smooth(x - ix); var ty = Smooth(y - iy);
        var a = Unit(Hash(domain, ix, iy)); var b = Unit(Hash(domain, ix + 1, iy));
        var c = Unit(Hash(domain, ix, iy + 1)); var d = Unit(Hash(domain, ix + 1, iy + 1));
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }

    private static ulong Hash(ulong domain, long x, long y) =>
        Mix(domain ^ Mix(unchecked((ulong)x)) ^ Mix(unchecked((ulong)y + 0xD1B54A32D192ED03UL)));
    private static double Unit(ulong value) => (value >> 11) * (1.0 / (1UL << 53));
    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
