using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class BoardAppearanceTests
{
    [Fact]
    public void SeedUsesCanonicalInitialBoardAndScenarioIdentity()
    {
        Assert.Equal("board-v1:64a17d6b3811425f36bde4feca3be92f9e7773839bb1aee7a632560aecf30a47",
            BoardAppearance.DeriveSeed("fixture", new Board(2, 1, [])));
        var a = new Board(3, 2, [new(new(0, 0), new(1, 0), EdgeKind.Wall)])
        { Terrain = [new(new(1, 0), TerrainKind.Grass), new(new(2, 1), TerrainKind.Rail, RailDirection.West)] };
        var b = a with
        {
            Edges = [new(new(1, 0), new(0, 0), EdgeKind.Wall), new(new(0, 1), new(1, 1), EdgeKind.None)],
            Terrain = [a.Terrain[1], new(new(0, 0), TerrainKind.StoneFloor), a.Terrain[0]]
        };
        Assert.Equal(BoardAppearance.DeriveSeed("fixture", a), BoardAppearance.DeriveSeed("fixture", b));
        Assert.NotEqual(BoardAppearance.DeriveSeed("fixture", a), BoardAppearance.DeriveSeed("other", a));
        Assert.NotEqual(BoardAppearance.DeriveSeed("fixture", a), BoardAppearance.DeriveSeed("fixture", a with { Width = 4 }));
        Assert.Matches("^board-v1:[0-9a-f]{64}$", BoardAppearance.DeriveSeed("fixture", a));
    }

    [Fact]
    public async Task ImportedSeedSurvivesProgressionSnapshotsRestartAndNewHost()
    {
        var fixture = WebScenarioFixtures.Combat();
        var transport = ScenarioDefinitionTransport.Encode(fixture);
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(fixture));
        var imported = game.StartTransportScenario(game.Snapshot().Revision, transport);
        var seed = imported.VisualSeed;
        Assert.NotNull(seed);
        using var updates = game.Subscribe(null);
        var round = game.StartRound(imported.Revision);
        Assert.Equal(seed, round.VisualSeed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        GameUpdate update;
        do
        {
            update = await updates.Reader.ReadAsync(timeout.Token);
            Assert.Equal(seed, game.BrowserUpdate(update).Snapshot.VisualSeed);
        } while (update.Kind != "event");
        Assert.Equal(seed, game.BrowserResponse(game.Snapshot()).VisualSeed);
        Assert.Equal(seed, PlaytestGame.Detach(round).VisualSeed);
        var restarted = game.Restart(round.Revision);
        Assert.NotEqual(imported.SessionId, restarted.SessionId);
        Assert.Equal(seed, restarted.VisualSeed);
        using var reloaded = new PlaytestGame(new Dice(), GameEngine.CreateGame(fixture));
        Assert.Equal(seed, reloaded.StartTransportScenario(reloaded.Snapshot().Revision, transport).VisualSeed);
        var different = fixture with { Board = fixture.Board with { Width = fixture.Board.Width + 1 } };
        Assert.NotEqual(seed, game.StartTransportScenario(restarted.Revision, ScenarioDefinitionTransport.Encode(different)).VisualSeed);
    }

    [Fact]
    public void IndividualNamesRemainOptionalPresentationMetadataIncludingPlayback()
    {
        var type = new UnitType("custom-type", 1, 1, 1, 1, 3) { DisplayName = "Custom Type" };
        var state = new GameState
        {
            Physical = new(new Board(2, 2, []), [new("generated-17", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("generated-17", "amber")]
        };
        var result = new EngineResult(state, [], null) { ResolutionSteps = [new(0, state)] };
        Assert.Null(BrowserProjection.Create(result).Cards["generated-17"].IndividualName);
        var presentation = BrowserProjection.Create(result, new Dictionary<string, string> { ["generated-17"] = "Ada" });
        Assert.Equal("Custom Type", presentation.Cards["generated-17"].DisplayName);
        Assert.Equal("Ada", presentation.Cards["generated-17"].IndividualName);
        Assert.Equal("Ada", presentation.ResolutionSteps.Single().Cards["generated-17"].IndividualName);
        Assert.DoesNotContain("Ada", JsonSerializer.Serialize(state));
        Assert.Contains("Ada", JsonSerializer.Serialize(presentation));
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
