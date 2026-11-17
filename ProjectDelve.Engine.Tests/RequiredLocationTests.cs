using System.Collections.Immutable;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RequiredLocationTests
{
    private static RequiredLocation Location(string id, RequiredAccessCoverage coverage = RequiredAccessCoverage.All,
        params int[] positions) => new(id, "test", id, [new("access", coverage, [.. positions])]);

    [Fact]
    public void RegistryIsImmutableAndValidatesConjunctiveGroupsAndExplicitSpecialAccess()
    {
        var original = RequiredLocationRegistry.Create([Location("ordinary", RequiredAccessCoverage.Any, 1, 2)], 4, 4);
        Assert.Empty(original.Errors);
        Assert.Equal(RequiredAccessClass.Ordinary, original.Locations[0].AccessClass);
        var special = new RequiredLocation("special", "test", "source", RequiredAccessClass.DeliberateSpecial, null,
            [new("all", RequiredAccessCoverage.All, [3]), new("any", RequiredAccessCoverage.Any, [])]);
        var invalid = original.Merge([special, Location("ordinary", RequiredAccessCoverage.All, 16)]);
        Assert.Single(original.Locations);
        Assert.Contains(invalid.Errors, e => e.Contains("Duplicate required-location"));
        Assert.Contains(invalid.Errors, e => e.Contains("deliberate-special reason"));
        Assert.Contains(invalid.Errors, e => e.Contains("nonempty positions"));
        Assert.Contains(invalid.Errors, e => e.Contains("outside the Board"));
        Assert.Equal(invalid.Errors.ToArray(), RequiredLocationRegistry.Create(invalid.Locations.Reverse(), 4, 4).Errors.ToArray());
        Assert.Empty(RequiredLocationRegistry.Create([special with { SpecialReason = "Requires special progression",
            Groups = [new("all", RequiredAccessCoverage.All, [3]), new("any", RequiredAccessCoverage.Any, [5, 6])] }], 4, 4).Errors);
    }

    [Fact]
    public void EmptyBuildingRoomsAreOrdinaryAndCampsiteFurnitureIsExcluded()
    {
        var board = new Board(5, 5, []) { Terrain = [new(new(2, 2), TerrainKind.StoneFloor) { TypeId = "stone-floor-with-table" }] };
        var roomCells = Area("room-usable", 6, 7);
        var room = new RoomInfo("empty-room", "empty", roomCells, roomCells, [], 0);
        var building = new BuildingInfo("vacant-house", "empty", roomCells, [room], [], new(2, 2, true, false, 0), []);
        var site = Site(GeneratedSiteKind.Campsite) with { Buildings = [building] };
        var locations = RequiredLocations.ForSite(site, "sites", board);
        var roomLocation = Assert.Single(locations.Where(l => l.SourceId == "empty-room"));
        Assert.Equal(RequiredAccessClass.Ordinary, roomLocation.AccessClass);
        Assert.Equal(RequiredAccessCoverage.All, roomLocation.Groups[0].Coverage);
        Assert.Equal(new[] { 6, 7 }, roomLocation.Groups[0].Positions);
        var camp = Assert.Single(locations.Where(l => l.Groups[0].Id == "usable"));
        Assert.DoesNotContain(12, camp.Groups[0].Positions);
        Assert.Contains(13, camp.Groups[0].Positions);
        Assert.Empty(RequiredLocationRegistry.Create(locations, 5, 5).Errors);
    }

    [Fact]
    public void DungeonKeepsStableRootAndAllUsableRoomsWhileSettlementIncludesPublicAccess()
    {
        var roomArea = Area("room-usable", 6, 7);
        var room = new RoomInfo("dungeon-room", "treasure-chamber", Area("interior", 6, 7, 8), roomArea, [], 0);
        var dungeon = Site(GeneratedSiteKind.MountainDungeon) with
        {
            MountainDungeon = new(MountainDungeonProfile.TunnelNetwork, [room], Area("corridor", 11),
                Area("shell", 0), Area("required", 6, 11), Area("lava", 8), [], 20, 0, 2) { AccessRootIndex = 24 }
        };
        var declarations = RequiredLocations.ForSite(dungeon, "sites", new Board(5, 5, []));
        Assert.Equal(new[] { 24 }, Assert.Single(declarations.Where(l => l.Groups[0].Id == "access-root")).Groups[0].Positions);
        Assert.Equal(new[] { 6, 7 }, Assert.Single(declarations.Where(l => l.SourceId == room.Id)).Groups[0].Positions);
        Assert.DoesNotContain(declarations.SelectMany(l => l.Groups).SelectMany(g => g.Positions), i => i == 8 || i == 0);
        var settlement = Site(GeneratedSiteKind.Settlement) with
        {
            SettlementLayout = new(SettlementFamily.MarketVillage, [], [new("square", "public", Area("square-area", 6, 7))], Area("access", 11), [])
        };
        var publicDeclarations = RequiredLocations.ForSite(settlement, "sites", new Board(5, 5, []));
        Assert.Equal(2, publicDeclarations.Length);
        Assert.All(publicDeclarations, l => Assert.Equal(RequiredAccessCoverage.All, l.Groups[0].Coverage));
        Assert.Contains(publicDeclarations, l => l.SourceId == "square");
        Assert.Contains(publicDeclarations, l => l.SourceId == "access");
    }

    [Fact]
    public void MachinePotentialUsesOperatingGeometryIncludingWallsCornersAndOpenableDoors()
    {
        var machine = new Machine("machine", new(2, 2), MachineTypes.DiamondPress);
        var board = new Board(5, 5,
            [new(new(1, 2), new(2, 2), EdgeKind.Wall), new(new(2, 1), new(2, 2), EdgeKind.ClosedDoor)])
            { Machines = [machine], Terrain = [new(new(1, 1), TerrainKind.SolidRock)] };
        var positions = MachineGeometry.PotentialOperatingPositions(board, machine);
        Assert.DoesNotContain(new Cell(1, 2), positions);
        Assert.DoesNotContain(new Cell(1, 1), positions);
        Assert.DoesNotContain(machine.Position, positions);
        Assert.Contains(new Cell(2, 1), positions);
        Assert.Contains(new Cell(3, 2), positions);
        Assert.Equal(EdgeKind.ClosedDoor, board.Edges[1].Kind);
        var sealedMachine = board with { Edges =
            [new(new(1, 2), new(2, 2), EdgeKind.Wall), new(new(2, 1), new(2, 2), EdgeKind.Wall),
             new(new(2, 2), new(3, 2), EdgeKind.Wall), new(new(2, 2), new(2, 3), EdgeKind.Wall)] };
        var declarations = RequiredLocations.ForInteractives(Definition() with { Machines = [machine] }, "interactives", sealedMachine);
        Assert.Equal(RequiredAccessCoverage.Any, declarations[0].Groups[0].Coverage);
        Assert.Empty(declarations[0].Groups[0].Positions);
        Assert.Contains(RequiredLocationRegistry.Create(declarations, 5, 5).Errors, e => e.Contains("nonempty positions"));
    }

    [Fact]
    public void InteractiveSourcesKeepPhysicalIdsAndResourcesUseDiagnosticCellTypeKeys()
    {
        var definition = Definition() with { Resources = [new(ResourceTypes.Coal, new(1, 1))],
            LootMarkers = [new("treasure", new(2, 1))], GroundObjects = [new(new("equipment"), new(3, 1))] };
        var first = RequiredLocations.ForInteractives(definition, "population", new Board(5, 5, []));
        Assert.Equal(new[] { "equipment", "treasure", "resource/coal/1/1" }, first.Select(l => l.SourceId));
        Assert.All(first, location => Assert.Equal(RequiredAccessClass.Ordinary, location.AccessClass));
        Assert.All(first, location => Assert.Equal(RequiredAccessCoverage.All, location.Groups[0].Coverage));
        Assert.Equal(first.Select(l => l.Id), RequiredLocations.ForInteractives(definition, "population", new Board(5, 5, [])).Select(l => l.Id));
    }

    private static ScenarioDefinition Definition() => new()
        { Board = new() { Width = 5, Height = 5 }, UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty };
    private static GeneratedArea Area(string id, params int[] positions) => GeneratedArea.From(id, positions, 5);
    private static SiteInfo Site(GeneratedSiteKind kind) => new("camp", kind, Area("footprint", 6, 7, 11, 12, 13),
        Area("exterior", 6, 7, 11, 12, 13), [], [], new(4, 4, true, false, 0), [], [], [], null, PreparationSummary.Empty);
}
