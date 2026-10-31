using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ResolutionObservationTests
{
    private sealed class Random : IRandomProvider
    {
        public int Draws { get; private set; }
        public Action? BeforeDraw { get; init; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag)
        {
            BeforeDraw?.Invoke();
            Draws++;
            return bag[0];
        }
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState InitialState()
    {
        var type = new UnitType("ordinary", 1, 0, 0, 0, 1);
        return new()
        {
            Physical = new(new(3, 2, []), [new("unit", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("unit", "blue")],
            Controllers = [new(new(type.Id, "blue"), ControllerKind.Human)],
            WorldEffects = new(0, 1),
            WorldDeck = new() { DrawPile = [new("calm", WorldEffect.Calm)] }
        };
    }

    private static EngineResult Run(string operation, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        var state = InitialState();
        var random = new Random();
        if (operation == "start") return GameEngine.StartRound(state, random, false, observe);
        if (operation == "refresh")
        {
            state.Round = 1;
            state.Bag = [new("ordinary", "blue")];
            return GameEngine.RefreshChoices(state, random, false, observe);
        }
        state = GameEngine.StartRound(state, random, false).State;
        var move = state.Pending!.Candidates.First(c => c.Path?.Count > 1);
        return GameEngine.Advance(state, new Choice(move.Key), random, false, observe);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [Theory]
    [InlineData("start")]
    [InlineData("advance")]
    [InlineData("refresh")]
    public void ObservationMatchesAccumulatedOutputBeforeReturnWithoutChangingResolution(string operation)
    {
        var observed = new List<(RulesEvent Event, ResolutionStep Step)>();
        var returned = false;
        var result = Run(operation, (e, step) =>
        {
            Assert.False(returned);
            observed.Add((e, step));
        });
        returned = true;

        Assert.NotEmpty(observed);
        Assert.Equal(Json(Run(operation)), Json(result));
        Assert.Equal(Enumerable.Range(0, result.Events.Count), observed.Select(o => o.Step.EventIndex));
        foreach (var (e, step) in observed)
        {
            Assert.Equal(Json(result.Events[step.EventIndex]), Json(e));
            Assert.Equal(Json(result.ResolutionSteps[step.EventIndex]), Json(step));
            Assert.NotSame(result.ResolutionSteps[step.EventIndex].StateAfter, step.StateAfter);
        }
    }

    [Fact]
    public void RoundEventIsObservedBeforeLaterRandomnessOccurs()
    {
        var roundObserved = false;
        var random = new Random { BeforeDraw = () => Assert.True(roundObserved) };
        GameEngine.StartRound(InitialState(), random, observe: (e, _) =>
        {
            if (e.Kind == "RoundStarted") roundObserved = true;
        });
        Assert.True(roundObserved);
        Assert.Equal(1, random.Draws);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("advance")]
    [InlineData("refresh")]
    public void ObserverMutationCannotChangeEngineOrAccumulatedEventsAndSnapshots(string operation)
    {
        var paths = 0;
        var result = Run(operation, (e, step) =>
        {
            if (e.Path is not null) { paths++; e.Path.Clear(); }
            var state = step.StateAfter;
            state.Round = 999;
            state.Physical.Figures.Clear();
            state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
            state.Physical.Board.Terrain.Clear();
            state.Units.Clear(); state.Types.Clear(); state.Bag.Clear(); state.Controllers.Clear();
            state.CompletedUnitIds.Clear(); state.ModifiersThisTurn.Clear(); state.PendingExplosions.Clear();
            state.BonusActionUseCountsThisActivation.Clear(); state.BonusActionUseCountsThisActivation.Clear();
            state.WorldDeck!.DrawPile.Clear();
            state.Pending?.Candidates.Clear();
        });

        Assert.Equal(Json(Run(operation)), Json(result));
        if (operation == "advance") Assert.True(paths > 0);
    }

    [Fact]
    public void ObserverExceptionPropagatesWithoutMutatingPreviousStateOrUndoingRandomness()
    {
        var previous = InitialState();
        var saved = Json(previous);
        var random = new Random();
        var failure = new InvalidOperationException("Observer failed.");
        var emitted = new List<string>();
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            GameEngine.StartRound(previous, random, observe: (e, _) =>
            {
                emitted.Add(e.Kind);
                if (e.Kind == "TokenDrawn") throw failure;
            }));

        Assert.Same(failure, thrown);
        Assert.Equal(new[] { "RoundStarted", "WorldDeckShuffled", "ActivationBagPopulated", "TokenDrawn" }, emitted);
        Assert.Equal(1, random.Draws);
        Assert.Equal(saved, Json(previous));
    }
}
