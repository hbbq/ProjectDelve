using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainContentTests
{
    private static readonly TerrainDefinition Barrier = new("oddity-47", "The Velvet Interruption", false, false)
        { Presentation = new("an-unfamiliar-recipe") };
    private static readonly TerrainDefinition Opaque = new("cloud-19", "Opaque Cloud", true, true);
    private static readonly TerrainDefinition Rock = new("shape-62", "Tall Shape", false, true);
    private static TerrainTile Tile(int x, int y, string type) => new(new(x, y)) { TypeId = type };
    private static GameState Corridor(params TerrainTile[] tiles) => new()
    {
        Physical = new(new Board(7, 1, [])
        {
            TerrainTypes = TerrainCatalog.Resolve([Barrier, Opaque, Rock]), Terrain = [.. tiles]
        }, [new("actor", new(0, 0)), new("target", new(6, 0))]),
        Types = [new("actor-type", 6, 6, 1, 0, 1), new("target-type", 0, 0, 0, 0, 1)],
        Units = [new("actor", "actor-type", "blue", 1), new("target", "target-type", "red", 1)]
    };

    [Theory]
    [InlineData("oddity-47", false, false)]
    [InlineData("cloud-19", true, true)]
    [InlineData("shape-62", false, true)]
    public void UnfamiliarDefinitionsDrivePathsStoppingAndAttackGeometry(string id, bool passable, bool blocksLos)
    {
        var state = Corridor(Tile(2, 0, id));
        var board = state.Physical.Board;
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Equal(passable, paths.ContainsKey(new(2, 0)));
        Assert.Equal(passable, paths.ContainsKey(new(3, 0)));
        Assert.Equal(passable ? 6 : (int?)null, ApproachRules.Distance(board, new(0, 0), new(6, 0)));
        Assert.Equal(blocksLos ? UnitTargetEvaluation.NotPossible : UnitTargetEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
        Assert.Equal(id, board.TerrainDefinitionAt(new(2, 0)).Id);
        Assert.Equal((TerrainKind)(-1), board.TerrainAt(new(2, 0)));
    }

    [Fact]
    public void PhaseTraversesDeclaredObstructionButCannotStopThereOrIgnoreAttackLos()
    {
        var state = Corridor(Tile(2, 0, Rock.Id));
        state.Types[0] = state.Types[0] with { Phase = new() };
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(3, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
    }

    [Fact]
    public void LocalLosPropertiesPreserveOppositeCornerClosureAndDoorSemantics()
    {
        var board = new Board(2, 2, []) { TerrainTypes = TerrainCatalog.Resolve([Barrier, Rock]),
            Terrain = [Tile(1, 0, Rock.Id)] };
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1)));
        board.Terrain.Add(Tile(0, 1, Barrier.Id));
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1)));
        board.Terrain[1] = Tile(0, 1, Rock.Id);
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1)));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(0, 0)));
        board.Terrain.RemoveAt(1);
        board.Edges.Add(new(new(0, 0), new(0, 1), EdgeKind.ClosedDoor));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1)));
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1), phaseVision: true));
        board.Edges[0] = board.Edges[0] with { Kind = EdgeKind.OpenDoor };
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(1, 1)));
    }

    [Fact]
    public void MultiCellPlacementAndTranslationCheckEveryCellAndInternalEdge()
    {
        var board = new Board(5, 4, []) { TerrainTypes = TerrainCatalog.Resolve([Barrier]),
            Terrain = [Tile(2, 2, Barrier.Id)] };
        Assert.True(SpatialRules.Fits(board, Footprint.TwoByTwo, new(0, 0)));
        Assert.False(SpatialRules.Fits(board, Footprint.TwoByTwo, new(1, 1)));
        Assert.False(MovementRules.CanStep(board, Footprint.TwoByTwo, new(0, 1), new(1, 1), default));
        Assert.True(MovementRules.CanStep(board, Footprint.TwoByTwo, new(0, 1), new(1, 1), new(IgnoreTerrainAndEdges: true)));
        board.Terrain.Clear();
        board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        Assert.False(SpatialRules.Fits(board, Footprint.TwoByTwo, new(1, 1)));
    }

    [Fact]
    public void UnitCategoryRestrictionsAcceptAnyListedCategoryAndStillRequirePassability()
    {
        var road = new TerrainDefinition("new-road", "New Road", true, false) { Categories = ["racing-ground"] };
        var track = new TerrainDefinition("new-track", "New Track", true, false) { Categories = ["track"] };
        var blocked = new TerrainDefinition("blocked-road", "Blocked Road", false, false) { Categories = ["racing-ground"] };
        var state = Corridor();
        state.Physical = state.Physical with { Board = state.Physical.Board with
        {
            TerrainTypes = TerrainCatalog.Resolve([road, track, blocked]),
            Terrain = [new(new(0, 0), TerrainKind.Asphalt), Tile(1, 0, road.Id), Tile(2, 0, track.Id), Tile(3, 0, blocked.Id)]
        } };
        state.Types[0] = state.Types[0] with { TerrainRestriction = new(["racing-ground", "track"]) };
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(2, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(3, 0), paths.Keys);
        state.Types[0] = state.Types[0] with { TerrainRestriction = new(["racing-ground"]) };
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("actor-type", "new", "blue", new(4, 0)));
        state.PlaceUnit("actor-type", "new", "blue", new(1, 0));
        Assert.Equal("new", FootprintGeometry.UnitAtCell(state, new(1, 0)));
    }

    [Fact]
    public void LegacyAsphaltRestrictionResolvesCategoryWithoutChangingCanonicalSurfaces()
    {
        Assert.Equal(TerrainKind.Asphalt, Assert.Single(TerrainCatalog.All,
            d => d.Categories.Contains(TerrainCategories.RacingGround)).LegacyKind);
        var state = Corridor(new(new(0, 0), TerrainKind.Asphalt), new(new(1, 0), TerrainKind.Asphalt));
        state.Types[0] = state.Types[0] with { TerrainRestrictedMovement = new(TerrainKind.Asphalt) };
        var legacy = MovementRules.FindPaths(state, "actor", new(0, 0)).Keys.ToArray();
        state.Types[0] = state.Types[0] with { TerrainRestrictedMovement = null, TerrainRestriction = new([TerrainCategories.RacingGround]) };
        Assert.Equal(legacy, MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
        Assert.Contains(new Cell(1, 0), legacy);
        Assert.DoesNotContain(new Cell(2, 0), legacy);
    }

    [Fact]
    public void FogDiscoversBlockingDestinationAndFiltersHiddenDefinitionsAndPlacements()
    {
        var state = Corridor(Tile(1, 0, Rock.Id), Tile(4, 0, Barrier.Id));
        state.FogOfWarSides = ["blue"];
        FogOfWar.Refresh(state);
        Assert.True(FogOfWar.IsExplored(state, "blue", new(1, 0)));
        Assert.False(FogOfWar.IsExplored(state, "blue", new(2, 0)));
        var observation = FogOfWar.Observe(state, "blue");
        Assert.Contains(observation.Physical.Board.TerrainTypes, d => d.Id == Rock.Id);
        Assert.DoesNotContain(observation.Physical.Board.TerrainTypes, d => d.Id == Barrier.Id);
        Assert.DoesNotContain(observation.Physical.Board.Terrain, t => t.Position == new Cell(4, 0));
        Assert.Equal(Barrier.Id, state.Physical.Board.TerrainDefinitionAt(new(4, 0)).Id);
        state.Physical.Board.Terrain.RemoveAt(0);
        FogOfWar.Refresh(state);
        Assert.True(FogOfWar.IsExplored(state, "blue", new(4, 0)));
        Assert.Contains(FogOfWar.Observe(state, "blue").Physical.Board.TerrainTypes, d => d.Id == Barrier.Id);
    }

    [Fact]
    public void SavesAndDetachedCopiesRetainImmutableDefinitionsAndTypeReferences()
    {
        var state = Corridor(Tile(2, 0, Barrier.Id));
        state.Types[0] = state.Types[0] with { Rng = 1 };
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(Barrier, restored.Physical.Board.TerrainDefinitionAt(new(2, 0)));
        var queries = new GameplayQueries(restored);
        restored.Physical.Board.Terrain.Clear();
        Assert.Equal(Barrier.Id, queries.TerrainDefinitionAt(new(2, 0)).Id);
        Assert.Null(queries.DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.Equal("stone-floor", restored.Physical.Board.TerrainDefinitionAt(new(2, 0)).Id);
    }

    [Fact]
    public void CanonicalDefinitionsRoundTripAsDataAndLocalCatalogsRemainIsolated()
    {
        var json = JsonSerializer.Serialize(TerrainCatalog.All);
        var restored = JsonSerializer.Deserialize<ImmutableArray<TerrainDefinition>>(json);
        TerrainCatalog.Validate(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        var first = Corridor(Tile(1, 0, Barrier.Id));
        var alternate = Barrier with { Name = "Other Scenario", Passable = true };
        var second = new Board(2, 1, []) { TerrainTypes = TerrainCatalog.Resolve([alternate]), Terrain = [Tile(1, 0, Barrier.Id)] };
        Assert.False(first.Physical.Board.TerrainDefinitionAt(new(1, 0)).Passable);
        Assert.True(second.TerrainDefinitionAt(new(1, 0)).Passable);
        Assert.DoesNotContain(TerrainCatalog.All, d => d.Id == Barrier.Id);
    }

    [Fact]
    public void CatalogRejectsDuplicatesSpoofedCompatibilityAndMalformedDeclarations()
    {
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier, Barrier]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { Id = "stone-floor" }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { LegacyKind = TerrainKind.Lava }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { Categories = ["track", "track"] }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { Presentation = null! }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { Name = " " }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Barrier with { Passable = true, AllowsResourceMarkers = false }]));
        Assert.Throws<ArgumentException>(() => Corridor(Tile(2, 0, "missing")).Physical.Board.TerrainDefinitionAt(new(2, 0)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TerrainDefinition>(
            """{"Id":"x","Name":"X","Passable":false,"BlocksLos":false,"UnrecognizedRule":true}"""));
    }

    [Fact]
    public void ResourcePresenceAndProductionEligibilityRemainDifferentContracts()
    {
        var state = Corridor(Tile(2, 0, Barrier.Id));
        state.PlaceResource(ResourceTypes.Coal, new(2, 0));
        Assert.Single(state.Resources);
        Assert.False(ResourceRules.CanCreateAt(state, new(2, 0)));
        state.Physical.Board.Terrain.Add(new(new(3, 0)) { TypeId = "lava", CoalGeneration = false });
        Assert.Throws<ArgumentException>(() => state.PlaceResource(ResourceTypes.Coal, new(3, 0)));
        Assert.False(state.Physical.Board.TerrainDefinitionAt(new(3, 0)).Passable);
    }

    [Fact]
    public void PassableResourceForbiddenTerrainIsRejectedBeforeTransportCanCreateInvalidState()
    {
        var forbidden = new TerrainDefinition("void-61", "Void", true, false) { AllowsResourceMarkers = false };
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([forbidden]));
        var state = Corridor();
        state.Physical = state.Physical with { Board = state.Physical.Board with
        {
            TerrainTypes = TerrainCatalog.Resolve([forbidden with { Passable = false }]),
            Terrain = [Tile(2, 0, forbidden.Id)]
        } };
        Assert.Throws<ArgumentException>(() => state.PlaceResource(ResourceTypes.Coal, new(2, 0)));
        Assert.False(ResourceRules.CanCreateAt(state, new(2, 0)));
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
    }

    [Theory]
    [InlineData("""{"Id":"x","Name":"X","BlocksLos":false}""")]
    [InlineData("""{"Id":"x","Name":"X","Passable":false}""")]
    [InlineData("""{"Id":"x","Name":"X","Passable":false,"BlocksLos":false,"Presentation":{}}""")]
    public void MissingRequiredPropertiesAreRejectedEvenByTheRuntimeSaveSerializer(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TerrainDefinition>(json));
    }
}
