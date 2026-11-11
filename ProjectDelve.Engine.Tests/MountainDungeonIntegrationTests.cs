using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MountainDungeonIntegrationTests
{
    private static WorldGenerationRecipe Recipe(PopulationDensity density = PopulationDensity.Off) => new(
        new(Width: 60, Height: 50), new(15, 8, 50, FeatureScale.Broad, WaterForm.Mixed), 3,
        new(), new(), new(Profile: RouteProfileKind.RockPassage), new(density), "All");

    [Fact]
    public void LaterRoutesPreservePublishedShellRoomsAndOutwardOnlyAccess()
    {
        var world = WorldGenerator.Generate(Recipe(PopulationDensity.Medium)); var board = world.Context.ConstructionBoard!;
        var civilization = world.Preview.Frames.Single(f => f.Stage == "Civilization");
        var sites = world.Context.Sites.Where(s => s.MountainDungeon is not null).ToArray(); Assert.NotEmpty(sites);
        var principal = PlayableWorldComponent.Select(board);
        Assert.Equal(sites.Length, civilization.CivilizationStats!.DungeonsAccepted);
        Assert.Equal(world.Context.Sites.Sum(s => s.Buildings.Sum(b => b.Rooms.Count) + (s.MountainDungeon?.Rooms.Count ?? 0)), civilization.CivilizationStats.Rooms);
        foreach (var site in sites)
        {
            var dungeon = site.MountainDungeon!; Assert.Empty(site.Buildings);
            var excavation = dungeon.Rooms.SelectMany(r => r.Interior.Indices(board.Width)).Concat(dungeon.Corridors.Indices(board.Width)).ToHashSet();
            foreach (var i in dungeon.ProtectedRock.Indices(board.Width)) Assert.Equal("solid-rock", board.TerrainAt(i));
            foreach (var i in excavation)
                Assert.Equal(world.Preview.TerrainPalette[civilization.Terrain[i] - 'A'], board.TerrainAt(i));
            Assert.All(dungeon.Rooms.SelectMany(r => r.Usable.Indices(board.Width)), i => Assert.Contains(i, principal));
            Assert.DoesNotContain(world.Context.Routes.SelectMany(r => r.Corridor.Indices(board.Width)), excavation.Contains);
            Assert.DoesNotContain(site.AccessPorts.SelectMany(p => p.Permission.TraverseCells), excavation.Contains);
            foreach (var port in site.AccessPorts.Where(p => board.IsRouteCorridor(p.AnchorIndex) || board.IsRouteCorridor(p.OutwardIndex)))
                Assert.Contains(world.Context.RouteNetwork!.SiteAttachments, a => a.SiteId == site.Id && a.PortId == port.Id);
            Assert.NotEmpty(site.PopulationIntent!.EncounterRegions);
        }
    }

    [Fact]
    public void CurrentWldAndDelveRoundTripRetainLootWithoutPopulationAndEngineReplay()
    {
        var world = WorldGenerator.Generate(Recipe()); Assert.Null(world.Context.Population);
        Assert.Empty(world.Definition.Units); Assert.NotEmpty(world.Context.SiteLootMarkers);
        Assert.Equal(world.Context.SiteLootMarkers, world.Preview.Frames[^1].SiteLootMarkers);
        var repeated = WorldGenerator.Generate(world.Code); Assert.Equal(world.Fingerprint, repeated.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(world.Preview), JsonSerializer.Serialize(repeated.Preview));
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(world.Definition), ScenarioDefinitionJson.ToJson(restored));
        var room = world.Context.Sites.First(s => s.MountainDungeon is not null).MountainDungeon!.Rooms[0];
        var cell = room.Usable.Indices(60).First();
        restored = restored with { UnitTypeIds = [UnitTypeIds.Barbarian], Units = [new(UnitTypeIds.Barbarian, "blue", new(cell % 60, cell / 60), UnitId: "hero")],
            Agency = [new(UnitTypeIds.Barbarian, "blue", ControllerKind.Human)], FogOfWarSides = ["blue"], Objectives = [new("blue", ScenarioObjective.NoObjective)] };
        var game = GameEngine.CreateGame(restored); var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(game))!;
        var live = GameEngine.StartRound(game, new Dice(), false); var replay = GameEngine.StartRound(saved, new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(live), JsonSerializer.Serialize(replay));
        Assert.InRange(FogOfWar.Observe(live.State, "blue").Physical.Board.Terrain.Count, 1, 2999);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
