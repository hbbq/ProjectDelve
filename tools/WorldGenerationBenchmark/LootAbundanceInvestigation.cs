using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;

internal static class LootAbundanceInvestigation
{
    internal static void Run(string[] args)
    {
        var content = ContentBundle.Canonical(); var records = new List<object>();
        foreach (var preset in WorldGenerationPresets.All.Where(p => p.Id is "Grassland" or "Woodland" or "RockyWilds"))
        foreach (var size in new[] { (40, 35), (60, 50) })
        foreach (var seed in new ulong[] { 1, 12, 12345 })
        foreach (var density in new[] { PopulationDensity.Off, PopulationDensity.High })
        {
            var counts = new List<int>(); string? units = null;
            foreach (var abundance in Enum.GetValues<LootAbundance>())
            {
                var recipe = new WorldGenerationRecipe(new(-17, -13, size.Item1, size.Item2), preset.Settings, seed,
                    preset.Enrichment, preset.Civilization, preset.Routes, new(density, 5), LootAbundance: abundance);
                var world = WorldGenerator.Generate(recipe, content); var definition = world.Definition;
                var state = GameEngine.CreateGame(definition, content); var width = size.Item1;
                var markers = (definition.LootMarkers ?? []).Select(m => m.Position.Y * width + m.Position.X).ToHashSet();
                var unitCells = state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id)).Select(c => c.Y * width + c.X).ToHashSet();
                var reached = PlayReadyScenarioSetup.ReachableFromDeployment(definition, state);
                var protectedCells = world.Context.PopulationProtectedAreas.SelectMany(a => a.Indices(width)).ToHashSet();
                if (!PlayReadyScenarioSetup.Validate(definition, state).Valid || markers.Any(i => !reached.Contains(i) || protectedCells.Contains(i) || unitCells.Contains(i)))
                    throw new InvalidOperationException("Loot failed final reachability, protection or occupancy: " + world.Code);
                var unitsJson = JsonSerializer.Serialize(definition.Units);
                if (units is not null && units != unitsJson) throw new InvalidOperationException("Abundance changed population.");
                units = unitsJson;
                var rows = definition.Loot!.ResolveComposition(content.LootCards, content.LootPresets);
                if (rows.Select(r => r.Count).Distinct().Count() != 1 || rows.Sum(r => r.Count) < markers.Count)
                    throw new InvalidOperationException("All mix or finite supply changed.");
                var quick = WorldQuickPlay.Prepare(definition, content);
                if (quick.Loot != definition.Loot || !quick.LootMarkers!.SequenceEqual(definition.LootMarkers!))
                    throw new InvalidOperationException("Quick Play changed Loot.");
                if (world.Fingerprint != WorldGenerator.Generate(world.Code, content).Fingerprint)
                    throw new InvalidOperationException("Non-deterministic recipe: " + world.Code);
                var buildings = world.Context.Sites.SelectMany(s => s.Buildings).Select(b => b.Rooms.SelectMany(r => r.Usable.Indices(width)).ToHashSet()).ToArray();
                var dungeons = world.Context.Sites.Where(s => s.MountainDungeon is not null)
                    .Select(s => s.MountainDungeon!.Rooms.SelectMany(r => r.Usable.Indices(width)).Concat(s.MountainDungeon.Corridors.Indices(width)).ToHashSet()).ToArray();
                var siteCells = world.Context.Sites.SelectMany(s => s.Footprint.Indices(width)).ToHashSet();
                var record = new { preset = preset.Id, width, height = size.Item2, seed, density = density.ToString(), abundance = abundance.ToString(),
                    world.Code, markers = markers.Count, inSites = markers.Count(siteCells.Contains), outsideSites = markers.Count(i => !siteCells.Contains(i)),
                    buildings = buildings.Length, rewardedBuildings = buildings.Count(b => b.Overlaps(markers)), emptyBuildings = buildings.Count(b => !b.Overlaps(markers) && !b.Overlaps(unitCells)),
                    dungeons = dungeons.Length, rewardedDungeons = dungeons.Count(b => b.Overlaps(markers)), emptyDungeons = dungeons.Count(b => !b.Overlaps(markers) && !b.Overlaps(unitCells)),
                    siteLoot = world.Context.SiteLootStatistics, populationLootWanted = world.Context.Population?.Statistics.RequestedLootMarkers ?? 0,
                    populationLootPlaced = world.Context.Population?.Statistics.LootMarkers ?? 0, deckCards = rows.Sum(r => r.Count), copiesPerCard = rows[0].Count,
                    mix = rows.Select(r => r.LootCardId).ToArray(), valid = true };
                records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record)); counts.Add(markers.Count);
            }
            if (counts[0] > counts[1] || counts[1] > counts[2]) throw new InvalidOperationException("Abundance reduced marker quantity.");
        }
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }
}
