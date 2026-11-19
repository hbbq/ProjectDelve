using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class OutpostGenerationTests
{
    private const int Size = 36;
    private static readonly CellBounds Area = new(0, 0, Size, Size);

    [Fact]
    public void OutpostPublishesOneClosedGateACompletePerimeterAndOwnedConnectedRooms()
    {
        var board = Board();
        var scope = Scope(board);
        var request = new OutpostRequest("outpost", Area, new());
        var plan = Assert.IsType<Planned<SitePlan>>(OutpostGenerator.Plan(request, scope)).Plan;
        var repeat = Assert.IsType<Planned<SitePlan>>(OutpostGenerator.Plan(request, scope)).Plan;
        Assert.Equal(JsonSerializer.Serialize(plan.Site), JsonSerializer.Serialize(repeat.Site));
        Assert.Equal(plan.Patch.TerrainChanges, repeat.Patch.TerrainChanges);
        Assert.Equal(plan.Patch.EdgeChanges, repeat.Patch.EdgeChanges);
        Assert.Equal(plan.Patch.Reservations, repeat.Patch.Reservations);
        Assert.Empty(board.State.Reservations);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
        Assert.Equal(GeneratedSiteKind.FortifiedOutpost, plan.Site.Kind);
        Assert.Equal(SiteConstructionProfile.FortifiedOutpost, plan.Site.ConstructionProfile);
        Commit(board, scope, plan);

        var site = plan.Site;
        var outpost = Assert.IsType<OutpostInfo>(site.Outpost);
        var gate = Assert.Single(site.Entrances);
        Assert.Equal(outpost.GateId, gate.Id);
        Assert.Equal(site.Id, gate.OwnerId);
        Assert.Equal(EdgeKind.ClosedDoor, gate.Kind);
        Assert.True(DefensivePerimeter.Validate(board, outpost.Enclosed.Bounds, gate, out var error), error);
        var expected = Boundary(board, outpost.Enclosed.Indices(Size).ToHashSet());
        Assert.Equal(expected.OrderBy(edge => edge.Index).ThenBy(edge => edge.Direction),
            outpost.Perimeter.OrderBy(edge => edge.Index).ThenBy(edge => edge.Direction));
        Assert.All(expected, edge => Assert.Equal(edge == gate.Edge ? EdgeKind.ClosedDoor : EdgeKind.Wall, board.EdgeAt(edge)));
        Assert.Equal(1, expected.Count(edge => board.EdgeAt(edge) == EdgeKind.ClosedDoor));
        var protections = ConstructionProtections.FromSites([site], Size);
        Assert.All(expected, edge => Assert.Contains(protections.Edges,
            protection => protection.Key == edge && protection.OwnerId == site.Id && protection.SourceId == site.Id));

        var building = Assert.Single(site.Buildings);
        Assert.Single(building.Rooms);
        Assert.All(building.Footprint.Indices(Size), cell => Assert.Equal(building.Id, board.State.Reservations[cell]));
        Assert.All(site.Exterior.Indices(Size), cell => Assert.Equal(site.Id, board.State.Reservations[cell]));
        Assert.All(outpost.Courtyard.Indices(Size), cell =>
        {
            Assert.True(board.Passable(cell));
            Assert.DoesNotContain(cell, building.Footprint.Indices(Size));
        });
        Assert.Contains(outpost.Courtyard.Indices(Size), cell => new[] { cell, cell + 1, cell + Size, cell + Size + 1 }
            .All(next => outpost.Courtyard.Contains(next, Size)));
        var state = GameEngine.CreateGame(board.Export());
        var reachable = MandatoryConnectivityRepair.ActualReachable(state.Physical.Board, [gate.OutsideIndex]);
        Assert.All(site.Exterior.Indices(Size), cell => Assert.Contains(cell, reachable));
        Assert.All(building.Rooms.SelectMany(room => room.Usable.Indices(Size)), cell => Assert.Contains(cell, reachable));
        var declarations = RequiredLocations.ForSite(site, "civilization", state.Physical.Board);
        Assert.Contains(declarations, location => location.SourceId == building.Rooms[0].Id);
        Assert.All(outpost.Courtyard.Indices(Size), cell => Assert.Contains(declarations,
            location => location.Groups.Any(group => group.Positions.Contains(cell))));
    }

    [Fact]
    public void GateUsesTheOrdinaryOpenDoorActionAndMovementInBothDirections()
    {
        var board = Board();
        var scope = Scope(board);
        var plan = Plan(scope);
        Commit(board, scope, plan);
        var gate = Assert.Single(plan.Site.Entrances);
        var scenario = board.Export();
        var state = Actor(scenario, Position(gate.OutsideIndex));
        var geometry = AccessibilityAnalysis.Capture(state.Physical.Board);
        Assert.False(geometry.CanStep(ConstructionCapabilities.OrdinaryLand, Position(gate.OutsideIndex), Position(gate.InsideIndex)));
        var conditional = AccessibilityAnalysis.Capture(state).Analyze("actor", Position(gate.OutsideIndex),
            new(AccessibilityMode.ConditionalDoors));
        Assert.Equal(AccessibilityStatus.Conditional, conditional.StatusAt(Position(gate.InsideIndex)));
        var opener = Assert.Single(state.Pending!.Candidates, candidate => candidate.Ability?.Definition.Mechanic is OpenDoor &&
            candidate.Door is { } door && SameEdge(door, gate));
        var opened = Use(state, opener.Key);
        Assert.Contains(opened.Events, action => action.Kind == "DoorOpened");
        Assert.True(AccessibilityAnalysis.Capture(opened.State.Physical.Board).CanStep(ConstructionCapabilities.OrdinaryLand,
            Position(gate.OutsideIndex), Position(gate.InsideIndex)));
        var inward = Assert.Single(opened.NextInput!.Candidates, candidate => candidate.Kind == ActivationChoiceKind.Move &&
            candidate.Destination == Position(gate.InsideIndex));
        var entered = Use(opened.State, inward.Key);
        Assert.Equal(Position(gate.InsideIndex), entered.State.Physical.Figures.Single(figure => figure.Id == "actor").Position);

        var openScenario = scenario with { Board = scenario.Board with
        {
            Edges = scenario.Board.Edges.Select(edge => edge.Position.Y * Size + edge.Position.X == gate.Edge.Index &&
                edge.Direction == gate.Edge.Direction ? edge with { Kind = EdgeKind.OpenDoor } : edge).ToList()
        } };
        var returning = Actor(openScenario, Position(gate.InsideIndex));
        var outward = Assert.Single(returning.Pending!.Candidates, candidate => candidate.Kind == ActivationChoiceKind.Move &&
            candidate.Destination == Position(gate.OutsideIndex));
        var exited = Use(returning, outward.Key);
        Assert.Equal(Position(gate.OutsideIndex), exited.State.Physical.Figures.Single(figure => figure.Id == "actor").Position);
    }

    [Fact]
    public void PerimeterValidationRejectsMissingWallsExtraOpeningsAndBlockedGateTerrain()
    {
        var board = Board();
        var scope = Scope(board);
        var plan = Plan(scope);
        Commit(board, scope, plan);
        var gate = Assert.Single(plan.Site.Entrances);
        var bounds = plan.Site.Outpost!.Enclosed.Bounds;
        var wall = plan.Site.Outpost.Perimeter.First(edge => edge != gate.Edge);
        foreach (var invalid in new[] { EdgeKind.None, EdgeKind.OpenDoor, EdgeKind.ClosedDoor })
        {
            board.State.Edges[wall] = invalid;
            Assert.False(DefensivePerimeter.Validate(board, bounds, gate, out _));
        }
        board.State.Edges[wall] = EdgeKind.Wall;
        board.State.Edges[gate.Edge] = EdgeKind.Wall;
        Assert.False(DefensivePerimeter.Validate(board, bounds, gate, out _));
        board.State.Edges[gate.Edge] = EdgeKind.ClosedDoor;
        board.State.TerrainWrites[gate.OutsideIndex] = "solid-rock";
        Assert.False(DefensivePerimeter.Validate(board, bounds, gate, out _));
    }

    [Fact]
    public void CompletionReportsASealedGuardhouseEvenWhenTheCourtyardRemainsReachable()
    {
        var board = Board();
        var deployment = Assert.IsType<HeroDeployment>(
            HeroDeploymentPlanner.Establish(board, new(), 17, []).Deployment);
        var guard = new CompletionGuard(board, deployment, Enumerable.Range(0, Size * Size));
        var scope = Scope(board);
        var plan = Plan(scope);
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(guard.TryPublish(scope.Draft.BuildPatch(), plan.Site.Id, "civilization", out var accepted,
            newSites: [plan.Site]), accepted.Detail);
        var original = board.Export();
        var building = Assert.Single(plan.Site.Buildings);
        var door = Assert.Single(building.Portals);
        var sealedScenario = original with { Board = original.Board with
        {
            Edges = original.Board.Edges.Select(edge => edge.Position.Y * Size + edge.Position.X == door.Edge.Index &&
                edge.Direction == door.Edge.Direction ? edge with { Kind = EdgeKind.Wall } : edge).ToList()
        } };
        var failures = guard.ActualFailures(GameEngine.CreateGame(sealedScenario));
        Assert.Contains(failures, failure => failure.GroupId == "building-entrance" && failure.Unreachable.Contains(door.InsideIndex));
        Assert.Contains(failures, failure => failure.GroupId == "room");
        Assert.DoesNotContain(failures, failure => failure.GroupId is "courtyard" or "approach");
    }

    [Theory]
    [InlineData("tree", false, true)]
    [InlineData("solid-rock", true, false)]
    public void ProhibitedDensePreparationRejectsWithoutPartialWrites(string terrain, bool clearTrees, bool excavateRock)
    {
        var board = Board(terrain);
        var scope = Scope(board);
        var result = Assert.IsType<Rejected<SitePlan>>(OutpostGenerator.Plan(new("outpost", Area,
            new(ClearTrees: clearTrees, ExcavateRock: excavateRock)), scope));
        Assert.NotEqual(PlacementFailureCode.ValidationFailed, result.Failure.Code);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
        Assert.Empty(scope.Draft.BuildPatch().EdgeChanges);
        Assert.Empty(scope.Draft.BuildPatch().Reservations);
    }

    [Fact]
    public void ProtectedCellsAndNearbySitesCannotBeOverwritten()
    {
        var board = Board();
        var scope = Scope(board);
        var first = Plan(scope);
        Commit(board, scope, first);
        var original = ScenarioDefinitionJson.ToJson(board.Export());
        var competing = Scope(board);
        Assert.IsType<Rejected<SitePlan>>(OutpostGenerator.Plan(new("competing", first.Site.Footprint.Bounds, new()), competing));
        Assert.Empty(competing.Draft.BuildPatch().TerrainChanges);
        Assert.Empty(competing.Draft.BuildPatch().Reservations);
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(board.Export()));

        var protectedBoard = Board();
        foreach (var cell in Enumerable.Range(0, Size * Size)) protectedBoard.State.Reservations.Add(cell, "expedition/endpoints");
        var protectedScope = Scope(protectedBoard);
        Assert.IsType<Rejected<SitePlan>>(OutpostGenerator.Plan(new("protected", Area, new()), protectedScope));
        Assert.Empty(protectedScope.Draft.BuildPatch().Reservations);
        Assert.All(protectedBoard.State.Reservations.Values, owner => Assert.Equal("expedition/endpoints", owner));
    }

    [Fact]
    public void AProtectedGateApproachAndAnExistingRoadUseAlternativesWithoutOverwritingEither()
    {
        var original = Board();
        var initial = Plan(Scope(original));
        var protectedCell = Assert.Single(initial.Site.Entrances).OutsideIndex;
        var board = Board();
        board.State.Reservations.Add(protectedCell, "expedition/exit");
        var road = Enumerable.Range(0, Size).Select(x => (Size / 2) * Size + x).ToArray();
        foreach (var cell in road) board.State.RouteCorridors.Add(cell, new(StringComparer.Ordinal) { "fixture-road" });
        var scope = Scope(board);
        var result = OutpostGenerator.Plan(new("outpost", Area, new(), new(RouteAnchors: road)), scope);
        if (result is Planned<SitePlan> accepted)
        {
            Assert.DoesNotContain(accepted.Plan.Patch.TerrainChanges, change => change.Index == protectedCell || road.Contains(change.Index));
            Assert.DoesNotContain(accepted.Plan.Patch.Reservations, reservation => reservation.Index == protectedCell || road.Contains(reservation.Index));
            Assert.DoesNotContain(accepted.Plan.Patch.EdgeChanges, change => road.Contains(change.Key.Index) ||
                road.Contains(change.Key.Index + (change.Key.Direction == EdgeDirection.Right ? 1 : Size)));
            Commit(board, scope, accepted.Plan);
        }
        else
        {
            Assert.IsType<Rejected<SitePlan>>(result);
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
            Assert.Empty(scope.Draft.BuildPatch().Reservations);
        }
        Assert.Equal("expedition/exit", board.State.Reservations[protectedCell]);
        Assert.All(road, cell => Assert.Contains("fixture-road", board.RouteMemberships(cell)));
        Assert.All(road, cell => Assert.Equal("grass", board.TerrainAt(cell)));
    }

    private static HashSet<EdgeKey> Boundary(IPlanningBoardView board, HashSet<int> enclosed) => enclosed
        .SelectMany(cell => board.Neighbors(cell).Where(next => !enclosed.Contains(next))
            .Select(next => EdgeKey.Between(cell, next, board.Width))).ToHashSet();
    private static bool SameEdge(Edge edge, SitePortal portal) =>
        new[] { edge.A, edge.B }.ToHashSet().SetEquals([Position(portal.InsideIndex), Position(portal.OutsideIndex)]);
    private static Cell Position(int index) => new(index % Size, index / Size);
    private static PlanningBoard Board(string ground = "grass") => CampsiteGenerationTests.Board(ground, Size, Size);
    private static GenerationScope Scope(PlanningBoard board) => CampsiteGenerationTests.Scope(board, 17, new(512, 1_000_000));
    private static SitePlan Plan(GenerationScope scope) => Assert.IsType<Planned<SitePlan>>(
        OutpostGenerator.Plan(new("outpost", Area, new()), scope)).Plan;
    private static void Commit(PlanningBoard board, GenerationScope scope, SitePlan plan)
    {
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
    }

    private static GameState Actor(ScenarioDefinition scenario, Cell position)
    {
        var type = new UnitType("outpost-visitor", 1, 0, 0, 0, 3, Actions: UnitAction.None)
        {
            Unique = true,
            LocalAbilities = [new("open-door", "Open Door", AbilityTiming.FreeAction, new OpenDoor()),
                new("reserve", "Guard", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
        };
        var state = GameEngine.CreateGame(scenario with
        {
            UnitTypeIds = [type.Id], UnitTypes = [type], Units = [new(type.Id, "blue", position, UnitId: "actor")],
            Agency = [new(type.Id, "blue", ControllerKind.Human)], Objectives = [new("blue", ScenarioObjective.NoObjective)]
        });
        state.Round = 1;
        state.ActiveToken = new(type.Id, "blue");
        state.CurrentUnitId = "actor";
        state.ResolvingTokenUnitIds = ["actor"];
        state.Pending = new(DecisionKind.Activation, type.Id, "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Opening the gate uses the ordinary deterministic Open Door Action.");
    }
}
