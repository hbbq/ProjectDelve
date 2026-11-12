using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainV2Tests
{
    private static readonly TerrainDefinition Mixed = new("test-mixed", "Crossing", [MovementKinds.Land, MovementKinds.Water], false)
        { Categories = [TerrainCategories.Land, TerrainCategories.Water, TerrainCategories.Rock, TerrainCategories.RacingGround] };
    private static readonly TerrainDefinition Wet = new("test-wet", "Wet", [MovementKinds.Water], false)
        { Categories = [TerrainCategories.Land, TerrainCategories.Rock] };
    private static readonly TerrainDefinition Dry = new("test-dry", "Dry", [MovementKinds.Land], false)
        { Categories = [TerrainCategories.Water] };
    private static readonly TerrainDefinition Rock = new("test-rock", "Solid", [], false)
        { Categories = [TerrainCategories.Rock, TerrainCategories.Land, TerrainCategories.Water] };
    private static readonly TerrainDefinition Custom = new("test-custom", "Cliff path", ["rock-surface"], false)
        { Categories = [TerrainCategories.Rock] };
    private static readonly ImmutableArray<TerrainDefinition> Terrain = [Mixed, Wet, Dry, Rock, Custom];
    private static UnitType Walker(params string[] kinds) => new("test-walker", 8, 1, 1, 1, 1)
    { BaseMovementCapabilities = [.. kinds], CanHaveEquipment = true };
    private static ScenarioDefinition Scenario(UnitType type, params string[] columns) => new()
    {
        Board = new()
        {
            Width = columns.Length, Height = 2,
            Cells = columns.SelectMany((id, x) => Enumerable.Range(0, 2)
                .Select(y => new CellDefinition(new(x, y), TerrainKind.StoneFloor) { TerrainTypeId = id })).ToList()
        },
        TerrainTypes = [.. Terrain], UnitTypes = [type], UnitTypeIds = [type.Id],
        Units = [new(type.Id, "blue", new(0, 0), UnitId: "actor")],
        Agency = [new(type.Id, "blue", ControllerKind.Human)], Loot = LootSettings.Empty,
        Objectives = [new("blue", ScenarioObjective.NoObjective)]
    };
    private static LocalAbilityDefinition Grant(params string[] kinds) =>
        new("grant", "Cliff walking", AbilityTiming.Passive, new MovementCapabilityGrant([.. kinds]));

    [Fact]
    public void MachineRequirementIsAnExplicitTerrainDependency()
    {
        var dependency = Assert.Single(ContentDefinitions.Dependencies(MachineTypes.Beacon));
        Assert.Equal(new ContentIdentity(ContentFamily.Terrain, "stone-floor"), dependency.Target);
        Assert.Equal("requiredTerrainTypeId", dependency.Path);
        Assert.Equal(ContentDependencyKind.Explicit, dependency.Kind);
    }

    [Fact]
    public void GrantValidationAcceptsArbitraryNormalizedKindsAndOnlyPersistentSourceProfiles()
    {
        var grant = new MovementCapabilityGrant(["cliff:stone", "open-water"]);
        Assert.Empty(MechanicCatalog.Validate(grant, new(MechanicSource.Unit, MechanicTiming.Passive)));
        Assert.Empty(MechanicCatalog.Validate(grant, new(MechanicSource.Equipment, MechanicTiming.Passive)));
        Assert.NotEmpty(MechanicCatalog.Validate(grant, new(MechanicSource.Item, MechanicTiming.Passive)));
        Assert.NotEmpty(MechanicCatalog.Validate(grant, new(MechanicSource.Equipment, MechanicTiming.Passive, MaxUses: 1)));
        foreach (var kinds in new ImmutableArray<string>[] { [], default, ["water", "water"], ["Water"], ["bad kind"] })
            Assert.NotEmpty(MechanicCatalog.Validate(new MovementCapabilityGrant(kinds)));
        Assert.NotEmpty(ContentDefinitions.Validate(new(Walker("Water"))));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Custom with { MovementPermissions = ["Rock"] }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Custom with { AllowsResourceMarkers = false }]));
    }

    [Fact]
    public void CurrentAuthoringRoundtripsThroughTypedBoundaryAndRejectsOldTerrainShape()
    {
        var scenario = Scenario(Walker(MovementKinds.Land) with { LocalAbilities = [Grant("rock-surface")] }, Custom.Id, Mixed.Id);
        var json = ScenarioDefinitionJson.ToJson(scenario);
        var parsed = ScenarioDefinitionJson.FromJson(json);
        var state = GameEngine.CreateGame(parsed);
        Assert.Contains("rock-surface", state.EffectiveMovementCapabilitiesOf("actor"));
        Assert.Contains("\"movementPermissions\"", json);
        Assert.Contains("\"movementCapabilityGrant\"", json);
        const string oldTerrain = """{"Id":"old","Name":"Old","Passable":true,"BlocksLos":false}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TerrainDefinition>(oldTerrain));
    }

    [Fact]
    public void ResolvedCapabilityAndCategoryMatchingAllocateNothingPerCell()
    {
        var profile = MovementTraversal.OccupancyFor(Walker(MovementKinds.Land, MovementKinds.Water));
        var restriction = new TerrainRestriction([TerrainCategories.Water, TerrainCategories.Rock]);
        for (var i = 0; i < 1000; i++) { profile.CanTraverse(Mixed); restriction.Accepts(Mixed); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = 0;
        for (var i = 0; i < 10000; i++)
            if (profile.CanTraverse(Mixed) && restriction.Accepts(Mixed)) matches++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10000, matches);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("land", true, false, true, false)]
    [InlineData("water", true, true, false, false)]
    [InlineData("rock-surface", false, false, false, true)]
    public void PermissionsMatchCapabilitiesIndependentlyOfSemanticCategories(string kind,
        bool mixed, bool wet, bool dry, bool custom)
    {
        var occupancy = MovementTraversal.OccupancyFor(Walker(kind));
        Assert.Equal(mixed, occupancy.CanTraverse(Mixed));
        Assert.Equal(wet, occupancy.CanTraverse(Wet));
        Assert.Equal(dry, occupancy.CanTraverse(Dry));
        Assert.Equal(custom, occupancy.CanTraverse(Custom));
        Assert.False(occupancy.CanTraverse(Rock));
        Assert.True(new TerrainRestriction([TerrainCategories.Water]).Accepts(Dry));
        Assert.False(new TerrainRestriction([TerrainCategories.Water]).Accepts(Wet));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedRouteRequiresEveryEnteredTerrainToSupportTheUnit(bool both)
    {
        var type = Walker(both ? [MovementKinds.Land, MovementKinds.Water] : [MovementKinds.Water]);
        var state = GameEngine.CreateGame(Scenario(type, Mixed.Id, Wet.Id, Mixed.Id, Dry.Id, Rock.Id));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(1, 0), paths.Keys);
        Assert.Contains(new Cell(2, 0), paths.Keys);
        Assert.Equal(both, paths.ContainsKey(new(3, 0)));
        Assert.DoesNotContain(new Cell(4, 0), paths.Keys);
        Assert.Equal(2, MovementRules.Cost(paths[new(2, 0)]));
    }

    [Fact]
    public void RepeatedCandidateScoringDoesNotRebuildLargeCapabilityGrants()
    {
        var actor = Walker(MovementKinds.Land);
        var opponent = actor with { Id = "opponent", DefaultController = ControllerKind.Human };
        var scenario = Scenario(actor, Dry.Id, Dry.Id, Dry.Id) with {
            UnitTypes = [actor, opponent], UnitTypeIds = [actor.Id, opponent.Id] };
        scenario.Units.Add(new(opponent.Id, "red", new(2, 1), UnitId: "enemy"));
        var state = GameEngine.CreateGame(scenario);
        var baseline = new GameplayQueries(state);
        state.Types[state.Types.FindIndex(t => t.Id == actor.Id)] = actor with {
            LocalAbilities = [Grant(Enumerable.Range(0, 32).Select(i => $"custom-{i}").ToArray())] };
        var enriched = new GameplayQueries(state);
        long Allocated(GameplayQueries queries)
        {
            for (var i = 0; i < 10; i++) Assert.Equal(3, queries.DistanceToNearestHostileFrom("actor", new(0, 0)));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 50; i++) queries.DistanceToNearestHostileFrom("actor", new(0, 0));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var baselineBytes = Allocated(baseline);
        var enrichedBytes = Allocated(enriched);
        Assert.True(enrichedBytes <= baselineBytes + 1024,
            $"Scoring allocations must not scale with grant size after profile resolution: {baselineBytes} vs {enrichedBytes} bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAndInitialEquipmentGrantsPersistWhileLyingAndAcrossSnapshot(bool equipment)
    {
        var type = Walker(MovementKinds.Land);
        if (!equipment) type = type with { LocalAbilities = [Grant("rock-surface", MovementKinds.Water)] };
        var scenario = Scenario(type, Custom.Id, Wet.Id, Dry.Id);
        if (equipment)
        {
            scenario = scenario with { EquipmentTypes = [new("test-boots", "Boots")
                { LocalAbilities = [Grant("rock-surface", MovementKinds.Water)] },
                new("test-cloak", "Cloak") { LocalAbilities = [Grant("rock-surface", MovementKinds.Water)] }] };
            scenario.Units[0] = scenario.Units[0] with
                { Inventory = [new("physical-boots", "test-boots"), new("physical-cloak", "test-cloak")] };
        }
        scenario.Units[0] = scenario.Units[0] with { Posture = Posture.Lying };
        var state = GameEngine.CreateGame(scenario);
        Assert.False(state.IsUpright("actor"));
        Assert.Equal(new[] { "land", "rock-surface", "water" }, state.EffectiveMovementCapabilitiesOf("actor"));
        Assert.True(SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(state.EffectiveMovementCapabilitiesOf("actor").ToArray(), restored.EffectiveMovementCapabilitiesOf("actor").ToArray());
        Assert.Equal(Posture.Lying, Assert.Single(restored.Physical.Figures).Posture);
        Assert.True(SpatialRules.CanPlaceUnit(restored.Copy(), "actor", new(2, 0)));
        Assert.False(restored.CanUseAbility("actor", restored.AbilitiesOf("actor").First()));
    }

    [Fact]
    public void AmphibiousAddsWaterWithoutGivingWaterOnlyUnitsLand()
    {
        var type = Walker(MovementKinds.Water) with { Amphibious = true };
        var state = GameEngine.CreateGame(Scenario(type, Wet.Id, Mixed.Id, Dry.Id));
        Assert.Equal(new[] { "water" }, state.EffectiveMovementCapabilitiesOf("actor"));
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
    }

    [Fact]
    public void WaterOnlyPhaseTraversesUnsupportedTerrainButUsesReceivingEndpoints()
    {
        var type = Walker(MovementKinds.Water) with
            { LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] };
        var state = GameEngine.CreateGame(Scenario(type, Mixed.Id, Dry.Id, Rock.Id, Wet.Id));
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(3, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(1, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.Throws<ArgumentException>(() => HypotheticalPosition.Validate(state, "actor", new(1, 0)));
    }

    [Fact]
    public void LargeFootprintChecksEveryCellAndInternalEdge()
    {
        var type = Walker(MovementKinds.Land, MovementKinds.Water) with { Footprint = Footprint.TwoByTwo };
        var state = GameEngine.CreateGame(Scenario(type, Mixed.Id, Dry.Id, Wet.Id, Rock.Id));
        Assert.True(SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
        Assert.False(SpatialRules.CanPlaceUnit(state, "actor", new(2, 0)));
        state.Types[0] = type with { BaseMovementCapabilities = [MovementKinds.Land] };
        Assert.False(SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
        state.Types[0] = type;
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        Assert.False(SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
    }

    [Fact]
    public void OrdinaryCategoryRestrictionDoesNotForbidExternalOccupancyOrReturnStep()
    {
        var type = Walker(MovementKinds.Land) with
            { TerrainRestriction = new([TerrainCategories.RacingGround]) };
        var scenario = Scenario(type, Mixed.Id, Dry.Id, Mixed.Id);
        var state = GameEngine.CreateGame(scenario);
        Assert.DoesNotContain(new Cell(1, 0), MovementRules.FindPaths(state, "actor", new(0, 0)).Keys);
        Assert.Contains(new Cell(1, 0), ExternalMovementRules.Destinations(state, "actor", "actor", 1));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(1, 0) };
        GameEngine.ValidateScenario(state);
        Assert.Contains(new Cell(0, 0), MovementRules.FindPaths(state, "actor", new(1, 0), 1).Keys);
        scenario.Units[0] = scenario.Units[0] with { Anchor = new(1, 0) };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario));
    }

    [Fact]
    public void WaterWallsAreLegalAndBreakStillProtectsReceivingOccupancy()
    {
        var crossing = Mixed with { Id = "test-destructible", DestroyedTerrainTypeId = Wet.Id };
        var scenario = Scenario(Walker(MovementKinds.Land), crossing.Id, Wet.Id);
        scenario = scenario with { TerrainTypes = [.. Terrain, crossing] };
        var state = GameEngine.CreateGame(scenario);
        var target = new BreakTarget(Cell: new(0, 0));
        Assert.False(BreakObjectRules.CanBreak(state, target));
        state.Types[0] = state.Types[0] with { LocalAbilities = [Grant(MovementKinds.Water)] };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.True(BreakObjectRules.CanBreak(state, target));
        var edge = new Edge(new(0, 1), new(1, 1), EdgeKind.WallWithWindow);
        state.Physical.Board.Terrain.RemoveAll(t => t.Position == new Cell(0, 1));
        state.Physical.Board.Terrain.Add(new(new(0, 1)) { TypeId = Wet.Id });
        Assert.True(EdgeReplacementRules.CanReplace(state, edge));
        state.Physical.Board.Edges.Add(edge);
        GameEngine.ValidateScenario(state);
    }

    [Fact]
    public void WaterOnlySwapForcedMovementAndFowUsePersistentReceivingCapabilities()
    {
        var swimmer = Walker(MovementKinds.Water);
        var scenario = Scenario(swimmer, Wet.Id, Mixed.Id, Dry.Id);
        var shore = Walker(MovementKinds.Land) with { Id = "shore", DefaultController = ControllerKind.Human };
        scenario = scenario with { UnitTypes = [swimmer, shore], UnitTypeIds = [swimmer.Id, shore.Id] };
        scenario.Units.Add(new(shore.Id, "blue", new(2, 0), UnitId: "shore"));
        var state = GameEngine.CreateGame(scenario);
        Assert.False(ExternalMovementRules.CanSwap(state, "actor", "shore"));
        Assert.Contains(new Cell(1, 0), ExternalMovementRules.Destinations(state, "shore", "actor", 1));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(1, 0), Posture = Posture.Lying };
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(1, 1) };
        Assert.DoesNotContain(new Cell(2, 0), ExternalMovementRules.Destinations(state, "shore", "actor", 1));
        Assert.Contains(new Cell(0, 0), ExternalMovementRules.Destinations(state, "shore", "actor", 1));
        Assert.True(ExternalMovementRules.CanSwap(state, "actor", "shore"));
        ExternalMovementRules.SwapPlaces(state, "actor", "shore");
        GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        state.FogOfWarSides = ["blue"]; state.ExploredCells["blue"] = [new(1, 1)];
        Assert.DoesNotContain(new Cell(0, 1), MovementRules.FindPaths(state, "actor", new(1, 1)).Keys);
        state.ExploredCells["blue"].Add(new(0, 1));
        Assert.Contains(new Cell(0, 1), MovementRules.FindPaths(state, "actor", new(1, 1)).Keys);
    }

    [Fact]
    public void WaterOnlySummonCreatesLegalLyingUnitWithDeterministicEventsAndRestore()
    {
        var swimmer = Walker(MovementKinds.Water) with { Id = "summoned-swimmer", DefaultController = ControllerKind.Human };
        var summoner = Walker(MovementKinds.Land) with { LocalAbilities = [new("call", "Call Swimmer",
            AbilityTiming.Action, new AdjacentUnitSummonMechanic(swimmer.Id, Posture.Lying))] };
        var scenario = Scenario(summoner, Dry.Id, Wet.Id, Mixed.Id);
        scenario = scenario with { UnitTypes = [summoner, swimmer] };
        var state = GameEngine.CreateGame(scenario);
        var actor = state.Units.Single();
        var candidates = SummoningRules.Candidates(state, actor).ToArray();
        Assert.Contains(candidates, c => c.Destination == new Cell(1, 0));
        Assert.DoesNotContain(candidates, c => c.Destination == new Cell(0, 1));
        state.Round = 1; state.ActiveToken = actor.Token; state.CurrentUnitId = actor.Id;
        state.ResolvingTokenUnitIds = [actor.Id]; state.MoveDone = true;
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var choice = state.Pending!.Candidates.Single(c => c.Destination == new Cell(1, 0) &&
            c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic);
        var first = GameEngine.Advance(state, new Choice(choice.Key), new Dice(), false);
        var replay = GameEngine.Advance(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!,
            new Choice(choice.Key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(first.Events), JsonSerializer.Serialize(replay.Events));
        Assert.Equal(JsonSerializer.Serialize(first.State), JsonSerializer.Serialize(replay.State));
        var figure = first.State.Physical.Figures.Single(f => f.Position == new Cell(1, 0));
        Assert.Equal(Posture.Lying, figure.Posture);
        Assert.Equal(new[] { "water" }, first.State.EffectiveMovementCapabilitiesOf(figure.Id));
        GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(first.State))!);
    }

    [Fact]
    public void AllInternalEdgeKindsAcceptAllCanonicalTerrainPairs()
    {
        foreach (var first in new[] { TerrainKind.Grass, TerrainKind.Water, TerrainKind.SolidRock })
        foreach (var second in new[] { TerrainKind.Grass, TerrainKind.Water, TerrainKind.SolidRock })
        foreach (var kind in Enum.GetValues<EdgeKind>())
        {
            var edge = new Edge(new(0, 0), new(1, 0), kind);
            var state = new GameState { Physical = new(new Board(2, 1, []) {
                Terrain = [new(edge.A, first), new(edge.B, second)] }, []), Types = [], Units = [] };
            TestGame.Author(state);
            Assert.True(EdgeReplacementRules.CanReplace(state, edge));
            EdgeReplacementRules.ReplaceEdges(state, [edge]);
            Assert.Equal(kind, state.Physical.Board.EdgeBetween(edge.A, edge.B));
            GameEngine.ValidateScenario(state);
        }
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
