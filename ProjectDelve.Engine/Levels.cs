using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Each entry is the complete configuration printed at this Level. Identity,
// eligibility, shared storage, and drops remain on the Type.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UnitLevelDefinition([property: JsonRequired] int Mov, [property: JsonRequired] int Rng,
    [property: JsonRequired] int Atk, [property: JsonRequired] int Def, [property: JsonRequired] int Hp)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UpgradeCost { get; init; }
    public int SightDistance { get; init; } = 5;
    public UnitAction Actions { get; init; } = UnitAction.NormalAttack;
    public ImmutableArray<UnitAction> AttackDefinitionOrder { get; init; } = [];
    public MovementPattern? MovementPattern { get; init; }
    public TerrainRestrictedMovement? TerrainRestrictedMovement { get; init; }
    public TerrainRestriction? TerrainRestriction { get; init; }
    public ImmutableArray<string> BaseMovementCapabilities { get; init; } = MovementKinds.Default;
    public bool Amphibious { get; init; }
    public int MoveCountModifier { get; init; }
    public int ActionCountModifier { get; init; }
    public int BonusActionUseCountModifier { get; init; }
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EquipmentLevelDefinition
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UpgradeCost { get; init; }
    public int MaxHpModifier { get; init; }
    public int MoveCountModifier { get; init; }
    public int ActionCountModifier { get; init; }
    public int BonusActionUseCountModifier { get; init; }
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
}

public sealed partial record UnitType
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<UnitLevelDefinition> AdditionalLevels { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MoveCountModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActionCountModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BonusActionUseCountModifier { get; init; }

    private static readonly ConditionalWeakTable<UnitType, UnitType[]> configurations = new();
    internal int ConfigurationLevel { get; private init; } = 1;
    public UnitType AtLevel(int level)
    {
        if (level == ConfigurationLevel) return this;
        if (level < 2 || AdditionalLevels.IsDefaultOrEmpty || level - 2 >= AdditionalLevels.Length)
            throw new ArgumentException($"Unit Type '{Id}' has no definition for Level {level}.");
        return configurations.GetValue(this, type => type.AdditionalLevels.Select((entry, index) =>
            entry is null ? throw ScenarioValidationContext.At(new ArgumentException($"Unit Level {index + 2} requires a definition."),
                $"additionalLevels[{index}]") : type with
            {
                Mov = entry.Mov, Rng = entry.Rng, Atk = entry.Atk, Def = entry.Def, Hp = entry.Hp,
                SightDistance = entry.SightDistance, Actions = entry.Actions, MovementPattern = entry.MovementPattern,
                AttackDefinitionOrder = entry.AttackDefinitionOrder, TerrainRestrictedMovement = entry.TerrainRestrictedMovement,
                TerrainRestriction = entry.TerrainRestriction,
                BaseMovementCapabilities = entry.BaseMovementCapabilities, Amphibious = entry.Amphibious,
                MoveCountModifier = entry.MoveCountModifier, ActionCountModifier = entry.ActionCountModifier,
                BonusActionUseCountModifier = entry.BonusActionUseCountModifier, LocalAbilities = entry.LocalAbilities,
                ConfigurationLevel = index + 2, AdditionalLevels = default
            }).ToArray())[level - 2];
    }
}

public sealed partial record EquipmentDefinition
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<EquipmentLevelDefinition> AdditionalLevels { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxHpModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MoveCountModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActionCountModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BonusActionUseCountModifier { get; init; }

    private static readonly ConditionalWeakTable<EquipmentDefinition, EquipmentDefinition[]> configurations = new();
    private int ConfigurationLevel { get; init; } = 1;
    public EquipmentDefinition AtLevel(int level)
    {
        if (level == ConfigurationLevel) return this;
        if (level < 2 || AdditionalLevels.IsDefaultOrEmpty || level - 2 >= AdditionalLevels.Length)
            throw new ArgumentException($"Equipment Type '{Id}' has no definition for Level {level}.");
        return configurations.GetValue(this, type => type.AdditionalLevels.Select((entry, index) =>
            entry is null ? throw ScenarioValidationContext.At(new ArgumentException($"Equipment Level {index + 2} requires a definition."),
                $"additionalLevels[{index}]") : type with
            {
                MaxHpModifier = entry.MaxHpModifier, MoveCountModifier = entry.MoveCountModifier,
                ActionCountModifier = entry.ActionCountModifier, BonusActionUseCountModifier = entry.BonusActionUseCountModifier,
                LocalAbilities = entry.LocalAbilities, ConfigurationLevel = index + 2, AdditionalLevels = default
            }).ToArray())[level - 2];
    }
}

public static class LevelQueries
{
    public static int? UnitUpgradeCostOf(this GameState state, Unit unit)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        return !type.AdditionalLevels.IsDefaultOrEmpty && unit.Level >= 1 && unit.Level <= type.AdditionalLevels.Length
            ? type.AdditionalLevels[unit.Level - 1].UpgradeCost : null;
    }

    public static int? EquipmentUpgradeCostOf(this GameState state, OwnedObject obj)
    {
        if (state.EquipmentTypeId(obj) is not { } id) return null;
        var type = state.Equipment(id);
        return !type.AdditionalLevels.IsDefaultOrEmpty && obj.Level >= 1 && obj.Level <= type.AdditionalLevels.Length
            ? type.AdditionalLevels[obj.Level - 1].UpgradeCost : null;
    }

    public static UnitType UnitConfiguration(this GameState state, Unit unit) =>
        state.Types.Single(t => t.Id == unit.TypeId).AtLevel(unit.Level);
    public static UnitType UnitConfiguration(this GameState state, string id) =>
        state.UnitConfiguration(state.Units.Single(u => u.Id == id));
    public static EquipmentDefinition EquipmentConfiguration(this GameState state, OwnedObject obj) =>
        state.Equipment(state.EquipmentTypeId(obj) ?? throw new ArgumentException($"Object '{obj.Id}' is not Equipment.")).AtLevel(obj.Level);

    internal static IEnumerable<EquipmentDefinition> Equipped(this GameState state, Unit unit)
    {
        foreach (var obj in unit.Inventory)
            if (state.EquipmentTypeId(obj) is { } id) yield return state.Equipment(id).AtLevel(obj.Level);
    }

    public static int EffectiveMaxHpOf(this GameState state, string unitId) =>
        state.EffectiveMaxHpOf(state.Units.Single(u => u.Id == unitId));
    public static int EffectiveMaxHpOf(this GameState state, Unit unit)
    {
        var configuration = state.UnitConfiguration(unit);
        if (!configuration.Unique) return 1;
        return (int)Math.Clamp(configuration.Hp + state.Equipped(unit).Sum(gear => (long)gear.MaxHpModifier), 1L, int.MaxValue);
    }

    internal static Unit WithInventory(this GameState state, Unit unit, ImmutableArray<OwnedObject> inventory)
    {
        var changed = unit with { Inventory = inventory };
        return changed with { CurrentHp = Math.Min(changed.CurrentHp, state.EffectiveMaxHpOf(changed)) };
    }
}

public static partial class GameEngine
{
    // Authoritative transformations for callers composing an upgrade. There is
    // no player command, cost, RNG draw, or activation accounting in this layer.
    public static GameState UpgradeUnit(GameState state, string unitId)
    {
        var result = BeginLevelChange(state);
        int index = result.Units.FindIndex(u => u.Id == unitId && u.CurrentHp > 0);
        if (index < 0) throw new ArgumentException($"Cannot upgrade Unit '{unitId}': a living Unit is required.");
        var unit = result.Units[index];
        var upgraded = AdvanceUnitLevel(result, unit);
        result.Units[index] = upgraded with { CurrentHp = result.EffectiveMaxHpOf(upgraded) };
        return FinishLevelChange(result, unitId);
    }

    public static GameState UpgradeEquipment(GameState state, string objectId)
    {
        var result = BeginLevelChange(state);
        for (int i = 0; i < result.Units.Count; i++)
        {
            var unit = result.Units[i];
            if (unit.Inventory.FirstOrDefault(o => o.Id == objectId) is not { } obj) continue;
            result.Units[i] = result.WithInventory(unit, unit.Inventory.Replace(obj, AdvanceEquipmentLevel(result, obj)));
            return FinishLevelChange(result, unit.Id);
        }
        int index = result.GroundObjects.FindIndex(g => g.Object.Id == objectId);
        if (index < 0) throw new ArgumentException($"No owned or ground Equipment instance '{objectId}' is in play.");
        var ground = result.GroundObjects[index];
        result.GroundObjects[index] = ground with { Object = AdvanceEquipmentLevel(result, ground.Object) };
        return FinishLevelChange(result, null);
    }

    private static Unit AdvanceUnitLevel(GameState state, Unit unit)
    {
        int next = checked(unit.Level + 1);
        _ = state.Types.Single(t => t.Id == unit.TypeId).AtLevel(next);
        return unit with { Level = next, AbilityUseCounts = ImmutableDictionary<string, int>.Empty };
    }

    private static OwnedObject AdvanceEquipmentLevel(GameState state, OwnedObject obj)
    {
        if (state.EquipmentTypeId(obj) is not { } typeId)
            throw new ArgumentException($"Object '{obj.Id}' is not Equipment.");
        int next = checked(obj.Level + 1);
        _ = state.Equipment(typeId).AtLevel(next);
        return obj with { Level = next, AbilityUseCounts = ImmutableDictionary<string, int>.Empty };
    }

    private static GameState BeginLevelChange(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.AttackInProgress is not null || state.AttackAwaitingConsequences is not null ||
            state.BreakInProgress is not null || state.DoorInProgress is not null ||
            state.PendingDefeatEffects.Count != 0 || state.CleavePending || state.MoveAfterAttackAllowance is not null ||
            state.MachineInteraction is not null)
            throw new InvalidOperationException("Finish the resolving sequence before changing an instance's Level.");
        return state.DetachedCopy();
    }

    private static GameState FinishLevelChange(GameState state, string? bearerId)
    {
        if (bearerId is not null &&
            (state.Physical.Figures.SingleOrDefault(f => f.Id == bearerId) is not { } figure ||
            !SpatialRules.CanPlaceUnit(state, bearerId, figure.Position)))
            throw new ArgumentException($"The next Level does not permit Unit '{bearerId}' to occupy its current footprint.");
        FogOfWar.Refresh(state);
        if (state.Pending is not null) state.Pending = CreateDecision(state);
        return state;
    }
}
