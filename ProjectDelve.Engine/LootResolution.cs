namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveLootDrop(GameState state, DefeatContext defeat, ResolutionEvents events)
    {
        var chance = defeat.Type.LootDropSuccesses;
        if (chance == 0 || (state.LootDeck?.DrawPile.Count ?? 0) <= state.LootMarkers.Count) return;
        var roll = events.Random.RollD6();
        var success = roll <= chance;
        LootMarker? marker = null;
        if (success)
        {
            marker = new($"drop:{defeat.Unit.Id}", defeat.Figure.Position);
            state.LootMarkers.Add(marker);
        }
        events.Add(new RulesEvent("LootDropRolled", defeat.Unit.Id, DieRoll: roll, SuccessCount: chance, Succeeded: success)
        { Cell = defeat.Figure.Position, LootMarker = marker, DefeatContext = defeat });
    }

    // Arrival hazards are resolved by callers before this queue is drained. It is
    // sequence-local: no ordinary decision can interrupt mandatory pickup.
    private static void ResolveLootArrivals(GameState state, ResolutionEvents events)
    {
        while (events.LootArrivals.Count > 0)
        {
            var unitId = events.LootArrivals[0];
            events.LootArrivals.RemoveAt(0);
            while (state.Physical.Figures.Any(f => f.Id == unitId) && state.Units.Any(u => u.Id == unitId && u.CurrentHp > 0))
            {
                var cells = FootprintGeometry.OccupiedCells(state, unitId);
                var marker = state.LootMarkers.Where(m => cells.Contains(m.Position))
                    .OrderBy(m => m.Position.Y).ThenBy(m => m.Position.X).FirstOrDefault();
                if (marker is null) break;
                var deck = state.LootDeck;
                if (deck is null || deck.DrawPile.Count < state.LootMarkers.Count || deck.DrawPile.Count == 0)
                    throw new InvalidOperationException("Loot supply invariant violated: an unopened marker has no available card.");
                var cardIndex = events.Random.LootDrawIndex(deck.DrawPile.Count);
                var card = deck.DrawPile[cardIndex];
                deck.DrawPile.RemoveAt(cardIndex);
                state.LootMarkers.Remove(marker);
                var index = state.Units.FindIndex(u => u.Id == unitId);
                var unit = state.Units[index];
                var category = state.ObjectContent(card).Category;
                if (category == LootCategory.Immediate) deck.DiscardPile.Add(card);
                else if (LootRules.CanOwn(state, unit, card))
                    state.Units[index] = unit with { Inventory = unit.Inventory.Add(card) };
                else state.GroundObjects.Add(new(card, state.Physical.Figures.Single(f => f.Id == unitId).Position));
                events.Add(new RulesEvent("LootCardDrawn", unitId)
                    { Cell = marker.Position, ObjectCard = card, LootMarker = marker });
                if (category == LootCategory.Immediate && card.Kind == LootCardKind.PocketBreakfast)
                    RestoreHp(state, unitId, LootCards.Content(card.Kind).Healing, unitId, LootCards.Content(card.Kind).Name, "pocket-breakfast", events);
                ResolveExplosions(state, events);
            }
        }
    }

    private static IEnumerable<Candidate> ObjectCandidates(GameState state, Unit unit)
    {
        if (state.GroundObjects.Count == 0 && unit.Inventory.IsEmpty) yield break;
        if (!state.IsUpright(unit.Id)) yield break;
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var cells = FootprintGeometry.OccupiedCells(state, unit.Id);
        foreach (var ground in state.GroundObjects.Where(g => cells.Contains(g.Position) && LootRules.CanOwn(state, unit, g.Object)))
            yield return new($"pickup-object:{ground.Object.Id}", ground.Position, Kind: ActivationChoiceKind.FreeAction)
            { ObjectId = ground.Object.Id, ObjectKind = ground.Object.Kind, EquipmentTypeId = state.EquipmentTypeId(ground.Object) };
        if (state.BonusActionUsesThisActivation("Second Wind") < state.EffectiveBonusActionUsesPerActivation)
            foreach (var item in unit.Inventory.Where(o => state.EquipmentTypeId(o) is null && o.Kind == LootCardKind.SecondWind))
                yield return new($"use-object:{item.Id}", Kind: ActivationChoiceKind.BonusAction,
                    BonusAction: new("Second Wind", null, [])) { ObjectId = item.Id, ObjectKind = item.Kind };
        if (state.MoveDone && state.ActionsRemaining > 0)
            foreach (var source in unit.Inventory.Where(o => state.EquipmentTypeId(o) is not null))
            foreach (var ability in state.Equipment(state.EquipmentTypeId(source)!).Abilities)
            {
                switch (ability.Mechanic)
                {
                    case EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit:
                        foreach (var target in state.Units.Where(u => u.CurrentHp > 0 && u.SideId == unit.SideId &&
                                     state.Physical.Figures.Any(f => f.Id == u.Id && f.Posture == Posture.Lying) &&
                                     SpatialRules.AreAdjacent(state, unit.Id, u.Id)))
                        {
                            var prefix = ability.CandidatePrefix ?? $"equipment:{Uri.EscapeDataString(source.Id)}:{Uri.EscapeDataString(ability.Id)}";
                            yield return new($"{prefix}:{target.Id}", TargetId: target.Id)
                            { ObjectId = source.Id, EquipmentTypeId = state.EquipmentTypeId(source), EquipmentAbilityId = ability.Id,
                                AbilityName = ability.Name, EquipmentMechanic = ability.Mechanic };
                        }
                        break;
                }
            }
    }

    private static void ResolveObjectChoice(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var index = state.Units.FindIndex(u => u.Id == request.UnitId);
        var unit = state.Units[index];
        var content = state.CandidateObjectContent(choice)!;
        if (choice.Kind == ActivationChoiceKind.FreeAction)
        {
            var ground = state.GroundObjects.Single(g => g.Object.Id == choice.ObjectId);
            state.GroundObjects.Remove(ground);
            state.Units[index] = unit with { Inventory = unit.Inventory.Add(ground.Object) };
            events.Add(new RulesEvent("ObjectCollected", unit.Id, AbilityName: content.Name)
                { ObjectCard = ground.Object, Cell = ground.Position, Category = choice.Kind });
        }
        else if (choice.EquipmentTypeId is null && choice.ObjectKind == LootCardKind.SecondWind)
        {
            var item = unit.Inventory.Single(o => o.Id == choice.ObjectId);
            state.Units[index] = unit with { Inventory = unit.Inventory.Remove(item) };
            state.LootDeck ??= new(); // Discard records do not create a draw supply.
            state.LootDeck.DiscardPile.Add(item);
            state.ExtraActionsThisActivation += content.ExtraActions;
            state.BonusActionUseCountsThisActivation["Second Wind"] = state.BonusActionUsesThisActivation("Second Wind") + 1;
            events.Add(new RulesEvent("ItemUsed", unit.Id, AbilityName: content.Name)
                { ObjectCard = item, ActionId = "second-wind", Category = choice.Kind });
        }
        else
        {
            var source = unit.Inventory.Single(o => o.Id == choice.ObjectId);
            var ability = state.Equipment(state.EquipmentTypeId(source)!).Abilities.Single(a => a.Id == choice.EquipmentAbilityId);
            var actionId = ability.CandidatePrefix ?? state.Equipment(choice.EquipmentTypeId!).EntryId(ability.Id);
            state.ActionsUsedThisActivation++;
            events.Add(new RulesEvent("ActionUsed", unit.Id, choice.TargetId, AbilityName: ability.Name)
                { ActionId = actionId, Category = choice.Kind, ObjectCard = source });
            switch (ability.Mechanic)
            {
                case EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit:
                    ChangePosture(state, choice.TargetId!, Posture.Upright, events, unit.Id, actionId);
                    break;
                default: throw new InvalidOperationException("Unsupported Equipment Action.");
            }
        }
    }
}
