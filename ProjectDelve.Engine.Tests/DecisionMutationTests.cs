using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DecisionMutationTests
{
    private sealed class Choice(Func<DecisionRequest, string?> choose) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choose(request);
    }

    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Unexpected attack.");
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Unexpected defence.");
    }

    private static EngineResult Pending(DecisionKind kind, IRandomProvider random)
    {
        var state = new GameState
        {
            Physical = new PhysicalState(new Board(3, 3,
                [new Edge(new Cell(0, 0), new Cell(1, 0), EdgeKind.Wall)]),
                [new Figure("hero", new Cell(0, 0)), new Figure("enemy", new Cell(0, 2)),
                    new Figure("friend", new Cell(2, 0))]),
            Types = [new UnitType("hero-type", 3, 2, 1, 0, 2) { Unique = true }, new UnitType("other", 0, 0, 0, 0, 1)],
            Units = [new Unit("hero", "hero-type", "blue", 2), new Unit("enemy", "other", "red", 1),
                new Unit("friend", "other", "blue", 1)]
        };
        if (kind == DecisionKind.SelectUnit)
        {
            state.Types[0] = state.Types[0] with { Hp = 1, Unique = false };
            state.Units[0] = state.Units[0] with { CurrentHp = 1 };
            state.Units.Add(new Unit("ally", "hero-type", "blue", 1));
            state.Physical.Figures.Add(new Figure("ally", new Cell(2, 2)));
        }
        var result = TestGame.StartRound(state, random);
        if (kind == DecisionKind.Act)
        {
            result = TestGame.Advance(result.State, new Choice(_ => "stay"), random);
        }
        Assert.Equal(kind == DecisionKind.SelectUnit ? kind : DecisionKind.Activation, result.NextInput!.Kind);
        return result;
    }

    [Theory]
    [InlineData("off-board")]
    [InlineData("wall")]
    [InlineData("intermediate-wall")]
    [InlineData("replace")]
    [InlineData("clear")]
    public void ProviderCannotChangeMovementDestinationOrCanonicalPath(string mutation)
    {
        var random = new Random();
        var result = Pending(DecisionKind.Move, random);
        var original = JsonSerializer.Serialize(result.State);
        Cell[] canonical = [new Cell(0, 0), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1)];

        var advanced = TestGame.Advance(result.State, new Choice(request =>
        {
            var candidate = request.Candidates.Single(c => c.Key == "2,1");
            switch (mutation)
            {
                case "off-board":
                    candidate.Path![^1] = new Cell(20, 20);
                    break;
                case "wall":
                    candidate.Path!.Clear();
                    candidate.Path.AddRange([new Cell(0, 0), new Cell(1, 0)]);
                    break;
                case "intermediate-wall":
                    candidate.Path![1] = new Cell(1, 0); // Legal endpoint, illegal route.
                    break;
                case "replace":
                    request.Candidates[request.Candidates.IndexOf(candidate)] =
                        new Candidate(candidate.Key, new Cell(20, 20), [new Cell(20, 20)]);
                    break;
                case "clear":
                    request.Candidates.Clear();
                    break;
            }
            return candidate.Key;
        }), random);

        Assert.Equal(new Cell(2, 1), advanced.State.Physical.Figures.Single(f => f.Id == "hero").Position);
        Assert.Equal(canonical, Assert.Single(TestGame.OperationEvents(advanced), e => e.Kind == "MovementCompleted" && e.UnitId == "hero").Path);
        Assert.Equal(original, JsonSerializer.Serialize(result.State));
    }

    [Fact]
    public void ResumedMovementRebuildsCandidatesAndPathsFromState()
    {
        var random = new Random();
        var result = Pending(DecisionKind.Move, random);
        result.NextInput!.Candidates.Single(c => c.Key == "2,1").Path![1] = new Cell(1, 0);
        result.NextInput.Candidates.Add(new Candidate("20,20", new Cell(20, 20), [new Cell(20, 20)]));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        Cell[] canonical = [new Cell(0, 0), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1)];

        Assert.Throws<ArgumentException>(() => TestGame.Advance(restored, new Choice(_ => "20,20"), random));
        var advanced = TestGame.Advance(restored, new Choice(request =>
        {
            Assert.DoesNotContain(request.Candidates, c => c.Key == "20,20");
            Assert.Equal(canonical, request.Candidates.Single(c => c.Key == "2,1").Path);
            return "2,1";
        }), random);
        Assert.Equal(canonical, Assert.Single(TestGame.OperationEvents(advanced), e => e.Kind == "MovementCompleted" && e.UnitId == "hero").Path);
    }

    [Theory]
    [InlineData(DecisionKind.SelectUnit)]
    [InlineData(DecisionKind.Move)]
    [InlineData(DecisionKind.Act)]
    public void ProviderCannotInsertIllegalCandidate(DecisionKind kind)
    {
        var random = new Random();
        var result = Pending(kind, random);
        var original = JsonSerializer.Serialize(result.State);
        var forged = kind == DecisionKind.Move
            ? new Candidate("20,20", new Cell(20, 20), [new Cell(0, 0), new Cell(20, 20)])
            : new Candidate("friend"); // Wrong activation type or friendly attack target.

        Assert.Throws<ArgumentException>(() => TestGame.Advance(result.State,
            new Choice(request =>
            {
                request.Candidates.Clear();
                request.Candidates.Add(forged);
                return forged.Key;
            }), random));
        Assert.Equal(original, JsonSerializer.Serialize(result.State));
    }
}
