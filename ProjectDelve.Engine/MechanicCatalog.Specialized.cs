using System.Text.Json;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    // These entries describe existing native Unit fields, not AbilityMechanic
    // discriminators. They neither grant abilities nor change their storage.
    private static partial IEnumerable<MechanicRegistration> SpecializedRegistrations()
    {
        var cleaveDefaults = new Cleave();
        var cleaveUses = NativeInteger("maxUses", cleaveDefaults.MaxUses, minimum: 1);
        var triggerDamage = NativeInteger("triggerDamage", cleaveDefaults.TriggerDamage, minimum: 1);
        var cleaveDamage = NativeInteger("damage", cleaveDefaults.Damage, minimum: 1);
        yield return MechanicRegistration.Create<Cleave>(new("cleave", "Cleave", MechanicFamily.NativeUnit)
        {
            Parameters = [cleaveUses, triggerDamage, cleaveDamage],
            Profiles = [new(MechanicSource.Unit, MechanicTiming.AfterAttack, MechanicUsageSupport.LimitedOnly)],
            Context = MechanicContext.ActingUnit,
            Targeting = new(MechanicTargetSelection.Unit, "One living adjacent hostile Unit, Upright or Lying, using ordinary adjacency and LOS."),
            Dependencies = ["Completed Attack damage per target", "Upright acting Unit", "Remaining native Cleave uses"],
            Description = new("After this Unit's Attack, if it dealt {triggerDamage} or more damage to a Unit, you may immediately deal {damage} damage to an adjacent enemy.",
                ["triggerDamage", "damage"]),
            SerializedBinding = "unitType.cleave",
            Grantable = false,
            MaxPerSource = 1
        }, (payload, path, errors) =>
        {
            MechanicChecks.Integer(errors, cleaveUses, payload.MaxUses, $"{path}.{cleaveUses.Name}");
            MechanicChecks.Integer(errors, triggerDamage, payload.TriggerDamage, $"{path}.{triggerDamage.Name}");
            MechanicChecks.Integer(errors, cleaveDamage, payload.Damage, $"{path}.{cleaveDamage.Name}");
        });

        yield return MechanicRegistration.Create<Telekinesis>(new("telekinesis", "Telekinesis", MechanicFamily.NativeUnit)
        {
            Profiles = [new(MechanicSource.Unit, MechanicTiming.Action, MechanicUsageSupport.UnlimitedOnly)],
            Context = MechanicContext.ActingUnit,
            Targeting = new(MechanicTargetSelection.Unit, "One Upright hostile Unit within effective RNG and normal Unit-origin LOS through the same occupied-Cell pair."),
            Dependencies = ["Native Telekinesis Action slot", "Effective RNG", "Normal Unit-origin LOS", "Ordinary Action opportunity"],
            Description = new("Lay down an upright enemy within RNG and LOS.", []),
            SerializedBinding = "unitType.actions",
            Grantable = false,
            MaxPerSource = 1
        }, (_, _, _) => { });

        var furyDefaults = new Fury();
        var adjacentEnemies = NativeInteger("adjacentEnemyThreshold", furyDefaults.AdjacentEnemyThreshold, minimum: 1);
        var furyBonus = NativeInteger("atkBonus", furyDefaults.AtkBonus);
        yield return MechanicRegistration.Create<Fury>(new("fury", "Fury", MechanicFamily.NativeUnit)
        {
            Parameters = [adjacentEnemies, furyBonus],
            Profiles = [new(MechanicSource.Unit, MechanicTiming.Passive, MechanicUsageSupport.UnlimitedOnly)],
            Context = MechanicContext.BearingUnit,
            Targeting = new(MechanicTargetSelection.None, "The bearing Unit; adjacent hostile Units establish the condition once per Unit."),
            Dependencies = ["Upright bearing Unit", "Ordinary adjacency and LOS", "General effective ATK"],
            Description = new("ATK {atkBonus} while adjacent to {adjacentEnemyThreshold} or more enemies.",
                ["atkBonus", "adjacentEnemyThreshold"]),
            SerializedBinding = "unitType.fury",
            Grantable = false,
            MaxPerSource = 1
        }, (payload, path, errors) =>
        {
            MechanicChecks.Integer(errors, adjacentEnemies, payload.AdjacentEnemyThreshold, $"{path}.{adjacentEnemies.Name}");
            MechanicChecks.Integer(errors, furyBonus, payload.AtkBonus, $"{path}.{furyBonus.Name}");
        });

        var backstabBonus = NativeInteger("atkBonus", new Backstab().AtkBonus);
        yield return MechanicRegistration.Create<Backstab>(new("backstab", "Backstab", MechanicFamily.NativeUnit)
        {
            Parameters = [backstabBonus],
            Profiles = [new(MechanicSource.Unit, MechanicTiming.Passive, MechanicUsageSupport.UnlimitedOnly)],
            Context = MechanicContext.ActingUnit,
            Targeting = new(MechanicTargetSelection.None, "The current Attack target establishes the condition; there is no additional target choice."),
            Dependencies = ["Upright attacker", "Attack target", "Another friendly Unit adjacent to that target", "Target-specific Attack Dice"],
            Description = new("ATK {atkBonus} when attacking an enemy adjacent to another friendly Unit.", ["atkBonus"]),
            SerializedBinding = "unitType.backstab",
            Grantable = false,
            MaxPerSource = 1
        }, (payload, path, errors) => MechanicChecks.Integer(errors, backstabBonus, payload.AtkBonus, $"{path}.{backstabBonus.Name}"));
    }

    private static MechanicParameter NativeInteger(string name, int defaultValue, int? minimum = null) =>
        new(name, MechanicParameterKind.Integer)
        {
            Required = false,
            Minimum = minimum,
            DefaultValue = JsonSerializer.SerializeToElement(defaultValue)
        };

    // Preserve the existing native parameter domains at the scenario boundary.
    // In particular, Fury and Backstab ATK bonuses have no extra numeric bound.
    internal static bool HasValidSpecializedNativeMechanics(UnitType type) =>
        (type.Cleave is null || Validate(type.Cleave).IsEmpty) &&
        (type.Fury is null || Validate(type.Fury).IsEmpty) &&
        (type.Backstab is null || Validate(type.Backstab).IsEmpty);
}
