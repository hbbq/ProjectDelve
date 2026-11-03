using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace ProjectDelve.Engine;

// Authoring metadata describes the existing UnitType fields. It is not another
// definition model and does not execute mechanics or decide gameplay legality.
public sealed record UnitBehaviorOption(string Value, string Name);
public sealed record UnitAuthoringContract(UnitType Defaults, ImmutableArray<MechanicParameter> Stats,
    ImmutableArray<MechanicParameter> Fields, string[] Footprints, string[] MovementPatterns,
    string[] TerrainKinds, ImmutableArray<UnitBehaviorOption> Behaviors, string[] AttackActions);

public static class UnitAuthoringSchema
{
    public const int MinimumStat = 0, MinimumHp = 1, MinimumDropSuccesses = 0, MaximumDropSuccesses = 6;
    public static ImmutableArray<UnitAction> AttackActions { get; } =
        [UnitAction.NormalAttack, UnitAction.Fireball, UnitAction.FireBreath, UnitAction.ClawAttack, UnitAction.ShootShell];
    internal static UnitBehavior SupportedBehaviors { get; } = Enum.GetValues<UnitBehavior>().Aggregate(UnitBehavior.None, (a, b) => a | b);

    public static UnitAuthoringContract Contract { get; } = Create();
    private static UnitAuthoringContract Create()
    {
        MechanicParameter Integer(string name, int minimum, int? maximum = null) => new(name, MechanicParameterKind.Integer)
            { Minimum = minimum, Maximum = maximum };
        MechanicParameter Text(string name) => new(name, MechanicParameterKind.String);
        MechanicParameter List(string name, MechanicParameter item, int minimum = 0) => new(name, MechanicParameterKind.List)
            { MinimumItems = minimum, Item = item };
        MechanicParameter Object(string name, params MechanicParameter[] members) => new(name, MechanicParameterKind.Object)
            { Members = [.. members] };
        MechanicParameter EnumField(string name, IEnumerable<string> values) => new(name, MechanicParameterKind.Enum)
            { AllowedValues = [.. values] };
        MechanicParameter Optional(MechanicParameter field) => field with { Required = false, AllowNull = true };
        var terrains = Enum.GetValues<TerrainKind>().Where(t => t.Passable()).Select(t => t.ToString()).ToArray();
        return new(new("", 3, 1, 1, 1, 1) { DisplayName = "" },
            [Integer("mov", MinimumStat), Integer("rng", MinimumStat), Integer("atk", MinimumStat),
                Integer("def", MinimumStat), Integer("hp", MinimumHp)],
            [new("unique", MechanicParameterKind.Boolean), EnumField("footprint", Enum.GetNames<Footprint>()),
                new("canHaveItems", MechanicParameterKind.Boolean), new("canHaveEquipment", MechanicParameterKind.Boolean),
                Integer("lootDropSuccesses", MinimumDropSuccesses, MaximumDropSuccesses),
                Optional(EnumField("movementPattern", Enum.GetNames<MovementPattern>())),
                Optional(Object("terrainRestrictedMovement", EnumField("terrain", terrains))),
                Optional(Object("terrainRestriction", List("categories", Text("category"), 1))),
                Optional(Object("resourceStorage", List("capacities", Object("capacity", Object("type", Text("id"), Text("name")),
                    Integer("capacity", ResourceRules.MinimumCapacity)), 1))),
                List("attackDefinitionOrder", EnumField("attack", AttackActions.Select(a => a.ToString())))],
            Enum.GetNames<Footprint>(), Enum.GetNames<MovementPattern>(), terrains,
            [.. Enum.GetValues<UnitBehavior>().Where(b => b != UnitBehavior.None).Select(b => new UnitBehaviorOption(b.ToString(),
                Regex.Replace(b.ToString(), "(?<=[a-z])([A-Z])", " $1")))],
            [.. AttackActions.Select(a => a.ToString())]);
    }
}
