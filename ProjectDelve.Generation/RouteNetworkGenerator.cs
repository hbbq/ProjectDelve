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
        // Trying a preferred footprint must not spend the original connection's allowance.
        var widthBudget = new PlanningBudget(32, 1_000_000);
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
                var requirement = RouteRequirements.For(profile, parameters.Settings.Intensity, parameters.Settings.WaterCrossings) with { Width = 1 };
                if (board.Width < 4) horizontal = false;
                if (board.Height < 4) horizontal = true;
                var startSide = horizontal ? CardinalSide.West : CardinalSide.North;
                var endSide = horizontal ? CardinalSide.East : CardinalSide.South;
                var extent = horizontal ? board.Height : board.Width;
                var margin = Math.Min(6, Math.Max(0, (extent - requirement.Width) / 8));
                var startPosition = margin + random.Next(Math.Max(1, extent - requirement.Width + 1 - margin * 2));
                var endPosition = margin + random.Next(Math.Max(1, extent - requirement.Width + 1 - margin * 2));
                int[] PortCells(CardinalSide side, int position, int width)
                {
                    var result = new List<int>();
                    for (var p = Math.Max(0, position - 2); p <= Math.Min(extent - width, position + 2); p++)
                        result.Add(side switch { CardinalSide.West => p * board.Width, CardinalSide.East => p * board.Width + board.Width - width,
                            CardinalSide.North => p, _ => (board.Height - width) * board.Width + p });
                    return result.ToArray();
                }
                var start = new RouteEndpoint(path + "/" + key + "/entry", PortCells(startSide, startPosition, 1), RouteEndpointKind.Boundary, startSide);
                var destination = new RouteEndpoint(path + "/" + key + "/exit", PortCells(endSide, endPosition, 1), RouteEndpointKind.Boundary, endSide);
                var request = new RouteRequest(key, area, start, destination, profile, RoutePurpose.ThroughRoute, context.Window, requirement);
                var wide = request with
                {
                    Start = start with { Cells = PortCells(startSide, startPosition, 2) },
                    Destination = destination with { Cells = PortCells(endSide, endPosition, 2) }
                };
                Plan(request, preferredWidth: wide);
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
                    var request = new RouteRequest(requestKey, area,
                        new(port.Id, [port.OutwardIndex], RouteEndpointKind.Site, port.Direction, site.Id),
                        hubPort is null ? new(path + "/network", network, RouteEndpointKind.Network) : new(hubPort.Id, [hubPort.OutwardIndex], RouteEndpointKind.Site, hubPort.Direction, hub!.Id),
                        profile, RoutePurpose.SiteConnection, context.Window, req, AccessGrants: grants);
                    var wide = site.Kind is GeneratedSiteKind.Settlement or GeneratedSiteKind.MountainDungeon || connection.Required
                        ? request with
                        {
                            Start = request.Start with { Cells = OutsideAnchors(port) },
                            Destination = hubPort is null ? request.Destination : request.Destination with { Cells = OutsideAnchors(hubPort) }
                        }
                        : null;
                    Plan(request, connection.Id, preferredWidth: wide);
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
            string? error;
            bool committed;
            if (context.Completion is { } completion)
            {
                committed = completion.TryPublish(draft.BuildPatch(), path, step.Id, out var diagnostic);
                error = diagnostic.Detail;
            }
            else committed = board.TryCommit(draft.BuildPatch(), out error);
            if (committed)
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
        context.RoutePlanningWork += budget.Work + widthBudget.Work;
        context.RoutePlanningAttempts += budget.Attempts + widthBudget.Attempts;
        context.RefreshConstructionAnalysis();
        context.RouteNetwork = RouteTopologyBuilder.Build(context);

        int[] OutsideAnchors(SiteAccessPort port)
        {
            // Keep the certified port in the endpoint square, with the second row
            // extending outward. The route receives only the original Site grant.
            var x = port.OutwardIndex % board.Width;
            var y = port.OutwardIndex / board.Width;
            (int X, int Y)[] positions = port.Direction switch
            {
                CardinalSide.North => [(x - 1, y - 1), (x, y - 1)],
                CardinalSide.South => [(x - 1, y), (x, y)],
                CardinalSide.West => [(x - 1, y - 1), (x - 1, y)],
                _ => [(x, y - 1), (x, y)]
            };
            return positions.Where(p => SiteGeometry.Fits(new(p.X, p.Y, 2, 2), area))
                .Select(p => p.Y * board.Width + p.X).ToArray();
        }

        void Plan(RouteRequest request, string? requirementId = null, Func<RoutePlan, bool>? accept = null,
            RouteRequest? preferredWidth = null)
        {
            submitted++;
            string? widthFailure = null;
            if (preferredWidth is not null)
            {
                var wide = preferredWidth with
                {
                    Requirements = request.Requirements! with { Width = 2, RequiredFootprint = 2, MaxAttempts = 1 }
                };
                if (wide.Start.Cells.Count > 0 && wide.Destination.Cells.Count > 0)
                {
                    // Plan against the accepted stage draft, with its original owners and grants.
                    var wideIntent = Attempt(wide, widthBudget, requirementId, null);
                    if (wideIntent.Status == RouteIntentStatus.Accepted) { intents.Add(wideIntent); return; }
                    widthFailure = wideIntent.Reason;
                }
                else widthFailure = "An endpoint has no room for a width-two footprint.";
            }
            var intent = Attempt(request, budget, requirementId, accept);
            if (widthFailure is not null)
                intent = intent with { Reason = ("Width-two preference skipped: " + widthFailure + " " + intent.Reason).TrimEnd() };
            intents.Add(intent);
        }

        RouteIntent Attempt(RouteRequest request, PlanningBudget planningBudget, string? requirementId, Func<RoutePlan, bool>? accept)
        {
            var clock = Stopwatch.StartNew();
            var result = RouteGenerator.Plan(request, scope, planningBudget);
            context.RouteSearchMilliseconds += clock.Elapsed.TotalMilliseconds;
            if (result is Rejected<RoutePlan> failure)
            {
                if (planningBudget == budget && context.PlanningFailures.Count < 64) context.PlanningFailures.Add(failure.Failure);
                return new(requirementId ?? scope.ObjectId(request.Key), request.Purpose, request.ProfileKind, request.Start.Cells.FirstOrDefault(),
                    request.Destination.Cells.Cast<int?>().FirstOrDefault(), request.Destination.SiteId ?? request.Destination.Id,
                    RouteIntentStatus.Unresolved, null, failure.Failure.Reason);
            }
            var plan = ((Planned<RoutePlan>)result).Plan;
            var intent = new RouteIntent(requirementId ?? plan.Route.Id, request.Purpose, request.ProfileKind, plan.Route.Start.AnchorIndex,
                plan.Route.End.AnchorIndex, plan.Route.End.SiteId ?? plan.Route.End.Id, RouteIntentStatus.Accepted, plan.Route.Id, null);
            if (context.Routes.Concat(routes).Sum(r => r.Preparation.ChangedTerrain) + plan.Route.Preparation.ChangedTerrain > parameters.Settings.Intensity * 1000 ||
                context.Routes.Concat(routes).Sum(r => r.Preparation.RockExcavated) + plan.Route.Preparation.RockExcavated > parameters.Settings.Intensity * 400 ||
                context.Routes.Concat(routes).Sum(r => r.Preparation.WaterFilled) + plan.Route.Preparation.WaterFilled > parameters.Settings.Intensity * 36 ||
                accept is not null && !accept(plan))
            {
                return intent with
                {
                    Status = request.Purpose == RoutePurpose.ExplorationLink ? RouteIntentStatus.RejectedAlternative : RouteIntentStatus.Unresolved,
                    RouteId = null, Reason = "Candidate exceeds cumulative disturbance limits or adds no useful alternative."
                };
            }
            if (context.Completion is { } completion)
            {
                var evaluation = completion.Check(draft.PreviewChild(plan.Patch), requirementId ?? plan.Route.Id, step.Id);
                if (evaluation.Diagnostic.Status != CompletionStatus.Allowed)
                {
                    completion.RecordRejection(evaluation.Diagnostic);
                    return intent with { Status = RouteIntentStatus.Unresolved, RouteId = null,
                        Reason = evaluation.Diagnostic.Detail ?? "Completion preflight rejected route." };
                }
            }
            if (!draft.Accept(plan.Patch, out var error)) throw new InvalidOperationException(error);
            routes.Add(plan.Route); operations.AddRange(plan.Operations);
            context.RoutingSearches += plan.Route.Stats.Attempts; context.RoutingVisitedCells += plan.Route.Stats.ExpandedStates;
            context.RouteSearchMilliseconds -= plan.MaterializationMilliseconds; context.RouteMaterializationMilliseconds += plan.MaterializationMilliseconds;
            return intent;
        }
    }
}
