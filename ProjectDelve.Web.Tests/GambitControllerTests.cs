using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GambitControllerTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Attacks, Defences;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { Attacks++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Defences++; return DefenceFace.Block; }
        public int RollD6() => 6;
    }
    private static GameState Setup()
    {
        var type = new UnitType("ordinary", 1, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new(3, 1, []), [new("one", new(0,0)), new("two", new(1,0))]),
            Types = [type], Units = [type.CreateUnit("one","amber"), type.CreateUnit("two","violet")],
            Controllers = [new(new(type.Id,"amber"),ControllerKind.Human),new(new(type.Id,"violet"),ControllerKind.Human)],
            Objectives = [new("amber",ScenarioObjective.EliminateEnemies),new("violet",ScenarioObjective.EliminateEnemies)]
        };
    }
    private static Dictionary<string,string> Bindings(bool reversed = false) => new() { ["amber"] = reversed ? "Human" : "Gambit", ["violet"] = reversed ? "Gambit" : "Human" };
    private static async Task Wait(PlaytestGame game, Func<PlaytestGame,bool> predicate)
    {
        for(var n=0;n<200;n++) { if(predicate(game)) return; await Task.Delay(10); }
        Assert.Fail("Controller progression timed out.");
    }
    [Fact]
    public async Task GambitAttackRollsAutomaticallyButHumanDefenceIsManualAndReceiptsReconcile()
    {
        var dice = new Dice(); using var game = new PlaytestGame(dice, Setup(), controllers: Bindings());
        game.StartRound(0);
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal("amber", request.Binding.SideId); Assert.Equal("amber", request.Request.Token!.SideId);
        Assert.False(game.Snapshot().AutoChooseSingleRelevantChoice);
        Assert.True(request.Request.Candidates.Count > 1);
        Assert.Contains(request.Request.Candidates,c=>c.Key=="attack:two");
        Assert.Equal(0, dice.Attacks); Assert.NotEmpty(request.Events);
        game.AdmitController(request.Binding);
        var selection = new GambitSubmission("chosen", request.Binding, "attack:two", "private gambit");
        Assert.Equal("queued", game.SubmitController(selection).Status);
        await Wait(game,g=>g.Receipt("chosen").Status=="applied");
        Assert.Equal("applied",game.SubmitController(selection).Status);
        Assert.Equal(2,dice.Attacks); Assert.Equal(0,dice.Defences);
        Assert.Equal("violet",game.Snapshot().Result.NextInput!.Token!.SideId);
        Assert.Equal(DecisionKind.RollDice,game.Snapshot().Result.NextInput!.Kind);
        Assert.Null(game.ControllerRequest());
        Assert.Equal("unknown",game.Receipt("expired-or-host-restarted").Status);
        Assert.Throws<PlaytestRequestException>(()=>game.SubmitController(selection with { CandidateKey="stay" }));
    }
    [Fact]
    public async Task InvalidSelectionPausesRetryRetainsObservationAndCancelInvalidatesAttempt()
    {
        using var game = new PlaytestGame(new Dice(),Setup(),controllers:Bindings());
        game.StartRound(0); var request=game.ControllerRequest()!; game.AdmitController(request.Binding);
        Assert.Equal("rejected",game.SubmitController(new("invalid",request.Binding,"not-legal")).Status);
        Assert.Equal("controller-error",game.ControllerState().Status); Assert.Null(game.ControllerRequest());
        var state=game.ControllerState();
        game.Accept(new() { CommandId="retry",SessionId=state.SessionId,ExpectedRevision=request.Binding.Revision,Operation="controller/retry" });
        await Wait(game,g=>g.ControllerRequest()!=null);
        var retry=game.ControllerRequest()!;
        Assert.NotEqual(request.Binding.AttemptId,retry.Binding.AttemptId);
        Assert.Equal(JsonSerializer.Serialize(request.Observation),JsonSerializer.Serialize(retry.Observation));
        Assert.Equal(request.Binding.CursorEnd,retry.Binding.CursorEnd);
        Assert.Throws<PlaytestRequestException>(()=>game.AdmitController(request.Binding));
        game.Accept(new() { CommandId="cancel",SessionId=state.SessionId,ExpectedRevision=retry.Binding.Revision,Operation="controller/cancel" });
        await Wait(game,g=>g.ControllerState().Status=="controller-error");
        Assert.Throws<PlaytestRequestException>(()=>game.AdmitController(retry.Binding));
        Assert.NotNull(game.Snapshot().Result.State.Pending);
    }
    [Fact]
    public void ResetInvalidatesAdmissionAndPolicyCannotReenableRelevantChoiceShortcut()
    {
        using var game = new PlaytestGame(new Dice(),Setup(),controllers:Bindings());
        game.StartRound(0); var request=game.ControllerRequest()!; game.AdmitController(request.Binding);
        Assert.Throws<PlaytestRequestException>(()=>game.SetRelevanceAutoChoice(request.Binding.Revision,true));
        game.StartScenario(request.Binding.Revision,PlaytestScenarios.DefaultId);
        Assert.NotEqual(request.Binding.SessionId,game.ControllerState().SessionId);
        Assert.Null(game.ControllerRequest());
        Assert.Equal("rejected",game.SubmitController(new("late",request.Binding,"attack:two")).Status);
        Assert.Equal("idle",game.ControllerState().Status);
    }
    [Fact]
    public async Task RequestTokenRoutesHumanAttackToGambitDefenceWithoutInference()
    {
        var dice=new Dice(); using var game=new PlaytestGame(dice,Setup(),controllers:Bindings(true));
        var round=game.StartRound(0);
        var attack=game.Decide(round.Revision,"attack:two");
        Assert.Equal("amber",attack.Result.NextInput!.Token!.SideId);
        var rolled=game.Decide(attack.Revision,"roll-dice");
        Assert.Equal(1,dice.Defences);
        Assert.Contains(rolled.Result.Events,e=>e.Dice?.Pool.Family==DiceFamily.Defence);
        Assert.True(game.ControllerRequest() is null || game.ControllerRequest()!.Request.Kind != DecisionKind.RollDice);
        await Task.CompletedTask;
    }
    [Fact]
    public void CandidatePlusDeclineIsTwoAlternativesAndCompletedMatchNeverAdmits()
    {
        var state=Setup();
        state.Pending=new(DecisionKind.Move,"ordinary","one",[new("stay",Kind:ActivationChoiceKind.Stay)],true) { Token=new("ordinary","amber") };
        using var game=new PlaytestGame(new Dice(),state,controllers:Bindings());
        var request=game.ControllerRequest()!;
        Assert.Single(request.Request.Candidates); Assert.True(request.Request.AllowsNone);
        state.Completion=new(["amber"]);
        using var complete=new PlaytestGame(new Dice(),state,controllers:Bindings());
        Assert.Null(complete.ControllerRequest()); Assert.Equal("completed",complete.ControllerState().Status);
        Assert.Throws<PlaytestRequestException>(()=>complete.AdmitController(request.Binding));
        Assert.Equal("amber",complete.ControllerState().Objective!.SideId);
    }
    [Fact]
    public void SemanticHistorySurvivesMoreThan256UpdatesAndPollingDoesNotAcknowledgeIt()
    {
        var immobile=new UnitType("immobile",0,1,1,1,1);
        var mobile=new UnitType("mobile",1,1,1,1,1);
        var state=new GameState
        {
            Physical=new(new(50,50,[]),Enumerable.Range(0,130).Select(n=>new Figure($"human-{n}",new(n%50,n/50))).Append(new("gambit",new(49,49))).ToList()),
            Types=[immobile,mobile],Units=Enumerable.Range(0,130).Select(n=>immobile.CreateUnit($"human-{n}","amber")).Append(mobile.CreateUnit("gambit","violet")).ToList(),
            Controllers=[new(new("immobile","amber"),ControllerKind.Human),new(new("mobile","violet"),ControllerKind.Human)],
            Objectives=[new("amber",ScenarioObjective.EliminateEnemies),new("violet",ScenarioObjective.EliminateEnemies)]
        };
        using var game=new PlaytestGame(new Dice(),state,replayCapacity:2,controllers:Bindings(true));
        var round=game.StartRound(0);
        for(var n=0;n<140 && game.ControllerRequest() is null && round.Result.NextInput is {} pending;n++)
            round=game.Decide(round.Revision,pending.Candidates[0].Key,"opposing-human-strategy-marker");
        var request=game.ControllerRequest()!;
        Assert.NotNull(request); Assert.True(request.Events.Count>256);
        Assert.Equal(0,request.Binding.CursorStart);
        Assert.Equal(request.Binding,game.ControllerRequest()!.Binding);
        Assert.Equal(request.Events.Count,game.ControllerRequest()!.Events.Count);
        Assert.Equal(130,request.Observation.Units.Count(u=>u.Unit.SideId=="amber"));
        Assert.DoesNotContain("opposing-human-strategy-marker", JsonSerializer.Serialize(request));
        Assert.DoesNotContain("commentar", JsonSerializer.Serialize(request), StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData(1,8388608)]
    [InlineData(16384,1)]
    public void ObservationOverflowPausesWithoutTruncationOrFallback(int historyLimit,int payloadLimit)
    {
        using var game=new PlaytestGame(new Dice(),Setup(),controllers:Bindings(),historyLimit:historyLimit,payloadLimit:payloadLimit);
        game.StartRound(0);
        Assert.Equal("controller-error",game.ControllerState().Status);
        Assert.Null(game.ControllerRequest());
        Assert.False(game.Snapshot().ControllerStatus!.CanRetry); Assert.False(game.Snapshot().ControllerStatus!.CanCancel);
        Assert.NotNull(game.Snapshot().Result.State.Pending);
        Assert.Throws<PlaytestRequestException>(()=>game.SetRelevanceAutoChoice(game.Snapshot().Revision,false));
        game.StartScenario(game.Snapshot().Revision,PlaytestScenarios.DefaultId);
        Assert.Equal("idle",game.ControllerState().Status);
    }
    [Fact]
    public async Task ReadinessExpiryPublishesWithoutResolvingOrInferring()
    {
        using var game=new PlaytestGame(new Dice(),Setup(),controllers:Bindings());
        game.StartRound(0); game.ControllerContact();
        var binding=game.ControllerRequest()!.Binding;
        Assert.True(game.Snapshot().ControllerStatus!.Available);
        using var subscription=game.Subscribe(null);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        GameUpdate update;
        do { update=await subscription.Reader.ReadAsync(deadline.Token); }
        while(update.Snapshot.ControllerStatus?.Available!=false);
        Assert.Equal("waiting-external",update.Status);
        Assert.Equal(binding,game.ControllerRequest()!.Binding);
        Assert.Equal(binding.Revision,game.Snapshot().Revision);
    }
    [Fact]
    public async Task SetupCommandMapsAreDetachedAndEqualPayloadRetryIsDeduplicated()
    {
        using var game=new PlaytestGame(new Dice());
        var map=game.Snapshot().Result.State.Objectives.ToDictionary(o=>o.SideId,o=>"Human");
        var original=new Dictionary<string,string>(map);
        var command=new GameCommand { CommandId="setup-once",SessionId=game.ControllerState().SessionId,ExpectedRevision=0,Operation="restart",Controllers=map };
        var first=game.Accept(command);
        Assert.Equal(first,game.Accept(command with { Controllers=new(original) }));
        map[map.Keys.First()]="Default Behavior";
        await Wait(game,g=>g.Snapshot().Revision==1);
        Assert.All(game.Snapshot().Controllers.Values,v=>Assert.Equal("Human",v));
        Assert.Null(game.Snapshot().ControllerStatus);
    }
    [Fact]
    public void BrowserReplayPreservesCommentaryAndOriginalIdentityAcrossSideSwap()
    {
        using var game=new PlaytestGame(new Dice());
        var sides=game.Snapshot().Result.State.Objectives.Select(o=>o.SideId).ToArray();
        var first=game.SetupRestart(0,new() { [sides[0]]="Gambit",[sides[1]]="Human" });
        var old=new GameUpdate(game.ControllerState().SessionId,1,"commentary","waiting-external",first.Revision,null,first,
            Commentary:new("old",null,ControllerKind.Automated,sides[0],DecisionKind.Activation,"stay","private-marker",0));
        game.SetupRestart(first.Revision,new() { [sides[0]]="Human",[sides[1]]="Gambit" });
        var replay = game.BrowserUpdate(old);
        Assert.Equal("private-marker", replay.Commentary!.Text);
        Assert.Equal(sides[0], replay.Commentary.SideId);
        Assert.Equal(ControllerKind.Automated, replay.Commentary.Controller);
    }
    [Fact]
    public void BrowserBoundaryIncludesAllCommentaryAndHidesFutureDeck()
    {
        var state=Setup(); state.WorldEffects=new(1,0); state.WorldDeck=new() { DrawPile=[new("future-secret",WorldEffect.Haste)] };
        using var game=new PlaytestGame(new Dice(),state,controllers:Bindings());
        var source=game.Snapshot() with { Commentaries=[new("d","one",ControllerKind.Automated,"amber",DecisionKind.Activation,"stay","opponent-marker",0),new("h","two",ControllerKind.Human,"violet",DecisionKind.Activation,"stay","own-marker",0)] };
        var player=game.BrowserResponse(source);
        Assert.Equal(2, player.Commentaries.Count);
        Assert.Equal("opponent-marker", player.Commentaries[0].Text);
        Assert.Equal("own-marker", player.Commentaries[1].Text);
        Assert.Empty(player.Result.State.WorldDeck!.DrawPile);
        Assert.Equal(1,player.Presentation.WorldEffects!.DrawPileCount);
        Assert.DoesNotContain("future-secret",JsonSerializer.Serialize(player));
        Assert.Single(game.Snapshot().Result.State.WorldDeck!.DrawPile);
    }
}
