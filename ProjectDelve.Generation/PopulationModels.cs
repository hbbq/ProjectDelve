using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum PopulationDensity { Off, Low, Medium, High }
public sealed record PopulationSettings(PopulationDensity Density = PopulationDensity.Medium, int Difficulty = 3)
{
    public PopulationSettings Validate()
    {
        if (!Enum.IsDefined(Density) || Difficulty is < 1 or > 5) throw new ArgumentException("Population requires a known density and difficulty 1..5.");
        return this;
    }
}
public sealed record PopulationStepParameters(PopulationSettings Settings) : WorldStepParameters;
public sealed record PopulationFailure(string RequestId, string? Kind, string? SourceId, string Reason);
public sealed record PopulationStatistics(int Target, int AcceptedEncounters, int Units, int Groups, int Attempts, long Work, int Shortfall)
{
    public int DormantUnits { get; init; }
    public int AwakeUnits { get; init; }
    public int InitialTokens { get; init; }
    public int RequestedUnits { get; init; }
    public int UnitShortfall { get; init; }
    public int LootMarkers { get; init; }
    public int QuietRegions { get; init; }
}
public sealed record PopulationRoleAssignment(string UnitId, string Role, string? AreaId = null);
public sealed record PopulationAlertComponent(int Index, IReadOnlyList<string> UnitIds);
public sealed record PopulationEncounter(string Id, string Kind, string SourceId, IReadOnlyList<string> GroupIds,
    IReadOnlyList<UnitPlacement> Units, IReadOnlyList<InitialGroupOrder> Orders, IReadOnlyList<Route> Routes,
    IReadOnlyList<Waypoint> Waypoints)
{
    public IReadOnlyList<AgencyAssignment> Agency { get; init; } = [];
    public IReadOnlyList<PopulationRoleAssignment> Roles { get; init; } = [];
    public int RequestedUnits { get; init; }
    public string? IntendedAlertTopology { get; init; }
    public int ActualAlertComponents { get; init; }
}
public sealed record PopulationSnapshot(IReadOnlyList<PopulationEncounter> Encounters, PopulationStatistics Statistics,
    IReadOnlyList<PopulationFailure> Failures)
{
    public IReadOnlyList<LootMarker> LootMarkers { get; init; } = [];
    public LootSettings? Loot { get; init; }
    public IReadOnlyList<PopulationAlertComponent> AlertComponents { get; init; } = [];
}
public sealed partial class WorldGenerationContext
{
    public PopulationSnapshot? Population { get; internal set; }
    // Authoring-only exclusions, independent of construction reservations.
    public List<GeneratedArea> PopulationProtectedAreas { get; } = [];
    internal ScenarioDefinition AddPopulation(ScenarioDefinition definition)
    {
        if (Population is null) return definition;
        return PopulationGenerator.Combine(definition, Population.Encounters) with
        { LootMarkers = [.. definition.LootMarkers ?? [], .. Population.LootMarkers], Loot = Population.Loot ?? definition.Loot };
    }
}

internal sealed record PopulationLocation(string Kind, string SourceId, int[] Cells, CellBounds Bounds, int[]? Path = null)
{
    public SitePopulationIntent? Intent { get; init; }
}
internal static partial class GoblinCampPopulationGenerator
{
    internal static IEnumerable<PopulationLocation> Locations(WorldGenerationContext c)
    {
        foreach (var site in c.Sites.Where(s => s.Kind == GeneratedSiteKind.Campsite).OrderBy(s => s.Id, StringComparer.Ordinal))
            yield return new("GoblinCamp", site.Id, site.Exterior.Indices(c.Window.Width).ToArray(), site.Exterior.Bounds)
            { Intent = site.PopulationIntent };
        foreach (var o in (c.CurrentAnalysis?.Opportunities ?? []).Concat(c.FeatureOpportunities)
            .Where(o => o.Kind == "clearing" && o.UsableArea >= 12 && o.WalkableComponent >= 0)
            .DistinctBy(o => o.Id).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
            var forbidden = c.Sites.SelectMany(s => s.Footprint.Indices(c.Window.Width)).Concat(c.Routes.SelectMany(r => r.Corridor.Indices(c.Window.Width))).ToHashSet();
            var cells = SiteGeometry.Cells(o.Bounds, c.Window.Width).Where(i => !forbidden.Contains(i) && c.CurrentAnalysis!.WalkableLabels[i] == o.WalkableComponent &&
                c.CurrentAnalysis.FormationLabels[i] == c.CurrentAnalysis.FormationLabels[o.AnchorIndex]).ToArray();
            yield return new("GoblinCamp", o.Id, cells, o.Bounds);
        }
    }
}
internal static partial class ZombieHousePopulationGenerator
{
    internal static IEnumerable<PopulationLocation> Locations(WorldGenerationContext c)
    {
        var w = c.Window.Width; var n = c.Samples.Length;
        var edges = c.ConstructionEdges().ToDictionary(e => new EdgeKey(e.Position.Y * w + e.Position.X, e.Direction), e => e.Kind);
        var passability = (c.ConstructionBoard?.Content ?? ContentBundle.Canonical()).Terrain.ToDictionary(t => t.Id, t => t.Passable, StringComparer.Ordinal);
        bool Passable(int i) => i >= 0 && i < n && (c.ConstructionBoard?.Passable(i) ?? passability[c.Samples[i].TerrainTypeId]);
        bool Linked(int a, int b) => edges.GetValueOrDefault(EdgeKey.Between(a, b, w), EdgeKind.None) is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor;
        IEnumerable<int> Neighbors(int i)
        {
            if (i >= w) yield return i - w;
            if (i % w > 0) yield return i - 1;
            if (i % w + 1 < w) yield return i + 1;
            if (i + w < n) yield return i + w;
        }
        foreach (var b in c.Sites.SelectMany(s => s.Buildings).OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var footprint = b.Footprint.Indices(w).ToHashSet();
            var entrances = b.Portals.Where(p => footprint.Contains(p.InsideIndex) && !footprint.Contains(p.OutsideIndex) &&
                Passable(p.InsideIndex) && Passable(p.OutsideIndex) && Linked(p.InsideIndex, p.OutsideIndex)).ToArray();
            if (entrances.Length == 0) continue;
            var exterior = entrances.Select(p => p.OutsideIndex).ToHashSet();
            HashSet<int> reached = []; Queue<int> queue = new();
            foreach (var entrance in entrances) if (reached.Add(entrance.OutsideIndex)) queue.Enqueue(entrance.OutsideIndex);
            while (queue.TryDequeue(out var i)) foreach (var j in Neighbors(i))
                if ((footprint.Contains(j) || exterior.Contains(j)) && Passable(j) && Linked(i, j) && reached.Add(j)) queue.Enqueue(j);
            var cells = b.Rooms.SelectMany(r => r.Usable.Indices(w)).Where(reached.Contains).Distinct().Order().ToArray();
            if (cells.Length >= 2) yield return new("ZombieHouse", b.Id, cells, b.Footprint.Bounds);
        }
    }
}
internal static partial class RoadPatrolPopulationGenerator
{
    internal static IEnumerable<PopulationLocation> Locations(WorldGenerationContext c)
    {
        foreach (var r in c.Routes.Where(r => r.Profile == RouteProfileKind.Road && !r.RequiresOpeningDoors).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var all = r.Geometry.LocalIndices(c.Window.Width, c.Window).Distinct().ToArray();
            for (var offset = 0; offset + 6 <= all.Length && offset < 256; offset += 16)
            {
                var path = all.Skip(offset).Take(16).ToArray();
                var area = GeneratedArea.From(r.Id + "/segment-" + offset, path, c.Window.Width);
                yield return new("RoadPatrol", area.Id, path, area.Bounds, path);
            }
        }
    }
}
