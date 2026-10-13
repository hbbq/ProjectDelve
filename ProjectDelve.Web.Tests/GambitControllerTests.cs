using System.Text.Json;
using System.Collections.Immutable;
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
        Assert.Contains("invalid decision", game.Snapshot().ControllerStatus!.Error!, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(130,request.Observation.Units.Count(u=>u.SideId=="amber"));
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
    [Fact]
    public void ProjectionIsDeterministicCompactAndPreservesBoardUnitsTypesBagAndChoices()
    {
        var state = Setup();
        state.Physical = new(new(3, 1, [new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor)])
        {
            Terrain = [new(new(2, 0), TerrainKind.Water)]
        }, state.Physical.Figures);
        state.Bag = [new("ordinary", "violet"), new("ordinary", "amber"), new("ordinary", "amber")];
        state.Pending = new(DecisionKind.Act, "ordinary", "one",
        [
            new("exact-choice", new(2, 0), [new(0, 0), new(1, 0), new(2, 0)], UnitAction.NormalAttack,
                "two", new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor), new(4), ActivationChoiceKind.Action)
                { TargetIds = ["two"] },
            new("stay", Kind: ActivationChoiceKind.Stay)
        ], false) { Token = new("ordinary", "amber") };

        var first = GambitProjection.Observation(state);
        var second = GambitProjection.Observation(state);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(EdgeKind.ClosedDoor, Assert.Single(first.Board.Edges).Kind);
        Assert.Equal(TerrainKind.Water, Assert.Single(first.Board.Terrain).Kind);
        var unit = Assert.Single(first.Units, unit => unit.Id == "one");
        Assert.Equal(1, unit.MaximumHp); Assert.Equal(new GambitStats(1, 1, 2, 1), unit.EffectiveStats);
        Assert.Null(unit.OccupiedCells); Assert.Null(unit.CellSpan);
        var type = Assert.Single(first.Types);
        Assert.Equal(1, type.Hp); Assert.Contains(type.Abilities, ability => ability.Id == "attack");
        Assert.Equal([new GambitBagEntry("ordinary", "amber", 2), new GambitBagEntry("ordinary", "violet", 1)], first.Bag);
        Assert.Contains("Attack dice", first.Glossary.ATK); Assert.Contains("StoneFloor", first.Glossary.Board);

        var decision = GambitProjection.Decision(state.Pending);
        var choice = Assert.Single(decision.Candidates, candidate => candidate.Key == "exact-choice");
        Assert.Equal(new Cell(2, 0), choice.Destination);
        Assert.Equal([new(0, 0), new(1, 0), new(2, 0)], choice.Path);
        Assert.Equal("two", choice.TargetId); Assert.Equal(["two"], choice.TargetIds);
        Assert.Equal(EdgeKind.ClosedDoor, choice.Door!.Kind); Assert.Equal(4, choice.DoorSuccessCount);
        Assert.Equal(["exact-choice", "stay"], decision.Candidates.Select(candidate => candidate.Key));

        var json = JsonSerializer.Serialize(first, GambitJson.Options);
        Assert.Contains("\"a\":[0,0]", json);
        Assert.Contains("\"b\":[1,0]", json);
        Assert.Contains("\"position\":[2,0]", json);
        Assert.DoesNotContain("\"x\":", json);
        Assert.DoesNotContain("\"y\":", json);
        var roundTrip = JsonSerializer.Deserialize<GambitObservation>(json, GambitJson.Options)!;
        Assert.Equal(new Cell(2, 0), Assert.Single(roundTrip.Board.Terrain).Position);
        Assert.Equal(new Cell(0, 0), Assert.Single(roundTrip.Board.Edges).A);
        Assert.DoesNotContain("controllers", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("worldDeck", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":null", json);

        var decisionJson = JsonSerializer.Serialize(decision, GambitJson.Options);
        Assert.Contains("\"destination\":[2,0]", decisionJson);
        Assert.Contains("\"path\":[[0,0],[1,0],[2,0]]", decisionJson);
        Assert.Contains("\"door\":{\"a\":[0,0],\"b\":[1,0]", decisionJson);
        var decisionRoundTrip = JsonSerializer.Deserialize<GambitDecision>(decisionJson, GambitJson.Options)!;
        var roundTripChoice = Assert.Single(decisionRoundTrip.Candidates, candidate => candidate.Key == "exact-choice");
        Assert.Equal(choice.Destination, roundTripChoice.Destination);
        Assert.Equal(choice.Path, roundTripChoice.Path);
        Assert.Equal(choice.Door, roundTripChoice.Door);
    }

    [Fact]
    public void AsciiBoardShowsCoordinatesTerrainEdgesAndEveryCellOfTwoByTwoUnits()
    {
        var ordinary = new UnitType("ordinary", 1, 1, 1, 1, 1);
        var large = new UnitType("large", 1, 1, 1, 1, 2) { Unique = true, Footprint = Footprint.TwoByTwo };
        var board = new Board(4, 3,
        [
            new(new(0, 0), new(1, 0), EdgeKind.Wall),
            new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor),
            new(new(2, 0), new(3, 0), EdgeKind.OpenDoor),
            new(new(0, 1), new(1, 1), EdgeKind.WallWithWindow),
            new(new(0, 0), new(0, 1), EdgeKind.Wall),
            new(new(1, 0), new(1, 1), EdgeKind.ClosedDoor),
            new(new(2, 0), new(2, 1), EdgeKind.OpenDoor),
            new(new(3, 0), new(3, 1), EdgeKind.WallWithWindow)
        ])
        {
            Terrain =
            [
                new(new(0, 0), TerrainKind.Grass),
                new(new(1, 0), TerrainKind.Tree),
                new(new(2, 0), TerrainKind.Water),
                new(new(3, 0), TerrainKind.StoneFloorWithTable)
            ]
        };
        var state = new GameState
        {
            Physical = new(board, [new("unit-exact", new(0, 0)), new("large-exact", new(2, 1))]),
            Types = [ordinary, large],
            Units = [ordinary.CreateUnit("unit-exact", "amber"), large.CreateUnit("large-exact", "violet")],
            Controllers = [new(new("ordinary", "amber"), ControllerKind.Human), new(new("large", "violet"), ControllerKind.Human)],
            Objectives = [new("amber"), new("violet")]
        };

        var observation = GambitProjection.Observation(state);
        var map = observation.AsciiBoard;
        var lines = map.Split("\r\n");

        Assert.Contains("x->right; y->down", map);
        Assert.Contains("repeated unit symbol=2x2 footprint", map);
        Assert.Equal("x>  0  1  2  3", lines[3]);
        Assert.Equal("y0> gB|t-C~-O#-", lines[4]);
        Assert.Equal("    -- CC OO XX", lines[5]);
        Assert.Equal("y1> .-X.-:.A:.A", lines[6]);
        Assert.Equal("    :: :: :: ::", lines[7]);
        Assert.Equal("y2> .-:.-:.A:.A", lines[8]);
        Assert.Equal("units: A=large-exact; B=unit-exact", lines[9]);
        Assert.Equal(4, lines[6].Count(character => character == 'A') + lines[8].Count(character => character == 'A'));
        Assert.DoesNotContain("[0,0", map);
        const int LegacyFocusedMapCharacters = 824;
        Console.WriteLine($"Compact focused 4x3 ASCII map is {map.Length} characters.");
        Assert.True(map.Length < LegacyFocusedMapCharacters * 0.70,
            $"Expected compact map below 70% of {LegacyFocusedMapCharacters} characters, got {map.Length}.");
        Assert.Equal("large-exact", observation.Units.Single(unit => unit.Id == "large-exact").Id);
        Assert.Equal(new Cell(2, 1), observation.Units.Single(unit => unit.Id == "large-exact").Position);
    }

    [Fact]
    public void AsciiBoardPayloadDeltaIsMeasuredAgainstTheSameStructuredObservation()
    {
        var observation = GambitProjection.Observation(Setup());
        var bytesWithMap = JsonSerializer.SerializeToUtf8Bytes(observation, GambitJson.Options).Length;
        var withoutMap = JsonSerializer.SerializeToNode(observation, GambitJson.Options)!.AsObject();
        Assert.True(withoutMap.Remove(JsonNamingPolicy.CamelCase.ConvertName(nameof(GambitObservation.AsciiBoard))));
        var bytesWithoutMap = JsonSerializer.SerializeToUtf8Bytes(withoutMap, GambitJson.Options).Length;
        var delta = bytesWithMap - bytesWithoutMap;

        Console.WriteLine($"ASCII board adds {delta} serialized UTF-8 bytes to the focused 3x1 observation.");
        Assert.True(delta > observation.AsciiBoard.Length,
            $"Expected the JSON property and newline escaping to exceed {observation.AsciiBoard.Length} text characters; got {delta} bytes.");
    }

    [Fact]
    public void ProjectionHidesWorldOrderAndRetainsPublicWorldStateAndEventConsequences()
    {
        var state = Setup();
        state.WorldEffects = new(1, 2);
        state.WorldDeck = new()
        {
            DrawPile = [new("future-secret-a", WorldEffect.Haste), new("future-secret-b", WorldEffect.Weakness)],
            DiscardPile = [new("known-discard", WorldEffect.Fatigue)],
            ActiveContinuous = [new("active-public", WorldEffect.Bloodlust)]
        };
        var observation = GambitProjection.Observation(state);
        var json = JsonSerializer.Serialize(observation);
        Assert.DoesNotContain("future-secret", json); Assert.Contains("active-public", json);
        Assert.Equal(2, observation.WorldEffects!.DrawPileCount);
        Assert.Equal(1, observation.WorldEffects.DiscardPileCount);

        var attack = new RulesEvent("AttackResolved", "one", "two", Hits: 2, Blocks: 1, Damage: 1,
            Attack: new(2, 2, [new("two", 1, 1, 1)]));
        var projectedAttack = GambitProjection.Event(attack);
        Assert.Equal(2, projectedAttack.Hits); Assert.Equal(1, projectedAttack.Blocks);
        Assert.Equal(1, projectedAttack.Damage); Assert.Equal("two", Assert.Single(projectedAttack.Attack!.Targets).TargetId);
        var door = GambitProjection.Event(new("DoorOpeningAttemptResolved", "one",
            Door: new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor), DieRoll: 5, SuccessCount: 4, Succeeded: true));
        Assert.Equal(5, door.DieRoll); Assert.Equal(4, door.SuccessCount); Assert.True(door.Succeeded);
        var movement = GambitProjection.Event(new RulesEvent("UnitMoved", "one", Path: [new(0, 0), new(1, 0)])
            { Cell = new(1, 0) });
        var eventsJson = JsonSerializer.Serialize(new[] { door, movement }, GambitJson.Options);
        Assert.Contains("\"door\":{\"a\":[0,0],\"b\":[1,0]", eventsJson);
        Assert.Contains("\"path\":[[0,0],[1,0]]", eventsJson);
        Assert.Contains("\"cell\":[1,0]", eventsJson);
        var eventsRoundTrip = JsonSerializer.Deserialize<GambitEvent[]>(eventsJson, GambitJson.Options)!;
        Assert.Equal(door.Door, eventsRoundTrip[0].Door);
        Assert.Equal(movement.Path, eventsRoundTrip[1].Path);
        Assert.Equal(movement.Cell, eventsRoundTrip[1].Cell);
        var roll = new DiceResult(new(DiceFamily.Attack, 2, "one", "one", "attack", "Attack", "two"),
            ImmutableArray.Create("Hit", "Miss"), 1);
        var dice = GambitProjection.Event(new("DiceRolled", "one") { Dice = roll });
        Assert.Equal(1, dice.Dice!.Successes); Assert.Equal(["Hit", "Miss"], dice.Dice.Faces);
    }

    [Fact]
    public void RepresentativeRequestKeepsStructuredSavingsAndFullPayloadBelowRecordedBaseline()
    {
        using var game = new PlaytestGame(new Dice());
        var sides = game.Snapshot().Result.State.Objectives.Select(objective => objective.SideId).ToArray();
        var setup = game.SetupRestart(0, new() { [sides[0]] = "Gambit", [sides[1]] = "Human" });
        game.StartRound(setup.Revision);
        var withMap = JsonSerializer.SerializeToNode(game.ControllerRequest(), GambitJson.Options)!.AsObject();
        var bytesWithMap = JsonSerializer.SerializeToUtf8Bytes(withMap, GambitJson.Options).Length;
        var observation = withMap[JsonNamingPolicy.CamelCase.ConvertName(nameof(GambitRequest.Observation))]!.AsObject();
        var mapCharacters = observation[JsonNamingPolicy.CamelCase.ConvertName(nameof(GambitObservation.AsciiBoard))]!.GetValue<string>().Length;
        Assert.True(mapCharacters < 3594 / 2, $"Expected compact map below half of the legacy 3594 characters, got {mapCharacters}.");
        Assert.True(observation.Remove(JsonNamingPolicy.CamelCase.ConvertName(nameof(GambitObservation.AsciiBoard))));
        var structuredBytes = JsonSerializer.SerializeToUtf8Bytes(withMap, GambitJson.Options).Length;
        var mapDelta = bytesWithMap - structuredBytes;
        const int RecordedBaselineBytes = 12302;
        Console.WriteLine($"ASCII board is {mapCharacters} characters and adds {mapDelta} serialized UTF-8 bytes to the representative request.");
        Assert.True(bytesWithMap < RecordedBaselineBytes,
            $"Expected full request < {RecordedBaselineBytes} bytes, got {bytesWithMap}; map adds {mapDelta} bytes.");
        Assert.True(structuredBytes <= RecordedBaselineBytes * 0.60,
            $"Expected structured request <= 60% of {RecordedBaselineBytes} bytes, got {structuredBytes}; map adds {mapDelta} bytes.");
    }

    [Fact]
    public void LargeCandidateSetsRemainComplete()
    {
        var state = Setup();
        state.Pending = new(DecisionKind.Activation, "ordinary", "one",
            Enumerable.Range(0, 2000).Select(index => new Candidate($"choice-{index}", Kind: ActivationChoiceKind.Stay)).ToList(), false)
            { Token = new("ordinary", "amber") };
        using var game = new PlaytestGame(new Dice(), state, controllers: Bindings());
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal(2000, request.Request.Candidates.Count);
        Assert.Equal("choice-0", request.Request.Candidates[0].Key);
        Assert.Equal("choice-1999", request.Request.Candidates[^1].Key);
        var serialized = JsonSerializer.Serialize(request, GambitJson.Options);
        var roundTrip = JsonSerializer.Deserialize<GambitRequest>(serialized, GambitJson.Options)!;
        Assert.Equal(request.Request.Candidates.Select(candidate => candidate.Key),
            roundTrip.Request.Candidates.Select(candidate => candidate.Key));
        Assert.DoesNotContain("commentar", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("model-unavailable", "unavailable or rate limited")]
    [InlineData("invalid-decision", "invalid decision")]
    [InlineData("controller-failure", "controller failed")]
    [InlineData("unexpected-detail", "controller failed")]
    [InlineData(null, "controller failed")]
    public void ControllerErrorsExposeSafeUsefulCategories(string? reason, string expected)
    {
        using var game = new PlaytestGame(new Dice(), Setup(), controllers: Bindings());
        game.StartRound(0);
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        game.ReportControllerError(new(request.Binding, reason));
        var status = game.Snapshot().ControllerStatus!;
        Assert.Equal("controller-error", status.Status);
        Assert.Contains(expected, status.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.True(status.CanRetry); Assert.True(status.CanCancel);
        Assert.NotNull(game.Snapshot().Result.State.Pending);
    }
    private sealed class ShamanFirstDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(token => token.TypeId == UnitTypeIds.Shaman)
            ?? bag.FirstOrDefault(token => token.TypeId == UnitTypeIds.Goblin)
            ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }

    private static async Task<GambitRequest> ReachShamanAction(PlaytestGame game, string submissionId)
    {
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        if (request.Request.Candidates.Any(candidate => candidate.Action == UnitAction.SummonAdjacent)) return request;
        var stay = request.Request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay);
        Assert.True(stay is not null || request.Request.AllowsNone);
        game.AdmitController(request.Binding);
        game.SubmitController(new(submissionId, request.Binding, stay?.Key));
        await Wait(game, g => g.Receipt(submissionId).Status == "applied");
        return Assert.IsType<GambitRequest>(game.ControllerRequest());
    }
    [Fact]
    public void ExactAssignmentsRequireCompleteUniqueAuthoredTokensAndOneGambitSide()
    {
        using var game = new PlaytestGame(new Dice());
        var authored = PlaytestScenarios.Catalog.Single(s => s.Id == "shaman-hunt").ControllerAssignments.ToList();
        var exact = authored.Select(a => a with { Controller = a.TypeId == UnitTypeIds.Shaman ? "Gambit" : a.Controller }).ToList();
        var configured = game.SetupScenario(0, "shaman-hunt", null, exact);
        Assert.Equal(exact.OrderBy(a => a.TypeId), configured.ControllerAssignments.OrderBy(a => a.TypeId));
        Assert.Equal("Mixed", configured.Controllers["red"]);
        Assert.Equal(ControllerKind.Automated, configured.Result.State.ControllerFor(new ActivationToken(UnitTypeIds.Shaman, "red")));
        Assert.Equal(ControllerKind.Automated, configured.Result.State.ControllerFor(new ActivationToken(UnitTypeIds.Goblin, "red")));

        Assert.Throws<PlaytestRequestException>(() => game.SetupRestart(configured.Revision, new() { ["blue"] = "Human", ["red"] = "Human" }, exact));
        Assert.Throws<PlaytestRequestException>(() => game.SetupRestart(configured.Revision, null, exact.Skip(1).ToList()));
        Assert.Throws<PlaytestRequestException>(() => game.SetupRestart(configured.Revision, null, [.. exact, exact[0]]));
        var twoSides = exact.Select(a => a with { Controller = a.TypeId is UnitTypeIds.Shaman or UnitTypeIds.Barbarian ? "Gambit" : a.Controller }).ToList();
        Assert.Throws<PlaytestRequestException>(() => game.SetupRestart(configured.Revision, null, twoSides));
    }

    [Fact]
    public void ProjectionMarksDirectAlliedAndHostileUnitsWithoutExposingProviderMap()
    {
        var state = Setup();
        var ally = new UnitType("ally", 1, 1, 1, 1, 1);
        state.Types.Add(ally); state.Units.Add(ally.CreateUnit("friend", "amber"));
        state.Controllers.Add(new(new("ally", "amber"), ControllerKind.Automated));
        var bindings = new Dictionary<ActivationToken, string>
        {
            [new("ordinary", "amber")] = "Gambit",
            [new("ally", "amber")] = "Default Behavior",
            [new("ordinary", "violet")] = "Human"
        };
        var observation = GambitProjection.Observation(state, "amber", bindings);
        Assert.Equal(GambitAgency.Direct, observation.Units.Single(u => u.Id == "one").Agency);
        Assert.Equal(GambitAgency.Allied, observation.Units.Single(u => u.Id == "friend").Agency);
        Assert.Equal(GambitAgency.Hostile, observation.Units.Single(u => u.Id == "two").Agency);
        var json = JsonSerializer.Serialize(observation, GambitJson.Options);
        Assert.Contains("\"agency\":\"Direct\"", json); Assert.DoesNotContain("controllerBindings", json);
    }

    [Fact]
    public async Task ShamanGambitSummonsPreassignedDefaultGoblinAsAllied()
    {
        using var game = new PlaytestGame(new ShamanFirstDice());
        var authored = PlaytestScenarios.Catalog.Single(s => s.Id == "shaman-hunt").ControllerAssignments;
        var exact = authored.Select(a => a with { Controller = a.TypeId == UnitTypeIds.Shaman ? "Gambit" : a.Controller }).ToList();
        var configured = game.SetupScenario(0, "shaman-hunt", null, exact);
        game.StartRound(configured.Revision);
        var request = await ReachShamanAction(game, "stay-before-default-summon");
        Assert.Equal(2, request.SchemaVersion); Assert.Equal(UnitTypeIds.Shaman, request.Request.TypeId);
        var summon = request.Request.Candidates.First(candidate => candidate.Action == UnitAction.SummonAdjacent);
        game.AdmitController(request.Binding);
        game.SubmitController(new("summon-default-goblin", request.Binding, summon.Key));
        await Wait(game, g => g.Receipt("summon-default-goblin").Status == "applied");
        var snapshot = game.Snapshot();
        var providers = snapshot.ControllerAssignments.ToDictionary(a => new ActivationToken(a.TypeId, a.SideId), a => a.Controller);
        var goblin = Assert.Single(GambitProjection.Observation(snapshot.Result.State, "red", providers).Units, unit => unit.TypeId == UnitTypeIds.Goblin);
        Assert.Equal(GambitAgency.Allied, goblin.Agency);
        Assert.Equal("Default Behavior", snapshot.ControllerAssignments.Single(a => a.TypeId == UnitTypeIds.Goblin && a.SideId == "red").Controller);
    }

    [Fact]
    public async Task UnauthoredSpawnedTokenInheritsTheSummonersExactProvider()
    {
        var definition = Scenario.Define(Scenario.Map(6, 4),
        [
            Scenario.Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, Scenario.At(2, 1)),
            Scenario.Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, Scenario.At(5, 3))
        ], [UnitTypeIds.Shaman, UnitTypeIds.Goblin, UnitTypeIds.Barbarian]);
        using var game = new PlaytestGame(new ShamanFirstDice());
        var configured = game.SetupImport(0, ScenarioDefinitionTransport.Encode(definition), null,
        [
            new(UnitTypeIds.Shaman, "red", "Gambit"),
            new(UnitTypeIds.Barbarian, "blue", "Human")
        ]);
        game.StartRound(configured.Revision);
        var request = await ReachShamanAction(game, "stay-before-inherited-summon");
        var summon = request.Request.Candidates.First(candidate => candidate.Action == UnitAction.SummonAdjacent);
        game.AdmitController(request.Binding);
        game.SubmitController(new("summon-inherited-goblin", request.Binding, summon.Key));
        await Wait(game, g => g.Receipt("summon-inherited-goblin").Status == "applied");
        var snapshot = game.Snapshot();
        var inherited = Assert.Single(snapshot.ControllerAssignments, a => a.TypeId == UnitTypeIds.Goblin && a.SideId == "red");
        Assert.Equal("Gambit", inherited.Controller);
        var providers = snapshot.ControllerAssignments.ToDictionary(a => new ActivationToken(a.TypeId, a.SideId), a => a.Controller);
        Assert.Equal(GambitAgency.Direct, Assert.Single(GambitProjection.Observation(snapshot.Result.State, "red", providers).Units, u => u.TypeId == UnitTypeIds.Goblin).Agency);
    }
    [Fact]
    public void MixedSameSideRoutingUsesTheExactDecisionTokenIncludingDice()
    {
        var shaman = UnitType.Shaman(); var goblin = UnitType.Goblin(); var hero = UnitType.Barbarian();
        var state = new GameState
        {
            Physical = new(new(4, 2, []), [new("shaman", new(0, 0)), new("goblin", new(1, 0)), new("hero", new(3, 1))]),
            Types = [shaman, goblin, hero],
            Units = [shaman.CreateUnit("shaman", "red"), goblin.CreateUnit("goblin", "red"), hero.CreateUnit("hero", "blue")],
            Controllers = [new(new(shaman.Id, "red"), ControllerKind.Automated), new(new(goblin.Id, "red"), ControllerKind.Automated), new(new(hero.Id, "blue"), ControllerKind.Human)],
            Objectives = [new("red"), new("blue")]
        };
        var exact = new ControllerSelection[] { new(shaman.Id, "red", "Gambit"), new(goblin.Id, "red", "Default Behavior"), new(hero.Id, "blue", "Human") };
        var goblinPool = new DicePool(DiceFamily.Attack, 1, "goblin", "goblin", "attack", "Attack", "hero");
        state.Pending = new(DecisionKind.RollDice, goblin.Id, "goblin", [new("roll-dice", Kind: ActivationChoiceKind.RollDice)], false)
            { Token = new(goblin.Id, "red"), Roll = goblinPool };
        using var defaultGame = new PlaytestGame(new Dice(), state, controllerAssignments: exact);
        Assert.Null(defaultGame.ControllerRequest()); Assert.Equal("resolving", defaultGame.ControllerState().Status);

        var shamanPool = new DicePool(DiceFamily.Defence, 1, "shaman", "hero", "attack", "Defence", "shaman");
        state.Pending = new(DecisionKind.RollDice, shaman.Id, "shaman", [new("roll-dice", Kind: ActivationChoiceKind.RollDice)], false)
            { Token = new(shaman.Id, "red"), Roll = shamanPool };
        using var gambitGame = new PlaytestGame(new Dice(), state, controllerAssignments: exact);
        var request = Assert.IsType<GambitRequest>(gambitGame.ControllerRequest());
        Assert.Equal(new ActivationToken(shaman.Id, "red"), request.Request.Token);
        Assert.Equal(DiceFamily.Defence, request.Request.Roll!.Family);
    }
}
