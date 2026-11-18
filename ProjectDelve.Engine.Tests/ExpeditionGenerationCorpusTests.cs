using System.Diagnostics;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionGenerationCorpusTests(ITestOutputHelper output)
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();
    private static readonly Cell Entry = new(0, 30), Exit = new(59, 20);
    private static readonly ObjectiveArea Pad = new(57, 19, 2, 3);

    // Dedicated generation fixtures: no assertions depend on mutable playtest scenarios.
    private static IEnumerable<(string Name, WorldGenerationRecipe Recipe)> Corpus()
    {
        yield return ("FullDefault", Full(new(), 17) with { Civilization = new(), Routes = new(), Population = new() });
        yield return ("DenseGrassland", Full(new(10, 4, 5, FeatureScale.Broad, WaterForm.Lakes), 42) with
        { Routes = new(ThroughRoutes: ContentDensity.High, Profile: RouteProfileKind.Road, Alternatives: ContentDensity.High) });
        yield return ("RockyWilds", Full(WorldGenerationPresets.All.Single(p => p.Id == "RockyWilds").Settings, 42));
        yield return ("RoughRock17", Full(new(0, 0, 75, FeatureScale.Broad), 17));
        yield return ("RoughRock42", Full(new(0, 0, 75, FeatureScale.Broad), 42));
        yield return ("DifficultWater", Full(new(5, 70, 20, FeatureScale.Broad, WaterForm.Mixed), 17) with
        { Routes = new(ThroughRoutes: ContentDensity.High, WaterCrossings: true, Alternatives: ContentDensity.High) });
    }

    private static WorldGenerationRecipe Full(NaturalTerrainSettings nature, ulong seed) =>
        new(new(-60, 0, 60, 60), nature, seed, new(),
            new(Campsites: ContentDensity.High, Settlements: ContentDensity.High),
            new(ThroughRoutes: ContentDensity.Medium, Alternatives: ContentDensity.Medium, WaterCrossings: true),
            new(PopulationDensity.High, 4));

    [Fact]
    public void FullPipelineCorpusRetainsFeaturesAndUnoccupiedOrdinaryExitAccess()
    {
        var settlements = 0; var camps = 0; var dungeons = 0; var buildings = 0; var routes = 0; var monsters = 0;
        var siteEncounters = 0; var siteLoot = 0; var detours = 0; var deadEnds = 0; var broadSpaces = 0;
        foreach (var (name, recipe) in Corpus())
        {
            var timer = Stopwatch.StartNew();
            output.WriteLine($"{name}: {recipe}; local seed 123");
            var execution = Execute(recipe);
            var context = execution.Context;
            var definition = context.Materialize();
            var firstGenerationSeconds = timer.Elapsed.TotalSeconds;
            var repeat = Execute(recipe).Context.Materialize();
            Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(repeat));
            Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(
                ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))));
            var state = GameEngine.CreateGame(definition, Content);
            var validation = PlayReadyScenarioSetup.Validate(definition, state);
            Assert.True(validation.Valid, string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
            var setup = definition.Setup!;
            var occupied = definition.Units.SelectMany(u => FootprintGeometry.OccupiedCells(
                Content.Units.Single(t => t.Id == u.UnitTypeId).Footprint, u.Anchor)).ToHashSet();
            Assert.DoesNotContain(setup.Deployment.Cells, occupied.Contains);
            Assert.DoesNotContain(context.SiteLootMarkers, m => setup.Deployment.Cells.Contains(m.Position));
            var geometry = AccessibilityAnalysis.Capture(state.Physical.Board with
            { Edges = state.Physical.Board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
            var distances = Distances(geometry, Entry, occupied);
            foreach (var cell in setup.RequiredLocations.Where(l => l.Id.StartsWith("expedition/", StringComparison.Ordinal))
                         .SelectMany(l => l.Groups).SelectMany(g => g.Positions))
                Assert.True(distances.ContainsKey(cell), $"{name}: initial occupied geometry blocks {cell}");
            detours += distances[Exit] > Math.Abs(Entry.X - Exit.X) + Math.Abs(Entry.Y - Exit.Y) ? 1 : 0;
            var reachableDeadEnds = distances.Keys.Count(c => Neighbors(c).Count(n => distances.ContainsKey(n) && geometry.CanStep(ConstructionCapabilities.OrdinaryLand, c, n)) == 1);
            deadEnds += reachableDeadEnds;
            broadSpaces += distances.Keys.Count(c => geometry.CanStep(ConstructionCapabilities.OrdinaryLand, c, new(c.X + 1, c.Y)) &&
                geometry.CanStep(ConstructionCapabilities.OrdinaryLand, c, new(c.X, c.Y + 1)) &&
                geometry.CanStep(ConstructionCapabilities.OrdinaryLand, new(c.X + 1, c.Y), new(c.X + 1, c.Y + 1)) &&
                geometry.CanStep(ConstructionCapabilities.OrdinaryLand, new(c.X, c.Y + 1), new(c.X + 1, c.Y + 1)));
            foreach (var route in context.Routes)
            {
                var path = route.Geometry.LocalIndices(60, recipe.Window).Distinct().Select(i => new Cell(i % 60, i / 60)).ToArray();
                Assert.NotEmpty(path);
                Assert.All(path, c => Assert.True(geometry.CanStop(ConstructionCapabilities.OrdinaryLand, c)));
                for (var i = 1; i < path.Length; i++) Assert.True(geometry.CanStep(ConstructionCapabilities.OrdinaryLand, path[i - 1], path[i]));
                Assert.InRange(route.RealizedWidth, 1, 2);
            }
            settlements += context.Sites.Count(s => s.Kind == GeneratedSiteKind.Settlement);
            camps += context.Sites.Count(s => s.Kind == GeneratedSiteKind.Campsite);
            dungeons += context.Sites.Count(s => s.MountainDungeon is not null);
            var buildingCount = context.Sites.Sum(s => s.Buildings.Count);
            buildings += buildingCount;
            routes += context.Routes.Count; monsters += definition.Units.Count; siteLoot += context.SiteLootMarkers.Count;
            siteEncounters += context.Population!.Encounters.Count(e => context.Sites.Any(s => s.Id == e.SourceId ||
                s.PopulationIntent?.EncounterRegions.Any(r => r.Id == e.SourceId) == true || s.Buildings.Any(b => b.Id == e.SourceId)));
            Assert.All(context.Sites, s => Assert.NotNull(s.PopulationIntent));
            var occupancyRejections = context.Population.Failures.Count(f => f.Reason.Contains("disconnect ordinary Expedition", StringComparison.Ordinal));
            output.WriteLine($"firstGeneration={firstGenerationSeconds:F2}s, withRepeatAndChecks={timer.Elapsed.TotalSeconds:F2}s: sites={context.Sites.Count}, buildings={buildingCount}, dungeons={context.Sites.Count(s => s.MountainDungeon is not null)}, routes={context.Routes.Count}, width1={context.Routes.Count(r => r.RealizedWidth == 1)}, width2={context.Routes.Count(r => r.RealizedWidth == 2)}, units={definition.Units.Count}, siteLoot={context.SiteLootMarkers.Count}, distance={distances[Exit]}, reachableDeadEnds={reachableDeadEnds}, occupancyRejections={occupancyRejections}");
        }
        Assert.True(settlements > 0); Assert.True(camps > 0); Assert.True(dungeons > 0); Assert.True(buildings > 0);
        Assert.True(routes > 0); Assert.True(monsters > 0); Assert.True(siteEncounters > 0); Assert.True(siteLoot > 0);
        Assert.True(detours > 0); Assert.True(deadEnds > 0); Assert.True(broadSpaces > 20);
    }

    [Fact]
    public void SiteRewardSelectionIsIndependentOfPopulationAndOrdinaryGenerationStillUsesItsOwnSetup()
    {
        var rewardedVacancy = false; var shifted = false;
        foreach (var (name, recipe) in Corpus().Take(3))
        {
            var populated = Execute(recipe).Context;
            var vacant = Execute(recipe with { Population = new(PopulationDensity.Off) }).Context;
            Assert.Empty(vacant.Materialize().Units);
            Assert.Equal(populated.SiteLootStatistics!.SelectedLocations, vacant.SiteLootStatistics!.SelectedLocations);
            Assert.Equal(populated.SiteLootStatistics.UnrewardedLocations, vacant.SiteLootStatistics.UnrewardedLocations);
            Assert.Equal(populated.Sites.Select(s => s.Id), vacant.Sites.Select(s => s.Id));
            rewardedVacancy |= vacant.SiteLootMarkers.Count > 0;
            var ordinary = WorldGenerator.Generate(recipe).Context;
            var commonSites = ordinary.Sites.Select(s => string.Join(',', s.Footprint.Indices(60).Order()))
                .Intersect(populated.Sites.Select(s => string.Join(',', s.Footprint.Indices(60).Order()))).Count();
            var changedTerrain = ordinary.Samples.Zip(populated.Samples).Count(pair => pair.First.TerrainTypeId != pair.Second.TerrainTypeId);
            shifted |= !ordinary.Deployment!.PlacementOrder.SequenceEqual(populated.Deployment!.PlacementOrder);
            output.WriteLine($"{name}: ordinary sites/routes={ordinary.Sites.Count}/{ordinary.Routes.Count}; expedition={populated.Sites.Count}/{populated.Routes.Count}; identical Site footprints={commonSites}; changed terrain={changedTerrain}/3600; Site Loot on/off={populated.SiteLootMarkers.Count}/{vacant.SiteLootMarkers.Count}");
        }
        Assert.True(rewardedVacancy); Assert.True(shifted);
    }

    private static WorldGenerationExecution Execute(WorldGenerationRecipe recipe) =>
        ExpeditionRegionGeometry.Execute(recipe, Entry, Exit, Pad, 123, Content);

    [Fact]
    public void InitialFigureInOnlyOpenableDoorwayBlocksAccessWithoutChangingTerrainConnectivity()
    {
        var context = Execute(Full(new(0, 0, 0), 17) with
        { Civilization = new(Enabled: false), Routes = new(Enabled: false), Population = new(PopulationDensity.Off) }).Context;
        var original = context.Materialize();
        var definition = original with
        {
            Board = original.Board with
            {
                // A dedicated physical partition, with a single ordinary openable doorway.
                Cells = Enumerable.Range(0, 60).Where(y => y != 30)
                    .Select(y => new CellDefinition(new(30, y), TerrainKind.SolidRock)).ToList(),
                Edges = [new(new(29, 30), EdgeDirection.Right, EdgeKind.ClosedDoor)]
            },
            UnitTypeIds = [UnitTypeIds.Zombie],
            Units = [new(UnitTypeIds.Zombie, "monsters", new(30, 30), Dormant: true)],
            Agency = [new(UnitTypeIds.Zombie, "monsters", ControllerKind.Automated)]
        };
        var state = GameEngine.CreateGame(definition, Content);
        Assert.Contains(Exit.Y * 60 + Exit.X, PlayReadyScenarioSetup.ReachableFromDeployment(definition, state));
        Assert.False(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
        var movedAside = GameEngine.CreateGame(definition with
        { Units = [definition.Units[0] with { Anchor = new(31, 29) }] }, Content);
        Assert.True(context.Expedition.HasOccupiedAccess(movedAside, context.Deployment!));
    }

    private static Dictionary<Cell, int> Distances(AccessibilityAnalysis geometry, Cell start, HashSet<Cell> occupied)
    {
        Dictionary<Cell, int> result = new() { [start] = 0 }; Queue<Cell> pending = new([start]);
        while (pending.TryDequeue(out var current))
            foreach (var next in Neighbors(current))
                if (!occupied.Contains(next) && !result.ContainsKey(next) && geometry.CanStep(ConstructionCapabilities.OrdinaryLand, current, next))
                { result[next] = result[current] + 1; pending.Enqueue(next); }
        return result;
    }

    private static IEnumerable<Cell> Neighbors(Cell c) => [new(c.X - 1, c.Y), new(c.X + 1, c.Y), new(c.X, c.Y - 1), new(c.X, c.Y + 1)];
}
