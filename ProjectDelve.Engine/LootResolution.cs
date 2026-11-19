namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveLootDrop(GameState state, DefeatContext defeat, ResolutionEvents events)
    {
        var chance = defeat.Type.LootDropSuccesses;
        if (defeat.Type.CreditsDropSuccesses > 0)
        {
            ResolveConfiguredDefeatDrop(state, defeat, events);
            return;
        }
        if (chance == 0 || (state.LootDeck?.DrawPile.Count ?? 0) <= state.LootMarkers.Count) return;
        var roll = events.Random.RollD6();
        var success = roll <= chance;
        LootMarker? marker = null;
        if (success) marker = PlaceDefeatLoot(state, defeat);
        events.Add(new RulesEvent("LootDropRolled", defeat.Unit.Id, DieRoll: roll, SuccessCount: chance, Succeeded: success)
        { Cell = defeat.Figure.Position, LootMarker = marker, DefeatContext = defeat });
    }

    private static void ResolveConfiguredDefeatDrop(GameState state, DefeatContext defeat, ResolutionEvents events)
    {
        var lootFaces = defeat.Type.LootDropSuccesses;
        var creditsFaces = defeat.Type.CreditsDropSuccesses;
        var lootAvailable = (state.LootDeck?.DrawPile.Count ?? 0) > state.LootMarkers.Count;
        // Exhausted Loot faces remain nothing. They are never reassigned to Credits.
        // Only all-Credits has a single effective outcome with Credits enabled.
        int? roll = creditsFaces == 6 ? null : events.Random.RollD6();
        var credits = roll is null || roll > lootFaces && roll <= lootFaces + creditsFaces;
        LootMarker? marker = null;
        if (credits)
            state.PlaceCredits(defeat.Type.CreditsDropAmount, defeat.Figure.Position);
        else if (lootAvailable && roll <= lootFaces) marker = PlaceDefeatLoot(state, defeat);
        events.Add(new RulesEvent("DefeatDropResolved", defeat.Unit.Id, DieRoll: roll, Succeeded: credits || marker is not null)
        {
            Cell = defeat.Figure.Position, LootMarker = marker, DefeatContext = defeat,
            CreditsAmount = credits ? defeat.Type.CreditsDropAmount : null
        });
    }

    private static LootMarker PlaceDefeatLoot(GameState state, DefeatContext defeat)
    {
        var marker = new LootMarker($"drop:{defeat.Unit.Id}", defeat.Figure.Position);
        state.LootMarkers.Add(marker);
        return marker;
    }

    // Arrival hazards are resolved by callers before this queue is drained. It is
    // sequence-local: no ordinary decision can interrupt mandatory pickup.
    private static void ResolveLootArrivals(GameState state, ResolutionEvents events)
    {
        while (events.LootArrivals.Count > 0)
        {
            var unitId = events.LootArrivals[0];
            events.LootArrivals.RemoveAt(0);
            while ((state.CreditsPiles.Count != 0 || state.LootMarkers.Count != 0) &&
                state.Physical.Figures.Any(f => f.Id == unitId) && state.Units.Any(u => u.Id == unitId && u.CurrentHp > 0))
            {
                CreditsPile? pile = null;
                LootMarker? marker = null;
                // OccupiedCells yields board order. Re-read the collector after
                // each pickup and its consequences, including changes of position.
                foreach (var cell in FootprintGeometry.OccupiedCells(state, unitId))
                {
                    pile = state.CreditsPiles.Find(p => p.Position == cell);
                    if (pile is not null) break;
                    marker = state.LootMarkers.Find(m => m.Position == cell);
                    if (marker is not null) break;
                }
                if (pile is not null)
                {
                    var collector = state.Units.Single(u => u.Id == unitId);
                    var balance = checked(state.SideCredits.GetValueOrDefault(collector.SideId) + pile.Amount);
                    state.SideCredits[collector.SideId] = balance;
                    state.CreditsPiles.Remove(pile);
                    events.Add(new RulesEvent("CreditsCollected", unitId)
                        { SideId = collector.SideId, Cell = pile.Position, CreditsAmount = pile.Amount, CreditsBalance = balance });
                    continue;
                }
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
                    state.Units[index] = state.WithInventory(unit, unit.Inventory.Add(card));
                else state.GroundObjects.Add(new(card, state.Physical.Figures.Single(f => f.Id == unitId).Position));
                events.Add(new RulesEvent("LootCardDrawn", unitId)
                    { Cell = marker.Position, ObjectCard = card, LootMarker = marker });
                if (category == LootCategory.Immediate)
                {
                    var definition = state.LootCard(card);
                    switch (definition.Immediate)
                    {
                        case HealCollectorMechanic healing:
                            RestoreHp(state, unitId, healing.Amount, unitId, definition.Name, LootCompatibility.ActionId(definition.Id), events);
                            break;
                        default: throw new InvalidOperationException("Unsupported Immediate Loot mechanic.");
                    }
                }
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
            { ObjectId = ground.Object.Id, ObjectKind = ground.Object.Kind, EquipmentTypeId = state.EquipmentTypeId(ground.Object), ItemTypeId = state.ItemTypeId(ground.Object) };
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
            state.Units[index] = state.WithInventory(unit, unit.Inventory.Add(ground.Object));
            events.Add(new RulesEvent("ObjectCollected", unit.Id, AbilityName: content.Name)
                { ObjectCard = ground.Object, Cell = ground.Position, Category = choice.Kind });
        }
    }
}
