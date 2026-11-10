using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PopulationV2Tests
{
    private static WorldGenerationRecipe Recipe(ulong seed = 12345, PopulationDensity density = PopulationDensity.High,
        int width = 60, int height = 50) => new(new(Width: width, Height: height), new(0, 0, 0), seed,
            Population: new(density, 4));

    [Fact]
    public void NaturalPopulationIsDenseUnevenMostlyDormantAndUsesOrdinaryLoneOrders()
    {
        var world = WorldGenerator.Generate(Recipe()); var snapshot = world.Context.Population!;
        Assert.Empty(world.Context.Sites);
        Assert.True(snapshot.Statistics.Units > 96);
        Assert.True(snapshot.Statistics.DormantUnits >= snapshot.Statistics.Units * .9);
        Assert.Equal(0, snapshot.Statistics.InitialTokens);
        Assert.True(snapshot.Statistics.QuietRegions > 0);
        Assert.Contains(snapshot.Encounters, e => e.Kind == "WildernessGroup");
        var singles = snapshot.Encounters.Where(e => e.Kind == "LoneMonster").ToArray(); Assert.NotEmpty(singles);
        foreach (var encounter in singles)
        {
            var unit = Assert.Single(encounter.Units); Assert.True(unit.Dormant); Assert.Equal(Posture.Upright, unit.Posture);
            Assert.Equal(OrderKind.Normal, Assert.Single(encounter.Orders).Kind);
            Assert.Equal(ControllerKind.Automated, Assert.Single(encounter.Agency).Controller);
        }
        var regions = world.Definition.Units.GroupBy(u => (u.Anchor.X / 12, u.Anchor.Y / 12)).Select(g => g.Count()).ToArray();
        Assert.True(regions.Max() >= regions.Min() * 2);
        Assert.InRange(snapshot.Statistics.Units, 1, PopulationGenerator.MaxUnits);
        Assert.InRange(snapshot.Statistics.Groups, 1, PopulationGenerator.MaxGroups);
        Assert.InRange(snapshot.Statistics.Work, 1, PopulationGenerator.MaxWork);
        Assert.InRange(snapshot.Statistics.Attempts, 1, PopulationGenerator.MaxAttempts);
    }

    [Fact]
    public void DensityChangesAmountAndCurrentRecipeReproducesExactly()
    {
        var low = WorldGenerator.Generate(Recipe(density: PopulationDensity.Low));
        var medium = WorldGenerator.Generate(Recipe(density: PopulationDensity.Medium));
        var high = WorldGenerator.Generate(Recipe());
        Assert.True(low.Definition.Units.Count < medium.Definition.Units.Count);
        Assert.True(medium.Definition.Units.Count < high.Definition.Units.Count);
        var repeated = WorldGenerator.Generate(high.Code);
        Assert.Equal(high.Fingerprint, repeated.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(high.Context.Population), JsonSerializer.Serialize(repeated.Context.Population));
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(high.Definition));
        Assert.Equal(high.Definition.Units, restored.Units);
        Assert.Equal(high.Definition.Orders, restored.Orders);
        var state = GameEngine.CreateGame(restored);
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Units, saved.Units); Assert.Equal(state.Orders, saved.Orders);
    }

    [Fact]
    public void SitesCanRemainEmptyAndResolveBothCampAlertTopologies()
    {
        var topologies = new HashSet<string>(); var layouts = new HashSet<string>();
        var sawEmpty = false; var sawOccupied = false; var sawLying = false; PopulationEncounter? patrolCamp = null; BoardDefinition? patrolBoard = null;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var recipe = Recipe(seed, width: 40, height: 35) with
            { Civilization = new(Campsites: 2, Settlements: 0), Routes = new(ThroughRoutes: 1, Profile: RouteProfileKind.Road) };
            var world = WorldGenerator.Generate(recipe); var encounters = world.Context.Population!.Encounters;
            foreach (var site in world.Context.Sites)
            {
                Assert.NotNull(site.PopulationIntent); layouts.Add(site.PopulationIntent.Layout);
                var encounter = encounters.SingleOrDefault(e => e.SourceId == site.Id);
                if (encounter is null) { sawEmpty = true; continue; }
                sawOccupied = true; topologies.Add(encounter.IntendedAlertTopology!);
                Assert.InRange(encounter.Units.Count, 6, 16);
                Assert.Contains(encounter.Roles, r => r.Role == "sentry");
                Assert.Contains(encounter.Roles, r => r.Role == "resting");
                Assert.True(encounter.GroupIds.Count >= 2);
                Assert.Equal(encounter.Units.Count, encounter.Roles.Count);
                Assert.All(encounter.Units, unit => Assert.Equal(unit.Dormant, encounter.Roles.Single(r => r.UnitId == unit.UnitId).Role != "patrol"));
                var graph = PopulationTopology.Components(encounter.Units, ContentBundle.Canonical().Units);
                Assert.Equal(encounter.ActualAlertComponents, graph.Count);
                Assert.True(encounter.IntendedAlertTopology == "Connected" ? graph.Count == 1 : graph.Count >= 2);
                sawLying |= encounter.Units.Any(u => u.Posture == Posture.Lying && u.Dormant && u.WakeDistance == 1);
                if (encounter.Roles.Any(r => r.Role == "patrol"))
                {
                    patrolCamp = encounter; patrolBoard = world.Definition.Board;
                    var waypoints = encounter.Waypoints.Select(p => new Cell(p.Area.X, p.Area.Y)).ToHashSet();
                    Assert.All(encounter.Units.Where(u => u.Dormant), u => Assert.DoesNotContain(u.Anchor, waypoints));
                }
            }
        }
        Assert.True(sawEmpty); Assert.True(sawOccupied); Assert.True(sawLying);
        Assert.Contains("Connected", topologies); Assert.Contains("Separated", topologies);
        Assert.Contains("clearing-centered", layouts); Assert.Contains("roadside", layouts);
        Assert.NotNull(patrolCamp);
        var camp = patrolCamp!;
        var shell = new ScenarioDefinition { Board = patrolBoard!, Units = [], UnitTypeIds = [], Agency = [],
            Loot = LootSettings.Empty, Objectives = [new(HeroesVsMonsters.Monsters, ScenarioObjective.NoObjective)] };
        var state = GameEngine.CreateGame(PopulationGenerator.Combine(shell, [camp]));
        var dice = new NoCombatDice(); var result = GameEngine.StartRound(state, dice, false);
        for (var step = 0; step < 250 && result.State.Orders.Single(o => o.Kind == OrderKind.PatrolRoute).Route!.Direction == 1; step++)
            result = result.State.RoundComplete ? GameEngine.StartRound(result.State, dice, false) : GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), dice, false);
        Assert.Equal(-1, result.State.Orders.Single(o => o.Kind == OrderKind.PatrolRoute).Route!.Direction);
        Assert.All(result.State.Units.Where(u => camp.Units.Single(p => p.UnitId == u.Id).Dormant), u => Assert.True(u.Dormant));
        var noPopulation = WorldGenerator.Generate(Recipe() with { Civilization = new(Campsites: 1, Settlements: 0), Population = new(PopulationDensity.Off) });
        Assert.NotEmpty(noPopulation.Context.Sites); Assert.Empty(noPopulation.Definition.Units);
    }

    [Fact]
    public void LootUsesSelectedRepeatedDeckAndEmptyNeverCreatesDrawMarkers()
    {
        var empty = WorldGenerator.Generate(Recipe() with { LootPreset = "Empty" });
        Assert.Empty(empty.Definition.LootMarkers ?? []);
        var world = WorldGenerator.Generate(Recipe() with { LootPreset = "All" });
        Assert.NotEmpty(world.Definition.LootMarkers!);
        Assert.Equal("All", world.Definition.Loot!.Preset);
        var state = GameEngine.CreateGame(world.Definition);
        Assert.True(state.LootDeck!.DrawPile.Count >= state.LootMarkers.Count);
        Assert.True(world.Definition.Loot.PresetMultiplier > 1);
        var labels = world.Context.CurrentAnalysis!.WalkableLabels;
        Assert.All(world.Definition.LootMarkers!, marker => Assert.True(labels[marker.Position.Y * 60 + marker.Position.X] >= 0));
        var roundtrip = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition));
        Assert.Equal(world.Definition.LootMarkers, roundtrip.LootMarkers);
        Assert.Equal(world.Definition.Loot.PresetMultiplier, roundtrip.Loot!.PresetMultiplier);
    }

    [Fact]
    public void SuppliedCustomLootRetainsItsFiniteCompositionAndExistingReservations()
    {
        var context = new WorldGenerationContext(new(12345, new(0, 0, 0)), new(Width: 60, Height: 50))
        { Loot = new() { Composition = [new(LootCardKind.PocketBreakfast, 1)] } };
        context.SampleNaturalTerrain(); context.CurrentAnalysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
        var definition = context.Materialize() with { LootMarkers = [new("authored-cache", new(0, 0))],
            UnitTypeIds = [UnitTypeIds.Goblin], Units = [new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, new(7, 7), Dormant: true, WakeDistance: 0)],
            Agency = [new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, ControllerKind.Automated)] };
        context.ConstructionBoard = new PlanningBoard(definition, ContentBundle.Canonical());
        context.Population = PopulationGenerator.Generate(context, new(PopulationDensity.High));
        Assert.Empty(context.Population.LootMarkers);
        var final = context.Materialize(); Assert.Equal("authored-cache", Assert.Single(final.LootMarkers!).Id);
        Assert.Null(final.Loot!.Preset); Assert.Equal(1, final.Loot.ResolveComposition().Sum(r => r.Count));
        var state = GameEngine.CreateGame(final);
        Assert.Contains(state.Units[0].Id, context.Population.AlertComponents.SelectMany(c => c.UnitIds));
        Assert.All(context.Population.AlertComponents.SelectMany(c => c.UnitIds), id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void PopulationPreservesGeometryAndAllowsTemporaryChokepointOccupation()
    {
        // A single passable line has no bypass; every occupied inner Cell is a
        // combat chokepoint, not a permanent geometric disconnection.
        var recipe = Recipe(12, width: 60, height: 1);
        var plain = WorldGenerator.Generate(recipe with { Population = null });
        var populated = WorldGenerator.Generate(recipe);
        Assert.NotEmpty(populated.Definition.Units);
        Assert.Equal(JsonSerializer.Serialize(plain.Definition.Board), JsonSerializer.Serialize(populated.Definition.Board));
        Assert.Contains(populated.Definition.Units, u => u.Anchor.X > 0 && u.Anchor.X < 59);
        _ = GameEngine.CreateGame(populated.Definition);
    }

    [Fact]
    public void FullyProtectedSpaceReportsBoundedShortfallWithoutPublishingUnits()
    {
        var context = new WorldGenerationContext(new(42, new(0, 0, 0)), new(Width: 60, Height: 50)); context.SampleNaturalTerrain();
        context.CurrentAnalysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
        context.PopulationProtectedAreas.Add(GeneratedArea.From("protected", Enumerable.Range(0, 3000), 60));
        var result = PopulationGenerator.Generate(context, new(PopulationDensity.High));
        Assert.Empty(result.Encounters); Assert.Equal(result.Statistics.Target, result.Statistics.Shortfall);
        Assert.NotEmpty(result.Failures); Assert.InRange(result.Statistics.Attempts, 0, PopulationGenerator.MaxAttempts);
    }

    [Fact]
    public void TopologyUsesCompleteFootprintsAwakeRelaysAndSameSideOnly()
    {
        UnitPlacement U(string id, string type, int x, bool dormant = true, string side = "red") => new(type, side, new(x, 0), UnitId: id, Dormant: dormant);
        var units = new[] { U("dragon", UnitTypeIds.RedDragon, 0), U("relay", UnitTypeIds.Goblin, 4, false), U("last", UnitTypeIds.Zombie, 7), U("hostile", UnitTypeIds.Goblin, 5, side: "blue") };
        var graph = PopulationTopology.Components(units, ContentBundle.Canonical().Units);
        Assert.Equal(2, graph.Count); Assert.Contains(graph, c => c.UnitIds.Count == 3);
        Assert.Equal(3, PopulationTopology.Components(units.Where(u => u.UnitId != "relay"), ContentBundle.Canonical().Units).Count);
    }

    [Fact]
    public void DensePlacementValidatesEveryCellAndInternalEdgeOfLargeFootprints()
    {
        var content = ContentBundle.Canonical(); var pool = HeroesVsMonsters.Pool.Select(p => p.UnitTypeId).ToHashSet();
        content = content with { Units = [.. content.Units.Select(t => pool.Contains(t.Id) ? t with { Footprint = Footprint.TwoByTwo } : t)] };
        var world = WorldGenerator.Generate(Recipe(width: 40, height: 35), content);
        Assert.NotEmpty(world.Definition.Units); var state = GameEngine.CreateGame(world.Definition, content);
        var cells = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToArray();
        Assert.Equal(state.Units.Count * 4, cells.Length); Assert.Equal(cells.Length, cells.Distinct().Count());
        Assert.All(cells, c => { Assert.InRange(c.X, 0, 39); Assert.InRange(c.Y, 0, 34); });
    }

    [Fact]
    public void GuardAreaContainsTheCompleteReceivingFootprint()
    {
        var content = ContentBundle.Canonical();
        var placements = new[] { (UnitTypeIds.RedDragon, 44, "central", "chamber", (string?)null) };
        var encounter = PopulationComposition.FromRoles("fixture", "fixture", "chamber", 10, placements, 1, new(42, 0),
            types: content.Units.ToDictionary(t => t.Id));
        Assert.Equal(new ObjectiveArea(4, 4, 2, 2), Assert.Single(encounter.Orders).Area);
    }

    private sealed class NoCombatDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 1;
    }
}
