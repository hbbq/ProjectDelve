using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GenerationIntegrationTests
{
    private const string Code = "DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF";
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.OrderBy(t => t.SideId == HeroesVsMonsters.Monsters ? 0 : 1).ThenBy(t => t.TypeId).First();
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }
    [Theory]
    [InlineData(Code)]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QFun-S000000000000002A")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000002")]
    public async Task PreviewAndDirectPlayShareExactDefinitionAndRestartKeepsCompletedRecipe(string recipe)
    {
        await using var host = await Host.Start();
        using var previewResponse = await host.Client.PostAsJsonAsync("/api/generation", new { code = recipe });
        previewResponse.EnsureSuccessStatusCode();
        Assert.Equal("no-store", previewResponse.Headers.CacheControl!.ToString());
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var definition = ScenarioDefinitionJson.FromJson(preview.GetProperty("definition").GetRawText());
        var expected = GameEngine.CreateGame(definition);
        Assert.Equal(JsonSerializer.Serialize(expected.Physical.Board.TerrainTypes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }),
            preview.GetProperty("terrainDefinitions").GetRawText());
        using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, generationCode = recipe });
        played.EnsureSuccessStatusCode(); var observed = await played.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(recipe, observed.GetProperty("generationCode").GetString());
        var actual = host.Game.Snapshot();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual.Result.State));
        var fog = GenerationCode.Parse(recipe).FogOfWar;
        Assert.Equal(fog ? HeroesVsMonsters.Heroes : null, observed.GetProperty("presentation").GetProperty("viewingSideId").GetString());
        var shown = observed.GetProperty("result").GetProperty("state");
        if (fog) Assert.True(shown.GetProperty("units").GetArrayLength() < expected.Units.Count);
        else Assert.Equal(expected.Units.Count, shown.GetProperty("units").GetArrayLength());
        var known = FogOfWar.Observe(expected, HeroesVsMonsters.Heroes);
        Assert.Equal(JsonSerializer.Serialize(known.Physical.Board), JsonSerializer.Serialize(host.Game.BrowserResponse(actual).Result.State.Physical.Board));
        using var restarted = await host.Client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = actual.Revision });
        restarted.EnsureSuccessStatusCode();
        Assert.Equal(recipe, (await restarted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generationCode").GetString());
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(host.Game.Snapshot().Result.State));
        Assert.Equal(preview.GetProperty("transport").GetString(), ScenarioDefinitionTransport.Encode(definition));
        using var exported = await host.Client.PostAsJsonAsync("/api/designer/export", preview.GetProperty("definition"));
        exported.EnsureSuccessStatusCode();
        Assert.Equal(preview.GetProperty("transport").GetString(), (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString());
        foreach (var metadata in preview.GetProperty("unitMetadata").EnumerateArray())
        {
            var type = UnitContent.Find(metadata.GetProperty("unitTypeId").GetString()!)!;
            Assert.Equal(FootprintGeometry.OccupiedCells(type.Footprint, new(0, 0)).Count, metadata.GetProperty("footprintOffsets").GetArrayLength());
        }
    }
    [Fact]
    public async Task PrefixPlayPublishesOneReusableCodeAndRejectsStaleReplacement()
    {
        await using var host = await Host.Start();
        using var response = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, generationCode = Code[..Code.LastIndexOf("-S", StringComparison.Ordinal)] });
        response.EnsureSuccessStatusCode();
        var completed = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generationCode").GetString()!;
        Assert.NotNull(GenerationCode.Parse(completed).Seed);
        var expected = GameEngine.CreateGame(DungeonGenerator.Generate(completed).Definition);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(host.Game.Snapshot().Result.State));
        using var stale = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, generationCode = Code });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(completed, host.Game.Snapshot().GenerationCode);
    }
    [Theory]
    [InlineData("DNG2-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1")]
    [InlineData("DNG1-HVM-14x14-R24-E2-C2-D2-N2-L3-OR2-F1")]
    public async Task InvalidRequestsRetainTheCurrentGame(string code)
    {
        await using var host = await Host.Start();
        using var preview = await host.Client.PostAsJsonAsync("/api/generation", new { code });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        using var play = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, generationCode = code });
        Assert.Equal(HttpStatusCode.BadRequest, play.StatusCode);
        Assert.Equal(0, host.Game.Snapshot().Revision);
    }
    [Fact]
    public void GeneratedHiddenMonstersRunUnderNormalAgencyAndRevealNoHiddenEventHistory()
    {
        var dungeon = DungeonGenerator.Generate(Code);
        using var game = new PlaytestGame(new Dice());
        var setup = game.SetupImport(0, null, null, generationCode: Code);
        var hidden = setup.Result.State.Units.Where(u => u.SideId == HeroesVsMonsters.Monsters &&
            !FogOfWar.IsUnitObservable(setup.Result.State, HeroesVsMonsters.Heroes, u.Id)).Select(u => u.Id).ToHashSet();
        Assert.NotEmpty(hidden);
        var round = game.StartRound(setup.Revision);
        Assert.Contains(round.Result.Events, e => e.Kind == "ActivationCompleted" && hidden.Contains(e.UnitId ?? ""));
        var browser = game.BrowserResponse(round);
        Assert.Equal(Code, browser.GenerationCode);
        var hiddenCompletions = round.Result.Events.Select((e, i) => (Event: e, Index: i))
            .Where(p => p.Event.Kind == "ActivationCompleted" && p.Event.UnitId is { } id && hidden.Contains(id) &&
                !FogOfWar.IsUnitObservable(round.Result.ResolutionSteps.Single(s => s.EventIndex == p.Index).StateAfter, HeroesVsMonsters.Heroes, id))
            .Select(p => p.Event.UnitId!).ToHashSet();
        Assert.NotEmpty(hiddenCompletions);
        Assert.DoesNotContain(browser.Result.Events, e => e.Kind == "ActivationCompleted" && hiddenCompletions.Contains(e.UnitId ?? ""));
    }
    [Fact]
    public void MissionAreaIsStructuredThroughUnknownCellsAndEachObservedPlaybackSnapshot()
    {
        var state = GameEngine.CreateGame(DungeonGenerator.Generate(Code).Definition);
        var objective = state.Objectives.Single(o => o.SideId == HeroesVsMonsters.Heroes);
        var area = objective.Area!;
        Assert.False(FogOfWar.IsExplored(state, HeroesVsMonsters.Heroes, new(area.X, area.Y)));
        var exploration = state.ExploredCells[HeroesVsMonsters.Heroes].ToHashSet();
        var after = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var changedArea = new ObjectiveArea(1, 1, 2, 2);
        after.Objectives[0] = objective with { Area = changedArea };
        var occurrence = new RulesEvent("RoundStarted");
        var result = new EngineResult(after, [occurrence], null) { ResolutionSteps = [new(0, state)] };
        var view = BrowserProjection.Create(result);
        Assert.Equal(changedArea, view.Objectives.Single(o => o.SideId == HeroesVsMonsters.Heroes).Area);
        Assert.Equal(area, Assert.Single(view.ResolutionSteps).Objectives.Single(o => o.SideId == HeroesVsMonsters.Heroes).Area);
        Assert.Equal(exploration.OrderBy(c => c.Y).ThenBy(c => c.X), state.ExploredCells[HeroesVsMonsters.Heroes].OrderBy(c => c.Y).ThenBy(c => c.X));
    }
    [Fact]
    public async Task SolidRockIsValidatedSerializedAndExposedToDesigner()
    {
        await using var host = await Host.Start();
        var catalog = await host.Client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        Assert.Contains("SolidRock", catalog.GetProperty("terrains").Deserialize<string[]>()!);
        var generated = DungeonGenerator.Generate(Code).Definition;
        using var exported = await host.Client.PostAsJsonAsync("/api/designer/export", JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(generated)));
        exported.EnsureSuccessStatusCode();
        var encoded = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(TerrainKind.SolidRock, ScenarioDefinitionTransport.Decode(encoded).Board.DefaultTerrain);
    }
    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public PlaytestGame Game => app.Services.GetRequiredService<PlaytestGame>();
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], new Dice());
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
