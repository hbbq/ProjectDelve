using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LevelTests
{
    private static LocalAbilityDefinition Step(string key, int uses) => new(key, key, AbilityTiming.BonusAction,
        new SelfExtraMovesMechanic(1)) { Usage = new(uses) };

    private static ScenarioDefinition Scene()
    {
        var actor = new UnitType("scout", 3, 1, 2, 2, 4)
        {
            Unique = true, CanHaveEquipment = true,
            LocalAbilities = [new("swap", "Swap", AbilityTiming.Action, UnitAuthoring.Swap(RelationshipSelector.Friendlies)), Step("old", 2)],
            AdditionalLevels = [new(4, 0, 0, 3, 6)
            {
                Actions = UnitAction.None, ActionCountModifier = 1, MoveCountModifier = 1,
                LocalAbilities = [new("swap", "Swap", AbilityTiming.BonusAction, UnitAuthoring.Swap(RelationshipSelector.Friendlies)), Step("new", 3)]
            }]
        };
        var gear = new EquipmentDefinition("charm", "Charm")
        {
            LocalAbilities = [Step("stride", 2)],
            AdditionalLevels = [new()
            {
                MaxHpModifier = 2, BonusActionUseCountModifier = 1,
                LocalAbilities = [Step("dash", 3), new("swim", "Swim", AbilityTiming.Passive, new Amphibious())]
            }, new() { MaxHpModifier = -1 }]
        };
        return new()
        {
            Board = new() { Width = 8, Height = 5 },
            UnitTypeIds = ["scout"], UnitTypes = [actor], EquipmentTypes = [gear, gear with { Id = "ring", Name = "Ring" }],
            Units = [new("scout", "blue", new(1, 1), InitialHp: 2, UnitId: "a")
            {
                Inventory = [new("first", "charm") { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("stride", 1) },
                    new("second", "ring") { Level = 2, AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("dash", 1) }]
            }, new("scout", "red", new(6, 2), UnitId: "b") { Level = 2, Inventory = [new("third", "charm") { Level = 2 }] }],
            Agency = [new("scout", "blue", ControllerKind.Human), new("scout", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
    }

    [Fact]
    public void UpgradeReplacesEveryNativeGrantAndRefillsOnlyItsOwnSource()
    {
        var state = GameEngine.CreateGame(Scene());
        var old = state.AbilitiesOf("a").Single(g => g.Definition.Key == "old");
        state.Units[0] = state.Units[0] with { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(old.AccountingId, 1) };
        state.CurrentUnitId = "a";
        state.ActiveToken = state.Units[0].Token;
        state.MovesCompletedThisActivation = 1;
        state.ActionsUsedThisActivation = 1;
        Assert.Equal(6, state.EffectiveMaxHpOf("a"));
        Assert.Equal(8, state.Units[1].CurrentHp);
        Assert.Equal(AbilityTiming.Action, state.AbilitiesOf("a").Single(g => g.Definition.Key == "swap").Definition.Timing);
        var higher = GameEngine.UpgradeUnit(state, "a");
        Assert.Equal(1, state.Units[0].Level);
        Assert.Equal(2, higher.Units[0].Level);
        Assert.Equal(8, higher.Units[0].CurrentHp);
        Assert.Equal(8, higher.EffectiveMaxHpOf("a"));
        Assert.Equal("a", higher.CurrentUnitId);
        Assert.Equal(1, higher.MovesCompletedThisActivation);
        Assert.Equal(1, higher.ActionsUsedThisActivation);
        Assert.Equal(2, higher.EffectiveActionsPerActivation);
        Assert.Equal(2, higher.EffectiveMovesPerActivation);
        Assert.Equal(2, higher.EffectiveBonusActionUsesPerActivation);
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(higher))!;
        Assert.Equal(1, resumed.MovesCompletedThisActivation);
        Assert.False(resumed.MoveDone);
        Assert.Equal(state.Physical.Figures, higher.Physical.Figures);
        Assert.Equal(0, higher.EffectiveAtkOf("a"));
        Assert.False(higher.UnitConfiguration("a").HasAction(UnitAction.NormalAttack));
        var grants = higher.AbilitiesOf("a").ToList();
        Assert.DoesNotContain(grants, g => g.Definition.Key == "old");
        Assert.Equal(AbilityTiming.BonusAction, grants.Single(g => g.Definition.Key == "swap").Definition.Timing);
        Assert.Equal(3, higher.AbilityUsesOf("a", grants.Single(g => g.Definition.Key == "new"))!.RemainingUses);
        Assert.Equal(1, higher.AbilityUsesOf("a", grants.Single(g => g.Definition.Key == "stride"))!.RemainingUses);
        Assert.Equal(2, higher.AbilityUsesOf("a", grants.Single(g => g.Definition.Key == "dash"))!.RemainingUses);
        Assert.Throws<ArgumentException>(() => GameEngine.UpgradeUnit(higher, "a"));
    }

    [Fact]
    public void EquipmentReplacesOnlySelectedCopyAndClampsHpWithoutHealingOnGain()
    {
        var state = GameEngine.CreateGame(Scene());
        var old = state.AbilitiesOf("a").Single(g => g.Definition.Key == "old");
        state.Units[0] = state.Units[0] with { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(old.AccountingId, 1) };
        var changed = GameEngine.UpgradeEquipment(state, "first");
        Assert.Equal(2, changed.Units[0].CurrentHp);
        Assert.Equal(8, changed.EffectiveMaxHpOf("a"));
        Assert.Equal(1, changed.AbilityUsesOf("a", old)!.RemainingUses);
        var twins = changed.AbilitiesOf("a").Where(g => g.Definition.Key == "dash").ToArray();
        Assert.Equal(new[] { 3, 2 }, twins.Select(grant => changed.AbilityUsesOf("a", grant)!.RemainingUses));
        Assert.Equal(2, twins.Select(g => g.AccountingId).Distinct().Count());
        changed.Units[0] = changed.Units[0] with { CurrentHp = 8 };
        var reduced = GameEngine.UpgradeEquipment(changed, "first");
        Assert.Equal(5, reduced.EffectiveMaxHpOf("a"));
        Assert.Equal(5, reduced.Units[0].CurrentHp);
        Assert.Equal(3, reduced.Units[0].Inventory[0].Level);
        Assert.Equal(2, reduced.Units[0].Inventory[1].Level);
        Assert.Equal(2, reduced.Units[1].Inventory[0].Level);
        Assert.DoesNotContain(reduced.AbilitiesOf("a"), g => g.SourceInstanceId == "first");
    }

    [Fact]
    public void HealingAndDefeatKeepTheSelectedMaximumAndPhysicalEquipmentLevel()
    {
        var scene = Scene() with { WorldEffects = new(1, 1) { Composition = [new("miracle", 1)] } };
        var state = GameEngine.CreateGame(scene);
        var result = GameEngine.StartRound(state, new FixedRolls(), autoChooseSingleRelevantChoice: false);
        Assert.Equal(6, result.State.Units[0].CurrentHp);
        Assert.Contains(result.Events, e => e.Kind == "HealResolved" && e.TargetId == "a" && e.Healing == 4);
        var defeated = GameEngine.ResolveDefeat(state, "a");
        Assert.Equal(2, defeated.DefeatContext!.Unit.Inventory.Single(o => o.Id == "second").Level);
        var dropped = state.GroundObjects.Single(g => g.Object.Id == "second");
        Assert.Equal(2, dropped.Object.Level);
        Assert.Equal(1, dropped.Object.AbilityUseCounts["dash"]);
        var upgraded = GameEngine.UpgradeEquipment(state, dropped.Object.Id);
        var ground = upgraded.GroundObjects.Single(g => g.Object.Id == dropped.Object.Id);
        Assert.Equal(dropped.Position, ground.Position);
        Assert.Equal(3, ground.Object.Level);
        Assert.Empty(ground.Object.AbilityUseCounts);
    }

    private sealed class FixedRolls : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Fact]
    public void MoveDoneOnlyLegacySaveAndReorderedModernSaveRecoverTheRightMoveCount()
    {
        var state = GameEngine.CreateGame(Scene());
        state.CurrentUnitId = "b";
        state.ActiveToken = state.Units[1].Token;
        state.MovesCompletedThisActivation = 1;
        var saved = JsonSerializer.SerializeToNode(state)!.AsObject();
        saved.Remove(nameof(GameState.MovesCompletedThisActivation));
        saved.Remove(nameof(GameState.MoveDone));
        saved.Insert(0, nameof(GameState.MoveDone), true);
        var legacy = saved.Deserialize<GameState>()!;
        Assert.True(legacy.MoveDone);
        Assert.Equal(2, legacy.MovesCompletedThisActivation);
        saved[nameof(GameState.MovesCompletedThisActivation)] = 1;
        var modern = saved.Deserialize<GameState>()!;
        Assert.False(modern.MoveDone);
        Assert.Equal(1, modern.MovesCompletedThisActivation);
    }

    [Fact]
    public void NonUniqueCapsHpAtOneWithOtherEquipmentEffectsAndLevelsIntact()
    {
        var scene = Scene();
        var type = scene.UnitTypes![0];
        scene.UnitTypes[0] = type with { Unique = false, Hp = 1, AdditionalLevels = [type.AdditionalLevels[0] with { Hp = 1 }] };
        scene.Units[0] = scene.Units[0] with { InitialHp = null };
        var state = GameEngine.UpgradeUnit(GameEngine.CreateGame(scene), "a");
        Assert.Equal(1, state.Units[0].CurrentHp);
        Assert.Equal(1, state.EffectiveMaxHpOf("a"));
        Assert.Contains("water", state.EffectiveMovementCapabilitiesOf("a"));
        Assert.Contains(state.AbilitiesOf("a"), g => g.Definition.Key == "dash");
        scene.UnitTypes[0] = scene.UnitTypes[0] with { AdditionalLevels = [type.AdditionalLevels[0] with { Hp = 2 }] };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene));
    }

    [Fact]
    public void WaterOccupancyValidatesTheProposedConfigurationAtomically()
    {
        var scene = Scene();
        scene = scene with { Board = scene.Board with { Cells = [new(new(1, 1), TerrainKind.Water)] } };
        var state = GameEngine.CreateGame(scene);
        var saved = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => GameEngine.UpgradeEquipment(state, "second"));
        Assert.Equal(saved, JsonSerializer.Serialize(state));
        var bothSwim = GameEngine.UpgradeEquipment(state, "first");
        var changed = GameEngine.UpgradeEquipment(bothSwim, "second");
        Assert.Contains("water", changed.EffectiveMovementCapabilitiesOf("a"));
        GameEngine.ValidateScenario(changed);
    }

    [Fact]
    public void UsesTheSameConfigurationAtPlacementAfterTransportAndAfterSnapshotRestore()
    {
        var scene = Scene();
        scene.Units[0] = scene.Units[0] with { InitialHp = 6, Posture = Posture.Lying };
        var reopened = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scene));
        var state = GameEngine.CreateGame(reopened);
        Assert.Equal(6, state.EffectiveMaxHpOf("a"));
        var other = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(other);
        Assert.Equal(JsonSerializer.Serialize(GameEngine.UpgradeUnit(state, "a")), JsonSerializer.Serialize(GameEngine.UpgradeUnit(other, "a")));
        Assert.False(GameEngine.UpgradeUnit(other, "a").IsUpright("a"));
        scene.Units[0] = scene.Units[0] with { InitialHp = 7 };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene));
        scene.Units[0] = scene.Units[0] with { InitialHp = null, Level = 3 };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene));
        scene.Units[0] = scene.Units[0] with { Level = 1, Inventory = [new("bad", "charm") { Level = 0 }] };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene));
    }
}
