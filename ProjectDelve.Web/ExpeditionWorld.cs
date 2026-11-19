using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

public readonly record struct ExpeditionRegion(long X, long Y);

public enum ExpeditionEdge { North, East, South, West }

public sealed record ExpeditionRegionPlan(ExpeditionRegion Region, ExpeditionEdge EntryEdge, Cell EntrySeam,
    ExpeditionEdge ExitEdge, Cell ExitSeam, ObjectiveArea ExitArea, ExpeditionRegion NextRegion,
    ulong LocalSeed, ulong TransitionSeed, string TransitionId)
{
    // A generation request uses the committed roster size. Stored region identity
    // remains determined solely by the seed and transition history.
    [JsonIgnore] public int DeploymentCapacity { get; init; } = ExpeditionParty.Originals.Count;
}

internal static class ExpeditionWorld
{
    internal const int Stride = ExpeditionRegionGeometry.RegionStride;

    internal static ExpeditionRegionPlan Initial(ulong seed)
    {
        var origin = new ExpeditionRegion(0, 0);
        var entry = Opposite(Exit(seed, origin));
        return Plan(seed, origin, entry, Offset(Seed(seed, origin, "initial-entry")));
    }

    internal static ExpeditionRegionPlan Next(ulong seed, ExpeditionRegionPlan previous) =>
        Plan(seed, previous.NextRegion, Opposite(previous.ExitEdge), OffsetOf(previous.ExitEdge, previous.ExitSeam));

    private static ExpeditionRegionPlan Plan(ulong seed, ExpeditionRegion region, ExpeditionEdge entry, int entryOffset)
    {
        var exit = Exit(seed, region);
        if (entry == exit) throw new InvalidOperationException("Expedition entry and exit must use different edges.");
        var transition = Seed(seed, region, "transition");
        var offset = Offset(transition);
        var area = exit switch
        {
            ExpeditionEdge.North => new ObjectiveArea(offset - 1, 1, 3, 2),
            ExpeditionEdge.East => new ObjectiveArea(Stride - 3, offset - 1, 2, 3),
            ExpeditionEdge.South => new ObjectiveArea(offset - 1, Stride - 3, 3, 2),
            ExpeditionEdge.West => new ObjectiveArea(1, offset - 1, 2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(exit))
        };
        return new(region, entry, OnEdge(entry, entryOffset), exit, OnEdge(exit, offset), area, Neighbor(region, exit),
            Seed(seed, region, "local"), transition, FormattableString.Invariant($"{region.X},{region.Y}:{transition:x16}"));
    }

    private static ExpeditionEdge Exit(ulong seed, ExpeditionRegion region)
    {
        var axes = Seed(seed, default, "axes");
        var horizontal = (axes & 1) == 0 ? ExpeditionEdge.East : ExpeditionEdge.West;
        var vertical = (axes & 2) == 0 ? ExpeditionEdge.South : ExpeditionEdge.North;
        return (Seed(seed, region, "exit") & 1) == 0 ? horizontal : vertical;
    }

    private static int Offset(ulong seed) => 10 + (int)(seed % (Stride - 20));
    private static int OffsetOf(ExpeditionEdge edge, Cell cell) =>
        edge is ExpeditionEdge.East or ExpeditionEdge.West ? cell.Y : cell.X;

    private static Cell OnEdge(ExpeditionEdge edge, int offset) => edge switch
    {
        ExpeditionEdge.North => new(offset, 0),
        ExpeditionEdge.East => new(Stride - 1, offset),
        ExpeditionEdge.South => new(offset, Stride - 1),
        ExpeditionEdge.West => new(0, offset),
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    private static ExpeditionEdge Opposite(ExpeditionEdge edge) => (ExpeditionEdge)(((int)edge + 2) % 4);

    private static ExpeditionRegion Neighbor(ExpeditionRegion region, ExpeditionEdge edge) => edge switch
    {
        ExpeditionEdge.North => region with { Y = checked(region.Y - 1) },
        ExpeditionEdge.East => region with { X = checked(region.X + 1) },
        ExpeditionEdge.South => region with { Y = checked(region.Y + 1) },
        ExpeditionEdge.West => region with { X = checked(region.X - 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    internal static ulong Seed(ulong seed, ExpeditionRegion region, string domain) =>
        BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"Expedition/1/{domain}/{seed:x16}/{region.X}/{region.Y}"))));

    internal static WorldGenerationRecipe Configure(ulong seed, WorldGenerationRecipe? settings = null)
    {
        var recipe = settings ?? new WorldGenerationRecipe(new(), new());
        return (recipe with
        {
            Window = new(0, 0, Stride, Stride), Seed = seed, HeroCapacity = ExpeditionParty.Originals.Count,
            Enrichment = recipe.Enrichment ?? new(), Civilization = recipe.Civilization ?? new(),
            Routes = recipe.Routes ?? new(), Population = recipe.Population ?? new()
        }).Validate();
    }

    internal static WorldWindow RegionWindow(ExpeditionRegion region)
    {
        // Division toward zero gives the first whole region above int.MinValue.
        const long first = int.MinValue / (long)Stride;
        const long last = (int.MaxValue - (Stride - 1L)) / Stride;
        if (region.X is < first or > last || region.Y is < first or > last)
            throw new ArgumentOutOfRangeException(nameof(region), region,
                $"A complete {Stride} by {Stride} Expedition region must fit within the signed WLD1 Cell coordinate range.");
        return new(checked((int)(region.X * Stride)), checked((int)(region.Y * Stride)), Stride, Stride);
    }

    internal static WorldGenerationRecipe RecipeFor(ExpeditionRegionPlan plan, WorldGenerationRecipe settings) =>
        settings with { Window = RegionWindow(plan.Region), HeroCapacity = plan.DeploymentCapacity };

    internal static ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content) =>
        Generate(plan, content, Configure(seed));

    internal static ScenarioDefinition Generate(ExpeditionRegionPlan plan, ContentBundle content, WorldGenerationRecipe settings,
        ExpeditionWorldProfile? profile = null)
    {
        var recipe = RecipeFor(plan, settings);
        return ExpeditionRegionGeometry.Generate(recipe, plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile);
    }
}
