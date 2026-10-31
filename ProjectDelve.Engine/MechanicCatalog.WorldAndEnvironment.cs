using System.Collections.Immutable;
using System.Text.Json;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    private static partial IEnumerable<MechanicRegistration> WorldAndEnvironmentRegistrations()
    {
        var amount = new MechanicParameter("amount", MechanicParameterKind.Integer)
        { Minimum = 1, Maximum = 100 };

        yield return MechanicRegistration.Create<HealCollectorMechanic>(new("healCollector", "Heal collector", MechanicFamily.ImmediateLoot)
        {
            Parameters = [amount],
            Profiles = [new(MechanicSource.LootCard, MechanicTiming.LootReveal, MechanicUsageSupport.NotApplicable)],
            Context = MechanicContext.CollectorUnit,
            Targeting = new(MechanicTargetSelection.None, "The collecting Unit, with healing capped at its maximum HP."),
            Dependencies = ["Collecting Unit", "Current and maximum HP", "Mandatory Loot reveal"],
            Description = new("Restore {amount} HP, without exceeding maximum HP.", ["amount"]),
            MaxPerSource = 1,
            SerializedBinding = "lootCard.immediate.kind=healCollector"
        }, (payload, path, errors) => MechanicChecks.Integer(errors, amount, payload.Amount, path + ".amount"));

        yield return MechanicRegistration.Create<LayDownAllUnitsMechanic>(ImmediateWorldDescriptor(
            "layDownAllUnits", "Lay down all Units", [],
            "All Upright Units on the Board.", ["Unit Posture"],
            new("Lay down all Units.", [])), (_, _, _) => { });

        var open = new MechanicParameter("open", MechanicParameterKind.Boolean);
        yield return MechanicRegistration.Create<SetAllDoorsMechanic>(ImmediateWorldDescriptor(
            "setAllDoors", "Set all Doors", [open],
            "All Closed Doors when opening, or all Open Doors when closing.", ["Board Doors"],
            new("Open all Doors when {open} is true; otherwise close all Doors.", ["open"])), (_, _, _) => { });

        yield return MechanicRegistration.Create<FullyHealUnitsMechanic>(ImmediateWorldDescriptor(
            "fullyHealUnits", "Fully heal Units", [],
            "All Units on the Board, without changing Posture.", ["Current and maximum HP"],
            new("Fully heal all Units.", [])), (_, _, _) => { });

        yield return MechanicRegistration.Create<HealDamagedUnitsMechanic>(ImmediateWorldDescriptor(
            "healDamagedUnits", "Heal damaged Units", [amount],
            "Every damaged Unit on the Board, with healing capped at its maximum HP.", ["Current and maximum HP"],
            new("Heal every damaged Unit for {amount} HP.", ["amount"])),
            (payload, path, errors) => MechanicChecks.Integer(errors, amount, payload.Amount, path + ".amount"));

        var adjacent = new MechanicParameter("adjacent", MechanicParameterKind.Boolean);
        yield return MechanicRegistration.Create<DamageUnitsByAdjacencyMechanic>(ImmediateWorldDescriptor(
            "damageUnitsByAdjacency", "Damage Units by adjacency", [amount, adjacent],
            "Units adjacent to another Unit when adjacent is true, or Units not adjacent to another Unit otherwise; ignores Side.",
            ["Ordinary adjacency and LOS", "Damage and automatic consequences"],
            new("Deal {amount} damage to every Unit whose adjacency to another Unit matches {adjacent}.", ["amount", "adjacent"])),
            (payload, path, errors) => MechanicChecks.Integer(errors, amount, payload.Amount, path + ".amount"));

        yield return MechanicRegistration.Create<ReplenishLimitedAbilitiesMechanic>(ImmediateWorldDescriptor(
            "replenishLimitedAbilities", "Replenish limited Abilities", [amount],
            "Every limited-use Ability on every Unit on the Board, capped at its normal maximum; unlimited Abilities are unaffected.",
            ["Limited Ability use counters"],
            new("Replenish {amount} uses of every limited-use Ability.", ["amount"])),
            (payload, path, errors) => MechanicChecks.Integer(errors, amount, payload.Amount, path + ".amount"));

        var target = new MechanicParameter("target", MechanicParameterKind.Enum)
        { AllowedValues = [.. Enum.GetNames<WorldModifierTarget>()] };
        var modifierAmount = new MechanicParameter("amount", MechanicParameterKind.Integer)
        { Minimum = -100, Maximum = 100 };
        var modifiers = new MechanicParameter("modifiers", MechanicParameterKind.List)
        {
            Required = false,
            DefaultValue = JsonSerializer.SerializeToElement(Array.Empty<WorldModifier>()),
            MinimumItems = 0,
            Item = new("modifier", MechanicParameterKind.Object) { Members = [target, modifierAmount] }
        };
        yield return MechanicRegistration.Create<ImmutableArray<WorldModifier>>(new("worldModifiers", "Continuous World modifiers", MechanicFamily.ContinuousWorld)
        {
            Parameters = [modifiers],
            Profiles = [new(MechanicSource.WorldCard, MechanicTiming.WhileWorldCardActive, MechanicUsageSupport.NotApplicable)],
            Context = MechanicContext.Global,
            Targeting = new(MechanicTargetSelection.None, "All Units; each unique Bonus Action for BonusActionUses modifiers."),
            Dependencies = ["Active Continuous World cards", "Effective values floored at zero", "Ordinary Action and Bonus Action restrictions"],
            Description = new("{modifiers}", ["modifiers"])
            {
                AllowsOverride = true,
                RejectsNumericOverrideLiterals = true,
                RejectsBlankOverride = true,
                RejectsUnknownOverridePlaceholders = true,
                AllowedOverridePlaceholders = ["modifiers"],
                RequiredOverridePlaceholders = ["modifiers"],
                NonemptyListOverridePlaceholders = ["modifiers"]
            },
            SerializedBinding = "worldCard.modifiers"
        }, (payload, path, errors) =>
        {
            if (!MechanicChecks.List(errors, modifiers, payload.IsDefault, payload.IsDefault ? 0 : payload.Length, path + ".modifiers")) return;
            for (var index = 0; index < payload.Length; index++)
            {
                var entryPath = $"{path}.modifiers[{index}]";
                if (payload[index] is not { } modifier)
                {
                    MechanicChecks.Error(errors, entryPath, "required", "A World modifier is required.");
                    continue;
                }
                MechanicChecks.Enum(errors, target, modifier.Target, entryPath + ".target");
                MechanicChecks.Integer(errors, modifierAmount, modifier.Amount, entryPath + ".amount");
            }
        });

        var productionDefaults = new DirectionalResourceProduction(string.Empty, 0);
        var resource = new MechanicParameter("resourceTypeId", MechanicParameterKind.Reference) { ReferenceCatalog = "resources" };
        var attempts = new MechanicParameter("attempts", MechanicParameterKind.Integer) { Minimum = 1, Maximum = 100 };
        var placement = new MechanicParameter("placement", MechanicParameterKind.Enum)
        {
            Required = false,
            DefaultValue = JsonSerializer.SerializeToElement(productionDefaults.Placement.ToString()),
            AllowedValues = [ProductionPlacement.SurroundingCellD8.ToString()]
        };
        var sourceLos = new MechanicParameter("sourceLos", MechanicParameterKind.Enum)
        {
            Required = false,
            DefaultValue = JsonSerializer.SerializeToElement(productionDefaults.SourceLos.ToString()),
            AllowedValues = [.. Enum.GetNames<ProductionSourceLos>()]
        };
        yield return MechanicRegistration.Create<DirectionalResourceProduction>(new("directionalResourceProduction", "Directional Resource production", MechanicFamily.EnvironmentalProduction)
        {
            Parameters = [resource, attempts, placement, sourceLos],
            Profiles = [new(MechanicSource.Terrain, MechanicTiming.RoundStart, MechanicUsageSupport.NotApplicable)],
            Context = MechanicContext.TerrainCell,
            Targeting = new(MechanicTargetSelection.None, "A d8 selects one of the eight surrounding Cells per attempt; failed placement does not reroll."),
            Dependencies = ["Terrain Cell", "Resource Type catalog", "d8 rolls", "Resource placement eligibility", "Geometric LOS", "World consequences completed before production"],
            Description = new("At Round Start, make {attempts} d8 attempts to place one {resourceTypeId} on the selected surrounding Cell, using the declared placement and source LOS policies.",
                ["attempts", "resourceTypeId", "placement", "sourceLos"]),
            MaxPerSource = 1,
            SerializedBinding = "terrain.production.kind=directionalResourceProduction"
        }, (payload, path, errors) =>
        {
            MechanicChecks.Reference(errors, resource, payload.ResourceTypeId, path + ".resourceTypeId");
            MechanicChecks.Integer(errors, attempts, payload.Attempts, path + ".attempts");
            MechanicChecks.Enum(errors, placement, payload.Placement, path + ".placement");
            MechanicChecks.Enum(errors, sourceLos, payload.SourceLos, path + ".sourceLos");
        });
    }

    private static MechanicDescriptor ImmediateWorldDescriptor(string id, string name, ImmutableArray<MechanicParameter> parameters,
        string recipients, ImmutableArray<string> dependencies, MechanicDescription description) => new(id, name, MechanicFamily.ImmediateWorld)
        {
            Parameters = parameters,
            Profiles = [new(MechanicSource.WorldCard, MechanicTiming.WorldCardReveal, MechanicUsageSupport.NotApplicable)],
            Context = MechanicContext.Global,
            Targeting = new(MechanicTargetSelection.None, recipients),
            Dependencies = dependencies,
            Description = description with
            {
                AllowsOverride = true,
                RejectsNumericOverrideLiterals = true,
                RejectsBlankOverride = true,
                RejectsUnknownOverridePlaceholders = true,
                AllowedOverridePlaceholders = parameters.Any(parameter => parameter.Name == "amount") ? ["amount"] : [],
                RequiredOverridePlaceholders = parameters.Any(parameter => parameter.Name == "amount") ? ["amount"] : []
            },
            MaxPerSource = 1,
            SerializedBinding = "worldCard.immediate.kind=" + id
        };
}
