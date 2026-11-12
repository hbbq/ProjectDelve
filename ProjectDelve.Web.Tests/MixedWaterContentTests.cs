using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class MixedWaterContentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static readonly Cell SecretCell = new(7, 1);

    private static ScenarioDefinition Fixture(bool fog = false) => new()
    {
        Board = new() { Width = 10, Height = 3, Cells = [
            new(new(1, 0), TerrainKind.StoneFloor) { TerrainTypeId = "wet-grass" },
            new(new(3, 2), TerrainKind.StoneFloor) { TerrainTypeId = "rocky-water" },
            new(SecretCell, TerrainKind.StoneFloor) { TerrainTypeId = "stepping-stones" }] },
        UnitTypes = [new("mixed-observer", 2, 1, 1, 1, 5) { Unique = true }], UnitTypeIds = ["mixed-observer"],
        Units = [new("mixed-observer", "blue", new(0, 1), UnitId: "observer")],
        Agency = [new("mixed-observer", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        FogOfWarSides = fog ? ["blue"] : null, Loot = LootSettings.Empty
    };

    [Theory]
    [InlineData("wet-grass", "WetGrass", "vegetation")]
    [InlineData("rocky-water", "RockyWater", "rock")]
    [InlineData("stepping-stones", "SteppingStones", "constructed")]
    public void ContentBrowserProjectsCanonicalTypedDefinitionsAndDesignerTransportReferences(
        string id, string recipe, string category)
    {
        var canonical = ContentBrowserCatalog.Canonical();
        var entry = Assert.Single(canonical.Entries, e => e.Family == "terrain" && e.Id == id);
        Assert.Equal(new[] { "land", "water" }, entry.Definition.GetProperty("movementPermissions").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(new[] { "land", "water", category }, entry.Definition.GetProperty("categories").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(recipe, entry.Definition.GetProperty("presentation").GetProperty("recipe").GetString());
        Assert.False(entry.Definition.GetProperty("blocksLos").GetBoolean());
        Assert.False(entry.Definition.GetProperty("blocksVision").GetBoolean());
        Assert.True(entry.Definition.GetProperty("allowsResourceMarkers").GetBoolean());
        var typed = JsonSerializer.Deserialize<TerrainDefinition>(entry.Definition.GetRawText(), Json)!;
        Assert.Equal(JsonSerializer.Serialize(TerrainCatalog.All.Single(t => t.Id == id), Json), JsonSerializer.Serialize(typed, Json));
        Assert.Null(typed.LegacyKind);
        if (id == "stepping-stones")
        {
            Assert.Equal("water", typed.DestroyedTerrainTypeId);
            Assert.Contains(entry.References, r => r.TargetKey == "terrain:water" && r.Exists);
        }
        else Assert.Null(typed.DestroyedTerrainTypeId);

        var fixture = Fixture();
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(fixture));
        Assert.Equal(ScenarioDefinitionJson.ToJson(fixture), ScenarioDefinitionJson.ToJson(restored));
        Assert.True(restored.TerrainTypes is null || restored.TerrainTypes.Count == 0);
        var scoped = Assert.Single(ContentBrowserCatalog.Scenario(restored).Entries, e => e.Family == "terrain" && e.Id == id);
        Assert.Equal(entry.Definition.GetRawText(), scoped.Definition.GetRawText());
        var cell = restored.Board.Cells.Single(c => c.TerrainTypeId == id).Position;
        Assert.Equal(id, GameEngine.CreateGame(restored).Physical.Board.TerrainDefinitionAt(cell).Id);
    }

    [Fact]
    public void CanonicalMixedTerrainIdentityAndPermissionsSurviveDetachedSaveAndRestart()
    {
        var fixture = Fixture();
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(fixture));
        var imported = game.StartTransportScenario(game.Snapshot().Revision, ScenarioDefinitionTransport.Encode(fixture));
        var round = game.StartRound(imported.Revision);
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        var saved = PlaytestGame.Detach(moved);
        using var resumed = new PlaytestGame(new Dice(), saved.Result.State);
        Assert.Equal(JsonSerializer.Serialize(saved.Result.State), JsonSerializer.Serialize(resumed.Snapshot().Result.State));
        GameEngine.RefreshChoices(resumed.Snapshot().Result.State, new Dice(), false);
        var restarted = game.Restart(moved.Revision);
        foreach (var cell in fixture.Board.Cells)
        {
            var type = restarted.Result.State.Physical.Board.TerrainDefinitionAt(cell.Position);
            Assert.Equal(cell.TerrainTypeId, type.Id);
            Assert.Equal(new[] { "land", "water" }, type.MovementPermissions);
        }
        Assert.Equal(imported.VisualSeed, restarted.VisualSeed);
        Assert.Equal(new Cell(0, 1), Assert.Single(restarted.Result.State.Physical.Figures).Position);
    }

    [Fact]
    public async Task CanonicalMixedTerrainObservationAndReplayKeepEventTimeDiscovery()
    {
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(Fixture(fog: true)));
        using var live = game.Subscribe(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var baseline = await live.Reader.ReadAsync(timeout.Token);
        var before = game.BrowserUpdate(baseline);
        Assert.Contains(SecretCell, before.Snapshot.Presentation.UnknownCells);
        Assert.DoesNotContain(before.Snapshot.Result.State.Physical.Board.Terrain, t => t.Position == SecretCell);
        Assert.Contains(before.Snapshot.Result.State.Physical.Board.Terrain, t => t.TypeId == "wet-grass");
        var round = game.StartRound(baseline.Revision);
        var move = Assert.Single(round.Result.NextInput!.Candidates, c => c.Path is not null && c.Destination == new Cell(2, 1));
        var moved = game.Decide(round.Revision, move.Key);
        var discovered = game.BrowserResponse(moved);
        Assert.DoesNotContain(SecretCell, discovered.Presentation.UnknownCells);
        Assert.Equal("stepping-stones", discovered.Result.State.Physical.Board.TerrainDefinitionAt(SecretCell).Id);
        var streamed = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) streamed.Add(game.BrowserUpdate(update));
        Assert.NotEmpty(streamed);
        Assert.All(streamed, CheckObservation);
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(streamed.Select(u => JsonSerializer.Serialize(u, Json)), replayed.Select(u => JsonSerializer.Serialize(u, Json)));
        Assert.All(replayed, CheckObservation);
        // Reobserving a retained earlier frame after discovery must not backfill geometry.
        Assert.Contains(SecretCell, game.BrowserUpdate(baseline).Snapshot.Presentation.UnknownCells);
    }

    private static void CheckObservation(GameUpdate update)
    {
        void Check(GameState state)
        {
            var known = state.ExploredCells["blue"].Contains(SecretCell);
            Assert.Equal(known, state.Physical.Board.Terrain.Any(t => t.Position == SecretCell && t.TypeId == "stepping-stones"));
            if (known) Assert.Equal(new[] { "land", "water" }, state.Physical.Board.TerrainDefinitionAt(SecretCell).MovementPermissions);
        }
        Check(update.Snapshot.Result.State);
        foreach (var step in update.Snapshot.Result.ResolutionSteps) Check(step.StateAfter);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
