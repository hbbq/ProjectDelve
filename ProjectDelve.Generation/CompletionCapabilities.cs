using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Producer-owned completion permissions. Preferred route ports are not an exhaustive boundary declaration.</summary>
internal static class CompletionCapabilities
{
    private const string Owner = "geographic-completion";

    internal static ConstructionCapabilities Capture(PlanningBoard board, IReadOnlyCollection<SiteInfo> sites,
        IReadOnlySet<int> naturalCells, HeroDeployment deployment)
    {
        var siteProtection = ConstructionProtections.FromSites(sites, board.Width);
        var deploymentCells = deployment.Area.Indices(board.Width).Append(deployment.DepartureIndex).ToHashSet();
        var protections = new ConstructionProtections(siteProtection.Cells.Concat(sites.SelectMany(site =>
                site.Footprint.Indices(board.Width).Select(i => new ConstructionCellProtection(i, site.Id,
                    "Immutable published Site geometry", site.Id)))).Concat(deploymentCells.Select(i =>
            new ConstructionCellProtection(i, "hero-deployment", "Immutable Hero deployment and departure", "hero-deployment"))).ToArray(),
            siteProtection.Edges);
        var protectedCells = protections.Cells.Select(p => p.Index).ToHashSet();
        var grants = new Dictionary<string, GrantBuilder>(StringComparer.Ordinal);
        var interfaces = new List<ConstructionInterface>();
        var knownOwners = sites.Select(s => s.Id).Concat(sites.SelectMany(s => s.Buildings.Select(b => b.Id)))
            .Append("hero-deployment").ToHashSet(StringComparer.Ordinal);
        // Unpublished reservation metadata cannot certify exhaustive legal interfaces, even when
        // the currently selected conversions happen not to touch that owner's boundary.
        var interfacesComplete = board.State.Reservations.Values.All(owner =>
            owner == "\0protected" || knownOwners.Contains(owner));
        string? Reservation(int index) => board.State.Reservations.GetValueOrDefault(index);
        bool PotentialEdge(int inside, int outside) => board.EdgeAt(EdgeKey.Between(inside, outside, board.Width))
            is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor;
        GrantBuilder Grant(string owner)
        {
            if (!grants.TryGetValue(owner, out var grant)) grants.Add(owner, grant = new());
            return grant;
        }
        void DeclareContact(string source, int inside, int outside)
        {
            board.CheckIndex(inside); board.CheckIndex(outside);
            var owner = Reservation(inside);
            if (owner is null || owner.Contains('\0') || !PotentialEdge(inside, outside)) return;
            Grant(owner).Traverse.Add(inside);
            if (Reservation(outside) != owner)
                interfaces.Add(new($"{source}/boundary/{inside}/{outside}", inside, outside, owner));
        }
        void DeclareContacts(string source, IEnumerable<int> authorized)
        {
            foreach (var inside in authorized.Distinct().Order())
            {
                board.CheckIndex(inside);
                var owner = Reservation(inside);
                if (owner is null || owner.Contains('\0')) continue;
                Grant(owner).Traverse.Add(inside);
                foreach (var outside in board.Neighbors(inside).Order())
                    DeclareContact(source, inside, outside);
            }
        }

        foreach (var site in sites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var shell = site.MountainDungeon?.ProtectedRock.Indices(board.Width).ToHashSet() ?? [];
            var buildingCells = site.Buildings.SelectMany(b => b.Footprint.Indices(board.Width)).ToHashSet();
            // Exact, producer-published writable aprons are the sole exception to foreign reservation writes.
            foreach (var port in site.AccessPorts.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                var permission = port.Permission;
                var grant = Grant(permission.ExactOwnerId);
                foreach (var cell in permission.TraverseCells)
                    if (Reservation(cell) == permission.ExactOwnerId && !shell.Contains(cell)) grant.Traverse.Add(cell);
                foreach (var cell in permission.WritableCells)
                    if (Reservation(cell) == permission.ExactOwnerId && !protectedCells.Contains(cell))
                    { grant.Traverse.Add(cell); grant.Writable.Add(cell); }
                foreach (var edge in permission.WritableEdges)
                {
                    var end = board.CheckEdge(edge);
                    if (board.EdgeAt(edge) == EdgeKind.None && !protectedCells.Contains(edge.Index) && !protectedCells.Contains(end) &&
                        (grant.Traverse.Contains(edge.Index) || grant.Traverse.Contains(end))) grant.Edges.Add(edge);
                }
            }
            IEnumerable<int> authorized = site.Exterior.Indices(board.Width);
            if (site.MountainDungeon is not null)
                authorized = authorized.Concat(site.AccessPorts.SelectMany(p => p.Approach.Indices(board.Width)));
            else if (site.Kind == GeneratedSiteKind.Campsite)
                authorized = authorized.Concat(site.Footprint.Indices(board.Width));
            else if (site.SettlementLayout is { } settlement)
                authorized = authorized.Concat(settlement.Access.Indices(board.Width))
                    .Concat(settlement.PublicSpaces.SelectMany(p => p.Area.Indices(board.Width)))
                    .Concat(site.Entrances.Select(p => p.OutsideIndex));
            var explicitAprons = site.AccessPorts.SelectMany(p => p.Permission.WritableCells).ToHashSet();
            // Blocking furniture, protected shells and building interiors never become preparation contacts.
            authorized = authorized.Concat(explicitAprons).Where(i => !shell.Contains(i) && !buildingCells.Contains(i) &&
                (board.Passable(i) || explicitAprons.Contains(i)) && Reservation(i) == site.Id);
            DeclareContacts(site.Id, authorized);
            // An accepted external Building portal authorizes precisely its physical contact.
            // It does not authorize preparation against the rest of the private interior boundary.
            foreach (var building in site.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            foreach (var portal in building.Portals.OrderBy(p => p.Id, StringComparer.Ordinal))
                if (building.Footprint.Contains(portal.InsideIndex, board.Width) &&
                    !building.Footprint.Contains(portal.OutsideIndex, board.Width) &&
                    board.Passable(portal.InsideIndex) && !shell.Contains(portal.OutsideIndex) &&
                    PotentialEdge(portal.InsideIndex, portal.OutsideIndex))
                {
                    DeclareContact(portal.Id, portal.InsideIndex, portal.OutsideIndex);
                    DeclareContact(portal.Id, portal.OutsideIndex, portal.InsideIndex);
                }
        }
        // Spawn protection forbids writes, while all its boundary land remains an explicit read-only contact.
        DeclareContacts("hero-deployment", deploymentCells.Where(i => Reservation(i) == "hero-deployment"));
        var publishedGrants = grants.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            new RouteAccessGrant(p.Key, p.Value.Traverse.Order().ToArray(), p.Value.Writable.Order().ToArray(),
                p.Value.Edges.OrderBy(e => e.Index).ThenBy(e => e.Direction).ToArray())).ToArray();
        var publishedInterfaces = interfaces.DistinctBy(i => (i.ExactOwnerId, i.InsideIndex, i.OutsideIndex))
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var baseline = ConstructionCapabilities.Capture(board, Owner, [], publishedGrants, publishedInterfaces,
            conversionsComplete: false, interfacesComplete: interfacesComplete, protections: protections);
        if (baseline.Error is not null) return baseline;
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), Owner, new());
        draft.EnableRoutePlanning(publishedGrants);
        var candidates = HeroDeploymentPlanner.NaturalConversions(board, naturalCells)
            .Where(c => !protectedCells.Contains(c.Index) && draft.CanWrite(c.Index)).ToDictionary(c => c.Index);
        var rejected = new HashSet<int>();
        var complete = true;
        bool Declared(int inside, int outside)
        {
            var foreign = Reservation(inside);
            return foreign is null || foreign == Owner || foreign == Reservation(outside) ||
                publishedInterfaces.Any(p => p.ExactOwnerId == foreign && p.InsideIndex == inside && p.OutsideIndex == outside);
        }
        bool LandNow(int index) => baseline.CurrentGeometry.CanStop(ConstructionCapabilities.OrdinaryLand,
            new(index % board.Width, index / board.Width));
        foreach (var conversion in candidates.Values.OrderBy(c => c.Index))
        foreach (var neighbor in board.Neighbors(conversion.Index))
        {
            if (!PotentialEdge(conversion.Index, neighbor) ||
                Declared(conversion.Index, neighbor) && Declared(neighbor, conversion.Index)) continue;
            if (LandNow(neighbor))
            {
                rejected.Add(conversion.Index);
                if (Reservation(neighbor) is { } owner && !knownOwners.Contains(owner)) complete = false;
            }
        }
        // Two individually blocked Cells can expose a new foreign contact only in the combined patch.
        // Such alternatives do not establish an exhaustive mutually compatible operation set.
        foreach (var conversion in candidates.Values.Where(c => !rejected.Contains(c.Index)).OrderBy(c => c.Index))
        foreach (var neighbor in board.Neighbors(conversion.Index))
            if (candidates.ContainsKey(neighbor) && !rejected.Contains(neighbor) && PotentialEdge(conversion.Index, neighbor) &&
                (!Declared(conversion.Index, neighbor) || !Declared(neighbor, conversion.Index)))
            { rejected.Add(conversion.Index); rejected.Add(neighbor); complete = false; }
        return ConstructionCapabilities.Capture(board, Owner, candidates.Values.Where(c => !rejected.Contains(c.Index)),
            publishedGrants, publishedInterfaces, conversionsComplete: complete, interfacesComplete: interfacesComplete, protections: protections);
    }

    private sealed class GrantBuilder
    {
        internal HashSet<int> Traverse { get; } = [];
        internal HashSet<int> Writable { get; } = [];
        internal HashSet<EdgeKey> Edges { get; } = [];
    }
}
