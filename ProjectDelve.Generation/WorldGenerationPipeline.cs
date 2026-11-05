using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum WorldStepKind { NaturalTerrain, TerrainAnalysis, ForestEnrichment, RockEnrichment, WaterEnrichment, GrasslandEnrichment, PostEnrichmentAnalysis }
public abstract record WorldStepParameters;
public sealed record NaturalStepParameters(NaturalTerrainSettings Settings) : WorldStepParameters;
public sealed record AnalysisStepParameters : WorldStepParameters;
public sealed record EnrichmentStepParameters(int Intensity = 2) : WorldStepParameters;
public sealed record WorldGenerationStep(string Id, WorldStepKind Kind, WorldStepParameters Parameters, IReadOnlyList<string> DependsOn);

// Internal typed recipe. UI codes construct this explicit schedule; no runtime registry or executable content.
public sealed record WorldGenerationPlan(WorldDefinition World, WorldWindow Window, IReadOnlyList<WorldGenerationStep> Steps)
{
    public static WorldGenerationPlan FromRecipe(WorldGenerationRecipe recipe)
    {
        recipe.Validate();
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
        steps.Add(new("current", WorldStepKind.PostEnrichmentAnalysis, new AnalysisStepParameters(), [last]));
        return new(recipe.World, recipe.Window, steps);
    }

    public WorldGenerationPlan Validate()
    {
        World.Validate(); Window.Validate();
        if (Steps is null || Steps.Count is < 2 or > 24) throw new ArgumentException("A world plan needs 2..24 steps.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<WorldStepKind>> ancestry = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> ancestorIds = new(StringComparer.Ordinal);
        string? lastTerrainStep = null;
        bool natural = false, analyzed = false;
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
                    if (!analyzed || step.Parameters is not AnalysisStepParameters || !ancestors.Contains(WorldStepKind.TerrainAnalysis))
                        throw new ArgumentException("Post analysis requires original analysis.");
                    break;
                default:
                    if (!analyzed || step.Parameters is not EnrichmentStepParameters { Intensity: >= 1 and <= 3 } || !ancestors.Contains(WorldStepKind.TerrainAnalysis))
                        throw new ArgumentException("Enrichment requires original analysis and intensity 1..3.");
                    break;
            }
            seen.Add(step.Id);
            ancestors.Add(step.Kind); ancestry.Add(step.Id, ancestors);
            ancestorIds.Add(step.Id, ids);
            if (step.Kind == WorldStepKind.NaturalTerrain || step.Parameters is EnrichmentStepParameters) lastTerrainStep = step.Id;
        }
        if (!analyzed) throw new ArgumentException("World plans require original formation analysis.");
        return this;
    }
}

public sealed record WorldStageMeasurement(string StepId, WorldStepKind Stage, double Milliseconds, long AllocatedBytes);
public sealed record WorldGenerationMetrics(IReadOnlyList<WorldStageMeasurement> Stages, double FinalizationMilliseconds, long RetainedPreviewBytes)
{
    public double SamplingMilliseconds => Stages.Where(s => s.Stage == WorldStepKind.NaturalTerrain).Sum(s => s.Milliseconds);
}
public sealed record WorldStageDiagnostics(int ChangedCells, int FeaturesAdded, int WalkableArea, int ComponentCount, int LargestComponentArea,
    int RoutingSearches, int RoutingVisitedCells);
public sealed record WorldPreviewFrame(string StepId, string Stage, string Terrain, int[] ChangedCells, int[] WalkableComponents,
    int[] Chokepoints, IReadOnlyList<EntranceCandidate> Entrances, IReadOnlyList<TerrainOpportunity> Opportunities, WorldStageDiagnostics Diagnostics);
public sealed record WorldPreview(IReadOnlyList<string> TerrainPalette, IReadOnlyList<WorldPreviewFrame> Frames,
    IReadOnlyList<NaturalFormation> Formations, int[] FormationLabels, int[] BoundaryCells)
{
    // Raw retained data estimate, excluding CLR/browser object overhead; measured payload is reported separately.
    public long EstimatedRetainedBytes => Frames.Sum(f => (long)f.Terrain.Length * 2 +
        4L * (f.ChangedCells.Length + f.WalkableComponents.Length + f.Chokepoints.Length) +
        f.Entrances.Count * 96L + f.Opportunities.Count * 192L) + 4L * (FormationLabels.Length + BoundaryCells.Length) + Formations.Count * 256L;
}
public sealed record WorldGenerationExecution(WorldGenerationContext Context, WorldPreview Preview, IReadOnlyList<WorldStageMeasurement> Measurements);

public sealed partial class WorldGenerationContext
{
    public NaturalTerrainSample[] OriginalSamples { get; private set; } = [];
    public TerrainAnalysisSnapshot? OriginalAnalysis { get; internal set; }
    public TerrainAnalysisSnapshot? CurrentAnalysis { get; internal set; }
    public List<EnrichmentFeature> Features { get; } = [];
    public List<EntranceCandidate> Entrances { get; } = [];
    public List<TerrainOpportunity> FeatureOpportunities { get; } = [];
    public int Revision { get; internal set; }
    public int RoutingSearches { get; internal set; }
    public int RoutingVisitedCells { get; internal set; }
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
    {
        plan.Validate(); content ??= ContentBundle.Canonical();
        var context = new WorldGenerationContext(plan.World, plan.Window);
        List<WorldPreviewFrame> frames = []; List<WorldStageMeasurement> measurements = [];
        string[] palette = ["grass", "dirt", "tree", "solid-rock", "water", "stone-floor"];
        var paletteIndex = palette.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.Ordinal);
        string? previous = null;
        foreach (var step in plan.Steps)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            var featureStart = context.Features.Count; var searches = context.RoutingSearches; var visited = context.RoutingVisitedCells;
            switch (step.Kind)
            {
                case WorldStepKind.NaturalTerrain: context.SampleNaturalTerrain(); break;
                case WorldStepKind.TerrainAnalysis:
                    context.OriginalAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.OriginalSamples, content);
                    context.CurrentAnalysis = context.OriginalAnalysis; break;
                case WorldStepKind.PostEnrichmentAnalysis:
                    context.CurrentAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.Samples, content, context.Revision); break;
                case WorldStepKind.ForestEnrichment: ForestEnricher.Apply(context, step, content); break;
                case WorldStepKind.RockEnrichment: RockEnricher.Apply(context, step, content); break;
                case WorldStepKind.WaterEnrichment: WaterEnricher.Apply(context, step, content); break;
                case WorldStepKind.GrasslandEnrichment: GrasslandEnricher.Apply(context, step, content); break;
                default: throw new ArgumentException($"Step {step.Kind} is not available.");
            }
            var terrain = new string(context.Samples.Select(s => (char)('A' + paletteIndex[s.TerrainTypeId])).ToArray());
            var changed = previous is null ? [] : Enumerable.Range(0, terrain.Length).Where(i => terrain[i] != previous[i]).ToArray();
            if (step.Parameters is EnrichmentStepParameters)
            {
                if (changed.Length > 0) context.Revision++;
                context.CurrentAnalysis = TerrainAnalyzer.Analyze(plan.Window, context.Samples, content, context.Revision);
            }
            var analysis = context.CurrentAnalysis;
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
            frames.Add(new(step.Id, step.Kind.ToString(), terrain, changed, analysis?.WalkableLabels ?? [], analysis?.Chokepoints ?? [],
                context.Entrances.ToArray(), opportunities,
                new(changed.Length, context.Features.Count - featureStart, connectivity?.WalkableArea ?? 0, connectivity?.ComponentCount ?? 0,
                    connectivity?.LargestComponentArea ?? 0, context.RoutingSearches - searches, context.RoutingVisitedCells - visited)));
            previous = terrain;
            measurements.Add(new(step.Id, step.Kind, timer.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated));
        }
        var original = context.OriginalAnalysis!;
        // Analysis is read-only, so its overlay also describes the preceding natural frame.
        frames[0] = frames[0] with
        {
            WalkableComponents = original.WalkableLabels, Chokepoints = original.Chokepoints, Opportunities = original.Opportunities,
            Diagnostics = frames[0].Diagnostics with { WalkableArea = original.Connectivity.WalkableArea,
                ComponentCount = original.Connectivity.ComponentCount, LargestComponentArea = original.Connectivity.LargestComponentArea }
        };
        return new(context, new(palette, frames, original.Formations, original.FormationLabels,
            Enumerable.Range(0, original.Boundary.Length).Where(i => original.Boundary[i]).ToArray()), measurements);
    }
}
