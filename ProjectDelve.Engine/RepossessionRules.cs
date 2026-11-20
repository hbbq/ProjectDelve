namespace ProjectDelve.Engine;

public static class RepossessionRules
{
    internal static void ValidateContinuation(GameState state, RepossessionContinuation attempt)
    {
        var actor = state.Units.SingleOrDefault(u => u.Id == attempt.UnitId && u.CurrentHp > 0);
        var target = state.Units.SingleOrDefault(u => u.Id == attempt.TargetId && u.CurrentHp > 0);
        var source = attempt.AbilitySource;
        if (actor is null || target is null || source is null || state.CurrentUnitId != actor.Id ||
            state.ActiveToken != actor.Token || state.ActionsUsedThisActivation < 1 ||
            state.AttackInProgress is not null || state.BreakInProgress is not null ||
            state.DoorInProgress is not null || state.MachineInteraction is not null ||
            state.Physical.Figures.SingleOrDefault(f => f.Id == actor.Id) is not { } figure || !state.Physical.Figures.Any(f => f.Id == target.Id) ||
            !FogOfWar.IsUnitObservable(state, actor.SideId, target.Id) ||
            AttackRules.EvaluateHostileTargetFrom(state, actor.Id, figure.Position, target.Id, attempt.TargetingRange) != UnitTargetEvaluation.Possible ||
            !EligibleEquipment(state, target.Id).Any() || !state.Storages.Any(s => s.StorageId == attempt.DestinationStorageId))
            throw new ArgumentException("Invalid pending repossession source, target or destination.");
        var ability = source.Kind == AbilitySourceKind.Item ? null :
            state.AbilitiesOf(actor.Id).SingleOrDefault(a => a.SourceReference == source);
        // A Single Use Item is in the discard pile during its paid invocation.
        if (source.Kind == AbilitySourceKind.Item && state.LootDeck?.DiscardPile.Any(o =>
                o.Id == source.InstanceId && state.ItemTypeId(o) == source.TypeId) == true &&
            state.Item(source.TypeId).LocalAbilities.SingleOrDefault(a => a.Key == source.Key) is { } itemAbility)
            ability = new(itemAbility, source.Kind, source.TypeId, source.InstanceId);
        if (ability?.Definition.Mechanic is not TryRepossessRandomEquipment mechanic || ability.Definition.Timing != AbilityTiming.Action ||
            mechanic.SuccessCount != attempt.SuccessCount || mechanic.DestinationStorageId != attempt.DestinationStorageId ||
            attempt.ActionId != ability.ActionId)
            throw new ArgumentException("Pending repossession must match its invoked typed Ability.");
    }
    internal static void ValidateStorageReferences(GameState state)
    {
        void Validate(IEnumerable<LocalAbilityDefinition> abilities)
        {
            foreach (var mechanic in abilities.Select(a => a.Mechanic).OfType<TryRepossessRandomEquipment>())
                if (!state.Storages.Any(storage => storage.StorageId == mechanic.DestinationStorageId))
                    throw new ArgumentException($"Unknown destination Storage ID '{mechanic.DestinationStorageId}'.");
        }
        foreach (var type in state.Types)
            for (var level = 1; level <= 1 + (type.AdditionalLevels.IsDefault ? 0 : type.AdditionalLevels.Length); level++)
                Validate(type.AtLevel(level).LocalAbilities);
        foreach (var type in state.EquipmentTypes)
            for (var level = 1; level <= 1 + (type.AdditionalLevels.IsDefault ? 0 : type.AdditionalLevels.Length); level++)
                Validate(type.AtLevel(level).LocalAbilities);
        foreach (var type in state.ItemTypes) Validate(type.LocalAbilities);
    }
    public static IEnumerable<OwnedObject> EligibleEquipment(GameState state, string targetId)
    {
        var owner = state.Units.Single(u => u.Id == targetId);
        return owner.Inventory.Where(obj => state.EquipmentTypeId(obj) is not null && StorageRules.CanRemove(state, owner, obj))
            .OrderBy(obj => obj.Id, StringComparer.Ordinal);
    }

    public static bool CanTarget(GameState state, string actorId, Cell from, string targetId)
    {
        var actor = state.Units.SingleOrDefault(u => u.Id == actorId);
        var target = state.Units.SingleOrDefault(u => u.Id == targetId);
        if (actor is not { CurrentHp: > 0 } || target is not { CurrentHp: > 0 } ||
            !state.IsUpright(actorId) || !state.Physical.Figures.Any(f => f.Id == targetId) ||
            !FogOfWar.IsUnitObservable(state, actor.SideId, targetId)) return false;

        state = HypotheticalPosition.WithUnitAt(state, actorId, from);
        return AttackRules.EvaluateHostileTargetFrom(state, actorId, from, targetId) == UnitTargetEvaluation.Possible &&
            EligibleEquipment(state, targetId).Any();
    }
}

public static partial class GameEngine
{
    private static void ResolveRepossession(GameState state, int roll, ResolutionEvents events)
    {
        var attempt = state.RepossessionInProgress!;
        state.RepossessionInProgress = null;
        var success = roll <= attempt.SuccessCount;
        if (!success) state.RepossessionFailedThisActivation = true;
        var sourceCell = state.Physical.Figures.Single(f => f.Id == attempt.UnitId).Position;
        OwnedObject? selected = null;
        if (success)
        {
            var eligible = RepossessionRules.EligibleEquipment(state, attempt.TargetId).ToArray();
            selected = eligible[events.Random.LootDrawIndex(eligible.Length)];
            StorageRules.Deposit(state, state.Units.Single(u => u.Id == attempt.TargetId), selected.Id, attempt.DestinationStorageId);
        }
        var departure = success ? RemoveAfterRepossession(state, attempt) : null;
        events.Add(new RulesEvent("EquipmentRepossessionAttemptResolved", attempt.UnitId, attempt.TargetId,
            DieRoll: roll, SuccessCount: attempt.SuccessCount, Succeeded: success)
        {
            SourceUnitId = attempt.UnitId, ActionId = attempt.ActionId, AbilitySource = attempt.AbilitySource,
            ObjectCard = selected, StorageId = success ? attempt.DestinationStorageId : null, Cell = sourceCell,
            RepossessedEquipmentId = selected?.Id,
            RepossessedEquipmentName = selected is null ? null : state.ObjectContent(selected).Name
        });
        if (departure is not null) events.Add(departure);
    }

    private static RulesEvent RemoveAfterRepossession(GameState state, RepossessionContinuation attempt)
    {
        // The departure is its own lifecycle outcome: no defeat bookkeeping,
        // defeat replacement, defeat consequence or random drop is involved.
        var index = state.Units.FindIndex(u => u.Id == attempt.UnitId);
        var unit = state.Units[index];
        var figure = state.Physical.Figures.Single(f => f.Id == unit.Id);
        RemoveFigureAndDropInventory(state, index, unit, figure);
        return new RulesEvent("UnitRemoved", unit.Id)
        {
            SourceUnitId = unit.Id, ActionId = attempt.ActionId, AbilitySource = attempt.AbilitySource,
            Cell = figure.Position
        };
    }
}
