using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Null settings mean no World Deck; null composition means the canonical baseline.
public sealed record WorldEffectsSettings(int CardsPerRound, int Cycling)
{
    public IReadOnlyList<WorldCardQuantity>? Composition { get; init; }
}
public sealed record WorldCardQuantity(string TypeId, int Count = 1);
public enum WorldCardLifecycle { Immediate, Continuous }
public enum WorldModifierTarget { Atk, Def, Mov, Actions, BonusActionUses }
public sealed record WorldModifier(WorldModifierTarget Target, int Amount);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LayDownAllUnitsMechanic), "layDownAllUnits")]
[JsonDerivedType(typeof(SetAllDoorsMechanic), "setAllDoors")]
[JsonDerivedType(typeof(FullyHealUnitsMechanic), "fullyHealUnits")]
[JsonDerivedType(typeof(HealDamagedUnitsMechanic), "healDamagedUnits")]
[JsonDerivedType(typeof(DamageUnitsByAdjacencyMechanic), "damageUnitsByAdjacency")]
[JsonDerivedType(typeof(ReplenishLimitedAbilitiesMechanic), "replenishLimitedAbilities")]
public abstract record WorldImmediateMechanic;
public sealed record LayDownAllUnitsMechanic : WorldImmediateMechanic;
public sealed record SetAllDoorsMechanic(bool Open) : WorldImmediateMechanic;
public sealed record FullyHealUnitsMechanic : WorldImmediateMechanic;
public sealed record HealDamagedUnitsMechanic(int Amount) : WorldImmediateMechanic;
public sealed record DamageUnitsByAdjacencyMechanic(int Amount, bool Adjacent) : WorldImmediateMechanic;
public sealed record ReplenishLimitedAbilitiesMechanic(int Amount) : WorldImmediateMechanic;

public sealed record WorldCardDefinition(string Id, string Name, WorldCardLifecycle Lifecycle)
{
    public ImmutableArray<WorldModifier> Modifiers { get; init; } = [];
    public WorldImmediateMechanic? Immediate { get; init; }
    // Numeric values are inserted from the declaration, never independently authored.
    public string? DescriptionOverride { get; init; }
    [JsonIgnore] public bool Continuous => Lifecycle == WorldCardLifecycle.Continuous;
    [JsonIgnore] public string Description
    {
        get
        {
            var modifiers = string.Join(" ", Modifiers.Select(DescribeModifier));
            var amount = Immediate switch
            {
                HealDamagedUnitsMechanic m => m.Amount,
                DamageUnitsByAdjacencyMechanic m => m.Amount,
                ReplenishLimitedAbilitiesMechanic m => m.Amount,
                _ => 0
            };
            var text = DescriptionOverride ?? (Continuous ? (Modifiers.IsEmpty ? "No Effect." : "{modifiers}") : Immediate switch
            {
                LayDownAllUnitsMechanic => "Lay down all Units.",
                SetAllDoorsMechanic m => m.Open ? "Open all Doors." : "Close all Doors.",
                FullyHealUnitsMechanic => "Fully heal all Units.",
                HealDamagedUnitsMechanic => "Heal every damaged Unit for {amount} HP.",
                DamageUnitsByAdjacencyMechanic m => "Deal {amount} damage to every Unit " + (m.Adjacent ? "adjacent" : "not adjacent") + " to another Unit.",
                ReplenishLimitedAbilitiesMechanic => "Replenish {amount} " + (amount == 1 ? "use" : "uses") + " of every limited-use Ability.",
                _ => throw new ArgumentException("Unsupported Immediate World mechanic.")
            });
            return text.Replace("{amount}", amount.ToString(CultureInfo.InvariantCulture)).Replace("{modifiers}", modifiers);
        }
    }

    private static string DescribeModifier(WorldModifier modifier) => modifier.Target switch
    {
        WorldModifierTarget.Atk or WorldModifierTarget.Def or WorldModifierTarget.Mov =>
            $"All Units have {modifier.Target.ToString().ToUpperInvariant()} {modifier.Amount:+0;-0;0}.",
        WorldModifierTarget.Actions => $"All Units have {modifier.Amount:+0;-0;0} {(Math.Abs(modifier.Amount) == 1 ? "Action" : "Actions")} per activation.",
        WorldModifierTarget.BonusActionUses => $"Each unique Bonus Action may be used {(Math.Abs(modifier.Amount) == 1 ? "one" : Math.Abs(modifier.Amount).ToString(CultureInfo.InvariantCulture))} {(modifier.Amount >= 0 ? "additional" : "fewer")} {(Math.Abs(modifier.Amount) == 1 ? "time" : "times")} per activation.",
        _ => throw new ArgumentException("Unsupported World modifier.")
    };
}

public static class WorldCardCatalog
{
    public static IReadOnlyList<WorldCardDefinition> All => WorldCardRoster.Definitions;
    public static List<WorldCardDefinition> Resolve(IEnumerable<WorldCardDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList();
        Validate(definitions);
        return definitions;
    }
    public static void Validate(IReadOnlyList<WorldCardDefinition> definitions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !ids.Add(d.Id) || !Enum.IsDefined(d.Lifecycle))
                throw new ArgumentException("World Cards require unique nonblank Type IDs, names and supported lifecycles.");
            if (d.Modifiers.IsDefault || d.Modifiers.Any(m => m is null || !Enum.IsDefined(m.Target) || m.Amount is < -100 or > 100))
                throw new ArgumentException("Invalid World Card modifiers.");
            if (d.Continuous ? d.Immediate is not null : !d.Modifiers.IsEmpty || d.Immediate is not
                (LayDownAllUnitsMechanic or SetAllDoorsMechanic or FullyHealUnitsMechanic or HealDamagedUnitsMechanic or DamageUnitsByAdjacencyMechanic or ReplenishLimitedAbilitiesMechanic))
                throw new ArgumentException("Continuous cards accept modifiers; Immediate cards require one supported Immediate mechanic.");
            if (d.Immediate is HealDamagedUnitsMechanic { Amount: < 1 or > 100 } or DamageUnitsByAdjacencyMechanic { Amount: < 1 or > 100 } or ReplenishLimitedAbilitiesMechanic { Amount: < 1 or > 100 })
                throw new ArgumentException("World Card amounts must be from 1 to 100.");
            if (d.DescriptionOverride is { } text)
            {
                var amount = d.Immediate is HealDamagedUnitsMechanic or DamageUnitsByAdjacencyMechanic or ReplenishLimitedAbilitiesMechanic;
                var modifiers = !d.Modifiers.IsEmpty;
                var remainder = text.Replace("{amount}", "").Replace("{modifiers}", "");
                if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsDigit) || amount && !text.Contains("{amount}") || modifiers && !text.Contains("{modifiers}") ||
                    !amount && text.Contains("{amount}") || !modifiers && text.Contains("{modifiers}") || remainder.Contains('{') || remainder.Contains('}'))
                    throw new ArgumentException("World Card overrides must insert authoritative {amount} or {modifiers}, without duplicate numbers or unsupported placeholders.");
            }
        }
    }
}

// Read compatibility for established saves and callers. These names never dispatch mechanics.
public enum WorldEffect
{
    Bloodlust, Weakness, IronSkin, Vulnerability, Haste, Sluggishness,
    Frenzy, Fatigue, Surge, Hesitation, Calm,
    Earthquake, OpenSesame, Lockdown, Miracle, SecondWind, Repulsion, Loneliness, Renewal
}
[method: JsonConstructor]
public sealed record WorldCard(string Id, WorldEffect Effect = (WorldEffect)(-1))
{
    public string? TypeId
    {
        get;
        init
        {
            field = value;
            // A typed card needs no legacy identity. Keep its unused legacy field
            // serializable, while an omitted Type ID AND Effect remains invalid.
            if (value is not null && Effect == (WorldEffect)(-1)) Effect = WorldEffect.Calm;
        }
    }
    public WorldCard(string id, string typeId) : this(id, WorldEffect.Calm) => TypeId = typeId;
    [JsonIgnore] public string ResolvedTypeId => TypeId ?? WorldCards.LegacyTypeId(Effect);
}
public sealed record WorldCardContent(WorldEffect Effect, string Name, string Text, bool Continuous, int Copies);

public static class WorldCards
{
    // Explicit compatibility identities preserve historical card and event IDs.
    private static readonly IReadOnlyDictionary<WorldEffect, string> LegacyIds = new Dictionary<WorldEffect, string>
    {
        [WorldEffect.Bloodlust] = "bloodlust", [WorldEffect.Weakness] = "weakness",
        [WorldEffect.IronSkin] = "iron-skin", [WorldEffect.Vulnerability] = "vulnerability",
        [WorldEffect.Haste] = "haste", [WorldEffect.Sluggishness] = "sluggishness",
        [WorldEffect.Frenzy] = "frenzy", [WorldEffect.Fatigue] = "fatigue",
        [WorldEffect.Surge] = "surge", [WorldEffect.Hesitation] = "hesitation", [WorldEffect.Calm] = "calm",
        [WorldEffect.Earthquake] = "earthquake", [WorldEffect.OpenSesame] = "open-sesame", [WorldEffect.Lockdown] = "lockdown",
        [WorldEffect.Miracle] = "miracle", [WorldEffect.SecondWind] = "second-wind",
        [WorldEffect.Repulsion] = "repulsion", [WorldEffect.Loneliness] = "loneliness", [WorldEffect.Renewal] = "renewal"
    };
    public static string LegacyTypeId(WorldEffect effect) => LegacyIds.TryGetValue(effect, out var id) ? id : throw new ArgumentException("Unknown legacy World Effect.");
    public static WorldEffect? LegacyEffect(string typeId) => LegacyIds.Where(p => p.Value == typeId).Select(p => (WorldEffect?)p.Key).SingleOrDefault();
    public static string ActionId(WorldCard card) => LegacyEffect(card.ResolvedTypeId) is { } legacy ? $"world-{legacy}" : $"world:{Uri.EscapeDataString(card.ResolvedTypeId)}";
    public static IReadOnlyList<WorldCardContent> All => WorldCardRoster.All;
    public static WorldCardContent Content(WorldEffect effect) => All.Single(c => c.Effect == effect);
    public static IReadOnlyList<WorldCardQuantity> ResolveComposition(WorldEffectsSettings? settings, IReadOnlyList<WorldCardDefinition>? definitions = null)
    {
        var rows = settings?.Composition ?? WorldCardRoster.Composition;
        var catalog = definitions ?? WorldCardCatalog.All;
        if (settings is not null && (settings.CardsPerRound < 0 || settings.Cycling < 1))
            throw new ArgumentException("World Effects require CardsPerRound >= 0 and Cycling >= 1.");
        if (rows.Any(r => r is null || string.IsNullOrWhiteSpace(r.TypeId) || r.Count is < 1 or > 1000 || !catalog.Any(d => d.Id == r.TypeId)) ||
            rows.Select(r => r.TypeId).Distinct(StringComparer.Ordinal).Count() != rows.Count || rows.Sum(r => (long)r.Count) > 6000)
            throw new ArgumentException("World Deck composition requires known unique Type IDs and quantities from 1 to 1000 (at most 6000 cards).");
        return rows;
    }
    public static WorldDeck CreateDeck(WorldEffectsSettings? settings = null, IReadOnlyList<WorldCardDefinition>? definitions = null) => new()
    {
        DrawPile = ResolveComposition(settings, definitions).SelectMany(row => Enumerable.Range(1, row.Count).Select(copy =>
            LegacyEffect(row.TypeId) is { } legacy
                ? new WorldCard($"world-{legacy}-{copy}", legacy)
                : new WorldCard($"world-local:{Uri.EscapeDataString(row.TypeId)}:{copy}", row.TypeId))).ToList()
    };
}

public static class WorldCardQueries
{
    public static WorldCardDefinition WorldCard(this GameState state, string typeId) => state.WorldCardDefinitions.SingleOrDefault(d => d.Id == typeId)
        ?? throw new ArgumentException($"Unknown World Card Type ID '{typeId}'.");
    public static WorldCardDefinition WorldCard(this GameState state, WorldCard card) => state.WorldCard(card.ResolvedTypeId);
    public static int WorldModifier(this GameState state, WorldModifierTarget target) => state.WorldDeck?.ActiveContinuous
        .SelectMany(c => state.WorldCard(c).Modifiers).Where(m => m.Target == target).Sum(m => m.Amount) ?? 0;
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
}
