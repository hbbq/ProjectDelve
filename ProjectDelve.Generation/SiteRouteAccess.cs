using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record SiteAccessPort(string Id, string OwnerId, int AnchorIndex, int OutwardIndex, CardinalSide Direction,
    GeneratedArea Approach, int VerifiedWidth, RouteAccessGrant Permission);
public sealed record SiteConnectionRequest(string Id, string PortId, bool Required = false, bool DeliberatelyRemote = false);

internal static class SiteRouteAccess
{
    internal static int ProximityCost(CellBounds bounds, IReadOnlyList<int>? route, int width, int setback = 3)
    {
        if (route is null || route.Count == 0) return 0;
        var distance = route.Min(i => Math.Abs(i % width - Math.Clamp(i % width, bounds.X, bounds.X + bounds.Width - 1)) +
            Math.Abs(i / width - Math.Clamp(i / width, bounds.Y, bounds.Y + bounds.Height - 1)));
        return distance < setback ? (setback - distance) * 20 : Math.Max(0, distance - setback - 6) * 3;
    }

    internal static SiteInfo Publish(SiteInfo site, BoardDraft draft, SitePlacementPreferences? preferences)
    {
        var exterior = site.Exterior.Indices(draft.Width).ToHashSet(); var footprint = site.Footprint.Indices(draft.Width).ToHashSet();
        var start = site.Entrances[0].OutsideIndex; var parents = new Dictionary<int, int> { [start] = -1 }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var i)) foreach (var j in draft.Neighbors(i))
            if (exterior.Contains(j) && draft.EdgeAt(EdgeKey.Between(i, j, draft.Width)).Passable() && parents.TryAdd(j, i)) queue.Enqueue(j);
        var candidates = parents.Keys.SelectMany(i => draft.Neighbors(i).Where(j => !footprint.Contains(j) && !draft.IsReserved(j) &&
                SiteGeometry.Fits(new(j % draft.Width, j / draft.Width, 1, 1), draft.AllowedArea))
            .Select(j => (Inside: i, Outside: j, Side: j / draft.Width < i / draft.Width ? CardinalSide.North : j / draft.Width > i / draft.Width ? CardinalSide.South : j % draft.Width < i % draft.Width ? CardinalSide.West : CardinalSide.East)))
            .Where(p => draft.EdgeAt(EdgeKey.Between(p.Inside, p.Outside, draft.Width)).Passable())
            .OrderBy(p => draft.Passable(p.Outside) ? 0 : draft.TerrainAt(p.Outside) is "tree" or "fruit-tree" ? 1 : 2)
            .ThenBy(p => preferences?.PreferredApproach is { } desired && p.Side != desired ? 100 : 0)
            .ThenBy(p => preferences?.RouteAnchors is { Count: > 0 } route ? route.Min(i => Math.Abs(i % draft.Width - p.Outside % draft.Width) + Math.Abs(i / draft.Width - p.Outside / draft.Width)) : Math.Abs(p.Inside % draft.Width - start % draft.Width) + Math.Abs(p.Inside / draft.Width - start / draft.Width))
            .ThenBy(p => p.Inside).ThenBy(p => p.Outside).ToArray();
        var ports = new List<SiteAccessPort>();
        foreach (var candidate in candidates.GroupBy(c => c.Side).Select(g => g.First()).Take(4))
        {
            var path = new List<int>(); for (var i = candidate.Inside; i >= 0; i = parents[i]) path.Add(i);
            var id = site.Id + "/access-" + candidate.Side.ToString().ToLowerInvariant();
            RouteAccessGrant permission;
            if (!draft.IsRouteCorridor(candidate.Outside))
            {
                draft.Reserve([candidate.Outside]);
                permission = new(site.Id, path.Append(candidate.Outside).ToArray(), [candidate.Outside], [EdgeKey.Between(candidate.Inside, candidate.Outside, draft.Width)]);
            }
            else permission = new(site.Id, path, [], []);
            ports.Add(new(id, site.Id, candidate.Inside, candidate.Outside, candidate.Side, GeneratedArea.From(id + "/approach", path, draft.Width), 1, permission));
        }
        return site with { AccessPorts = ports, ConnectionRequests = [new(site.Id + "/external-connection", ports.Count == 0 ? site.Id + "/unavailable-access" : ports[0].Id)] };
    }
}
