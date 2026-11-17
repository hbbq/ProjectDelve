using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

// Measurement only: the same source can run against a saved Generation/Engine DLL pair.
internal static class SitePopulationInvestigation
{
    internal static void Run(string[] args)
    {
        var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        var content = ContentBundle.Canonical();
        List<object> records = [];
        foreach (var preset in WorldGenerationPresets.All.Where(p => p.Id is "Grassland" or "RockyWilds"))
        foreach (var size in new[] { (Width: 40, Height: 35), (Width: 100, Height: 80) })
        foreach (var seed in (preset.Id == "RockyWilds" && size.Width == 100 ? new ulong[] { 1, 12, 12345, 2, 3, 4, 5 } : [1UL, 12UL, 12345UL]))
        foreach (var density in new[] { PopulationDensity.Low, PopulationDensity.Medium, PopulationDensity.High })
        {
            var recipe = new WorldGenerationRecipe(new(-17, -13, size.Width, size.Height), preset.Settings, seed,
                preset.Enrichment, preset.Civilization, preset.Routes, new(density, 5), LootPreset: "All", LootAbundance: LootAbundance.Standard);
            try
            {
                var world = WorldGenerator.Generate(recipe, content);
                var definition = world.Definition;
                var state = GameEngine.CreateGame(definition, content);
                var population = world.Context.Population!;
                int Index(Cell c) => c.Y * size.Width + c.X;
                var units = state.Physical.Figures.ToDictionary(f => f.Id, f => FootprintGeometry.OccupiedCells(state, f.Id).Select(Index).ToHashSet());
                var loot = (definition.LootMarkers ?? []).Select(m => Index(m.Position)).ToHashSet();
                var machines = (definition.Machines ?? []).Select(m => Index(m.Position)).ToHashSet();
                var resources = (definition.Resources ?? []).Select(r => Index(r.Position)).ToHashSet();
                var objects = (definition.GroundObjects ?? []).Select(o => Index(o.Position)).ToHashSet();
                var meaningful = units.Values.SelectMany(u => u).Concat(loot).Concat(machines).Concat(resources).Concat(objects).ToHashSet();
                var encounters = population.Encounters;
                var sites = world.Context.Sites.SelectMany(s => s.Buildings.Select(b => Measure(s.Id, b.Id, "Building", b.Rooms, []))
                    .Concat(s.MountainDungeon is { } d ? [Measure(s.Id, s.Id, "Dungeon", d.Rooms, d.Corridors.Indices(size.Width))] : [])).ToArray();
                SiteMeasure Measure(string parent, string id, string kind, IReadOnlyList<RoomInfo> rooms, IEnumerable<int> corridors)
                {
                    var cells = rooms.SelectMany(r => r.Usable.Indices(size.Width)).Concat(corridors).ToHashSet();
                    var count = units.Count(u => u.Value.Overlaps(cells));
                    var rewards = loot.Count(cells.Contains);
                    var large = cells.Count >= (kind == "Building" ? 50 : 100);
                    return new(parent, id, kind, cells.Count, rooms.Count,
                        rooms.Count(r => !r.Usable.Indices(size.Width).Any(meaningful.Contains)), count, rewards,
                        machines.Count(cells.Contains), resources.Count(cells.Contains), objects.Count(cells.Contains),
                        count == 0 && rewards > 0, !cells.Overlaps(meaningful), large, count > 0,
                        large && count > 0 && (count < 4 || (double)count / cells.Count < .02),
                        rooms.Select(r => new RoomMeasure(r.Id, r.Usable.Area,
                            units.Count(u => u.Value.Overlaps(r.Usable.Indices(size.Width))),
                            r.Usable.Indices(size.Width).Count(loot.Contains), !r.Usable.Indices(size.Width).Any(meaningful.Contains))).ToArray());
                }
                // Recompute the complete initial friendly graph rather than trusting stored counts.
                var actual = PopulationTopology.Components(definition.Units, ScenarioContentReferences.ScenarioCatalog(content, definition).Definitions.Units);
                var normalized = actual.Select(c => string.Join("|", c.UnitIds.Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal).ToArray();
                var reported = population.AlertComponents.Select(c => string.Join("|", c.UnitIds.Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal).ToArray();
                var alertMismatch = !normalized.SequenceEqual(reported);
                var playReady = PlayReadyScenarioSetup.Validate(definition, state);
                var rows = definition.Loot!.ResolveComposition(content.LootCards, content.LootPresets);
                var repeatChecked = seed == 1 && density == PopulationDensity.Medium;
                var deterministic = !repeatChecked || world.Fingerprint == WorldGenerator.Generate(world.Code, content).Fingerprint;
                var geometry = Hash(JsonSerializer.Serialize(new { definition.Board, sites = world.Context.Sites.Select(s => new { s.Id, s.Footprint, s.Buildings, s.MountainDungeon }) }));
                var record = new
                {
                    preset = preset.Id, width = size.Width, height = size.Height, seed, density = density.ToString(), recipe = world.Code,
                    world.Fingerprint, geometryFingerprint = geometry, repeatChecked, deterministic,
                    engineValid = true, playReadyValid = playReady.Valid, playReadyDiagnostics = playReady,
                    totalUnits = definition.Units.Count, totalLoot = loot.Count, totalMachines = machines.Count, totalResources = resources.Count, totalGroundObjects = objects.Count,
                    population = population.Statistics, failures = population.Failures,
                    alertFailures = population.Failures.Count(f => f.Reason.Contains("Alert", StringComparison.OrdinalIgnoreCase)), alertMismatch,
                    selectionDiagnostics = Optional(population, "SiteStatistics", json), encounters = encounters.Select(e => new { e.Id, e.Kind, e.SourceId, units = e.Units.Count, e.RequestedUnits }),
                    sites, loot = new { settings = definition.Loot, mix = rows.Select(r => r.LootCardId), supply = rows.Sum(r => r.Count), uniform = rows.Select(r => r.Count).Distinct().Count() == 1 },
                    errors = Array.Empty<string>()
                };
                records.Add(record);
                Console.Error.WriteLine($"{preset.Id} {size.Width}x{size.Height} {seed} {density}: {sites.Length} structures, {definition.Units.Count} Units, valid={playReady.Valid}");
            }
            catch (Exception error)
            {
                records.Add(new { preset = preset.Id, width = size.Width, height = size.Height, seed, density = density.ToString(), recipe = recipe.ToString(), errors = new[] { error.ToString() } });
                Console.Error.WriteLine($"FAILED {recipe}: {error.Message}");
            }
        }
        var output = JsonSerializer.Serialize(new
        {
            method = "48 deterministic recipes: Grassland/RockyWilds; 40x35/100x80; seeds 1,12,12345; RockyWilds 100x80 tail seeds 2..5; Low/Medium/High; difficulty 5; All/Standard; offset -17,-13; preset geometry settings. Large Building >=50 usable Cells, Dungeon >=100. Active means actual Units overlap usable Cells; low population means <4 Units or <.02 Units/usable Cell. Meaningful means Unit footprint, Loot, Machine anchor, Resource or GroundObject. Four seed-1 Medium recipes regenerated via code.",
            generationAssembly = typeof(WorldGenerator).Assembly.ManifestModule.ModuleVersionId,
            records
        }, json);
        if (args.Length > 0) File.WriteAllText(args[0], output); else Console.WriteLine(output);
    }

    private static JsonElement? Optional(object value, string property, JsonSerializerOptions json) =>
        value.GetType().GetProperty(property)?.GetValue(value) is { } result ? JsonSerializer.SerializeToElement(result, result.GetType(), json) : null;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record RoomMeasure(string Id, int UsableCells, int Units, int Loot, bool Empty);
    private sealed record SiteMeasure(string ParentSiteId, string Id, string Kind, int UsableCells, int Rooms, int EmptyRooms, int Units, int Loot,
        int Machines, int Resources, int GroundObjects, bool RewardedMonsterFree, bool CompletelyEmpty, bool Large, bool Active, bool LowPopulationLargeActive, RoomMeasure[] RoomDetails);
}
