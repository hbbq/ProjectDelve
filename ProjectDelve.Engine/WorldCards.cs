namespace ProjectDelve.Engine;

// Null scenario settings mean no World Deck. This is the current concrete deck only.
public sealed record WorldEffectsSettings(int CardsPerRound, int Cycling);
public enum WorldEffect
{
    Bloodlust, Weakness, IronSkin, Vulnerability, Haste, Sluggishness,
    Frenzy, Fatigue, Surge, Hesitation, Calm,
    Earthquake, OpenSesame, Lockdown, Miracle, SecondWind, Repulsion, Loneliness, Renewal
}
public sealed record WorldCard(string Id, WorldEffect Effect);
public sealed record WorldCardContent(WorldEffect Effect, string Name, string Text, bool Continuous, int Copies);

public static class WorldCards
{
    public static IReadOnlyList<WorldCardContent> All { get; } = Array.AsReadOnly(new WorldCardContent[]
    {
        new(WorldEffect.Bloodlust, "Bloodlust", "All Units have ATK +1.", true, 3),
        new(WorldEffect.Weakness, "Weakness", "All Units have ATK -1.", true, 3),
        new(WorldEffect.IronSkin, "Iron Skin", "All Units have DEF +1.", true, 3),
        new(WorldEffect.Vulnerability, "Vulnerability", "All Units have DEF -1.", true, 3),
        new(WorldEffect.Haste, "Haste", "All Units have MOV +1.", true, 3),
        new(WorldEffect.Sluggishness, "Sluggishness", "All Units have MOV -1.", true, 3),
        new(WorldEffect.Frenzy, "Frenzy", "All Units have +1 Action per activation.", true, 1),
        new(WorldEffect.Fatigue, "Fatigue", "All Units have -1 Action per activation.", true, 1),
        new(WorldEffect.Surge, "Surge", "Each unique Bonus Action may be used one additional time per activation.", true, 1),
        new(WorldEffect.Hesitation, "Hesitation", "Each unique Bonus Action may be used one fewer time per activation.", true, 1),
        new(WorldEffect.Calm, "Calm", "No Effect.", true, 8),
        new(WorldEffect.Earthquake, "Earthquake", "Lay down all Units.", false, 1),
        new(WorldEffect.OpenSesame, "Open Sesame!", "Open all Doors.", false, 1),
        new(WorldEffect.Lockdown, "Lockdown", "Close all Doors.", false, 1),
        new(WorldEffect.Miracle, "Miracle", "Fully heal all Units.", false, 1),
        new(WorldEffect.SecondWind, "Second Wind", "Heal every damaged Unit for 1 HP.", false, 2),
        new(WorldEffect.Repulsion, "Repulsion", "Deal 1 damage to every Unit adjacent to another Unit.", false, 1),
        new(WorldEffect.Loneliness, "Loneliness", "Deal 1 damage to every Unit not adjacent to another Unit.", false, 1),
        new(WorldEffect.Renewal, "Renewal", "Replenish 1 use of every limited-use Ability.", false, 2)
    });

    public static WorldCardContent Content(WorldEffect effect) => All.Single(c => c.Effect == effect);
    public static WorldDeck CreateDeck() => new()
    {
        DrawPile = All.SelectMany(c => Enumerable.Range(1, c.Copies)
            .Select(copy => new WorldCard($"world-{c.Effect}-{copy}", c.Effect))).ToList()
    };
}

public sealed class WorldDeck
{
    // Top card first; active row oldest first. ResolvingCard is sequence-local draw context.
    public List<WorldCard> DrawPile { get; set; } = [];
    public List<WorldCard> DiscardPile { get; set; } = [];
    public List<WorldCard> ActiveContinuous { get; set; } = [];
    public WorldCard? ResolvingCard { get; set; }
    internal WorldDeck Copy() => new()
    {
        DrawPile = [.. DrawPile], DiscardPile = [.. DiscardPile],
        ActiveContinuous = [.. ActiveContinuous], ResolvingCard = ResolvingCard
    };
    internal int Modifier(WorldEffect positive, WorldEffect negative) =>
        ActiveContinuous.Count(c => c.Effect == positive) - ActiveContinuous.Count(c => c.Effect == negative);
}
