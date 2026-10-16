using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Web.Tests;

public sealed class OrdersPlaygroundTests
{
    private static readonly OrderAwareAutomatedProvider Behavior = new();

    [Theory]
    [InlineData(UnitTypeIds.Grunt, OrderKind.DoNothing)]
    [InlineData(UnitTypeIds.SkeletonArcher, OrderKind.HoldPosition)]
    [InlineData(UnitTypeIds.Zombie, OrderKind.MoveTo)]
    [InlineData(UnitTypeIds.Goblin, OrderKind.MoveTo)]
    public void OrderFixtureLoadsSparseOrderAndCreatesIndependentState(string typeId, OrderKind kind)
    {
        var definition = Definition(typeId);
        var state = GameEngine.CreateGame(definition);
        var another = GameEngine.CreateGame(definition);
        Assert.Equal(kind, Assert.Single(state.Orders).Kind);
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(new ActivationToken(typeId, "red")));
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Barbarian, "blue")));
        state.Orders.Clear();
        Assert.Equal(kind, Assert.Single(another.Orders).Kind);
        Assert.Equal(kind, Assert.Single(definition.Orders!).Kind);
    }

    [Fact]
    public void GruntDoesNothingDespiteImmediateAttackOpportunity()
    {
        var (state, request, _) = Begin(UnitTypeIds.Grunt);
        Assert.Contains(request.Candidates, candidate => candidate.Kind == ActivationChoiceKind.Stay);
        var moveChoice = new Capture(Behavior);
        var action = GameEngine.Advance(state, moveChoice, new FixedRandom(UnitTypeIds.Grunt), false);
        Assert.Equal("stay", moveChoice.Key);
        Assert.Contains(action.NextInput!.Candidates, candidate => candidate.Action == UnitAction.NormalAttack);
        var actionChoice = new Capture(Behavior);
        _ = GameEngine.Advance(action.State, actionChoice, new FixedRandom(UnitTypeIds.Grunt), false);
        Assert.Equal("end-turn", actionChoice.Key);
    }

    [Fact]
    public void HoldingArcherStaysAndThenUsesItsAvailableAttack()
    {
        var (state, request, _) = Begin(UnitTypeIds.SkeletonArcher);
        var moveChoice = new Capture(Behavior);
        var action = GameEngine.Advance(state, moveChoice, new FixedRandom(UnitTypeIds.SkeletonArcher), false);
        Assert.Equal("stay", moveChoice.Key);
        var attack = Assert.Single(action.NextInput!.Candidates, candidate => candidate.Action == UnitAction.NormalAttack);
        var actionChoice = new Capture(Behavior);
        _ = GameEngine.Advance(action.State, actionChoice, new FixedRandom(UnitTypeIds.SkeletonArcher), false);
        Assert.Equal(attack.Key, actionChoice.Key);
    }

    [Fact]
    public void ZombieTakesTheShortestDetourAroundTheWall()
    {
        var (state, request, _) = Begin(UnitTypeIds.Zombie);
        var choice = new Capture(Behavior);
        _ = GameEngine.Advance(state, choice, new FixedRandom(UnitTypeIds.Zombie), false);
        var move = Assert.Single(request.Candidates, candidate => candidate.Key == choice.Key);

        Assert.Equal(new Cell(3, 2), move.Destination);
        Assert.Equal(new Cell[] { new(4, 1), new(3, 1), new(3, 2) }, move.Path);
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(new(2, 1), new(3, 1)));
        Assert.DoesNotContain(request.Candidates, candidate => candidate.Destination == new Cell(2, 1));
    }

    [Fact]
    public void GoblinMovesPastAnEarlierAttackPositionThenUsesNormalBackAway()
    {
        var (state, request, _) = Begin(UnitTypeIds.Goblin);
        var moveChoice = new Capture(Behavior);
        var result = GameEngine.Advance(state, moveChoice, new FixedRandom(UnitTypeIds.Goblin), false);
        var move = Assert.Single(request.Candidates, candidate => candidate.Key == moveChoice.Key);
        Assert.Equal(new Cell(4, 2), move.Destination);
        Assert.Equal(new Cell[] { new(0, 2), new(1, 2), new(2, 2), new(3, 2), new(4, 2) }, move.Path);

        var random = new FixedRandom(UnitTypeIds.Goblin);
        var attack = Assert.Single(result.NextInput!.Candidates, candidate => candidate.Action == UnitAction.NormalAttack);
        var attackChoice = new Capture(Behavior);
        result = GameEngine.Advance(result.State, attackChoice, random, false);
        Assert.Equal(attack.Key, attackChoice.Key);
        while (result.NextInput is { IsMoveAfterAttack: false })
            result = GameEngine.Advance(result.State, Behavior, random, false);

        Assert.True(result.NextInput!.IsMoveAfterAttack);
        var copy = System.Text.Json.JsonSerializer.Deserialize<GameState>(
            System.Text.Json.JsonSerializer.Serialize(result.State))!;
        var orderedChoice = new Capture(Behavior);
        var backedAway = GameEngine.Advance(result.State, orderedChoice, random, false);
        var normalChoice = new Capture(new DefaultAutomatedProvider());
        _ = GameEngine.Advance(copy, normalChoice, random, false);
        Assert.Equal(normalChoice.Key, orderedChoice.Key);
        Assert.Equal("4,3", orderedChoice.Key);
        Assert.Contains(backedAway.Events, occurrence => occurrence.Kind == "MovementCompleted" && occurrence.IsMoveAfterAttack);
    }

    [Fact]
    public void BrowserProjectionCarriesOrdersInCurrentAndResolutionState()
    {
        var state = GameEngine.CreateGame(Definition(UnitTypeIds.Zombie));
        var result = new EngineResult(state, [new("Notice")], null) { ResolutionSteps = [new(0, state)] };
        var presentation = BrowserProjection.Create(result);
        Assert.Equal(state.Orders, presentation.Orders);
        Assert.Equal(state.Orders, Assert.Single(presentation.ResolutionSteps).Orders);
    }

    // Each rules question gets a compact board; no dependency on the catalog's Orders Playground.
    private static ScenarioDefinition Definition(string typeId)
    {
        var (board, hero, actor, order) = typeId switch
        {
            UnitTypeIds.Grunt => (Map(5, 3), At(1, 1), At(2, 1), new InitialGroupOrder(typeId, "red", OrderKind.DoNothing)),
            UnitTypeIds.SkeletonArcher => (Map(7, 3), At(1, 1), At(4, 1), new InitialGroupOrder(typeId, "red", OrderKind.HoldPosition)),
            UnitTypeIds.Zombie => (Map(6, 4, edges: [.. Vertical(2, 1, 2, EdgeKind.Wall)]), At(0, 0), At(4, 1),
                new InitialGroupOrder(typeId, "red", OrderKind.MoveTo, new(0, 3))),
            UnitTypeIds.Goblin => (Map(7, 4), At(4, 1), At(0, 2), new InitialGroupOrder(typeId, "red", OrderKind.MoveTo, new(4, 2))),
            _ => throw new ArgumentException("Unknown Order test fixture.", nameof(typeId))
        };
        return Define(board,
            [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, hero),
             Group(typeId, "red", ControllerKind.Automated, actor)], orders: [order]);
    }

    private static (GameState State, DecisionRequest Request, string UnitId) Begin(string typeId)
    {
        var random = new FixedRandom(typeId);
        var started = GameEngine.StartRound(GameEngine.CreateGame(Definition(typeId)), random, false);
        Assert.Equal(new ActivationToken(typeId, "red"), started.State.ActiveToken);
        var unit = Assert.Single(started.State.Units, candidate => candidate.TypeId == typeId && candidate.SideId == "red");
        if (started.NextInput!.Kind != DecisionKind.SelectUnit)
            return (started.State, started.NextInput, unit.Id);
        var selected = GameEngine.Advance(started.State, new Choice(started.NextInput.Candidates.Single().Key), random, false);
        return (selected.State, selected.NextInput!, unit.Id);
    }

    private sealed class Capture(IDecisionProvider inner) : IDecisionProvider
    {
        public string? Key { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries) =>
            Key = inner.Choose(request, queries);
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class FixedRandom(string typeId) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(token => token.TypeId == typeId && token.SideId == "red") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}