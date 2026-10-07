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
        if (type.TryOpenDoor is { } attempt)
            entries.Add(new("try-open-door", names.TryOpenDoor ?? "Try Open Door", "Action",
                $"Try to open a bordering Closed Door. Roll a D6: success on {attempt.SuccessCount} of 6 faces."));
        if (type.FreeActions.HasFlag(UnitFreeAction.OpenDoor))
            entries.Add(new("open-door", "Open Door", "Free Action", "Open a bordering Closed Door."));
        foreach (var ability in type.BonusActions)
            entries.Add(new(BonusEntryId(ability.Name), ability.DisplayName ?? ability.Name, "Bonus Action",
                string.Join(" & ", ability.Modifiers.Select(m =>
                    $"{(m.Amount >= 0 ? "+" : "")}{m.Amount} {m.Stat.ToString().ToUpperInvariant()}")) + " this turn",
                ability.MaxUses));
        if (type.AdjacentFriendlyUnitsDefenceBonus is { } aura)
            entries.Add(new($"passive:{aura.Name}", names.Aura ?? aura.Name, "Passive", $"Adjacent friendly Units get DEF +{aura.Amount}"));
        if (type.Fury is { } fury)
            entries.Add(new("passive:Fury", names.Fury ?? fury.Name, "Passive", fury.DisplayText));
        if (type.Backstab is { } backstab)
            entries.Add(new("passive:Backstab", names.Backstab ?? backstab.Name, "Passive", backstab.DisplayText));
        if (type.Undying is not null)
            entries.Add(new("undying", names.Undying ?? "Undying", "Capability",
                "If this Upright Unit would be defeated,\ninstead set its current HP to 1 and lay it down."));
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
        { Action: UnitAction.NormalAttack } => "attack",
        { Action: UnitAction.FireBreath } => "fire-breath",
        { Action: UnitAction.ClawAttack } => "claw-attack",
        { Action: UnitAction.Heal } => "heal",
        { Action: UnitAction.HolyWave } => "holy-wave",
        { Action: UnitAction.Fireball } => "fireball",
        { Action: UnitAction.Telekinesis } => "telekinesis",
        { Action: UnitAction.SummonAdjacent } => "spawn-goblin",
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
