namespace ProjectDelve.Engine;

// Player-facing content, suitable for a physical card as well as any digital client.
// These descriptions do not replace the concrete components that implement rules.
public sealed record CardEntryDescription(string Id, string Name, string Category,
    string Description, int? MaxUses = null)
{
    // All currently supported limited-use content has a per-game limit.
    // Keep its printed wording in the domain rather than inferring a period in a renderer.
    public string? UseLimitText => MaxUses is { } max ? $"{max}/game" : null;
}

public static class ContentDescriptions
{
    public static IReadOnlyList<CardEntryDescription> CardEntries(this UnitType type, IReadOnlyList<UnitType>? existingTypes = null)
    {
        List<CardEntryDescription> entries = [];
        var names = type.AbilityNames;
        if (type.MovementPattern == MovementPattern.StraightOrthogonalOrDiagonalCorridor)
            entries.Add(new("straight-orthogonal-or-diagonal-corridor", "Straight Orthogonal or Diagonal Corridor Movement", "Capability",
                "Each ordinary Move may Stay, travel straight in one cardinal direction for 1 MOV per Cell, or travel along one straight diagonal for 2 MOV per Cell. Each diagonal segment uses two legal orthogonal steps through either corner. Do not turn or mix modes. No passing through Banana Peels; only the final Cell resolves arrival effects."));
        if (type.MovementPattern == MovementPattern.Adjacent)
            entries.Add(new("adjacent", "Adjacent Movement", "Capability",
                "Each ordinary Move may Stay or finish on one of the eight surrounding Cells. A cardinal destination costs one legal orthogonal step; a diagonal destination costs two through either legal corner. Use at most effective MOV. No longer detours or passing through Banana Peels; only the final Cell resolves arrival effects."));
        if (type.MovementPattern == MovementPattern.DiagonalCorridor)
            entries.Add(new("diagonal-corridor", "Diagonal Corridor Movement", "Capability",
                "Each ordinary Move may Stay or travel along one straight diagonal. Each diagonal segment costs 2 MOV and uses two legal orthogonal steps through either corner. No stopping at corners or passing through Banana Peels. Only the final Cell resolves arrival effects."));
        if (type.MovementPattern == MovementPattern.LShape)
            entries.Add(new("l-shape", "L-shaped Movement", "Capability",
                "Each ordinary Move may Stay or leap two Cells along one axis and one along the other, for 3 MOV. Ignore intermediate Cells and Edges. The landing Cell must be legal; normal arrival effects apply. Extra MOV does not allow another leap within that Move."));
        if (type.MovementPattern == MovementPattern.StraightOrthogonal)
            entries.Add(new("straight-orthogonal", "Straight Orthogonal Movement", "Capability",
                "Each ordinary Move may Stay or follow one uninterrupted cardinal direction up to MOV. All ordinary passage restrictions apply."));
        if (type.AdvanceOnKill is not null)
            entries.Add(new("advance-on-kill", "Advance On Kill", "Follow-up",
                "After this Unit's normal melee Attack fully resolves, if it defeated a hostile target, immediately Move to that target's former adjacent Cell or Stay. Use one cardinal step for an orthogonal Cell or two through either intermediate Cell for a diagonal Cell. Respect physical passage; ignore ordinary MOV and Movement Pattern. Normal arrival effects apply."));
        if (type.TerrainRestrictedMovement is { } restriction)
            entries.Add(new("terrain-restricted-movement", "Terrain-restricted Movement", "Capability",
                $"Ordinary Moves may only enter {restriction.Terrain} Cells."));
        if (type.ShootShell is { } shell)
            entries.Add(new("shoot-shell", "Shoot Shell", "Action", $"Spend {shell.ShellCost} Shell to attack an enemy with RNG {shell.Rng} and ATK {shell.Atk}."));
        if (type.WaypointSlotMachine is not null)
            entries.Add(new("waypoint-slot-machine", "Waypoint Slot Machine", "Automatic", "At the end of this Unit's activation, if its own Move advanced its group's waypoint, roll once: d6 1–3 Banana, 4–5 Mushroom, 6 Shell. Add one to shared Resource Storage; discard if full."));
        if (type.RunRails is { } rails)
            entries.Add(new("run-rails", $"Run Rails ({rails.Steps})", "Automatic",
                $"When this Unit activates Upright, follow the outgoing Rail directions for up to {rails.Steps} separately resolved steps. Stop at a blocked step. Swap with and lay down an Upright 1 x 1 Unit; defeat a Lying 1 x 1 Unit before entering its Cell. Larger Units block the step. Resources stay in place."));
        if (type.Actions.HasFlag(UnitAction.NormalAttack))
            entries.Add(new("attack", "Attack", "Action", "Attack an enemy within Range and Line of Sight."));
        if (type.Actions.HasFlag(UnitAction.FireBreath))
            entries.Add(new("fire-breath", names.FireBreath ?? "Fire Breath", "Action", "Attack all enemies within RNG."));
        if (type.Actions.HasFlag(UnitAction.ClawAttack) && type.ClawAttack is { } claw)
            entries.Add(new("claw-attack", names.ClawAttack ?? "Claw Attack", "Action", $"Attack an adjacent enemy with ATK {claw.AtkBonus:+0;-0;0}."));
        if (type.Actions.HasFlag(UnitAction.Heal) && type.Heal is { } heal)
            entries.Add(new("heal", names.Heal ?? "Heal", "Action", $"Heal an adjacent damaged friendly Unit for {heal.Amount} HP.", heal.MaxUses));
        if (type.Actions.HasFlag(UnitAction.HolyWave) && type.HolyWave is { } wave)
            entries.Add(new("holy-wave", names.HolyWave ?? "Holy Wave", "Action", "Lay down all adjacent upright enemies.\nThen lay down this Unit.", wave.MaxUses));
        if (type.Actions.HasFlag(UnitAction.Fireball) && type.Fireball is { } fireball)
            entries.Add(new("fireball", names.Fireball ?? "Fireball", "Action",
                "Choose a Cell within RNG and LOS.\nAttack all Units on or adjacent to that Cell.", fireball.MaxUses));
        if (type.Actions.HasFlag(UnitAction.Telekinesis))
            entries.Add(new("telekinesis", names.Telekinesis ?? "Telekinesis", "Action", "Lay down an upright enemy within RNG and LOS."));
        if (type.Actions.HasFlag(UnitAction.SummonAdjacent) && type.SummonAdjacent is { } summon)
        {
            var summonedName = UnitContent.Find(summon.UnitTypeId, existingTypes)?.DisplayName ?? summon.UnitTypeId;
            // Retain the existing opaque card/candidate id; rules dispatch uses the typed Action.
            entries.Add(new("spawn-goblin", names.Summon ?? "Summon Adjacent", "Action",
                $"Place one {summon.InitialPosture} {summonedName} in an adjacent empty Cell."));
        }
        if (type.Actions.HasFlag(UnitAction.SummonWorker) && type.SummonWorker is { } worker)
            entries.Add(new("summon-worker", "Summon Worker", "Action",
                $"If fewer than {worker.SideLimit} living Workers are on this Unit's Side, place one Upright Worker in an adjacent empty Cell on this Unit's Side."));
        if (type.Actions.HasFlag(UnitAction.SummonTroll) && type.SummonTroll is { } troll)
            entries.Add(new("summon-troll", "Summon Troll", "Action",
                $"Spend {troll.DiamondCost} Diamond from this Unit's shared Resource Storage and place one Upright Troll in an adjacent empty Cell on this Unit's Side."));
        if (type.Actions.HasFlag(UnitAction.PickupResource))
            entries.Add(new("pickup-resource", "Pickup Resource", "Action", "Take one Resource from this Unit's Cell into its shared Side + Unit Type storage, if that storage accepts its Type and has room."));
        if (type.Actions.HasFlag(UnitAction.GiveResource))
            entries.Add(new("give-resource", "Give Resource", "Action", "Give one Resource from this Unit's Cell to an adjacent friendly Unit's shared storage or an adjacent Machine's own storage, if it accepts its Type and has room."));
        if (type.Actions.HasFlag(UnitAction.Operate))
            entries.Add(new("operate", "Operate", "Action", "Operate an adjacent Machine with sufficient stored Resources and no impassable Edge separating it from this Unit. Spend its inputs and place its output on this Unit's Cell together, provided that Cell contains no Resource."));
        if (type.ResourceStorage is { } storage)
            entries.Add(new("resource-storage", "Resource Storage", "Capability", "Shared by all Units of this Type on this Side: " +
                string.Join(", ", storage.Capacities.Select(c => $"{c.Type.Name} capacity {c.Capacity}")) + "."));
        if (type.TryOpenDoor is { } attempt)
            entries.Add(new("try-open-door", names.TryOpenDoor ?? "Try Open Door", "Action",
                $"Try to open a bordering Closed Door. Roll a D6: success on {attempt.SuccessCount} of 6 faces."));
        if (type.FreeActions.HasFlag(UnitFreeAction.OpenDoor))
            entries.Add(new("open-door", "Open Door", "Free Action", "Open a bordering Closed Door."));
        foreach (var ability in type.BonusActions)
            entries.Add(new(BonusEntryId(ability.Name), ability.DisplayName ?? ability.Name, "Bonus Action",
                ability.PlaceBananaPeel is { } banana ? $"Spend {banana.BananaCost} Banana from shared Resource Storage to place a Banana Peel in an adjacent passable Cell with no Unit, Machine or Cell Marker. Loose Resources may remain there." :
                ability.Boost is { } boost ? $"Spend {boost.MushroomCost} Mushroom to gain one additional Move this activation. Use before any normal Action, even after an earlier Move." :
                ability.ReshapeRoom is not null ? "Replace Edges in the 3 \u00D7 3 area centered on this Unit to form a room with an opening at the middle of each of its four sides." :
                ability.Swap is not null ? "Swap places with an adjacent enemy Unit." :
                ability.Displace is { } displace ? $"Move an adjacent enemy Unit up to {displace.MaxMove} Cell." :
                string.Join(" & ", ability.Modifiers.Select(m =>
                    $"{(m.Amount >= 0 ? "+" : "")}{m.Amount} {m.Stat.ToString().ToUpperInvariant()}")) + " this turn",
                ability.MaxUses));
        if (type.AdjacentFriendlyUnitsDefenceBonus is { } aura)
            entries.Add(new($"passive:{aura.Name}", names.Aura ?? aura.Name, "Passive", $"Adjacent friendly Units get DEF +{aura.Amount}"));
        if (type.Fury is { } fury)
            entries.Add(new("passive:Fury", names.Fury ?? fury.Name, "Passive", fury.DisplayText));
        if (type.Backstab is { } backstab)
            entries.Add(new("passive:Backstab", names.Backstab ?? backstab.Name, "Passive", backstab.DisplayText));
        if (type.Explosion is { } explosion)
            entries.Add(new("explosion", names.Explosion ?? "Explosion", "Capability",
                $"When this Unit is defeated, deal {explosion.Damage} damage to every adjacent Unit."));
        if (type.Undying is not null)
            entries.Add(new("undying", names.Undying ?? "Undying", "Capability",
                "If this Upright Unit would be defeated,\ninstead set its current HP to 1 and lay it down."));
        if (type.TransportResource is not null)
            entries.Add(new("transport-resource", "Transport Resource", "Capability",
                "During this Unit?s own Move, the Resource on its starting Cell accompanies it to its final destination if that Cell has no Resource. Stay and external repositioning do not transport Resources."));
        if (type.Phase is not null)
            entries.Add(new("phase", "Phase", "Capability",
                "Move through all terrain and Edges, including Walls and Closed Doors, without opening them.\nEnd on an unoccupied, normally passable Cell."));
        if (type.Cleave is { } cleave)
            entries.Add(new("cleave", names.Cleave ?? "Cleave", "Follow-up",
                $"After this Unit's Attack, if it dealt {cleave.TriggerDamage} or more damage to a Unit, you may immediately deal {cleave.Damage} damage to an adjacent enemy.", cleave.MaxUses));
        if (type.MoveAfterAttack is { } move)
            entries.Add(new("move-after-attack", names.MoveAfterAttack ?? "Move After Attack", "Follow-up",
                $"After an Attack, immediately Move up to {move.MaxSteps} steps."));
        return entries;
    }

    public static string BonusEntryId(string name) => $"bonus:{name}";

    public static string? EntryId(Candidate candidate) => candidate switch
    {
        { BonusAction: { } ability } => BonusEntryId(ability.Name),
        { Kind: ActivationChoiceKind.Cleave } => "cleave",
        { TryOpenDoor: not null } => "try-open-door",
        { FreeAction: UnitFreeAction.OpenDoor } => "open-door",
        { Action: UnitAction.PickupResource } => "pickup-resource",
        { Action: UnitAction.GiveResource } => "give-resource",
        { Action: UnitAction.Operate } => "operate",
        { Action: UnitAction.NormalAttack } => "attack",
        { Action: UnitAction.FireBreath } => "fire-breath",
        { Action: UnitAction.ClawAttack } => "claw-attack",
        { Action: UnitAction.Heal } => "heal",
        { Action: UnitAction.HolyWave } => "holy-wave",
        { Action: UnitAction.Fireball } => "fireball",
        { Action: UnitAction.Telekinesis } => "telekinesis",
        { Action: UnitAction.SummonAdjacent } => "spawn-goblin",
        { Action: UnitAction.SummonWorker } => "summon-worker",
        { Action: UnitAction.SummonTroll } => "summon-troll",
        { Action: UnitAction.PlaceBananaPeel } => "place-banana-peel",
        { Action: UnitAction.ShootShell } => "shoot-shell",
        _ => null
    };

    // Read existing concrete counters without introducing another source of game state.
    public static AbilityUses? UsesFor(this UnitType type, Unit unit, string entryId) => entryId switch
    {
        "cleave" => unit.CleaveUses,
        "heal" => unit.HealUses,
        "holy-wave" => unit.HolyWaveUses,
        "fireball" => unit.FireballUses,
        _ => type.BonusActions.FirstOrDefault(a => BonusEntryId(a.Name) == entryId) is { } ability
            ? unit.BonusActionUses.GetValueOrDefault(ability.Name) : null
    };
}
