using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldLootAbundanceTests
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();

    [Fact]
    public void DefaultsAndEveryAbundanceRoundTripWithoutPriorState()
    {
        const string old = "WLD1-30x24-X0-Y0-F0-W0-R0-KBalanced-HAuto-S0000000000003039";
        var recipe = WorldGenerationRecipe.Parse(old);
        Assert.Equal("All", recipe.LootPreset);
        Assert.Equal(LootAbundance.Standard, recipe.LootAbundance);
        Assert.Contains("-OAll-LAStandard-HC5-S", recipe.ToString());
        foreach (var abundance in Enum.GetValues<LootAbundance>())
        foreach (var preset in new[] { "All", "Classic", "Empty" })
        {
            var selected = recipe with { LootPreset = preset, LootAbundance = abundance };
            Assert.Equal(selected, WorldGenerationRecipe.Parse(selected.ToString()));
            Assert.Equal(abundance, WorldGenerationPlan.FromRecipe(selected).LootAbundance);
        }
        Assert.Throws<ArgumentException>(() => (recipe with { LootAbundance = (LootAbundance)99 }).Validate());
        Assert.Throws<FormatException>(() => WorldGenerationRecipe.Parse(recipe.ToString().Replace("LAStandard", "LARare")));
    }

    [Fact]
    public void GeneratedDefaultAllUsesSharedDynamicMembershipAndUniformCopies()
    {
        var content = Content with { LootCards = [.. Content.LootCards,
            new("fixture-extra-reward", "Extra reward") { Immediate = new HealCollectorMechanic(3) }] };
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 30, Height: 24), new(0, 0, 0), 12,
            Population: new(PopulationDensity.High)), content);
        var rows = world.Definition.Loot!.ResolveComposition(content.LootCards, content.LootPresets);
        Assert.Equal("All", world.Definition.Loot.Preset);
        Assert.Equal(content.LootCards.Select(c => c.Id).Order(StringComparer.Ordinal), rows.Select(r => r.LootCardId));
        Assert.Single(rows.Select(r => r.Count).Distinct());
        Assert.True(rows.Sum(r => r.Count) >= world.Definition.LootMarkers!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeUnoccupiedSitesDistributeSeededRewardsAcrossRoomsAndKeepAccessClear(bool dungeon)
    {
        var sawReward = false; var sawVacant = false;
        for (ulong seed = 0; seed < 12; seed++)
        {
            var counts = new List<int>();
            foreach (var abundance in Enum.GetValues<LootAbundance>())
            {
                var context = Fixture(seed, dungeon, abundance);
                SiteLootGenerator.Generate(context, Content);
                var markers = context.Materialize().LootMarkers ?? [];
                counts.Add(markers.Count);
                Assert.Empty(context.Materialize().Units);
                Assert.Equal(context.SiteLootStatistics!.Placed, markers.Count);
                Assert.Equal(0, context.SiteLootStatistics.Requested - context.SiteLootStatistics.Placed);
                var reserved = context.PopulationProtectedAreas.SelectMany(a => a.Indices(48))
                    .Concat(context.Sites[0].MountainDungeon?.RequiredAccess.Indices(48) ?? []).ToHashSet();
                Assert.All(markers, marker => Assert.DoesNotContain(marker.Position.Y * 48 + marker.Position.X, reserved));
                if (abundance == LootAbundance.Standard && markers.Count > 0)
                {
                    var rooms = dungeon ? context.Sites[0].MountainDungeon!.Rooms : context.Sites[0].Buildings[0].Rooms;
                    Assert.True(rooms.Count(r => markers.Any(m => r.Usable.Contains(m.Position.Y * 48 + m.Position.X, 48))) >= 2);
                    sawReward = true;
                }
                var repeated = Fixture(seed, dungeon, abundance); SiteLootGenerator.Generate(repeated, Content);
                Assert.Equal(JsonSerializer.Serialize(markers), JsonSerializer.Serialize(repeated.Materialize().LootMarkers ?? []));
                var state = GameEngine.CreateGame(context.Materialize(), Content);
                Assert.True(state.LootDeck!.DrawPile.Count >= markers.Count);
            }
            Assert.True(counts[0] <= counts[1] && counts[1] <= counts[2]);
            sawVacant |= counts[2] == 0;
        }
        Assert.True(sawReward); Assert.True(sawVacant);
    }

    [Fact]
    public void EmptyAndFiniteCustomSuppliesAreNotExpandedIncludingStackedReservations()
    {
        var empty = Fixture(1, true, LootAbundance.Plentiful); empty.Loot = LootSettings.Empty;
        SiteLootGenerator.Generate(empty, Content); Assert.Empty(empty.SiteLootMarkers);
        var custom = Fixture(1, true, LootAbundance.Plentiful);
        custom.Loot = new() { Composition = [new(LootCardKind.LuckyBoots, 2)] };
        custom.SiteLootMarkers.Add(new("existing-a", new(1, 1)));
        custom.SiteLootMarkers.Add(new("existing-b", new(1, 1)));
        SiteLootGenerator.Generate(custom, Content);
        Assert.Equal(2, custom.Materialize().LootMarkers!.Count);
        Assert.Equal(2, custom.Materialize().Loot!.ResolveComposition().Sum(c => c.Count));
    }

    [Fact]
    public void WldAbundanceChangesLootWithoutChangingPopulationGeometryOrDefeatChances()
    {
        var worlds = Enum.GetValues<LootAbundance>().Select(abundance => WorldGenerator.Generate(new WorldGenerationRecipe(
            new(Width: 40, Height: 35), new(0, 0, 0), 12, Civilization: new(),
            Population: new(PopulationDensity.High, 5), LootAbundance: abundance))).ToArray();
        Assert.True(worlds[0].Definition.LootMarkers!.Count < worlds[1].Definition.LootMarkers!.Count);
        Assert.True(worlds[1].Definition.LootMarkers!.Count < worlds[2].Definition.LootMarkers!.Count);
        foreach (var world in worlds)
        {
            Assert.Equal(JsonSerializer.Serialize(worlds[0].Definition.Board), JsonSerializer.Serialize(world.Definition.Board));
            Assert.Equal(worlds[0].Definition.Units, world.Definition.Units);
            Assert.True(PlayReadyScenarioSetup.Validate(world.Definition, Content).Valid);
            var state = GameEngine.CreateGame(world.Definition, Content);
            var reached = PlayReadyScenarioSetup.ReachableFromDeployment(world.Definition, state);
            var protectedCells = world.Context.PopulationProtectedAreas.SelectMany(a => a.Indices(40)).ToHashSet();
            Assert.All(world.Definition.LootMarkers!, marker =>
            {
                var index = marker.Position.Y * 40 + marker.Position.X;
                Assert.Contains(index, reached); Assert.DoesNotContain(index, protectedCells);
                Assert.DoesNotContain(state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id)), c => c == marker.Position);
            });
            Assert.All(state.Types, type => Assert.Equal(Content.Units.Single(t => t.Id == type.Id).LootDropSuccesses, type.LootDropSuccesses));
            var repeated = WorldGenerator.Generate(world.Code);
            Assert.Equal(world.Fingerprint, repeated.Fingerprint);
            Assert.Equal(world.Context.Population!.Statistics.RequestedLootMarkers, world.Context.Population.Statistics.LootMarkers);
        }
    }

    // Test-owned authoring regions over explicit connected terrain, never a mutable playtest scenario.
    private static WorldGenerationContext Fixture(ulong seed, bool dungeon, LootAbundance abundance)
    {
        var context = new WorldGenerationContext(new(seed, new(0, 0, 0)), new(Width: 48, Height: 24))
        { Loot = new() { Preset = "All" }, LootAbundance = abundance };
        context.SampleNaturalTerrain();
        var rooms = Enumerable.Range(0, 3).Select(n =>
        {
            var area = GeneratedArea.From("fixture/room-" + n,
                Enumerable.Range(8, 7).SelectMany(y => Enumerable.Range(5 + n * 12 + y * 48, 8)), 48);
            return new RoomInfo(area.Id, "room", area, area, [], 42);
        }).ToArray();
        var corridor = GeneratedArea.From("fixture/corridor", Enumerable.Range(2 + 11 * 48, 35), 48);
        var all = GeneratedArea.From("fixture/floor", rooms.SelectMany(r => r.Usable.Indices(48)).Concat(corridor.Indices(48)), 48);
        context.ConstructionBoard = new(context.Materialize() with { Board = new() { Width = 48, Height = 24,
            Cells = all.Indices(48).Select(i => new CellDefinition(new(i % 48, i / 48), TerrainKind.StoneFloor)).ToList() } }, Content);
        var empty = GeneratedArea.From("fixture/empty", [], 48);
        var access = new SpaceAccess(all.Area, all.Area, true, false, 42);
        var entrance = new SitePortal("fixture/entrance", "fixture/site", 11 * 48 + 2, 11 * 48 + 1,
            EdgeKey.Between(11 * 48 + 2, 11 * 48 + 1, 48), EdgeKind.None);
        context.Sites.Add(new("fixture/site", dungeon ? GeneratedSiteKind.MountainDungeon : GeneratedSiteKind.Settlement,
            all, empty, dungeon ? [] : [new("fixture/building", "workshop", all, rooms, [entrance], access, [])],
            [entrance], access, [], [], [], null, PreparationSummary.Empty)
        { MountainDungeon = dungeon ? new(MountainDungeonProfile.DeepPassage, rooms, corridor, empty, corridor, empty, [], all.Area, 0, 3)
            { AccessRootIndex = 0 } : null });
        context.PopulationProtectedAreas.Add(GeneratedArea.From("fixture/deployment", [0, 1, 48, 49], 48));
        return context;
    }
}


