using System.Collections.Immutable;
using System.Text.Json;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    // Concrete mechanics use the same local Ability envelope. Only handlers
    // with source-bound invocation advertise Equipment, Items or multiplicity.
    private static partial IEnumerable<MechanicRegistration> SpecializedRegistrations()
    {
        yield return SpecializedNumbers<Cleave>("cleave", "Cleave", MechanicTiming.AfterAttack,
            "After this Unit's Attack, if it dealt {triggerDamage} or more damage to a Unit, you may immediately deal {damage} damage to an adjacent enemy.",
            MechanicUsageSupport.LimitedOrUnlimited, MechanicTargetSelection.Unit,
            "One living adjacent hostile Unit, Upright or Lying, using ordinary adjacency and LOS.",
            (NativeInteger("triggerDamage", 2, 1), p => p.TriggerDamage), (NativeInteger("damage", 1, 1), p => p.Damage));
        yield return ReusableActive(SpecializedMarker<Telekinesis>("telekinesis", "Telekinesis", MechanicTiming.Action,
            "Lay down an upright enemy within RNG and LOS.", MechanicTargetSelection.Unit));
        yield return ReusablePassive(SpecializedNumbers<Fury>("fury", "Fury", MechanicTiming.Passive,
            "ATK {atkBonus} while adjacent to {adjacentEnemyThreshold} or more enemies", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("adjacentEnemyThreshold", 2, 1), p => p.AdjacentEnemyThreshold),
            (NativeInteger("atkBonus", 1), p => p.AtkBonus)));
        yield return ReusablePassive(SpecializedNumbers<Backstab>("backstab", "Backstab", MechanicTiming.Passive,
            "{atkBonus} ATK when attacking an enemy adjacent to another friendly Unit", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("atkBonus", 1), p => p.AtkBonus)));
        yield return SpecializedNumbers<MoveAfterAttack>("moveAfterAttack", "Move After Attack", MechanicTiming.AfterAttack,
            "After an Attack, immediately Move up to {maxSteps} steps.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Cell, "A legal own-Move destination within the authored step allowance, including Stay.",
            (RequiredInteger("maxSteps", 0), p => p.MaxSteps));
        yield return SpecializedMarker<AdvanceOnKill>("advanceOnKill", "Advance on Kill", MechanicTiming.AfterAttack,
            "After this Unit's normal melee Attack fully resolves, if it defeated a hostile target, immediately Move to that target's former adjacent Cell or Stay. Use one cardinal step for an orthogonal Cell or two through either intermediate Cell for a diagonal Cell. Respect physical passage; ignore ordinary MOV and Movement Pattern. Normal arrival effects apply.",
            MechanicTargetSelection.Cell);
        yield return SpecializedNumbers<Explosion>("explosion", "Explosion", MechanicTiming.WhenDefeated,
            "When this Unit is defeated, deal {damage} damage to every adjacent Unit.", MechanicUsageSupport.UnlimitedOnly,
            (NativeInteger("damage", 1), p => p.Damage));
        yield return SpecializedMarker<Undying>("undying", "Undying", MechanicTiming.InsteadOfDefeat,
            "If this Upright Unit would be defeated,\ninstead set its current HP to 1 and lay it down.");
        yield return SpecializedNumbers<RunRails>("runRails", "Run Rails", MechanicTiming.InsteadOfActivation,
            "When this Unit activates Upright, follow the outgoing Rail directions for up to {steps} separately resolved steps. Stop at a blocked step. Swap with and lay down an Upright 1 x 1 Unit; defeat a Lying 1 x 1 Unit before entering its Cell. Larger Units block the step. Resources stay in place.", MechanicUsageSupport.UnlimitedOnly,
            (RequiredInteger("steps", 1), p => p.Steps));
        yield return ReusableActive(SpecializedNumbers<TryOpenDoor>("tryOpenDoor", "Bordering Door Open Attempt", MechanicTiming.Action,
            "Try to open a bordering Closed Door. Roll a D6: success on {successCount} of 6 faces.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Edge, "One bordering Closed Door.",
            (RequiredInteger("successCount", 0, 6), p => p.SuccessCount)));
        yield return ReusableActive(SpecializedMarker<FireBreath>("fireBreath", "In-Range Enemy Units Attack", MechanicTiming.Action,
            "Attack all enemies within RNG."));
        yield return ReusableActive(SpecializedNumbers<ClawAttack>("clawAttack", "Claw Attack", MechanicTiming.Action,
            "Attack an adjacent enemy with ATK {atkBonus}.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "One adjacent living hostile Unit using ordinary adjacency and LOS.",
            (NativeInteger("atkBonus", 1), p => p.AtkBonus)));
        yield return ReusableActive(SpecializedMarker<ReshapeRoom>("reshapeRoom", "Reshape Room", MechanicTiming.BonusAction,
            "Replace Edges in the 3 × 3 area centered on this Unit to form a room with an opening at the middle of each of its four sides."));
        var swapSelector = new MechanicParameter("selector", MechanicParameterKind.Enum)
        {
            Required = false, AllowedValues = ["Friendlies", "Enemies"],
            DefaultValue = JsonSerializer.SerializeToElement("Enemies")
        };
        yield return ReusableActive(MechanicRegistration.Create<Swap>(SpecializedDescriptor(
            "swap", "Adjacent Unit Position Swap", MechanicTiming.BonusAction,
            "Swap places with an adjacent {selector} Unit.") with
        {
            Parameters = [swapSelector],
            Targeting = new(MechanicTargetSelection.Unit,
                "One living adjacent Unit matching the relationship selector and eligible for external repositioning; both receiving placements must be legal."),
            Description = new("Swap places with an adjacent {selector} Unit.", ["selector"])
        }, (payload, path, errors) => MechanicChecks.Enum(errors, swapSelector, payload.Selector, path + ".selector")),
            MechanicTiming.Action, MechanicTiming.BonusAction);
        yield return ReusableActive(SpecializedNumbers<Displace>("displace", "Displace", MechanicTiming.BonusAction,
            "Move an adjacent enemy Unit up to {maxMove} Cell.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "An adjacent hostile one-Cell Unit and one legal external movement destination, including its current Cell.",
            (RequiredInteger("maxMove", 0, 1), p => p.MaxMove)));
        yield return ReusableActive(SpecializedMarker<PlaceBananaPeel>("placeBananaPeel", "Adjacent Banana Peel Placement", MechanicTiming.BonusAction,
            "Place a Banana Peel in an adjacent passable Cell with no Unit, Machine or Cell Marker. Loose Resources may remain there.", MechanicTargetSelection.Cell));
        yield return ReusableActive(SpecializedNumbers<SelfExtraMovesMechanic>("selfExtraMoves", "Self Extra Moves", MechanicTiming.BonusAction,
            "Gain {amount} additional Move opportunities this activation. Use before any normal Action, even after an earlier Move.", MechanicUsageSupport.UnlimitedOnly,
            (RequiredInteger("amount", 1), p => p.Amount)));
        yield return ReusableActive(SpecializedNumbers<TargetUnitAttackMechanic>("targetUnitAttack", "Target Unit Attack", MechanicTiming.Action,
            "Attack an enemy with RNG {rng} and ATK {atk}.", MechanicUsageSupport.UnlimitedOnly,
            MechanicTargetSelection.Unit, "One living hostile Unit within the authored range and normal LOS.",
            (RequiredInteger("rng", 1), p => p.Rng), (RequiredInteger("atk", 1), p => p.Atk)));
        var movementKinds = new MechanicParameter("kinds", MechanicParameterKind.List)
        { MinimumItems = 1, Item = new("kind", MechanicParameterKind.String) };
        yield return PersistentCapability(MechanicRegistration.Create<MovementCapabilityGrant>(SpecializedDescriptor(
            "movementCapabilityGrant", "Movement Capability Grant", MechanicTiming.Passive,
            "Traverse and occupy Terrain permitting any of: {kinds}. This capability persists while Lying.") with
        {
            Parameters = [movementKinds],
            Description = new("Traverse and occupy Terrain permitting any of: {kinds}. This capability persists while Lying.", ["kinds"])
        }, (payload, path, errors) =>
        {
            if (!MechanicChecks.List(errors, movementKinds, payload.Kinds.IsDefault,
                payload.Kinds.IsDefault ? 0 : payload.Kinds.Length, path + ".kinds")) return;
            if (!MovementKinds.Valid(payload.Kinds))
                MechanicChecks.Error(errors, path + ".kinds", "invalidMovementKinds", "Movement kinds must be normalized, valid and distinct.");
        }), "Persistent movement capability, including Equipment grants while Lying; does not bypass Edges or other obstacles.");
        yield return PersistentCapability(SpecializedMarker<Amphibious>("amphibious", "Amphibious", MechanicTiming.Passive,
            "Traverse and occupy Water, including while Lying.", equipment: true),
            "Persistent Water occupancy permission, independent of Posture; does not bypass Edges or other obstacles.");
        yield return PersistentCapability(SpecializedMarker<Phase>("phase", "Phase", MechanicTiming.Passive,
            "Move through all terrain and Edges, including Walls and Closed Doors, without opening them.\nEnd on an unoccupied Cell this Unit can normally occupy. This capability persists while Lying; it does not grant a Move or Action.",
            equipment: true), "Requires the bearer's established Phase movement and vision context, independent of Posture. Multiple declarations grant the same capability once; no Item Passive or finite-use lifecycle is defined.");
        yield return ReusablePassive(SpecializedMarker<TransportResource>("transportResource", "Transport Resource", MechanicTiming.Passive,
            "During this Unit's own Move, the Resource on its starting Cell accompanies it to its final destination if that Cell has no Resource. Stay and external repositioning do not transport Resources."),
            "Requires an Upright one-Cell bearer and its own Move. Multiple declarations grant the same transport capability once; no Item Passive or finite-use lifecycle is defined.");
        yield return ReusableActive(SpecializedMarker<PickupResource>("pickupResource", "Pick Up Resource", MechanicTiming.Action,
            "Take one Resource from this Unit's Cell into its shared Side + Unit Type storage, if that storage accepts its Type and has room."));
        yield return ReusableActive(SpecializedMarker<GiveResource>("giveResource", "Give Resource", MechanicTiming.Action,
            "Give one Resource from this Unit's Cell to an adjacent friendly Unit's shared storage or an adjacent Machine's own storage, if it accepts its Type and has room.",
            MechanicTargetSelection.UnitOrMachine,
            recipients: "An adjacent friendly Unit or adjacent Machine that is public or owned by this Unit's Side. Its storage must accept the Resource on this Unit's Cell and have room."));
        yield return ReusableActive(SpecializedMarker<Operate>("operate", "Operate", MechanicTiming.Action,
            "Operate an adjacent public Machine or one owned by this Unit's Side, with no impassable Edge between them. Meet its input requirements and use limits, then resolve its declared effect. Resource output requires this Unit's Cell to contain no Resource; Reveal requires new discovery.",
            MechanicTargetSelection.Machine,
            recipients: "An adjacent Machine that is public or owned by this Unit's Side, satisfying its inputs, use limits and effect conditions, with no impassable Edge between them. Only Resource output requires an empty Resource destination; Reveal requires new discovery."));
        yield return ReusableActive(SpecializedMarker<OpenDoor>("openDoor", "Bordering Door Open", MechanicTiming.FreeAction,
            "Open a bordering Closed Door.", MechanicTargetSelection.Edge), MechanicTiming.Action, MechanicTiming.FreeAction);

        var target = new MechanicParameter("unitTypeId", MechanicParameterKind.Reference) { ReferenceCatalog = "units" };
        var posture = new MechanicParameter("initialPosture", MechanicParameterKind.Enum) { AllowedValues = ["Upright", "Lying"] };
        var sideLimit = new MechanicParameter("sameTypeSideLimit", MechanicParameterKind.Integer) { Required = false, AllowNull = true, Minimum = 1 };
        var summonerLimit = new MechanicParameter("sameTypeSummonerLimit", MechanicParameterKind.Integer) { Required = false, AllowNull = true, Minimum = 1 };
        yield return ReusableActive(MechanicRegistration.Create<AdjacentUnitSummonMechanic>(SpecializedDescriptor("adjacentUnitSummon", "Adjacent Unit Summon", MechanicTiming.Action,
            "Place one {initialPosture} {unitTypeId} in an adjacent empty Cell.") with
        {
            MaxPerSource = null,
            Parameters = [target, posture, sideLimit, summonerLimit], Targeting = new(MechanicTargetSelection.Cell, "A legal adjacent footprint with LOS, subject to uniqueness and both optional living same-Type limits: on the acting Side and created by the acting Unit."),
            ContextRequirements = "Requires an Upright acting Unit with an Action available. Resolve the summoned Unit Type from the effective scenario catalog, then require a legal empty adjacent footprint with LOS and respect that Unit Type's uniqueness. Equipment and Items use their bearer's context.",
            Description = new("Place one {initialPosture} {unitTypeId} in an adjacent empty Cell on this Unit's Side.{sameTypeSideLimit}{sameTypeSummonerLimit}", ["initialPosture", "unitTypeId", "sameTypeSideLimit", "sameTypeSummonerLimit"])
        }, (payload, path, errors) =>
        {
            MechanicChecks.Reference(errors, target, payload.UnitTypeId, path + ".unitTypeId");
            MechanicChecks.Enum(errors, posture, payload.InitialPosture, path + ".initialPosture");
            if (payload.SameTypeSideLimit is { } limit) MechanicChecks.Integer(errors, sideLimit, limit, path + ".sameTypeSideLimit");
            if (payload.SameTypeSummonerLimit is { } creatorLimit) MechanicChecks.Integer(errors, summonerLimit, creatorLimit, path + ".sameTypeSummonerLimit");
        }));
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

    private static MechanicRegistration ReusableActive(MechanicRegistration registration, params MechanicTiming[] timings)
    {
        var supportedTimings = timings.Length == 0 ? registration.Descriptor.Profiles.Select(p => p.Timing).Distinct() : timings;
        return registration with { Descriptor = registration.Descriptor with
        {
            Profiles = supportedTimings.SelectMany(timing => new[]
            {
                new MechanicSupportProfile(MechanicSource.Unit, timing, MechanicUsageSupport.LimitedOrUnlimited),
                new MechanicSupportProfile(MechanicSource.Equipment, timing, MechanicUsageSupport.LimitedOrUnlimited),
                new MechanicSupportProfile(MechanicSource.Item, timing, MechanicUsageSupport.UnlimitedOnly)
            }).ToImmutableArray(),
            MaxPerSource = null
        } };
    }

    private static MechanicRegistration PersistentCapability(MechanicRegistration registration, string requirements)
    {
        var reusable = ReusablePassive(registration, requirements);
        return reusable with { Descriptor = reusable.Descriptor with
        { Dependencies = ["bearingUnit", "persistentCapability"] } };
    }

    private static MechanicRegistration ReusablePassive(MechanicRegistration registration, string? requirements = null) => registration with
    { Descriptor = registration.Descriptor with
        {
            Profiles = [new(MechanicSource.Unit, MechanicTiming.Passive, MechanicUsageSupport.UnlimitedOnly),
                new(MechanicSource.Equipment, MechanicTiming.Passive, MechanicUsageSupport.UnlimitedOnly)],
            MaxPerSource = null,
            ContextRequirements = requirements ?? registration.Descriptor.ContextRequirements
        }
    };

    private static MechanicDescriptor SpecializedDescriptor(string id, string name, MechanicTiming timing, string description,
        MechanicUsageSupport usage = MechanicUsageSupport.UnlimitedOnly) => new(id, name, MechanicFamily.Ability)
    {
        Profiles = [new(MechanicSource.Unit, timing, timing is MechanicTiming.Action or MechanicTiming.BonusAction
            ? MechanicUsageSupport.LimitedOrUnlimited : usage)],
        Context = timing == MechanicTiming.Passive ? MechanicContext.BearingUnit : MechanicContext.ActingUnit,
        ContextRequirements = timing switch
        {
            MechanicTiming.AfterAttack => "Requires a qualifying completed Attack and its immediate follow-up continuation.",
            MechanicTiming.WhenDefeated => "Requires an actual defeat snapshot and the automatic consequence queue.",
            MechanicTiming.InsteadOfDefeat => "Requires a pending defeat to replace and the source's stated Posture.",
            MechanicTiming.InsteadOfActivation => "Requires the Unit's activation to replace.",
            MechanicTiming.EndActivation => "Requires the ending activation and its own-Move waypoint progress.",
            MechanicTiming.Passive => "Requires a bearing Unit and an ongoing Passive; limited use and Item Passive lifecycles are not defined.",
            _ => "Requires an Upright acting Unit, the declared timing opportunity and the mechanic's legal target or effect conditions. Equipment and Items use their bearer's context; ownership and remaining uses are checked for the selected source."
        },
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
        MechanicTargetSelection selection = MechanicTargetSelection.None, bool equipment = false, string? recipients = null) where T : AbilityMechanic =>
        MechanicRegistration.Create<T>(SpecializedDescriptor(id, name, timing, description) with
            { Targeting = new(selection, recipients ?? description), Profiles = equipment
                ? [new(MechanicSource.Unit, timing, MechanicUsageSupport.UnlimitedOnly), new(MechanicSource.Equipment, timing, MechanicUsageSupport.UnlimitedOnly)]
                : [new(MechanicSource.Unit, timing, timing is MechanicTiming.Action or MechanicTiming.BonusAction
                    ? MechanicUsageSupport.LimitedOrUnlimited : MechanicUsageSupport.UnlimitedOnly)] }, (_, _, _) => { });
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
