using ProjectDelve.Engine;
using System.Diagnostics;

namespace ProjectDelve.Generation;

public static class RouteNetworkGenerator
{
    public static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var parameters = (RouteNetworkStepParameters)step.Parameters;
        if (!parameters.Settings.Enabled) return;
        var board = context.EnsurePlanningBoard(content);
        var path = WorldGenerationContext.WindowPath(step.Id, context.Window);
        var area = new CellBounds(0, 0, board.Width, board.Height);
        var budget = new PlanningBudget(128, 2_000_000);
        var draft = board.BeginDraft(area, path, budget);
        var scope = new GenerationScope(context.World.Seed, path, draft);
        var operations = new List<ConstructionOperation> { new(path, null, parameters.Mode.ToString(), path, area, [], []) };
        var routes = new List<RouteInfo>(); var intents = new List<RouteIntent>();
        var alternatives = new List<AlternativeRouteQuality>();
        var submitted = 0;
        if (parameters.Mode == RouteNetworkMode.PrimaryRoutes)
        {
            var axis = RouteSeed.Stream(context.World.Seed, path, "selection").Next(2);
            for (var ordinal = 0; ordinal < DensityScaling.ThroughRoutes(context.Window, parameters.Settings.ThroughRoutes); ordinal++)
            {
                var key = "through-" + ordinal;
                if (Math.Max(board.Width, board.Height) < 4)
                {
                    intents.Add(new(scope.ObjectId(key), RoutePurpose.ThroughRoute, parameters.Settings.Profile, 0, null, null,
                        RouteIntentStatus.Unresolved, null, "Window is too short for separated boundary ports.")); continue;
                }
                var random = RouteSeed.Stream(context.World.Seed, path + "/" + key, "endpoints");
                var horizontal = (axis + ordinal) % 2 == 0;
                var profile = parameters.Settings.Profile;
                var requirement = RouteRequirements.For(profile, parameters.Settings.Intensity, parameters.Settings.WaterCrossings);
                if (board.Width < 4) horizontal = false;
                if (board.Height < 4) horizontal = true;
                var startSide = horizontal ? CardinalSide.West : CardinalSide.North;
                var endSide = horizontal ? CardinalSide.East : CardinalSide.South;
                var extent = horizontal ? board.Height : board.Width;
                var margin = Math.Min(6, Math.Max(0, (extent - requirement.Width) / 8));
                var startPosition = margin + random.Next(Math.Max(1, extent - requirement.Width + 1 - margin * 2));
                var endPosition = margin + random.Next(Math.Max(1, extent - requirement.Width + 1 - margin * 2));
                int[] PortCells(CardinalSide side, int position)
                {
                    var result = new List<int>();
                    for (var p = Math.Max(0, position - 2); p <= Math.Min(extent - requirement.Width, position + 2); p++)
                        result.Add(side switch { CardinalSide.West => p * board.Width, CardinalSide.East => p * board.Width + board.Width - requirement.Width,
                            CardinalSide.North => p, _ => (board.Height - requirement.Width) * board.Width + p });
                    return result.ToArray();
                }
                var start = new RouteEndpoint(path + "/" + key + "/entry", PortCells(startSide, startPosition), RouteEndpointKind.Boundary, startSide);
                var destination = new RouteEndpoint(path + "/" + key + "/exit", PortCells(endSide, endPosition), RouteEndpointKind.Boundary, endSide);
                Plan(new(key, area, start, destination, profile, RoutePurpose.ThroughRoute, context.Window, requirement));
            }
        }
        if (parameters.Mode == RouteNetworkMode.ExplorationLinks)
        {
            foreach (var candidate in ExplorationRoutes.Candidates(context, scope, parameters.Settings))
            {
                if (alternatives.Count >= DensityScaling.ExplorationLinks(context.Window, parameters.Settings.Alternatives)) break;
                Plan(candidate.Request, accept: plan =>
                {
                    var quality = ExplorationRoutes.Quality(draft, plan, candidate);
                    if (quality is null) return false;
                    alternatives.Add(quality); return true;
                });
            }
        }
        if (parameters.Mode == RouteNetworkMode.SiteConnections && parameters.Settings.ConnectSites)
        {
            var sites = context.Sites.OrderBy(s => s.Kind == GeneratedSiteKind.Settlement ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
            bool Remote(SiteInfo site, SiteConnectionRequest request) => request.DeliberatelyRemote || !request.Required && site.Kind == GeneratedSiteKind.Campsite && RouteSeed.Stream(context.World.Seed, site.Id, "remote").Next(5) == 0;
            var hub = sites.FirstOrDefault(s => s.AccessPorts.Count > 0 && s.ConnectionRequests.Any(r => !Remote(s, r)));
            foreach (var site in sites)
            {
                foreach (var connection in site.ConnectionRequests)
                {
                    var port = site.AccessPorts.FirstOrDefault(p => p.Id == connection.PortId);
                    if (port is null) { intents.Add(new(connection.Id, RoutePurpose.SiteConnection, parameters.Settings.Profile, 0, null, site.Id, RouteIntentStatus.Unresolved, null, "Published access port is missing.")); continue; }
                    var network = Enumerable.Range(0, board.Width * board.Height).Where(draft.IsRouteCorridor).ToArray();
                    if (Remote(site, connection))
                    {
                        intents.Add(new(connection.Id, RoutePurpose.SiteConnection, RouteProfileKind.ForestTrail, port.OutwardIndex, null, site.Id, RouteIntentStatus.DeliberatelyRemote, null, "Optional remote Campsite retained.")); continue;
                    }
                    if (network.Length == 0 && hub?.Id == site.Id) continue;
                    var hubPort = network.Length == 0 ? hub?.AccessPorts.FirstOrDefault() : null;
                    if (network.Length == 0 && hubPort is null)
                    { intents.Add(new(connection.Id, RoutePurpose.SiteConnection, parameters.Settings.Profile, port.OutwardIndex, null, site.Id, RouteIntentStatus.Unresolved, null, "No compatible network or other Site.")); continue; }
                    var profile = site.Kind == GeneratedSiteKind.Campsite ? RouteProfileKind.ForestTrail : RouteProfileKind.MinorPath;
                    var req = RouteRequirements.For(profile, parameters.Settings.Intensity, parameters.Settings.WaterCrossings);
                    var grants = hubPort is null ? new[] { port.Permission } : new[] { port.Permission, hubPort.Permission };
                    var requestKey = "connect-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(connection.Id)))[..24];
                    Plan(new(requestKey, area,
                        new(port.Id, [port.OutwardIndex], RouteEndpointKind.Site, port.Direction, site.Id),
                        hubPort is null ? new(path + "/network", network, RouteEndpointKind.Network) : new(hubPort.Id, [hubPort.OutwardIndex], RouteEndpointKind.Site, hubPort.Direction, hub!.Id),
                        profile, RoutePurpose.SiteConnection, context.Window, req, AccessGrants: grants), connection.Id);
                }
            }
            if (hub is not null)
            foreach (var connection in hub.ConnectionRequests.Where(c => !intents.Any(i => i.Id == c.Id)))
            {
                var route = routes.FirstOrDefault(r => r.Start.Id == connection.PortId || r.End.Id == connection.PortId);
                var port = hub.AccessPorts.First(p => p.Id == connection.PortId);
                intents.Add(new(connection.Id, RoutePurpose.SiteConnection, parameters.Settings.Profile, port.OutwardIndex, null, hub.Id,
                    route is null ? RouteIntentStatus.Unresolved : RouteIntentStatus.Accepted, route?.Id, route is null ? "No accepted branch reaches the Site hub." : null));
            }
        }
        if (routes.Count > 0)
        {
            if (board.TryCommit(draft.BuildPatch(), out var error))
            {
                context.Routes.AddRange(routes); context.Construction.AddRange(operations); context.RouteAlternatives.AddRange(alternatives);
            }
            else
            {
                intents = intents.Select(i => i.Status == RouteIntentStatus.Accepted ? i with { Status = RouteIntentStatus.Unresolved, RouteId = null, Reason = error } : i).ToList();
                context.PlanningFailures.Add(new(path, "RouteNetwork", PlacementFailureCode.ValidationFailed, error!, budget.Attempts));
            }
        }
        context.RouteIntents.AddRange(intents);
        if (parameters.Mode == RouteNetworkMode.PrimaryRoutes)
            context.ThroughRouteDensity = new(DensityScaling.ThroughRoutes(context.Window, parameters.Settings.ThroughRoutes), submitted, intents.Count(i => i.Status == RouteIntentStatus.Accepted));
        if (parameters.Mode == RouteNetworkMode.ExplorationLinks)
        {
            var requested = DensityScaling.ExplorationLinks(context.Window, parameters.Settings.Alternatives);
            var accepted = intents.Count(i => i.Status == RouteIntentStatus.Accepted);
            context.ExplorationLinkDensity = new(requested, submitted, accepted,
                accepted >= requested ? null : submitted == 0
                    ? "No useful exploration candidates remain in the current network and terrain."
                    : "Available useful candidates or planning and disturbance allowances could not satisfy the exploration target.");
        }
        context.RoutePlanningWork += budget.Work; context.RoutePlanningAttempts += budget.Attempts;
        context.RefreshConstructionAnalysis();
        context.RouteNetwork = RouteTopologyBuilder.Build(context);

        RouteInfo? Plan(RouteRequest request, string? requirementId = null, Func<RoutePlan, bool>? accept = null)
        {
            submitted++;
            var clock = Stopwatch.StartNew();
            var result = RouteGenerator.Plan(request, scope);
            context.RouteSearchMilliseconds += clock.Elapsed.TotalMilliseconds;
            if (result is Rejected<RoutePlan> failure)
            {
                if (context.PlanningFailures.Count < 64) context.PlanningFailures.Add(failure.Failure);
                intents.Add(new(requirementId ?? scope.ObjectId(request.Key), request.Purpose, request.ProfileKind, request.Start.Cells.FirstOrDefault(),
                    request.Destination.Cells.Cast<int?>().FirstOrDefault(), request.Destination.SiteId ?? request.Destination.Id, RouteIntentStatus.Unresolved, null, failure.Failure.Reason));
                return null;
            }
            var plan = ((Planned<RoutePlan>)result).Plan;
            if (context.Routes.Concat(routes).Sum(r => r.Preparation.ChangedTerrain) + plan.Route.Preparation.ChangedTerrain > parameters.Settings.Intensity * 1000 ||
                context.Routes.Concat(routes).Sum(r => r.Preparation.RockExcavated) + plan.Route.Preparation.RockExcavated > parameters.Settings.Intensity * 400 ||
                context.Routes.Concat(routes).Sum(r => r.Preparation.WaterFilled) + plan.Route.Preparation.WaterFilled > parameters.Settings.Intensity * 36 ||
                accept is not null && !accept(plan))
            {
                intents.Add(new(requirementId ?? plan.Route.Id, request.Purpose, request.ProfileKind, plan.Route.Start.AnchorIndex, plan.Route.End.AnchorIndex,
                    plan.Route.End.Id, request.Purpose == RoutePurpose.ExplorationLink ? RouteIntentStatus.RejectedAlternative : RouteIntentStatus.Unresolved,
                    null, "Candidate exceeds cumulative disturbance limits or adds no useful alternative."));
                return null;
            }
            if (!draft.Accept(plan.Patch, out var error)) throw new InvalidOperationException(error);
            routes.Add(plan.Route); operations.AddRange(plan.Operations);
            intents.Add(new(requirementId ?? plan.Route.Id, request.Purpose, request.ProfileKind, plan.Route.Start.AnchorIndex, plan.Route.End.AnchorIndex,
                plan.Route.End.SiteId ?? plan.Route.End.Id, RouteIntentStatus.Accepted, plan.Route.Id, null));
            context.RoutingSearches += plan.Route.Stats.Attempts; context.RoutingVisitedCells += plan.Route.Stats.ExpandedStates;
            context.RouteSearchMilliseconds -= plan.MaterializationMilliseconds; context.RouteMaterializationMilliseconds += plan.MaterializationMilliseconds;
            return plan.Route;
        }
    }
}
