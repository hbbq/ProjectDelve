using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class UniqueUnitTypeTests
{
    [Fact]
    public void DifferentSidesMayEachSetUpAndPlaceTheSameUniqueTypeWithSeparateTokens()
    {
        var type = UnitRoster.ChiefMechanic();
        var definition = Scenario.Define(Scenario.Map(5, 5), [
            Scenario.Group(type.Id, "blue", ControllerKind.Human, Scenario.At(0, 0)),
            Scenario.Group(type.Id, "red", ControllerKind.Automated, Scenario.At(4, 4))]);
        var state = GameEngine.CreateGame(definition);
        var round = GameEngine.StartRound(state, new Random(), false);
        Assert.Contains(new ActivationToken(type.Id, "blue"), round.State.Bag.Append(round.State.ActiveToken!));
        Assert.Contains(new ActivationToken(type.Id, "red"), round.State.Bag.Append(round.State.ActiveToken!));
        var other = Setup(type, "blue");
        other.PlaceUnit(type.Id, "red-unit", "red", new(1, 0));
        Assert.Equal(2, other.Units.Count);
        Assert.Throws<ArgumentException>(() => other.PlaceUnit(type.Id, "third", "red", new(2, 0)));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { Units = [.. definition.Units, new(type.Id, "blue", new(2, 2))] }));
    }

    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Setup(UnitType type, params string[] sides) => new()
    {
        Physical = new(new Board(5, 5, []), sides.Select((_, i) => new Figure($"unit-{i}", new(i, 0), Posture.Lying)).ToList()),
        Types = [type],
        Units = sides.Select((side, i) => type.CreateUnit($"unit-{i}", side)).ToList()
    };

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ExplicitUniqueSupportsOneOrMultipleHpAndRoundTrips(int hp)
    {
        var type = UnitType.Define("test-type", "Test", Stats(0, 0, 0, 0, hp), Unique());
        Assert.True(type.Unique);
        Assert.Equal(type, JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type)));
        Assert.True(TestGame.StartRound(Setup(type, "side"), new Random(), false).State.IsCompleted);
    }

    [Fact]
    public void MultipleHpRequiresExplicitUniqueInAuthoringAndRawScenarioContent()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("invalid", "Invalid", Stats(0, 0, 0, 0, 2)));
        // The legacy Hero helper does not imply Unique either.
        var raw = UnitType.Hero("invalid", 0, 0, 0, 0, 2);
        Assert.False(raw.Unique);
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(Setup(raw, "side"), new Random(), false));
        Assert.Throws<ArgumentException>(() => Setup(raw).PlaceUnit(raw.Id, "new", "side", new(0, 0)));
    }

    [Fact]
    public void CanonicalHeroesAreUniqueAndMonstersAreNot()
    {
        Assert.All(new[] { UnitRoster.Barbarian(), UnitRoster.Rogue(), UnitRoster.Wizard(), UnitRoster.Cleric() },
            type => Assert.True(type.Unique));
        Assert.All(new[] { UnitRoster.Grunt(), UnitRoster.Goblin(), UnitRoster.SkeletonArcher(), UnitRoster.Zombie(),
            UnitRoster.Ghost(), UnitRoster.Troll(), UnitRoster.Shaman() }, type => Assert.False(type.Unique));
    }

    [Fact]
    public void DuplicateUniqueSetupOnSameSideIsRejectedRegardlessOfPostureOrProvider()
    {
        var type = UnitType.Define("unique", "Unique", Stats(0, 0, 0, 0, 1), Unique());
        var state = Setup(type, "blue", "blue");
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Upright };
        // Validation precedes any controller decision; no provider can accept this setup.
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new Random(), false));
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new Random(), true));
    }

    [Fact]
    public void NonUniqueOneHpUnitsCanShareATypeInSetupAndPlacement()
    {
        var type = UnitType.Define("ordinary", "Ordinary", Stats(0, 0, 0, 0, 1));
        var state = Setup(type, "blue", "red");
        state.PlaceUnit(type.Id, "third", "blue", new(2, 0), Posture.Lying);
        Assert.NotNull(TestGame.StartRound(state, new Random(), false).NextInput);
        Assert.Equal(3, state.Units.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementRejectsSecondUniqueButAllowsLaterInstanceAfterDefeatOrRemoval(bool removeUnit)
    {
        var type = UnitType.Define("unique", "Unique", Stats(0, 0, 0, 0, 1), Unique());
        var state = Setup(type, "blue");
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => state.PlaceUnit(type.Id, "second", "blue", new(1, 0)));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        state.Physical.Figures.Clear();
        if (removeUnit) state.Units.Clear();
        else state.Units[0] = state.Units[0] with { CurrentHp = 0 };
        state.PlaceUnit(type.Id, "later", "blue", new(1, 0), Posture.Lying);
        Assert.Single(state.Units.Where(u => u.CurrentHp > 0));
        Assert.True(TestGame.StartRound(state, new Random(), false).State.IsCompleted);
    }

    [Fact]
    public void OpposingUniqueDoesNotBlockSummoningOnOwnSide()
    {
        var target = UnitRoster.ChiefMechanic();
        var summoner = UnitType.Define("summoner", "Summoner", Stats(1, 0, 0, 0, 1), CantAttack(),
            Ability("Call", Unlimited(), SummonAdjacent(target.Id, Posture.Lying), id: "call"));
        var state = Setup(summoner, "blue");
        state.Types.Add(target);
        state.Physical.Figures[0] = new("unit-0", new(2, 2));
        state.PlaceUnit(target.Id, "opponent", "red", new(4, 4));
        var ready = TestGame.StartRound(state, new Random(), false);
        if (ready.NextInput?.Kind == DecisionKind.SelectUnit)
            ready = TestGame.Advance(ready.State, new Choice("unit-0"), new Random(), false);
        ready = TestGame.Advance(ready.State, new Choice("stay"), new Random(), false);
        var summon = ready.NextInput!.Candidates.First(c => c.Action == UnitAction.SummonAdjacent);
        var result = TestGame.Advance(ready.State, new Choice(summon.Key), new Random(), false);
        Assert.Equal(2, result.State.Units.Count(u => u.TypeId == target.Id));
        Assert.Equal(new[] { "blue", "red" }, result.State.Units.Where(u => u.TypeId == target.Id).Select(u => u.SideId).Order());
    }

    [Fact]
    public void SummoningRechecksUniqueBeforeCreationAndAllowsAnotherAfterDefeat()
    {
        var target = UnitType.Define("unique", "Unique", Stats(0, 0, 0, 0, 1), Unique());
        var summoner = UnitType.Define("summoner", "Summoner", Stats(1, 0, 0, 0, 1), CantAttack(), OpenDoor(),
            Ability("Call", Unlimited(), SummonAdjacent(target.Id, Posture.Lying), id: "call"));
        var state = Setup(summoner, "blue");
        state.Types.Add(target);
        state.Physical.Figures[0] = new("unit-0", new(2, 2));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.ClosedDoor));
        var ready = TestGame.StartRound(state, new Random(), false);
        ready = TestGame.Advance(ready.State, new Choice("stay"), new Random(), false);
        Assert.Contains(ready.NextInput!.Candidates, c => c.Action == UnitAction.SummonAdjacent);
        // A saved candidate cannot bypass legality if another Unit has since entered play.
        ready.State.PlaceUnit(target.Id, "existing", "blue", new(0, 0), Posture.Lying);
        Assert.DoesNotContain(GameEngine.GameplayCandidates(ready.State, ready.State.Units[0]),
            c => c.Action == UnitAction.SummonAdjacent);
        Assert.Throws<ArgumentException>(() => TestGame.Advance(ready.State, new Choice("ability:call:1,1"), new Random(), false));
        ready.State.Units[1] = ready.State.Units[1] with { CurrentHp = 0 };
        ready.State.Physical.Figures.RemoveAll(f => f.Id == "existing");
        var created = TestGame.Advance(ready.State, new Choice("ability:call:1,1"), new Random(), false);
        Assert.Single(TestGame.OperationEvents(created), e => e.Kind == "UnitCreated" && e.TypeId == target.Id);
        Assert.Single(created.State.Units.Where(u => u.TypeId == target.Id && u.CurrentHp > 0));
    }
}
