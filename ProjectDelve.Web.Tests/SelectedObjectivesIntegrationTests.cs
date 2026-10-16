using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

// Exact coordinates and identities belong to this mechanic fixture, never a catalog layout.
public sealed class SelectedObjectivesIntegrationTests
{
    private static GameState Setup() => GameEngine.CreateGame(new ScenarioDefinition
    {
        Board = new() { Width = 6, Height = 4 }, UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "Blue", new(0, 0), UnitId: "blue-one"),
            new(UnitTypeIds.Grunt, "Blue", new(0, 2), UnitId: "blue-two"),
            new(UnitTypeIds.Grunt, "Red", new(5, 3), UnitId: "red-one")],
        Agency = [new(UnitTypeIds.Grunt, "Blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "Red", ControllerKind.Human)],
        Objectives = [new("Blue", ScenarioObjective.ReachArea)
            { Count = 0, Selectors = [UnitSelector.SideAtStart("Blue")], Area = new(3, 0, 2, 2) },
            new("Red", ScenarioObjective.DefeatUnits) { Count = 1, Selectors = [UnitSelector.SideAtStart("Blue")] }],
        Orders = [new(UnitTypeIds.Grunt, "Red", OrderKind.Attack, Target: OrderTarget.ForSide("Blue"))]
    });

    [Fact]
    public void ProjectionExplainsObjectivesAndDetachesFrozenIdentityAndDefeatEvidence()
    {
        var state = Setup();
        var browser = BrowserProjection.Create(new(state, [], null));
        Assert.Contains(browser.Objectives, objective => objective.Name == "Reach Area" &&
            objective.Meaning.Contains("(3, 0), 2 x 2") && objective.Meaning.Contains("simultaneously") &&
            objective.Meaning.Contains("fixed initial Units"));
        Assert.Contains(browser.Objectives, objective => objective.Name == "Defeat Units" &&
            objective.Meaning.Contains("At least 1") && objective.Meaning.Contains("actually be defeated"));
        state.DefeatedUnitIds.Add("blue-one");
        state.Physical.Figures.RemoveAll(figure => figure.Id is "blue-one" or "blue-two");
        state.Units.Add(state.Units[0] with { Id = "later-created" });
        var observation = GambitProjection.Observation(state, "Red", new Dictionary<ActivationToken, string>());
        Assert.Equal(new[] { "blue-one" }, observation.DefeatedUnitIds);
        Assert.Equal(new[] { "blue-one", "blue-two" }, observation.ObjectiveStartUnitIds["Blue"]);
        state.DefeatedUnitIds.Clear(); state.ObjectiveStartUnitIds.Clear();
        Assert.Single(observation.DefeatedUnitIds);
        Assert.Equal(new[] { "blue-one", "blue-two" }, observation.ObjectiveStartUnitIds["Blue"]);
        var status = new GambitStatus(1, "session", "binding", "Red", "ready", null, null) { Objective = state.Objectives[1] };
        Assert.Contains("Defeat Units", status.ObjectiveMeaning);
        Assert.Contains("At least 1", status.ObjectiveMeaning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReachAllRequiresSimultaneousOccupancyAndACompletedActivation(bool allInside)
    {
        var state = Setup();
        Position(state, "blue-one", new(3, 0));
        if (allInside) Position(state, "blue-two", new(4, 1));
        var dice = new Dice("Blue");
        var result = GameEngine.StartRound(state, dice, false);
        Assert.Null(result.State.Completion);
        result = GameEngine.Advance(result.State, new Choice("blue-one"), dice, false);
        Assert.Null(result.State.Completion);
        result = GameEngine.Advance(result.State, new Choice("stay"), dice, false);
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted");
        Assert.Equal(allInside, result.State.IsCompleted);
        if (allInside)
        {
            Assert.Equal(new[] { "Blue" }, result.State.Completion!.AchievedSideIds);
            var finish = result.Events.FindIndex(e => e.Kind == "ActivationCompleted");
            Assert.All(result.ResolutionSteps.Where(step => step.EventIndex < finish), step => Assert.Null(step.StateAfter.Completion));
            Assert.Null(result.NextInput);
            Assert.Throws<InvalidOperationException>(() => GameEngine.StartRound(result.State, dice, false));
            Assert.Throws<InvalidOperationException>(() => GameEngine.Advance(result.State, new Choice("stay"), dice, false));
        }
    }

    [Fact]
    public void DefeatCompletesOnlyAfterDiceAndActivationAndProjectsTheWinningSide()
    {
        var state = Setup(); Position(state, "blue-one", new(4, 3));
        var dice = new Dice("Red");
        var result = GameEngine.StartRound(state, dice, false);
        result = GameEngine.Advance(result.State, new Choice("stay"), dice, false);
        result = GameEngine.Advance(result.State, new Choice("attack:blue-one"), dice, false);
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
        Assert.Null(result.State.Completion);
        while (result.NextInput?.Kind == DecisionKind.RollDice)
            result = GameEngine.Advance(result.State, new Choice("roll-dice"), dice, false);
        Assert.Contains("blue-one", result.State.DefeatedUnitIds);
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted");
        Assert.Equal(new[] { "Red" }, result.State.Completion!.AchievedSideIds);
        var browser = BrowserProjection.Create(result);
        Assert.Equal(result.State.Completion, browser.Completion);
        Assert.Contains(browser.Events, e => e.Text.Contains("Victory: Red"));
    }

    private static void Position(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(figure => figure.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice(string preferredSide) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(token => token.SideId == preferredSide) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
