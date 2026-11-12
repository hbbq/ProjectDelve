using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainV2SpecialMovementTests
{
    private static LocalAbilityDefinition Summon(UnitType type) =>
        new("summon-" + type.Id, "Summon " + type.Id, AbilityTiming.Action,
            new AdjacentUnitSummonMechanic(type.Id, Posture.Upright));

    private static GameState SummoningWorld(BoardDefinition board, Cell actor, Cell target,
        UnitType targetType, params UnitType[] summoned)
    {
        var summoner = new UnitType("test-summoner", 0, 0, 0, 0, 1)
        {
            Actions = UnitAction.None, Behaviors = UnitBehavior.UseSummon,
            LocalAbilities = [.. summoned.Select(Summon)]
        };
        return GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = board, UnitTypes = [summoner, targetType, .. summoned],
            UnitTypeIds = [summoner.Id, targetType.Id, .. summoned.Select(t => t.Id)],
            Units = [new(summoner.Id, "blue", actor, UnitId: "actor"),
                new(targetType.Id, "red", target, UnitId: "target")],
            Agency = [new(summoner.Id, "blue", ControllerKind.Human), new(targetType.Id, "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        });
    }

    private static Candidate Placement(GameState state, UnitType summoned, Cell position) =>
        SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "actor"))
            .Single(c => c.Destination == position &&
                c.Ability!.Definition.Mechanic is AdjacentUnitSummonMechanic summon && summon.UnitTypeId == summoned.Id);

    [Fact]
    public void WaterSummonRankingUsesItsConnectedWaterRouteInsteadOfLandSummonerPermissions()
    {
        var swimmer = new UnitType("test-swimmer", 8, 1, 1, 0, 1) { BaseMovementCapabilities = [MovementKinds.Water] };
        var targetType = new UnitType("test-water-target", 0, 0, 0, 0, 1) { BaseMovementCapabilities = [MovementKinds.Water] };
        var state = SummoningWorld(new()
        {
            Width = 6, Height = 4, DefaultTerrain = TerrainKind.Grass,
            Cells = [new(new(1, 0), TerrainKind.Water),
                .. Enumerable.Range(1, 5).Select(x => new CellDefinition(new(x, 2), TerrainKind.Water))]
        }, new(1, 1), new(5, 2), targetType, swimmer);
        var upper = Placement(state, swimmer, new(1, 0));
        var lower = Placement(state, swimmer, new(1, 2));
        var request = new DecisionRequest(DecisionKind.Act, "test-summoner", "actor", [upper, lower], true);
        var queries = new GameplayQueries(state);
        Assert.Equal(new[] { MovementKinds.Land }, state.EffectiveMovementCapabilitiesOf("actor"));
        Assert.Null(queries.OpenRouteDistanceFromSummonPlacement("actor", upper, ["target"]));
        Assert.Equal(4, queries.OpenRouteDistanceFromSummonPlacement("actor", lower, ["target"]));
        Assert.Equal(lower.Key, DefaultAutomatedProvider.SelectAction(request, queries, ["target"]));
        state.AssignOrder(state.Controllers.Single(c => c.Token == state.Units.Single(u => u.Id == "actor").Token).Token,
            OrderKind.Attack, target: OrderTarget.ForUnit("target"));
        Assert.Equal(lower.Key, new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void SummonRouteRequiresItsCompleteFootprintToFitTheConnectingPassage()
    {
        var small = new UnitType("test-small", 10, 1, 1, 0, 1);
        var large = small with { Id = "test-large", Footprint = Footprint.TwoByTwo };
        var floor = Enumerable.Range(1, 3).SelectMany(y => Enumerable.Range(0, 3).Concat(Enumerable.Range(6, 3))
            .Select(x => new CellDefinition(new(x, y), TerrainKind.Grass)))
            .Concat(Enumerable.Range(3, 3).Select(x => new CellDefinition(new(x, 2), TerrainKind.Grass)))
            .Append(new(new(0, 0), TerrainKind.Grass)).ToList();
        var state = SummoningWorld(new() { Width = 9, Height = 5, DefaultTerrain = TerrainKind.SolidRock, Cells = floor },
            new(0, 0), new(7, 2), new("test-target", 0, 0, 0, 0, 1), small, large);
        var queries = new GameplayQueries(state);
        Assert.Equal(8, queries.OpenRouteDistanceFromSummonPlacement("actor", Placement(state, small, new(0, 1)), ["target"]));
        Assert.Null(queries.OpenRouteDistanceFromSummonPlacement("actor", Placement(state, large, new(0, 1)), ["target"]));
        // Querying the large Type must not replace the small Type's cached traversal.
        Assert.Equal(8, queries.OpenRouteDistanceFromSummonPlacement("actor", Placement(state, small, new(0, 1)), ["target"]));
    }

    [Theory]
    [InlineData(5, 1, 4)]
    [InlineData(4, 3, 4)]
    public void LargeSummonRouteReachesBorderTargetWithAFittingOverlappingAnchor(int targetX, int targetY, int distance)
    {
        var large = new UnitType("test-large", 10, 1, 1, 0, 1) { Footprint = Footprint.TwoByTwo };
        var state = SummoningWorld(new() { Width = 6, Height = 4, DefaultTerrain = TerrainKind.Grass },
            new(0, 0), new(targetX, targetY), new("test-target", 0, 0, 0, 0, 1), large);
        var candidate = Placement(state, large, new(0, 1));
        Assert.Equal(distance, new GameplayQueries(state)
            .OpenRouteDistanceFromSummonPlacement("actor", candidate, ["target"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummonedPhaseCanCrossObstructionsWhilePlacementAndMoveEndpointsStayLegal(bool phase)
    {
        var summoned = new UnitType("test-phaser", 8, 1, 1, 0, 1)
        {
            LocalAbilities = phase ? [new("phase", "Phase", AbilityTiming.Passive, new Phase())] : []
        };
        var state = SummoningWorld(new()
        {
            Width = 5, Height = 3, DefaultTerrain = TerrainKind.SolidRock,
            Cells = [new(new(0, 0), TerrainKind.Grass), new(new(0, 1), TerrainKind.Grass),
                new(new(3, 1), TerrainKind.Grass), new(new(4, 1), TerrainKind.Grass)],
            Edges = [new(new(1, 1), EdgeDirection.Right, EdgeKind.Wall)]
        }, new(0, 0), new(4, 1), new("test-target", 0, 0, 0, 0, 1), summoned);
        var candidate = Placement(state, summoned, new(0, 1));
        Assert.Equal(phase ? 4 : (int?)null,
            new GameplayQueries(state).OpenRouteDistanceFromSummonPlacement("actor", candidate, ["target"]));
        Assert.DoesNotContain(SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "actor")),
            c => c.Destination == new Cell(1, 1));
        state.PlaceUnit(summoned.Id, "spawn", "blue", new(0, 1));
        var paths = MovementRules.FindPaths(state, "spawn", new(0, 1));
        Assert.Equal(phase, paths.ContainsKey(new(3, 1)));
        Assert.DoesNotContain(new Cell(1, 1), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 1), paths.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RailStepsHonorCapturedTerrainPermissionsForTheReceivingUnit(bool water)
    {
        var permissions = water ? MovementKinds.Water : MovementKinds.Land;
        var rail = TerrainCatalog.All.Single(t => t.LegacyKind == TerrainKind.Rail);
        var terrain = TerrainCatalog.All.Replace(rail, rail with { MovementPermissions = [permissions] });
        var train = new UnitType("test-train", 0, 0, 0, 0, 1)
        {
            Actions = UnitAction.None, BaseMovementCapabilities = [permissions],
            LocalAbilities = [new("rails", "Run Rails", AbilityTiming.InsteadOfActivation, new RunRails(2))]
        };
        // This test owns a captured runtime catalog; canonical authored Rail is unchanged.
        var state = new GameState
        {
            Physical = new(new Board(4, 2, [])
            {
                TerrainTypes = terrain,
                Terrain = [.. Enumerable.Range(0, 3).Select(x => new TerrainTile(new(x, 0), TerrainKind.Rail) { Direction = RailDirection.East })]
            }, [new("train", new(0, 0))]),
            Types = [train], Units = [train.CreateUnit("train", "blue")],
            Objectives = [new("blue", ScenarioObjective.NoObjective)]
        };
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var result = TestGame.StartRound(restored, new NoDice(), false);
        Assert.Equal(new Cell(2, 0), Assert.Single(result.State.Physical.Figures).Position);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "UnitRepositioned"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "DiceRolled");
        GameEngine.ValidateScenario(result.State);
        Assert.Equal(new[] { MovementKinds.Land }, rail.MovementPermissions);
    }

    private sealed class NoDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("No combat dice expected.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("No combat dice expected.");
        public int RollD6() => throw new InvalidOperationException("No dice expected.");
    }
}
