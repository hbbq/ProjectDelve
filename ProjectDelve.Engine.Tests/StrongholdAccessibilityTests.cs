using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class StrongholdAccessibilityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(18, 18)] [InlineData(30, 24)] [InlineData(50, 50)]
    [InlineData(18, 50)] [InlineData(50, 18)]
    public void AuthoritativeSpatialQueriesConfirmMissionCachesAndLargeFootprints(int width, int height)
    {
        var largeCount = 0;
        for (ulong seed = 0; seed < 9; seed++)
        {
            var generated = DungeonGenerator.Generate(new GenerationCode(Width: width, Height: height,
                Profile: "OS", Objective: "ORA", Openness: 1 + (int)(seed % 3), Terrain: (int)(seed % 4),
                Treasure: 3, Density: 3, Difficulty: 3 + (int)(seed % 3), Seed: seed));
            var definition = generated.Definition;
            var state = GameEngine.CreateGame(definition);
            var board = state.Physical.Board;
            // Setup geometry, with Doors opened normally; combat and FoW are deliberately
            // excluded from this audit. The engine's spatial queries are independent of
            // the generator's own flood and footprint implementation.
            var reachable = ApproachRules.Distances(board, definition.Units[0].Anchor, closedDoorsTraversable: true);
            var objective = definition.Objectives!.Single(o => o.SideId == HeroesVsMonsters.Heroes);
            var area = objective.Area!;
            Assert.True(reachable.Keys.Count(c => c.X >= area.X && c.X < area.X + area.W &&
                c.Y >= area.Y && c.Y < area.Y + area.H) >= 4, generated.Code.ToString());
            Assert.All(definition.LootMarkers!, marker => Assert.Contains(marker.Position, reachable.Keys));

            foreach (var unit in state.Units.Where(u => FootprintGeometry.FootprintOf(state, u.Id) == Footprint.TwoByTwo))
            {
                largeCount++;
                var start = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
                Assert.True(SpatialRules.Fits(board, Footprint.TwoByTwo, start));
                HashSet<Cell> visited = [start]; Queue<Cell> queue = new(); queue.Enqueue(start);
                while (queue.TryDequeue(out var current))
                    foreach (var next in MovementRules.Neighbors(current))
                        if (!visited.Contains(next) && MovementRules.CanStep(board, Footprint.TwoByTwo, current, next,
                                new MovementTraversal(ClosedDoorsTraversable: true)))
                        { visited.Add(next); queue.Enqueue(next); }
                Assert.True(visited.Count >= 8, $"{generated.Code}: {unit.TypeId} has only {visited.Count} reachable anchors.");
            }
        }
        output.WriteLine($"{width}x{height}: 9 maps, mission/caches accessible; {largeCount} large footprints independently checked.");
    }
}
