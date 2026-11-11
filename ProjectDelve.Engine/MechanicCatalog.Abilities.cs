using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    private static partial IEnumerable<MechanicRegistration> AbilityRegistrations()
    {
        var threshold = new MechanicParameter("successThreshold", MechanicParameterKind.Integer) { Minimum = 1, Maximum = 6 };
        yield return MechanicRegistration.Create<BreakObject>(AbilityContract("breakObject", "Break Object",
            [MechanicTiming.Action, MechanicTiming.BonusAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.CellOrEdge, "One safely destructible neighboring Cell outside the source footprint using LOS ignoring only target Terrain, or one structure Edge bordering the footprint."),
            "Choose a neighboring destructible object or bordering structure. Roll a d6; break it on {successThreshold} or less. A success threshold of 6 succeeds without rolling.", ["successThreshold"], ["terrain", "edges", "lineOfSight", "dice"])
            with { Parameters = [threshold] },
            (payload, path, errors) => MechanicChecks.Integer(errors, threshold, payload.SuccessThreshold, path + ".successThreshold"));
        var adjacent = AbilityContract("breakAdjacentObjects", "Break Adjacent Objects",
            [MechanicTiming.Action, MechanicTiming.BonusAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "Every safely destructible neighboring Cell outside the source footprint using target-only Terrain LOS exemption, and every structure Edge bordering it. Freeze recipients before resolving."),
            "Break every neighboring destructible object and bordering structure on a separate d6 result of {successThreshold} or less. A success threshold of 6 succeeds without rolling.", ["successThreshold"], ["terrain", "edges", "lineOfSight", "dice"]);
        yield return MechanicRegistration.Create<BreakAdjacentObjects>(adjacent with
        {
            Parameters = [threshold],
            ContextRequirements = "Active uses require an Upright acting Unit and the declared Action or Bonus Action opportunity. Native WhenDefeated uses the captured defeat footprint and source Ability through the FIFO consequence queue.",
            Profiles = adjacent.Profiles.Add(new(MechanicSource.Unit, MechanicTiming.WhenDefeated, MechanicUsageSupport.UnlimitedOnly)),
            Description = adjacent.Description with { TimingTemplates = [new(MechanicTiming.WhenDefeated, "When this Unit is defeated, break every neighboring destructible object and bordering structure on a separate d6 result of {successThreshold} or less. A success threshold of 6 succeeds without rolling.")] }
        }, (payload, path, errors) => MechanicChecks.Integer(errors, threshold, payload.SuccessThreshold, path + ".successThreshold"));
        var defeatSelf = SpecializedMarker<DefeatSelf>("defeatSelf", "Defeat Self", MechanicTiming.InsteadOfActivation,
            "Instead of this Upright Unit's activation, defeat it.");
        yield return defeatSelf with { Descriptor = defeatSelf.Descriptor with
            { Dependencies = ["uprightSource", "activationSelection", "defeatReplacement", "defeatConsequences"] } };
        var selector = RelationshipSelectorParameter();
        var posture = new MechanicParameter("newPosture", MechanicParameterKind.Enum) { AllowedValues = ["Upright", "Lying"] };
        yield return MechanicRegistration.Create<AdjacentUnitsPostureChangeMechanic>(AbilityContract("adjacentUnitsPostureChange", "Adjacent Units Posture Change",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "All other living adjacent Units matching the relationship selector whose Posture differs from the result; legal with no recipients."),
            "{newPosture} all adjacent {selector} Units.", ["selector", "newPosture"],
            ["posture", "adjacency", "lineOfSight", "actionOpportunity"]) with { Parameters = [selector, posture] },
            (payload, path, errors) => ValidateAdjacentPosture(payload.Selector, payload.NewPosture, selector, posture, path, errors));

        var healAmount = new MechanicParameter("amount", MechanicParameterKind.Integer) { Minimum = 1 };
        yield return MechanicRegistration.Create<AdjacentUnitHealMechanic>(AbilityContract("adjacentUnitHeal", "Adjacent Unit Heal",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Unit, "One other living, damaged, adjacent Unit matching the relationship selector using ordinary adjacency and LOS."),
            "Heal an adjacent damaged {selector} Unit for {amount} HP.", ["selector", "amount"],
            ["hitPoints", "adjacency", "lineOfSight", "actionOpportunity"]) with { Parameters = [selector, healAmount] },
            (payload, path, errors) =>
            {
                MechanicChecks.Enum(errors, selector, payload.Selector, path + ".selector");
                MechanicChecks.Integer(errors, healAmount, payload.Amount, path + ".amount");
            });

        yield return MechanicRegistration.Create<FireballMechanic>(AbilityContract("fireball", "Target Cell Area Attack",
            [MechanicTiming.Action, MechanicTiming.BonusAction, MechanicTiming.FreeAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Cell, "Choose a Cell within effective RNG and Unit-origin LOS; automatically Attack every living Unit in its established explosion area and geometric LOS."),
            "Choose a Cell within RNG and LOS.\nAttack all Units on or adjacent to that Cell.", [],
            ["effectiveRange", "effectiveAttack", "lineOfSight", "attack", "dice"]), (_, _, _) => { });

        var revealDistance = new MechanicParameter("distance", MechanicParameterKind.Integer) { Minimum = 0 };
        yield return MechanicRegistration.Create<RevealDistance>(AbilityContract("revealDistance", "Reveal Distance",
            [MechanicTiming.Action, MechanicTiming.BonusAction, MechanicTiming.FreeAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "Reveal from every occupied Cell to the acting Unit's own Side; legal with no new discovery."),
            "Reveal Cells within {distance} Cells using this Unit's vision.", ["distance"], ["vision", "fogOfWar"])
            with { Parameters = [revealDistance] },
            (payload, path, errors) => MechanicChecks.Integer(errors, revealDistance, payload.Distance, path + ".distance"));

        var selfModifiers = AbilityModifiersParameter();
        var self = AbilityContract("selfModifiers", "Self Stat Modifiers", [MechanicTiming.BonusAction, MechanicTiming.Passive],
            MechanicContext.BearingUnit, new(MechanicTargetSelection.None, "The bearing Unit; Bonus Action modifiers last through the current activation, Passive modifiers require an Upright source."),
            "{modifiers} this activation.", ["modifiers"], ["effectiveStats", "posture", "activationModifiers"]);
        yield return MechanicRegistration.Create<SelfModifiersMechanic>(self with
        {
            Parameters = [selfModifiers],
            Description = self.Description with { TimingTemplates = [new(MechanicTiming.Passive, "{modifiers}.")] }
        }, (payload, path, errors) => ValidateAbilityModifiers(selfModifiers, payload.Modifiers, path + ".modifiers", errors));

        var adjacentModifiers = AbilityModifiersParameter();
        yield return MechanicRegistration.Create<AdjacentUnitsStatModifiersMechanic>(AbilityContract("adjacentUnitsStatModifiers", "Adjacent Units Stat Modifiers",
            [MechanicTiming.Passive], MechanicContext.BearingUnit,
            new(MechanicTargetSelection.None, "Other adjacent Units matching the relationship selector; requires an Upright source and ordinary adjacency and LOS."),
            "Adjacent {selector} Units get {modifiers}.", ["selector", "modifiers"], ["effectiveStats", "posture", "adjacency", "lineOfSight"])
            with { Parameters = [selector, adjacentModifiers] },
            (payload, path, errors) =>
            {
                MechanicChecks.Enum(errors, selector, payload.Selector, path + ".selector");
                ValidateAbilityModifiers(adjacentModifiers, payload.Modifiers, path + ".modifiers", errors);
            });

        var extraAmount = new MechanicParameter("amount", MechanicParameterKind.Integer) { Minimum = 1 };
        yield return MechanicRegistration.Create<ExtraActionsMechanic>(AbilityContract("extraActions", "Extra Actions",
            [MechanicTiming.BonusAction], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.None, "The acting Unit receives additional Actions during its current activation."),
            "Gain +{amount} Action this activation.", ["amount"], ["activationActions"])
            with { Parameters = [extraAmount] },
            (payload, path, errors) => MechanicChecks.Integer(errors, extraAmount, payload.Amount, path + ".amount"));

        yield return MechanicRegistration.Create<AdjacentUnitPostureChangeMechanic>(AbilityContract("adjacentUnitPostureChange", "Adjacent Unit Posture Change",
            [MechanicTiming.Action], MechanicContext.ActingUnit,
            new(MechanicTargetSelection.Unit, "One other living adjacent Unit matching the relationship selector whose Posture differs from the result, using ordinary adjacency and LOS."),
            "{newPosture} one adjacent {selector} Unit.", ["selector", "newPosture"], ["posture", "adjacency", "lineOfSight", "actionOpportunity"])
            with { Parameters = [selector, posture] },
            (payload, path, errors) => ValidateAdjacentPosture(payload.Selector, payload.NewPosture, selector, posture, path, errors));
    }

    private static MechanicParameter RelationshipSelectorParameter() => new("selector", MechanicParameterKind.Enum)
        { AllowedValues = ["Friendlies", "Enemies", "All"] };

    private static void ValidateAdjacentPosture(RelationshipSelector relationship, Posture result,
        MechanicParameter selector, MechanicParameter posture, string path, List<MechanicDiagnostic> errors)
    {
        MechanicChecks.Enum(errors, selector, relationship, path + ".selector");
        MechanicChecks.Enum(errors, posture, result, path + ".newPosture");
    }

    private static MechanicDescriptor AbilityContract(string id, string name, ImmutableArray<MechanicTiming> timings,
        MechanicContext context, MechanicTargeting targeting, string description, ImmutableArray<string> placeholders,
        ImmutableArray<string> dependencies) => new(id, name, MechanicFamily.Ability)
        {
            Grantable = true, SerializedBinding = id, Context = context, Targeting = targeting, Dependencies = dependencies,
            ContextRequirements = context == MechanicContext.BearingUnit
                ? "Requires a bearing Unit; continuous modifiers require an Upright source and temporary modifiers require its current activation."
                : "Requires an acting Unit and the declared timing opportunity. Other source or timing adapters are not implemented.",
            Profiles = timings.SelectMany(timing => timing == MechanicTiming.Passive
                ? new[] { new MechanicSupportProfile(MechanicSource.Unit, timing, MechanicUsageSupport.UnlimitedOnly),
                    new MechanicSupportProfile(MechanicSource.Equipment, timing, MechanicUsageSupport.UnlimitedOnly) }
                : new[] { new MechanicSupportProfile(MechanicSource.Unit, timing, MechanicUsageSupport.LimitedOrUnlimited),
                    new MechanicSupportProfile(MechanicSource.Equipment, timing, MechanicUsageSupport.LimitedOrUnlimited),
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
            Members = [new("stat", MechanicParameterKind.Enum) { AllowedValues = ["Atk", "Mov", "Rng", "Def", "SightDistance"] },
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
        AbilityTiming.AfterAttack => MechanicTiming.AfterAttack,
        AbilityTiming.WhenDefeated => MechanicTiming.WhenDefeated,
        AbilityTiming.InsteadOfDefeat => MechanicTiming.InsteadOfDefeat,
        AbilityTiming.InsteadOfActivation => MechanicTiming.InsteadOfActivation,
        AbilityTiming.EndActivation => MechanicTiming.EndActivation,
        _ => throw new ArgumentException("Unsupported Ability timing.")
    };

    internal static MechanicSource AbilitySourceContext(AbilitySourceKind source) => source switch
    {
        AbilitySourceKind.Unit => MechanicSource.Unit,
        AbilitySourceKind.Equipment => MechanicSource.Equipment,
        AbilitySourceKind.Item => MechanicSource.Item,
        _ => throw new ArgumentException("Unsupported Ability source.")
    };

    internal static string AbilityDescriptionTemplate(LocalAbilityDefinition ability)
    {
        if (ability.Mechanic is BreakObject { SuccessThreshold: 6 })
            return "Choose a neighboring destructible object or bordering structure. Break it without rolling.";
        if (ability.Mechanic is BreakAdjacentObjects { SuccessThreshold: 6 })
            return (ability.Timing == AbilityTiming.WhenDefeated ? "When this Unit is defeated, break" : "Break") +
                " every neighboring destructible object and bordering structure without rolling.";
        var descriptor = FindByPayload(ability.Mechanic)
            ?? throw new ArgumentException("Unsupported ability mechanic.");
        var timing = AbilityTimingContext(ability.Timing);
        return descriptor.Description.TimingTemplates.SingleOrDefault(t => t.Timing == timing)?.Template
            ?? descriptor.Description.Template;
    }

    internal static void ValidateAbilityOverride(LocalAbilityDefinition ability)
    {
        if (ability.DescriptionOverride is not { } text) return;
        var contract = FindByPayload(ability.Mechanic)!.Description;
        List<MechanicDiagnostic> diagnostics = [];
        MechanicChecks.Description(diagnostics, contract, text, "descriptionOverride");
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Path}: {d.Message}")));
    }
}
