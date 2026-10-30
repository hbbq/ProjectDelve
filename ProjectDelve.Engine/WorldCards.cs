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
    public static IReadOnlyList<WorldCardContent> All => WorldCardRoster.All;

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
