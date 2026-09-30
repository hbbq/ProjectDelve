using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RandomMutationTests
{
    private sealed class MutatingRandom : IRandomProvider
    {
        public List<IReadOnlyList<string>> SuppliedBags { get; } = [];

        public string DrawToken(IReadOnlyList<string> bag)
        {
            SuppliedBags.Add(bag);
            var drawn = bag[0];
            var mutable = Assert.IsAssignableFrom<IList<string>>(bag);
            try
            {
                // Remove a different token while returning a valid draw.
                if (bag.Count > 1) mutable.RemoveAt(bag.Count - 1);
                else mutable.Clear();
            }
            catch (NotSupportedException)
            {
                // A read-only boundary rejects the attempted mutation.
            }
            return drawn;
        }

        public AttackFace RollAttackDie() => throw new InvalidOperationException("Unexpected attack.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Unexpected defence.");
    }

    private sealed class FirstChoice : IDecisionProvider
    {
        public string? Choose(DecisionRequest request) => request.Candidates[0].Key;
    }

    [Fact]
    public void ProviderCannotRemoveTokensOrSkipUnitTypeActivations()
    {
        var state = new GameState
        {
            Physical = new PhysicalState(new Board(3, 1, []),
                [new Figure("a", new Cell(0, 0)), new Figure("b", new Cell(1, 0)),
                    new Figure("c", new Cell(2, 0))]),
            Types = [new UnitType("a", 0, 0, 0, 0, 1), new UnitType("b", 0, 0, 0, 0, 1),
                new UnitType("c", 0, 0, 0, 0, 1)],
            Units = [new Unit("a", "a", "blue", 1), new Unit("b", "b", "blue", 1),
                new Unit("c", "c", "blue", 1)]
        };
        var original = JsonSerializer.Serialize(state);
        var random = new MutatingRandom();
        var result = GameEngine.StartRound(state, random);

        Assert.Equal(new[] { "b", "c" }, result.State.Bag);
        Assert.Equal(original, JsonSerializer.Serialize(state));
        var events = new List<RulesEvent>(result.Events);
        var selectedTypes = new List<string>();
        for (var step = 0; step < 9; step++) // Three phase selections per Unit Type.
        {
            Assert.Equal(DecisionKind.SelectUnit, result.NextInput!.Kind);
            selectedTypes.Add(result.NextInput.TypeId);
            // Resume through the existing serialization boundary as well.
            var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
            result = GameEngine.Advance(restored, new FirstChoice(), random);
            events.AddRange(result.Events);
        }

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Empty(result.State.Bag);
        Assert.Equal(new[] { "a", "b", "c" }, events.Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Equal(new[] { "a", "a", "a", "b", "b", "b", "c", "c", "c" }, selectedTypes);
        Assert.Equal(3, random.SuppliedBags.Count);
        // Retained provider inputs are snapshots, independent of later engine draws.
        Assert.Equal(new[] { "a", "b", "c" }, random.SuppliedBags[0]);
        Assert.Equal(new[] { "b", "c" }, random.SuppliedBags[1]);
        Assert.Equal(new[] { "c" }, random.SuppliedBags[2]);
    }
}
