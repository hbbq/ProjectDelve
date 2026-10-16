using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class DecisionCommentaryTests
{
    private static GameState Setup(ControllerKind controller = ControllerKind.Human, UnitType? actor = null)
    {
        actor ??= new UnitType("ordinary", 1, 1, 2, 1, 1);
        var target = new UnitType("target", 1, 1, 1, 1, 1);
        return new()
        {
            Physical = new(new(6, 2, []), [new("one", new(0, 0)), new("two", new(4, 0))]),
            Types = [actor, target], Units = [actor.CreateUnit("one", "amber"), target.CreateUnit("two", "violet")],
            Controllers = [new(new(actor.Id, "amber"), controller), new(new(target.Id, "violet"), ControllerKind.Human)]
        };
    }
    private sealed class Dice : IRandomProvider
    {
        public int Draws, Rolls;
        public string? PreferredType;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Draws++; return bag.FirstOrDefault(t => t.TypeId == PreferredType) ?? bag[0]; }
        public AttackFace RollAttackDie() { Rolls++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Rolls++; return DefenceFace.Miss; }
        public int RollD6() { Rolls++; return 6; }
    }
    private static async Task<GameUpdate> Next(GameSubscription subscription, Func<GameUpdate, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { var update = await subscription.Reader.ReadAsync(timeout.Token); if (predicate(update)) return update; }
    }
    private static string Move(GameResponse response) => response.Result.NextInput!.Candidates.First(c => c.Kind == ActivationChoiceKind.Move).Key;

    [Fact]
    public async Task SelectedCommentPrecedesRulesEventsAndReplaysWithoutContaminatingBaseline()
    {
        using var game = new PlaytestGame(new Dice(), Setup());
        game.SetRelevanceAutoChoice(0, false); var start = game.StartRound(1);
        using var updates = game.Subscribe(null); var baseline = await Next(updates, _ => true);
        var response = game.Decide(start.Revision, Move(start), "  Over here!  ");
        var metadata = Assert.Single(response.Commentaries);
        Assert.Equal("Over here!", metadata.Text); Assert.Equal("one", metadata.UnitId);
        Assert.Equal("amber", metadata.SideId); Assert.Equal(ControllerKind.Human, metadata.Controller);
        Assert.Equal(baseline.DecisionId, metadata.DecisionId); Assert.Equal(0, metadata.EventIndex);
        var comment = await Next(updates, u => u.Kind == "commentary");
        var consequence = await Next(updates, u => u.Kind == "event");
        Assert.True(comment.Sequence < consequence.Sequence); Assert.Equal(metadata, comment.Commentary);
        Assert.Empty(comment.Snapshot.Result.Events);
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        Assert.Equal(comment, await Next(replay, u => u.Kind == "commentary"));
        using var fresh = game.Subscribe(null); var refreshed = await Next(fresh, _ => true);
        Assert.Equal("baseline", refreshed.Kind); Assert.Null(refreshed.Commentary); Assert.Empty(refreshed.Snapshot.Commentaries);
        Assert.Empty(game.Snapshot().Commentaries);
        var reset = game.Restart(response.Revision); Assert.Empty(reset.Commentaries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void CommentaryIsOptionalAndDoesNotChangeRulesOrRandomness(string? text)
    {
        var firstDice = new Dice(); var secondDice = new Dice();
        using var first = new PlaytestGame(firstDice, Setup()); using var second = new PlaytestGame(secondDice, Setup());
        first.SetRelevanceAutoChoice(0, false); second.SetRelevanceAutoChoice(0, false);
        var a = first.StartRound(1); var b = second.StartRound(1);
        var plain = first.Decide(a.Revision, Move(a), text); var commented = second.Decide(b.Revision, Move(b), "Moving up.");
        Assert.Empty(plain.Commentaries); Assert.Single(commented.Commentaries);
        Assert.Equal(JsonSerializer.Serialize(plain.Result), JsonSerializer.Serialize(commented.Result));
        Assert.Equal(firstDice.Draws, secondDice.Draws); Assert.Equal(firstDice.Rolls, secondDice.Rolls);
    }

    [Fact]
    public void InvalidCandidatesAndOversizedCommentsDoNotAdvanceOrPublish()
    {
        using var game = new PlaytestGame(new Dice(), Setup()); var start = game.StartRound(0);
        Assert.Equal(400, Assert.Throws<PlaytestRequestException>(() => game.Decide(start.Revision, "invented", "Illegal")).StatusCode);
        Assert.Equal(400, Assert.Throws<PlaytestRequestException>(() => game.Decide(start.Revision, Move(start), new string('x', 281))).StatusCode);
        Assert.Equal(start.Revision, game.Snapshot().Revision); Assert.Empty(game.Snapshot().Commentaries);
    }

    [Fact]
    public async Task CommandTransportPreservesCommentAndExistingSubmissionsDeserialize()
    {
        var old = JsonSerializer.Deserialize<DecisionSubmission>("{\"ExpectedRevision\":0,\"CandidateKey\":\"stay\"}")!;
        Assert.Null(old.Commentary);
        using var game = new PlaytestGame(new Dice(), Setup()); var start = game.StartRound(0);
        using var updates = game.Subscribe(null); var baseline = await Next(updates, _ => true);
        var command = new GameCommand { CommandId = "with-comment", SessionId = baseline.SessionId,
            DecisionId = baseline.DecisionId, ExpectedRevision = start.Revision, Operation = "decision", CandidateKey = Move(start), Commentary = "Advance." };
        Assert.Equal(command, JsonSerializer.Deserialize<GameCommand>(JsonSerializer.Serialize(command)));
        game.Accept(command); var comment = await Next(updates, u => u.Kind == "commentary");
        Assert.Equal("Advance.", comment.Commentary!.Text); Assert.Equal(command.CommandId, comment.CommandId);
        await Next(updates, u => u.Revision > start.Revision);
    }

    [Fact]
    public async Task ExternalControllerCanAttachItsOwnCommentUsingPendingDecisionIdentity()
    {
        using var game = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated), externalSelection:
            (context, _) => Task.FromResult(new ExternalDecisionSelection(context.Request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Move)?.Key
                ?? context.Request.Candidates.First(c => c.Kind == ActivationChoiceKind.EndTurn).Key, "External intention.")));
        using var updates = game.Subscribe(null); game.StartRound(0);
        var pending = await Next(updates, u => u.Status == "waiting-external");
        var comment = await Next(updates, u => u.Kind == "commentary");
        Assert.Equal(pending.DecisionId, comment.Commentary!.DecisionId); Assert.Equal("External intention.", comment.Commentary.Text);
        await Next(updates, u => u.Kind == "boundary" && u.Revision > pending.Revision);
    }

    [Fact]
    public void AutomatedZombieCommentIsOrderedInsideCombinedLegacyResponse()
    {
        using var game = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated, UnitType.Zombie()));
        var response = game.StartRound(0); var comment = Assert.Single(response.Commentaries);
        Assert.Equal("Closer... closer...", comment.Text); Assert.True(comment.EventIndex > 0);
        Assert.Equal("MovementCompleted", response.Result.Events[comment.EventIndex].Kind);
        Assert.Equal("one", comment.UnitId); Assert.Equal(ControllerKind.Automated, comment.Controller);
    }
    [Fact]
    public void ActualDiceContinuationsRemainIdenticalWithComments()
    {
        var setup = Setup(); setup.Physical = setup.Physical with { Figures = [new("one", new(0, 0)), new("two", new(1, 0))] };
        var aDice = new Dice(); var bDice = new Dice();
        using var a = new PlaytestGame(aDice, setup); using var b = new PlaytestGame(bDice, setup);
        var plain = a.StartRound(0); var commented = b.StartRound(0); var sawDice = false;
        for (var count = 0; plain.Result.NextInput is { } pending; count++)
        {
            Assert.True(count < 30);
            var candidate = pending.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)
                ?? pending.Candidates.FirstOrDefault(c => c.Action == UnitAction.NormalAttack)
                ?? pending.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.RollDice)
                ?? pending.Candidates.First(c => c.Kind == ActivationChoiceKind.EndTurn);
            sawDice |= pending.Kind == DecisionKind.RollDice;
            plain = a.Decide(plain.Revision, candidate.Key);
            commented = b.Decide(commented.Revision, candidate.Key, "A chosen intention.");
            Assert.Equal(JsonSerializer.Serialize(plain.Result), JsonSerializer.Serialize(commented.Result));
        }
        Assert.True(sawDice); Assert.True(aDice.Rolls > 0); Assert.Equal(aDice.Rolls, bDice.Rolls); Assert.Equal(aDice.Draws, bDice.Draws);
    }

    [Fact]
    public void ConcreteFlavorsOnlyAccompanyApplicableSelections()
    {
        var goblinSetup = Setup(ControllerKind.Automated, UnitType.Goblin());
        goblinSetup.Physical = goblinSetup.Physical with { Figures = [new("one", new(1, 0)), new("two", new(2, 0))] };
        goblinSetup.Controllers = goblinSetup.Controllers.Select(c => c with { Controller = ControllerKind.Automated }).ToList();
        using var goblin = new PlaytestGame(new Dice(), goblinSetup);
        var escaped = goblin.StartRound(0); var retreat = Assert.Single(escaped.Commentaries);
        Assert.Equal("Hit and run!", retreat.Text);
        Assert.True(escaped.Result.Events[retreat.EventIndex].IsMoveAfterAttack);

        var trollSetup = Setup(ControllerKind.Automated, UnitType.Troll());
        trollSetup.Physical = trollSetup.Physical with
        {
            Figures = [new("one", new(0, 0)), new("two", new(2, 0))],
            Board = new(3, 1, [new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor)])
        };
        using var troll = new PlaytestGame(new Dice(), trollSetup);
        var smashed = troll.StartRound(0); var door = Assert.Single(smashed.Commentaries);
        Assert.Equal("I'll smash this door!", door.Text);
        Assert.Equal("ActionUsed", smashed.Result.Events[door.EventIndex].Kind);
        Assert.Contains(smashed.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved");

        using var shaper = new PlaytestGame(new Dice { PreferredType = UnitTypeIds.LabyrinthShaper }, GameEngine.CreateGame(WebScenarioFixtures.Shaper()));
        var scenario = shaper.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(WebScenarioFixtures.Shaper())); var reshaped = shaper.StartRound(scenario.Revision);
        var reshape = Assert.Single(reshaped.Commentaries);
        Assert.Equal("The room shall take a new shape.", reshape.Text);
        Assert.Equal("ActionUsed", reshaped.Result.Events[reshape.EventIndex].Kind);
        Assert.Contains(reshaped.Result.Events, e => e.Kind == "AbilityUsed" && e.AbilityName == "Reshape Room");

        using var ordinary = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated));
        Assert.Empty(ordinary.StartRound(0).Commentaries);
        using var noDoor = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated, UnitType.Troll()));
        Assert.Empty(noDoor.StartRound(0).Commentaries);
    }

    [Fact]
    public async Task HttpDecisionAndCommandsAcceptOptionalCommentary()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], new Dice(), Setup());
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var roundResponse = await client.PostAsJsonAsync("/api/game/round", new { expectedRevision = 0 });
        roundResponse.EnsureSuccessStatusCode(); var round = (await roundResponse.Content.ReadFromJsonAsync<GameResponse>(json))!;
        using var submitted = await client.PostAsJsonAsync("/api/game/decision", new { expectedRevision = round.Revision, candidateKey = Move(round), commentary = "HTTP intention." });
        submitted.EnsureSuccessStatusCode(); var result = (await submitted.Content.ReadFromJsonAsync<GameResponse>(json))!;
        Assert.Equal("HTTP intention.", Assert.Single(result.Commentaries).Text);
        var game = app.Services.GetRequiredService<PlaytestGame>(); using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        var key = Move(result);
        using var accepted = await client.PostAsJsonAsync("/api/game/commands", new { commandId = "http-comment", sessionId = baseline.SessionId,
            decisionId = baseline.DecisionId, expectedRevision = result.Revision, operation = "decision", candidateKey = key, commentary = "Done for now." });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("Done for now.", (await Next(updates, u => u.Kind == "commentary")).Commentary!.Text);
        var boundary = await Next(updates, u => u.Kind == "boundary" && u.Revision > result.Revision);
        using var restart = await client.PostAsJsonAsync("/api/game/restart", new { expectedRevision = boundary.Revision });
        restart.EnsureSuccessStatusCode(); var restarted = (await restart.Content.ReadFromJsonAsync<GameResponse>(json))!;
        using var nextRound = await client.PostAsJsonAsync("/api/game/round", new { expectedRevision = restarted.Revision });
        nextRound.EnsureSuccessStatusCode(); var next = (await nextRound.Content.ReadFromJsonAsync<GameResponse>(json))!;
        using var oldSubmission = await client.PostAsJsonAsync("/api/game/decision", new { expectedRevision = next.Revision,
            candidateKey = next.Result.NextInput!.Candidates.First().Key });
        oldSubmission.EnsureSuccessStatusCode(); Assert.Empty((await oldSubmission.Content.ReadFromJsonAsync<GameResponse>(json))!.Commentaries);
    }

    [Fact]
    public void LegalNoneFollowupCanCarryCommentaryWithoutChangingSelection()
    {
        var setup = Setup(actor: UnitType.Goblin());
        setup.Physical = setup.Physical with { Figures = [new("one", new(1, 0)), new("two", new(2, 0))] };
        using var game = new PlaytestGame(new Dice(), setup); var result = game.StartRound(0);
        for (var count = 0; !result.Result.NextInput!.IsMoveAfterAttack; count++)
        {
            Assert.True(count < 10); var pending = result.Result.NextInput;
            var candidate = pending.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)
                ?? pending.Candidates.FirstOrDefault(c => c.Action == UnitAction.NormalAttack)
                ?? pending.Candidates.First(c => c.Kind == ActivationChoiceKind.RollDice);
            result = game.Decide(result.Revision, candidate.Key);
        }
        Assert.True(result.Result.NextInput.AllowsNone);
        var stayed = game.Decide(result.Revision, null, "I'll stay here.");
        var comment = Assert.Single(stayed.Commentaries); Assert.Null(comment.CandidateKey);
        Assert.Equal("one", comment.UnitId); Assert.Equal(DecisionKind.Move, comment.DecisionKind);
        Assert.Equal(new Cell(1, 0), stayed.Result.State.Physical.Figures.Single(f => f.Id == "one").Position);
        Assert.Single(Assert.Single(stayed.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == "one").Path!);
    }

}
