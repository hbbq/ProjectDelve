using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class OrdersPlaygroundTests
{
    private static readonly OrderAwareAutomatedProvider Behavior = new();

    [Fact]
    public void CatalogDefinitionIsFreshAndCarriesExactSparseOrdersAndControllers()
    {
        var description = Assert.Single(PlaytestScenarios.Catalog, scenario => scenario.Id == "orders-playground");
        Assert.Equal("Orders Playground", description.Name);
        var definition = PlaytestScenarios.Definition(description.Id);
        var state = PlaytestScenarios.Create(description.Id);

        Assert.Equal((10, 10), (state.Physical.Board.Width, state.Physical.Board.Height));
        Assert.Equal(new[]
        {
            new GroupOrder(new(UnitTypeIds.Grunt, "red"), OrderKind.DoNothing),
            new GroupOrder(new(UnitTypeIds.SkeletonArcher, "red"), OrderKind.HoldPosition),
            new GroupOrder(new(UnitTypeIds.Zombie, "red"), OrderKind.MoveTo, new(5, 9)),
            new GroupOrder(new(UnitTypeIds.Goblin, "red"), OrderKind.MoveTo, new(5, 6))
        }, state.Orders);
        Assert.All(state.Controllers.Where(binding => binding.Token.SideId == "red"),
            binding => Assert.Equal(ControllerKind.Automated, binding.Controller));
        Assert.All(state.Controllers.Where(binding => binding.Token.SideId == "blue"),
            binding => Assert.Equal(ControllerKind.Human, binding.Controller));
        Assert.Equal(4, state.Controllers.Count(binding => binding.Token.SideId == "red"));
        Assert.Equal(2, state.Controllers.Count(binding => binding.Token.SideId == "blue"));

        var another = PlaytestScenarios.Create(description.Id);
        state.Orders.Clear();
        Assert.Equal(4, another.Orders.Count);
        Assert.Equal(definition.Orders, PlaytestScenarios.Definition(description.Id).Orders);
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

        Assert.Equal(new Cell(8, 7), move.Destination);
        Assert.Equal(new Cell[] { new(9, 6), new(8, 6), new(8, 7) }, move.Path);
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(new(7, 6), new(8, 6)));
        Assert.DoesNotContain(request.Candidates, candidate => candidate.Destination == new Cell(7, 6));
    }

    [Fact]
    public void GoblinMovesPastAnEarlierAttackPositionThenUsesNormalBackAway()
    {
        var (state, request, _) = Begin(UnitTypeIds.Goblin);
        var moveChoice = new Capture(Behavior);
        var result = GameEngine.Advance(state, moveChoice, new FixedRandom(UnitTypeIds.Goblin), false);
        var move = Assert.Single(request.Candidates, candidate => candidate.Key == moveChoice.Key);
        Assert.Equal(new Cell(5, 6), move.Destination);
        Assert.Equal(new Cell[] { new(1, 6), new(2, 6), new(3, 6), new(4, 6), new(5, 6) }, move.Path);

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
        Assert.Equal("5,7", orderedChoice.Key);
        Assert.Contains(backedAway.Events, occurrence => occurrence.Kind == "MovementCompleted" && occurrence.IsMoveAfterAttack);
    }

    [Fact]
    public void BrowserProjectionCarriesOrdersInCurrentAndResolutionState()
    {
        var state = PlaytestScenarios.Create("orders-playground");
        var result = new EngineResult(state, [new("Notice")], null) { ResolutionSteps = [new(0, state)] };
        var presentation = BrowserProjection.Create(result);
        Assert.Equal(state.Orders, presentation.Orders);
        Assert.Equal(state.Orders, Assert.Single(presentation.ResolutionSteps).Orders);
    }

    private static (GameState State, DecisionRequest Request, string UnitId) Begin(string typeId)
    {
        var random = new FixedRandom(typeId);
        var started = GameEngine.StartRound(PlaytestScenarios.Create("orders-playground"), random, false);
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