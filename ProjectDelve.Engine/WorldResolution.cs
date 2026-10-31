using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveRoundStartWorldCards(GameState state, IRandomProvider random, ResolutionEvents events)
    {
        if (state.WorldEffects is not { } settings) return;
        var deck = state.WorldDeck ??= WorldCards.CreateDeck(settings, state.WorldCardDefinitions);
        if (state.Round == 1)
        {
            ShuffleWorldPile(deck.DrawPile, random);
            events.Add(new("WorldDeckShuffled"));
        }
        for (var draw = 0; draw < settings.CardsPerRound; draw++)
        {
            if (deck.DrawPile.Count == 0 && deck.DiscardPile.Count > 0)
            {
                deck.DrawPile.AddRange(deck.DiscardPile);
                deck.DiscardPile.Clear();
                ShuffleWorldPile(deck.DrawPile, random);
                events.Add(new("WorldDeckReshuffled"));
            }
            if (deck.DrawPile.Count == 0)
            {
                events.Add(new("WorldDrawSkipped"));
                break;
            }
            var card = deck.DrawPile[0];
            deck.DrawPile.RemoveAt(0);
            deck.ResolvingCard = card;
            events.Add(new RulesEvent("WorldCardDrawn") { WorldCard = card });
            if (state.WorldCard(card).Lifecycle == WorldCardLifecycle.Continuous)
            {
                WorldCard? cycled = null;
                if (deck.ActiveContinuous.Count == settings.Cycling)
                {
                    cycled = deck.ActiveContinuous[0];
                    deck.ActiveContinuous.RemoveAt(0);
                    deck.DiscardPile.Add(cycled);
                }
                deck.ActiveContinuous.Add(card);
                deck.ResolvingCard = null;
                // Replacement is atomic: only the completed row is exposed.
                events.Add(new RulesEvent("WorldContinuousChanged") { WorldCard = card, CycledWorldCard = cycled });
            }
            else
            {
                ResolveImmediateWorldCard(state, card, events);
                ResolveExplosions(state, events);
                deck.ResolvingCard = null;
                deck.DiscardPile.Add(card);
                events.Add(new RulesEvent("WorldCardDiscarded") { WorldCard = card });
            }
        }
    }

    private static void ShuffleWorldPile(List<WorldCard> pile, IRandomProvider random)
    {
        for (var i = pile.Count - 1; i > 0; i--)
        {
            var chosen = random.WorldShuffleIndex(i + 1);
            if (chosen < 0 || chosen > i)
                throw new ArgumentException("World shuffle index is outside the supplied range.", nameof(random));
            (pile[i], pile[chosen]) = (pile[chosen], pile[i]);
        }
    }

    private static void ResolveImmediateWorldCard(GameState state, WorldCard card, ResolutionEvents events)
    {
        var content = state.WorldCard(card);
        var mechanic = content.Immediate;
        var actionId = WorldCards.ActionId(card);
        if (mechanic is SetAllDoorsMechanic doorsMechanic)
        {
            var from = doorsMechanic.Open ? EdgeKind.ClosedDoor : EdgeKind.OpenDoor;
            var doors = state.Physical.Board.Edges.Where(e => e.Kind == from)
                .OrderBy(e => Math.Min(e.A.Y, e.B.Y)).ThenBy(e => Math.Min(e.A.X, e.B.X)).ToArray();
            foreach (var door in doors)
            {
                if (doorsMechanic.Open) OpenDoor(state, null, door, events, actionId);
                else
                {
                    var closed = door with { Kind = EdgeKind.ClosedDoor };
                    state.Physical.Board.Edges[state.Physical.Board.Edges.IndexOf(door)] = closed;
                    events.Add(new RulesEvent("DoorClosed", Door: closed) { ActionId = actionId });
                }
            }
            return;
        }
        // Snapshot membership, including adjacency, before resolving any recipient.
        var recipients = state.Physical.Figures.OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X)
            .Where(f => mechanic switch
            {
                HealDamagedUnitsMechanic => state.Units.Single(u => u.Id == f.Id).CurrentHp <
                    state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == f.Id).TypeId).Hp,
                DamageUnitsByAdjacencyMechanic { Adjacent: true } => state.Physical.Figures.Any(other => other.Id != f.Id && SpatialRules.AreAdjacent(state, f.Id, other.Id)),
                DamageUnitsByAdjacencyMechanic { Adjacent: false } => !state.Physical.Figures.Any(other => other.Id != f.Id && SpatialRules.AreAdjacent(state, f.Id, other.Id)),
                _ => true
            }).Select(f => f.Id).ToArray();
        foreach (var id in recipients)
        {
            if (!state.Physical.Figures.Any(f => f.Id == id)) continue;
            switch (mechanic)
            {
                case LayDownAllUnitsMechanic:
                    if (state.IsUpright(id)) ChangePosture(state, id, Posture.Lying, events, actionId: actionId);
                    break;
                case FullyHealUnitsMechanic:
                case HealDamagedUnitsMechanic:
                    RestoreHp(state, id, mechanic is FullyHealUnitsMechanic
                        ? state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == id).TypeId).Hp : ((HealDamagedUnitsMechanic)mechanic).Amount,
                        null, content.Name, actionId, events);
                    break;
                case DamageUnitsByAdjacencyMechanic damage:
                    DealDamage(state, id, damage.Amount,
                        new RulesEvent("WorldDamageResolved", TargetId: id, Damage: damage.Amount, AbilityName: content.Name)
                            { ActionId = actionId }, events);
                    break;
                case ReplenishLimitedAbilitiesMechanic renewal:
                    ReplenishUnitAbilities(state, id, renewal.Amount, events, actionId);
                    break;
            }
        }
    }

    private static void RestoreHp(GameState state, string targetId, int amount, string? sourceId,
        string abilityName, string actionId, ResolutionEvents events)
    {
        var index = state.Units.FindIndex(u => u.Id == targetId);
        var target = state.Units[index];
        var healing = Math.Min(amount, state.Types.Single(t => t.Id == target.TypeId).Hp - target.CurrentHp);
        state.Units[index] = target with { CurrentHp = target.CurrentHp + healing };
        events.Add(new RulesEvent("HealResolved", sourceId, targetId, AbilityName: abilityName, Healing: healing)
            { SourceUnitId = sourceId, ActionId = actionId });
    }

    private static void ReplenishUnitAbilities(GameState state, string id, int amount, ResolutionEvents events, string actionId)
    {
        var index = state.Units.FindIndex(u => u.Id == id);
        var unit = state.Units[index];
        state.Units[index] = unit with
        {
            AbilityUseCounts = unit.AbilityUseCounts.ToImmutableDictionary(p => p.Key, p => Math.Max(0, p.Value - amount))
        };
        events.Add(new RulesEvent("AbilityUsesReplenished", id) { ActionId = actionId, ReplenishedUses = amount });
    }

    private static void ValidateWorldDeck(GameState state)
    {
        WorldCardCatalog.Validate(state.WorldCardDefinitions);
        if (state.WorldEffects is { } settings && (settings.CardsPerRound < 0 || settings.Cycling < 1))
            throw new ArgumentException("World Effects require CardsPerRound >= 0 and Cycling >= 1.");
        if (state.WorldDeck is not { } deck) return;
        if (state.WorldEffects is null) throw new ArgumentException("A World Deck requires World Effects settings.");
        var cards = deck.DrawPile.Concat(deck.DiscardPile).Concat(deck.ActiveContinuous)
            .Concat(deck.ResolvingCard is { } card ? [card] : Array.Empty<WorldCard>()).ToArray();
        if (cards.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || !state.WorldCardDefinitions.Any(d => d.Id == c.ResolvedTypeId)) ||
            cards.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != cards.Length ||
            deck.ActiveContinuous.Count > state.WorldEffects.Cycling ||
            deck.ActiveContinuous.Any(c => state.WorldCard(c).Lifecycle != WorldCardLifecycle.Continuous) || deck.ResolvingCard is not null)
            throw new ArgumentException("Invalid World Card locations or active Continuous row.");
    }
}
