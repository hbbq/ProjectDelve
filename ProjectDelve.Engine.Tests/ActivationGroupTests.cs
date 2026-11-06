using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ActivationGroupTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 12, Height = 4 }, UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0), UnitId: "plain"),
            new(UnitTypeIds.Grunt, "blue", new(2, 0), UnitId: "a", GroupId: "a"),
            new(UnitTypeIds.Grunt, "blue", new(4, 0), UnitId: "b", GroupId: "b")],
        Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "blue", ControllerKind.Automated, "b")],
        Orders = [new(UnitTypeIds.Grunt, "blue", OrderKind.HoldPosition)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)]
    };
    [Fact]
    public void SetupCopiesDefaultsAndExactOverridesRemainIndependent()
    {
        var definition = Definition();
        definition.Orders!.Add(new(UnitTypeIds.Grunt, "blue", OrderKind.DoNothing, GroupId: "b"));
        var state = GameEngine.CreateGame(definition);
        var plain = new ActivationToken(UnitTypeIds.Grunt, "blue");
        var a = plain with { GroupId = "a" }; var b = plain with { GroupId = "b" };
        Assert.Equal(3, state.Controllers.Count); Assert.Equal(3, state.Orders.Count);
        Assert.Equal(ControllerKind.Human, state.ControllerFor(a));
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(b));
        Assert.Equal(OrderKind.DoNothing, state.OrderFor(b).Kind);
        state.AssignOrder(plain, OrderKind.Normal);
        Assert.Equal(OrderKind.HoldPosition, state.OrderFor(a).Kind);
        Assert.Equal(OrderKind.Normal, state.OrderFor(plain).Kind);
        Assert.Null(state.Units[0].GroupId);
        Assert.Equal(3, ScenarioGroups.Controllers(definition).Count);
    }
    [Fact]
    public void BagUsesFullIdentityAndDrawCapturesOnlyCurrentMembers()
    {
        var result = GameEngine.StartRound(GameEngine.CreateGame(Definition()), new Random(), false);
        Assert.Null(result.State.ActiveToken!.GroupId);
        Assert.Equal(new[] { "a", "b" }, result.State.Bag.Select(t => t.GroupId));
        Assert.Equal(new[] { "plain" }, result.State.ResolvingTokenUnitIds);
        result.State.PlaceUnit(UnitTypeIds.Grunt, "late", "blue", new(1, 2));
        result.State.PlaceUnit(UnitTypeIds.Grunt, "before-a", "blue", new(3, 2), groupId: "a");
        result = GameEngine.Advance(result.State, new Choice("stay"), new Random(), false);
        if (result.State.ActiveToken?.GroupId is null)
            result = GameEngine.Advance(result.State, new Choice("end-turn"), new Random(), false);
        Assert.Equal("a", result.State.ActiveToken!.GroupId);
        Assert.DoesNotContain("late", result.State.CompletedUnitIds);
        Assert.Equal(new[] { "a", "before-a" }, result.State.ResolvingTokenUnitIds.Order());
        Assert.Equal(DecisionKind.SelectUnit, result.NextInput!.Kind);
        Assert.Equal(new[] { "a", "before-a" }, result.NextInput.Candidates.Select(c => c.Key));
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        Assert.Equal(result.State.ResolvingTokenUnitIds.Order(), saved.ResolvingTokenUnitIds.Order());
        var first = GameEngine.Advance(result.State, new Choice("before-a"), new Random(), false);
        var replay = GameEngine.Advance(saved, new Choice("before-a"), new Random(), false);
        Assert.Equal(JsonSerializer.Serialize(first.State), JsonSerializer.Serialize(replay.State));
        Assert.Equal("before-a", first.State.CurrentUnitId);
    }
    [Fact]
    public void CopiedRouteProgressAdvancesOnlyTheMovingGroup()
    {
        var definition = Definition() with
        {
            Waypoints = [new("first", new(0, 0, 3, 1)), new("last", new(8, 0, 1, 1))],
            Routes = [new("route", ["first", "last"])],
            Orders = [new(UnitTypeIds.Grunt, "blue", OrderKind.FollowRoute, Route: new("route"))]
        };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Random(), false);
        result = GameEngine.Advance(result.State, new Choice("stay"), new Random(), false);
        Assert.Equal(1, result.State.OrderFor(new(UnitTypeIds.Grunt, "blue")).Route!.CurrentIndex);
        Assert.Equal(0, result.State.OrderFor(new(UnitTypeIds.Grunt, "blue", "a")).Route!.CurrentIndex);
    }
    [Fact]
    public void EmptyIdentityTokenRemainsInBagAndResolvesWithoutParticipants()
    {
        var result = GameEngine.StartRound(GameEngine.CreateGame(Definition()), new Random(), false);
        result.State.Units[1] = result.State.Units[1] with { CurrentHp = 0 };
        result.State.Physical.Figures.RemoveAll(f => f.Id == "a");
        Assert.Contains(new ActivationToken(UnitTypeIds.Grunt, "blue", "a"), result.State.Bag);
        result = GameEngine.Advance(result.State, new Choice("stay"), new Random(), false);
        Assert.Contains(result.Events, e => e.Kind == "TokenDrawn" && e.Token!.GroupId == "a");
        Assert.DoesNotContain(result.Events, e => e.Kind == "ActivationStarted" && e.Token!.GroupId == "a");
    }
    [Fact]
    public void GroupTargetsAndFogObservationsRespectExactGroupIdentity()
    {
        var definition = Definition();
        definition.Units.Add(new(UnitTypeIds.Grunt, "red", new(11, 3), UnitId: "hidden", GroupId: "hidden"));
        definition.Units.Add(new(UnitTypeIds.Grunt, "red", new(0, 1), UnitId: "visible", GroupId: "visible"));
        definition.Agency.Add(new(UnitTypeIds.Grunt, "red", ControllerKind.Human));
        var state = GameEngine.CreateGame(definition);
        state.FogOfWarSides = ["blue"];
        state.ExploredCells["blue"] = [new(0, 0), new(0, 1)];
        var visibleToken = state.Units.Single(u => u.Id == "visible").Token;
        var hiddenToken = state.Units.Single(u => u.Id == "hidden").Token;
        var queries = new GameplayQueries(state);
        Assert.Equal(new[] { "visible" }, queries.EligibleAttackOrderTargets("plain", OrderTarget.ForGroup(UnitTypeIds.Grunt, "red", "visible")));
        Assert.Empty(queries.EligibleAttackOrderTargets("plain", OrderTarget.ForGroup(UnitTypeIds.Grunt, "red")));
        var observed = FogOfWar.Observe(state, "blue");
        Assert.Contains(observed.Controllers, c => c.Token == visibleToken);
        Assert.DoesNotContain(observed.Controllers, c => c.Token == hiddenToken);
        state.ActiveToken = hiddenToken; state.ResolvingTokenUnitIds = ["hidden"];
        Assert.Null(FogOfWar.ObserveEvent(state, "blue", new("TokenDrawn") { Token = hiddenToken }));
        state.ActiveToken = visibleToken; state.ResolvingTokenUnitIds = ["visible"];
        Assert.NotNull(FogOfWar.ObserveEvent(state, "blue", new("TokenDrawn") { Token = visibleToken }));
    }

}
