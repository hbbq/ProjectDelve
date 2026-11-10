using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EffectiveStatProjectionTests
{
    private static LocalAbilityDefinition Passive(string key, AbilityMechanic mechanic) =>
        new(key, "Shared presentation name", AbilityTiming.Passive, mechanic);

    private static GameState World()
    {
        var state = StateDetachmentTests.World();
        state.Types[0] = state.Types[0] with { CanHaveEquipment = true, LocalAbilities = [
            Passive("self", new SelfModifiersMechanic([new(Stat.Atk, 2), new(Stat.Mov, -9), new(Stat.Rng, -3)])),
            Passive("fury", new Fury { AdjacentEnemyThreshold = 1, AtkBonus = 3 })] };
        var provider = new UnitType("hidden-type", 2, 1, 1, 0, 1)
        { LocalAbilities = [Passive("aura", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies,
            [new(Stat.Def, 2), new(Stat.Atk, 1), new(Stat.Mov, 1)]))] };
        state.Types.Add(provider);
        state.Types.Add(new("enemy-type", 1, 1, 1, 1, 1));
        state.Units.AddRange([provider.CreateUnit("hidden-provider", "blue"),
            provider.CreateUnit("second-provider", "blue"), state.Types[2].CreateUnit("enemy", "red")]);
        state.Physical.Figures.AddRange([new("hidden-provider", new(3, 2)), new("second-provider", new(2, 3)), new("enemy", new(1, 2))]);
        state.EquipmentTypes = [new("test-gear", "Shared presentation name") { LocalAbilities = [
            Passive("self", new SelfModifiersMechanic([new(Stat.Atk, 4), new(Stat.Rng, 2)])),
            Passive("aura", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.All, [new(Stat.Def, -1)]))] }];
        state.Units[0] = state.Units[0] with { Inventory = [new("physical-gear", "test-gear")] };
        state.WorldCardDefinitions = [new("test-world", "Test world", WorldCardLifecycle.Continuous)
        { Modifiers = [new(WorldModifierTarget.Atk, -1), new(WorldModifierTarget.Def, -1), new(WorldModifierTarget.Mov, 2)] }];
        state.WorldDeck!.ActiveContinuous = [new("physical-world", "test-world")];
        state.CurrentUnitId = "actor";
        state.ModifiersThisTurn = [new(Stat.Atk, 2), new(Stat.Def, -1), new(Stat.Mov, 1), new(Stat.Rng, -1)];
        return state;
    }

    // Public gameplay methods remain the independent unoptimized oracle.
    private static (int Mov, int Rng, int Atk, int Def) Legacy(GameState state, string id) =>
        (state.EffectiveMovOf(id), state.EffectiveRngOf(id), state.EffectiveAtkOf(id), state.EffectiveDefOf(id));

    private static GameState AuraWorld(RelationshipSelector selector = RelationshipSelector.Friendlies)
    {
        var recipient = new UnitType("recipient-type", 3, 1, 2, 1, 1);
        var provider = new UnitType("provider-type", 3, 1, 2, 1, 1)
        { LocalAbilities = [Passive("aura", new AdjacentUnitsStatModifiersMechanic(selector, [new(Stat.Def, 2)]))] };
        return new()
        {
            Physical = new(new Board(8, 8, []), [new("recipient", new(2, 2)), new("provider", new(3, 2))]),
            Types = [recipient, provider],
            Units = [recipient.CreateUnit("recipient", "blue"), provider.CreateUnit("provider", "blue")]
        };
    }

    [Fact]
    public void ProjectionPreservesPerMechanicCheckedModifierSummation()
    {
        var state = AuraWorld();
        // Deliberately bypass content validation to exercise the query's existing
        // failure contract without allocating millions of valid modifiers.
        state.Types[0] = state.Types[0] with { LocalAbilities = [Passive("overflow",
            new SelfModifiersMechanic([new(Stat.Atk, int.MaxValue), new(Stat.Atk, 1)]))] };
        Assert.Throws<OverflowException>(() => state.EffectiveAtkOf("recipient"));
        Assert.Throws<OverflowException>(() => FogOfWar.Observe(state, null));
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    public void AuraProjectionUsesNormalLineOfSight(bool blocked, int expectedDef)
    {
        var state = AuraWorld();
        if (blocked) state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall));
        var observed = FogOfWar.Observe(state, null);
        Assert.Equal(expectedDef, state.EffectiveDefOf("recipient"));
        Assert.Equal(Legacy(state, "recipient"), Legacy(observed, "recipient"));
    }

    [Fact]
    public void MultiCellContactAppliesEachAuraOnceAndNeverToItsOwnFootprint()
    {
        var state = AuraWorld();
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        state.Physical.Figures[1] = new("provider", new(4, 2));
        var observed = FogOfWar.Observe(state, null);
        Assert.Equal(3, observed.EffectiveDefOf("recipient"));
        Assert.Equal(1, observed.EffectiveDefOf("provider"));
        foreach (var unit in state.Units) Assert.Equal(Legacy(state, unit.Id), Legacy(observed, unit.Id));
    }

    [Theory]
    [InlineData(RelationshipSelector.Enemies, "red", 3)]
    [InlineData(RelationshipSelector.Enemies, "neutral", 1)]
    [InlineData(RelationshipSelector.All, "neutral", 3)]
    [InlineData(RelationshipSelector.Friendlies, "neutral", 1)]
    public void AuraProjectionPreservesNeutralSideAndRelationshipSelectors(RelationshipSelector selector,
        string recipientSide, int expectedDef)
    {
        var state = AuraWorld(selector);
        state.NeutralSides = ["neutral"];
        state.Units[0] = state.Units[0] with { SideId = recipientSide };
        var observed = FogOfWar.Observe(state, null);
        Assert.Equal(expectedDef, observed.EffectiveDefOf("recipient"));
        foreach (var unit in state.Units) Assert.Equal(Legacy(state, unit.Id), Legacy(observed, unit.Id));
    }

    [Theory]
    [InlineData(Posture.Upright, 13)]
    [InlineData(Posture.Lying, 3)]
    public void HiddenEquipmentAuraAffectsLyingRecipientWithoutRevealingItsSource(Posture recipientPosture, int expectedDef)
    {
        var state = AuraWorld();
        var aura = state.Types[1].LocalAbilities;
        state.Types[0] = state.Types[0] with
        { LocalAbilities = [Passive("self", new SelfModifiersMechanic([new(Stat.Def, 10)]))] };
        state.Types[1] = state.Types[1] with { CanHaveEquipment = true, LocalAbilities = [] };
        state.EquipmentTypes = [new("secret-equipment", "Secret equipment") { LocalAbilities = aura }];
        state.Units[1] = state.Units[1] with { Inventory = [new("secret-physical-instance", "secret-equipment")] };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = recipientPosture };
        state.FogOfWarSides = ["blue"];
        state.ExploredCells = new() { ["blue"] = [new(2, 2)] };
        var observed = FogOfWar.Observe(state, "blue");
        Assert.Equal(expectedDef, observed.EffectiveDefOf("recipient"));
        Assert.Equal(Legacy(state, "recipient"), Legacy(observed, "recipient"));
        Assert.Equal("recipient", Assert.Single(observed.Units).Id);
        Assert.Empty(observed.EquipmentTypes);
        var json = JsonSerializer.Serialize(observed);
        Assert.DoesNotContain("secret-equipment", json);
        Assert.DoesNotContain("secret-physical-instance", json);
        Assert.DoesNotContain("provider", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectionMatchesGameplayWithStackedNativeEquipmentWorldTurnAndFuryEffects(bool reverse)
    {
        var state = World();
        if (reverse) { state.Units.Reverse(); state.Types.Reverse(); state.Physical.Figures.Reverse(); }
        var expected = state.Units.ToDictionary(u => u.Id, u => Legacy(state, u.Id));
        var observed = FogOfWar.Observe(state, null);
        foreach (var (id, values) in expected) Assert.Equal(values, Legacy(observed, id));
        Assert.Equal((0, -1, 14, 3), Legacy(observed, "actor"));
        var json = JsonSerializer.Serialize(observed);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(14, parsed.RootElement.GetProperty("EffectiveAtk").GetProperty("actor").GetInt32());
        Assert.DoesNotContain("ObservationValues", json);
        Assert.DoesNotContain("ObservationStatContext", json);
    }

    [Fact]
    public void HiddenProviderStatsSurviveDetachmentAndReobservationWithoutIdentityDisclosure()
    {
        var state = World();
        state.FogOfWarSides = ["blue"];
        var expected = Legacy(state, "actor");
        var observed = FogOfWar.Observe(state, "blue");
        Assert.Single(observed.Units);
        Assert.Equal(expected, Legacy(observed, "actor"));
        var copy = observed.DetachedCopy();
        var valuesProperty = typeof(GameState).GetProperty("ObservationValues",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.NotSame(valuesProperty.GetValue(observed), valuesProperty.GetValue(copy));
        Assert.Equal(expected, Legacy(copy, "actor"));
        Assert.Equal(expected, Legacy(FogOfWar.Observe(copy, "blue"), "actor"));
        var json = JsonSerializer.Serialize(copy);
        Assert.DoesNotContain("hidden-provider", json);
        Assert.DoesNotContain("hidden-type", json);
        Assert.DoesNotContain("second-provider", json);
        using var parsed = JsonDocument.Parse(json);
        foreach (var (property, value) in new[] { ("EffectiveMov", expected.Mov), ("EffectiveRng", expected.Rng),
            ("EffectiveAtk", expected.Atk), ("EffectiveDef", expected.Def) })
            Assert.Equal(value, parsed.RootElement.GetProperty(property).GetProperty("actor").GetInt32());
    }

    [Fact]
    public void MutationAndHypotheticalCopiesAlwaysUseFreshAuthoritativeContexts()
    {
        var state = World();
        var first = FogOfWar.Observe(state, null);
        var copy = state.DetachedCopy();
        copy.Physical.Figures[1] = copy.Physical.Figures[1] with { Posture = Posture.Lying };
        copy.Physical.Figures[2] = copy.Physical.Figures[2] with { Position = new(7, 7) };
        copy.Units[0] = copy.Units[0] with { Inventory = [] };
        Assert.Equal(Legacy(copy, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
        Assert.NotEqual(Legacy(first, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
        Assert.Equal(Legacy(first, "actor"), Legacy(FogOfWar.Observe(state, null), "actor"));
        copy.Units[3] = copy.Units[3] with { CurrentHp = 0 };
        copy.Physical.Figures.RemoveAt(3);
        Assert.Equal(Legacy(copy, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
        copy.Physical.Figures[1] = copy.Physical.Figures[1] with { Posture = Posture.Upright };
        copy.Units[1] = copy.Units[1] with { CurrentHp = 0 };
        Assert.Equal(Legacy(copy, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
        copy.Units[1] = copy.Units[1] with { CurrentHp = 1 };
        copy.Physical.Figures.RemoveAt(1);
        Assert.Equal(Legacy(copy, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
        copy.Physical.Figures[0] = copy.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(Legacy(copy, "actor"), Legacy(FogOfWar.Observe(copy, null), "actor"));
    }

    [Fact]
    public void RawStateSerializationRestoresDerivedStatsAndIgnoresUntrustedStatMaps()
    {
        var state = World();
        var json = JsonSerializer.Serialize(state);
        var supplied = JsonNode.Parse(json)!;
        foreach (var property in new[] { "EffectiveAtk", "EffectiveDef", "EffectiveMov", "EffectiveRng" })
            supplied[property]!["actor"] = 999;
        var restored = JsonSerializer.Deserialize<GameState>(supplied.ToJsonString())!;
        Assert.Equal(Legacy(state, "actor"), Legacy(restored, "actor"));
        restored.ModifiersThisTurn.Clear();
        Assert.NotEqual(Legacy(state, "actor"), Legacy(restored, "actor"));
    }
}
