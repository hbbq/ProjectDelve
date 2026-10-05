using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ClericTests
{
    private sealed class Random(string token = "cleric-type", bool block = false) : IRandomProvider
    {
        public int DefenceRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag.Contains(token) ? token : bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie()
        {
            DefenceRolls++;
            return block ? DefenceFace.Block : DefenceFace.Miss;
        }
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(UnitType? recipient = null)
    {
        var cleric = UnitType.Cleric();
        recipient ??= UnitType.Hero("recipient-type", 3, 1, 3, 2, 4);
        return new()
        {
            Physical = new(new Board(6, 5, []), [new("cleric", new(1, 1)), new("recipient", new(2, 1))]),
            Types = [cleric, recipient],
            Units = [cleric.CreateUnit("cleric", "blue"), recipient.CreateUnit("recipient", "blue")]
        };
    }

    private static void MoveFigure(GameState state, string id, Cell position)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = position };
    }

    private static void AddCleric(GameState state, Cell position, string side = "blue")
    {
        state.Units.Add(UnitType.Cleric().CreateUnit("second-cleric", side));
        state.Physical.Figures.Add(new("second-cleric", position));
    }

    private static EngineResult Choose(GameState state, string key, Random random) =>
        GameEngine.Advance(state, new Choice(key), random, false);

    [Fact]
    public void ClericSuppliesStatsOpenDoorAndConcretePassive()
    {
        var type = UnitType.Cleric();
        Assert.Equal((3, 1, 3, 3, 4), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack | UnitAction.Heal, type.Actions);
        Assert.Equal(UnitFreeAction.OpenDoor, type.FreeActions);
        Assert.Equal(new AdjacentFriendlyUnitsDefenceBonus(1), type.AdjacentFriendlyUnitsDefenceBonus);
        Assert.Empty(type.BonusActions);
        Assert.Empty(type.CreateUnit("cleric", "blue").BonusActionUses);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    [InlineData(0, 1)]
    [InlineData(0, 0)]
    public void EverySurroundingFriendlyCellReceivesBonusWithoutConsumingOpportunities(int x, int y)
    {
        var state = State();
        MoveFigure(state, "recipient", new(x, y));
        Assert.Equal(3, state.EffectiveDefOf("recipient"));
        Assert.Equal(3, state.EffectiveDef["recipient"]);
        Assert.Equal(3, state.EffectiveDefOf("cleric")); // No self adjacency.
        Assert.Empty(state.ModifiersThisTurn);
        Assert.False(state.MoveDone);
        Assert.False(state.ActionDone);
        Assert.Empty(state.BonusActionsUsedThisActivation);
    }

    [Fact]
    public void FriendlyNonHeroReceivesBonusRegardlessOfBehaviorAndAttackContent()
    {
        var type = UnitType.Goblin() with { Actions = UnitAction.None, Rng = 0, Atk = 0 };
        var state = State(type);
        Assert.Equal(3, state.EffectiveDefOf("recipient"));
        Assert.Equal(UnitBehavior.BackAwayAfterAttack, type.Behaviors);
    }

    [Fact]
    public void HostileRecipientAndHostileSourceDoNotContribute()
    {
        var state = State();
        state.Units[1] = state.Units[1] with { SideId = "red" };
        Assert.Equal(2, state.EffectiveDefOf("recipient"));
        AddCleric(state, new(2, 2), "red");
        Assert.Equal(3, state.EffectiveDefOf("recipient")); // Only its friendly Cleric applies.
        Assert.Equal(3, state.EffectiveDefOf("cleric"));
        Assert.Equal(3, state.EffectiveDefOf("second-cleric"));
    }

    [Fact]
    public void FriendlyClericsGiveEachOtherBonusAndStackOnRecipient()
    {
        var state = State();
        AddCleric(state, new(1, 2));
        Assert.Equal(4, state.EffectiveDefOf("cleric"));
        Assert.Equal(4, state.EffectiveDefOf("second-cleric"));
        Assert.Equal(4, state.EffectiveDefOf("recipient"));
    }

    [Fact]
    public void NonAdjacentRecipientReceivesNoBonus()
    {
        var state = State();
        MoveFigure(state, "recipient", new(3, 1));
        Assert.Equal(2, state.EffectiveDefOf("recipient"));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, 2)]
    [InlineData(EdgeKind.ClosedDoor, 2)]
    [InlineData(EdgeKind.OpenDoor, 3)]
    [InlineData(EdgeKind.WallWithWindow, 3)]
    public void PassiveAndGoblinThreatShareOrdinaryEdgeLos(EdgeKind kind, int expectedDef)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), kind));
        Assert.Equal(expectedDef, state.EffectiveDefOf("recipient"));
        var threatState = state.Copy();
        threatState.Units[1] = threatState.Units[1] with { SideId = "red" };
        Assert.Equal(expectedDef == 3,
            new GameplayQueries(threatState).HasNearbyHostileThreatFrom("cleric", new(1, 1)));
    }

    [Fact]
    public void DiagonalAdjacencyUsesNormalCornerLosAndDoorChangesImmediately()
    {
        var state = State();
        MoveFigure(state, "recipient", new(2, 2));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        Assert.Equal(3, state.EffectiveDefOf("recipient"));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor));
        Assert.Equal(2, state.EffectiveDefOf("recipient"));
        state.Physical.Board.Edges[1] = state.Physical.Board.Edges[1] with { Kind = EdgeKind.OpenDoor };
        Assert.Equal(3, state.EffectiveDefOf("recipient"));
        Assert.Empty(state.ModifiersThisTurn);
    }

    [Fact]
    public void OrdinaryDefModifiersAndPassiveAreAdditive()
    {
        var state = State();
        state.CurrentUnitId = "recipient";
        state.ModifiersThisTurn.Add(new(Stat.Def, 2));
        Assert.Equal(5, state.EffectiveDefOf("recipient"));
        Assert.Equal(3, state.EffectiveDefOf("cleric"));
        Assert.Single(state.ModifiersThisTurn);
    }

    [Theory]
    [InlineData("cleric", "cleric-type", "0,3")]
    [InlineData("recipient", "recipient-type", "4,1")]
    public void NormalMovementOfEitherUnitChangesDerivedDefWithoutModifierState(string id, string token, string destination)
    {
        var state = State();
        var random = new Random(token);
        var started = GameEngine.StartRound(state, random, false);
        Assert.Equal(id, started.State.CurrentUnitId);
        Assert.Equal(3, started.State.EffectiveDefOf("recipient"));
        var moved = Choose(started.State, destination, random);
        Assert.Equal(2, moved.State.EffectiveDefOf("recipient"));
        Assert.Equal(2, moved.ResolutionSteps[0].StateAfter.EffectiveDefOf("recipient"));
        Assert.Equal(3, started.State.EffectiveDefOf("recipient"));
        Assert.Empty(moved.State.ModifiersThisTurn);
    }

    [Fact]
    public void RemovedOrDeadClericStopsContributing()
    {
        var state = State();
        var removed = state.Copy();
        removed.Units.RemoveAll(u => u.Id == "cleric");
        removed.Physical.Figures.RemoveAll(f => f.Id == "cleric");
        Assert.Equal(2, removed.EffectiveDefOf("recipient"));
        state.Units[0] = state.Units[0] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "cleric");
        Assert.Equal(2, state.EffectiveDefOf("recipient"));
        Assert.Equal(3, state.EffectiveDef["cleric"]); // Dead Units remain serializable.
    }

    [Fact]
    public void CopiedHypotheticalStateUsesItsOwnPositionsEdgesAndUnits()
    {
        var original = State();
        var hypothetical = original.Copy();
        MoveFigure(hypothetical, "cleric", new(0, 3));
        Assert.Equal(3, original.EffectiveDefOf("recipient"));
        Assert.Equal(2, hypothetical.EffectiveDefOf("recipient"));
        MoveFigure(hypothetical, "recipient", new(1, 3));
        Assert.Equal(3, hypothetical.EffectiveDefOf("recipient"));
        hypothetical.Physical.Board.Edges.Add(new(new(0, 3), new(1, 3), EdgeKind.Wall));
        Assert.Equal(2, hypothetical.EffectiveDefOf("recipient"));
        Assert.Equal(3, original.EffectiveDefOf("recipient"));
        Assert.Empty(original.Physical.Board.Edges);
        original.Units[0] = original.Units[0] with { CurrentHp = 0 };
        original.Physical.Figures.RemoveAll(f => f.Id == "cleric");
        hypothetical.Physical.Board.Edges.Clear();
        Assert.Equal(2, original.EffectiveDefOf("recipient"));
        Assert.Equal(3, hypothetical.EffectiveDefOf("recipient"));
    }

    private static void AddAttacker(GameState state, Cell position, int attack = 3)
    {
        var type = UnitType.Grunt() with { Atk = attack };
        state.Types.Add(type);
        state.Units.Add(type.CreateUnit("enemy", "red"));
        state.Physical.Figures.Add(new("enemy", position));
    }

    [Fact]
    public void NormalAttackRollsEffectiveDefAfterSerialization()
    {
        var state = State();
        AddAttacker(state, new(3, 1));
        var random = new Random("grunt-type", block: true);
        var started = GameEngine.StartRound(state, random, false);
        var stayed = Choose(started.State, "stay", random);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(stayed.State))!;
        Assert.Equal(3, restored.EffectiveDefOf("recipient"));
        var attacked = Choose(restored, "attack:recipient", random);
        var attack = Assert.Single(attacked.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(3, random.DefenceRolls);
        Assert.Equal(3, attack.Blocks);
        Assert.Equal(0, attack.Damage);
        Assert.Equal(4, attacked.State.Units.Single(u => u.Id == "recipient").CurrentHp);
    }

    [Fact]
    public void KillingClericThroughNormalAttackRemovesBonusImmediately()
    {
        var state = State();
        AddAttacker(state, new(0, 1), attack: 4);
        var random = new Random("grunt-type");
        var started = GameEngine.StartRound(state, random, false);
        var stayed = Choose(started.State, "stay", random);
        var attacked = Choose(stayed.State, "attack:cleric", random);
        Assert.Contains(attacked.Events, e => e.Kind == "UnitDied" && e.UnitId == "cleric");
        Assert.Equal(2, attacked.State.EffectiveDefOf("recipient"));
        Assert.Equal(2, attacked.ResolutionSteps.Last().StateAfter.EffectiveDefOf("recipient"));
        Assert.Equal(3, stayed.State.EffectiveDefOf("recipient"));
        Assert.Empty(attacked.State.ModifiersThisTurn);
    }
}
