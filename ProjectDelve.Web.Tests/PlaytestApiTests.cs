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
        Assert.Equal(10, second.Result.State.Physical.Board.Width);
        Assert.Equal(8, second.Result.State.Physical.Board.Height);
        Assert.Equal(9, second.Result.State.Physical.Figures.Count);
        var heroes = second.Result.State.Units.Where(u => u.SideId == "blue").ToArray();
        Assert.Equal(new[] { "aria", "bram" }, heroes.Select(u => u.Id));
        Assert.Equal(2, heroes.Select(u => u.TypeId).Distinct().Count());
        Assert.All(second.Result.State.Types.Where(t => heroes.Any(u => u.TypeId == t.Id)),
            type => Assert.Equal(UnitAction.NormalAttack | UnitAction.OpenDoor, type.Actions));
        Assert.Equal(new[] { 1, 3, 2, 1 }, second.Result.State.Units.Where(u => u.SideId == "red")
            .GroupBy(u => u.TypeId).Select(group => group.Count()));
        Assert.Equal(5, second.Result.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.ClosedDoor));
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

    [Fact]
    public async Task StayRunsMonstersInOrder_AndNextRoundPreservesPositions()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        Assert.Equal(1, started.Revision);
        Assert.Equal("aria-type", started.Result.NextInput!.TypeId);
        Assert.Equal(DecisionKind.Move, started.Result.NextInput.Kind);
        var completed = await FinishRound(host, started);
        Assert.True(completed.Result.State.RoundComplete);
        Assert.Null(completed.Result.NextInput);
        var moves = completed.Result.Events.Where(e => e.Kind == "MovementCompleted").ToArray();
        Assert.Equal(new[] { "aria", "bram", "wolf-1", "wolf-2", "wolf-3", "sentinel-1", "sentinel-2", "zombie-1", "archer-1" }, moves.Select(e => e.UnitId));
        Assert.Equal(new[] { "aria-type", "bram-type", "wolf-type", "sentinel-type", "zombie-type", "skeleton-archer-type" },
            completed.Result.Events.Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Contains(moves, e => e.Path!.Count > 1);
        foreach (var typeId in new[] { "wolf-type", "sentinel-type" })
        {
            var groupIds = completed.Result.State.Units.Where(u => u.TypeId == typeId).Select(u => u.Id).ToArray();
            var groupEvents = completed.Result.Events.Where(e => groupIds.Contains(e.UnitId)).ToList();
            var firstAttack = groupEvents.FindIndex(e => e.Kind == "AttackResolved");
            if (firstAttack >= 0)
                Assert.All(groupEvents.Skip(firstAttack), e => Assert.NotEqual("MovementCompleted", e.Kind));
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
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = 0, candidateKey = "3,2" })).StatusCode);
        var started = await host.Round(0);
        var before = JsonSerializer.Serialize(await host.Read(), Json);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("round", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "99,99" })).StatusCode);
        // Missing a key is malformed, not an implicit choice to do nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("round", new { })).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await host.Read(), Json));
        await host.Decide(started.Revision, "3,2");
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task DoorChoiceUsesEngineKey_AndOpenDoorPersistsIntoNextRound()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "3,2");
        Assert.Equal(new[] { new Cell(1, 2), new Cell(2, 2), new Cell(3, 2) },
            Assert.Single(moved.Result.Events, e => e.Kind == "MovementCompleted").Path);
        var door = moved.Result.NextInput!.Candidates.Single(c => c.Action == UnitAction.OpenDoor);
        var opened = await host.Decide(moved.Revision, door.Key);
        var doorEvent = Assert.Single(opened.Result.Events, e => e.Kind == "DoorOpened");
        Assert.Equal(EdgeKind.OpenDoor, doorEvent.Door!.Kind);
        Assert.Contains(doorEvent.Door, opened.Result.State.Physical.Board.Edges);
        Assert.Equal("bram-type", opened.Result.NextInput!.TypeId);
        var completed = await FinishRound(host, opened);
        var next = await host.Round(completed.Revision);
        Assert.Contains(doorEvent.Door, next.Result.State.Physical.Board.Edges);
        // The Wolf occupies (4,2): an open door does not make an occupied destination legal.
        Assert.Contains(next.Result.State.Physical.Figures, f => f.Position == new Cell(4, 2));
        Assert.DoesNotContain(next.Result.NextInput!.Candidates, c => c.Destination == new Cell(4, 2));
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
        Assert.Contains(died.UnitId, new[] { "aria", "bram" });
        var deathIndex = events.IndexOf(died);
        Assert.Equal("AttackResolved", events[deathIndex - 1].Kind);
        Assert.Equal(died.UnitId, events[deathIndex - 1].TargetId);
        Assert.Equal(1, events[deathIndex - 1].Damage);
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
        var moved = await host.Decide(started.Revision, "3,2");
        var opened = await host.Decide(moved.Revision,
            moved.Result.NextInput!.Candidates.Single(c => c.Action == UnitAction.OpenDoor).Key);
        var completed = await FinishRound(host, opened);
        host.Hits = true;
        var next = await host.Round(completed.Revision);
        var stayed = await host.Decide(next.Revision, null);
        var attack = stayed.Result.NextInput!.Candidates.Single(c => c.TargetId == "wolf-1");
        var result = await host.Decide(stayed.Revision, attack.Key);
        Assert.Equal("AttackResolved", result.Result.Events[0].Kind);
        Assert.Equal("aria", result.Result.Events[0].UnitId);
        Assert.Equal(2, result.Result.Events[0].Damage);
        Assert.Equal("UnitDied", result.Result.Events[1].Kind);
        Assert.Equal("wolf-1", result.Result.Events[1].UnitId);
        Assert.Equal(0, result.Result.State.Units.Single(u => u.Id == "wolf-1").CurrentHp);
        Assert.DoesNotContain(result.Result.State.Physical.Figures, f => f.Id == "wolf-1");
    }

    [Fact]
    public async Task MonsterFirstToken_AdvancesBeforeExposingPlayerDecision()
    {
        await using var host = await Host.Start(monstersFirst: true);
        var result = await host.Round(0);
        Assert.Equal("wolf-type", result.Result.Events[0].TypeId);
        Assert.Equal("aria-type", result.Result.NextInput!.TypeId);
        Assert.Equal(new[] { "wolf-1", "wolf-2", "wolf-3", "sentinel-1", "sentinel-2", "zombie-1", "archer-1" }, result.Result.Events
            .Where(e => e.Kind == "MovementCompleted").Select(e => e.UnitId));
        Assert.Equal("aria-type", result.Result.Events[^1].TypeId);
        Assert.False(result.Result.State.RoundComplete);
    }

    [Fact]
    public async Task BothHeroDoorsChangeMonsterRoutes_AndMonstersAttackBothHeroes()
    {
        await using var closedHost = await Host.Start();
        await using var openHost = await Host.Start();
        var closed = await ApproachDoors(closedHost, open: false);
        var opened = await ApproachDoors(openHost, open: true);
        Assert.True(opened.Result.State.RoundComplete);
        Assert.Equal(new[] { "aria", "bram" }, opened.Result.Events
            .Where(e => e.Kind == "DoorOpened").Select(e => e.UnitId));
        Assert.Equal(new[] { "aria", "bram" }, opened.Result.Events
            .Where(e => e.Kind == "AttackResolved").Select(e => e.TargetId).Distinct().Order());
        var closedMoves = closed.Result.Events.Where(e => e.Kind == "MovementCompleted" && e.UnitId is not ("aria" or "bram"));
        var openMoves = opened.Result.Events.Where(e => e.Kind == "MovementCompleted" && e.UnitId is not ("aria" or "bram"));
        Assert.NotEqual(JsonSerializer.Serialize(closedMoves, Json), JsonSerializer.Serialize(openMoves, Json));
        Assert.Equal(new Cell(3, 2), opened.Result.State.Physical.Figures.Single(f => f.Id == "aria").Position);
        Assert.Equal(new Cell(7, 5), opened.Result.State.Physical.Figures.Single(f => f.Id == "bram").Position);
    }

    [Fact]
    public async Task ConcurrentSubmissionsWithSameRevision_CommitOnlyOnce()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var responses = await Task.WhenAll(
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "3,2" }),
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "3,2" }));
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
        Assert.Equal(new[] { new Cell(0, 6), new Cell(0, 5), new Cell(0, 4) }, move.Path);
        var attempt = Assert.Single(result.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Equal(roll, attempt.DieRoll);
        Assert.Equal(2, attempt.SuccessCount);
        Assert.Equal(succeeds, attempt.Succeeded);
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor,
            result.Result.State.Physical.Board.EdgeBetween(new(0, 3), new(0, 4)));
        Assert.Equal(succeeds, result.Result.Events.Any(e => e.Kind == "DoorOpened" && e.UnitId == "zombie-1"));
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
        Assert.Equal(new Cell(1, 0), move.Path![0]);
        Assert.True(move.Path.Count > 1);
        var attack = Assert.Single(completed.Result.Events,
            e => e.Kind == "AttackResolved" && e.UnitId == "archer-1");
        Assert.Equal("aria", attack.TargetId);
        var end = move.Path[^1];
        // The tree and west barrier limit the initially available firing distance to three.
        Assert.Equal(3, Math.Abs(end.X - 1) + Math.Abs(end.Y - 2));
    }

    private static async Task<GameResponse> ApproachDoors(Host host, bool open)
    {
        var result = await host.Round(0);
        var events = new List<RulesEvent>(result.Result.Events);
        foreach (var (unitId, destination) in new[] { ("aria", "3,2"), ("bram", "7,5") })
        {
            Assert.Equal(unitId, result.Result.NextInput!.UnitId);
            result = await host.Decide(result.Revision, destination);
            events.AddRange(result.Result.Events);
            Assert.Equal(DecisionKind.Act, result.Result.NextInput!.Kind);
            var door = result.Result.NextInput.Candidates.Single(c => c.Action == UnitAction.OpenDoor);
            result = await host.Decide(result.Revision, open ? door.Key : null);
            events.AddRange(result.Result.Events);
        }
        return result with { Result = result.Result with { Events = events } };
    }

    private static async Task<GameResponse> FinishRound(Host host, GameResponse result)
    {
        var events = new List<RulesEvent>(result.Result.Events);
        for (var decisions = 0; result.Result.NextInput is not null; decisions++)
        {
            Assert.True(decisions < 12, "A round must stop after the two Heroes' choices.");
            Assert.Contains(result.Result.NextInput.TypeId, new[] { "aria-type", "bram-type" });
            result = await host.Decide(result.Revision, null);
            events.AddRange(result.Result.Events);
        }
        return result with { Result = result.Result with { Events = events } };
    }

    private sealed class FixedRandom(bool hit, bool monstersFirst, int doorRoll) : IRandomProvider
    {
        public bool Hits { get; set; } = hit;
        public string DrawToken(IReadOnlyList<string> bag) => monstersFirst
            ? bag.FirstOrDefault(typeId => typeId is "wolf-type" or "sentinel-type" or "zombie-type" or "skeleton-archer-type") ?? bag[0]
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
        public Task<GameResponse> Decide(long revision, string? key) => Mutation("decision", new { expectedRevision = revision, candidateKey = key });
        private async Task<GameResponse> Mutation(string operation, object body)
        {
            using var response = await Post(operation, body);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }
}
