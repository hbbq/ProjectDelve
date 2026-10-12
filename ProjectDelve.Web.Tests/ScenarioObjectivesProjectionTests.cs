using System.Collections.Immutable;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioObjectivesProjectionTests
{
    [Fact]
    public void ProjectionRetainsEverySideAndUsesEachPlaybackSnapshotsCompletion()
    {
        var before = GameEngine.CreateGame(Scenario.Define(Scenario.Map(4, 1),
            [Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(0, 0)),
             Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(3, 0))]));
        var after = PlaytestGame.Detach(before);
        after.Physical.Figures.Clear();
        after.Completion = new(ImmutableArray.Create("red", "blue"));
        var result = new EngineResult(after,
            [new("UnitDefeated", UnitId: before.Units[0].Id), new("GameCompleted") { Completion = after.Completion }], null)
        { ResolutionSteps = [new(0, before), new(1, after)] };
        var view = BrowserProjection.Create(result);
        Assert.Equal(new[] { "blue", "red" }, view.Objectives.Select(o => o.SideId).Order());
        Assert.All(view.Objectives, o => Assert.Equal("Eliminate Enemies", o.Name));
        Assert.Equal(after.Completion, view.Completion);
        Assert.Null(view.ResolutionSteps[0].Completion);
        Assert.Equal(after.Completion, view.ResolutionSteps[1].Completion);
        Assert.Equal(2, view.ResolutionSteps[1].Objectives.Count);
        Assert.Contains("Simultaneous victory", view.Events[1].Text);
        Assert.Contains("red, blue", view.Events[1].Text);
    }

    [Fact]
    public async Task WorldEffectCompletionStreamsAfterResolutionAndBlocksProgressionButAllowsRestart()
    {
        var setup = GameEngine.CreateGame(Scenario.Define(Scenario.Map(4, 1),
            [Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(0, 0)),
             Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(3, 0))],
            worldEffects: new(1, 1)));
        setup.WorldDeck!.DrawPile = [new("lonely", WorldEffect.Loneliness)];
        using var game = new PlaytestGame(new Dice(), setup);
        using var subscription = game.Subscribe(null);
        var initial = await subscription.Reader.ReadAsync();
        var completed = game.StartRound(0);
        Assert.Equal(2, completed.Result.State.Completion!.AchievedSideIds.Length);
        var updates = new List<GameUpdate>();
        while (subscription.Reader.TryRead(out var update)) updates.Add(update);
        var events = updates.Where(u => u.Kind == "event").ToList();
        var finish = events.Single(u => u.Snapshot.Result.Events[0].Kind == "GameCompleted");
        Assert.All(events.TakeWhile(u => u != finish), u => Assert.Null(u.Snapshot.Presentation.Completion));
        Assert.Contains(events.TakeWhile(u => u != finish), u => u.Snapshot.Result.Events[0].Kind == "WorldCardDiscarded");
        Assert.Equal(2, finish.Snapshot.Presentation.Completion!.AchievedSideIds.Length);
        var boundary = updates.Last();
        Assert.Equal("completed", boundary.Status);
        Assert.Null(boundary.DecisionId);
        Assert.Null(boundary.Snapshot.Presentation.Decision);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.StartRound(completed.Revision)).StatusCode);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Decide(completed.Revision, null)).StatusCode);
        using var replay = game.Subscribe($"{initial.SessionId}:{initial.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(update);
        Assert.Equal(updates.Select(u => u.Sequence), replayed.Select(u => u.Sequence));
        Assert.Equal(finish.Snapshot.Presentation.Completion, replayed.Single(u => u.Sequence == finish.Sequence).Snapshot.Presentation.Completion);
        var reset = game.Restart(completed.Revision);
        Assert.Null(reset.Result.State.Completion);
        Assert.Equal(0, reset.Result.State.Round);
    }

    private sealed class Dice : IRandomProvider
    {
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
