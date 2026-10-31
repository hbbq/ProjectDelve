using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

[JsonConverter(typeof(JsonStringEnumConverter<MechanicFamily>))]
public enum MechanicFamily { Ability, ImmediateLoot, ImmediateWorld, ContinuousWorld, EnvironmentalProduction }
[JsonConverter(typeof(JsonStringEnumConverter<MechanicParameterKind>))]
public enum MechanicParameterKind { Integer, Boolean, Enum, Reference, List, Object, String }
[JsonConverter(typeof(JsonStringEnumConverter<MechanicSource>))]
public enum MechanicSource { Unit, Equipment, Item, WorldCard, LootCard, Terrain }
// Inspection contexts, not additional gameplay Ability timings.
[JsonConverter(typeof(JsonStringEnumConverter<MechanicTiming>))]
public enum MechanicTiming { Action, BonusAction, FreeAction, Passive, AfterAttack, WorldCardReveal, WhileWorldCardActive, LootReveal, RoundStart, WhenDefeated, InsteadOfDefeat, InsteadOfActivation, EndActivation }
[JsonConverter(typeof(JsonStringEnumConverter<MechanicUsageSupport>))]
public enum MechanicUsageSupport { NotApplicable, UnlimitedOnly, LimitedOrUnlimited, LimitedOnly }
[JsonConverter(typeof(JsonStringEnumConverter<MechanicContext>))]
public enum MechanicContext { ActingUnit, BearingUnit, CollectorUnit, TerrainCell, Global }
[JsonConverter(typeof(JsonStringEnumConverter<MechanicTargetSelection>))]
public enum MechanicTargetSelection { None, Unit, Cell }

// Public contracts contain serializable data only. Typed accessors and execution
// remain inside the Engine; these descriptions cannot execute game rules.
public sealed record MechanicParameter(string Name, MechanicParameterKind Kind)
{
    public bool Required { get; init; } = true;
    public bool AllowNull { get; init; }
    public int? Minimum { get; init; }
    public int? Maximum { get; init; }
    public JsonElement? DefaultValue { get; init; }
    public ImmutableArray<string> AllowedValues { get; init; } = [];
    public int? MinimumItems { get; init; }
    public int? MaximumItems { get; init; }
    public MechanicParameter? Item { get; init; }
    public ImmutableArray<MechanicParameter> Members { get; init; } = [];
    public string? ReferenceCatalog { get; init; }
}

public sealed record MechanicSupportProfile(MechanicSource Source, MechanicTiming Timing, MechanicUsageSupport Usage);
public sealed record MechanicTargeting(MechanicTargetSelection Selection, string Recipients);
public sealed record MechanicDescriptionTemplate(MechanicTiming Timing, string Template);
public sealed record MechanicDescription(string Template, ImmutableArray<string> Parameters)
{
    public ImmutableArray<MechanicDescriptionTemplate> TimingTemplates { get; init; } = [];
    public bool AllowsOverride { get; init; }
    public bool RejectsNumericOverrideLiterals { get; init; }
    public bool RejectsBlankOverride { get; init; }
    public bool RejectsUnknownOverridePlaceholders { get; init; }
    public ImmutableArray<string> AllowedOverridePlaceholders { get; init; } = [];
    public ImmutableArray<string> RequiredOverridePlaceholders { get; init; } = [];
    // Existing World modifier wording allows/requires its placeholder only for
    // a nonempty list. These names identify concrete list parameters, not expressions.
    public ImmutableArray<string> NonemptyListOverridePlaceholders { get; init; } = [];
}
public sealed record MechanicDescriptor(string Id, string Name, MechanicFamily Family)
{
    public ImmutableArray<MechanicParameter> Parameters { get; init; } = [];
    public ImmutableArray<MechanicSupportProfile> Profiles { get; init; } = [];
    public MechanicContext Context { get; init; }
    public MechanicTargeting Targeting { get; init; } = new(MechanicTargetSelection.None, "");
    public ImmutableArray<string> Dependencies { get; init; } = [];
    public MechanicDescription Description { get; init; } = new("", []);
    public bool Grantable { get; init; }
    public int? MaxPerSource { get; init; }
    // JSON discriminator, or the existing content field for non-polymorphic data.
    public string? SerializedBinding { get; init; }
}

public sealed record MechanicDiagnostic(string Path, string Code, string Message);
public sealed record MechanicValidationContext(MechanicSource Source, MechanicTiming Timing,
    int? MaxUses = null, bool HasUnitContext = true);
