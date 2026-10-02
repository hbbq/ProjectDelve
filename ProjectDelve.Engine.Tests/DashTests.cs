using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DashTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int width = 8, int height = 1)
    {
        var rogue = UnitType.Rogue();
        return new()
        {
            Physical = new(new Board(width, height, []), [new("rogue", new(0, 0))]),
            Types = [rogue], Units = [rogue.CreateUnit("rogue", "blue")]
        };
    }

    private static EngineResult Start(GameState state, bool auto = false) =>
        GameEngine.StartRound(state, new Random(), auto);
    private static EngineResult Choose(GameState state, string key, bool auto = false) =>
        GameEngine.Advance(state, new Choice(key), new Random(), auto);
    private static Candidate Dash(EngineResult result) =>
        Assert.Single(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
    private static AbilityUses Uses(GameState state) => state.Units.Single(u => u.Id == "rogue").BonusActionUses!;
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static Candidate[] Moves(EngineResult result) =>
        result.NextInput!.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToArray();

    [Fact]
    public void RogueHasDashWithTwoOfTwoUsesIncludingBareScenarioInitialization()
    {
        var state = State();
        Assert.Equal(new BonusActionAbility("Dash", 2, new(Stat.Mov, 2)), state.Types[0].BonusAction);
        Assert.Equal(new AbilityUses(2, 2), Uses(state));
        state.Units[0] = new("rogue", state.Types[0].Id, "blue", 4);
        var started = Start(state);
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.Null(Uses(state));
    }

    [Fact]
    public void DashSpendsUseAndBonusActionAndExpandsAuthoritativeMovementAfterSerialization()
    {
        var started = Start(State());
        Assert.True(Dash(started).Relevant);
        Assert.DoesNotContain(Moves(started), c => c.Destination == new Cell(6, 0));
        Assert.Throws<ArgumentException>(() => Choose(started.State, "6,0"));

        var dashed = Choose(started.State, Dash(started).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(dashed.State));
        Assert.True(dashed.State.BonusActionUsed);
        Assert.False(dashed.State.MoveDone);
        Assert.False(dashed.State.ActionDone);
        Assert.Equal(4, dashed.State.Types[0].Mov);
        Assert.Equal(6, dashed.State.EffectiveMovOf("rogue"));
        Assert.Equal(3, dashed.State.EffectiveAtkOf("rogue"));
        Assert.Equal(new ModifierThisTurn(Stat.Mov, 2), Assert.Single(dashed.State.ModifiersThisTurn));
        Assert.Equal("Dash", Assert.Single(dashed.Events).AbilityName);
        Assert.Equal(6, Assert.Single(dashed.ResolutionSteps).StateAfter.EffectiveMovOf("rogue"));
        Assert.DoesNotContain(dashed.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        Assert.Throws<ArgumentException>(() => Choose(dashed.State, "bonus-action:Dash"));
        Assert.Contains(Moves(dashed), c => c.Destination == new Cell(6, 0) && c.Path!.Count == 7);
        Assert.DoesNotContain(Moves(dashed), c => c.Destination == new Cell(7, 0));

        // Relevance evaluation and application both leave the supplied state detached.
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.Empty(started.State.ModifiersThisTurn);
        Assert.Equal(4, started.State.EffectiveMovOf("rogue"));

        var restored = Restore(dashed.State);
        Assert.Equal(6, restored.EffectiveMov["rogue"]);
        var moved = Choose(restored, "6,0");
        Assert.Equal(new Cell(6, 0), moved.State.Physical.Figures[0].Position);
        Assert.Equal(6, moved.ResolutionSteps.First().StateAfter.EffectiveMovOf("rogue"));
        Assert.True(moved.State.RoundComplete); // Sole End Turn still auto-resolves.
        Assert.Empty(moved.State.ModifiersThisTurn);
        Assert.Equal(4, moved.State.EffectiveMovOf("rogue"));
        Assert.Equal(new AbilityUses(2, 1), Uses(moved.State));
        Assert.Single(restored.ModifiersThisTurn);
        var next = Start(Restore(moved.State));
        Assert.Equal(new AbilityUses(2, 1), Uses(next.State));
        Assert.Equal(4, next.State.EffectiveMovOf("rogue"));
        Assert.False(next.State.BonusActionUsed);
    }

    [Fact]
    public void ZeroUsesMakesDashIllegalEvenWithAnUnusedBonusAction()
    {
        var state = State();
        state.Units[0] = state.Units[0] with { BonusActionUses = new(2, 0) };
        var started = Start(state);
        Assert.False(started.State.BonusActionUsed);
        Assert.DoesNotContain(started.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        Assert.Throws<ArgumentException>(() => Choose(started.State, "bonus-action:Dash"));
    }

    [Theory]
    [InlineData("stay")]
    [InlineData("4,0")]
    public void DashIsLegalButIrrelevantAfterMovementAndCanWasteAUse(string move)
    {
        var moved = Choose(Start(State()).State, move);
        Assert.True(moved.State.MoveDone);
        Assert.False(Dash(moved).Relevant);
        var dashed = Choose(Restore(moved.State), Dash(moved).Key);
        Assert.Equal("Dash", dashed.Events[0].AbilityName);
        var snapshot = dashed.ResolutionSteps[0].StateAfter;
        Assert.True(snapshot.MoveDone);
        Assert.True(snapshot.BonusActionUsed);
        Assert.Equal(6, snapshot.EffectiveMovOf("rogue"));
        Assert.Equal(new AbilityUses(2, 1), Uses(dashed.State));
        Assert.True(dashed.State.RoundComplete);
        Assert.Empty(dashed.State.ModifiersThisTurn);
    }

    [Theory]
    [InlineData("board")]
    [InlineData("terrain")]
    [InlineData("occupancy")]
    [InlineData("wall")]
    [InlineData("closed-door")]
    public void DashIsIrrelevantWhenAuthoritativeMovementAddsNoDestination(string barrier)
    {
        var state = State(barrier == "board" ? 5 : 8);
        if (barrier == "terrain")
            state.Physical.Board.Terrain.Add(new(new(5, 0), TerrainKind.Water));
        if (barrier is "wall" or "closed-door")
            state.Physical.Board.Edges.Add(new(new(4, 0), new(5, 0),
                barrier == "wall" ? EdgeKind.Wall : EdgeKind.ClosedDoor));
        if (barrier == "occupancy")
        {
            state.Types.Add(UnitType.Grunt());
            state.Units.Add(UnitType.Grunt().CreateUnit("enemy", "red"));
            state.Physical.Figures.Add(new("enemy", new(5, 0)));
        }
        var started = Start(state);
        Assert.False(started.State.MoveDone);
        Assert.False(Dash(started).Relevant);
        var dashed = Choose(started.State, Dash(started).Key);
        Assert.Equal(Moves(started).Select(c => c.Destination), Moves(dashed).Select(c => c.Destination));
        Assert.Equal(new AbilityUses(2, 1), Uses(dashed.State));
    }

    [Fact]
    public void RelevanceComparesDestinationsDespiteFreshCanonicalPathRepresentations()
    {
        var started = Start(State(3, 3)); // Every destination is already within MOV 4.
        Assert.False(Dash(started).Relevant);
        var dashed = Choose(started.State, Dash(started).Key);
        foreach (var before in Moves(started))
        {
            var after = Assert.Single(Moves(dashed), c => c.Destination == before.Destination);
            Assert.NotSame(before.Path, after.Path);
            Assert.NotEqual(before, after); // Candidate record equality includes the path List identity.
            Assert.Equal(before.Path, after.Path);
        }
        // Exposed path data is not authoritative input to relevance or movement.
        Moves(started).Single(c => c.Destination == new Cell(2, 2)).Path!.Reverse();
        var refreshed = GameEngine.RefreshChoices(started.State, new Random(), false);
        Assert.False(Dash(refreshed).Relevant);
        Assert.Equal(new Cell(0, 0), Moves(refreshed).Single(c => c.Destination == new Cell(2, 2)).Path![0]);
    }

    [Fact]
    public void IrrelevantDashAllowsRelevanceAutoEndAndRemainsExplicitlySelectableWhenDisabled()
    {
        var started = Start(State(), true);
        var automatic = Choose(started.State, "stay", true);
        Assert.True(automatic.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(automatic.State));
        var exposed = Choose(started.State, "stay");
        Assert.False(Dash(exposed).Relevant);
        Assert.Contains(exposed.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn && c.Relevant);
        Assert.Equal(new AbilityUses(2, 1), Uses(Choose(exposed.State, Dash(exposed).Key).State));
        var refreshed = GameEngine.RefreshChoices(exposed.State, new Random());
        Assert.True(refreshed.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(refreshed.State));
    }

    [Fact]
    public void EndingActivationDoesNotTransferMovementModifierToNextRogue()
    {
        var state = State(8, 2);
        state.Units.Add(UnitType.Rogue().CreateUnit("ally", "blue"));
        state.Physical.Figures.Add(new("ally", new(0, 1)));
        var started = Choose(Start(state).State, "rogue");
        var dashed = Choose(started.State, Dash(started).Key);
        Assert.Equal(4, dashed.State.EffectiveMovOf("ally"));
        var ended = Choose(dashed.State, "stay");
        Assert.Equal("ally", ended.State.CurrentUnitId);
        Assert.Empty(ended.State.ModifiersThisTurn);
        Assert.Equal(4, ended.State.EffectiveMovOf("rogue"));
        Assert.Equal(4, ended.State.EffectiveMovOf("ally"));
        Assert.Equal(new AbilityUses(2, 2), ended.State.Units.Single(u => u.Id == "ally").BonusActionUses);
        Assert.Equal(new AbilityUses(2, 1), Uses(ended.State));
    }
}
