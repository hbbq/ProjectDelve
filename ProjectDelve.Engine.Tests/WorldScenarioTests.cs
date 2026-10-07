using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldScenarioTests
{
    private static ScenarioDefinition Definition(WorldEffectsSettings? world = null) => Scenario.Define(
        Scenario.Map(4, 4), [Scenario.Group(UnitTypeIds.Barbarian, "red", ControllerKind.Human, Scenario.At(0, 0))],
        worldEffects: world);

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(3, 5)]
    public void JsonAndDelve1PreserveWorldSettingsAndCreateFreshPhysicalDeck(int draws, int cycling)
    {
        var definition = Definition(new(draws, cycling));
        var json = ScenarioDefinitionJson.ToJson(definition);
        var loaded = ScenarioDefinitionJson.FromJson(json);
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(loaded));
        Assert.Equal(definition.WorldEffects, decoded.WorldEffects);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(decoded));
        var first = GameEngine.CreateGame(decoded);
        var second = GameEngine.CreateGame(decoded);
        Assert.Equal(definition.WorldEffects, first.WorldEffects);
        Assert.Equal(40, first.WorldDeck!.DrawPile.Count);
        Assert.Empty(first.WorldDeck.ActiveContinuous); Assert.Empty(first.WorldDeck.DiscardPile);
        first.WorldDeck.DrawPile.Clear();
        Assert.Equal(40, second.WorldDeck!.DrawPile.Count);
    }

    [Fact]
    public void ExistingScenariosRemainDisabledWithoutAnyWorldEventsOrDeck()
    {
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Definition()));
        Assert.Null(definition.WorldEffects);
        var state = GameEngine.CreateGame(definition);
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Null(result.State.WorldDeck); Assert.Null(result.State.WorldEffects);
        Assert.DoesNotContain(result.Events, e => e.Kind.StartsWith("World", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, 1)] [InlineData(1, 0)] [InlineData(1, -1)]
    public void InvalidSettingsFailAtAllScenarioBoundariesWithAuthoritativePath(int draws, int cycling)
    {
        var definition = Definition(new(draws, cycling));
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        Assert.Equal("worldEffects", ScenarioValidationContext.PathOf(error));
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"cardsPerRound\":1}")]
    [InlineData("{\"cycling\":1}")]
    [InlineData("{\"cardsPerRound\":1.5,\"cycling\":1}")]
    public void EnabledWorldSettingsRequireBothIntegerConfigurationValues(string settings)
    {
        var json = ScenarioDefinitionJson.ToJson(Definition()).TrimEnd();
        json = json[..^1] + ",\"worldEffects\":" + settings + "}";
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(json));
    }

    [Fact]
    public void DeckLocationsAndSettingsSurviveFullGameStateSerialization()
    {
        var state = GameEngine.CreateGame(Definition(new(3, 2)));
        var result = GameEngine.StartRound(state, new Dice(), false);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        Assert.Equal(result.State.WorldEffects, restored.WorldEffects);
        Assert.Equal(result.State.WorldDeck!.DrawPile, restored.WorldDeck!.DrawPile);
        Assert.Equal(result.State.WorldDeck.DiscardPile, restored.WorldDeck.DiscardPile);
        Assert.Equal(result.State.WorldDeck.ActiveContinuous, restored.WorldDeck.ActiveContinuous);
        Assert.Null(restored.WorldDeck.ResolvingCard);
        restored.WorldDeck.ActiveContinuous.Clear();
        Assert.Equal(2, result.State.WorldDeck.ActiveContinuous.Count);
    }

    private sealed class Dice : IRandomProvider
    {
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
