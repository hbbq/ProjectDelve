using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class EnvironmentalProductionLifecycleTests
{
    private const string ProducerId = "fixture-mercury-bush";
    private const string HiddenId = "fixture-silent-seam";
    private static readonly Cell SourceCell = new(2, 2);
    private static readonly Cell HiddenCell = new(9, 2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Dedicated fixtures: mutable authored library scenarios are only loading examples.
    private static ScenarioDefinition Fixture(bool fog = false) => new()
    {
        TerrainTypes =
        [
            new(ProducerId, "Mercury Bush", false, true)
            {
                Presentation = new("unpublished-production-recipe"),
                Production = new DirectionalResourceProduction("fruit", 2,
                    SourceLos: ProductionSourceLos.IgnoreProducingCell)
            },
            new(HiddenId, "Silent Seam", false, false)
            {
                Presentation = new("SolidRock"),
                Production = new DirectionalResourceProduction("diamond", 1)
            }
        ],
        Board = new()
        {
            Width = 12, Height = 4,
            Cells =
            [
                new(SourceCell, TerrainKind.StoneFloor) { TerrainTypeId = ProducerId },
                new(new(4, 2), TerrainKind.StoneFloor) { TerrainTypeId = "ordinary-lava" },
                new(new(6, 2), TerrainKind.StoneFloor) { TerrainTypeId = "coal-lava" },
                new(HiddenCell, TerrainKind.StoneFloor) { TerrainTypeId = HiddenId }
            ]
        },
        UnitTypeIds = ["production-observer"],
        UnitTypes = [new("production-observer", 2, 1, 1, 1, 1)],
        Units = [new("production-observer", "blue", new(0, 0), UnitId: "observer")],
        Agency = [new("production-observer", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        FogOfWarSides = fog ? ["blue"] : null,
        Loot = LootSettings.Empty
    };

    [Fact]
    public async Task DesignerDelve1EditExportAndPlayPreserveProductionDeclarations()
    {
        await using var host = await Host.Start();
        var authored = Fixture();
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import",
            new { transport = ScenarioDefinitionTransport.Encode(authored) });
        imported.EnsureSuccessStatusCode();
        var draft = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
        Assert.Equal(ScenarioDefinitionJson.ToJson(authored), ScenarioDefinitionJson.ToJson(draft));
        draft.Board.Cells.Add(new(new(3, 3), TerrainKind.StoneFloor) { TerrainTypeId = ProducerId });
        using var validated = await host.PostDraft("validate", draft);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        using var exported = await host.PostDraft("export", draft);
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.StartsWith("DELVE1:", transport);
        Assert.Equal(ScenarioDefinitionJson.ToJson(draft), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));

        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport });
        played.EnsureSuccessStatusCode();
        var response = (await played.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        AssertProduction(response.Result.State);
        Assert.Equal(ProducerId, response.Result.State.Physical.Board.TerrainDefinitionAt(new(3, 3)).Id);
        Assert.Empty(response.Result.State.Resources);
    }

    [Fact]
    public void ProductionSurvivesDetachedSaveResumeAndRestartWithoutRerunningOnLoad()
    {
        var fixture = Fixture();
        var dice = new Dice();
        using var game = new PlaytestGame(dice, GameEngine.CreateGame(fixture));
        var imported = game.StartTransportScenario(game.Snapshot().Revision, ScenarioDefinitionTransport.Encode(fixture));
        var round = game.StartRound(imported.Revision);
        Assert.Equal(5, dice.Calls);
        Assert.Equal(3, round.Result.State.Resources.Count);
        Assert.Equal(3, round.Result.Events.Count(e => e.Kind == "TerrainProductionRolled"));
        Assert.Equal(2, round.Result.Events.Count(e => e.Kind == "LavaCoalRolled"));
        AssertProduction(round.Result.State);

        var saved = PlaytestGame.Detach(round);
        var resumeDice = new Dice();
        using var resumed = new PlaytestGame(resumeDice, saved.Result.State);
        var resumedSnapshot = resumed.Snapshot();
        Assert.Equal(0, resumeDice.Calls);
        Assert.Equal(round.Result.State.Resources, resumedSnapshot.Result.State.Resources);
        Assert.Equal(round.Result.State.Round, resumedSnapshot.Result.State.Round);
        AssertProduction(resumedSnapshot.Result.State);
        var betweenRounds = FinishRound(resumed, resumedSnapshot);
        var nextRound = resumed.StartRound(betweenRounds.Revision);
        Assert.Equal(5, resumeDice.Calls);
        Assert.Equal(2, nextRound.Result.State.Round);
        Assert.Equal(3, nextRound.Result.State.Resources.Count);

        var restarted = game.Restart(round.Revision);
        Assert.Empty(restarted.Result.State.Resources);
        Assert.Equal(0, restarted.Result.State.Round);
        Assert.Equal(imported.VisualSeed, restarted.VisualSeed);
        Assert.Equal(5, dice.Calls);
        AssertProduction(restarted.Result.State);
    }

    [Fact]
    public async Task ProductionStreamAndReplayKeepHiddenSourcesDefinitionsAndResourcesPrivate()
    {
        var dice = new Dice();
        using var game = new PlaytestGame(dice, GameEngine.CreateGame(Fixture(fog: true)));
        using var live = game.Subscribe(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var baseline = await live.Reader.ReadAsync(timeout.Token);
        AssertObservation(game.BrowserUpdate(baseline));
        var round = game.StartRound(baseline.Revision);
        Assert.Equal(5, dice.Calls);
        Assert.Contains(round.Result.Events, e => e.Kind == "TerrainProductionRolled" && e.Cell == HiddenCell);
        var visible = game.BrowserResponse(round);
        Assert.Equal(2, visible.Result.Events.Count(e => e.Kind == "TerrainProductionRolled"));
        Assert.DoesNotContain(visible.Result.Events, e => e.Cell == HiddenCell || e.Kind == "LavaCoalRolled");
        Assert.Equal(new Cell(2, 1), Assert.Single(visible.Result.State.Resources).Position);

        var streamed = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) streamed.Add(game.BrowserUpdate(update));
        Assert.Contains(streamed, u => u.Kind == "event" && u.Snapshot.Result.Events.Any(e => e.Kind == "TerrainProductionRolled"));
        Assert.All(streamed, AssertObservation);
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(streamed.Select(u => JsonSerializer.Serialize(u, Json)), replayed.Select(u => JsonSerializer.Serialize(u, Json)));
        Assert.All(replayed, AssertObservation);
    }

    private static GameResponse FinishRound(PlaytestGame game, GameResponse response)
    {
        for (var step = 0; response.Result.NextInput is { } pending && step < 8; step++)
        {
            var candidate = pending.Candidates.SingleOrDefault(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn);
            Assert.True(candidate is not null || pending.AllowsNone);
            response = game.Decide(response.Revision, candidate?.Key);
        }
        Assert.Null(response.Result.NextInput);
        return response;
    }

    private static void AssertObservation(GameUpdate update)
    {
        var json = JsonSerializer.Serialize(update, Json);
        Assert.DoesNotContain(HiddenId, json);
        Assert.DoesNotContain("Silent Seam", json);
        Assert.DoesNotContain(update.Snapshot.Result.Events, e => e.Cell == HiddenCell);
        void Check(GameState state)
        {
            Assert.DoesNotContain(state.Physical.Board.Terrain, t => t.Position == HiddenCell);
            Assert.DoesNotContain(state.Resources, r => r.Position == new Cell(9, 1));
            Assert.Equal("fruit", Assert.IsType<DirectionalResourceProduction>(state.Physical.Board.TerrainDefinitionAt(SourceCell).Production).ResourceTypeId);
        }
        Check(update.Snapshot.Result.State);
        foreach (var step in update.Snapshot.Result.ResolutionSteps) Check(step.StateAfter);
    }

    private static void AssertProduction(GameState state)
    {
        var board = state.Physical.Board;
        Assert.Null(board.TerrainDefinitionAt(new(4, 2)).Production);
        Assert.Equal(new DirectionalResourceProduction("coal", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell),
            board.TerrainDefinitionAt(new(6, 2)).Production);
        var local = board.TerrainDefinitionAt(SourceCell);
        Assert.Equal(ProducerId, local.Id);
        Assert.Equal(new DirectionalResourceProduction("fruit", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell), local.Production);
        Assert.Contains("Fruit", local.ProductionDescription);
        Assert.Equal("unpublished-production-recipe", local.Presentation.Recipe);
        Assert.Equal(new DirectionalResourceProduction("diamond", 1), board.TerrainDefinitionAt(HiddenCell).Production);
    }

    private sealed class Dice : IRandomProvider
    {
        public int Calls { get; private set; }
        public int RollD8() { Calls++; return 1; }
        public int RollD6() => throw new InvalidOperationException("This fixture needs only environmental d8 rolls.");
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root],
                random: new Dice(), initialState: GameEngine.CreateGame(Fixture()));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }
        public Task<HttpResponseMessage> PostDraft(string operation, ScenarioDefinition definition) => client.PostAsync($"/api/designer/{operation}",
            new StringContent(ScenarioDefinitionJson.ToJson(definition), Encoding.UTF8, "application/json"));
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
