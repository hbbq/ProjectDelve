using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GenombrottetTests
{
    [Fact]
    public void ImportedAuthoredMapAndPlacementsArePreservedWithRequestedObjectivesAndOrders()
    {
        var imported = ScenarioDefinitionTransport.Decode(GenombrottetScenario.Export);
        var definition = PlaytestScenarios.Definition("genombrottet");
        Assert.Equal(imported.Board.Cells, definition.Board.Cells);
        Assert.Equal(imported.Board.Edges, definition.Board.Edges);
        Assert.Equal(imported.Units, definition.Units);
        Assert.Equal(imported.UnitTypeIds, definition.UnitTypeIds);
        Assert.Equal(imported.Agency, definition.Agency);
        Assert.Equal((15, 10), (definition.Board.Width, definition.Board.Height));
        Assert.Equal(22, definition.Board.Cells.Count);
        Assert.Equal(34, definition.Board.Edges.Count);
        Assert.Equal(2, definition.Board.Edges.Count(edge => edge.Kind == EdgeKind.ClosedDoor));
        Assert.Equal(9, definition.Board.Cells.Count(cell => cell.Terrain == TerrainKind.Water));
        Assert.Equal(3, definition.Board.Cells.Count(cell => cell.Terrain == TerrainKind.StoneFloorWithTable));
        Assert.Equal(10, definition.Units.Count);
        Assert.Equal(new Cell(6, 4), Assert.Single(definition.Units, unit => unit.UnitTypeId == UnitTypeIds.Goblin).Anchor);
        Assert.Equal(3, definition.Units.Count(unit => unit.SideId == "side-1"));
        Assert.All(definition.Agency, agency => Assert.Equal(agency.SideId == "side-1" ? ControllerKind.Human : ControllerKind.Automated, agency.Controller));
        var blue = Assert.Single(definition.Objectives!, objective => objective.SideId == "side-1");
        Assert.Equal(ScenarioObjective.ReachArea, blue.Objective);
        Assert.Equal(0, blue.Count);
        Assert.Equal(new ObjectiveArea(13, 5, 2, 5), blue.Area);
        Assert.Equal(UnitSelector.SideAtStart("side-1"), Assert.Single(blue.Selectors));
        var red = Assert.Single(definition.Objectives!, objective => objective.SideId == "side-2");
        Assert.Equal(ScenarioObjective.DefeatUnits, red.Objective);
        Assert.Equal(1, red.Count);
        Assert.Equal(blue.Selectors.ToArray(), red.Selectors.ToArray());
        var state = PlaytestScenarios.Create("genombrottet");
        var description = Assert.Single(PlaytestScenarios.Catalog, scenario => scenario.Id == "genombrottet");
        Assert.Equal(210, description.SideHues["side-1"]);
        Assert.Equal(0, description.SideHues["side-2"]);
        Assert.All(description.ControllerAssignments, assignment => Assert.Equal(assignment.SideId == "side-1" ? "Human" : "Default Behavior", assignment.Controller));
        using var game = new PlaytestGame(new Dice(UnitTypeIds.Barbarian), state);
        Assert.All(game.Snapshot().ControllerAssignments!, assignment => Assert.Equal(assignment.SideId == "side-1" ? "Human" : "Default Behavior", assignment.Controller));
        Assert.Equal(new[]
        {
            new GroupOrder(new(UnitTypeIds.Zombie, "side-2"), OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new GroupOrder(new(UnitTypeIds.Troll, "side-2"), OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new GroupOrder(new(UnitTypeIds.SkeletonArcher, "side-2"), OrderKind.HoldPosition),
            new GroupOrder(new(UnitTypeIds.Grunt, "side-2"), OrderKind.HoldPosition)
        }, state.Orders);
        Assert.DoesNotContain(state.Orders, order => order.Token.TypeId == UnitTypeIds.Goblin);
        var roundtrip = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(definition.Units, roundtrip.Units);
        Assert.Equal(definition.Board.Cells, roundtrip.Board.Cells);
        Assert.Equal(definition.Board.Edges, roundtrip.Board.Edges);
        Assert.Equal(definition.Orders, roundtrip.Orders);
        Assert.Equal(blue.Area, roundtrip.Objectives![0].Area);
        Assert.Equal(blue.Selectors.ToArray(), roundtrip.Objectives[0].Selectors.ToArray());
    }

    [Fact]
    public void ProjectionsShowConcreteObjectivesAndRetainObjectiveIdentityEvidence()
    {
        var state = PlaytestScenarios.Create("genombrottet");
        var browser = BrowserProjection.Create(new(state, [], null));
        Assert.Equal(state.Orders, browser.Orders);
        Assert.Contains(browser.Objectives, objective => objective.Name == "Reach Area" && objective.Meaning.Contains("(13, 5), 2 x 5") && objective.Meaning.Contains("simultaneously") && objective.Meaning.Contains("fixed initial Units"));
        Assert.Contains(browser.Objectives, objective => objective.Name == "Defeat Units" && objective.Meaning.Contains("At least 1") && objective.Meaning.Contains("actually be defeated"));
        var hero = state.Units.First(unit => unit.SideId == "side-1");
        state.DefeatedUnitIds.Add(hero.Id);
        state.Physical.Figures.RemoveAll(figure => figure.Id == hero.Id);
        var absent = state.Units.Last(unit => unit.SideId == "side-1");
        state.Physical.Figures.RemoveAll(figure => figure.Id == absent.Id);
        state.Units.Add(hero with { Id = "later-created" });
        var observation = GambitProjection.Observation(state, "side-2", new Dictionary<ActivationToken, string>());
        Assert.Equal(new[] { hero.Id }, observation.DefeatedUnitIds);
        Assert.DoesNotContain(absent.Id, observation.DefeatedUnitIds);
        Assert.DoesNotContain("later-created", observation.ObjectiveStartUnitIds["side-1"]);
        Assert.Equal(3, observation.ObjectiveStartUnitIds["side-1"].Count);
        state.DefeatedUnitIds.Clear();
        state.ObjectiveStartUnitIds.Clear();
        Assert.Single(observation.DefeatedUnitIds);
        Assert.Equal(3, observation.ObjectiveStartUnitIds["side-1"].Count);
        var status = new GambitStatus(1, "session", "binding", "side-2", "ready", null, null) { Objective = state.Objectives[1] };
        Assert.Contains("Defeat Units", status.ObjectiveMeaning);
        Assert.Contains("At least 1", status.ObjectiveMeaning);
    }
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void BlueWinsOnlyWithAllThreeHeroesAtCompletedActivation(int inside, bool wins)
    {
        var state = PlaytestScenarios.Create("genombrottet");
        var heroes = state.Units.Where(unit => unit.SideId == "side-1").ToArray();
        for (var i = 0; i < inside; i++) Position(state, heroes[i].Id, new(13, 5 + i));
        var dice = new Dice(UnitTypeIds.Barbarian);
        var result = GameEngine.StartRound(state, dice, false);
        Assert.Null(result.State.Completion);
        result = Finish(result, dice);
        Assert.Equal(wins, result.State.IsCompleted);
        if (wins) Assert.Equal(new[] { "side-1" }, result.State.Completion!.AchievedSideIds);
    }

    [Fact]
    public void RedWinsWhenAnAttackActuallyDefeatsAnInitialHeroAtActivationBoundary()
    {
        var state = PlaytestScenarios.Create("genombrottet");
        var hero = state.Units.First(unit => unit.TypeId == UnitTypeIds.Wizard);
        var index = state.Units.IndexOf(hero);
        state.Units[index] = hero with { CurrentHp = 1 };
        Position(state, hero.Id, new(12, 2));
        for (var i = 0; i < state.Controllers.Count; i++) state.Controllers[i] = state.Controllers[i] with { Controller = ControllerKind.Human };
        var dice = new Dice(UnitTypeIds.Troll);
        var result = GameEngine.StartRound(state, dice, false);
        result = GameEngine.Advance(result.State, new Choice("stay"), dice, false);
        var attack = Assert.Single(result.NextInput!.Candidates, candidate => candidate.TargetId == hero.Id && candidate.Kind == ActivationChoiceKind.Action);
        result = GameEngine.Advance(result.State, new Choice(attack.Key), dice, false);
        while (result.NextInput?.Kind == DecisionKind.RollDice)
            result = GameEngine.Advance(result.State, new Choice("roll-dice"), dice, false);
        Assert.Contains(hero.Id, result.State.DefeatedUnitIds);
        if (!result.State.IsCompleted) result = Finish(result, dice);
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted");
        Assert.Equal(new[] { "side-2" }, result.State.Completion!.AchievedSideIds);
    }

    private static void Position(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(figure => figure.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }
    private static EngineResult Finish(EngineResult result, Dice dice)
    {
        for (var i = 0; i < 10 && result.NextInput is not null && !result.State.IsCompleted; i++)
        {
            var choice = result.NextInput.Candidates.FirstOrDefault(candidate => candidate.Key == "end-turn")
                ?? result.NextInput.Candidates.First(candidate => candidate.Key == "stay");
            result = GameEngine.Advance(result.State, new Choice(choice.Key), dice, false);
            if (result.Events.Any(e => e.Kind == "ActivationCompleted")) break;
        }
        return result;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice(string typeId) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(token => token.TypeId == typeId) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}