using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FuryTests
{
    private sealed class Random(string token = "barbarian-type") : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag.Contains(token) ? token : bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Decline : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => null;
    }

    private static GameState State(int enemies = 2)
    {
        var barbarian = UnitType.Barbarian();
        var enemy = UnitType.Grunt();
        Cell[] positions = [new(3, 2), new(2, 3), new(1, 1), new(1, 2)];
        return new()
        {
            Physical = new(new Board(7, 6, []), [new("hero", new(2, 2)),
                .. positions.Take(enemies).Select((p, i) => new Figure($"enemy-{i}", p))]),
            Types = [barbarian, enemy],
            Units = [barbarian.CreateUnit("hero", "blue"),
                .. positions.Take(enemies).Select((_, i) => enemy.CreateUnit($"enemy-{i}", "red"))]
        };
    }

    private static void Move(GameState state, string id, Cell position)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = position };
    }

    private static EngineResult Choose(GameState state, string key, Random random) =>
        GameEngine.Advance(state, new Choice(key), random, false);

    [Fact]
    public void BarbarianRetainsContentAndSuppliesNamedFury()
    {
        var type = UnitType.Barbarian();
        Assert.Equal((3, 1, 4, 3, 5), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitFreeAction.OpenDoor, type.FreeActions);
        Assert.NotNull(type.Fury);
        Assert.Equal("Fury", type.Fury.Name);
        Assert.Equal(new PassiveDescription("Fury", "ATK +1 while adjacent to 2 or more enemies"),
            Assert.Single(type.Passives));
        Assert.Equal("Rage", Assert.Single(type.BonusActions).Name);
        Assert.Null(UnitType.Cleric().Fury);
        Assert.Empty(UnitType.Grunt().Passives);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void ThresholdGivesOneTotalBonus(int enemies, int expected)
    {
        var state = State(enemies);
        Assert.Equal(expected, state.EffectiveAtkOf("hero"));
        Assert.Equal(expected, state.EffectiveAtk["hero"]);
        Assert.Empty(state.ModifiersThisTurn);
    }

    [Fact]
    public void SideAloneDeterminesEnemiesAndSideChangesAreImmediate()
    {
        var state = State();
        // Same UnitType can be hostile; Goblin behavior does not make a friendly Unit hostile.
        state.Units[1] = UnitType.Barbarian().CreateUnit("enemy-0", "green");
        state.Types.Add(UnitType.Goblin());
        state.Units[2] = UnitType.Goblin().CreateUnit("enemy-1", "blue");
        Assert.Equal(4, state.EffectiveAtkOf("hero"));
        state.Units[2] = state.Units[2] with { SideId = "red" };
        Assert.Equal(5, state.EffectiveAtkOf("hero"));
        state.Units[0] = state.Units[0] with { SideId = "red" };
        Assert.Equal(4, state.EffectiveAtkOf("hero"));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, 4)]
    [InlineData(EdgeKind.ClosedDoor, 4)]
    [InlineData(EdgeKind.OpenDoor, 5)]
    [InlineData(EdgeKind.WallWithWindow, 5)]
    public void GeometricAdjacencyRequiresSharedNormalLos(EdgeKind kind, int expected)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), kind));
        Assert.Equal(expected, state.EffectiveAtkOf("hero"));
    }

    [Fact]
    public void DiagonalCornerLosAndDoorChangesAreDerived()
    {
        var state = State();
        Move(state, "enemy-1", new(1, 1));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(1, 2), EdgeKind.Wall));
        Assert.Equal(5, state.EffectiveAtkOf("hero"));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(2, 1), EdgeKind.ClosedDoor));
        Assert.Equal(4, state.EffectiveAtkOf("hero"));
        state.Physical.Board.Edges[1] = state.Physical.Board.Edges[1] with { Kind = EdgeKind.OpenDoor };
        Assert.Equal(5, state.EffectiveAtkOf("hero"));
        Assert.Empty(state.ModifiersThisTurn);
    }

    [Theory]
    [InlineData("enemy-1", "grunt-type", "2,3", 5)]
    [InlineData("enemy-1", "grunt-type", "5,3", 4)]
    [InlineData("hero", "barbarian-type", "0,2", 4)]
    public void NormalMovementChangesFuryWithoutStoredModifiers(string id, string token, string destination, int expected)
    {
        var state = State();
        if (destination == "2,3") Move(state, "enemy-1", new(5, 3));
        var before = state.EffectiveAtkOf("hero");
        var random = new Random(token);
        var started = GameEngine.StartRound(state, random, false);
        if (id != started.State.CurrentUnitId)
            started = Choose(started.State, id, random);
        var moved = Choose(started.State, destination, random);
        Assert.Equal(expected, moved.State.EffectiveAtkOf("hero"));
        Assert.Equal(expected, moved.ResolutionSteps[0].StateAfter.EffectiveAtkOf("hero"));
        Assert.Equal(before, started.State.EffectiveAtkOf("hero"));
        Assert.Empty(moved.State.ModifiersThisTurn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadOrRemovedEnemyStopsCounting(bool remove)
    {
        var state = State();
        if (remove) state.Units.RemoveAt(2);
        else state.Units[2] = state.Units[2] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "enemy-1");
        Assert.Equal(4, state.EffectiveAtkOf("hero"));
        Assert.Empty(state.ModifiersThisTurn);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 7)]
    public void NormalAttackAndRageUseEffectiveAtkAndDeathEndsFury(bool rage, int expected)
    {
        var random = new Random();
        var started = GameEngine.StartRound(State(), random, false);
        var stayed = Choose(started.State, "stay", random);
        var rageCandidate = Assert.Single(stayed.NextInput!.Candidates, c => c.BonusAction?.Name == "Rage");
        Assert.True(rageCandidate.Relevant); // Existing hypothetical-effect relevance sees Fury + Rage.
        var ready = rage ? Choose(stayed.State, rageCandidate.Key, random) : stayed;
        Assert.Equal(expected, ready.State.EffectiveAtkOf("hero"));
        Assert.Equal(rage ? 1 : 0, ready.State.ModifiersThisTurn.Count);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(ready.State))!;
        Assert.Equal(expected, restored.EffectiveAtkOf("hero"));
        var attacked = Choose(restored, "attack:enemy-0", random);
        Assert.Equal(expected, random.AttackRolls);
        Assert.Equal(expected, Assert.Single(attacked.Events, e => e.Kind == "AttackResolved").Hits);
        Assert.Contains(attacked.Events, e => e.Kind == "UnitDied" && e.UnitId == "enemy-0");
        var deathIndex = attacked.Events.FindIndex(e => e.Kind == "UnitDied" && e.UnitId == "enemy-0");
        Assert.Equal(rage ? 6 : 4, attacked.ResolutionSteps.Single(s => s.EventIndex == deathIndex)
            .StateAfter.EffectiveAtkOf("hero"));
        Assert.Equal(rage ? 6 : 4, attacked.State.EffectiveAtkOf("hero"));
        Assert.Equal(DecisionKind.Cleave, attacked.NextInput!.Kind);
        var declined = GameEngine.Advance(attacked.State, new Decline(), random, false);
        Assert.Equal(4, declined.State.EffectiveAtkOf("hero"));
        Assert.Equal(expected, ready.State.EffectiveAtkOf("hero"));
    }

    [Fact]
    public void HypotheticalCopyUsesItsOwnPositionsLosSidesAndRage()
    {
        var original = State();
        original.CurrentUnitId = "hero";
        Move(original, "enemy-1", new(5, 3));
        var hypothetical = original.Copy();
        Move(hypothetical, "enemy-1", new(2, 3));
        Assert.Equal(4, original.EffectiveAtkOf("hero"));
        Assert.Equal(5, hypothetical.EffectiveAtkOf("hero"));
        Assert.Equal(4, original.EffectiveAtk["hero"]);
        Assert.Equal(5, hypothetical.EffectiveAtk["hero"]);
        hypothetical.ModifiersThisTurn.AddRange(UnitType.Barbarian().BonusActions[0].Modifiers);
        Assert.Equal(7, hypothetical.EffectiveAtkOf("hero"));
        Assert.Equal(4, original.EffectiveAtkOf("hero"));
        hypothetical.Physical.Board.Edges.Add(new(new(2, 2), new(2, 3), EdgeKind.ClosedDoor));
        Assert.Equal(6, hypothetical.EffectiveAtkOf("hero"));
        Assert.Empty(original.Physical.Board.Edges);
        hypothetical.Physical.Board.Edges.Clear();
        hypothetical.Units[2] = hypothetical.Units[2] with { SideId = "blue" };
        Assert.Equal(6, hypothetical.EffectiveAtkOf("hero"));
        Assert.Empty(original.ModifiersThisTurn);
    }

    [Fact]
    public void FuryAndAuraRemainIndependentAndAdditive()
    {
        var state = State();
        var cleric = UnitType.Cleric();
        state.Types.Add(cleric);
        state.Units.Add(cleric.CreateUnit("cleric", "blue"));
        state.Physical.Figures.Add(new("cleric", new(1, 2)));
        Assert.Equal(5, state.EffectiveAtkOf("hero"));
        Assert.Equal(4, state.EffectiveDefOf("hero"));
        Assert.Equal(new PassiveDescription("Aura", "Adjacent friendly Units get DEF +1"), Assert.Single(cleric.Passives));
        Assert.Equal(3, state.EffectiveDefOf("enemy-0"));
    }
}
