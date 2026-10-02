using System.Net;
using System.Net.Http.Json;
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

public sealed class PlaytestApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ReadIsSideEffectFree_AndHostServesBrowserAssets()
    {
        await using var host = await Host.Start();
        var first = await host.Read();
        var second = await host.Read();
        Assert.Equal(0, first.Revision);
        Assert.Equal(0, second.Result.State.Round);
        Assert.Empty(second.Result.Events);
        Assert.Null(second.Result.NextInput);
        Assert.Equal(15, second.Result.State.Physical.Board.Width);
        Assert.Equal(15, second.Result.State.Physical.Board.Height);
        Assert.Equal(9, second.Result.State.Physical.Figures.Count);
        var heroes = second.Result.State.Units.Where(u => u.SideId == "blue").ToArray();
        Assert.Equal(new[] { "barbarian", "rogue" }, heroes.Select(u => u.Id));
        Assert.Equal(2, heroes.Select(u => u.TypeId).Distinct().Count());
        Assert.All(second.Result.State.Types.Where(t => heroes.Any(u => u.TypeId == t.Id)),
            type => Assert.Equal(UnitAction.NormalAttack, type.Actions));
        Assert.Equal(new[] { 2, 2, 2, 1 }, second.Result.State.Units.Where(u => u.SideId == "red")
            .GroupBy(u => u.TypeId).Select(group => group.Count()));
        Assert.Equal(1, second.Result.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.ClosedDoor));
        Assert.Contains(second.Result.State.Physical.Board.Edges, e => e.Kind == EdgeKind.OpenDoor);
        var board = second.Result.State.Physical.Board;
        Assert.Contains(board.Edges, e => e.Kind == EdgeKind.WallWithWindow);
        Assert.Equal(Enum.GetValues<TerrainKind>().OrderBy(kind => kind),
            Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width)
                .Select(x => board.TerrainAt(new Cell(x, y)))).Distinct().OrderBy(kind => kind));
        Assert.All(second.Result.State.Physical.Figures, figure => Assert.True(board.TerrainAt(figure.Position).Passable()));
        Assert.Contains("visual playtest", await host.Client.GetStringAsync("/"));
        Assert.Contains("MovementCompleted", await host.Client.GetStringAsync("/app.js"));
    }

    [Theory]
    [InlineData("barbarian-type", 3, 1, 4, 3, 5)]
    [InlineData("rogue-type", 4, 1, 3, 2, 4)]
    [InlineData("grunt-type", 3, 1, 3, 3, 1)]
    public async Task BasicContentHasSpecifiedStatsAndExplicitActions(string id, int mov, int rng, int atk, int def, int hp)
    {
        await using var host = await Host.Start();
        var type = (await host.Read()).Result.State.Types.Single(t => t.Id == id);
        Assert.Equal((mov, rng, atk, def, hp), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(id == "grunt-type" ? UnitFreeAction.None : UnitFreeAction.OpenDoor, type.FreeActions);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Null(type.TryOpenDoor);
        Assert.Null(type.MoveAfterAttack);
    }

    [Fact]
    public async Task HeroDoorChoiceIsSuppliedByEngine_AndOpeningBeforeMoveKeepsMoveAndAttackAvailable()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "4,4");
        var afterMove = Assert.Single(moved.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        Assert.Equal(ActivationChoiceKind.FreeAction, afterMove.Kind);
        Assert.Null(afterMove.Action);
        Assert.Equal("open-door:3,4:4,4", afterMove.Key);
        // Decline it for this activation so the next activation can open before Move.
        var completed = await FinishRound(host, await host.Decide(moved.Revision, "end-turn"));
        var next = await host.Round(completed.Revision);
        var beforeMove = Assert.Single(next.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        var opened = await host.Decide(next.Revision, beforeMove.Key);
        Assert.Equal(next.Revision + 1, opened.Revision);
        Assert.Equal("DoorOpened", Assert.Single(opened.Result.Events).Kind);
        Assert.Equal("barbarian", opened.Result.NextInput!.UnitId);
        Assert.False(opened.Result.State.MoveDone);
        Assert.False(opened.Result.State.ActionDone);
        Assert.False(opened.Result.State.BonusActionUsed);
        Assert.Contains(opened.Result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Stay);
        Assert.DoesNotContain(opened.Result.NextInput.Candidates, c => c.Key == beforeMove.Key);
        Assert.Equal(EdgeKind.OpenDoor, opened.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        var stayed = await host.Decide(opened.Revision, "stay");
        var attack = Assert.Single(stayed.Result.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var attacked = await host.Decide(stayed.Revision, attack.Key);
        Assert.Contains(attacked.Result.Events, e => e.Kind == "AttackResolved" && e.UnitId == "barbarian");
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("candidate.freeAction === \"OpenDoor\"", script);
        Assert.Contains("(Free Action)", script);
    }

    [Fact]
    public async Task StayRunsMonstersInOrder_AndNextRoundPreservesPositions()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        Assert.Equal(1, started.Revision);
        Assert.Equal("barbarian-type", started.Result.NextInput!.TypeId);
        Assert.Equal(DecisionKind.Activation, started.Result.NextInput.Kind);
        var completed = await FinishRound(host, started);
        Assert.True(completed.Result.State.RoundComplete);
        Assert.Null(completed.Result.NextInput);
        var moves = completed.Result.Events.Where(e => e.Kind == "MovementCompleted").ToArray();
        Assert.Equal(new[] { "barbarian", "rogue", "grunt-2", "grunt-1", "zombie-1", "zombie-2", "archer-2", "archer-1", "goblin-1", "goblin-1" }, moves.Select(e => e.UnitId));
        Assert.Equal(new[] { "barbarian-type", "rogue-type", "grunt-type", "zombie-type", "skeleton-archer-type", "goblin-type" },
            completed.Result.Events.Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Contains(moves, e => e.Path!.Count > 1);
        // Each Unit finishes before the next Unit of that Type moves.
        foreach (var typeId in new[] { "grunt-type", "zombie-type", "skeleton-archer-type" })
        {
            var ids = completed.Result.State.Units.Where(u => u.TypeId == typeId).Select(u => u.Id).ToArray();
            var unitEvents = completed.Result.Events.Where(e => ids.Contains(e.UnitId)).Select(e => e.UnitId).ToArray();
            foreach (var id in ids)
            {
                var indices = unitEvents.Select((value, index) => (value, index)).Where(x => x.value == id).Select(x => x.index).ToArray();
                if (indices.Length > 0) Assert.Equal(indices.Length, indices[^1] - indices[0] + 1);
            }
        }
        Assert.Equal("RoundCompleted", completed.Result.Events[^1].Kind);
        Assert.Empty((await host.Read()).Result.Events); // Refresh must not replay effects.
        var next = await host.Round(completed.Revision);
        Assert.Equal(2, next.Result.State.Round);
        Assert.Equal(completed.Result.State.Physical.Figures, next.Result.State.Physical.Figures);
    }

    [Fact]
    public async Task IllegalChoiceAndWrongLifecycleDoNotChangeState_AndOldRevisionCannotBeReused()
    {
        await using var host = await Host.Start();
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = 0, candidateKey = "4,6" })).StatusCode);
        var started = await host.Round(0);
        var before = JsonSerializer.Serialize(await host.Read(), Json);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("round", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "99,99" })).StatusCode);
        // Missing a key is malformed, not an implicit choice to do nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("round", new { })).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await host.Read(), Json));
        await host.Decide(started.Revision, "4,6");
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task RosterAndSealedCryptUseOnlyCurrentContent()
    {
        await using var host = await Host.Start();
        var state = (await host.Read()).Result.State;
        Assert.Equal(new[] { UnitType.Barbarian(), UnitType.Rogue(), UnitType.Grunt(), UnitType.Zombie(),
            UnitType.SkeletonArcher(), UnitType.Goblin() }, state.Types);
        var board = state.Physical.Board;
        var room = Enumerable.Range(1, 3).SelectMany(x => Enumerable.Range(2, 4).Select(y => new Cell(x, y))).ToHashSet();
        var zombies = state.Units.Where(u => u.TypeId == "zombie-type").ToArray();
        Assert.Equal(2, zombies.Length);
        Assert.All(zombies, u => Assert.Contains(state.Physical.Figures.Single(f => f.Id == u.Id).Position, room));
        var boundary = room.SelectMany(c => new[] { new Cell(c.X - 1, c.Y), new Cell(c.X + 1, c.Y),
                new Cell(c.X, c.Y - 1), new Cell(c.X, c.Y + 1) }
            .Where(n => !room.Contains(n)).Select(n => board.EdgeBetween(c, n))).ToArray();
        Assert.Equal(14, boundary.Length);
        Assert.Single(boundary, kind => kind == EdgeKind.ClosedDoor);
        Assert.All(boundary, kind => Assert.Contains(kind, new[] { EdgeKind.Wall, EdgeKind.ClosedDoor }));
        Assert.Equal(state.Physical.Figures.Count, state.Physical.Figures.Select(f => f.Position).Distinct().Count());
        Assert.All(state.Physical.Figures, f => {
            Assert.InRange(f.Position.X, 0, 14); Assert.InRange(f.Position.Y, 0, 14);
            Assert.True(board.TerrainAt(f.Position).Passable());
            var unit = state.Units.Single(u => u.Id == f.Id);
            Assert.Equal(state.Types.Single(t => t.Id == unit.TypeId).Hp, unit.CurrentHp);
        });
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "4,6");
        Assert.DoesNotContain(moved.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
    }

    [Fact]
    public async Task CombinedMonsterResolutionTransportsProgressiveHpSnapshots()
    {
        await using var host = await Host.Start(hit: true);
        var result = await host.Read();
        Assert.Empty(result.Result.ResolutionSteps);
        var found = false;
        for (var round = 0; round < 10 && !found; round++)
        {
            result = await host.Round(result.Revision);
            while (!result.Result.State.RoundComplete)
            {
                var before = result.Result.State;
                result = await host.Decide(result.Revision, null);
                Assert.Equal(Enumerable.Range(0, result.Result.Events.Count),
                    result.Result.ResolutionSteps.Select(s => s.EventIndex));
                var attacks = result.Result.ResolutionSteps
                    .Where(s => result.Result.Events[s.EventIndex] is { Kind: "AttackResolved", Damage: > 0 })
                    .GroupBy(s => result.Result.Events[s.EventIndex].TargetId);
                foreach (var group in attacks.Where(g => g.Count() > 1))
                {
                    var hp = before.Units.Single(u => u.Id == group.Key).CurrentHp;
                    var values = new List<int>();
                    foreach (var step in group)
                    {
                        hp = Math.Max(0, hp - result.Result.Events[step.EventIndex].Damage);
                        var supplied = step.StateAfter.Units.Single(u => u.Id == group.Key).CurrentHp;
                        Assert.Equal(hp, supplied);
                        values.Add(supplied);
                    }
                    Assert.True(values[0] > values[^1]);
                    Assert.Equal(values[^1], result.Result.State.Units.Single(u => u.Id == group.Key).CurrentHp);
                    found = true;
                }
                if (found) break;
            }
        }
        Assert.True(found, "Expected several Monster attacks on the same Hero in one API response.");
        Assert.Empty((await host.Read()).Result.ResolutionSteps);
    }

    [Fact]
    public async Task MonsterAttacksAndDeathAreReturnedInOrder_WithAuthoritativeHpAndFigureRemoval()
    {
        await using var host = await Host.Start(hit: true);
        var result = await host.Read();
        var events = new List<RulesEvent>();
        for (var round = 0; round < 10 && events.All(e => e.Kind != "UnitDied"); round++)
        {
            result = await FinishRound(host, await host.Round(result.Revision));
            events.AddRange(result.Result.Events);
        }
        var died = events.First(e => e.Kind == "UnitDied");
        Assert.Contains(died.UnitId, new[] { "barbarian", "rogue" });
        var deathIndex = events.IndexOf(died);
        Assert.Equal("AttackResolved", events[deathIndex - 1].Kind);
        Assert.Equal(died.UnitId, events[deathIndex - 1].TargetId);
        Assert.True(events[deathIndex - 1].Damage > 0);
        Assert.Equal(0, result.Result.State.Units.Single(u => u.Id == died.UnitId).CurrentHp);
        Assert.DoesNotContain(result.Result.State.Physical.Figures, f => f.Id == died.UnitId);
        Assert.True(result.Result.State.RoundComplete);
        var next = await host.Round(result.Revision);
        var deadType = result.Result.State.Units.Single(u => u.Id == died.UnitId).TypeId;
        Assert.DoesNotContain(next.Result.State.Bag, typeId => typeId == deadType);
        Assert.DoesNotContain(next.Result.Events, e => e.Kind == "TokenDrawn" && e.TypeId == deadType);
    }

    [Fact]
    public async Task PlayerAttackUsesSuppliedCandidate_AndReturnsDamageThenDeath()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var stayedFirst = await host.Decide(started.Revision, null);
        var skipped = await host.Decide(stayedFirst.Revision, null);
        var completed = await FinishRound(host, skipped);
        host.Hits = true;
        var next = await host.Round(completed.Revision);
        // Move toward a supplied melee target rather than assuming a specific Monster's route.
        var destination = next.Result.NextInput!.Candidates.First(c => c.Destination is not null &&
            next.Result.State.Physical.Figures.Any(f => f.Id.StartsWith("grunt-") &&
                Math.Abs(f.Position.X - c.Destination.X) <= 1 && Math.Abs(f.Position.Y - c.Destination.Y) <= 1));
        var moved = await host.Decide(next.Revision, destination.Key);
        var attack = moved.Result.NextInput!.Candidates.First(c => c.TargetId?.StartsWith("grunt-") == true);
        var result = await host.Decide(moved.Revision, attack.Key);
        Assert.Equal("AttackResolved", result.Result.Events[0].Kind);
        Assert.Equal("barbarian", result.Result.Events[0].UnitId);
        Assert.Equal(4, result.Result.Events[0].Damage);
        Assert.Equal("UnitDied", result.Result.Events[1].Kind);
        Assert.Equal(attack.TargetId, result.Result.Events[1].UnitId);
        Assert.Equal(0, result.Result.State.Units.Single(u => u.Id == attack.TargetId).CurrentHp);
        Assert.DoesNotContain(result.Result.State.Physical.Figures, f => f.Id == attack.TargetId);
    }

    [Fact]
    public async Task MonsterFirstToken_AdvancesBeforeExposingPlayerDecision()
    {
        await using var host = await Host.Start(monstersFirst: true);
        var result = await host.Round(0);
        Assert.Equal("grunt-type", result.Result.Events[0].TypeId);
        Assert.Equal("barbarian-type", result.Result.NextInput!.TypeId);
        Assert.Equal(new[] { "grunt-2", "grunt-1", "zombie-1", "zombie-2", "archer-2", "archer-1", "goblin-1", "goblin-1" }, result.Result.Events
            .Where(e => e.Kind == "MovementCompleted").Select(e => e.UnitId));
        Assert.Equal("barbarian-type", result.Result.Events[^1].TypeId);
        Assert.False(result.Result.State.RoundComplete);
    }

    [Fact]
    public async Task ConcurrentSubmissionsWithSameRevision_CommitOnlyOnce()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var responses = await Task.WhenAll(
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "4,6" }),
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "4,6" }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(started.Revision + 1, (await host.Read()).Revision);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task ZombieApproachesDoorAndReportsOutcome(int roll, bool succeeds)
    {
        await using var host = await Host.Start(doorRoll: roll);
        var result = await FinishRound(host, await host.Round(0));
        var move = Assert.Single(result.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == "zombie-1");
        Assert.Equal(new[] { new Cell(2, 3), new Cell(3, 3), new Cell(3, 4) }, move.Path);
        var attempt = Assert.Single(result.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Equal(roll, attempt.DieRoll);
        Assert.Equal(2, attempt.SuccessCount);
        Assert.Equal(succeeds, attempt.Succeeded);
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor,
            result.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        Assert.Equal(succeeds, result.Result.Events.Any(e => e.Kind == "DoorOpened" && e.UnitId == "zombie-1"));
        var next = await FinishRound(host, await host.Round(result.Revision));
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor,
            next.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        foreach (var id in new[] { "zombie-1", "zombie-2" })
        {
            var position = next.Result.State.Physical.Figures.Single(f => f.Id == id).Position;
            Assert.Equal(succeeds, position.X >= 4);
        }
        if (!succeeds)
            Assert.Contains(next.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == "zombie-1");
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("DoorOpeningAttemptResolved", script);
        Assert.Contains("failed; door stays closed", script);
    }

    [Fact]
    public async Task ArcherRetreatsFromHeroAndStillAttacksAutomatically()
    {
        await using var host = await Host.Start();
        var initial = await host.Read();
        Assert.Equal(UnitType.SkeletonArcher(),
            initial.Result.State.Types.Single(t => t.Id == "skeleton-archer-type"));
        var completed = await FinishRound(host, await host.Round(0));
        var move = Assert.Single(completed.Result.Events,
            e => e.Kind == "MovementCompleted" && e.UnitId == "archer-1");
        Assert.Equal(new Cell(6, 7), move.Path![0]);
        Assert.True(move.Path.Count > 1);
        var attack = Assert.Single(completed.Result.Events,
            e => e.Kind == "AttackResolved" && e.UnitId == "archer-1");
        Assert.Equal("barbarian", attack.TargetId);
        var end = move.Path[^1];
        // The courtyard permits retreat to the full firing range.
        Assert.Equal(4, Math.Abs(end.X - 4) + Math.Abs(end.Y - 7));
    }

    [Fact]
    public async Task GoblinApproachesAttacksThenBacksAwayThroughNormalEngineEvents()
    {
        await using var host = await Host.Start();
        Assert.Equal(UnitType.Goblin(), (await host.Read()).Result.State.Types.Single(t => t.Id == "goblin-type"));
        var completed = await FinishRound(host, await host.Round(0));
        var events = completed.Result.Events.Where(e => e.UnitId == "goblin-1").ToArray();
        Assert.Equal(new[] { "MovementCompleted", "AttackResolved", "MovementCompleted" }, events.Select(e => e.Kind));
        Assert.False(events[0].IsMoveAfterAttack);
        Assert.Equal(new Cell(7, 10), events[0].Path![0]);
        Assert.InRange(events[0].Path!.Count - 1, 1, 4);
        Assert.Equal("rogue", events[1].TargetId);
        Assert.True(events[2].IsMoveAfterAttack);
        Assert.Equal(2, events[2].Path!.Count);
        Assert.Equal(events[0].Path![^1], events[2].Path![0]);
        Assert.Equal(events[2].Path![^1], completed.Result.State.Physical.Figures.Single(f => f.Id == "goblin-1").Position);
        Assert.True(completed.Result.State.RoundComplete);
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("decision.isMoveAfterAttack", script);
        Assert.Contains("event.isMoveAfterAttack", script);
    }

    [Fact]
    public async Task RelevanceAutoChoicePreferenceIsBackendVisibleRevisionedAndOutsideGameState()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        Assert.True(started.AutoChooseSingleRelevantChoice);
        Assert.All(started.Result.NextInput!.Candidates, c =>
            Assert.Equal(c.Kind != ActivationChoiceKind.BonusAction, c.Relevant));
        using var snapshot = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        Assert.All(snapshot.RootElement.GetProperty("result").GetProperty("nextInput")
            .GetProperty("candidates").EnumerateArray(), c =>
                Assert.Equal(c.GetProperty("kind").GetString() != "BonusAction", c.GetProperty("relevant").GetBoolean()));
        var stateBefore = JsonSerializer.Serialize(started.Result.State, Json);
        using var response = await host.Post("preferences", new {
            expectedRevision = started.Revision, autoChooseSingleRelevantChoice = false });
        response.EnsureSuccessStatusCode();
        var changed = (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.False(changed.AutoChooseSingleRelevantChoice);
        Assert.Equal(started.Revision + 1, changed.Revision);
        Assert.Equal(stateBefore, JsonSerializer.Serialize(changed.Result.State, Json));
        Assert.False((await host.Read()).AutoChooseSingleRelevantChoice);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("preferences", new {
            expectedRevision = started.Revision, autoChooseSingleRelevantChoice = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("preferences", new {
            expectedRevision = changed.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = changed.Revision, candidateKey = (string?)null })).StatusCode);
        var moved = await host.Decide(changed.Revision, "4,6");
        Assert.False(moved.AutoChooseSingleRelevantChoice);
        Assert.Contains(moved.Result.Events, e => e.Kind == "MovementCompleted");
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("autoChooseSingleRelevantChoice", script);
        Assert.Contains("candidate.relevant === false", script);
        Assert.DoesNotContain("state.phase", script);
        var page = await host.Client.GetStringAsync("/");
        Assert.Contains("Filter irrelevant choices (display only)", page);
        Assert.Contains("Auto-choose single relevant choice", page);
    }

    [Fact]
    public async Task DisabledRelevanceAutoChoice_PreservesOrdinarySoleLegalChoiceResolution()
    {
        await using var host = await Host.Start();
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var started = await host.Round(changed.Revision);
        Assert.False(started.AutoChooseSingleRelevantChoice);
        Assert.Equal(DecisionKind.Activation, started.Result.NextInput!.Kind);
        Assert.Equal("barbarian", started.Result.NextInput.UnitId);
        Assert.True(started.Result.NextInput.Candidates.Count > 1);
        Assert.All(started.Result.NextInput.Candidates, c =>
            Assert.Equal(c.Kind != ActivationChoiceKind.BonusAction, c.Relevant));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = started.Revision, candidateKey = "forged" })).StatusCode);
        var raging = await host.Decide(started.Revision,
            started.Result.NextInput.Candidates.Single(c => c.Kind == ActivationChoiceKind.BonusAction).Key);
        var stayed = await host.Decide(raging.Revision, "stay");
        // Sole End Turn and the next sole Unit selection still resolve automatically.
        Assert.Equal(DecisionKind.Activation, stayed.Result.NextInput!.Kind);
        Assert.Equal("rogue", stayed.Result.NextInput.UnitId);
        Assert.False(stayed.AutoChooseSingleRelevantChoice);
    }

    [Fact]
    public async Task RageIsSuppliedWithUsesEffectiveAtkAndAuthoritativeRelevance()
    {
        await using var host = await Host.Start(hit: true);
        var initial = await host.Read();
        Assert.Equal(new AbilityUses(2, 2), initial.Result.State.Units.Single(u => u.Id == "barbarian").BonusActionUses);
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var started = await host.Round(changed.Revision);
        var rage = Assert.Single(started.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.False(rage.Relevant);
        Assert.Equal("Rage", rage.BonusAction!.Name);
        var moved = await host.Decide(started.Revision, "5,7");
        rage = Assert.Single(moved.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.True(rage.Relevant);
        var raging = await host.Decide(moved.Revision, rage.Key);
        Assert.Equal(new AbilityUses(2, 1), raging.Result.State.Units.Single(u => u.Id == "barbarian").BonusActionUses);
        Assert.True(raging.Result.State.BonusActionUsed);
        Assert.Equal(6, raging.Result.State.EffectiveAtk["barbarian"]);
        Assert.Equal(4, raging.Result.State.Types.Single(t => t.Id == "barbarian-type").Atk);
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 2), Assert.Single(raging.Result.State.ModifiersThisTurn));
        Assert.Equal(6, Assert.Single(raging.Result.ResolutionSteps).StateAfter.EffectiveAtk["barbarian"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = raging.Revision, candidateKey = rage.Key })).StatusCode);
        var attacked = await host.Decide(raging.Revision, "attack:archer-1");
        Assert.Equal(6, Assert.Single(attacked.Result.Events, e => e.Kind == "AttackResolved").Hits);
        Assert.Empty(attacked.Result.State.ModifiersThisTurn);
        Assert.Equal(4, attacked.Result.State.EffectiveAtk["barbarian"]);
        Assert.Equal(new AbilityUses(2, 1), attacked.Result.State.Units.Single(u => u.Id == "barbarian").BonusActionUses);
    }

    [Fact]
    public async Task LegalIrrelevantRageCanBeSubmittedOverHttpAfterAction()
    {
        await using var host = await Host.Start();
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var moved = await host.Decide((await host.Round(changed.Revision)).Revision, "5,7");
        var attacked = await host.Decide(moved.Revision, "attack:archer-1");
        var rage = Assert.Single(attacked.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.True(attacked.Result.State.ActionDone);
        Assert.False(rage.Relevant);
        var used = await host.Decide(attacked.Revision, rage.Key);
        Assert.Contains(used.Result.Events, e => e.Kind == "AbilityUsed" && e.AbilityName == "Rage");
        Assert.Equal(new AbilityUses(2, 1), used.Result.State.Units.Single(u => u.Id == "barbarian").BonusActionUses);
        Assert.Empty(used.Result.State.ModifiersThisTurn);
        Assert.Equal("rogue", used.Result.NextInput!.UnitId);
    }

    private static async Task<GameResponse> FinishRound(Host host, GameResponse result)
    {
        var events = new List<RulesEvent>(result.Result.Events);
        for (var decisions = 0; result.Result.NextInput is not null; decisions++)
        {
            Assert.True(decisions < 12, "A round must stop after the two Heroes' choices.");
            Assert.Contains(result.Result.NextInput.TypeId, new[] { "barbarian-type", "rogue-type" });
            result = await host.Decide(result.Revision, null);
            events.AddRange(result.Result.Events);
        }
        return result with { Result = result.Result with { Events = events } };
    }

    private sealed class FixedRandom(bool hit, bool monstersFirst, int doorRoll) : IRandomProvider
    {
        public bool Hits { get; set; } = hit;
        public string DrawToken(IReadOnlyList<string> bag) => monstersFirst
            ? bag.FirstOrDefault(typeId => typeId is "grunt-type" or "zombie-type" or "skeleton-archer-type" or "goblin-type") ?? bag[0]
            : bag[0];
        public AttackFace RollAttackDie() => Hits ? AttackFace.Hit : AttackFace.Miss;
        public int RollD6() => doorRoll;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    // Real loopback HTTP tests, without adding a test-server package or persistence layer.
    private sealed class Host(WebApplication app, HttpClient client, FixedRandom random) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public bool Hits { set => random.Hits = value; }
        public static async Task<Host> Start(bool hit = false, bool monstersFirst = false, int doorRoll = 6)
        {
            var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var random = new FixedRandom(hit, monstersFirst, doorRoll);
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", contentRoot], random);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, random);
        }
        public async Task<GameResponse> Read() => (await Client.GetFromJsonAsync<GameResponse>("/api/game", Json))!;
        public Task<HttpResponseMessage> Post(string operation, object body) => Client.PostAsJsonAsync($"/api/game/{operation}", body);
        public Task<GameResponse> Round(long revision) => Mutation("round", new { expectedRevision = revision });
        public async Task<GameResponse> Decide(long revision, string? key)
        {
            if (key is null)
                key = (await Read()).Result.NextInput!.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key;
            return await Mutation("decision", new { expectedRevision = revision, candidateKey = key });
        }
        private async Task<GameResponse> Mutation(string operation, object body)
        {
            using var response = await Post(operation, body);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }
}
