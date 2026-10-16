using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Web.Tests;

public sealed class InitialOrdersScenariosTests
{
    private static readonly OrderAwareAutomatedProvider Behavior = new();

    [Fact]
    public void DoorAndAttackFixtureLoadsStableTargetsControllersAndCanonicalDoor()
    {
        var definition = FixtureDefinition("attack-door");
        var state = GameEngine.CreateGame(definition);

        Assert.Equal((9, 7), (state.Physical.Board.Width, state.Physical.Board.Height));
        Assert.Equal("assault-hero", Assert.Single(definition.Units, unit => unit.UnitId == "assault-hero").UnitId);
        Assert.Contains(state.Units, unit => unit.Id == "assault-hero" && unit.TypeId == UnitTypeIds.Barbarian);
        Assert.Contains(state.Units, unit => unit.Id == "assault-rogue" && unit.TypeId == UnitTypeIds.Rogue);
        Assert.Equal(EdgeKind.ClosedDoor, state.Physical.Board.EdgeBetween(new(5, 4), new(6, 4)));
        Assert.Equal(new[]
        {
            new GroupOrder(new(UnitTypeIds.Grunt, "red"), OrderKind.Attack,
                Target: OrderTarget.ForUnit("assault-hero")),
            new GroupOrder(new(UnitTypeIds.SkeletonArcher, "red"), OrderKind.Attack,
                Target: OrderTarget.ForSide("blue")),
            new GroupOrder(new(UnitTypeIds.Zombie, "red"), OrderKind.InteractWith,
                Target: OrderTarget.ForDoor(new(5, 4), new(6, 4)))
        }, state.Orders);
        Assert.All(state.Controllers.Where(binding => binding.Token.SideId == "red"),
            binding => Assert.Equal(ControllerKind.Automated, binding.Controller));
        Assert.All(state.Controllers.Where(binding => binding.Token.SideId == "blue"),
            binding => Assert.Equal(ControllerKind.Human, binding.Controller));
    }

    [Fact]
    public void EscortFixtureLoadsMoveProtectAndRetreatGroups()
    {
        var definition = FixtureDefinition("escort-retreat");
        var state = GameEngine.CreateGame(definition);

        Assert.Equal((11, 8), (state.Physical.Board.Width, state.Physical.Board.Height));
        Assert.Equal("escort-leader", Assert.Single(definition.Units, unit => unit.UnitId == "escort-leader").UnitId);
        Assert.Contains(state.Units, unit => unit.Id == "escort-leader" && unit.TypeId == UnitTypeIds.Grunt);
        Assert.Equal(new[]
        {
            new GroupOrder(new(UnitTypeIds.Grunt, "red"), OrderKind.MoveTo, new(3, 3)),
            new GroupOrder(new(UnitTypeIds.Zombie, "red"), OrderKind.Protect,
                Target: OrderTarget.ForUnit("escort-leader")),
            new GroupOrder(new(UnitTypeIds.Goblin, "red"), OrderKind.Retreat)
        }, state.Orders);
        Assert.Equal(3, state.Controllers.Count(binding => binding.Token.SideId == "red"));
        Assert.Equal(2, state.Controllers.Count(binding => binding.Token.SideId == "blue"));
    }

    [Fact]
    public void AssaultDoorOrderApproachesThenChoosesTheExistingDoorInteraction()
    {
        var (state, request) = Begin("attack-door", UnitTypeIds.Zombie);
        var moveChoice = new Capture(Behavior);
        var moved = GameEngine.Advance(state, moveChoice, new FixedRandom(UnitTypeIds.Zombie), false);
        var move = Assert.Single(request.Candidates, candidate => candidate.Key == moveChoice.Key);
        Assert.Equal(new Cell(6, 4), move.Destination);

        var interaction = Assert.Single(moved.NextInput!.Candidates, candidate => candidate.Door is not null);
        var interactionChoice = new Capture(Behavior);
        _ = GameEngine.Advance(moved.State, interactionChoice, new FixedRandom(UnitTypeIds.Zombie), false);
        Assert.Equal(interaction.Key, interactionChoice.Key);
        Assert.Equal(DoorTarget.Canonical(new(5, 4), new(6, 4)),
            DoorTarget.Canonical(interaction.Door!.A, interaction.Door.B));
    }

    [Fact]
    public void EscortMovementProtectResponseAndRetreatAreObservableAndDeterministic()
    {
        var (moveState, moveRequest) = Begin("escort-retreat", UnitTypeIds.Grunt);
        var moveChoice = new Capture(Behavior);
        _ = GameEngine.Advance(moveState, moveChoice, new FixedRandom(UnitTypeIds.Grunt), false);
        Assert.Equal(new Cell(6, 3), Assert.Single(moveRequest.Candidates,
            candidate => candidate.Key == moveChoice.Key).Destination);

        var protectSetup = Fixture("escort-retreat");
        var leaderFigure = protectSetup.Physical.Figures.Single(figure => figure.Id == "escort-leader");
        protectSetup.Physical.Figures[protectSetup.Physical.Figures.IndexOf(leaderFigure)] =
            leaderFigure with { Position = new(6, 3) };
        var (protectState, protectRequest) = Begin(protectSetup, UnitTypeIds.Zombie);
        var protectChoice = new Capture(Behavior);
        _ = GameEngine.Advance(protectState, protectChoice, new FixedRandom(UnitTypeIds.Zombie), false);
        var protectMove = Assert.Single(protectRequest.Candidates, candidate => candidate.Key == protectChoice.Key);
        Assert.Equal(new Cell(8, 4), protectMove.Destination);

        var (retreatState, retreatRequest) = Begin("escort-retreat", UnitTypeIds.Goblin);
        var retreatChoice = new Capture(Behavior);
        _ = GameEngine.Advance(retreatState, retreatChoice, new FixedRandom(UnitTypeIds.Goblin), false);
        var retreatMove = Assert.Single(retreatRequest.Candidates, candidate => candidate.Key == retreatChoice.Key);
        Assert.Equal(new Cell(5, 1), retreatMove.Destination);
    }

    [Theory]
    [InlineData("attack-door", UnitTypeIds.Zombie)]
    [InlineData("escort-retreat", UnitTypeIds.Grunt)]
    [InlineData("escort-retreat", UnitTypeIds.Zombie)]
    [InlineData("escort-retreat", UnitTypeIds.Goblin)]
    public void DefaultBehaviorPreviewUsesTheSameOrderAwareFirstChoice(string id, string typeId)
    {
        var (state, request) = Begin(id, typeId);
        var preview = GameEngine.PreviewDefaultBehaviorActivation(state, request.UnitId!);
        var expected = new Capture(Behavior);
        _ = GameEngine.Advance(state, expected, new FixedRandom(typeId), false);

        Assert.Equal(expected.Key, Assert.Single(preview.Steps.Take(1)).CandidateKey);
    }
    [Fact]
    public void CompletedOrDefeatedScenarioTargetsFallBackToNormalWithoutClearingOrders()
    {
        var attackOrdered = Fixture("attack-door");
        DefeatAndRemove(attackOrdered, "assault-hero");
        var attackNormal = Fixture("attack-door");
        DefeatAndRemove(attackNormal, "assault-hero");
        attackNormal.Orders.RemoveAll(order => order.Token == new ActivationToken(UnitTypeIds.Grunt, "red"));
        AssertSameFirstChoice(attackOrdered, attackNormal, UnitTypeIds.Grunt);
        Assert.Contains(attackOrdered.Orders, order => order.Kind == OrderKind.Attack && order.Target?.UnitId == "assault-hero");

        var protectOrdered = Fixture("escort-retreat");
        DefeatAndRemove(protectOrdered, "escort-leader");
        var protectNormal = Fixture("escort-retreat");
        DefeatAndRemove(protectNormal, "escort-leader");
        protectNormal.Orders.RemoveAll(order => order.Token == new ActivationToken(UnitTypeIds.Zombie, "red"));
        AssertSameFirstChoice(protectOrdered, protectNormal, UnitTypeIds.Zombie);
        Assert.Contains(protectOrdered.Orders, order => order.Kind == OrderKind.Protect && order.Target?.UnitId == "escort-leader");

        var doorOrdered = Fixture("attack-door");
        OpenAssaultDoor(doorOrdered);
        var doorNormal = Fixture("attack-door");
        OpenAssaultDoor(doorNormal);
        doorNormal.Orders.RemoveAll(order => order.Token == new ActivationToken(UnitTypeIds.Zombie, "red"));
        AssertSameFirstChoice(doorOrdered, doorNormal, UnitTypeIds.Zombie);
        Assert.Contains(doorOrdered.Orders, order => order.Kind == OrderKind.InteractWith);
    }
    [Theory]
    [InlineData("attack-door")]
    [InlineData("escort-retreat")]
    public void ProjectionAndDetachedCopyPreserveStandingOrders(string id)
    {
        var state = Fixture(id);
        var copy = System.Text.Json.JsonSerializer.Deserialize<GameState>(
            System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Orders, copy.Orders);
        Assert.NotSame(state.Orders, copy.Orders);

        var result = new EngineResult(state, [new("Notice")], null) { ResolutionSteps = [new(0, copy)] };
        var presentation = BrowserProjection.Create(result);
        Assert.Equal(state.Orders, presentation.Orders);
        Assert.Equal(copy.Orders, Assert.Single(presentation.ResolutionSteps).Orders);
    }

    // Deliberately small mechanic fixtures, independent of every editable catalog scenario.
    private static GameState Fixture(string id) => GameEngine.CreateGame(FixtureDefinition(id));
    private static ScenarioDefinition FixtureDefinition(string id) => id switch
    {
        "attack-door" => Define(Map(9, 7, edges: [
            .. Vertical(5, 0, 3, EdgeKind.Wall), .. Vertical(5, 5, 6, EdgeKind.Wall),
            Edge(5, 4, EdgeDirection.Right, EdgeKind.ClosedDoor)]),
            [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 2, unitId: "assault-hero")),
             Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 5, unitId: "assault-rogue")),
             Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(8, 2)),
             Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(8, 6)),
             Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 4))],
            orders: [new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForUnit("assault-hero")),
                new(UnitTypeIds.SkeletonArcher, "red", OrderKind.Attack, Target: OrderTarget.ForSide("blue")),
                new(UnitTypeIds.Zombie, "red", OrderKind.InteractWith, Target: OrderTarget.ForDoor(new(5, 4), new(6, 4)))]),
        "escort-retreat" => Define(Map(11, 8),
            [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 5)),
             Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 6)),
             Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(9, 3, unitId: "escort-leader")),
             Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(10, 4)),
             Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(5, 5), At(6, 6))],
            orders: [new(UnitTypeIds.Grunt, "red", OrderKind.MoveTo, new(3, 3)),
                new(UnitTypeIds.Zombie, "red", OrderKind.Protect, Target: OrderTarget.ForUnit("escort-leader")),
                new(UnitTypeIds.Goblin, "red", OrderKind.Retreat)]),
        _ => throw new ArgumentException("Unknown Order test fixture.", nameof(id))
    };

    private static void AssertSameFirstChoice(GameState ordered, GameState normal, string typeId)
    {
        var (orderedState, _) = Begin(ordered, typeId);
        var orderedChoice = new Capture(Behavior);
        _ = GameEngine.Advance(orderedState, orderedChoice, new FixedRandom(typeId), false);
        var (normalState, _) = Begin(normal, typeId);
        var normalChoice = new Capture(new DefaultAutomatedProvider());
        _ = GameEngine.Advance(normalState, normalChoice, new FixedRandom(typeId), false);
        Assert.Equal(normalChoice.Key, orderedChoice.Key);
    }

    private static void DefeatAndRemove(GameState state, string unitId)
    {
        var index = state.Units.FindIndex(unit => unit.Id == unitId);
        state.Units[index] = state.Units[index] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(figure => figure.Id == unitId);
    }

    private static void OpenAssaultDoor(GameState state)
    {
        var index = state.Physical.Board.Edges.FindIndex(edge =>
            DoorTarget.Canonical(edge.A, edge.B) == DoorTarget.Canonical(new(5, 4), new(6, 4)));
        state.Physical.Board.Edges[index] = state.Physical.Board.Edges[index] with { Kind = EdgeKind.OpenDoor };
    }
    private static (GameState State, DecisionRequest Request) Begin(string scenarioId, string typeId) =>
        Begin(Fixture(scenarioId), typeId);

    private static (GameState State, DecisionRequest Request) Begin(GameState state, string typeId)
    {
        var random = new FixedRandom(typeId);
        var started = GameEngine.StartRound(state, random, false);
        if (started.NextInput!.Kind != DecisionKind.SelectUnit) return (started.State, started.NextInput);
        var selected = GameEngine.Advance(started.State,
            new Choice(started.NextInput.Candidates.OrderBy(candidate => candidate.Key, StringComparer.Ordinal).First().Key),
            random, false);
        return (selected.State, selected.NextInput!);
    }

    private sealed class Capture(IDecisionProvider inner) : IDecisionProvider
    {
        public string? Key { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => Key = inner.Choose(request, queries);
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class FixedRandom(string typeId) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(token => token.TypeId == typeId && token.SideId == "red") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }
}