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
using static ProjectDelve.Engine.Scenario;

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
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => shamanFirst ? bag.FirstOrDefault(t => t.TypeId == "shaman-type") ?? bag[0] : bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Fact]
    public void GhostFixturePhasesThroughWallUsingGenericPresentationAndBehavior()
    {
        var definition = Define(Map(6, 1, edges: [Edge(4, 0, EdgeDirection.Right, EdgeKind.Wall)]),
            [Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(0, 0)),
             Group(UnitTypeIds.Ghost, "red", ControllerKind.Automated, At(5, 0))]);
        var state = GameEngine.CreateGame(definition);
        var ghost = Assert.Single(state.Units, u => u.TypeId == UnitTypeIds.Ghost);
        var start = state.Physical.Figures.Single(f => f.Id == ghost.Id).Position;
        Assert.Equal(new Cell(5, 0), start);
        Assert.True(state.Physical.Board.TerrainAt(start).Passable());
        var card = BrowserProjection.Cards(state)[ghost.Id];
        Assert.Equal("Ghost", card.DisplayName);
        var phase = Assert.Single(card.Entries, e => e.Content.Id == "ability:phase");
        Assert.Equal(state.Types.Single(t => t.Id == ghost.TypeId).CardEntries().Single(e => e.Id == "ability:phase"), phase.Content);
        Assert.Equal("Passive", phase.Content.Category);
        Assert.Null(phase.Uses);

        // Draw Ghost before any other Unit can alter the setup.
        var random = new GhostFirstRandom();
        var pending = TestGame.StartRound(state, random, false);
        Assert.Equal(ghost.Id, pending.NextInput!.UnitId);
        var moved = TestGame.Advance(pending.State, new DefaultAutomatedProvider(), random, false);
        var movement = Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(new Cell[] { new(5, 0), new(4, 0), new(3, 0) }, movement.Path);
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(movement.Path![0], movement.Path[1]));
        Assert.True(moved.State.Physical.Board.TerrainAt(movement.Path[^1]).Passable());
        Assert.Equal(state.Physical.Board.Edges, moved.State.Physical.Board.Edges);
        Assert.DoesNotContain(moved.Events, e => e.Kind is "DoorOpened" or "DoorOpeningAttemptResolved");
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);

        var game = new PlaytestGame(random, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var initial = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));
        var round = game.StartRound(initial.Revision);
        Assert.Contains(round.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == ghost.Id);
        Assert.NotEqual(UnitTypeIds.Ghost, round.Result.NextInput?.TypeId);
    }

    private sealed class GhostFirstRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Ghost) ?? bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Ghost has not reached attack range yet.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Unexpected attack.");
        public int RollD6() => throw new InvalidOperationException("Phase does not open Doors.");
    }

    [Fact]
    public void RestartRestoresEveryMutableStateAndKeepsSessionPreference()
    {
        var game = new PlaytestGame(new Random(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var initial = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(CourtyardFixture.Definition()));
        var expected = Serialize(initial.Result.State);
        var changed = game.SetRelevanceAutoChoice(initial.Revision, false);
        var state = changed.Result.State;
        state.Units[0] = state.Units[0] with { CurrentHp = 1,
            AbilityUseCounts = state.Units[0].AbilityUseCounts.SetItem("unit:cleave", 2).SetItem("unit:rage", 2) };
        var clericIndex = state.Units.FindIndex(u => u.TypeId == UnitTypeIds.Cleric);
        state.Units[clericIndex] = state.Units[clericIndex] with
        { AbilityUseCounts = state.Units[clericIndex].AbilityUseCounts.SetItem("unit:heal", 2).SetItem("unit:holy-wave", 2) };
        var wizardIndex = state.Units.FindIndex(u => u.TypeId == UnitTypeIds.Wizard);
        state.Units[wizardIndex] = state.Units[wizardIndex] with
        { AbilityUseCounts = state.Units[wizardIndex].AbilityUseCounts.SetItem("unit:fireball", 2) };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(0, 0), Posture = Posture.Lying };
        for (var i = 0; i < state.Physical.Board.Edges.Count; i++)
            if (state.Physical.Board.Edges[i].Kind == EdgeKind.ClosedDoor)
                state.Physical.Board.Edges[i] = state.Physical.Board.Edges[i] with { Kind = EdgeKind.OpenDoor };
        state.Units.Add(UnitType.Goblin().CreateUnit("spawned", "red"));
        state.Physical.Figures.Add(new("spawned", new(0, 1), Posture.Lying));
        state.Round = 9; state.Bag.Add(new("goblin-type", "red")); state.ActiveToken = new("barbarian-type", state.Units.First(u => u.TypeId == "barbarian-type").SideId);
        var barbarianId = state.Units.Single(u => u.TypeId == UnitTypeIds.Barbarian).Id;
        state.CurrentUnitId = barbarianId; state.MoveDone = true; state.ActionDone = true;
        state.CompletedUnitIds.Add(state.Units.Single(u => u.TypeId == UnitTypeIds.Rogue).Id); state.CleavePending = true; state.MoveAfterAttackAllowance = 1;
        state.BonusActionUseCountsThisActivation["unit:rage"] = 1; state.ModifiersThisTurn.Add(new(Stat.Atk, 2));
        state.Pending = new(DecisionKind.Cleave, UnitTypeIds.Barbarian, barbarianId, [], true);
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
        var game = new PlaytestGame(new Random(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var definition = Define(Map(5, 4),
            [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 1)),
             Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(3, 1), At(3, 2))]);
        var setup = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));
        var active = game.StartRound(setup.Revision);
        if (cleave)
        {
            active = game.Decide(active.Revision, "2,1");
            var target = active.Result.State.Units.Single(u => u.TypeId == UnitTypeIds.Grunt &&
                active.Result.State.Physical.Figures.Single(f => f.Id == u.Id).Position == new Cell(3, 1));
            active = game.Decide(active.Revision, active.Result.NextInput!.Candidates.Single(c =>
                c.Action == UnitAction.NormalAttack && c.TargetId == target.Id).Key);
            while (active.Result.NextInput?.Kind == DecisionKind.RollDice) active = game.Decide(active.Revision, "roll-dice");
            Assert.Equal(DecisionKind.Cleave, active.Result.NextInput!.Kind);
            Assert.True(active.Result.State.CleavePending);
        }
        Assert.NotNull(active.Result.State.CurrentUnitId);
        var replaced = game.StartTransportScenario(active.Revision, ScenarioDefinitionTransport.Encode(WebScenarioFixtures.Serialization()));
        Assert.Equal("imported", replaced.ScenarioId);
        Assert.Equal(Serialize(GameEngine.CreateGame(WebScenarioFixtures.Serialization())), Serialize(replaced.Result.State));
        Assert.Null(replaced.Result.NextInput);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Decide(active.Revision, "end-turn")).StatusCode);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Decide(replaced.Revision, "end-turn")).StatusCode);
        Assert.Equal(1, game.StartRound(replaced.Revision).Result.State.Round);
    }

    [Fact]
    public void InvalidSelectionAndStaleRestartLeaveStateUntouched()
    {
        var game = new PlaytestGame(new Random(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        Assert.Equal(400, Assert.Throws<PlaytestRequestException>(() => game.StartScenario(0, "missing")).StatusCode);
        var active = game.StartRound(0);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Restart(0)).StatusCode);
        Assert.Equal(active.Revision, game.Snapshot().Revision);
        Assert.Equal(Serialize(active.Result.State), Serialize(game.Snapshot().Result.State));
    }

    [Fact]
    public void ShamanFleesAndSpawnsThroughNormalRulesAndNextRoundBagSnapshot()
    {
        var game = new PlaytestGame(new Random(shamanFirst: true), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var definition = Define(Map(10, 8),
            [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(0, 4)),
             Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, At(5, 4)),
             Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated)]);
        var initial = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));
        Assert.DoesNotContain(initial.Result.State.Units, u => u.TypeId == "goblin-type");
        Assert.Contains(initial.Result.State.Types, t => t.Id == UnitTypeIds.Goblin);
        Assert.Contains(initial.Result.State.Controllers, c => c.Token == new ActivationToken(UnitTypeIds.Goblin, "red"));
        Assert.DoesNotContain(initial.Result.State.Bag, token => token.TypeId == UnitTypeIds.Goblin);
        var shamanId = Assert.Single(initial.Result.State.Units, u => u.TypeId == UnitTypeIds.Shaman).Id;
        var round = game.StartRound(initial.Revision);
        var move = Assert.Single(round.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == shamanId);
        Assert.NotEqual(move.Path![0], move.Path[^1]);
        var createdIndex = round.Result.Events.FindIndex(e => e.Kind == "UnitCreated");
        Assert.True(createdIndex >= 0);
        var afterSpawn = round.Result.ResolutionSteps.Single(s => s.EventIndex == createdIndex).StateAfter;
        var beforeSpawn = round.Result.ResolutionSteps.Last(s => s.EventIndex < createdIndex).StateAfter;
        Assert.Equal(beforeSpawn.Bag, afterSpawn.Bag);
        Assert.DoesNotContain("goblin-type", afterSpawn.Bag.Select(t => t.TypeId));
        Assert.Equal(JsonSerializer.Serialize(UnitType.Goblin()),
            JsonSerializer.Serialize(Assert.Single(afterSpawn.Types, t => t.Id == "goblin-type")));
        var goblin = Assert.Single(afterSpawn.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(Posture.Lying, afterSpawn.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        while (round.Result.NextInput is { } pending)
            round = game.Decide(round.Revision, pending.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn or ActivationChoiceKind.RollDice).Key);
        Assert.True(round.Result.State.RoundComplete);
        var next = game.StartRound(round.Revision);
        Assert.Contains("goblin-type", next.Result.State.Bag.Select(t => t.TypeId));
        var restarted = game.Restart(next.Revision);
        Assert.DoesNotContain(restarted.Result.State.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(Serialize(GameEngine.CreateGame(definition)), Serialize(restarted.Result.State));
    }

    [Theory]
    [InlineData(UnitTypeIds.Zombie)]
    [InlineData(UnitTypeIds.Troll)]
    public void GatedFixtureExercisesMonsterDoorAttempts(string typeId)
    {
        var definition = Define(Map(7, 3, edges: [.. Vertical(2, 0, 2, EdgeKind.Wall),
            Edge(2, 1, EdgeDirection.Right, EdgeKind.ClosedDoor)]),
            [Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(0, 1)),
             Group(typeId, "red", ControllerKind.Automated, At(4, 1))]);
        using var game = new PlaytestGame(new Random(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var initial = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));
        var monster = Assert.Single(initial.Result.State.Units, unit => unit.TypeId == typeId);
        var result = game.StartRound(initial.Revision);
        var events = new List<RulesEvent>(result.Result.Events);
        while (result.Result.NextInput is { } pending)
        {
            result = game.Decide(result.Revision, pending.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn or ActivationChoiceKind.RollDice).Key);
            events.AddRange(result.Result.Events);
        }
        Assert.Contains(events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == monster.Id);
    }

    [Fact]
    public async Task HttpImportsFixtureAndRestartsItAndServesRuleIndependentClient()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], new Random(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new(address) };
        var initial = (await client.GetFromJsonAsync<GameResponse>("/api/game", Json))!;
        var definition = WebScenarioFixtures.Combat();
        using var started = await client.PostAsJsonAsync("/api/game/scenario/import",
            new { expectedRevision = initial.Revision, transport = ScenarioDefinitionTransport.Encode(definition) });
        started.EnsureSuccessStatusCode();
        var selected = (await started.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.Equal("imported", selected.ScenarioId);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(
            GameEngine.CreateGame(definition), selected.Result.State);
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
        Assert.DoesNotContain("grunt", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shaman", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghost", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Board", script);
        var html = await client.GetStringAsync("/");
        Assert.Contains("id=\"scenario\"", html);
        Assert.Contains("id=\"start-scenario\"", html);
        Assert.Contains("id=\"restart-scenario\"", html);
        Assert.Contains("id=\"scenario-transport\"", html);
        Assert.Contains("id=\"import-scenario\"", html);
        Assert.Contains("replaceScenario(\"scenario/import\", { transport: ui[\"scenario-transport\"].value.trim() })", script);
    }
}
