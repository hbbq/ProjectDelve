using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record SitePopulationPlan(string SourceId, string SiteId, string Kind, GeneratedArea Area, int Rooms,
    string Selection, IReadOnlyList<PopulationLocation> Requests, int QuietRooms);
internal sealed record PopulationAttempt(PopulationLocation Location, int Requested, int Accepted, string Outcome,
    bool BudgetLimited = false, bool WorkLimited = false);

/// <summary>Bounded authoring of existing room encounters; no runtime Site or encounter rules.</summary>
internal static class SitePopulationAllocation
{
    internal const int LargeBuildingArea = 50, LargeDungeonArea = 100;

    internal static List<PopulationLocation> Select(WorldGenerationContext context, PopulationSettings settings,
        string path, out List<SitePopulationPlan> plans)
    {
        var width = context.Window.Width; plans = [];
        var buildings = ZombieHousePopulationGenerator.Locations(context).ToDictionary(l => l.SourceId, StringComparer.Ordinal);
        foreach (var site in context.Sites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            // A passive landmark with no Buildings is distinct from the campsite fallback.
            if (site.Kind == GeneratedSiteKind.Landmark) continue;
            var occupation = Pcg32.Stream(context.World.Seed, path + "/occupation/" + site.Id).Next(5);
            if (site.PopulationIntent is { EncounterRegions.Count: > 0 } intent)
            {
                var dungeon = site.MountainDungeon;
                var roomCount = dungeon?.Rooms.Count ?? intent.EncounterRegions.Count;
                var area = GeneratedArea.From(site.Id + "/population-area", dungeon is not null
                    ? dungeon.Rooms.SelectMany(r => r.Usable.Indices(width)).Concat(dungeon.Corridors.Indices(width))
                    : intent.EncounterRegions.SelectMany(r => r.Area.Indices(width)), width);
                var random = Pcg32.Stream(context.World.Seed, path + "/site-allocation/" + site.Id);
                var active = area.Area >= LargeDungeonArea ? random.Next(10) != 0 : occupation != 0;
                var regions = active ? ChooseRooms(intent.EncounterRegions, area.Area, settings.Density, true, random, width) : [];
                var requests = Allocate(site.Id, site.Id, "DungeonEncounter", regions, area.Area, settings.Density, true, random, width);
                plans.Add(new(site.Id, site.Id, dungeon is null ? "Site" : "Dungeon", area, roomCount, active ? "Active" : "Vacant",
                    requests, active ? roomCount - requests.Count : 0));
                continue;
            }
            var hasLargeBuilding = site.Buildings.Any(b => b.Rooms.Sum(r => r.Usable.Area) >= LargeBuildingArea);
            // Large buildings use room allocations rather than a small courtyard compound allowance.
            var compound = !hasLargeBuilding && occupation == 1 && site.Kind == GeneratedSiteKind.Settlement && site.PopulationIntent is not null;
            PopulationLocation? compoundRequest = compound ? new("SettlementCompound", site.Id,
                site.PopulationIntent!.Areas.SelectMany(a => a.Area.Indices(width)).Distinct().Order().ToArray(), site.Footprint.Bounds)
                { Intent = site.PopulationIntent, SiteId = site.Id } : null;
            if (compound) plans.Add(new(site.Id, site.Id, "SettlementCompound", site.Footprint,
                site.Buildings.Sum(b => b.Rooms.Count), "Compound", [compoundRequest!], 0));
            foreach (var building in site.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                var area = GeneratedArea.From(building.Id + "/population-area", building.Rooms.SelectMany(r => r.Usable.Indices(width)), width);
                var random = Pcg32.Stream(context.World.Seed, path + "/site-allocation/" + building.Id);
                var active = area.Area >= LargeBuildingArea ? random.Next(10) != 0 : occupation != 0 &&
                    Pcg32.Stream(context.World.Seed, path + "/occupation/" + building.Id).Next(5) != 0;
                var supplies = site.PopulationIntent?.Areas.Where(a => a.Role == "supplies")
                    .SelectMany(a => a.Area.Indices(width)).ToHashSet() ?? [];
                var reachable = buildings.GetValueOrDefault(building.Id)?.Cells.ToHashSet() ?? [];
                var eligible = building.Rooms.Select(room => new SiteEncounterRegion(room.Id + "/encounter",
                    GeneratedArea.From(room.Id + "/occupation", room.Usable.Indices(width).Where(i => reachable.Contains(i) && !supplies.Contains(i)), width),
                    1, 6, [], [])).Where(r => r.Area.Area > 0).ToArray();
                if (compound)
                {
                    // The parent owns the shared request. A building has no separately requested
                    // quota merely because it can receive some of that compound's occupants.
                    plans.Add(new(building.Id, site.Id, "Building", area, building.Rooms.Count, "CompoundMember", [], 0));
                    continue;
                }
                var regions = active ? ChooseRooms(eligible, area.Area, settings.Density, false, random, width) : [];
                var requests = Allocate(building.Id, site.Id, "BuildingDefenders", regions, area.Area, settings.Density, false, random, width);
                var selection = eligible.Length == 0 ? "Supplies" : active ? "Independent" : "Vacant";
                plans.Add(new(building.Id, site.Id, "Building", area, building.Rooms.Count, selection, requests,
                    active ? building.Rooms.Count - requests.Count : 0));
            }
            if (site.Buildings.Count == 0)
            {
                PopulationLocation[] requests = occupation == 0 ? [] : [new(site.PopulationIntent is null ? "GoblinCamp" : "StructuredCamp", site.Id,
                    (site.PopulationIntent?.Areas.SelectMany(a => a.Area.Indices(width)) ?? site.Exterior.Indices(width)).Distinct().Order().ToArray(),
                    site.Footprint.Bounds) { Intent = site.PopulationIntent, SiteId = site.Id }];
                plans.Add(new(site.Id, site.Id, "Campsite", site.Footprint, 0, occupation == 0 ? "Vacant" : "Active", requests, 0));
            }
        }
        // One request per Site at a time, keeping another Site's rooms ahead of a large Site's extras.
        var requestsBySite = plans.SelectMany(p => p.Requests).Distinct().ToList();
        if (requestsBySite.All(r => r.Region is null)) return requestsBySite;
        var groups = requestsBySite.GroupBy(l => l.SiteId).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.ToList()).ToList();
        Pcg32.Stream(context.World.Seed, path + "/site-order").Shuffle(groups);
        return Enumerable.Range(0, groups.Count == 0 ? 0 : groups.Max(g => g.Count))
            .SelectMany(round => groups.Where(g => g.Count > round).Select(g => g[round])).ToList();
    }

    private static List<SiteEncounterRegion> ChooseRooms(IReadOnlyList<SiteEncounterRegion> source, int area,
        PopulationDensity density, bool dungeon, Pcg32 random, int width)
    {
        source = source.Where(r => r.Area.Area > 0).ToArray();
        if (source.Count == 0) return [];
        var large = area >= (dungeon ? LargeDungeonArea : LargeBuildingArea);
        var divisor = dungeon ? density switch { PopulationDensity.Low => 120, PopulationDensity.High => 55, _ => 80 }
            : density switch { PopulationDensity.Low => 45, PopulationDensity.High => 25, _ => 35 };
        var minimum = large && source.Count >= 2 ? 2 : 1;
        var wanted = Math.Clamp((area + divisor - 1) / divisor + (source.Count - 1) / 3 + random.Next(2), minimum, Math.Min(4, source.Count));
        if (source.Count >= 3 && random.Next(4) != 0) wanted = Math.Min(wanted, source.Count - 1);
        var candidates = source.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(); random.Shuffle(candidates);
        var selected = new List<SiteEncounterRegion>();
        int Anchor(SiteEncounterRegion region) => region.Area.Indices(width).ElementAt(region.Area.Area / 2);
        int Distance(int a, int b) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
        while (selected.Count < wanted)
        {
            var next = candidates.OrderByDescending(r => selected.Count == 0 ? 0 : selected.Min(s => Distance(Anchor(r), Anchor(s)))).First();
            selected.Add(next); candidates.Remove(next);
        }
        return selected;
    }

    private static List<PopulationLocation> Allocate(string source, string site, string kind, List<SiteEncounterRegion> regions,
        int area, PopulationDensity density, bool dungeon, Pcg32 random, int width)
    {
        if (regions.Count == 0) return [];
        var divisor = dungeon ? density switch { PopulationDensity.Low => 35, PopulationDensity.High => 16, _ => 24 }
            : density switch { PopulationDensity.Low => 18, PopulationDensity.High => 9, _ => 13 };
        var ceiling = dungeon ? density switch { PopulationDensity.Low => 12, PopulationDensity.High => 24, _ => 18 }
            : density switch { PopulationDensity.Low => 6, PopulationDensity.High => 12, _ => 9 };
        if (!dungeon && area < LargeBuildingArea) ceiling = Math.Min(ceiling, 5);
        // Declared regional minimums remain binding; omit a request that cannot fit this
        // bounded Site allowance rather than silently lowering its authored minimum.
        var minimumTotal = 0;
        regions = regions.Where(r =>
        {
            var minimum = Math.Max(1, r.MinimumUnits);
            if (minimum > r.MaximumUnits || minimumTotal + minimum > ceiling) return false;
            minimumTotal += minimum; return true;
        }).ToList();
        if (regions.Count == 0) return [];
        var wanted = Math.Clamp((area + divisor - 1) / divisor + regions.Count + random.Next(2), minimumTotal, ceiling);
        var quantities = regions.Select(r => Math.Max(1, r.MinimumUnits)).ToArray();
        for (var pass = 0; pass < 7 && quantities.Sum() < wanted; pass++)
        for (var i = 0; i < regions.Count && quantities.Sum() < wanted; i++)
            if (quantities[i] < regions[i].MaximumUnits) quantities[i]++;
        return regions.Select((r, i) => new PopulationLocation(kind, dungeon ? r.Id : source, r.Area.Indices(width).ToArray(), r.Area.Bounds)
        { Region = r, SiteId = site, UnitTarget = quantities[i] }).ToList();
    }

    internal static IReadOnlyList<SitePopulationStatistics> Summarize(WorldGenerationContext context, ContentBundle content,
        IReadOnlyList<SitePopulationPlan> plans, IReadOnlyList<PopulationAttempt> attempts, IReadOnlyList<PopulationEncounter> accepted)
    {
        var width = context.Window.Width;
        var types = (context.ConstructionBoard?.Content ?? content).Units.ToDictionary(t => t.Id);
        return plans.Select(plan =>
        {
            var cells = plan.Area.Indices(width).ToHashSet();
            bool Inside(UnitPlacement unit) => FootprintGeometry.OccupiedCells(types[unit.UnitTypeId].Footprint, unit.Anchor).Any(c => cells.Contains(c.Y * width + c.X));
            var matching = attempts.Where(a => plan.Requests.Contains(a.Location)).ToArray();
            return new SitePopulationStatistics(plan.SourceId, plan.SiteId, plan.Kind, plan.Area.Area, plan.Rooms,
                plan.Selection is "Active" or "Independent" or "Compound", plan.Selection, plan.Requests.Count, plan.QuietRooms,
                plan.Requests.Sum(r => r.UnitTarget ?? matching.FirstOrDefault(a => a.Location == r)?.Requested ?? 0),
                accepted.Count(e => e.Units.Any(Inside)), accepted.Sum(e => e.Units.Count(Inside)),
                matching.Count(a => a.Outcome == "Reduced" && !a.BudgetLimited && !a.WorkLimited),
                matching.Count(a => a.Outcome == "Placement"), matching.Count(a => a.Outcome == "Alert"),
                matching.Count(a => a.Outcome == "Budget"), matching.Count(a => a.Outcome == "Work"), 0)
            {
                BudgetReducedEncounters = matching.Count(a => a.Outcome == "Reduced" && a.BudgetLimited),
                WorkReducedEncounters = matching.Count(a => a.Outcome == "Reduced" && a.WorkLimited)
            };
        }).ToArray();
    }

    internal static void FinalizeContent(WorldGenerationContext context)
    {
        if (context.Population is not { } population) return;
        var width = context.Window.Width; var definition = context.Materialize();
        var positions = (definition.LootMarkers ?? []).Select(m => m.Position).Concat((definition.Machines ?? []).Select(m => m.Position))
            .Concat((definition.Resources ?? []).Select(r => r.Position)).Concat((definition.GroundObjects ?? []).Select(o => o.Position)).ToArray();
        var areas = context.Sites.SelectMany(s => s.Buildings.Select(b => (b.Id, Area: b.Rooms.SelectMany(r => r.Usable.Indices(width)).ToHashSet()))
            .Append((s.Id, Area: s.MountainDungeon is { } d ? d.Rooms.SelectMany(r => r.Usable.Indices(width)).Concat(d.Corridors.Indices(width)).ToHashSet() : s.Footprint.Indices(width).ToHashSet())))
            .ToDictionary(p => p.Id, p => p.Area, StringComparer.Ordinal);
        context.Population = population with { SiteStatistics = population.SiteStatistics.Select(s => s with
        { MeaningfulNoncombatObjects = positions.Count(p => areas[s.SourceId].Contains(p.Y * width + p.X)) }).ToArray() };
    }
}
