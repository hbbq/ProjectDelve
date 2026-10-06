using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BackstabTests
{
    private sealed class Random : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "rogue-type") ?? bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int targetX = 2, bool ally = true)
    {
        var rogue = UnitType.Rogue();
        // A hostile Hero and a friendly Unit with monster behavior exercise Side relationships.
        var target = UnitType.Hero("target-type", 0, 1, 1, 0, 20) with { Unique = true };
        var friend = UnitType.Zombie();
        var state = new GameState
        {
            Physical = new(new Board(8, 6, []),
                [new("rogue", new(1, 1)), new("target", new(targetX, 1))]),
            Types = [rogue, target, friend],
            Units = [rogue.CreateUnit("rogue", "blue"), target.CreateUnit("target", "red")]
        };
        if (ally) AddFriend(state, "ally", new(targetX, 2));
        return state;
    }

    private static void AddFriend(GameState state, string id, Cell position, string side = "blue")
    {
        state.Units.Add(state.Types[2].CreateUnit(id, side));
        state.Physical.Figures.Add(new(id, position));
    }

    private static void Move(GameState state, string id, Cell position)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = position };
    }

    private static EngineResult Choose(GameState state, string key, Random? random = null) =>
        TestGame.Advance(state, new Choice(key), random ?? new(), false);

    private static EngineResult Ready(GameState state) =>
        Choose(TestGame.StartRound(state, new Random(), false).State, "stay");

    [Fact]
    public void ContentIsPassiveAndHasNoUsesOrActivationState()
    {
        var type = UnitType.Rogue();
        Assert.NotNull(type.Backstab);
        Assert.Equal(new PassiveDescription("Backstab",
            "+1 ATK when attacking an enemy that is adjacent to another friendly Unit"), Assert.Single(type.Passives));
        Assert.Equal(new[] { "Dash", "Throwing Knife" }, type.CreateUnit("rogue", "blue").BonusActionUses.Keys.Order().ToArray());
        Assert.Null(UnitType.Grunt().Backstab);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 6)]
    public void ConfiguredBonusIsTargetSpecificOnUnfamiliarTypeAfterSerialization(bool ally, int expected)
    {
        var state = State(ally: ally);
        var type = new UnitType("unfamiliar-flanker", 1, 1, 3, 0, 4)
        { Unique = true,
            Backstab = new() { AtkBonus = 3 }
        };
        state.Types[0] = type;
        state.Units[0] = type.CreateUnit("rogue", "blue");
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(type.Backstab, restored.Types[0].Backstab);
        Assert.Equal(3, restored.EffectiveAtkOf("rogue"));
        Assert.Equal(expected, restored.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal("+3 ATK when attacking an enemy that is adjacent to another friendly Unit",
            Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "passive:Backstab").Description);
        var ready = Ready(restored);
        var random = new Random();
        var attacked = Choose(ready.State, "attack:target", random);
        Assert.Equal(expected, random.AttackRolls);
        Assert.Equal(expected, Assert.Single(TestGame.OperationEvents(attacked), e => e.Kind == "AttackResolved").Attack!.AttackDice);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 4)]
    [InlineData(3, 4)]
    public void AnotherLivingFriendlyUnitGivesOnlyOneBonus(int allies, int expected)
    {
        var state = State(ally: false);
        Cell[] cells = [new(2, 2), new(3, 2), new(3, 1)];
        for (var i = 0; i < allies; i++) AddFriend(state, $"ally-{i}", cells[i]);
        Assert.Equal(expected, state.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(3, state.EffectiveAtkOf("rogue"));
        Assert.Equal(3, state.EffectiveAtk["rogue"]);
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "rogue", new(1, 1), "target"));
    }

    [Fact]
    public void RogueItselfAndHostileUnitsDoNotQualify()
    {
        var state = State(ally: false);
        Assert.True(SpatialRules.AreAdjacent(state.Physical.Board, new(1, 1), new(2, 1)));
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
        AddFriend(state, "hostile", new(2, 2), "red");
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
        state.Units[^1] = state.Units[^1] with { SideId = "green" };
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, 3)]
    [InlineData(EdgeKind.ClosedDoor, 3)]
    [InlineData(EdgeKind.OpenDoor, 4)]
    [InlineData(EdgeKind.WallWithWindow, 4)]
    public void AdjacencyUsesNormalLos(EdgeKind kind, int expected)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 2), kind));
        Assert.Equal(expected, state.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(expected == 4, SpatialRules.AreAdjacent(state.Physical.Board, new(2, 1), new(2, 2)));
    }

    [Fact]
    public void DiagonalAdjacencyUsesExistingCornerLos()
    {
        var state = State();
        Move(state, "ally", new(3, 2));
        Assert.Equal(4, state.EffectiveAtkAgainst("rogue", "target"));
        state.Physical.Board.Edges.Add(new(new(2, 1), new(3, 1), EdgeKind.Wall));
        Assert.Equal(4, state.EffectiveAtkAgainst("rogue", "target"));
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 2), EdgeKind.Wall));
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("remove")]
    [InlineData("kill")]
    public void ChangesToSupportingUnitImmediatelyChangeTheDerivedAttack(string change)
    {
        var state = State();
        Assert.Equal(4, state.EffectiveAtkAgainst("rogue", "target"));
        if (change == "move") Move(state, "ally", new(6, 4));
        if (change == "remove")
        {
            state.Physical.Figures.RemoveAll(f => f.Id == "ally");
            state.Units.RemoveAll(u => u.Id == "ally");
        }
        if (change == "kill") state.Units[2] = state.Units[2] with { CurrentHp = 0 };
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
    }

    [Fact]
    public void CopiesAndSerializationUseTheirOwnPositionsAndContent()
    {
        var original = State();
        Move(original, "ally", new(6, 4));
        var copy = original.Copy();
        Move(copy, "ally", new(2, 2));
        Assert.Equal(3, original.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(4, copy.EffectiveAtkAgainst("rogue", "target"));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(copy))!;
        Assert.Equal(4, restored.EffectiveAtkAgainst("rogue", "target"));
        Move(restored, "target", new(5, 1));
        Assert.Equal(3, restored.EffectiveAtkAgainst("rogue", "target"));
    }

    [Theory]
    [InlineData(false, false, 3, 1)]
    [InlineData(false, true, 4, 1)]
    [InlineData(true, false, 2, 3)]
    [InlineData(true, true, 3, 3)]
    public void MeleeAndThrowingKnifeResolutionRollAuthoritativeTargetSpecificDice(
        bool knife, bool ally, int expectedAttack, int expectedRange)
    {
        var ready = Ready(State(knife ? 4 : 2, ally));
        if (knife) ready = Choose(ready.State, "bonus-action:Throwing Knife");
        Assert.Equal(expectedRange, ready.State.EffectiveRngOf("rogue"));
        Assert.Equal(expectedAttack, ready.State.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(knife ? 2 : 3, ready.State.EffectiveAtkOf("rogue"));
        Assert.Contains(ready.NextInput!.Candidates, c => c.Key == "attack:target");
        var random = new Random();
        var attacked = Choose(ready.State, "attack:target", random);
        Assert.Equal(expectedAttack, random.AttackRolls);
        Assert.Equal(expectedAttack, Assert.Single(TestGame.OperationEvents(attacked), e => e.Kind == "AttackResolved").Hits);
    }

    [Fact]
    public void EffectivenessDependsOnSelectedTargetAndContentRatherThanTypeIdentity()
    {
        var state = State();
        state.Types[0] = state.Types[0] with { Id = "custom-type", Rng = 4 };
        state.Units[0] = state.Units[0] with { TypeId = "custom-type" };
        state.Units.Add(state.Types[1].CreateUnit("unsupported", "red"));
        state.Physical.Figures.Add(new("unsupported", new(1, 4)));
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "rogue", new(1, 1), "unsupported"));
        Assert.Equal(4, state.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "unsupported"));
        state.Types[0] = state.Types[0] with { Backstab = null };
        Assert.Equal(3, state.EffectiveAtkAgainst("rogue", "target"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void KnifeRelevanceUsesTargetSpecificLegalityAtZeroModifiedGeneralAttack(bool ally, bool relevant)
    {
        var state = State(4, ally);
        state.Types[0] = state.Types[0] with { Atk = 1 };
        var ready = Ready(state);
        Assert.Equal(relevant, Assert.Single(ready.NextInput!.Candidates,
            c => c.BonusAction?.Name == "Throwing Knife").Relevant);
        var used = Choose(ready.State, "bonus-action:Throwing Knife");
        Assert.Equal(0, used.State.EffectiveAtkOf("rogue"));
        Assert.Equal(ally ? 1 : 0, used.State.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(relevant, used.NextInput!.Candidates.Any(c => c.Key == "attack:target"));
        if (ally)
        {
            var random = new Random();
            Choose(used.State, "attack:target", random);
            Assert.Equal(1, random.AttackRolls);
        }
    }

    [Fact]
    public void SharedTargetRelevanceComparesTheSameAttackStrengthAsResolution()
    {
        var state = State();
        state.Types[0] = state.Types[0] with
        {
            BonusActions = [new("Attack Boost", 2, [new(Stat.Atk, 1)])]
        };
        state.Units[0] = state.Types[0].CreateUnit("rogue", "blue");
        var ready = Ready(state);
        Assert.True(Assert.Single(ready.NextInput!.Candidates, c => c.BonusAction is not null).Relevant);
        var boosted = Choose(ready.State, "bonus-action:Attack Boost");
        Assert.Equal(4, ready.State.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(5, boosted.State.EffectiveAtkAgainst("rogue", "target"));
        var random = new Random();
        Choose(boosted.State, "attack:target", random);
        Assert.Equal(5, random.AttackRolls);
    }

    [Fact]
    public void FuryAndAuraKeepTheirGeneralBonuses()
    {
        var state = State();
        state.Types[0] = UnitType.Barbarian();
        state.Units[0] = state.Types[0].CreateUnit("rogue", "blue");
        state.Types.Add(UnitType.Cleric());
        state.Units.Add(state.Types[^1].CreateUnit("cleric", "blue"));
        state.Physical.Figures.Add(new("cleric", new(0, 1)));
        AddFriend(state, "enemy", new(1, 0), "red");
        Assert.Equal(5, state.EffectiveAtkOf("rogue"));
        Assert.Equal(5, state.EffectiveAtkAgainst("rogue", "target"));
        Assert.Equal(4, state.EffectiveDefOf("rogue"));
    }
}
