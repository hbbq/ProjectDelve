using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentSnapshotProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureLootSummonDefinitionsStayInTheSaveAndOutOfGameplayResponses(bool fog)
    {
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 8, Height = 8 }, UnitTypeIds = ["actor", "secret-future-type"],
            UnitTypes = [new("actor", 1, 1, 1, 0, 1), new("secret-future-type", 2, 1, 2, 0, 1)],
            Units = [new("actor", "blue", new(0, 0))], Agency = [new("actor", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], FogOfWarSides = fog ? ["blue"] : [],
            EquipmentTypes = [new("secret-future-gear", "Concealed reward") { LocalAbilities =
                [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic("secret-future-type", Posture.Lying))] }],
            LootCardDefinitions = [new("secret-future-card", "Concealed card") { EquipmentTypeId = "secret-future-gear" }],
            Loot = new() { Composition = [new() { LootCardId = "secret-future-card" }] },
            LootMarkers = [new("unopened", new(7, 7))]
        };
        var state = GameEngine.CreateGame(scenario);
        Assert.Contains("secret-future-type", JsonSerializer.Serialize(state));
        using var game = new PlaytestGame(new Dice(), state);
        var browser = game.BrowserResponse(game.Snapshot());
        var browserJson = JsonSerializer.Serialize(browser);
        Assert.DoesNotContain("secret-future-type", browserJson);
        Assert.DoesNotContain("secret-future-gear", browserJson);
        Assert.DoesNotContain("secret-future-card", browserJson);
        var observation = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        Assert.DoesNotContain("secret-future-type", JsonSerializer.Serialize(observation));
        Assert.Contains(state.Types, type => type.Id == "secret-future-type");
    }
}
