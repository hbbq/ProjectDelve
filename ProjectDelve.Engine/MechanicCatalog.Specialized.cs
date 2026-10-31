using System.Collections.Immutable;
using System.Text.Json;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    // Concrete native mechanics use the same local Ability envelope. Their
    // single-instance Unit profiles preserve established ordering and stacking.
    private static partial IEnumerable<MechanicRegistration> SpecializedRegistrations()
    {
        yield return SpecializedNumbers<Cleave>("cleave", "Cleave", MechanicTiming.AfterAttack,
            "After this Unit's Attack, if it dealt {triggerDamage} or more damage to a Unit, you may immediately deal {damage} damage to an adjacent enemy.",
            MechanicUsageSupport.LimitedOnly, MechanicTargetSelection.Unit,
            "One living adjacent hostile Unit, Upright or Lying, using ordinary adjacency and LOS.",
            (NativeInteger("triggerDamage", 2, 1), p => p.TriggerDamage), (NativeInteger("damage", 1, 1), p => p.Damage));
        yield return SpecializedMarker<Telekinesis>("telekinesis", "Telekinesis", MechanicTiming.Action,
            "Lay down an upright enemy within RNG and LOS.", MechanicTargetSelection.Unit);
        yield return SpecializedNumbers<Fury>("fury", "Fury", MechanicTiming.Passive,
            "ATK {atkBonus} while adjacent to {adjacentEnemyThreshold} or more enemies", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("adjacentEnemyThreshold", 2, 1), p => p.AdjacentEnemyThreshold),
            (NativeInteger("atkBonus", 1), p => p.AtkBonus));
        yield return SpecializedNumbers<Backstab>("backstab", "Backstab", MechanicTiming.Passive,
            "{atkBonus} ATK when attacking an enemy adjacent to another friendly Unit", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("atkBonus", 1), p => p.AtkBonus));
        yield return SpecializedNumbers<MoveAfterAttack>("moveAfterAttack", "Move After Attack", MechanicTiming.AfterAttack,
            "After an Attack, immediately Move up to {maxSteps} steps.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Cell, "A legal own-Move destination within the authored step allowance, including Stay.",
            (RequiredInteger("maxSteps", 0), p => p.MaxSteps));
        yield return SpecializedMarker<AdvanceOnKill>("advanceOnKill", "Advance on Kill", MechanicTiming.AfterAttack,
            "After this Unit's normal melee Attack fully resolves, if it defeated a hostile target, immediately Move to that target's former adjacent Cell or Stay. Use one cardinal step for an orthogonal Cell or two through either intermediate Cell for a diagonal Cell. Respect physical passage; ignore ordinary MOV and Movement Pattern. Normal arrival effects apply.");
        yield return SpecializedNumbers<Explosion>("explosion", "Explosion", MechanicTiming.WhenDefeated,
            "When this Unit is defeated, deal {damage} damage to every adjacent Unit.", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("damage", 1), p => p.Damage));
        yield return SpecializedMarker<Undying>("undying", "Undying", MechanicTiming.InsteadOfDefeat,
            "If this Upright Unit would be defeated,\ninstead set its current HP to 1 and lay it down.");
        yield return SpecializedNumbers<RunRails>("runRails", "Run Rails", MechanicTiming.InsteadOfActivation,
            "When this Unit activates Upright, follow the outgoing Rail directions for up to {steps} separately resolved steps. Stop at a blocked step. Swap with and lay down an Upright 1 x 1 Unit; defeat a Lying 1 x 1 Unit before entering its Cell. Larger Units block the step. Resources stay in place.", MechanicUsageSupport.UnlimitedOnly,
            (RequiredInteger("steps", 1), p => p.Steps));
        yield return SpecializedNumbers<TryOpenDoor>("tryOpenDoor", "Try Open Door", MechanicTiming.Action,
            "Try to open a bordering Closed Door. Roll a D6: success on {successCount} of 6 faces.", MechanicUsageSupport.UnlimitedOnly,
            (RequiredInteger("successCount", 0, 6), p => p.SuccessCount));
        yield return SpecializedMarker<FireBreath>("fireBreath", "Fire Breath", MechanicTiming.Action,
            "Attack all enemies within RNG.");
        yield return SpecializedNumbers<ClawAttack>("clawAttack", "Claw Attack", MechanicTiming.Action,
            "Attack an adjacent enemy with ATK {atkBonus}.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "One adjacent living hostile Unit using ordinary adjacency and LOS.",
            (NativeInteger("atkBonus", 1), p => p.AtkBonus));
        yield return SpecializedMarker<ReshapeRoom>("reshapeRoom", "Reshape Room", MechanicTiming.BonusAction,
            "Replace Edges in the 3 × 3 area centered on this Unit to form a room with an opening at the middle of each of its four sides.");
        yield return SpecializedMarker<Swap>("swap", "Swap", MechanicTiming.BonusAction,
            "Swap places with an adjacent enemy Unit.", MechanicTargetSelection.Unit);
        yield return SpecializedNumbers<Displace>("displace", "Displace", MechanicTiming.BonusAction,
            "Move an adjacent enemy Unit up to {maxMove} Cell.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "An adjacent hostile one-Cell Unit and one legal external movement destination, including its current Cell.",
            (RequiredInteger("maxMove", 0, 1), p => p.MaxMove));
        yield return SpecializedNumbers<SummonWorker>("summonWorker", "Summon Worker", MechanicTiming.Action,
            "If fewer than {sideLimit} living Workers are on this Unit's Side, place one Upright Worker in an adjacent empty Cell on this Unit's Side.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Cell, "A legal adjacent placement Cell with LOS, subject to the Side's living Worker limit.",
            (RequiredInteger("sideLimit", 1), p => p.SideLimit));
        yield return SpecializedNumbers<SummonTroll>("summonTroll", "Summon Troll", MechanicTiming.Action,
            "Spend {diamondCost} Diamond from this Unit's shared Resource Storage and place one Upright Troll in an adjacent empty Cell on this Unit's Side.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Cell, "A legal adjacent placement Cell with LOS and sufficient shared Diamond storage.",
            (RequiredInteger("diamondCost", 1), p => p.DiamondCost));
        yield return SpecializedNumbers<PlaceBananaPeel>("placeBananaPeel", "Place Banana Peel", MechanicTiming.BonusAction,
            "Spend {bananaCost} Banana from shared Resource Storage to place a Banana Peel in an adjacent passable Cell with no Unit, Machine or Cell Marker. Loose Resources may remain there.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Cell, "An adjacent passable Cell without a Unit, Machine, or Cell Marker; loose Resources do not block it.",
            (RequiredInteger("bananaCost", 1), p => p.BananaCost));
        yield return SpecializedNumbers<Boost>("boost", "Boost", MechanicTiming.BonusAction,
            "Spend {mushroomCost} Mushroom to gain one additional Move this activation. Use before any normal Action, even after an earlier Move.", MechanicUsageSupport.UnlimitedOnly,
            (RequiredInteger("mushroomCost", 1), p => p.MushroomCost));
        yield return SpecializedNumbers<ShootShell>("shootShell", "Shoot Shell", MechanicTiming.Action,
            "Spend {shellCost} Shell to attack an enemy with RNG {rng} and ATK {atk}.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "One living hostile Unit within the authored range and normal LOS, with sufficient shared Shell storage.",
            (RequiredInteger("shellCost", 1), p => p.ShellCost), (RequiredInteger("rng", 1), p => p.Rng),
            (RequiredInteger("atk", 1), p => p.Atk));
        yield return SpecializedMarker<Phase>("phase", "Phase", MechanicTiming.Passive,
            "Move through all terrain and Edges, including Walls and Closed Doors, without opening them.\nEnd on an unoccupied, normally passable Cell.",
            equipment: true);
        yield return SpecializedMarker<TransportResource>("transportResource", "Transport Resource", MechanicTiming.Passive,
            "During this Unit's own Move, the Resource on its starting Cell accompanies it to its final destination if that Cell has no Resource. Stay and external repositioning do not transport Resources.");
        yield return SpecializedMarker<PickupResource>("pickupResource", "Pick Up Resource", MechanicTiming.Action,
            "Take one Resource from this Unit's Cell into its shared Side + Unit Type storage, if that storage accepts its Type and has room.");
        yield return SpecializedMarker<GiveResource>("giveResource", "Give Resource", MechanicTiming.Action,
            "Give one Resource from this Unit's Cell to an adjacent friendly Unit's shared storage or an adjacent Machine's own storage, if it accepts its Type and has room.", MechanicTargetSelection.Unit);
        yield return SpecializedMarker<Operate>("operate", "Operate", MechanicTiming.Action,
            "Operate an adjacent Machine with sufficient stored Resources and no impassable Edge separating it from this Unit. Spend its inputs and place its output on this Unit's Cell together, provided that Cell contains no Resource.");
        yield return SpecializedMarker<OpenDoor>("openDoor", "Open Door", MechanicTiming.FreeAction,
            "Open a bordering Closed Door.", equipment: true);

        var target = new MechanicParameter("unitTypeId", MechanicParameterKind.Reference) { ReferenceCatalog = "units" };
        var posture = new MechanicParameter("initialPosture", MechanicParameterKind.Enum) { AllowedValues = ["Upright", "Lying"] };
        yield return MechanicRegistration.Create<SummonAdjacent>(SpecializedDescriptor("summonAdjacent", "Summon", MechanicTiming.Action,
            "Place one {initialPosture} {unitTypeId} in an adjacent empty Cell.") with
        {
            Parameters = [target, posture], Targeting = new(MechanicTargetSelection.Cell, "A legal adjacent footprint with LOS."),
            Description = new("Place one {initialPosture} {unitTypeId} in an adjacent empty Cell.", ["initialPosture", "unitTypeId"])
        }, (payload, path, errors) =>
        {
            MechanicChecks.Reference(errors, target, payload.UnitTypeId, path + ".unitTypeId");
            MechanicChecks.Enum(errors, posture, payload.InitialPosture, path + ".initialPosture");
        });
        var resourceId = new MechanicParameter("id", MechanicParameterKind.Reference) { ReferenceCatalog = "resources" };
        var resource = new MechanicParameter("resource", MechanicParameterKind.Object)
        {
            Members = [resourceId, new("name", MechanicParameterKind.String) { Required = false, AllowNull = true }]
        };
        var faces = new MechanicParameter("faces", MechanicParameterKind.List) { MinimumItems = 6, MaximumItems = 6, Item = resource };
        yield return MechanicRegistration.Create<WaypointSlotMachine>(SpecializedDescriptor("waypointSlotMachine", "Waypoint Slot Machine", MechanicTiming.EndActivation,
            "At the end of this Unit's activation, if its own Move advanced its group's waypoint, roll once: d6 {outputTable}. Add one to shared Resource Storage; discard if full.") with
        {
            Parameters = [new("outputTable", MechanicParameterKind.Object) { Members = [faces] }],
            Description = new("At the end of this Unit's activation, if its own Move advanced its group's waypoint, roll once: d6 {outputTable}. Add one to shared Resource Storage; discard if full.", ["outputTable"])
        }, (payload, path, errors) =>
        {
            if (payload.OutputTable is null) { MechanicChecks.Error(errors, path + ".outputTable", "required", "A Resource output table is required."); return; }
            var values = payload.OutputTable.Faces;
            if (!MechanicChecks.List(errors, faces, values.IsDefault, values.IsDefault ? 0 : values.Length, path + ".outputTable.faces")) return;
            for (var i = 0; i < values.Length; i++) MechanicChecks.Reference(errors, resourceId, values[i]?.Id, $"{path}.outputTable.faces[{i}].id");
        });
    }

    private static MechanicDescriptor SpecializedDescriptor(string id, string name, MechanicTiming timing, string description,
        MechanicUsageSupport usage = MechanicUsageSupport.UnlimitedOnly) => new(id, name, MechanicFamily.Ability)
    {
        Profiles = [new(MechanicSource.Unit, timing, usage)],
        Context = timing == MechanicTiming.Passive ? MechanicContext.BearingUnit : MechanicContext.ActingUnit,
        Dependencies = timing switch
        {
            MechanicTiming.Action => ["actingUnit", "uprightSource", "ordinaryActionOpportunity"],
            MechanicTiming.BonusAction => ["actingUnit", "uprightSource", "bonusActionAllowance"],
            MechanicTiming.FreeAction => ["actingUnit", "uprightSource", "activation"],
            MechanicTiming.Passive => ["bearingUnit", "uprightSource"],
            MechanicTiming.AfterAttack => ["completedAttack", "postAttackContinuation", "uprightSource"],
            MechanicTiming.WhenDefeated => ["defeatSnapshot", "ordinaryAdjacency", "consequenceQueue"],
            MechanicTiming.InsteadOfDefeat => ["immediateDefeatReplacement", "sourcePosture"],
            MechanicTiming.InsteadOfActivation => ["uprightSource", "activationSelection", "railDirections"],
            MechanicTiming.EndActivation => ["ownMoveWaypointFlag", "resourceStorage", "randomD6"],
            _ => []
        },
        Targeting = new(MechanicTargetSelection.None, description),
        Description = new(description, []), SerializedBinding = id, Grantable = true, MaxPerSource = 1
    };
    private static MechanicRegistration SpecializedMarker<T>(string id, string name, MechanicTiming timing, string description,
        MechanicTargetSelection selection = MechanicTargetSelection.None, bool equipment = false) where T : AbilityMechanic =>
        MechanicRegistration.Create<T>(SpecializedDescriptor(id, name, timing, description) with
            { Targeting = new(selection, description), Profiles = equipment
                ? [new(MechanicSource.Unit, timing, MechanicUsageSupport.UnlimitedOnly), new(MechanicSource.Equipment, timing, MechanicUsageSupport.UnlimitedOnly)]
                : [new(MechanicSource.Unit, timing, MechanicUsageSupport.UnlimitedOnly)] }, (_, _, _) => { });
    private static MechanicRegistration SpecializedNumbers<T>(string id, string name, MechanicTiming timing, string description,
        MechanicUsageSupport usage, params (MechanicParameter Parameter, Func<T, int> Value)[] parameters) where T : AbilityMechanic =>
        SpecializedNumbers(id, name, timing, description, usage, MechanicTargetSelection.None, description, parameters);
    private static MechanicRegistration SpecializedNumbers<T>(string id, string name, MechanicTiming timing, string description,
        MechanicUsageSupport usage, MechanicTargetSelection selection, string recipients,
        params (MechanicParameter Parameter, Func<T, int> Value)[] parameters) where T : AbilityMechanic =>
        MechanicRegistration.Create<T>(SpecializedDescriptor(id, name, timing, description, usage) with
        {
            Parameters = parameters.Select(p => p.Parameter).ToImmutableArray(),
            Targeting = new(selection, recipients),
            Description = new(description, parameters.Select(p => p.Parameter.Name).ToImmutableArray())
        }, (payload, path, errors) =>
        {
            foreach (var (parameter, value) in parameters)
                MechanicChecks.Integer(errors, parameter, value(payload), path + "." + parameter.Name);
        });
    private static MechanicParameter NativeInteger(string name, int defaultValue, int? minimum = null) =>
        new(name, MechanicParameterKind.Integer) { Required = false, Minimum = minimum, DefaultValue = JsonSerializer.SerializeToElement(defaultValue) };
    private static MechanicParameter RequiredInteger(string name, int? minimum = null, int? maximum = null) =>
        new(name, MechanicParameterKind.Integer) { Minimum = minimum, Maximum = maximum };
}
