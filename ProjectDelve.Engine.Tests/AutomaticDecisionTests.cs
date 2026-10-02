using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AutomaticDecisionTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public int Calls { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries)
        {
            Calls++;
            return key ?? (request.Kind == DecisionKind.Activation
                ? request.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key : null);
        }
    }

    private sealed class UnexpectedChoice : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => throw new InvalidOperationException("No choice expected.");
    }

    private static GameState State(int width = 2, int height = 1, int mov = 1, int rng = 0, int atk = 0) => new()
    {
        Physical = new PhysicalState(new Board(width, height, []), [new Figure("hero", new Cell(0, 0))]),
        Types = [new UnitType("hero-type", mov, rng, atk, 0, 2)],
        Units = [new Unit("hero", "hero-type", "blue", 2)]
    };

    private static void AddEnemy(GameState state, string id, Cell position)
    {
        if (state.Types.All(t => t.Id != "enemy-type"))
            state.Types.Add(new UnitType("enemy-type", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit(id, "enemy-type", "red", 1));
        state.Physical.Figures.Add(new Figure(id, position));
    }

    [Fact]
    public void SoleRequiredUnitIsSelectedAutomatically_UntilMovementNeedsInput()
    {
        var state = State();
        var original = JsonSerializer.Serialize(state);
        var result = GameEngine.StartRound(state, new Random());

        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("hero", result.NextInput.UnitId);
        Assert.False(result.State.MoveDone);
        Assert.Equal("hero", result.State.CurrentUnitId);
        Assert.Same(result.NextInput, result.State.Pending);
        Assert.Equal(original, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void OnlyStayAndEndTurn_AutoResolveAndCompleteRound()
    {
        var state = State(mov: 0, rng: 1, atk: 1);
        var result = GameEngine.StartRound(state, new Random());

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Null(result.State.Pending);
        Assert.Equal(new[] { "TokenDrawn", "MovementCompleted", "RoundCompleted" }, result.Events.Select(e => e.Kind));
        Assert.Equal(new[] { new Cell(0, 0) }, result.Events[1].Path);
        Assert.Equal(2, result.State.Units.Single().CurrentHp);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1,0")]
    public void MovementDestinationAndStay_RequireProvider(string? choice)
    {
        var random = new Random();
        var result = GameEngine.StartRound(State(), random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.NextInput.AllowsNone);
        Assert.Equal(2, result.NextInput.Candidates.Count);
        Assert.Contains(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Stay);
        Assert.Equal("1,0", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);

        var provider = new Choice(choice);
        result = GameEngine.Advance(result.State, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.True(result.State.RoundComplete);
        Assert.Equal(new Cell(choice is null ? 0 : 1, 0), result.State.Physical.Figures.Single().Position);
        Assert.Equal("MovementCompleted", result.Events[0].Kind);
        Assert.Equal("RoundCompleted", result.Events[^1].Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("attack:enemy")]
    public void AttackAndEndTurn_RequireProvider(string? choice)
    {
        var state = State(mov: 0, rng: 1, atk: 1);
        AddEnemy(state, "enemy", new Cell(1, 0));
        var random = new Random();
        var result = GameEngine.StartRound(state, random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.NextInput.AllowsNone);
        Assert.Equal(2, result.NextInput.Candidates.Count);
        Assert.Contains(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.Equal("attack:enemy", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackResolved");

        var provider = new Choice(choice);
        result = GameEngine.Advance(result.State, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.True(result.State.RoundComplete);
        Assert.Equal(choice is null ? 1 : 0, result.State.Units.Single(u => u.Id == "enemy").CurrentHp);
        Assert.Equal(choice is not null, result.Events.Any(e => e.Kind == "AttackResolved"));
    }

    [Theory]
    [InlineData(DecisionKind.SelectUnit)]
    [InlineData(DecisionKind.Move)]
    [InlineData(DecisionKind.Act)]
    public void MultipleCandidates_StillRequireExternalInput(DecisionKind kind)
    {
        var state = State(width: 3, height: 2, mov: kind == DecisionKind.Act ? 0 : 1, rng: 1, atk: 1);
        if (kind == DecisionKind.SelectUnit)
        {
            state.Units.Add(new Unit("ally", "hero-type", "blue", 2));
            state.Physical.Figures.Add(new Figure("ally", new Cell(1, 0)));
        }
        else if (kind == DecisionKind.Act)
        {
            AddEnemy(state, "enemy-a", new Cell(1, 0));
            AddEnemy(state, "enemy-b", new Cell(0, 1));
        }
        var random = new Random();
        var result = GameEngine.StartRound(state, random);

        Assert.Equal(kind == DecisionKind.SelectUnit ? kind : DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal(2, result.NextInput.Candidates.Count(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)));
        Assert.False(result.NextInput.AllowsNone);
        Assert.False(result.State.RoundComplete);
        var provider = new Choice(result.NextInput.Candidates[0].Key);
        GameEngine.Advance(result.State, provider, random);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void Advance_AutoprogressesThroughIdleTypeToNextAmbiguousDecision()
    {
        var state = State(width: 3, mov: 1);
        state.Types.Add(new UnitType("idle-type", 0, 0, 0, 0, 1));
        state.Types.Add(new UnitType("next-type", 1, 0, 0, 0, 1));
        state.Units.Add(new Unit("idle", "idle-type", "blue", 1));
        state.Units.Add(new Unit("next", "next-type", "blue", 1));
        state.Physical = state.Physical with
        {
            Board = new Board(3, 2, []),
            Figures = [.. state.Physical.Figures, new Figure("idle", new Cell(2, 1)), new Figure("next", new Cell(2, 0))]
        };
        var random = new Random();
        var result = GameEngine.StartRound(state, random);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        var provider = new Choice(null);
        result = GameEngine.Advance(restored, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("next", result.NextInput.UnitId);
        Assert.Equal(new[] { "idle-type", "next-type" }, result.Events.Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Equal(new[] { "hero", "idle" }, result.Events.Where(e => e.Kind == "MovementCompleted").Select(e => e.UnitId));
        Assert.False(result.State.RoundComplete);
    }

    [Theory]
    [InlineData(DecisionKind.SelectUnit)]
    [InlineData(DecisionKind.Move)]
    [InlineData(DecisionKind.Act)]
    public void ResumedForcedDecision_UsesRebuiltLegalityWithoutInvokingProvider(DecisionKind kind)
    {
        // Serialized Pending is informational; rebuild forced choices from progress.
        var state = State(mov: 0);
        state.Round = 1;
        state.ActiveTypeId = "hero-type";
        state.MoveDone = kind == DecisionKind.Act;
        state.CurrentUnitId = kind == DecisionKind.SelectUnit ? null : "hero";
        state.Pending = new DecisionRequest(kind, "hero-type", state.CurrentUnitId,
            [new Candidate("forged")], false);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

        var result = GameEngine.Advance(restored, new UnexpectedChoice(), new Random());

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackResolved");
    }
}
