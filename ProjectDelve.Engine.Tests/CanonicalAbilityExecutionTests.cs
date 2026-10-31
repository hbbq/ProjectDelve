using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalAbilityExecutionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(UnitType type)
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 5, Height = 3 },
            UnitTypeIds = [type.Id, "friend", "enemy"],
            UnitTypes = [type, new("friend", 0, 0, 0, 0, 5) { Unique = true }, new("enemy", 0, 0, 0, 0, 1)],
            Units = [new(type.Id, "blue", new(1, 1), UnitId: "actor"),
                new("friend", "blue", new(0, 1), InitialHp: 1, UnitId: "friend"),
                new("enemy", "red", new(2, 1), UnitId: "enemy")],
            Agency = [new(type.Id, "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human), new("enemy", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
        return GameEngine.CreateGame(definition);
    }

    private static GameState Ready(GameState state)
    {
        state.Round = 1;
        state.CurrentUnitId = "actor";
        state.ActiveToken = new(state.Units[0].TypeId, "blue");
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, state.Units[0].TypeId, "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfPassiveModifiersAffectEveryDeclaredStatFromNativeOrEquipmentSources(bool equipment)
    {
        var ability = new LocalAbilityDefinition("self", "Steady Bearing", AbilityTiming.Passive,
            new SelfModifiersMechanic([new(Stat.Atk, 2), new(Stat.Mov, 1), new(Stat.Rng, 2), new(Stat.Def, 3)]));
        var type = new UnitType("passive-bearer", 2, 1, 1, 1, 1)
        {
            CanHaveEquipment = true,
            LocalAbilities = equipment ? [] : [ability]
        };
        var state = State(type);
        if (equipment)
        {
            state.EquipmentTypes.Add(new("local-charm", "Small Charm") { LocalAbilities = [ability] });
            state.Units[0] = state.Units[0] with { Inventory = [new("charm-copy", "local-charm")] };
        }
        state = Restore(state);
        GameEngine.ValidateScenario(state);
        Assert.Equal((3, 3, 3, 4), (state.EffectiveAtkOf("actor"), state.EffectiveMovOf("actor"),
            state.EffectiveRngOf("actor"), state.EffectiveDefOf("actor")));
        Assert.Equal((0, 0, 0, 0), (state.EffectiveAtkOf("friend"), state.EffectiveMovOf("friend"),
            state.EffectiveRngOf("friend"), state.EffectiveDefOf("friend")));
        var figureIndex = state.Physical.Figures.FindIndex(f => f.Id == "actor");
        state.Physical.Figures[figureIndex] = state.Physical.Figures[figureIndex] with { Posture = Posture.Lying };
        Assert.Equal((1, 2, 1, 1), (state.EffectiveAtkOf("actor"), state.EffectiveMovOf("actor"),
            state.EffectiveRngOf("actor"), state.EffectiveDefOf("actor")));
    }

    [Fact]
    public void RenamedClericKeepsDeclaredLimitsAndPassiveValuesWithAnotherTypeId()
    {
        var state = Ready(State(UnitRoster.Cleric("other-cleric")));
        Assert.Equal(1, state.EffectiveDefOf("friend"));
        var heal = state.AbilitiesOf("actor").Single(a => a.Key == "heal");
        Assert.Equal(new AbilityUses(2, 2), state.AbilityUsesOf("actor", heal));
        var healed = GameEngine.Advance(state, new Choice("ability:heal:friend"), new Dice(), false);
        Assert.Equal(3, healed.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 1), healed.State.AbilityUsesOf("actor", heal));
        Assert.Equal(1, healed.State.Units[0].AbilityUseCounts["unit:heal"]);
        Assert.Equal("ability:heal", Assert.Single(healed.Events.Where(e => e.Kind == "HealResolved")).ActionId);
        var resumed = Restore(healed.State);
        Assert.Equal(new AbilityUses(2, 1), resumed.AbilityUsesOf("actor", heal));
    }
}
