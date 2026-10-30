using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldCardDefinitionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public void EveryCanonicalDefinitionRoundTripsThroughTheSharedTypedJsonModel()
    {
        WorldCardCatalog.Validate(WorldCardCatalog.All);
        foreach (var definition in WorldCardCatalog.All)
        {
            var json = JsonSerializer.Serialize(definition, Json);
            var restored = JsonSerializer.Deserialize<WorldCardDefinition>(json, Json)!;
            WorldCardCatalog.Validate([restored]);
            Assert.Equal(json, JsonSerializer.Serialize(restored, Json));
            Assert.Equal(definition.Description, restored.Description);
            Assert.Equal(definition.Immediate, restored.Immediate);
            Assert.Equal(definition.Modifiers.ToArray(), restored.Modifiers.ToArray());
        }
    }

    [Fact]
    public void BaselineCompositionPreservesPhysicalIdentityOrderAndLegacySaveReferences()
    {
        var expected = new (WorldEffect Effect, int Count)[]
        {
            (WorldEffect.Bloodlust, 3), (WorldEffect.Weakness, 3), (WorldEffect.IronSkin, 3), (WorldEffect.Vulnerability, 3),
            (WorldEffect.Haste, 3), (WorldEffect.Sluggishness, 3), (WorldEffect.Frenzy, 1), (WorldEffect.Fatigue, 1),
            (WorldEffect.Surge, 1), (WorldEffect.Hesitation, 1), (WorldEffect.Calm, 8), (WorldEffect.Earthquake, 1),
            (WorldEffect.OpenSesame, 1), (WorldEffect.Lockdown, 1), (WorldEffect.Miracle, 1), (WorldEffect.SecondWind, 2),
            (WorldEffect.Repulsion, 1), (WorldEffect.Loneliness, 1), (WorldEffect.Renewal, 2)
        };
        var deck = WorldCards.CreateDeck();
        Assert.Equal(expected.SelectMany(r => Enumerable.Range(1, r.Count).Select(n => $"world-{r.Effect}-{n}")),
            deck.DrawPile.Select(c => c.Id));
        foreach (var (effect, _) in expected)
        {
            var legacy = JsonSerializer.Deserialize<WorldCard>($$"""{"id":"saved-physical-card","effect":"{{effect}}"}""", Json)!;
            Assert.Equal(new WorldCard("saved-physical-card", effect), legacy);
            Assert.Equal(WorldCards.LegacyTypeId(effect), legacy.ResolvedTypeId);
            Assert.Equal($"world-{effect}", WorldCards.ActionId(legacy));
            var definition = WorldCardCatalog.All.Single(d => d.Id == legacy.ResolvedTypeId);
            Assert.Equal(WorldCards.Content(effect).Text, definition.Description);
        }
    }

    [Fact]
    public void LegacyProjectionDoesNotRequireNewDefinitionsToHaveLegacyIdentitiesOrDeckRows()
    {
        var added = new WorldCardDefinition("restoring-weather", "Restoring Weather", WorldCardLifecycle.Immediate)
        { Immediate = new HealDamagedUnitsMechanic(2) };
        WorldCardDefinition[] definitions = [.. WorldCardCatalog.All, added];
        WorldCardCatalog.Validate(definitions);

        Assert.Null(WorldCards.LegacyEffect(added.Id));
        Assert.Equal(WorldCards.All, WorldCards.ProjectLegacyContent(definitions, WorldCardRoster.Composition));
        Assert.Equal(WorldCards.CreateDeck().DrawPile, WorldCards.CreateDeck(definitions: definitions).DrawPile);

        var selected = WorldCards.CreateDeck(new(1, 1) { Composition = [new(added.Id, 1)] }, definitions);
        Assert.Equal(added.Id, Assert.Single(selected.DrawPile).ResolvedTypeId);
        var withoutCalm = WorldCardRoster.Composition.Where(row => row.TypeId != "calm").ToArray();
        var projected = WorldCards.ProjectLegacyContent(definitions, withoutCalm);
        Assert.Equal(0, projected.Single(card => card.Effect == WorldEffect.Calm).Copies);
        Assert.Equal(WorldCards.All.Where(card => card.Effect != WorldEffect.Calm),
            projected.Where(card => card.Effect != WorldEffect.Calm));
    }

    [Fact]
    public void ExplicitTypeReferenceWinsOverLegacyFieldWithoutSelectingLegacyMechanics()
    {
        var card = JsonSerializer.Deserialize<WorldCard>("""{"id":"physical-91","effect":"Earthquake","typeId":"local-rain"}""", Json)!;
        Assert.Equal("local-rain", card.ResolvedTypeId);
        Assert.Equal("world:local-rain", WorldCards.ActionId(card));
        Assert.Equal(card, JsonSerializer.Deserialize<WorldCard>(JsonSerializer.Serialize(card, Json), Json));
    }

    [Fact]
    public void PhysicalCardRequiresTypedOrLegacyReferenceAndTypedJsonNeedsNoLegacyField()
    {
        var card = JsonSerializer.Deserialize<WorldCard>("""{"id":"physical-92","typeId":"local-rain"}""", Json)!;
        Assert.Equal(new WorldCard("physical-92", "local-rain"), card);
        Assert.Equal(card, JsonSerializer.Deserialize<WorldCard>(JsonSerializer.Serialize(card, Json), Json));
        var missing = JsonSerializer.Deserialize<WorldCard>("""{"id":"physical-without-type"}""", Json)!;
        Assert.Throws<ArgumentException>(() => missing.ResolvedTypeId);
    }

    [Fact]
    public void DefinitionsAreScopedAndCompositionIsIndependentOfPresentation()
    {
        var local = new WorldCardDefinition("local-name", "Haste", WorldCardLifecycle.Immediate)
        { Immediate = new HealDamagedUnitsMechanic(3) };
        var first = WorldCardCatalog.Resolve([local]);
        var second = WorldCardCatalog.Resolve([local with { Name = "Different Name", Immediate = new HealDamagedUnitsMechanic(2) }]);
        Assert.Equal(3, ((HealDamagedUnitsMechanic)first.Last().Immediate!).Amount);
        Assert.Equal(2, ((HealDamagedUnitsMechanic)second.Last().Immediate!).Amount);
        Assert.DoesNotContain(WorldCardCatalog.All, d => d.Id == local.Id);
        var settings = new WorldEffectsSettings(2, 1) { Composition = [new(local.Id, 3), new("calm", 2)] };
        var deck = WorldCards.CreateDeck(settings, first);
        Assert.Equal(5, deck.DrawPile.Count);
        Assert.Equal(3, deck.DrawPile.Count(c => c.ResolvedTypeId == local.Id));
        Assert.Equal(5, deck.DrawPile.Select(c => c.Id).Distinct().Count());
        Assert.Empty(WorldCards.CreateDeck(settings with { Composition = [] }, first).DrawPile);
    }

    [Fact]
    public void OverrideParametersStayAuthoritativeForHealingAndComposedModifiers()
    {
        var rain = new WorldCardDefinition("rain", "Rain", WorldCardLifecycle.Immediate)
        { Immediate = new HealDamagedUnitsMechanic(2), DescriptionOverride = "Restore {amount} HP to every damaged Unit." };
        var wind = new WorldCardDefinition("wind", "Wind", WorldCardLifecycle.Continuous)
        { Modifiers = [new(WorldModifierTarget.Mov, 2), new(WorldModifierTarget.Def, -1)], DescriptionOverride = "While active: {modifiers}" };
        WorldCardCatalog.Validate([rain, wind]);
        Assert.Equal("Restore 2 HP to every damaged Unit.", rain.Description);
        Assert.Equal("Restore 4 HP to every damaged Unit.", (rain with { Immediate = new HealDamagedUnitsMechanic(4) }).Description);
        Assert.Equal("While active: All Units have MOV +2. All Units have DEF -1.", wind.Description);
        Assert.Equal("While active: All Units have MOV +3.", (wind with { Modifiers = [new(WorldModifierTarget.Mov, 3)] }).Description);
    }

    [Theory]
    [InlineData("Restore 2 HP.")]
    [InlineData("Restore all health.")]
    [InlineData("Restore {amount} HP and {other}.")]
    [InlineData("Restore {amount} HP; {modifiers}")]
    [InlineData("")]
    public void InvalidNumericDescriptionOverridesAreRejected(string text)
    {
        var card = new WorldCardDefinition("rain", "Rain", WorldCardLifecycle.Immediate)
        { Immediate = new HealDamagedUnitsMechanic(2), DescriptionOverride = text };
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card]));
    }

    [Fact]
    public void ValidationRejectsMalformedAndUnsupportedDefinitions()
    {
        var immediate = new WorldCardDefinition("test", "Test", WorldCardLifecycle.Immediate);
        var continuous = new WorldCardDefinition("test", "Test", WorldCardLifecycle.Continuous);
        WorldCardDefinition[] invalid =
        [
            immediate,
            immediate with { Immediate = new HealDamagedUnitsMechanic(0) },
            immediate with { Immediate = new DamageUnitsByAdjacencyMechanic(-1, true) },
            immediate with { Immediate = new ReplenishLimitedAbilitiesMechanic(101) },
            immediate with { Immediate = new FullyHealUnitsMechanic(), Modifiers = [new(WorldModifierTarget.Mov, 1)] },
            continuous with { Immediate = new FullyHealUnitsMechanic() },
            continuous with { Modifiers = default },
            continuous with { Modifiers = [new((WorldModifierTarget)999, 1)] },
            continuous with { Modifiers = [new(WorldModifierTarget.Mov, 101)] },
            continuous with { Lifecycle = (WorldCardLifecycle)999 },
            continuous with { Id = "" },
            continuous with { Name = "" },
            continuous with { DescriptionOverride = "Unknown {amount}" }
        ];
        foreach (var card in invalid) Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card]));
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Resolve([continuous with { Id = "haste" }]));
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Resolve([continuous, continuous]));
    }

    [Theory]
    [InlineData("""{"id":"unknown","name":"Unknown","lifecycle":"Immediate","immediate":{"kind":"script","code":"anything"}}""")]
    [InlineData("""{"id":"unknown","name":"Unknown","lifecycle":"Immediate","immediate":{"kind":"healDamagedUnits"}}""")]
    [InlineData("""{"id":"unknown","name":"Unknown","lifecycle":"Unknown"}""")]
    public void JsonRejectsUnsupportedMechanicsAndMissingRequiredParameters(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorldCardDefinition>(json, Json));

    [Fact]
    public void CompositionValidationRejectsUnknownDuplicateAndInvalidQuantities()
    {
        IReadOnlyList<WorldCardQuantity>[] invalid =
        [
            [new("missing", 1)], [new("haste", 0)], [new("haste", -1)], [new("haste", 1001)],
            [new("haste", 1), new("haste", 2)], [new("", 1)],
            WorldCardRoster.Composition.Take(7).Select(q => q with { Count = 1000 }).ToArray()
        ];
        foreach (var composition in invalid)
            Assert.Throws<ArgumentException>(() => WorldCards.CreateDeck(new(1, 1) { Composition = composition }));
    }
}
