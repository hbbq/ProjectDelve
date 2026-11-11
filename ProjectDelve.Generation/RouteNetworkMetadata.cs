using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum RouteIntentStatus { Accepted, Unresolved, DeliberatelyRemote, RejectedAlternative }
public sealed record RouteIntent(string Id, RoutePurpose Purpose, RouteProfileKind Profile, int StartIndex, int? DestinationIndex,
    string? DestinationObjectId, RouteIntentStatus Status, string? RouteId, string? Reason);
public sealed record RouteJunction(string Id, GeneratedArea Contact, IReadOnlyList<string> RouteIds);
public sealed record SharedRouteSegment(string Id, GeneratedArea Area, IReadOnlyList<string> RouteIds);
public sealed record SiteRouteAttachment(string SiteId, string PortId, IReadOnlyList<string> RouteIds, int NetworkComponent,
    bool OrdinaryOpenAccess, bool DoorsOpenableAccess);
public sealed record RouteComponent(int Label, int Area, IReadOnlyList<string> RouteIds);
public sealed record RouteTopology(long BoardRevision, IReadOnlyList<RouteComponent> Components, ConnectivitySummary OrdinaryOpenBoard,
    ConnectivitySummary DoorsOpenableBoard);
public sealed record AlternativeRouteQuality(string RouteId, int PreviousDistance, int NewDistance, int NewCorridorCells,
    int SharedCorridorCells, bool AddsCycle, bool BypassesSelectedBottleneck, bool IndependentSiteEntrance)
{
    public bool ConnectsRegion { get; init; }
}
public sealed record RouteNetworkStatistics(int Searches, int ExpandedStates, int PeakQueue, int AcceptedRoutes, int ChangedTerrain,
    int ChangedEdges, int PreparationCost, int Crossings, int TunnelEntrances, long PlanningWork = 0, int PlanningAttempts = 0);
public sealed record RouteNetworkSnapshot(IReadOnlyList<RouteInfo> Routes, IReadOnlyList<RouteIntent> Intents,
    IReadOnlyList<RouteJunction> Junctions, IReadOnlyList<SharedRouteSegment> SharedSegments, IReadOnlyList<SiteRouteAttachment> SiteAttachments,
    IReadOnlyList<AlternativeRouteQuality> Alternatives, RouteTopology Topology, RouteNetworkStatistics Statistics);

public sealed partial class WorldGenerationContext
{
    public List<RouteInfo> Routes { get; } = [];
    public List<RouteIntent> RouteIntents { get; } = [];
    public List<AlternativeRouteQuality> RouteAlternatives { get; } = [];
    public RouteNetworkSnapshot? RouteNetwork { get; internal set; }
    public DensityAllocation? ThroughRouteDensity { get; internal set; }
    public DensityAllocation? ExplorationLinkDensity { get; internal set; }
    internal double RouteSearchMilliseconds { get; set; }
    internal double RouteMaterializationMilliseconds { get; set; }
    internal long RoutePlanningWork { get; set; }
    internal int RoutePlanningAttempts { get; set; }
    internal static string WindowPath(string stepId, WorldWindow window) =>
        FormattableString.Invariant($"{stepId}/x{window.OffsetX}-y{window.OffsetY}-w{window.Width}-h{window.Height}");
}

internal static class RouteTopologyBuilder
{
    internal static RouteNetworkSnapshot Build(WorldGenerationContext context)
    {
        var board = context.ConstructionBoard!; var w = board.Width; var n = w * board.Height;
        var members = board.State.RouteCorridors;
        var properties = board.Content.Terrain.ToDictionary(t => t.Id, t => t.Passable, StringComparer.Ordinal);
        var passable = Enumerable.Range(0, n).Select(i => properties[board.TerrainAt(i)]).ToArray();
        bool Linked(int a, int b) => board.EdgeAt(EdgeKey.Between(a, b, w)).Passable();
        var labels = Enumerable.Repeat(-1, n).ToArray(); var queue = new Queue<int>(); var components = new List<RouteComponent>();
        foreach (var start in members.Keys.Order())
        {
            if (labels[start] >= 0 || !passable[start]) continue;
            var label = components.Count; var area = 0; var ids = new HashSet<string>(StringComparer.Ordinal);
            labels[start] = label; queue.Enqueue(start);
            while (queue.TryDequeue(out var i))
            {
                area++; ids.UnionWith(members[i]);
                foreach (var j in board.Neighbors(i)) if (members.ContainsKey(j) && passable[j] && labels[j] < 0 && Linked(i, j))
                { labels[j] = label; queue.Enqueue(j); }
            }
            components.Add(new(label, area, ids.Order(StringComparer.Ordinal).ToArray()));
        }
        var shared = new List<SharedRouteSegment>(); var junctions = new List<RouteJunction>(); var visited = new HashSet<int>();
        var signatures = members.Where(p => p.Value.Count > 1).ToDictionary(p => p.Key, p => string.Join("|", p.Value.Order(StringComparer.Ordinal)));
        foreach (var start in signatures.Keys.Order())
        {
            if (!visited.Add(start)) continue;
            var cells = new HashSet<int> { start }; queue.Enqueue(start);
            while (queue.TryDequeue(out var i)) foreach (var j in board.Neighbors(i))
                if (signatures.GetValueOrDefault(j) == signatures[start] && Linked(i, j) && visited.Add(j)) { cells.Add(j); queue.Enqueue(j); }
            var id = $"shared/{context.Window.OffsetX + start % w}/{context.Window.OffsetY + start / w}";
            var ids = members[start].Order(StringComparer.Ordinal).ToArray();
            shared.Add(new(id, GeneratedArea.From(id + "/area", cells, w), ids));
            var contacts = new HashSet<int>();
            foreach (var route in context.Routes.Where(r => ids.Contains(r.Id)))
            {
                var run = route.Geometry.LocalIndices(w, context.Window).ToArray();
                for (var k = 0; k < run.Length; k++) if (cells.Contains(run[k]) &&
                    (k == 0 || k == run.Length - 1 || !cells.Contains(run[k - 1]) || !cells.Contains(run[k + 1]))) contacts.Add(run[k]);
            }
            foreach (var i in contacts.DefaultIfEmpty(start).Order())
                junctions.Add(new(id + $"/junction-{i}", GeneratedArea.From(id + $"/contact-{i}", [i], w), ids));
        }
        var openLabels = Enumerable.Repeat(-1, n).ToArray(); var openAreas = new List<int>();
        for (var start = 0; start < n; start++)
        {
            if (!passable[start] || openLabels[start] >= 0) continue;
            var area = 0; var label = openAreas.Count; openLabels[start] = label; queue.Enqueue(start);
            while (queue.TryDequeue(out var i)) { area++; foreach (var j in board.Neighbors(i)) if (passable[j] && openLabels[j] < 0 && Linked(i, j)) { openLabels[j] = label; queue.Enqueue(j); } }
            openAreas.Add(area);
        }
        var open = new ConnectivitySummary(openAreas.Sum(), openAreas.Count, openAreas, openAreas.DefaultIfEmpty().Max());
        var potential = context.CurrentAnalysis!.Connectivity;
        var attachments = new List<SiteRouteAttachment>();
        foreach (var site in context.Sites)
        foreach (var port in context.Routes.SelectMany(r => new[] { r.Start, r.End }).Where(p => p.SiteId == site.Id).DistinctBy(p => p.Id))
        {
            var ids = board.RouteMemberships(port.AnchorIndex); var outside = site.Entrances[0].OutsideIndex;
            attachments.Add(new(site.Id, port.Id, ids, labels[port.AnchorIndex],
                openLabels[outside] >= 0 && openLabels[outside] == openLabels[port.AnchorIndex],
                context.CurrentAnalysis.WalkableLabels[outside] >= 0 && context.CurrentAnalysis.WalkableLabels[outside] == context.CurrentAnalysis.WalkableLabels[port.AnchorIndex]));
        }
        // A frontage can meet a pre-existing route directly, without generating a
        // new route whose endpoint refers back to this Site. Report that attachment
        // from the same exact ports and current topology.
        foreach (var site in context.Sites)
        foreach (var port in site.AccessPorts.Where(p => board.IsRouteCorridor(p.OutwardIndex) &&
            !attachments.Any(a => a.SiteId == site.Id && a.PortId == p.Id)))
        {
            var inside = port.AnchorIndex; var outside = port.OutwardIndex;
            attachments.Add(new(site.Id, port.Id, board.RouteMemberships(outside), labels[outside],
                openLabels[inside] >= 0 && openLabels[inside] == openLabels[outside],
                context.CurrentAnalysis.WalkableLabels[inside] >= 0 &&
                context.CurrentAnalysis.WalkableLabels[inside] == context.CurrentAnalysis.WalkableLabels[outside]));
        }
        return new(context.Routes.ToArray(), context.RouteIntents.ToArray(), junctions, shared, attachments, context.RouteAlternatives.ToArray(),
            new(board.Revision, components, open, potential), new(context.Routes.Sum(r => r.Stats.Attempts), context.Routes.Sum(r => r.Stats.ExpandedStates),
                context.Routes.Select(r => r.Stats.QueuePeak).DefaultIfEmpty().Max(), context.Routes.Count,
                context.Routes.Sum(r => r.Preparation.ChangedTerrain), context.Routes.Sum(r => r.Preparation.ChangedEdges),
                context.Routes.Sum(r => r.Preparation.Cost), context.Routes.Sum(r => r.Crossings.Count), context.Routes.Sum(r => r.TunnelEntrances.Count), context.RoutePlanningWork, context.RoutePlanningAttempts));
    }
}
