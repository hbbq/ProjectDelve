using System.Globalization;

namespace ProjectDelve.Engine;

public sealed record CardEntryDescription(string Id, string Name, string Category,
    string Description, int? MaxUses = null, string? ShortDescription = null)
{
    public string? UseLimitText => MaxUses is { } max ? $"{max}/game" : null;
}

// Concrete formatting reads the same typed payloads as execution. Names, timing
// and usage belong exclusively to the owner-local declaration.
public static class ContentDescriptions
{
    public static CardEntryDescription AbilityEntry(LocalAbilityDefinition ability, string entryId,
        IReadOnlyList<UnitType>? existingTypes = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Number(string key, int value) => values[key] = value.ToString(CultureInfo.InvariantCulture);
        void Signed(string key, int value) => values[key] = value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        switch (ability.Mechanic)
        {
            case Swap swap: values["selector"] = Relationship(swap.Selector); break;
            case AdjacentUnitHealMechanic heal:
                values["selector"] = Relationship(heal.Selector); Number("amount", heal.Amount); break;
            case AdjacentUnitPostureChangeMechanic posture:
                values["selector"] = Relationship(posture.Selector); values["newPosture"] = PostureOperation(posture.NewPosture); break;
            case AdjacentUnitsPostureChangeMechanic posture:
                values["selector"] = Relationship(posture.Selector); values["newPosture"] = PostureOperation(posture.NewPosture); break;
            case RevealDistance reveal: Number("distance", reveal.Distance); break;
            case ExtraActionsMechanic extra: Number("amount", extra.Amount); break;
            case SelfExtraMovesMechanic extra: Number("amount", extra.Amount); break;
            case SelfModifiersMechanic self: values["modifiers"] = Modifiers(self.Modifiers); break;
            case AdjacentUnitsStatModifiersMechanic adjacent:
                values["selector"] = Relationship(adjacent.Selector); values["modifiers"] = Modifiers(adjacent.Modifiers); break;
            case Cleave cleave: Number("triggerDamage", cleave.TriggerDamage); Number("damage", cleave.Damage); break;
            case Fury fury: Signed("atkBonus", fury.AtkBonus); Number("adjacentEnemyThreshold", fury.AdjacentEnemyThreshold); break;
            case Backstab backstab: Signed("atkBonus", backstab.AtkBonus); break;
            case ClawAttack claw: Signed("atkBonus", claw.AtkBonus); break;
            case Explosion explosion: Number("damage", explosion.Damage); break;
            case MoveAfterAttack move: Number("maxSteps", move.MaxSteps); break;
            case RunRails rails: Number("steps", rails.Steps); break;
            case TryOpenDoor door: Number("successCount", door.SuccessCount); break;
            case BreakObject broken: Number("successThreshold", broken.SuccessThreshold); break;
            case BreakAdjacentObjects broken: Number("successThreshold", broken.SuccessThreshold); break;
            case Displace displace: Number("maxMove", displace.MaxMove); break;
            case AdjacentUnitSummonMechanic summon:
                values["unitTypeId"] = UnitContent.Find(summon.UnitTypeId, existingTypes)?.DisplayName ?? summon.UnitTypeId;
                values["initialPosture"] = summon.InitialPosture.ToString();
                values["sameTypeSideLimit"] = summon.SameTypeSideLimit is { } limit
                    ? $" Only while fewer than {limit.ToString(CultureInfo.InvariantCulture)} living Units of that Type are on this Unit's Side." : "";
                values["sameTypeSummonerLimit"] = summon.SameTypeSummonerLimit is { } creatorLimit
                    ? $" Only while fewer than {creatorLimit.ToString(CultureInfo.InvariantCulture)} living Units of that Type summoned by this Unit are in play." : "";
                break;
            case TargetUnitAttackMechanic attack: Number("rng", attack.Rng); Number("atk", attack.Atk); break;
            case WaypointSlotMachine slot:
                values["outputTable"] = string.Join(", ", slot.OutputTable.Faces.Select((face, index) => $"{index + 1} {face.Name}"));
                break;
        }
        var description = ability.DescriptionOverride ?? MechanicCatalog.AbilityDescriptionTemplate(ability);
        foreach (var (key, value) in values) description = description.Replace("{" + key + "}", value, StringComparison.Ordinal);
        if (!ability.Costs.IsDefaultOrEmpty)
            description = string.Join(" ", ability.Costs.Select(CostCatalog.Describe)) + " " + description;
        var category = ability.Timing switch
        {
            AbilityTiming.BonusAction => "Bonus Action", AbilityTiming.FreeAction => "Free Action",
            AbilityTiming.AfterAttack => "Follow-up",
            AbilityTiming.WhenDefeated or AbilityTiming.InsteadOfDefeat => "Capability",
            AbilityTiming.InsteadOfActivation or AbilityTiming.EndActivation => "Automatic",
            _ => ability.Timing.ToString()
        };
        return new(entryId, ability.Name, category, description, ability.Usage.MaxUses);
    }

    private static string Relationship(RelationshipSelector selector) => selector switch
        { RelationshipSelector.Friendlies => "friendly", RelationshipSelector.Enemies => "enemy", RelationshipSelector.All => "other", _ => throw new ArgumentException("Unknown relationship selector.") };
    private static string PostureOperation(Posture posture) => posture == Posture.Upright ? "Stand up" : "Lay down";

    private static string Modifiers(IEnumerable<ModifierThisTurn> modifiers) =>
        string.Join("; ", modifiers.Select(m => $"{(m.Stat == Stat.SightDistance ? "Sight Distance" : m.Stat.ToString().ToUpperInvariant())} {m.Amount:+0;-0;0}"));

    public static IReadOnlyList<CardEntryDescription> CardEntries(this UnitType type, IReadOnlyList<UnitType>? existingTypes = null)
    {
        List<CardEntryDescription> entries = [];
        if (type.Amphibious)
            entries.Add(new("amphibious", "Amphibious", "Capability",
                "May traverse and occupy Water as well as ordinarily legal terrain. Normal Edge, Machine, footprint and Unit occupancy restrictions apply."));
        if (type.MovementPattern == MovementPattern.StraightOrthogonalOrDiagonalCorridor)
            entries.Add(new("straight-orthogonal-or-diagonal-corridor", "Straight Orthogonal or Diagonal Corridor Movement", "Capability",
                "Each ordinary Move may Stay, travel straight in one cardinal direction for 1 MOV per Cell, or travel along one straight diagonal for 2 MOV per Cell. Each diagonal segment uses two legal orthogonal steps through either corner. Do not turn or mix modes. No passing through Banana Peels; only the final Cell resolves arrival effects.", ShortDescription: "Move straight or diagonally; 1 MOV per cardinal Cell, 2 per diagonal Cell."));
        if (type.MovementPattern == MovementPattern.Adjacent)
            entries.Add(new("adjacent", "Adjacent Movement", "Capability",
                "Each ordinary Move may Stay or finish on one of the eight surrounding Cells. A cardinal destination costs one legal orthogonal step; a diagonal destination costs two through either legal corner. Use at most effective MOV. No longer detours or passing through Banana Peels; only the final Cell resolves arrival effects.", ShortDescription: "Move to a neighboring Cell within MOV, or Stay."));
        if (type.MovementPattern == MovementPattern.DiagonalCorridor)
            entries.Add(new("diagonal-corridor", "Diagonal Corridor Movement", "Capability",
                "Each ordinary Move may Stay or travel along one straight diagonal. Each diagonal segment costs 2 MOV and uses two legal orthogonal steps through either corner. No stopping at corners or passing through Banana Peels. Only the final Cell resolves arrival effects.", ShortDescription: "Move along one straight diagonal for 2 MOV per Cell, or Stay."));
        if (type.MovementPattern == MovementPattern.LShape)
            entries.Add(new("l-shape", "L-shaped Movement", "Capability",
                "Each ordinary Move may Stay or leap two Cells along one axis and one along the other, for 3 MOV. Ignore intermediate Cells and Edges. The landing Cell must be legal; normal arrival effects apply. Extra MOV does not allow another leap within that Move.", ShortDescription: "Leap in an L shape for 3 MOV, or Stay."));
        if (type.MovementPattern == MovementPattern.StraightOrthogonal)
            entries.Add(new("straight-orthogonal", "Straight Orthogonal Movement", "Capability",
                "Each ordinary Move may Stay or follow one uninterrupted cardinal direction up to MOV. All ordinary passage restrictions apply.", ShortDescription: "Move straight in one cardinal direction up to MOV, or Stay."));
        if (type.TerrainRestrictedMovement is { } restriction)
            entries.Add(new("terrain-restricted-movement", "Terrain-restricted Movement", "Capability", $"Ordinary Moves may only enter {restriction.Terrain} Cells."));
        if (type.TerrainRestriction is { } categories)
            entries.Add(new("terrain-restricted-movement", "Terrain-restricted Movement", "Capability",
                $"Ordinary Moves may only enter Terrain in one of these categories: {string.Join(", ", categories.Categories)}."));
        if (type.ResourceStorage is { } storage)
            entries.Add(new("resource-storage", "Resource Storage", "Capability", "Shared by all Units of this Type on this Side: " +
                string.Join(", ", storage.Capacities.Select(c => $"{c.Type.Name} capacity {c.Capacity}")) + ".", ShortDescription: "Resource capacity shared by this Unit Type and Side."));
        if (type.HasAction(UnitAction.NormalAttack))
            entries.Add(new("attack", "Attack", "Action", "Attack an enemy within Range and Line of Sight.", ShortDescription: ""));
        foreach (var ability in type.LocalAbilities)
            entries.Add(AbilityEntry(ability, new ResolvedAbility(ability, AbilitySourceKind.Unit, type.Id, "content").EntryId, existingTypes));
        return entries;
    }

    public static string? EntryId(Candidate candidate) => candidate switch
    {
        { Ability: { } ability } => ability.EntryId,
        { ObjectKind: { } kind } => $"object:{kind}",
        { FreeAction: UnitFreeAction.OpenDoor } => "open-door",
        { Action: UnitAction.NormalAttack } => "attack",
        _ => null
    };

    public static AbilityUses? UsesFor(this UnitType type, Unit unit, string entryId)
    {
        var ability = AbilityQueries.NativeAbilities(type, unit.Id).SingleOrDefault(a => a.EntryId == entryId);
        if (ability?.Definition.Usage.MaxUses is not { } max) return null;
        return new(max, max - unit.AbilityUseCounts.GetValueOrDefault(ability.AccountingId));
    }
}
