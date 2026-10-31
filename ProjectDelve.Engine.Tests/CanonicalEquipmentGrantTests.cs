using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalEquipmentGrantTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["bearer"],
        UnitTypes = [new("bearer", 3, 1, 1, 1, 1) { CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("bearer", "blue", new(0, 0), UnitId: "actor") { Inventory = [new("bell-physical", "goat-bell")] },
            new("bearer", "blue", new(1, 0), Posture.Lying, UnitId: "friend")],
        Agency = [new("bearer", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition? scenario = null)
    {
        var state = GameEngine.CreateGame(scenario ?? Scenario());
        state.Round = 1; state.ActiveToken = new("bearer", "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "bearer", "actor", [], false);
        return state;
    }
    private static EngineResult Ring(GameState state) => GameEngine.Advance(state, new Choice("equipment:bell-physical:stand-up:friend"), new Random(), false);
    [Fact]
    public void CanonicalBellOwnsItsAbilityAndDerivesSourceIdentities()
    {
        var state = Ready();
        var equipment = state.Equipment("goat-bell");
        var ability = Assert.Single(equipment.LocalAbilities);
        Assert.Equal("stand-up", ability.Key);
        Assert.Equal(AbilityTiming.Action, ability.Timing); Assert.Null(ability.Usage.MaxUses);
        Assert.IsType<StandAdjacentFriendlyMechanic>(ability.Mechanic);
        var grant = Assert.Single(state.AbilitiesOf("actor"));
        Assert.Equal("equipment:bell-physical:stand-up", grant.CandidatePrefix); Assert.Equal("equipment:goat-bell:stand-up", grant.ActionId);
        Assert.Equal("equipment:goat-bell:stand-up", grant.EntryId);
        Assert.Equal("equipment:bell-physical:stand-up", grant.AccountingId);
        Assert.Equal("bell-physical", grant.SourceInstanceId);
        Assert.Equal("Action: Stand up one adjacent friendly Lying Unit.", state.ObjectContent(state.Units[0].Inventory[0]).Text);
    }

    [Fact]
    public void OwnedBellResumesTheSameChoiceAndSpendsOneAction()
    {
        var shared = Ready();
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(shared))!;
        var first = Ring(shared); var saved = Ring(resumed);
        Assert.Equal(first.Events, saved.Events);
        Assert.True(first.State.IsUpright("friend"));
        foreach (var result in new[] { first, saved })
        {
            var actionIndex = result.Events.FindIndex(e => e.Kind == "ActionUsed");
            var actionState = Assert.Single(result.ResolutionSteps.Where(s => s.EventIndex == actionIndex)).StateAfter;
            Assert.Equal("actor", actionState.CurrentUnitId);
            Assert.Equal(1, actionState.ActionsUsedThisActivation);
            Assert.Equal(0, actionState.ActionsRemaining);
        }
        Assert.Equal("bell-physical", Assert.Single(first.State.Units[0].Inventory).Id);
        Assert.Equal("equipment:goat-bell:stand-up", Assert.Single(first.Events.Where(e => e.Kind == "ActionUsed")).ActionId);
    }

    [Fact]
    public void RenamingCanonicalBellAndAbilityPreservesMechanicAndSourceKeys()
    {
        var state = Ready();
        var equipment = state.EquipmentTypes.FindIndex(d => d.Id == "goat-bell");
        state.EquipmentTypes[equipment] = state.EquipmentTypes[equipment] with
        {
            Name = "Silver Chime",
            LocalAbilities = [Assert.Single(state.EquipmentTypes[equipment].LocalAbilities) with { Name = "Rise Again" }]
        };
        var grant = Assert.Single(state.AbilitiesOf("actor"));
        Assert.Equal("Rise Again", grant.Definition.Name); Assert.Equal("Silver Chime", grant.SourceName);
        Assert.Equal("equipment:bell-physical:stand-up", grant.CandidatePrefix);
        var result = Ring(state);
        Assert.True(result.State.IsUpright("friend"));
        Assert.Equal("Rise Again", Assert.Single(result.Events.Where(e => e.Kind == "ActionUsed")).AbilityName);
    }

}
