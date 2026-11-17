using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SitePopulationAllocationTests
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();

    [Fact]
    public void BuildingsScaleWithAreaRoomsAndDensityWithinTheSameGlobalAllowances()
    {
        var totals = new int[3]; var small = 0; var large = 0; var distributed = false;
        for (ulong seed = 0; seed < 12; seed++)
        for (var density = PopulationDensity.Low; density <= PopulationDensity.High; density++)
        {
            var context = BuildingFixture(seed); var population = Generate(context, density);
            var smallStats = population.SiteStatistics.Single(s => s.SourceId == "fixture/small");
            var largeStats = population.SiteStatistics.Single(s => s.SourceId == "fixture/large");
            totals[(int)density - 1] += largeStats.AcceptedUnits;
            if (largeStats.SelectedForOccupation && smallStats.SelectedForOccupation)
            { small += smallStats.AcceptedUnits; large += largeStats.AcceptedUnits; }
            var roomRoles = population.Encounters.Where(e => e.SourceId == "fixture/large")
                .SelectMany(e => e.Roles).Select(r => r.AreaId).Distinct().Count();
            distributed |= roomRoles >= 2;
            Assert.InRange(population.Statistics.Units, 0, population.Statistics.UnitAllowance);
            Assert.InRange(population.Statistics.AcceptedEncounters, 0, population.Statistics.Target);
            Assert.InRange(largeStats.AcceptedUnits, 0, density switch { PopulationDensity.Low => 6, PopulationDensity.High => 12, _ => 9 });
            Assert.Equal(population.Statistics.RequestedUnits - population.Statistics.Units, population.Statistics.UnitShortfall);
            Assert.True(population.Statistics.RequestedUnits >= population.Encounters.Sum(e => e.RequestedUnits));
        }
        Assert.True(large > small); Assert.True(distributed);
        Assert.True(totals[0] < totals[1] && totals[1] < totals[2]);
    }

    [Fact]
    public void DungeonAllocationUsesExplorationAreaRoomCountAndDensityAndKeepsQuietRooms()
    {
        var smallRequests = 0; var largeRequests = 0; var densityUnits = new int[3]; var quiet = false;
        for (ulong seed = 0; seed < 12; seed++)
        {
            foreach (var (rooms, areaScale) in new[] { (1, 1), (3, 2) })
            {
                var context = DungeonFixture(seed, rooms, areaScale);
                var population = Generate(context, PopulationDensity.Medium);
                var stats = Assert.Single(population.SiteStatistics);
                if (rooms == 1) smallRequests += stats.RequestedUnits; else largeRequests += stats.RequestedUnits;
                quiet |= stats.SelectedForOccupation && stats.IntentionallyOmittedRooms > 0;
                Assert.Equal(rooms, stats.Rooms);
                if (stats.SelectedForOccupation && stats.UsableCells >= 100) Assert.InRange(stats.RequestedEncounters, 2, 4);
                Assert.All(population.Encounters.Where(e => e.Kind == "DungeonEncounter"), encounter =>
                {
                    var region = context.Sites[0].PopulationIntent!.EncounterRegions.Single(r => r.Id == encounter.SourceId);
                    Assert.All(encounter.Units, u => Assert.True(region.Area.Contains(u.Anchor.Y * 80 + u.Anchor.X, 80)));
                    var ids = encounter.Units.Select(u => u.UnitId).ToHashSet();
                    Assert.All(Assert.Single(population.AlertComponents, c => c.UnitIds.Any(ids.Contains)).UnitIds, id => Assert.Contains(id, ids));
                });
            }
            for (var density = PopulationDensity.Low; density <= PopulationDensity.High; density++)
                densityUnits[(int)density - 1] += Generate(DungeonFixture(seed, 3, 2), density).Statistics.Units;
        }
        Assert.True(largeRequests > smallRequests); Assert.True(quiet);
        Assert.True(densityUnits[0] < densityUnits[1] && densityUnits[1] < densityUnits[2]);
    }

    [Fact]
    public void LargeVacancyCanBeRewardedOrCompletelyEmptyAndLootSelectionRemainsIndependent()
    {
        var rewardedVacancy = false; var emptyVacancy = false; var active = false;
        for (ulong seed = 0; seed < 128 && !(rewardedVacancy && emptyVacancy && active); seed++)
        {
            var context = BuildingFixture(seed); var population = Generate(context, PopulationDensity.Medium, loot: true);
            var stats = population.SiteStatistics.Single(s => s.SourceId == "fixture/large");
            active |= stats.SelectedForOccupation && stats.AcceptedUnits > 0;
            rewardedVacancy |= stats.Selection == "Vacant" && stats.MonsterFree && stats.MeaningfulNoncombatObjects > 0;
            emptyVacancy |= stats.Selection == "Vacant" && stats.CompletelyEmpty;
            var withoutMonsters = BuildingFixture(seed); SiteLootGenerator.Generate(withoutMonsters, Content);
            Assert.Equal(context.SiteLootStatistics!.SelectedLocations, withoutMonsters.SiteLootStatistics!.SelectedLocations);
            Assert.Equal(context.SiteLootStatistics.UnrewardedLocations, withoutMonsters.SiteLootStatistics.UnrewardedLocations);
            Assert.Equal("All", context.Materialize().Loot!.Preset);
            Assert.Single(context.Materialize().Loot!.ResolveComposition().Select(r => r.Count).Distinct());
        }
        Assert.True(active); Assert.True(rewardedVacancy); Assert.True(emptyVacancy);
    }

    [Fact]
    public void FurnitureSuppliesRequiredPathsAndExistingLootRemainClear()
    {
        for (ulong seed = 0; seed < 8; seed++)
        {
            var context = BuildingFixture(seed, supplies: true);
            var existing = new LootMarker("fixture/existing-loot", new(37, 10)); context.SiteLootMarkers.Add(existing);
            var before = JsonSerializer.Serialize(context.Materialize().Board);
            var population = Generate(context, PopulationDensity.High, loot: true);
            var protectedCells = context.PopulationProtectedAreas.SelectMany(a => a.Indices(80)).ToHashSet();
            var supplies = context.Sites[0].PopulationIntent!.Areas.Where(a => a.Role == "supplies").SelectMany(a => a.Area.Indices(80)).ToHashSet();
            foreach (var unit in population.Encounters.SelectMany(e => e.Units))
            foreach (var cell in FootprintGeometry.OccupiedCells(Content.Units.Single(t => t.Id == unit.UnitTypeId).Footprint, unit.Anchor))
            {
                var index = cell.Y * 80 + cell.X;
                Assert.DoesNotContain(index, protectedCells); Assert.DoesNotContain(index, supplies);
                Assert.NotEqual(existing.Position, cell); Assert.True(context.ConstructionBoard!.Passable(index));
            }
            Assert.Contains(existing, context.Materialize().LootMarkers!);
            Assert.Equal(before, JsonSerializer.Serialize(context.Materialize().Board));
        }
    }

    [Fact]
    public void TypedRegionsOutsideDungeonsHonorTheirDeclaredMinimumAndPreferences()
    {
        var saw = false;
        for (ulong seed = 0; seed < 8; seed++)
        {
            var context = DungeonFixture(seed, 1, 1);
            var site = context.Sites[0];
            var region = site.PopulationIntent!.EncounterRegions[0] with { MinimumUnits = 4, MaximumUnits = 6 };
            context.Sites[0] = site with { Kind = GeneratedSiteKind.Campsite, MountainDungeon = null,
                PopulationIntent = site.PopulationIntent with { Areas = [], EncounterRegions = [region,
                    new("empty-region", GeneratedArea.From("empty-region", [], 80), 1, 1, [UnitTypeIds.Zombie], [])] } };
            var population = Generate(context, PopulationDensity.Low);
            foreach (var encounter in population.Encounters)
            {
                saw = true; Assert.InRange(encounter.Units.Count, 4, 6);
                Assert.All(encounter.Units, unit => Assert.Equal(UnitTypeIds.Zombie, unit.UnitTypeId));
            }
        }
        Assert.True(saw);
    }

    [Fact]
    public void RequestsUnattemptedAtTheGlobalEncounterLimitHaveBudgetDiagnostics()
    {
        var sawOmission = false;
        for (ulong seed = 0; seed < 12 && !sawOmission; seed++)
        {
            var context = Base(seed); var empty = Area("empty", 0, 0, 0, 0);
            var buildings = Enumerable.Range(0, 18).Select(n =>
            {
                var area = Area("fixture/room-" + n, 4 + n % 6 * 12, 4 + n / 6 * 9, 5, 4);
                var room = new RoomInfo(area.Id, "room", area, area, [], 12);
                var inside = area.Indices(80).First(); var outside = inside - 1;
                return new BuildingInfo("fixture/building-" + n, "house", area, [room],
                    [new("door-" + n, "fixture/building-" + n, inside, outside, EdgeKey.Between(inside, outside, 80), EdgeKind.None)],
                    new(20, 20, true, false, 12), []);
            }).ToArray();
            var footprint = GeneratedArea.From("fixture/many-buildings", buildings.SelectMany(b => b.Footprint.Indices(80)), 80);
            context.Sites.Add(new("fixture/settlement", GeneratedSiteKind.Settlement, footprint, empty, buildings, [],
                new(footprint.Area, footprint.Area, true, false, 12), [], [], [], null, PreparationSummary.Empty));
            ProtectOutside(context, footprint, empty);
            var population = Generate(context, PopulationDensity.Low);
            if (!population.SiteStatistics.Any(s => s.BudgetOmissions > 0)) continue;
            sawOmission = true;
            Assert.True(population.Statistics.GlobalBudgetExhausted);
            Assert.Equal(population.Statistics.Target, population.Statistics.AcceptedEncounters);
            Assert.InRange(population.Statistics.Units, 0, population.Statistics.UnitAllowance);
            Assert.All(population.SiteStatistics, s => Assert.Equal(s.RequestedEncounters,
                s.AcceptedEncounters + s.PlacementRejectedEncounters + s.AlertRejectedEncounters + s.BudgetOmissions + s.WorkOmissions));
        }
        Assert.True(sawOmission);
    }

    [Fact]
    public void GlobalCapsAndRequestDiagnosticsRemainHonestWhenSitesExceedTheWorldBudget()
    {
        var context = BuildingFixture(12);
        // A small request quota is a resource policy, distinct from placement reductions.
        var population = Generate(context, PopulationDensity.Low);
        Assert.NotEqual(population.Statistics.UnitAllowance, population.Statistics.RequestedUnits);
        Assert.All(population.SiteStatistics, s => Assert.Equal(s.RequestedEncounters,
            s.AcceptedEncounters + s.PlacementRejectedEncounters + s.AlertRejectedEncounters + s.BudgetOmissions + s.WorkOmissions));
        var recipe = new WorldGenerationRecipe(new(Width: 60, Height: 50), new(15, 8, 50), 1,
            new(), new(Campsites: ContentDensity.VeryHigh, Settlements: ContentDensity.VeryHigh), new(), new(PopulationDensity.High, 5));
        var world = WorldGenerator.Generate(recipe);
        Assert.InRange(world.Definition.Units.Count, 0, PopulationGenerator.MaxUnits);
        Assert.InRange(world.Context.Population!.Statistics.Groups, 0, PopulationGenerator.MaxGroups);
        Assert.InRange(world.Context.Population.Statistics.AcceptedEncounters, 0, world.Context.Population.Statistics.Target);
        Assert.True(PlayReadyScenarioSetup.Validate(world.Definition, Content).Valid);
        Assert.Equal(world.Fingerprint, WorldGenerator.Generate(world.Code).Fingerprint);
        var prepared = world.Definition.Loot!.ResolveComposition(Content.LootCards, Content.LootPresets);
        Assert.True(prepared.Sum(r => r.Count) >= world.Definition.LootMarkers!.Count);
    }

    private static PopulationSnapshot Generate(WorldGenerationContext context, PopulationDensity density, bool loot = false)
    {
        context.Population = PopulationGenerator.Generate(context, new(density, 5), Content);
        if (loot) { SiteLootGenerator.Generate(context, Content); SitePopulationAllocation.FinalizeContent(context); }
        return context.Population;
    }

    private static GeneratedArea Area(string id, int x, int y, int width, int height) => GeneratedArea.From(id,
        Enumerable.Range(y, height).SelectMany(row => Enumerable.Range(row * 80 + x, width)), 80);

    private static WorldGenerationContext Base(ulong seed)
    {
        var context = new WorldGenerationContext(new(seed, new(0, 0, 0)), new(Width: 80, Height: 40))
        { Loot = new() { Preset = "All" } };
        context.SampleNaturalTerrain();
        context.ConstructionBoard = new(context.Materialize() with { Board = new() { Width = 80, Height = 40,
            Edges = [new(new(4, 3), EdgeDirection.Down, EdgeKind.ClosedDoor), new(new(21, 11), EdgeDirection.Right, EdgeKind.ClosedDoor)],
            Cells = [new(new(25, 10), TerrainKind.StoneFloor) { TerrainTypeId = "stone-floor-with-table" }] } }, Content);
        return context;
    }

    private static WorldGenerationContext BuildingFixture(ulong seed, bool supplies = false)
    {
        var context = Base(seed); var empty = Area("empty", 0, 0, 0, 0);
        var smallArea = Area("small-room", 4, 4, 6, 4);
        var small = new RoomInfo("small-room", "room", smallArea, smallArea, [], 15);
        var rooms = Enumerable.Range(0, 3).Select(n =>
        {
            var area = Area("large-room-" + n, 24 + n * 12, 10, 8, 4);
            return new RoomInfo(area.Id, "room", area, GeneratedArea.From(area.Id + "/usable", area.Indices(80).Where(context.ConstructionBoard!.Passable), 80), [], 21);
        }).ToArray();
        var access = Area("fixture/access", 22, 11, 34, 1);
        BuildingInfo Building(string id, IReadOnlyList<RoomInfo> source, GeneratedArea paths, int inside, int outside)
        {
            var footprint = GeneratedArea.From(id, source.SelectMany(r => r.Interior.Indices(80)).Concat(paths.Indices(80)), 80);
            var portal = new SitePortal(id + "/door", id, inside, outside, EdgeKey.Between(inside, outside, 80), EdgeKind.ClosedDoor);
            return new(id, "house", footprint, source, [portal], new(footprint.Area, footprint.Area, true, false, 10), []);
        }
        var buildings = new[] { Building("fixture/small", [small], empty, 4 + 4 * 80, 4 + 3 * 80),
            Building("fixture/large", rooms, access, 22 + 11 * 80, 21 + 11 * 80) };
        var floor = GeneratedArea.From("fixture/floor", buildings.SelectMany(b => b.Footprint.Indices(80)), 80);
        var areas = rooms.Prepend(small).Select(r => new SitePopulationArea(r.Id, supplies && r == rooms[0] ? "supplies" : "resting",
            r.Usable, r.Usable.Indices(80).First(), r.Id == small.Id ? buildings[0].Id : buildings[1].Id)).ToArray();
        context.Sites.Add(new("fixture/site", GeneratedSiteKind.Settlement, floor, empty, buildings, [],
            new(floor.Area, floor.Area, true, false, 10), [], [], [], null, PreparationSummary.Empty)
        { PopulationIntent = new("fixture", areas, [], 2, 8, CampAlertTopology.Connected) });
        ProtectOutside(context, floor, access); return context;
    }

    private static WorldGenerationContext DungeonFixture(ulong seed, int roomCount, int areaScale)
    {
        var context = Base(seed); var empty = Area("empty", 0, 0, 0, 0);
        var rooms = Enumerable.Range(0, roomCount).Select(n =>
        {
            var area = Area("dungeon-room-" + n, 24 + n * 12, 8, 5 + areaScale, 4 + areaScale);
            return new RoomInfo(area.Id, "chamber", area, GeneratedArea.From(area.Id + "/usable", area.Indices(80).Where(context.ConstructionBoard!.Passable), 80), [], 20);
        }).ToArray();
        var access = Area("access", 22, 11, roomCount * 12, 1);
        var floor = GeneratedArea.From("dungeon/floor", rooms.SelectMany(r => r.Interior.Indices(80)).Concat(access.Indices(80)), 80);
        var regions = rooms.Select(r => new SiteEncounterRegion(r.Id + "/encounter", r.Usable, 1, 7, [UnitTypeIds.Zombie], [])).ToArray();
        context.Sites.Add(new("fixture/dungeon", GeneratedSiteKind.MountainDungeon, floor, empty, [], [],
            new(floor.Area, floor.Area, true, false, 10), [], [], [], null, PreparationSummary.Empty)
        { MountainDungeon = new(MountainDungeonProfile.DeepPassage, rooms, access, empty, access, empty, [], floor.Area, 0, rooms.Length) { AccessRootIndex = 0 },
            PopulationIntent = new("fixture", [], [], 0, 24, CampAlertTopology.Separated) { EncounterRegions = regions } });
        ProtectOutside(context, floor, access); return context;
    }

    private static void ProtectOutside(WorldGenerationContext context, GeneratedArea floor, GeneratedArea access)
    {
        var allowed = floor.Indices(80).ToHashSet();
        context.PopulationProtectedAreas.Add(GeneratedArea.From("fixture/protected", Enumerable.Range(0, 3200).Where(i => !allowed.Contains(i)).Concat(access.Indices(80)), 80));
        context.RefreshConstructionAnalysis();
    }
}
