using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record SiteLootStatistics(int Requested, int Placed, int SelectedLocations, int UnrewardedLocations);

public sealed partial class WorldGenerationContext
{
    public LootAbundance LootAbundance { get; set; } = LootAbundance.Standard;
    public SiteLootStatistics? SiteLootStatistics { get; internal set; }
}

/// <summary>Concrete, nonblocking exploration rewards. No Site metadata enters gameplay state.</summary>
internal static class SiteLootGenerator
{
    // Standard permits 64 Site rewards plus the existing 48 encounter rewards.
    private const int StandardLimit = 64, StandardLocationLimit = 8;
    private sealed record RewardLocation(string Id, int Standard, IReadOnlyList<int> Cells);

    internal static void Generate(WorldGenerationContext context, ContentBundle content)
    {
        if (context.SiteLootStatistics is not null) throw new InvalidOperationException("Site Loot has already been authored.");
        var definition = context.Materialize();
        var settings = definition.Loot ?? LootSettings.Empty;
        var composition = settings.ResolveComposition(content.LootCards, content.LootPresets);
        if (composition.Count == 0 || context.Sites.Count == 0)
        { context.SiteLootStatistics = new(0, 0, 0, 0); return; }
        var board = context.ConstructionBoard!; var width = board.Width;
        var state = GameEngine.CreateGame(definition, content);
        var used = (definition.LootMarkers ?? []).Select(m => Index(m.Position)).ToHashSet();
        var reservedCards = definition.LootMarkers?.Count ?? 0;
        var forbidden = context.PopulationProtectedAreas.SelectMany(a => a.Indices(width)).ToHashSet();
        forbidden.UnionWith(state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id)).Select(Index));
        forbidden.UnionWith((definition.Resources ?? []).Select(r => Index(r.Position)));
        forbidden.UnionWith((definition.GroundObjects ?? []).Select(o => Index(o.Position)));
        foreach (var site in context.Sites)
        {
            forbidden.UnionWith(site.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex }));
            if (site.SettlementLayout is { } settlement) forbidden.UnionWith(settlement.Access.Indices(width));
            if (site.MountainDungeon is { } dungeon) forbidden.UnionWith(dungeon.RequiredAccess.Indices(width));
        }
        var domain = context.Completion?.InteractivePlacementDomain() ?? PlayableWorldComponent.Select(state.Physical.Board);
        var locations = new List<RewardLocation>(); var unrewarded = 0;
        foreach (var site in context.Sites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (site.Kind == GeneratedSiteKind.Landmark) continue;
            // Vacancy is independent of population, density, abundance and selected card mix.
            if (Pcg32.Stream(context.World.Seed, site.Id + "/site-loot/vacancy").Next(5) == 0)
            { unrewarded += Math.Max(1, site.Buildings.Count); continue; }
            var distances = Distances(site);
            foreach (var building in site.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                if (Pcg32.Stream(context.World.Seed, building.Id + "/site-loot/vacancy").Next(5) == 0)
                { unrewarded++; continue; }
                Add(building.Id, building.Rooms.Select(r => r.Usable).ToArray(), distances);
            }
            if (site.MountainDungeon is { } dungeon)
                Add(site.Id, dungeon.Rooms.Select(r => r.Usable).Append(dungeon.Corridors).ToArray(), distances);
            else if (site.Buildings.Count == 0)
                Add(site.Id, (site.PopulationIntent?.Areas.OrderByDescending(a => a.Role == "supplies")
                    .Select(a => a.Area) ?? [site.Exterior]).ToArray(), distances);
        }
        // Interleave locations within each tier before increasing any location's allowance.
        // Sparse, Standard and Plentiful consume successive tiers of the same seeded ordering.
        Pcg32.Stream(context.World.Seed, WorldGenerationContext.WindowPath("site-loot", context.Window) + "/order").Shuffle(locations);
        var maximum = PopulationLoot.Scale(StandardLimit, context.LootAbundance);
        var custom = settings.Normalize(content.LootPresets).Composition is not null;
        var capacity = custom ? Math.Max(0, composition.Sum(c => c.Count) - reservedCards) : maximum;
        var requested = Math.Min(maximum, locations.Sum(l => PopulationLoot.Scale(l.Standard, context.LootAbundance)));
        var placed = 0;
        for (var tier = LootAbundance.Sparse; tier <= context.LootAbundance; tier++)
        for (var slot = 0; slot < StandardLocationLimit * 2; slot++)
        foreach (var location in locations)
        {
            var previous = tier == LootAbundance.Sparse ? 0 : PopulationLoot.Scale(location.Standard, tier - 1);
            var end = PopulationLoot.Scale(location.Standard, tier);
            if (slot < previous || slot >= end || slot >= location.Cells.Count || placed >= maximum || placed >= capacity) continue;
            var cell = location.Cells[slot];
            if (!used.Add(cell)) continue;
            context.SiteLootMarkers.Add(new(location.Id + "/exploration-loot-" + cell, new(cell % width, cell / width)));
            placed++;
        }
        context.SiteLootStatistics = new(requested, placed, locations.Count, unrewarded);
        if (placed < requested) context.Completion?.RecordOmission("site-loot", "site-loot",
            $"Placed {placed} of {requested} optional Site Loot markers; legal unoccupied candidates or finite custom supply exhausted.");
        var expectedDrops = (state.Units.Sum(u => state.Types.Single(t => t.Id == u.TypeId).LootDropSuccesses) + 5) / 6;
        var provisioned = PopulationLoot.Provision(settings, reservedCards + placed, expectedDrops, content);
        context.Loot = provisioned;
        if (context.Population is { } population) context.Population = population with { Loot = provisioned };

        int Index(Cell cell) => cell.Y * width + cell.X;
        void Add(string id, IReadOnlyList<GeneratedArea> areas, Dictionary<int, int> distances)
        {
            var all = areas.SelectMany(a => a.Indices(width)).Distinct().ToArray();
            if (all.Length == 0) return;
            var standard = Math.Clamp((all.Length + 23) / 24, 1, StandardLocationLimit);
            var supplies = context.Sites.SelectMany(s => s.PopulationIntent?.Areas.Where(a => a.Role == "supplies") ?? [])
                .SelectMany(a => a.Area.Indices(width)).ToHashSet();
            var regions = areas.Select(area =>
            {
                var cells = area.Indices(width).Where(i => board.Passable(i) && domain.Contains(i) &&
                    !forbidden.Contains(i) && !used.Contains(i)).Distinct().Order().ToList();
                Pcg32.Stream(context.World.Seed, id + "/site-loot/" + area.Id).Shuffle(cells);
                return cells.OrderByDescending(i => distances.GetValueOrDefault(i, -1)).ToArray();
            }).Where(c => c.Length > 0).OrderByDescending(c => c.Max(i => distances.GetValueOrDefault(i, -1)))
                .ThenByDescending(c => c.Any(supplies.Contains)).ToArray();
            var chosen = new List<int>();
            for (var round = 0; round < standard * 2 && chosen.Count < standard * 2; round++)
            foreach (var region in regions)
                if (round < region.Length && chosen.Count < standard * 2 && !chosen.Contains(region[round])) chosen.Add(region[round]);
            locations.Add(new(id, standard, chosen));
        }
        Dictionary<int, int> Distances(SiteInfo site)
        {
            var allowed = site.Footprint.Indices(width).ToHashSet();
            var distances = new Dictionary<int, int>(); var queue = new Queue<int>();
            var starts = site.Entrances.Select(e => e.InsideIndex).Concat(site.Buildings.SelectMany(b => b.Portals.Take(1).Select(p => p.InsideIndex)));
            foreach (var cell in starts.Distinct().Order())
                if (board.Passable(cell) && distances.TryAdd(cell, 0)) queue.Enqueue(cell);
            while (queue.TryDequeue(out var cell))
                foreach (var next in board.Neighbors(cell))
                    if (allowed.Contains(next) && board.Passable(next) &&
                        board.EdgeAt(EdgeKey.Between(cell, next, width)) is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor &&
                        distances.TryAdd(next, distances[cell] + 1)) queue.Enqueue(next);
            return distances;
        }
    }
}
