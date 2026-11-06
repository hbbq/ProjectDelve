using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

// Small deterministic walks compare activation lead time; these are not combat-balance claims.
public sealed class WakeDistanceEvaluationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(5, false, false, 5)]
    [InlineData(8, false, false, 4)]
    [InlineData(12, false, false, 3)]
    [InlineData(5, true, false, 6)]
    [InlineData(8, true, false, 5)]
    [InlineData(12, true, false, 4)]
    [InlineData(5, false, true, 5)]
    [InlineData(8, false, true, 4)]
    [InlineData(12, false, true, 3)]
    [InlineData(5, true, true, 6)]
    [InlineData(8, true, true, 5)]
    [InlineData(12, true, true, 4)]
    public void CompareProximityAcrossOpenObstructedAndFog(int distance, bool obstructed, bool fog, int expectedRound)
    {
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 28, Height = 5,
                Edges = obstructed ? Enumerable.Range(1, 4).Select(y => new EdgeDefinition(new(14, y), EdgeDirection.Right, EdgeKind.Wall)).ToList() : [] },
            UnitTypeIds = ["walker", "guard"],
            UnitTypes = [new("walker", 4, 0, 0, 0, 1, Actions: UnitAction.None), new("guard", 4, 1, 2, 2, 1)],
            Units = [new("walker", "blue", new(0, 2), UnitId: "walker"),
                new("guard", "red", new(24, 2), UnitId: "guard", Dormant: true, WakeDistance: distance)],
            Agency = [new("walker", "blue", ControllerKind.Human), new("guard", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            FogOfWarSides = fog ? ["blue"] : [], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(scenario);
        var provider = new Walk(obstructed ? [new(14, 0), new(22, 0), new(22, 2)] : [new(22, 2)]);
        var random = new Dice();
        var decisionCount = 0;
        EngineResult? waking = null;
        for (var round = 1; round <= 8 && waking is null; round++)
        {
            var result = GameEngine.StartRound(state, random, false);
            while (!result.State.RoundComplete)
            {
                Assert.True(++decisionCount < 100, "The deterministic walk must make bounded progress.");
                result = GameEngine.Advance(result.State, provider, random, false);
                if (result.Events.Any(e => e.Kind == "UnitWoken")) { waking = result; break; }
                Assert.DoesNotContain(result.Events, e => e.Kind == "ActivationStarted" && e.UnitId == "guard");
            }
            state = result.State;
        }
        Assert.NotNull(waking);
        var position = waking.State.Physical.Figures.Single(f => f.Id == "walker").Position;
        var actualDistance = Math.Abs(24 - position.X) + Math.Abs(2 - position.Y);
        Assert.Equal(expectedRound, waking.State.Round);
        Assert.True(actualDistance <= distance);
        Assert.DoesNotContain(new("guard", "red"), waking.State.Bag);
        Assert.False(waking.State.Units.Single(u => u.Id == "guard").Dormant);
        output.WriteLine($"WakeDistance={distance}; obstructed={obstructed}; FoW={fog}; wake round={waking.State.Round}; raw distance={actualDistance}; decisions={decisionCount}");
    }

    private sealed class Walk(Cell[] waypoints) : IDecisionProvider
    {
        private int next;
        public string? Choose(DecisionRequest request, IGameplayQueries queries)
        {
            if (request.Kind == DecisionKind.SelectUnit) return request.Candidates[0].Key;
            var position = queries.PositionOf(request.UnitId!);
            while (next + 1 < waypoints.Length && position == waypoints[next]) next++;
            var target = waypoints[next];
            var move = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move)
                .OrderBy(c => Math.Abs(c.Destination!.X - target.X) + Math.Abs(c.Destination.Y - target.Y))
                .ThenBy(c => MovementRules.Cost(c.Path!)).ThenBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X).FirstOrDefault();
            return move?.Key ?? request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)?.Key
                ?? request.Candidates.First(c => c.Kind == ActivationChoiceKind.EndTurn).Key;
        }
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }
}
