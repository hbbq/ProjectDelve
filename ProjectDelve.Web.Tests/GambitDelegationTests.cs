using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GambitDelegationTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Attacks, Defences;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { Attacks++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Defences++; return DefenceFace.Block; }
        public int RollD6() => 6;
    }
    private sealed class FaultingDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("injected dice fault");
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }

    private static Dictionary<string, string> Bindings() => new() { ["amber"] = "Gambit", ["violet"] = "Human" };

    private static GameState TwoAlliedUnits()
    {
        var type = new UnitType("ordinary", 1, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new(3, 2, []), [new("one", new(0, 1)), new("two", new(1, 0)), new("enemy", new(2, 0))]),
            Types = [type],
            Units = [type.CreateUnit("one", "amber"), type.CreateUnit("two", "amber"), type.CreateUnit("enemy", "violet")],
            Controllers = [new(new(type.Id, "amber"), ControllerKind.Human), new(new(type.Id, "violet"), ControllerKind.Human)],
            Objectives = [new("amber"), new("violet")]
        };
    }

    private static GameState SingleAlliedUnit()
    {
        var type = new UnitType("ordinary", 1, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new(3, 1, []), [new("one", new(0, 0)), new("enemy", new(1, 0))]),
            Types = [type], Units = [type.CreateUnit("one", "amber"), type.CreateUnit("enemy", "violet")],
            Controllers = [new(new(type.Id, "amber"), ControllerKind.Human), new(new(type.Id, "violet"), ControllerKind.Human)],
            Objectives = [new("amber"), new("violet")]
        };
    }

    private static async Task Wait(PlaytestGame game, Func<PlaytestGame, bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (predicate(game)) return;
            await Task.Delay(10);
        }
        Assert.Fail("Controller progression timed out.");
    }

    [Fact]
    public async Task SelectUnitRequestIncludesDetachedPreviewAndDirectControlRemainsUnchanged()
    {
        var dice = new Dice();
        using var game = new PlaytestGame(dice, TwoAlliedUnits(), controllers: Bindings());
        game.StartRound(0);
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal(3, request.SchemaVersion);
        Assert.Equal(DecisionKind.SelectUnit, request.Request.Kind);
        Assert.Equal(request.Request.Candidates.Select(candidate => candidate.Key).Order(),
            request.DefaultBehaviorPreviews.Select(preview => preview.UnitId).Order());
        Assert.All(request.DefaultBehaviorPreviews, preview => Assert.True(preview.Advisory));
        Assert.Equal(0, dice.Attacks);

        game.AdmitController(request.Binding);
        game.SubmitController(new("direct-select", request.Binding, "two", "I will retain control.", GambitActivationControl.Direct));
        await Wait(game, current => current.Receipt("direct-select").Status == "applied");

        var next = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal(DecisionKind.Activation, next.Request.Kind);
        Assert.Equal("two", next.Request.UnitId);
        var activePreview = Assert.Single(next.DefaultBehaviorPreviews);
        Assert.Equal("two", activePreview.UnitId);
        Assert.Equal(0, dice.Attacks);
    }

    [Fact]
    public async Task DelegationPausesForOtherControllerDefenceThenEndsAtActivationBoundary()
    {
        var dice = new Dice();
        using var game = new PlaytestGame(dice, TwoAlliedUnits(), controllers: Bindings());
        game.StartRound(0);
        var ownership = game.Snapshot().ControllerAssignments.ToArray();
        var select = Assert.IsType<GambitRequest>(game.ControllerRequest());
        game.AdmitController(select.Binding);
        game.SubmitController(new("delegate-two", select.Binding, "two",
            "Default Behavior can handle this attack.", GambitActivationControl.Delegate));
        await Wait(game, current => current.Receipt("delegate-two").Status == "applied" &&
            current.Snapshot().Result.NextInput?.Kind == DecisionKind.RollDice);

        var defence = game.Snapshot();
        Assert.Equal("enemy", defence.Result.NextInput!.Roll!.OwnerUnitId);
        Assert.Equal("violet", defence.Result.NextInput.Token!.SideId);
        Assert.Null(game.ControllerRequest());
        Assert.Equal(2, dice.Attacks);
        Assert.Equal(0, dice.Defences);

        var resumed = game.Decide(defence.Revision, "roll-dice");
        Assert.Contains(resumed.Result.Events, occurrence => occurrence.Kind == "ActivationCompleted" && occurrence.UnitId == "two");
        Assert.DoesNotContain(resumed.Commentaries, commentary => commentary.UnitId == "two");
        var afterDefence = game.Snapshot();
        Assert.True(game.ControllerRequest() is not null, $"status={game.ControllerState().Status}; current={afterDefence.Result.State.CurrentUnitId}; pending={afterDefence.Result.State.Pending?.Kind}; token={afterDefence.Result.State.Pending?.Token}");
        var restored = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal("one", restored.Request.UnitId);
        Assert.Equal(DecisionKind.Activation, restored.Request.Kind);
        Assert.Equal(1, dice.Defences);
        Assert.Equal(ownership, game.Snapshot().ControllerAssignments);
        Assert.Contains("two", game.Snapshot().Result.State.CompletedUnitIds);
    }

    [Fact]
    public async Task MidActivationDelegationUsesNullMetaChoiceAndDefaultsFromCurrentBoundary()
    {
        var dice = new Dice();
        using var game = new PlaytestGame(dice, TwoAlliedUnits(), controllers: Bindings());
        game.StartRound(0);
        var select = Assert.IsType<GambitRequest>(game.ControllerRequest());
        game.AdmitController(select.Binding);
        game.SubmitController(new("direct-to-boundary", select.Binding, "two"));
        await Wait(game, current => current.Receipt("direct-to-boundary").Status == "applied");

        var active = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal(DecisionKind.Activation, active.Request.Kind);
        var preview = Assert.Single(active.DefaultBehaviorPreviews);
        Assert.Equal("two", preview.UnitId);
        Assert.NotEmpty(preview.Steps);
        game.AdmitController(active.Binding);
        game.SubmitController(new("delegate-current", active.Binding, null,
            "Use the advisory default from here.", GambitActivationControl.Delegate));
        await Wait(game, current => current.Receipt("delegate-current").Status == "applied" &&
            current.Snapshot().Result.NextInput?.Kind == DecisionKind.RollDice);

        Assert.Equal(2, dice.Attacks);
        Assert.Equal("enemy", game.Snapshot().Result.NextInput!.Roll!.OwnerUnitId);
    }
    [Fact]
    public void DelegateIsRejectedOutsideCurrentSelectUnitAndStaleSelectionCannotCreateLease()
    {
        using var nonSelection = new PlaytestGame(new Dice(), SingleAlliedUnit(), controllers: Bindings());
        nonSelection.StartRound(0);
        var activation = Assert.IsType<GambitRequest>(nonSelection.ControllerRequest());
        nonSelection.AdmitController(activation.Binding);
        Assert.Equal("rejected", nonSelection.SubmitController(new("bad-delegate", activation.Binding,
            activation.Request.Candidates[0].Key, ActivationControl: GambitActivationControl.Delegate)).Status);

        using var stale = new PlaytestGame(new Dice(), TwoAlliedUnits(), controllers: Bindings());
        stale.StartRound(0);
        var old = Assert.IsType<GambitRequest>(stale.ControllerRequest());
        stale.AdmitController(old.Binding);
        stale.StartScenario(old.Binding.Revision, PlaytestScenarios.DefaultId);
        Assert.Equal("rejected", stale.SubmitController(new("stale-delegate", old.Binding, "two",
            ActivationControl: GambitActivationControl.Delegate)).Status);
        Assert.Equal("idle", stale.ControllerState().Status);
    }

    [Fact]
    public async Task EngineFaultDuringDelegatedForcedRollClearsTheTemporaryLease()
    {
        using var game = new PlaytestGame(new FaultingDice(), TwoAlliedUnits(), controllers: Bindings());
        game.StartRound(0);
        var select = Assert.IsType<GambitRequest>(game.ControllerRequest());
        game.AdmitController(select.Binding);
        game.SubmitController(new("faulted-delegation", select.Binding, "two",
            ActivationControl: GambitActivationControl.Delegate));

        await Wait(game, current => current.Receipt("faulted-delegation").Status == "rejected");

        Assert.Equal("engine-fault", game.Receipt("faulted-delegation").Error);
        Assert.Equal("faulted", game.ControllerState().Status);
        var lease = typeof(PlaytestGame).GetField("delegatedActivation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(lease);
        Assert.Null(lease.GetValue(game));
    }
    [Fact]
    public async Task DelegationAndPreviewUseStandingOrderWithoutChangingOwnership()
    {
        var dice = new Dice();
        var state = SingleAlliedUnit();
        var token = new ActivationToken("ordinary", "amber");
        state.AssignOrder(token, OrderKind.DoNothing);
        using var game = new PlaytestGame(dice, state, controllers: Bindings());
        var ownership = game.Snapshot().ControllerAssignments.ToArray();

        game.StartRound(0);
        var request = Assert.IsType<GambitRequest>(game.ControllerRequest());
        Assert.Equal(DecisionKind.Activation, request.Request.Kind);
        Assert.Equal(OrderKind.DoNothing, Assert.Single(request.Observation.Orders).Kind);
        var preview = Assert.Single(request.DefaultBehaviorPreviews);
        Assert.Equal(new[] { "end-turn" }, preview.Steps.Select(step => step.CandidateKey));

        game.AdmitController(request.Binding);
        game.SubmitController(new("delegate-do-nothing", request.Binding, null,
            ActivationControl: GambitActivationControl.Delegate));
        await Wait(game, current => current.Receipt("delegate-do-nothing").Status == "applied");

        var snapshot = game.Snapshot();
        Assert.Equal(new Cell(0, 0), snapshot.Result.State.Physical.Figures.Single(f => f.Id == "one").Position);
        Assert.Equal(ownership, snapshot.ControllerAssignments);
        Assert.Equal(0, dice.Attacks);
        Assert.Equal(OrderKind.DoNothing, Assert.Single(snapshot.Presentation.Orders).Kind);
    }
}
