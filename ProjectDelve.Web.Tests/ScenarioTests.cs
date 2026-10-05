using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static string Serialize(GameState state) => JsonSerializer.Serialize(state, Json);
    private sealed class Random(bool shamanFirst = false) : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => shamanFirst && bag.Contains("shaman-type") ? "shaman-type" : bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    public static TheoryData<string, int, string[], string[]> Content => new()
    {
        { "basic-combat", 8, ["barbarian"], ["grunt"] },
        { "goblins", 10, ["barbarian", "rogue"], ["grunt", "goblin"] },
        { "archers", 12, ["barbarian", "rogue"], ["grunt", "goblin", "skeleton-archer"] },
        { "wizard-doors", 15, ["barbarian", "rogue", "wizard"], ["goblin", "skeleton-archer", "zombie", "ghost"] },
        { "full-party-trolls", 15, ["barbarian", "rogue", "wizard", "cleric"], ["goblin", "skeleton-archer", "zombie", "troll"] },
        { "shaman-hunt", 15, ["barbarian", "rogue", "wizard", "cleric"], ["shaman", "grunt", "red-dragon"] }
    };

    [Fact]
    public void CatalogHasSixStableUniqueIdsAndPresentation()
    {
        Assert.Equal(Content.Select(row => (string)row[0]), PlaytestScenarios.Catalog.Select(s => s.Id));
        Assert.Equal(6, PlaytestScenarios.Catalog.Select(s => s.Id).Distinct().Count());
        Assert.All(PlaytestScenarios.Catalog, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }

    [Theory, MemberData(nameof(Content))]
    public void ContentIsValidAndFresh(string id, int size, string[] heroes, string[] monsters)
    {
        var first = PlaytestScenarios.Create(id);
        var second = PlaytestScenarios.Create(id);
        Assert.Equal((size, size), (first.Physical.Board.Width, first.Physical.Board.Height));
        Assert.Equal(heroes.Select(h => h + "-type").Order(), first.Units.Where(u => u.SideId == "blue").Select(u => u.TypeId).Distinct().Order());
        Assert.Equal(monsters.Select(m => m + "-type").Order(), first.Units.Where(u => u.SideId == "red").Select(u => u.TypeId).Distinct().Order());
        Assert.Equal(heroes.Concat(monsters).Select(t => t + "-type").Order(), first.Types.Select(t => t.Id).Order());
        Assert.All(first.Physical.Figures, f => Assert.True(first.Physical.Board.TerrainAt(f.Position).Passable()));
        var expected = Serialize(second);
        // Starting a round runs the engine's existing complete scenario validation.
        Assert.NotNull(GameEngine.StartRound(first, new Random(), false).NextInput);
        first.Units.Clear(); first.Types.Clear(); first.Physical.Figures.Clear();
        first.Physical.Board.Terrain.Clear(); first.Physical.Board.Edges.Clear(); first.Bag.Add("leak");
        Assert.Equal(expected, Serialize(second));
        Assert.Equal(expected, Serialize(PlaytestScenarios.Create(id)));
    }

    [Fact]
    public void WizardRoomGhostPhasesThroughWallUsingGenericPresentationAndBehavior()
    {
        var state = PlaytestScenarios.Create("wizard-doors");
        var ghost = Assert.Single(state.Units, u => u.TypeId == UnitTypeIds.Ghost);
        var start = state.Physical.Figures.Single(f => f.Id == ghost.Id).Position;
        Assert.Equal(new Cell(8, 5), start);
        Assert.True(state.Physical.Board.TerrainAt(start).Passable());
        var card = BrowserProjection.Cards(state)[ghost.Id];
        Assert.Equal("Ghost", card.DisplayName);
        var phase = Assert.Single(card.Entries, e => e.Content.Id == "phase");
        Assert.Equal(state.Types.Single(t => t.Id == ghost.TypeId).CardEntries().Single(e => e.Id == "phase"), phase.Content);
        Assert.Equal("Capability", phase.Content.Category);
        Assert.Null(phase.Uses);

        // Draw Ghost before any other Unit can alter the setup.
        var random = new GhostFirstRandom();
        var pending = GameEngine.StartRound(state, random, false);
        Assert.Equal(ghost.Id, pending.NextInput!.UnitId);
        var moved = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random, false);
        var movement = Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(new Cell[] { new(8, 5), new(7, 5), new(6, 5) }, movement.Path);
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(movement.Path![0], movement.Path[1]));
        Assert.True(moved.State.Physical.Board.TerrainAt(movement.Path[^1]).Passable());
        Assert.Equal(state.Physical.Board.Edges, moved.State.Physical.Board.Edges);
        Assert.DoesNotContain(moved.Events, e => e.Kind is "DoorOpened" or "DoorOpeningAttemptResolved");
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.Contains("Ghost", PlaytestScenarios.Catalog.Single(s => s.Id == "wizard-doors").Description);

        var game = new PlaytestGame(random);
        var initial = game.StartScenario(0, "wizard-doors");
        var round = game.StartRound(initial.Revision);
        Assert.Contains(round.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == ghost.Id);
        Assert.NotEqual(UnitTypeIds.Ghost, round.Result.NextInput?.TypeId);
    }

    private sealed class GhostFirstRandom : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag.Contains(UnitTypeIds.Ghost) ? UnitTypeIds.Ghost : bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Ghost has not reached attack range yet.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Unexpected attack.");
        public int RollD6() => throw new InvalidOperationException("Phase does not open Doors.");
    }

    [Fact]
    public void RestartRestoresEveryMutableStateAndKeepsSessionPreference()
    {
        var game = new PlaytestGame(new Random());
        var initial = game.StartScenario(0, "full-party-trolls");
        var expected = Serialize(initial.Result.State);
        var changed = game.SetRelevanceAutoChoice(initial.Revision, false);
        var state = changed.Result.State;
        state.Units[0] = state.Units[0] with { CurrentHp = 1, CleaveUses = new(2, 0),
            BonusActionUses = state.Units[0].BonusActionUses.SetItem("Rage", new(2, 0)) };
        var clericIndex = state.Units.FindIndex(u => u.Id == "cleric");
        state.Units[clericIndex] = state.Units[clericIndex] with { HealUses = new(2, 0), HolyWaveUses = new(2, 0) };
        var wizardIndex = state.Units.FindIndex(u => u.Id == "wizard");
        state.Units[wizardIndex] = state.Units[wizardIndex] with { FireballUses = new(2, 0) };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(0, 0), Posture = Posture.Lying };
        for (var i = 0; i < state.Physical.Board.Edges.Count; i++)
            if (state.Physical.Board.Edges[i].Kind == EdgeKind.ClosedDoor)
                state.Physical.Board.Edges[i] = state.Physical.Board.Edges[i] with { Kind = EdgeKind.OpenDoor };
        state.Units.Add(UnitType.Goblin().CreateUnit("spawned", "red"));
        state.Physical.Figures.Add(new("spawned", new(0, 1), Posture.Lying));
        state.Round = 9; state.Bag.Add("goblin-type"); state.ActiveTypeId = "barbarian-type";
        state.CurrentUnitId = "barbarian"; state.MoveDone = true; state.ActionDone = true;
        state.CompletedUnitIds.Add("rogue"); state.CleavePending = true; state.MoveAfterAttackAllowance = 1;
        state.BonusActionsUsedThisActivation.Add("Rage"); state.ModifiersThisTurn.Add(new(Stat.Atk, 2));
        state.Pending = new(DecisionKind.Cleave, "barbarian-type", "barbarian", [], true);
        var restored = game.Restart(changed.Revision);
        Assert.Equal(expected, Serialize(restored.Result.State));
        Assert.Null(restored.Result.NextInput);
        Assert.Empty(restored.Result.Events); Assert.Empty(restored.Result.ResolutionSteps);
        Assert.False(restored.AutoChooseSingleRelevantChoice);
        Assert.Equal(changed.Revision + 1, restored.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchDiscardsActualActivationOrCleaveContinuation(bool cleave)
    {
        var game = new PlaytestGame(new Random());
        var active = game.StartRound(0);
        if (cleave)
        {
            active = game.Decide(active.Revision, "3,4");
            active = game.Decide(active.Revision, "attack:grunt-1");
            Assert.Equal(DecisionKind.Cleave, active.Result.NextInput!.Kind);
            Assert.True(active.Result.State.CleavePending);
        }
        Assert.NotNull(active.Result.State.CurrentUnitId);
        var replaced = game.StartScenario(active.Revision, "archers");
        Assert.Equal("archers", replaced.ScenarioId);
        Assert.Equal(Serialize(PlaytestScenarios.Create("archers")), Serialize(replaced.Result.State));
        Assert.Null(replaced.Result.NextInput);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Decide(active.Revision, "end-turn")).StatusCode);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Decide(replaced.Revision, "end-turn")).StatusCode);
        Assert.Equal("barbarian", game.StartRound(replaced.Revision).Result.NextInput!.UnitId);
    }

    [Fact]
    public void InvalidSelectionAndStaleRestartLeaveStateUntouched()
    {
        var game = new PlaytestGame(new Random());
        Assert.Equal(400, Assert.Throws<PlaytestRequestException>(() => game.StartScenario(0, "missing")).StatusCode);
        var active = game.StartRound(0);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Restart(0)).StatusCode);
        Assert.Equal(active.Revision, game.Snapshot().Revision);
        Assert.Equal(Serialize(active.Result.State), Serialize(game.Snapshot().Result.State));
    }

    [Fact]
    public void ShamanFleesAndSpawnsThroughNormalRulesAndNextRoundBagSnapshot()
    {
        var game = new PlaytestGame(new Random(shamanFirst: true));
        var initial = game.StartScenario(0, "shaman-hunt");
        Assert.DoesNotContain(initial.Result.State.Units, u => u.TypeId == "goblin-type");
        Assert.DoesNotContain(initial.Result.State.Types, t => t.Id == "goblin-type");
        var round = game.StartRound(initial.Revision);
        var move = Assert.Single(round.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == "shaman-1");
        Assert.NotEqual(move.Path![0], move.Path[^1]);
        var createdIndex = round.Result.Events.FindIndex(e => e.Kind == "UnitCreated");
        Assert.True(createdIndex >= 0);
        var afterSpawn = round.Result.ResolutionSteps.Single(s => s.EventIndex == createdIndex).StateAfter;
        var beforeSpawn = round.Result.ResolutionSteps.Last(s => s.EventIndex < createdIndex).StateAfter;
        Assert.Equal(beforeSpawn.Bag, afterSpawn.Bag);
        Assert.DoesNotContain("goblin-type", afterSpawn.Bag);
        Assert.Equal(UnitType.Goblin(), Assert.Single(afterSpawn.Types, t => t.Id == "goblin-type"));
        var goblin = Assert.Single(afterSpawn.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(Posture.Lying, afterSpawn.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        while (round.Result.NextInput is { } pending)
            round = game.Decide(round.Revision, pending.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key);
        Assert.True(round.Result.State.RoundComplete);
        var next = game.StartRound(round.Revision);
        Assert.Contains("goblin-type", next.Result.State.Bag);
        var restarted = game.Restart(next.Revision);
        Assert.DoesNotContain(restarted.Result.State.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(Serialize(PlaytestScenarios.Create("shaman-hunt")), Serialize(restarted.Result.State));
    }

    [Theory]
    [InlineData("wizard-doors")]
    [InlineData("full-party-trolls")]
    public void GatedRoomsNaturallyExerciseMonsterDoorAttempts(string id)
    {
        var game = new PlaytestGame(new Random());
        var initial = game.StartScenario(0, id);
        var result = game.StartRound(initial.Revision);
        var events = new List<RulesEvent>(result.Result.Events);
        while (result.Result.NextInput is { } pending)
        {
            result = game.Decide(result.Revision, pending.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key);
            events.AddRange(result.Result.Events);
        }
        Assert.Contains(events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == "zombie-1");
        if (id == "full-party-trolls")
            Assert.Contains(events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == "troll-1");
        var earlier = PlaytestScenarios.Create("wizard-doors").Physical.Board.Edges;
        var later = PlaytestScenarios.Create("full-party-trolls").Physical.Board.Edges;
        Assert.True(later.Count(e => e.Kind == EdgeKind.Wall) > earlier.Count(e => e.Kind == EdgeKind.Wall));
        Assert.True(later.Count(e => e.Kind == EdgeKind.ClosedDoor) > earlier.Count(e => e.Kind == EdgeKind.ClosedDoor));
    }

    [Fact]
    public async Task HttpExposesAuthoritativeCatalogAndReplacesBoardOnStartAndRestart()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], new Random());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new(address) };
        var initial = (await client.GetFromJsonAsync<GameResponse>("/api/game", Json))!;
        Assert.Equal("basic-combat", initial.ScenarioId);
        using var payload = JsonDocument.Parse(await client.GetStringAsync("/api/game"));
        Assert.Equal(JsonSerializer.Serialize(PlaytestScenarios.Catalog, Json), payload.RootElement.GetProperty("scenarios").GetRawText());
        using var started = await client.PostAsJsonAsync("/api/game/scenario", new { expectedRevision = 0, scenarioId = "wizard-doors" });
        started.EnsureSuccessStatusCode();
        var selected = (await started.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal("wizard-doors", selected.ScenarioId);
        Assert.Equal(15, selected.Result.State.Physical.Board.Width);
        using var round = await client.PostAsJsonAsync("/api/game/round", new { expectedRevision = selected.Revision });
        round.EnsureSuccessStatusCode();
        var active = (await round.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.NotNull(active.Result.NextInput);
        using var restart = await client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = active.Revision });
        restart.EnsureSuccessStatusCode();
        var fresh = (await restart.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal(Serialize(selected.Result.State), Serialize(fresh.Result.State));
        using var invalid = await client.PostAsJsonAsync("/api/game/scenario", new { expectedRevision = fresh.Revision, scenarioId = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var script = await client.GetStringAsync("/app.js");
        Assert.All(PlaytestScenarios.Catalog, scenario =>
        {
            Assert.DoesNotContain(scenario.Id, script); Assert.DoesNotContain(scenario.Name, script);
        });
        Assert.DoesNotContain("grunt", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shaman", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghost", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Board", script);
        var html = await client.GetStringAsync("/");
        Assert.Contains("id=\"scenario\"", html);
        Assert.Contains("id=\"start-scenario\"", html);
        Assert.Contains("id=\"restart-scenario\"", html);
    }
}
