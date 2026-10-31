using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EquipmentPassiveTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static GameState World(params LocalAbilityDefinition[] passives)
    {
        var carrier = new UnitType("carrier", 3, 1, 2, 0, 8) { CanHaveEquipment = true, Unique = true };
        var other = new UnitType("other", 0, 1, 1, 0, 1);
        return new()
        {
            Physical = new(new Board(8, 6, []), [new("a", new(2, 2)), new("enemy", new(3, 2)),
                new("enemy-2", new(2, 3)), new("ally", new(4, 2))]),
            Types = [carrier, other],
            Units = [carrier.CreateUnit("a", "blue") with { Inventory = [new("physical", "gear")] },
                other.CreateUnit("enemy", "red"), other.CreateUnit("enemy-2", "red"), other.CreateUnit("ally", "blue")],
            EquipmentTypes = [new("gear", "Unfamiliar Gear") { LocalAbilities = [.. passives] }],
            LootCardDefinitions = [],
            Controllers = [new(new("carrier", "blue"), ControllerKind.Human),
                new(new("other", "red"), ControllerKind.Human), new(new("other", "blue"), ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
        };
    }
    private static LocalAbilityDefinition Passive(string key, AbilityMechanic mechanic) =>
        new(key, "Same display name", AbilityTiming.Passive, mechanic);

    [Fact]
    public void FuryAndBackstabUseAllOwnedPayloadsWithIndependentThresholdsAndAdditiveBonuses()
    {
        var state = World(Passive("one", new Fury { AdjacentEnemyThreshold = 1, AtkBonus = 2 }),
            Passive("two", new Fury { AdjacentEnemyThreshold = 2, AtkBonus = 3 }),
            Passive("three", new Fury { AdjacentEnemyThreshold = 3, AtkBonus = 100 }),
            Passive("back-one", new Backstab { AtkBonus = 4 }), Passive("back-two", new Backstab { AtkBonus = 5 }));
        state.Types[0] = state.Types[0] with { LocalAbilities = [Passive("native", new Fury { AtkBonus = 1 })] };
        state.EquipmentTypes.Add(new("second", "Other Gear")
        { LocalAbilities = [Passive("one", new Backstab { AtkBonus = 2 })] });
        state.Units[0] = state.Units[0] with { Inventory = [.. state.Units[0].Inventory, new("physical-2", "second")] };
        GameEngine.ValidateScenario(state);
        Assert.Equal(8, state.EffectiveAtkOf("a"));
        Assert.Equal(19, state.EffectiveAtkAgainst("a", "enemy"));
        Assert.Equal(8, state.EffectiveAtkAgainst("a", "enemy-2"));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(19, restored.EffectiveAtkAgainst("a", "enemy"));
        restored.Physical.Figures[0] = restored.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(2, restored.EffectiveAtkAgainst("a", "enemy"));
        Assert.Equal(19, state.EffectiveAtkAgainst("a", "enemy"));
    }

    [Fact]
    public void PassiveEquipmentFollowsOwnerAndDoesNotApplyFromGround()
    {
        var state = World(Passive("fury", new Fury { AdjacentEnemyThreshold = 1, AtkBonus = 3 }));
        Assert.Equal(5, state.EffectiveAtkOf("a"));
        var obj = Assert.Single(state.Units[0].Inventory);
        state.Units[0] = state.Units[0] with { Inventory = [] };
        state.GroundObjects.Add(new(obj, new(2, 2)));
        Assert.Equal(2, state.EffectiveAtkOf("a"));
        state.GroundObjects.Clear();
        state.Units[3] = state.Units[3] with { Inventory = [obj] };
        Assert.Equal(4, state.EffectiveAtkOf("ally"));
    }

    [Fact]
    public void EquipmentBackstabPreviewUsesSameDiceAsCommittedAttack()
    {
        var state = World(Passive("backstab", new Backstab { AtkBonus = 4 }));
        var start = TestGame.StartRound(state, new Dice(), false);
        var ready = TestGame.Advance(start.State, new Choice("stay"), new Dice(), false);
        var candidate = ready.NextInput!.Candidates.Single(c => c.Action == UnitAction.NormalAttack && c.TargetId == "enemy");
        Assert.Equal(6, new GameplayQueries(ready.State).EffectiveAttackDice("a", candidate));
        var attacked = TestGame.Advance(ready.State, new Choice(candidate.Key), new Dice(), false);
        Assert.Equal(6, Assert.Single(attacked.Events, e => e.Kind == "AttackResolved").Attack!.AttackDice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransportEquipmentUsesOwnMoveAndDoesNotReplaceDestinationResource(bool occupied)
    {
        var state = World(Passive("transport", new TransportResource()), Passive("duplicate", new TransportResource()));
        state.PlaceResource(ResourceTypes.Coal, new(2, 2));
        if (occupied) state.PlaceResource(ResourceTypes.Diamond, new(1, 2));
        var start = TestGame.StartRound(state, new Dice(), false);
        Assert.True(new GameplayQueries(start.State).CanTransportResource("a"));
        var moved = TestGame.Advance(start.State, new Choice("1,2"), new Dice(), false);
        Assert.Equal(occupied ? new(2, 2) : new Cell(1, 2), moved.State.Resources.Single(r => r.Type.Id == ResourceTypes.Coal.Id).Position);
        Assert.Equal(occupied ? 2 : 1, moved.State.Resources.Count);
        Assert.Equal(new(2, 2), state.Resources[0].Position);
    }

    [Fact]
    public void MultiplePhaseDeclarationsGrantTheSameTraversalCapabilityOnce()
    {
        var state = World(Passive("first", new Phase()), Passive("second", new Phase()));
        state.Physical.Board.Edges.Add(new(new(2, 2), new(1, 2), EdgeKind.ClosedDoor));
        GameEngine.ValidateScenario(state);
        Assert.Equal(2, MovementRules.FindPaths(state, "a", new(2, 2))[new(1, 2)].Count);
        state.Units[0] = state.Units[0] with { Inventory = [] };
        Assert.True(MovementRules.FindPaths(state, "a", new(2, 2))[new(1, 2)].Count > 2);
        state.Types[0] = state.Types[0] with
        { LocalAbilities = [Passive("first", new Phase()), Passive("second", new Phase())] };
        GameEngine.ValidateScenario(state);
        Assert.Equal(2, MovementRules.FindPaths(state, "a", new(2, 2))[new(1, 2)].Count);
    }

    [Fact]
    public void TransportEquipmentDoesNotEnableUndefinedLargeFootprintTransport()
    {
        var state = World(Passive("transport", new TransportResource()));
        state.Physical.Figures.RemoveAll(f => f.Id != "a");
        state.Units.RemoveAll(u => u.Id != "a");
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        state.PlaceResource(ResourceTypes.Coal, new(2, 2));
        Assert.False(new GameplayQueries(state).CanTransportResource("a"));
        ResourceRules.TransportDuringOwnMove(state, "a", new(2, 2), new(3, 2));
        Assert.Equal(new(2, 2), Assert.Single(state.Resources).Position);
    }
}
