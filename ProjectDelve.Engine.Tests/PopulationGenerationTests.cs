using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PopulationGenerationTests
{
    private static WorldGenerationContext Fixture(ulong seed = 42, int width = 30, int height = 24)
    {
        var c = new WorldGenerationContext(new(seed, new(0, 0, 0)), new(Width: width, Height: height));
        c.SampleNaturalTerrain();
        c.CurrentAnalysis = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        return c;
    }
    private static GeneratedArea Area(string id, int x, int y, int width, int height, int boardWidth = 30) =>
        GeneratedArea.From(id, Enumerable.Range(y, height).SelectMany(row => Enumerable.Range(row * boardWidth + x, width)), boardWidth);
    private static void Camp(WorldGenerationContext c, string id = "camp", int x = 3, int y = 3)
    {
        var area = Area(id, x, y, 7, 6);
        c.Sites.Add(new(id, GeneratedSiteKind.Campsite, area, area, [], [], new(42, 42, true, false, 30), [], [], [], null, PreparationSummary.Empty));
    }
    private static void House(WorldGenerationContext c)
    {
        var area = Area("house", 15, 12, 7, 6);
        var room = new RoomInfo("room", "room", area, area, ["door"], 30);
        var portal = new SitePortal("door", "house", 15 + 12 * 30, 14 + 12 * 30, new(14 + 12 * 30, EdgeDirection.Right), EdgeKind.ClosedDoor);
        var building = new BuildingInfo("house", "house", area, [room], [portal], new(42, 42, true, false, 30), []);
        c.Sites.Add(new("settlement", GeneratedSiteKind.Settlement, area, area, [building], [], new(42, 42, true, false, 30), [], [], [], null, PreparationSummary.Empty));
    }
    private static void Road(WorldGenerationContext c)
    {
        var area = Area("road", 3, 18, 12, 2);
        c.Routes.Add(new("road", RoutePurpose.ThroughRoute, RouteProfileKind.Road, area,
            new(3, 18, [new(3, 18, CardinalSide.East, 11)]), new("start", 543, 3, 18, RouteEndpointKind.Cell),
            new("end", 554, 14, 18, RouteEndpointKind.Cell), 1, 2, PreparationSummary.Empty,
            new(1, 0, 0, 0, 12, 0, 0), [], [], "road", 0));
    }
    // Keep these source-specific contracts independent of natural occupation.
    private static PopulationSnapshot GenerateSources(WorldGenerationContext c, PopulationSettings settings)
    {
        var permitted = c.Sites.SelectMany(s => s.Footprint.Indices(c.Window.Width))
            .Concat(c.Routes.SelectMany(r => r.Corridor.Indices(c.Window.Width))).ToHashSet();
        c.PopulationProtectedAreas.Add(GeneratedArea.From("fixture-source-exclusions",
            Enumerable.Range(0, c.Samples.Length).Where(i => !permitted.Contains(i)), c.Window.Width));
        return PopulationGenerator.Generate(c, settings);
    }
    [Theory]
    [InlineData("GoblinCamp")]
    [InlineData("BuildingDefenders")]
    [InlineData("RoadPatrol")]
    public void ConcreteGeneratorsMaterializeOrdinaryCanonicalGroups(string kind)
    {
        var c = Fixture(kind == "BuildingDefenders" ? 43UL : 42UL);
        if (kind == "GoblinCamp") Camp(c); else if (kind == "BuildingDefenders") House(c); else Road(c);
        var before = JsonSerializer.Serialize(c.Materialize().Board);
        c.Population = GenerateSources(c, new(PopulationDensity.Low));
        var encounter = Assert.Single(c.Population.Encounters.Where(e => e.Kind == kind));
        Assert.InRange(encounter.Units.Count, kind == "GoblinCamp" ? 4 : 2, kind == "GoblinCamp" ? 8 : 5);
        Assert.All(encounter.Units, u => { Assert.Equal(HeroesVsMonsters.Monsters, u.SideId); Assert.Equal(encounter.GroupIds[0], u.GroupId); Assert.Equal(kind != "RoadPatrol", u.Dormant); });
        Assert.Equal(encounter.GroupIds[0], Assert.Single(encounter.Agency).GroupId);
        Assert.Equal(encounter.GroupIds[0], Assert.Single(encounter.Orders).GroupId);
        if (kind == "RoadPatrol") { Assert.Equal(OrderKind.PatrolRoute, encounter.Orders[0].Kind); Assert.Equal(RouteMode.PingPong, Assert.Single(encounter.Routes).Mode); Assert.True(encounter.Waypoints.Count > 1); }
        else Assert.Equal(kind == "BuildingDefenders" ? OrderKind.GuardArea : OrderKind.Normal, encounter.Orders[0].Kind);
        if (kind != "BuildingDefenders") Assert.All(encounter.Units, u => Assert.Equal(Posture.Upright, u.Posture));
        var definition = c.Materialize();
        Assert.Equal(before, JsonSerializer.Serialize(definition.Board));
        Assert.Empty(definition.Objectives ?? []); Assert.Empty(definition.Units.Where(u => u.SideId == HeroesVsMonsters.Heroes));
        var roundtrip = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition));
        _ = GameEngine.CreateGame(roundtrip, ContentBundle.Canonical());
        Assert.Equal(definition.Units, roundtrip.Units);
    }
    [Fact]
    public void ReproductionProtectionExhaustionAndBoundsAreExplicit()
    {
        var a = Fixture(); Camp(a); House(a); Road(a);
        var b = Fixture(); Camp(b); House(b); Road(b);
        var first = PopulationGenerator.Generate(a, new(PopulationDensity.High, 5));
        var repeat = PopulationGenerator.Generate(b, new(PopulationDensity.High, 5));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(repeat));
        Assert.Equal(first.Statistics.Groups, first.Encounters.SelectMany(e => e.GroupIds).Distinct().Count());
        Assert.Equal(first.Statistics.Units, first.Encounters.SelectMany(e => e.Units).Select(u => u.UnitId).Distinct().Count());
        Assert.InRange(first.Statistics.Attempts, 0, PopulationGenerator.MaxAttempts);
        Assert.InRange(first.Statistics.Work, 0, PopulationGenerator.MaxWork);
        Assert.InRange(first.Statistics.Units, 0, PopulationGenerator.MaxUnits);
        Assert.InRange(first.Statistics.Groups, 0, PopulationGenerator.MaxGroups);
        Assert.Equal(Math.Max(0, first.Statistics.Target - first.Encounters.Count), first.Statistics.Shortfall);
        var protectedContext = Fixture(); Camp(protectedContext);
        protectedContext.PopulationProtectedAreas.Add(Area("protected", 0, 0, 30, 24));
        var excluded = PopulationGenerator.Generate(protectedContext, new());
        Assert.Empty(excluded.Encounters); Assert.NotEmpty(excluded.Failures);
        Assert.Equal(excluded.Statistics.Target, excluded.Statistics.Shortfall);
    }
    [Fact]
    public void WeightedNaturalCompositionIsDeterministicAndUsesSeveralCanonicalTypes()
    {
        var types = new HashSet<string>();
        for (ulong seed = 0; seed < 16; seed++)
        {
            var c = Fixture(seed); var repeat = Fixture(seed);
            var result = PopulationGenerator.Generate(c, new(PopulationDensity.Medium, 4));
            Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(PopulationGenerator.Generate(repeat, new(PopulationDensity.Medium, 4))));
            Assert.All(result.Encounters, e => Assert.Contains(e.Kind, new[] { "LoneMonster", "WildernessGroup" }));
            foreach (var unit in result.Encounters.SelectMany(e => e.Units)) types.Add(unit.UnitTypeId);
            _ = GameEngine.CreateGame(PopulationGenerator.Combine(c.Materialize(), result.Encounters));
        }
        Assert.True(types.Count >= 3);
        Assert.All(types, id => Assert.Contains(ContentBundle.Canonical().Units, t => t.Id == id));
        Assert.DoesNotContain(UnitTypeIds.Shaman, types);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PipelineAddsPopulationOnlyAtTheFinalStageAndPreservesTerrain(bool construction)
    {
        var recipe = new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), 123,
            Civilization: construction ? new(Campsites: 2, Settlements: 0) : null);
        var baseline = WorldGenerator.Generate(recipe);
        var generated = WorldGenerator.Generate(recipe with { Population = new() });
        Assert.Null(baseline.Context.Population);
        Assert.All(generated.Preview.Frames.SkipLast(1), f => Assert.Null(f.Population));
        Assert.NotNull(generated.Preview.Frames[^1].Population);
        Assert.Empty(generated.Preview.Frames[^1].ChangedCells); Assert.Empty(generated.Preview.Frames[^1].ChangedEdges);
        Assert.Equal(JsonSerializer.Serialize(baseline.Definition.Board), JsonSerializer.Serialize(generated.Definition.Board));
        Assert.Equal(generated.Fingerprint, WorldGenerator.Generate(generated.Code).Fingerprint);
        Assert.Equal(recipe with { Population = new() }, WorldGenerationRecipe.Parse((recipe with { Population = new() }).ToString()));
    }
    [Fact]
    public void DisabledAndSmallMapsAreValidAndSettingsAreStrict()
    {
        Assert.Equal(new PopulationSettings(), WorldGenerationPresets.All[0].Population);
        foreach (var density in Enum.GetValues<PopulationDensity>())
        {
            var recipe = new WorldGenerationRecipe(new(Width: 1, Height: 1), new(0, 0, 0), 7, Population: new(density));
            Assert.Empty(WorldGenerator.Generate(recipe).Definition.Units);
            Assert.Equal(recipe, WorldGenerationRecipe.Parse(recipe.ToString()));
        }
        Assert.Throws<ArgumentException>(() => new PopulationSettings(Difficulty: 0).Validate());
        Assert.Throws<ArgumentException>(() => new PopulationSettings(Difficulty: 6).Validate());
    }
    private sealed class Dice(ActivationToken first) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.Contains(first) ? first : bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    [Fact]
    public void GeneratedGroupsRelayAlertAcrossTypesAndLyingZombieOnlyStands()
    {
        var campContext = Fixture();
        var location = new PopulationLocation("GoblinCamp", "fixture-camp", [1], new(0, 0, 8, 2));
        var camp = GoblinCampPopulationGenerator.Build("test-camp", location, [1], 30, new(42, 0));
        var house = ZombieHousePopulationGenerator.Build("test-house", location with { Kind = "ZombieHouse" }, [4], 30, new(42, 0));
        var goblin = camp.Units[0] with { Anchor = new(1, 0), WakeDistance = 1 };
        var zombie = house.Units[0] with { Anchor = new(4, 0), WakeDistance = 0, Posture = Posture.Lying };
        var definition = campContext.Materialize() with
        {
            UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Goblin, UnitTypeIds.Zombie],
            Units = [new(UnitTypeIds.Barbarian, HeroesVsMonsters.Heroes, new(0, 0), UnitId: "hero", WakeDistance: 0), goblin, zombie],
            Agency = [new(UnitTypeIds.Barbarian, HeroesVsMonsters.Heroes, ControllerKind.Human), .. camp.Agency, .. house.Agency],
            Orders = [.. camp.Orders, .. house.Orders],
            Objectives = [new(HeroesVsMonsters.Heroes, ScenarioObjective.NoObjective), new(HeroesVsMonsters.Monsters, ScenarioObjective.NoObjective)]
        };
        var token = new ActivationToken(zombie.UnitTypeId, zombie.SideId, zombie.GroupId);
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        var result = GameEngine.StartRound(state, new Dice(token));
        Assert.All(result.State.Units, u => Assert.False(u.Dormant));
        Assert.Contains(result.Events, e => e.Kind == "UnitWoken" && e.UnitId == zombie.UnitId);
        Assert.Equal(Posture.Upright, result.State.Physical.Figures.Single(f => f.Id == zombie.UnitId).Posture);
        Assert.Contains(result.Events, e => e.Kind == "PostureChanged" && e.UnitId == zombie.UnitId);
        Assert.DoesNotContain(result.Events, e => e.UnitId == zombie.UnitId && e.Kind is "MovementCompleted" or "AttackResolved" or "AbilityUsed");
        Assert.Equal(Posture.Lying, state.Physical.Figures.Single(f => f.Id == zombie.UnitId).Posture);
        Assert.All(result.State.Orders, order => Assert.Equal(OrderKind.GuardArea, order.Kind));
    }
    [Fact]
    public void MultiGroupPlansAndSharedRoutesKeepExactIndependentOrderState()
    {
        var location = new PopulationLocation("GoblinCamp", "camp", [93, 94, 123, 124], new(3, 3, 5, 5));
        PopulationEncounter? camp = null;
        for (ulong seed = 0; seed < 20 && camp?.GroupIds.Count != 2; seed++)
            camp = GoblinCampPopulationGenerator.Build("test-camp", location, location.Cells, 30, new(seed, 0));
        Assert.Equal(2, camp!.GroupIds.Count);
        var context = Fixture();
        var definition = PopulationGenerator.Combine(context.Materialize(), [camp]);
        var state = GameEngine.CreateGame(definition);
        var first = new ActivationToken(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, camp.GroupIds[0]);
        var second = first with { GroupId = camp.GroupIds[1] };
        Assert.NotEqual(first, second); Assert.Equal(2, state.Controllers.Count);
        state.AssignOrder(first, OrderKind.DoNothing);
        Assert.Equal(OrderKind.GuardArea, state.Orders.Single(o => o.Token == second).Kind);
        state.Waypoints.AddRange([new("a", new(0, 0, 1, 1)), new("b", new(1, 0, 1, 1))]);
        var route = new Route("shared", ["a", "b"], RouteMode.PingPong); state.Routes.Add(route);
        state.AssignOrder(first, OrderKind.FollowRoute, route: new("shared"));
        state.AssignOrder(second, OrderKind.FollowRoute, route: new("shared"));
        state.AssignOrder(first, OrderKind.FollowRoute, route: RouteRules.Next(state.Orders.Single(o => o.Token == first).Route!, route));
        Assert.Equal(1, state.Orders.Single(o => o.Token == first).Route!.CurrentIndex);
        Assert.Equal(0, state.Orders.Single(o => o.Token == second).Route!.CurrentIndex);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Orders, restored.Orders);
    }

    [Fact]
    public void ExhaustedCampBudgetDoesNotStarveRoadAndProtectedRoadSegmentsAreRejected()
    {
        var c = Fixture();
        for (var i = 0; i < 180; i++) Camp(c, "blocked-" + i);
        Road(c); c.PopulationProtectedAreas.Add(Area("blocked-camps", 3, 3, 7, 6));
        var snapshot = GenerateSources(c, new(PopulationDensity.Low));
        Assert.Single(snapshot.Encounters.Where(e => e.Kind == "RoadPatrol"));
        Assert.DoesNotContain(snapshot.Encounters, e => e.Kind == "GoblinCamp");
        Assert.InRange(snapshot.Statistics.Attempts, 1, PopulationGenerator.MaxAttempts);
        var blocked = Fixture(); Road(blocked);
        blocked.PopulationProtectedAreas.Add(Area("road-protection", 9, 18, 1, 1));
        Assert.DoesNotContain(GenerateSources(blocked, new()).Encounters, e => e.Kind == "RoadPatrol");
    }
    [Fact]
    public void GeneratedRoadPatrolActuallyAdvancesAndReversesWhileAnotherGroupRetainsItsCursor()
    {
        var c = Fixture(); Road(c);
        var patrol = Assert.Single(GenerateSources(c, new(PopulationDensity.Low)).Encounters.Where(e => e.Kind == "RoadPatrol"));
        Assert.Equal(OrderKind.PatrolRoute, Assert.Single(patrol.Orders).Kind);
        var definition = PopulationGenerator.Combine(c.Materialize(), [patrol]);
        var group = patrol.GroupIds[0]; var otherGroup = group + "-other";
        definition = definition with
        {
            Units = [.. definition.Units, patrol.Units[0] with { UnitId = "other-patrol", GroupId = otherGroup, Anchor = new(0, 0), Dormant = true, WakeDistance = 0 }],
            Agency = [.. definition.Agency, patrol.Agency[0] with { GroupId = otherGroup }],
            Orders = [.. definition.Orders!, patrol.Orders[0] with { GroupId = otherGroup }],
            Objectives = [new(HeroesVsMonsters.Monsters, ScenarioObjective.NoObjective)]
        };
        var token = new ActivationToken(UnitTypeIds.Grunt, HeroesVsMonsters.Monsters, group);
        var random = new Dice(token); var provider = new OrderAwareAutomatedProvider();
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), random);
        var advanced = false;
        for (var step = 0; step < 250 && result.State.Orders.Single(o => o.Token == token).Route!.Direction == 1; step++)
        {
            if (result.State.RoundComplete) result = GameEngine.StartRound(result.State, random);
            else result = GameEngine.Advance(result.State, provider, random);
            advanced |= result.Events.Any(e => e.Kind == "RouteAdvanced");
        }
        Assert.True(advanced);
        Assert.Equal(-1, result.State.Orders.Single(o => o.Token == token).Route!.Direction);
        Assert.Equal(0, result.State.Orders.Single(o => o.Token.GroupId == otherGroup).Route!.CurrentIndex);
        Assert.True(result.State.Units.Single(u => u.Id == "other-patrol").Dormant);
    }

    [Fact]
    public void FinalSealedBuildingPortalIsIneligibleDespiteEarlierAccessMetadata()
    {
        var c = Fixture(); House(c);
        var portal = c.Sites[0].Buildings[0].Portals[0];
        var definition = c.Materialize();
        c.ConstructionBoard = new PlanningBoard(definition with { Board = definition.Board with
        { Edges = [new(new(portal.Edge.Index % 30, portal.Edge.Index / 30), portal.Edge.Direction, EdgeKind.Wall)] } }, ContentBundle.Canonical());
        var snapshot = GenerateSources(c, new(PopulationDensity.Low));
        Assert.Empty(snapshot.Encounters); Assert.Equal(snapshot.Statistics.Target, snapshot.Statistics.Shortfall);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    public void PatrolRechecksFinalEdgesRatherThanRetainedRoadMetadata(EdgeKind kind)
    {
        var c = Fixture(); Road(c);
        var definition = c.Materialize();
        c.ConstructionBoard = new PlanningBoard(definition with { Board = definition.Board with
        { Edges = [new(new(9, 18), EdgeDirection.Right, kind)] } }, ContentBundle.Canonical());
        var snapshot = GenerateSources(c, new(PopulationDensity.Low));
        Assert.DoesNotContain(snapshot.Encounters, e => e.Kind == "RoadPatrol"); Assert.Contains(snapshot.Failures, f => f.Kind == "RoadPatrol");
    }

    [Fact]
    public void IndependentSameTypeEncountersNeverShareGroupsAndEmptyAuthoredGroupsReserveTheirIds()
    {
        var c = Fixture(); Camp(c); Camp(c, "other-camp", 20, 15);
        var snapshot = GenerateSources(c, new(PopulationDensity.High));
        Assert.Equal(2, snapshot.Encounters.Count);
        Assert.All(snapshot.Encounters, e => Assert.Equal("GoblinCamp", e.Kind));
        var groups = snapshot.Encounters.SelectMany(e => e.GroupIds).ToArray();
        Assert.Equal(groups.Length, groups.Distinct(StringComparer.Ordinal).Count());

        var collision = Fixture(); Camp(collision);
        const string existing = "population/x0-y0-w30-h24/request-1/GoblinCamp/group-0";
        var definition = collision.Materialize() with
        {
            UnitTypeIds = [UnitTypeIds.Goblin],
            Agency = [new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, ControllerKind.Automated, existing)]
        };
        collision.ConstructionBoard = new PlanningBoard(definition, ContentBundle.Canonical());
        var encounter = Assert.Single(GenerateSources(collision, new(PopulationDensity.Low)).Encounters);
        Assert.DoesNotContain(existing, encounter.GroupIds);
        Assert.StartsWith("population/x0-y0-w30-h24/request-1/GoblinCamp-0/", encounter.Units[0].UnitId);
        _ = GameEngine.CreateGame(PopulationGenerator.Combine(collision.Materialize(), [encounter]));
    }

    [Fact]
    public void PopulationPlanRequiresAnalysisAfterTheLastTerrainWriter()
    {
        var recipe = new WorldGenerationRecipe(new(), new(), 42);
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var steps = plan.Steps.ToList();
        steps.Add(new("late-roads", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.PrimaryRoutes, new()), ["current"]));
        steps.Add(new("population", WorldStepKind.Population, new PopulationStepParameters(new()), ["late-roads"]));
        Assert.Throws<ArgumentException>(() => (plan with { Steps = steps }).Validate());
    }
}
