using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BeaconGenerationTests
{
    private static WorldGenerationRecipe Recipe(int width = 30, int height = 30) => new(new(0, 0, width, height), new(0, 0, 0),
        4291, Civilization: new(Campsites: ContentDensity.None, Settlements: ContentDensity.None));
    private static ScenarioDefinition Empty(int width, int height) => new()
    {
        Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass }, Units = [], UnitTypeIds = [], Agency = []
    };

    [Theory]
    [InlineData(30, 30, 1)]
    [InlineData(120, 120, 4)]
    public void CurrentSeedAndTransportReproduceStandaloneMachines(int width, int height, int expected)
    {
        var first = WorldGenerator.Generate(Recipe(width, height));
        var second = WorldGenerator.Generate(first.Code);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(first.Preview), JsonSerializer.Serialize(second.Preview));
        var json = ScenarioDefinitionJson.ToJson(first.Definition);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(first.Definition))));
        var machines = first.Definition.Machines!;
        Assert.Equal(expected, machines.Count);
        Assert.Empty(first.Context.Sites);
        var stats = first.Context.CivilizationStats!;
        Assert.Equal(expected, stats.BeaconsPlaced); Assert.Equal(0, stats.BeaconsOmitted);
        Assert.InRange(stats.BeaconAttempts, expected, expected * 96);
        Assert.InRange(stats.BeaconWork, expected * 25, expected * 96 * 25);
        var board = first.Context.ConstructionBoard!;
        Assert.All(machines, machine =>
        {
            Assert.Equal(MachineTypes.Beacon, machine.Type); Assert.Null(machine.OwnerSide); Assert.Empty(machine.UseCounts);
            var i = machine.Position.Y * width + machine.Position.X;
            Assert.Equal("stone-floor", board.TerrainAt(i)); Assert.False(board.Passable(i));
            Assert.Equal(-1, first.Context.CurrentAnalysis!.WalkableLabels[i]);
            Assert.Contains(board.Neighbors(i), board.Passable);
        });
        Assert.Empty(first.Preview.Frames.First().Machines);
        Assert.Equal(expected, first.Preview.Frames.Last().Machines.Count);
    }

    [Fact]
    public void OpenPadPreservesConnectivityForOrdinaryAndTwoByTwoFiguresAndPopulationAvoidsMachines()
    {
        var world = WorldGenerator.Generate(Recipe() with { Population = new(PopulationDensity.High) });
        var board = world.Context.ConstructionBoard!;
        Assert.NotEmpty(world.Definition.Machines!);
        Assert.Equal(1, world.Context.CurrentAnalysis!.Connectivity.ComponentCount);
        foreach (var size in new[] { 1, 2 })
        {
            var legal = Enumerable.Range(0, board.Width * board.Height).Where(i => i % board.Width + size <= board.Width &&
                i / board.Width + size <= board.Height && Enumerable.Range(0, size).All(y => Enumerable.Range(0, size).All(x => board.Passable(i + y * board.Width + x)))).ToHashSet();
            var remaining = new HashSet<int>(legal); var pending = new Queue<int>();
            var start = remaining.First(); remaining.Remove(start); pending.Enqueue(start);
            while (pending.TryDequeue(out var i)) foreach (var j in board.Neighbors(i)) if (remaining.Remove(j)) pending.Enqueue(j);
            Assert.Empty(remaining);
        }
        var state = GameEngine.CreateGame(world.Definition);
        var occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToHashSet();
        Assert.All(world.Definition.Machines!, m => Assert.DoesNotContain(m.Position, occupied));
        Assert.All(world.Context.Population!.Encounters.SelectMany(e => e.Waypoints),
            p => Assert.DoesNotContain(world.Definition.Machines!, m => m.Position.X >= p.Area.X && m.Position.X < p.Area.X + p.Area.W &&
                m.Position.Y >= p.Area.Y && m.Position.Y < p.Area.Y + p.Area.H));
    }

    [Fact]
    public void NarrowPassageIsOmittedWithBoundedWorkAndNoTerrainChanges()
    {
        var world = WorldGenerator.Generate(Recipe(2, 60));
        Assert.Empty(world.Definition.Machines ?? []);
        Assert.Equal(1, world.Context.CivilizationStats!.BeaconsOmitted);
        Assert.Equal(96, world.Context.CivilizationStats.BeaconAttempts);
        Assert.Equal(2400, world.Context.CivilizationStats.BeaconWork);
        Assert.Empty(world.Preview.Frames.Single(f => f.Stage == "Civilization").ChangedCells);
    }

    [Fact]
    public void BeaconPadKeepsProtectedRoutesAndTheirTwoCellApproachesClear()
    {
        var context = new WorldGenerationContext(new(4291, new(0, 0, 0)), new(0, 0, 30, 30)); context.SampleNaturalTerrain();
        var board = new PlanningBoard(context.Materialize(), ContentBundle.Canonical());
        var corridor = Enumerable.Range(0, 30).Select(y => y * 30 + 15).ToArray();
        var route = board.BeginDraft(new(0, 0, 30, 30), "route", new()); route.EnableRoutePlanning();
        route.ReserveRoute(corridor, "protected-route"); Assert.True(board.TryCommit(route.BuildPatch(), out _));
        var before = corridor.Select(board.TerrainAt).ToArray();
        var step = new WorldGenerationStep("civilization", WorldStepKind.Civilization, new CivilizationStepParameters(new()), []);
        var stats = BeaconGenerator.Apply(context, step, board, new(0, 0, 30, 30), "test");
        Assert.Equal(1, stats.Placed);
        Assert.Equal(before, corridor.Select(board.TerrainAt));
        Assert.All(board.Export().Machines!, m => Assert.True(Math.Abs(m.Position.X - 15) > 2));
        Assert.All(corridor, i => Assert.True(board.Passable(i)));
    }

    [Fact]
    public void MachineDraftIsAtomicDetachedAndCannotBeRoutedThroughOrRepainted()
    {
        var board = new PlanningBoard(Empty(5, 1), ContentBundle.Canonical());
        var draft = board.BeginDraft(new(0, 0, 5, 1), "beacon", new());
        Assert.Throws<ArgumentException>(() => draft.PlaceMachine(new("beacon", new(2, 0), MachineTypes.Beacon)));
        draft.SetTerrain(2, "stone-floor"); draft.PlaceMachine(new("beacon", new(2, 0), MachineTypes.Beacon));
        Assert.True(board.Passable(2)); Assert.False(draft.Passable(2)); Assert.Empty(board.Export().Machines ?? []);
        var patch = draft.BuildPatch(); Assert.Single(patch.Machines);
        Assert.True(board.TryCommit(patch, out var error), error);
        var export = board.Export(); export.Machines!.Clear(); Assert.Single(board.Export().Machines!);
        var route = board.BeginDraft(new(0, 0, 5, 1), "route", new(32, 100000)); route.EnableRoutePlanning();
        Assert.False(route.CanRouteThrough(2)); Assert.False(route.CanWrite(2));
        Assert.Throws<ArgumentException>(() => route.SetTerrain(2, "dirt"));
        var request = new RouteRequest("test", new(0, 0, 5, 1), new("start", [0]), new("end", [4]));
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, new(42, "routes", route)));
    }
}
