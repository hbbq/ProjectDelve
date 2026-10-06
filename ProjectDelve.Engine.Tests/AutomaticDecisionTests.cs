using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AutomaticDecisionTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
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
        Types = [new UnitType("hero-type", mov, rng, atk, 0, 2) { Unique = true }],
        Units = [new Unit("hero", "hero-type", "blue", 2)]
    };

    private static void AddEnemy(GameState state, string id, Cell position)
    {
        if (state.Types.All(t => t.Id != "enemy-type"))
            state.Types.Add(new UnitType("enemy-type", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit(id, "enemy-type", "red", 1));
        state.Physical.Figures.Add(new Figure(id, position));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SoleLegalChoices_AutoResolveRegardlessOfRelevancePreference(bool enabled)
    {
        var random = new Random();
        var result = TestGame.StartRound(State(mov: 0), random, autoChooseSingleRelevantChoice: enabled);
        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(new[] { "TokenDrawn", "MovementCompleted", "RoundCompleted" }, TestGame.OperationEvents(result).Select(e => e.Kind));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefreshChoices_PreservesMultipleRelevantLegalChoices(bool enabled)
    {
        var random = new Random();
        var result = TestGame.StartRound(State(), random);
        var original = JsonSerializer.Serialize(result.State);
        var refreshed = GameEngine.RefreshChoices(result.State, random, autoChooseSingleRelevantChoice: enabled);
        Assert.False(refreshed.State.RoundComplete);
        Assert.Equal(JsonSerializer.Serialize(result.NextInput), JsonSerializer.Serialize(refreshed.NextInput));
        Assert.Empty(TestGame.OperationEvents(refreshed));
        Assert.Equal(original, JsonSerializer.Serialize(result.State));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SoleLegalChoice_IsAutomaticRegardlessOfPreferenceOrRelevance(bool enabled, bool relevant)
    {
        var request = new DecisionRequest(DecisionKind.Activation, "hero-type", "hero",
            [new Candidate("end-turn", Kind: ActivationChoiceKind.EndTurn, Relevant: relevant)], false);
        Assert.Equal("end-turn", GameEngine.SelectChoice(request, new UnexpectedChoice(),
            new GameplayQueries(State()), enabled));
        Assert.False(GameEngine.TryAutomaticChoice(request with { AllowsNone = true }, enabled, out _));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void AutomaticChoice_UsesRelevantSubsetWithoutRemovingLegalChoices(
        bool enabled, bool allowsNone, bool expectedAutomatic)
    {
        // Synthetic metadata isolates the automatic-choice policy from concrete content.
        var request = new DecisionRequest(DecisionKind.Activation, "hero-type", "hero",
            [new Candidate("optional", Relevant: false),
                new Candidate("end-turn", Kind: ActivationChoiceKind.EndTurn)], allowsNone);
        var original = JsonSerializer.Serialize(request);
        Assert.Equal(expectedAutomatic, GameEngine.TryAutomaticChoice(request, enabled, out var choice));
        Assert.Equal(expectedAutomatic ? "end-turn" : null, choice);
        Assert.Equal(original, JsonSerializer.Serialize(request));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultipleLegalChoicesWithZeroRelevantCandidates_StillRequireInput(bool allowsNone)
    {
        var request = new DecisionRequest(DecisionKind.Activation, "hero-type", "hero",
            [new Candidate("optional", Relevant: false), new Candidate("other", Relevant: false)], allowsNone);
        Assert.False(GameEngine.TryAutomaticChoice(request, true, out var choice));
        Assert.Null(choice);
    }

    [Theory]
    [InlineData("optional")]
    [InlineData("end-turn")]
    [InlineData("forged")]
    public void ExternalSelection_ReceivesAllLegalChoicesAndValidatesRegardlessOfRelevance(string key)
    {
        var request = new DecisionRequest(DecisionKind.Activation, "hero-type", "hero",
            [new Candidate("optional", Relevant: false),
                new Candidate("end-turn", Kind: ActivationChoiceKind.EndTurn)], false);
        var provider = new InspectChoice(supplied =>
        {
            Assert.Equal(request.Candidates, supplied.Candidates);
            // Provider mutations must not change authoritative validation.
            supplied.Candidates.Clear();
            supplied.Candidates.Add(new Candidate(key));
            return key;
        });
        if (key == "forged")
            Assert.Throws<ArgumentException>(() => GameEngine.SelectChoice(request, provider,
                new GameplayQueries(State()), false));
        else
            Assert.Equal(key, GameEngine.SelectChoice(request, provider, new GameplayQueries(State()), false));
        Assert.Equal(2, request.Candidates.Count);
        Assert.False(request.Candidates[0].Relevant);
    }

    private sealed class InspectChoice(Func<DecisionRequest, string?> choose) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choose(request);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyOptionalRequest_PreservesForcedNone(bool enabled)
    {
        var request = new DecisionRequest(DecisionKind.Move, "hero-type", "hero", [], true);
        Assert.True(GameEngine.TryAutomaticChoice(request, enabled, out var choice));
        Assert.Null(choice);
    }

    [Fact]
    public void PendingRelevanceIsRebuilt_AndDoesNotRestrictSubmission()
    {
        var random = new Random();
        var result = TestGame.StartRound(State(), random);
        Assert.All(result.NextInput!.Candidates, c => Assert.True(c.Relevant));
        result.NextInput.Candidates[0] = result.NextInput.Candidates[0] with { Relevant = false };
        var provider = new Choice("1,0");
        var advanced = TestGame.Advance(result.State, provider, random);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(new Cell(1, 0), advanced.State.Physical.Figures.Single().Position);
    }

    [Fact]
    public void SoleRequiredUnitIsSelectedAutomatically_UntilMovementNeedsInput()
    {
        var state = State();
        var original = JsonSerializer.Serialize(state);
        var result = TestGame.StartRound(state, new Random());

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
        var result = TestGame.StartRound(state, new Random());

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Null(result.State.Pending);
        Assert.Equal(new[] { "TokenDrawn", "MovementCompleted", "RoundCompleted" }, TestGame.OperationEvents(result).Select(e => e.Kind));
        Assert.Equal(new[] { new Cell(0, 0) }, TestGame.OperationEvents(result)[1].Path);
        Assert.Equal(2, result.State.Units.Single().CurrentHp);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1,0")]
    public void MovementDestinationAndStay_RequireProvider(string? choice)
    {
        var random = new Random();
        var result = TestGame.StartRound(State(), random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.NextInput.AllowsNone);
        Assert.Equal(2, result.NextInput.Candidates.Count);
        Assert.Contains(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Stay);
        Assert.Equal("1,0", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);

        var provider = new Choice(choice);
        result = TestGame.Advance(result.State, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.True(result.State.RoundComplete);
        Assert.Equal(new Cell(choice is null ? 0 : 1, 0), result.State.Physical.Figures.Single().Position);
        Assert.Equal("MovementCompleted", TestGame.OperationEvents(result)[0].Kind);
        Assert.Equal("RoundCompleted", TestGame.OperationEvents(result)[^1].Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("attack:enemy")]
    public void AttackAndEndTurn_RequireProvider(string? choice)
    {
        var state = State(mov: 0, rng: 1, atk: 1);
        AddEnemy(state, "enemy", new Cell(1, 0));
        var random = new Random();
        var result = TestGame.StartRound(state, random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.NextInput.AllowsNone);
        Assert.Equal(2, result.NextInput.Candidates.Count);
        Assert.Contains(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.Equal("attack:enemy", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
        Assert.DoesNotContain(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved");

        var provider = new Choice(choice);
        result = TestGame.Advance(result.State, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.True(result.State.RoundComplete);
        Assert.Equal(choice is null ? 1 : 0, result.State.Units.Single(u => u.Id == "enemy").CurrentHp);
        Assert.Equal(choice is not null, TestGame.OperationEvents(result).Any(e => e.Kind == "AttackResolved"));
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
            state.Types[0] = state.Types[0] with { Hp = 1, Unique = false };
            state.Units[0] = state.Units[0] with { CurrentHp = 1 };
            state.Units.Add(new Unit("ally", "hero-type", "blue", 1));
            state.Physical.Figures.Add(new Figure("ally", new Cell(1, 0)));
        }
        else if (kind == DecisionKind.Act)
        {
            AddEnemy(state, "enemy-a", new Cell(1, 0));
            AddEnemy(state, "enemy-b", new Cell(0, 1));
        }
        var random = new Random();
        var result = TestGame.StartRound(state, random);

        Assert.Equal(kind == DecisionKind.SelectUnit ? kind : DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal(2, result.NextInput.Candidates.Count(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)));
        Assert.False(result.NextInput.AllowsNone);
        Assert.False(result.State.RoundComplete);
        var provider = new Choice(result.NextInput.Candidates[0].Key);
        TestGame.Advance(result.State, provider, random);
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
        var result = TestGame.StartRound(state, random);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        var provider = new Choice(null);
        result = TestGame.Advance(restored, provider, random);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("next", result.NextInput.UnitId);
        Assert.Equal(new[] { "idle-type", "next-type" }, TestGame.OperationEvents(result).Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Equal(new[] { "hero", "idle" }, TestGame.OperationEvents(result).Where(e => e.Kind == "MovementCompleted").Select(e => e.UnitId));
        Assert.False(result.State.RoundComplete);
    }

    [Theory]
    [InlineData(DecisionKind.SelectUnit, true)]
    [InlineData(DecisionKind.SelectUnit, false)]
    [InlineData(DecisionKind.Move, true)]
    [InlineData(DecisionKind.Move, false)]
    [InlineData(DecisionKind.Act, true)]
    [InlineData(DecisionKind.Act, false)]
    public void ResumedForcedDecision_UsesRebuiltLegalityWithoutInvokingProvider(DecisionKind kind, bool enabled)
    {
        // Serialized Pending is informational; rebuild forced choices from progress.
        var state = State(mov: 0);
        state.Round = 1;
        state.ActiveToken = new("hero-type", state.Units.First(u => u.TypeId == "hero-type").SideId);
        state.MoveDone = kind == DecisionKind.Act;
        state.CurrentUnitId = kind == DecisionKind.SelectUnit ? null : "hero";
        state.Pending = new DecisionRequest(kind, "hero-type", state.CurrentUnitId,
            [new Candidate("forged")], false);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

        var result = TestGame.Advance(restored, new UnexpectedChoice(), new Random(), enabled);

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);
        Assert.DoesNotContain(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved");
    }
}
