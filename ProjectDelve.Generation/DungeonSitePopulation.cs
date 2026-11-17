using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class DungeonSitePopulation
{
    internal static void PublishIntents(WorldGenerationContext context)
    {
        var width = context.Window.Width;
        for (var index = 0; index < context.Sites.Count; index++)
        {
            var site = context.Sites[index];
            if (site.MountainDungeon is not { } dungeon) continue;
            var regions = dungeon.Rooms.Select(room =>
            {
                var cells = room.Usable.Indices(width).ToArray();
                var preferred = PopulationComposition.Pool((context.ConstructionBoard?.Content ?? ContentBundle.Canonical()).Units.Select(t => t.Id))
                    .Select(p => p.UnitTypeId).ToArray();
                // Large chambers can host an existing large creature, without promising exit access.
                if (room.TwoByTwoPlacements >= 4) preferred = [.. preferred, UnitTypeIds.RedDragon];
                // A short local circuit is an optional ordinary patrol, entirely within its room.
                var set = cells.ToHashSet();
                var corner = cells.FirstOrDefault(i => i % width < width - 1 && new[] { i + 1, i + width, i + width + 1 }.All(set.Contains), -1);
                int[] patrol = corner < 0 ? [] : [corner, corner + 1, corner + width + 1, corner + width];
                return new SiteEncounterRegion(room.Id + "/encounter", room.Usable, 1, Math.Clamp(cells.Length / 6, 2, 7), preferred, patrol);
            }).ToArray();
            var areas = dungeon.Rooms.Select(room => new SitePopulationArea(room.Id + "/resting", "resting", room.Usable,
                room.Usable.Indices(width).First(), room.Id)).Concat(dungeon.TreasureAreas.Select(a =>
                new SitePopulationArea(a.Id + "/supplies", "supplies", a, a.Indices(width).First(), a.Id))).ToArray();
            context.Sites[index] = site with { PopulationIntent = new(dungeon.Profile.ToString(), areas, [], 0,
                regions.Sum(r => r.MaximumUnits), CampAlertTopology.Separated) { EncounterRegions = regions } };
        }
    }

    // Validate final geometry before population; exploration Loot is placed after population.
    internal static void FinalizeGeometry(WorldGenerationContext context)
    {
        var sites = context.Sites.Where(s => s.MountainDungeon is not null).ToArray();
        if (sites.Length == 0) return;
        var board = context.ConstructionBoard!; var width = board.Width;
        var regions = sites.Select(s => s.MountainDungeon!.AccessRootIndex).Distinct()
            .ToDictionary(root => root, root => PlayableWorldComponent.FromRoot(board, root));
        foreach (var site in sites)
            if (site.Entrances.Any(e => !regions[site.MountainDungeon!.AccessRootIndex].Contains(e.InsideIndex) ||
                    !regions[site.MountainDungeon.AccessRootIndex].Contains(e.OutsideIndex)) ||
                site.MountainDungeon!.Rooms.Any(r => r.Usable.Indices(width).Any(i => !regions[site.MountainDungeon.AccessRootIndex].Contains(i))))
                throw new ArgumentException("Final geometry disconnected a Mountain Dungeon from its anchored doors-openable world component.");
    }
}

public static partial class PopulationGenerator
{
    private static PopulationEncounter? RoomRegion(string id, PopulationLocation location, SiteEncounterRegion region,
        List<int> free, PopulationSettings settings, int remaining, Pcg32 random, PlanningBoard board, GameState state,
        Dictionary<string, UnitType> types, PlanningBudget budget, Func<int, bool> available, Action<string> fail, out int requested)
    {
        requested = location.UnitTarget ?? region.MinimumUnits + random.Next(Math.Max(1, region.MaximumUnits - region.MinimumUnits + 1));
        var wanted = Math.Min(requested, remaining); var width = board.Width;
        var allowed = free.ToHashSet(); var claimed = new HashSet<Cell>();
        var preferences = region.PreferredUnitTypes.Where(types.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var pool = PopulationComposition.Pool(types.Keys).Where(p => preferences.Count == 0 || preferences.Contains(p.UnitTypeId)).ToArray();
        if (pool.Length == 0) { fail("Room region has no available preferred Monster composition."); return null; }
        var building = location.Kind == "BuildingDefenders";
        var type = building ? PopulationComposition.Monster(random, settings.Difficulty, types.Keys, building: true)
            : PopulationComposition.Select(random, settings.Difficulty, pool);
        if (building && types[type].Footprint != Footprint.OneByOne)
        { fail("Building occupation omitted: single-Cell portals do not certify large-footprint access."); return null; }
        if (!building && settings.Difficulty == 5 && preferences.Contains(UnitTypeIds.RedDragon) && random.Next(12) == 0 &&
            !state.Units.Any(u => u.TypeId == UnitTypeIds.RedDragon)) { type = UnitTypeIds.RedDragon; wanted = 1; }
        var placed = new List<(string Type, int Cell, string Role, string Cluster, string? Area)>();
        bool Legal(int i) => allowed.Contains(i) && available(i);
        foreach (var cell in free)
        {
            if (!budget.Spend(16)) break;
            if (!Fits(types[type], cell, claimed, board, Legal)) continue;
            var footprint = FootprintGeometry.OccupiedCells(types[type].Footprint, new(cell % width, cell / width)).ToArray();
            if (placed.Count > 0 && !footprint.Any(c => claimed.Any(other => Math.Abs(c.X - other.X) + Math.Abs(c.Y - other.Y) <= DormantRules.AlertDistance))) continue;
            placed.Add((type, cell, !building && placed.Count == 0 && random.Next(3) == 0 ? "sentry" : "resting", region.Id, region.Id));
            claimed.UnionWith(footprint);
            if (placed.Count >= wanted) break;
        }
        if (placed.Count < region.MinimumUnits) { fail("Room region lacks complete legal footprints outside required access and Alert spacing."); return null; }
        IReadOnlyList<int>? patrol = null;
        // Patrols stay in their own region and never assume a large creature can exit a chamber.
        if (types[type].Footprint == Footprint.OneByOne && random.Next(5) == 0 &&
            OpenPath(board, region.PatrolPath, i => Legal(i) && (i == placed[0].Cell || !claimed.Contains(new(i % width, i / width)))) &&
            region.PatrolPath.Contains(placed[0].Cell))
        { patrol = region.PatrolPath; placed[0] = placed[0] with { Role = "patrol" }; }
        if (placed.Count < requested) fail($"Room occupation reduced to {placed.Count} of {requested} requested Units.");
        return PopulationComposition.FromRoles(id, location.Kind, location.SourceId, width, placed, requested, random, patrol, types)
            with { IntendedAlertTopology = "Independent region; connected occupants" };
    }
}
