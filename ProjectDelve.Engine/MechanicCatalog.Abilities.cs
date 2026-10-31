using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    private static partial IEnumerable<MechanicRegistration> AbilityRegistrations()
    {
        yield return MechanicRegistration.Create<HolyWaveMechanic>(AbilityContract("holyWave", "Holy Wave",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "Adjacent Upright hostile Units, followed by the acting Unit; legal with no hostile recipients."),
            "Lay down all adjacent upright enemies.\nThen lay down this Unit.", [],
            ["posture", "adjacency", "lineOfSight", "actionOpportunity"]), (_, _, _) => { });

        var healAmount = new MechanicParameter("amount", MechanicParameterKind.Integer) { Minimum = 1 };
        yield return MechanicRegistration.Create<HealMechanic>(AbilityContract("heal", "Heal",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Unit, "One other living, damaged, adjacent friendly Unit using ordinary adjacency and LOS."),
            "Heal an adjacent damaged friendly Unit for {amount} HP.", ["amount"],
            ["hitPoints", "adjacency", "lineOfSight", "actionOpportunity"]) with { Parameters = [healAmount] },
            (payload, path, errors) => MechanicChecks.Integer(errors, healAmount, payload.Amount, path + ".amount"));

        yield return MechanicRegistration.Create<FireballMechanic>(AbilityContract("fireball", "Fireball",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Cell, "Choose a Cell within effective RNG and Unit-origin LOS; automatically Attack every living Unit in its established explosion area and geometric LOS."),
            "Choose a Cell within RNG and LOS.\nAttack all Units on or adjacent to that Cell.", [],
            ["effectiveRange", "effectiveAttack", "lineOfSight", "attack", "dice", "actionOpportunity"]), (_, _, _) => { });

        var selfModifiers = AbilityModifiersParameter();
        var self = AbilityContract("selfModifiers", "Self Modifiers", [MechanicTiming.BonusAction, MechanicTiming.Passive],
            MechanicContext.BearingUnit, new(MechanicTargetSelection.None, "The bearing Unit; Bonus Action modifiers last through the current activation, Passive modifiers require an Upright source."),
            "{modifiers} this activation.", ["modifiers"], ["effectiveStats", "posture", "activationModifiers"]);
        yield return MechanicRegistration.Create<SelfModifiersMechanic>(self with
        {
            Parameters = [selfModifiers],
            Description = self.Description with { TimingTemplates = [new(MechanicTiming.Passive, "{modifiers}.")] }
        }, (payload, path, errors) => ValidateAbilityModifiers(selfModifiers, payload.Modifiers, path + ".modifiers", errors));

        var adjacentModifiers = AbilityModifiersParameter();
        yield return MechanicRegistration.Create<AdjacentModifiersMechanic>(AbilityContract("adjacentModifiers", "Adjacent Friendly Modifiers",
            [MechanicTiming.Passive], MechanicContext.BearingUnit,
            new(MechanicTargetSelection.None, "Other adjacent friendly Units; requires an Upright source and ordinary adjacency and LOS."),
            "Adjacent friendly Units get {modifiers}.", ["modifiers"], ["effectiveStats", "posture", "adjacency", "lineOfSight"])
            with { Parameters = [adjacentModifiers] },
            (payload, path, errors) => ValidateAbilityModifiers(adjacentModifiers, payload.Modifiers, path + ".modifiers", errors));

        var extraAmount = new MechanicParameter("amount", MechanicParameterKind.Integer) { Minimum = 1 };
        yield return MechanicRegistration.Create<ExtraActionsMechanic>(AbilityContract("extraActions", "Extra Actions",
            [MechanicTiming.BonusAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "The acting Unit receives additional Actions during its current activation."),
            "Gain +{amount} Action this activation.", ["amount"], ["activationActions"])
            with { Parameters = [extraAmount] },
            (payload, path, errors) => MechanicChecks.Integer(errors, extraAmount, payload.Amount, path + ".amount"));

        yield return MechanicRegistration.Create<StandAdjacentFriendlyMechanic>(AbilityContract("standAdjacentFriendly", "Stand Adjacent Friendly Unit",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Unit, "One living adjacent friendly Lying Unit using ordinary adjacency and LOS."),
            "Stand up one adjacent friendly Lying Unit.", [], ["posture", "adjacency", "lineOfSight", "actionOpportunity"]), (_, _, _) => { });
    }

    private static MechanicDescriptor AbilityContract(string id, string name, ImmutableArray<MechanicTiming> timings,
        MechanicContext context, MechanicTargeting targeting, string description, ImmutableArray<string> placeholders,
        ImmutableArray<string> dependencies) => new(id, name, MechanicFamily.Ability)
        {
            Grantable = true, SerializedBinding = id, Context = context, Targeting = targeting, Dependencies = dependencies,
            Profiles = timings.SelectMany(timing => timing == MechanicTiming.Passive
                ? new[] { new MechanicSupportProfile(MechanicSource.Unit, timing, MechanicUsageSupport.UnlimitedOnly),
                    new MechanicSupportProfile(MechanicSource.Equipment, timing, MechanicUsageSupport.UnlimitedOnly) }
                : new[] { new MechanicSupportProfile(MechanicSource.Unit, timing, MechanicUsageSupport.LimitedOrUnlimited),
                    new MechanicSupportProfile(MechanicSource.Equipment, timing, MechanicUsageSupport.UnlimitedOnly),
                    new MechanicSupportProfile(MechanicSource.Item, timing, MechanicUsageSupport.UnlimitedOnly) }).ToImmutableArray(),
            Description = new(description, placeholders)
            {
                AllowsOverride = true, RejectsNumericOverrideLiterals = true,
                AllowedOverridePlaceholders = placeholders, RequiredOverridePlaceholders = placeholders
            }
        };

    private static MechanicParameter AbilityModifiersParameter() => new("modifiers", MechanicParameterKind.List)
    {
        MinimumItems = 1,
        Item = new("modifier", MechanicParameterKind.Object)
        {
            Members = [new("stat", MechanicParameterKind.Enum) { AllowedValues = ["Atk", "Mov", "Rng", "Def"] },
                new("amount", MechanicParameterKind.Integer) { Minimum = -100, Maximum = 100 }]
        }
    };

    private static void ValidateAbilityModifiers(MechanicParameter parameter, ImmutableArray<ModifierThisTurn> modifiers,
        string path, List<MechanicDiagnostic> errors)
    {
        if (!MechanicChecks.List(errors, parameter, modifiers.IsDefault, modifiers.IsDefault ? 0 : modifiers.Length, path)) return;
        var stat = parameter.Item!.Members.Single(p => p.Name == "stat");
        var amount = parameter.Item.Members.Single(p => p.Name == "amount");
        for (var i = 0; i < modifiers.Length; i++)
        {
            var modifier = modifiers[i];
            var itemPath = $"{path}[{i}]";
            if (modifier is null) { MechanicChecks.Error(errors, itemPath, "required", "A modifier is required."); continue; }
            MechanicChecks.Enum(errors, stat, modifier.Stat, itemPath + ".stat");
            MechanicChecks.Integer(errors, amount, modifier.Amount, itemPath + ".amount");
        }
    }

    internal static MechanicTiming AbilityTimingContext(AbilityTiming timing) => timing switch
    {
        AbilityTiming.Action => MechanicTiming.Action,
        AbilityTiming.BonusAction => MechanicTiming.BonusAction,
        AbilityTiming.FreeAction => MechanicTiming.FreeAction,
        AbilityTiming.Passive => MechanicTiming.Passive,
        _ => throw new ArgumentException("Unsupported Ability timing.")
    };

    internal static MechanicSource AbilitySourceContext(AbilitySourceKind source) => source switch
    {
        AbilitySourceKind.Unit => MechanicSource.Unit,
        AbilitySourceKind.Equipment => MechanicSource.Equipment,
        AbilitySourceKind.Item => MechanicSource.Item,
        _ => throw new ArgumentException("Unsupported Ability source.")
    };

    internal static string AbilityDescriptionTemplate(AbilityDefinition ability)
    {
        var descriptor = FindByPayload(ability.Mechanic)
            ?? throw new ArgumentException("Unsupported ability mechanic.");
        var timing = AbilityTimingContext(ability.Timing);
        return descriptor.Description.TimingTemplates.SingleOrDefault(t => t.Timing == timing)?.Template
            ?? descriptor.Description.Template;
    }

    internal static void ValidateAbilityOverride(AbilityDefinition ability)
    {
        if (ability.DescriptionOverride is not { } text) return;
        var contract = FindByPayload(ability.Mechanic)!.Description;
        List<MechanicDiagnostic> diagnostics = [];
        MechanicChecks.Description(diagnostics, contract, text, "descriptionOverride");
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Path}: {d.Message}")));
    }
}
