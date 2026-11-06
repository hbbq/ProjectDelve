using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ProjectDelve.Generation;

public enum FeatureScale { Fine, Balanced, Broad }
public enum WaterForm { Auto, Lakes, River, Mixed }

public sealed record NaturalTerrainSettings(int ForestCoverage = 35, int WaterCoverage = 12,
    int RockCoverage = 15, FeatureScale Scale = FeatureScale.Balanced, WaterForm WaterForm = WaterForm.Auto)
{
    public NaturalTerrainSettings Validate()
    {
        if (ForestCoverage is < 0 or > 100 || WaterCoverage is < 0 or > 100 || RockCoverage is < 0 or > 100 ||
            ForestCoverage + WaterCoverage + RockCoverage > 100)
            throw new ArgumentException("Forest, water and rock coverage must be 0..100 and total at most 100.");
        if (!Enum.IsDefined(Scale) || !Enum.IsDefined(WaterForm)) throw new ArgumentException("Unknown natural terrain setting.");
        return this;
    }
}

public sealed record WorldWindow(int OffsetX = 0, int OffsetY = 0, int Width = 30, int Height = 24)
{
    public WorldWindow Validate()
    {
        if (Width is < 1 or > 120 || Height is < 1 or > 120 || (long)Width * Height > 14400)
            throw new ArgumentException("World windows support dimensions 1..120.");
        if ((long)OffsetX + Width - 1 > int.MaxValue || (long)OffsetY + Height - 1 > int.MaxValue)
            throw new ArgumentException("World window exceeds signed coordinate limits.");
        return this;
    }
}

public sealed record EnrichmentSettings(bool Enabled = true, int Intensity = 2,
    bool Forest = true, bool Rock = true, bool Water = true, bool Grassland = true)
{
    public EnrichmentSettings Validate()
    {
        if (Intensity is < 1 or > 3) throw new ArgumentException("Enrichment intensity must be 1..3.");
        return this;
    }
}

public sealed record WorldGenerationRecipe(WorldWindow Window, NaturalTerrainSettings NaturalTerrain, ulong? Seed = null,
    EnrichmentSettings? Enrichment = null, CivilizationSettings? Civilization = null, RouteSettings? Routes = null, PopulationSettings? Population = null)
{
    public WorldDefinition World => Seed is { } seed ? new WorldDefinition(seed, NaturalTerrain).Validate() :
        throw new InvalidOperationException("Complete the recipe before resolving its world.");
    public WorldGenerationRecipe Validate()
    {
        if (Window is null || NaturalTerrain is null) throw new ArgumentException("Window and natural terrain settings are required.");
        Window.Validate(); NaturalTerrain.Validate(); Enrichment?.Validate(); Civilization?.Validate(); Routes?.Validate(); Population?.Validate(); return this;
    }
    public WorldGenerationRecipe Complete() => Seed.HasValue ? Validate() : Validate() with
    { Seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) };
    public string Prefix
    {
        get
        {
            Validate();
            var prefix = FormattableString.Invariant($"WLD1-{Window.Width}x{Window.Height}-X{Window.OffsetX}-Y{Window.OffsetY}-F{NaturalTerrain.ForestCoverage}-W{NaturalTerrain.WaterCoverage}-R{NaturalTerrain.RockCoverage}-K{NaturalTerrain.Scale}-H{NaturalTerrain.WaterForm}");
            return prefix + (Enrichment is { } e ? FormattableString.Invariant($"-E{(e.Enabled ? 1 : 0)}-I{e.Intensity}-B{(e.Forest ? 1 : 0)}{(e.Rock ? 1 : 0)}{(e.Water ? 1 : 0)}{(e.Grassland ? 1 : 0)}") : "") +
                (Civilization is { } c ? FormattableString.Invariant($"-C{(c.Enabled ? 1 : 0)}-N{c.Campsites}-V{c.Settlements}-P{c.PreparationIntensity}") : "") +
                (Routes is { } r ? FormattableString.Invariant($"-Q{(r.Enabled ? 1 : 0)}-T{r.ThroughRoutes}-J{r.Profile}-L{(r.ConnectSites ? 1 : 0)}-A{r.Alternatives}-U{(r.WaterCrossings ? 1 : 0)}-Z{r.Intensity}") : "") + (Population is { } population ? FormattableString.Invariant($"-M{population.Density}-D{population.Difficulty}") : "");
        }
    }
    public override string ToString() => Prefix + (Seed is { } seed ? $"-S{seed:X16}" : "");
    public static WorldGenerationRecipe Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 320) throw new FormatException("Enter a WLD1 recipe.");
        var m = Regex.Match(input.Trim(), @"\AWLD1-([1-9][0-9]{0,2})x([1-9][0-9]{0,2})-X(-?(?:0|[1-9][0-9]{0,9}))-Y(-?(?:0|[1-9][0-9]{0,9}))-F(0|[1-9][0-9]{0,2})-W(0|[1-9][0-9]{0,2})-R(0|[1-9][0-9]{0,2})-K(Fine|Balanced|Broad)-H(Auto|Lakes|River|Mixed)(?:-E([01])-I([123])-B([01])([01])([01])([01]))?(?:-C([01])-N([0-8])-V([0-4])-P([123]))?(?:-Q(?<routeEnabled>[01])-T(?<through>[0-4])-J(?<profile>ForestTrail|MinorPath|Road|RockPassage)-L(?<connect>[01])-A(?<alternatives>[0-3])-U(?<crossings>[01])-Z(?<routeIntensity>[123]))?(?:-M(?<population>Off|Low|Medium|High)-D(?<difficulty>[1-5]))?(?:-S(?<seed>[0-9a-fA-F]{16}))?\z", RegexOptions.CultureInvariant);
        if (!m.Success) throw new FormatException("Expected WLD1-30x24-X0-Y0-F35-W12-R15-KBalanced-HAuto, optionally followed by -S and 16 hexadecimal digits.");
        int N(int i) => int.TryParse(m.Groups[i].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException("Coordinate exceeds signed integer limits.");
        return new WorldGenerationRecipe(new(N(3), N(4), N(1), N(2)),
            new(N(5), N(6), N(7), Enum.Parse<FeatureScale>(m.Groups[8].Value), Enum.Parse<WaterForm>(m.Groups[9].Value)),
            m.Groups["seed"].Success ? ulong.Parse(m.Groups["seed"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null,
            m.Groups[10].Success ? new(m.Groups[10].Value == "1", N(11), N(12) == 1, N(13) == 1, N(14) == 1, N(15) == 1) : null,
            m.Groups[16].Success ? new(m.Groups[16].Value == "1", N(17), N(18), N(19)) : null,
            m.Groups["routeEnabled"].Success ? new(m.Groups["routeEnabled"].Value == "1", int.Parse(m.Groups["through"].Value), Enum.Parse<RouteProfileKind>(m.Groups["profile"].Value), m.Groups["connect"].Value == "1", int.Parse(m.Groups["alternatives"].Value), m.Groups["crossings"].Value == "1", int.Parse(m.Groups["routeIntensity"].Value)) : null, m.Groups["population"].Success ? new(Enum.Parse<PopulationDensity>(m.Groups["population"].Value), int.Parse(m.Groups["difficulty"].Value)) : null).Validate();
    }
}

// World identity excludes every crop/window property. A recipe combines this
// immutable definition with the finite board the author wants to materialize.
public sealed record WorldDefinition(ulong Seed, NaturalTerrainSettings NaturalTerrain)
{
    public string Version => "WLD1";
    public WorldDefinition Validate() { NaturalTerrain.Validate(); return this; }
}

public sealed record WorldGenerationPreset(string Id, string Name, NaturalTerrainSettings Settings)
{
    public EnrichmentSettings Enrichment { get; init; } = new();
    public CivilizationSettings Civilization { get; init; } = new();
    public RouteSettings Routes { get; init; } = new();
    public PopulationSettings Population { get; init; } = new();
}
public static class WorldGenerationPresets
{
    public static IReadOnlyList<WorldGenerationPreset> All { get; } = Array.AsReadOnly(new WorldGenerationPreset[]
    {
        new("Grassland", "Grassland", new(10, 4, 5, FeatureScale.Broad, WaterForm.Lakes)) { Routes = new(Profile: RouteProfileKind.Road) },
        new("Woodland", "Woodland", new(60, 8, 8)) { Routes = new(Profile: RouteProfileKind.ForestTrail) },
        new("LakeCountry", "Lake Country", new(25, 30, 10, FeatureScale.Broad, WaterForm.Lakes)),
        new("RiverValley", "River Valley", new(25, 15, 10, FeatureScale.Balanced, WaterForm.River)),
        new("RockyWilds", "Rocky Wilds", new(15, 8, 50, FeatureScale.Broad, WaterForm.Mixed)) { Routes = new(Profile: RouteProfileKind.RockPassage) }
    });
}
