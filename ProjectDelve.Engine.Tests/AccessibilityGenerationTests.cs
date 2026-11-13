using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AccessibilityGenerationTests
{
    internal const string IslandRecipe = "WLD1-30x24-X-17-Y-13-F10-W4-R5-KBroad-HLakes-E1-I2-B1111-C1-NLow-VMedium-P2-Q1-TLow-JRoad-L1-ALow-U0-Z2-MMedium-D3-OClassic-S000000000000000D";
    internal const string LakeRecipe = "WLD1-50x50-X-17-Y-13-F25-W30-R10-KBroad-HLakes-E1-I2-B1111-C1-NLow-VMedium-P2-Q1-TLow-JMinorPath-L1-ALow-U1-Z2-MMedium-D3-OClassic-S000000000000000D";

    [Fact]
    public void LakeCountryDungeonKeepsOriginalRegionWhenCivilizationCreatesALargerOne()
    {
        var world = WorldGenerator.Generate(LakeRecipe, ContentBundle.Canonical());
        var board = world.Context.ConstructionBoard!;
        var site = Assert.Single(world.Context.Sites.Where(s => s.MountainDungeon is not null));
        var dungeon = site.MountainDungeon!;
        var anchored = PlayableWorldComponent.FromRoot(board, dungeon.AccessRootIndex);
        var largest = PlayableWorldComponent.Select(board);
        // Enrichment changes area counts; the regression is component rank changing while
        // every usable dungeon Cell retains access to its original, now smaller region.
        Assert.True(anchored.Count > 0 && anchored.Count < largest.Count);
        Assert.DoesNotContain(dungeon.AccessRootIndex, largest);
        Assert.All(site.Entrances, e => { Assert.Contains(e.InsideIndex, anchored); Assert.Contains(e.OutsideIndex, anchored); });
        Assert.All(dungeon.Rooms.SelectMany(r => r.Usable.Indices(board.Width)), i => Assert.Contains(i, anchored));
        // Finalization must still reject a real loss of access, not just suppress its old error.
        var siteIndex = world.Context.Sites.IndexOf(site);
        world.Context.Sites[siteIndex] = site with { MountainDungeon = dungeon with { AccessRootIndex = largest.Min() } };
        Assert.Contains("disconnected", Assert.Throws<ArgumentException>(() =>
            DungeonSitePopulation.FinalizeGeometryAndLoot(world.Context, ContentBundle.Canonical())).Message);
    }

    [Theory]
    [InlineData(30, 24, 12345UL, false, false)]
    [InlineData(30, 24, 12345UL, true, false)]
    [InlineData(60, 50, 3UL, false, false)]
    [InlineData(60, 50, 3UL, true, false)]
    [InlineData(60, 50, 3UL, false, true)]
    [InlineData(60, 50, 3UL, true, true)]
    public void SessionBHistoricalAccessFailuresRetainThePlannedRoot(int width, int height, ulong seed, bool forest, bool rock)
    {
        var recipe = new WorldGenerationRecipe(new(-17,-13,width,height), new(15,8,50,FeatureScale.Broad,WaterForm.Mixed), seed,
            new(Enabled:forest || rock,Intensity:3,Forest:forest,Rock:rock,Water:false,Grassland:false),new());
        var world = WorldGenerator.Generate(recipe);
        var sites = world.Context.Sites.Where(s => s.MountainDungeon is not null).ToArray();
        Assert.NotEmpty(sites); // These regressions must not disappear merely by omitting the dungeon.
        foreach (var site in sites)
        {
            var dungeon = site.MountainDungeon!;
            var connected = PlayableWorldComponent.FromRoot(world.Context.ConstructionBoard!, dungeon.AccessRootIndex);
            Assert.All(site.Entrances, e => { Assert.Contains(e.InsideIndex, connected); Assert.Contains(e.OutsideIndex, connected); });
            Assert.All(dungeon.Rooms.SelectMany(r => r.Usable.Indices(width)), i => Assert.Contains(i, connected));
        }
    }

    [Fact]
    public void GenerationAuditPreservesCurrentWorldAndRemainsDeterministic()
    {
        var world = WorldGenerator.Generate(IslandRecipe, ContentBundle.Canonical());
        // Enrichment may deliberately change this recipe's geography. Audit non-mutation
        // is the contract, rather than a fingerprint from an older terrain algorithm.
        var before = ScenarioDefinitionJson.ToJson(world.Context.Materialize());
        _ = WorldAccessibilityAudit.Geometry(world.Context, ContentBundle.Canonical());
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(world.Context.Materialize()));
        var audit = Assert.IsType<PopulationAccessibilityAudit>(world.Context.UnitAccessibility);
        Assert.NotNull(world.Preview.Frames.Single(f => f.Stage == "PostEnrichmentAnalysis").GeometryAccessibility);
        Assert.Null(world.Preview.Frames.Single(f => f.Stage == "PostEnrichmentAnalysis").UnitAccessibility);
        Assert.Same(audit, world.Preview.Frames[^1].UnitAccessibility);
        var repeated = WorldGenerator.Generate(IslandRecipe, ContentBundle.Canonical());
        Assert.Equal(world.Fingerprint, repeated.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(world.Preview), JsonSerializer.Serialize(repeated.Preview));
    }

    [Fact]
    public void OptionalSixCellIslandReportsAllThreeOccupantsWithoutMovingThem()
    {
        var type = new UnitType("island-guard", 3, 0, 0, 0, 1);
        var board = new Board(10, 3, [])
        {
            Terrain = Enumerable.Range(0, 3).Select(y => new TerrainTile(new(7, y), TerrainKind.SolidRock)).ToList()
        };
        var ids = new[] { "guard-1", "guard-2", "guard-3" };
        var state = new GameState
        {
            Physical = new(board, [new(ids[0], new(8, 0)), new(ids[1], new(9, 1)), new(ids[2], new(8, 2))]),
            Types = [type], Units = ids.Select(id => type.CreateUnit(id, "red")).ToList()
        };
        var encounter = new PopulationEncounter("island-encounter", "Wilderness", "island", [], [], [], [], []);
        var before = JsonSerializer.Serialize(state);
        var audit = WorldAccessibilityAudit.Units(state, ids, encounterFor: _ => encounter);
        Assert.Equal(3, audit.Units.Count);
        Assert.All(audit.Units, u =>
        {
            Assert.False(u.InPrincipalRegion);
            Assert.Equal(6, u.StoppingPositions);
            Assert.Equal(AccessibilityStatus.Disconnected, u.PrincipalAccess);
            Assert.Equal(encounter.Id, u.EncounterId);
        });
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void LargeRoomGuardHasOnlyRoomStoppingPositionsWithoutInvalidatingPopulation()
    {
        var type = new UnitType("room-guard", 3, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo };
        var board = new Board(7, 3, [])
        {
            Terrain = [new(new(3, 0), TerrainKind.SolidRock), new(new(3, 2), TerrainKind.SolidRock)]
        };
        var state = new GameState { Physical = new(board, [new("guard", new(0, 0))]), Types = [type], Units = [type.CreateUnit("guard", "red")] };
        var encounter = new PopulationEncounter("room-encounter", "DungeonRoom", "room", [], [], [], [], []);
        var before = JsonSerializer.Serialize(state);
        var audit = WorldAccessibilityAudit.Units(state, ["guard"], encounterFor: _ => encounter, roomGuard: _ => true);
        var guard = Assert.Single(audit.Units);
        Assert.True(guard.RoomBoundLargeGuard);
        Assert.True(guard.Complete);
        Assert.Equal(4, guard.StoppingPositions);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void AnchoredRegionDoesNotFallBackWhenRootIsBlockedOrOffBoard()
    {
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 2, Cells = [new(new(0, 0), TerrainKind.SolidRock)] },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        Assert.Empty(PlayableWorldComponent.FromRoot(board, 0));
        Assert.Empty(PlayableWorldComponent.FromRoot(board, -1));
        Assert.Equal(5, PlayableWorldComponent.FromRoot(board, 1).Count);
    }

    [Fact]
    public void LargeAuditBoundsWorkAndRetainedDoorDetails()
    {
        var type = new UnitType("walker", 3, 0, 0, 0, 1);
        var board = new Board(120, 120, Enumerable.Range(0, 120).SelectMany(y =>
            Enumerable.Range(0, 119).Select(x => new Edge(new(x, y), new(x + 1, y), EdgeKind.OpenDoor))).ToList());
        var ids = Enumerable.Range(0, 192).Select(i => "unit-" + i).ToArray();
        var state = new GameState
        {
            Physical = new(board, ids.Select((id, i) => new Figure(id, new(i % 120, i / 120))).ToList()),
            Types = [type], Units = ids.Select(id => type.CreateUnit(id, "blue")).ToList()
        };
        var audit = WorldAccessibilityAudit.Units(state, ids);
        Assert.True(audit.Complete);
        Assert.InRange(audit.WorkUsed, 1, 100_000);
        Assert.Equal(192, audit.Units.Count);
        Assert.All(audit.Units, unit =>
        {
            Assert.InRange(unit.Doors.Count, 0, WorldAccessibilityAudit.MaximumDoorsPerUnit);
            Assert.NotEmpty(unit.Assumptions);
        });
        Assert.Contains(audit.Units, unit => unit.DoorDetailsOmitted > 0);
    }
}
