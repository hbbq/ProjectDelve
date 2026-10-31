using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LocalAbilityResumeTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Ready(UnitType actor, bool refresh = true)
    {
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 2 }, UnitTypeIds = [actor.Id, "friend"],
            UnitTypes = [actor, new("friend", 0, 0, 0, 0, 5) { Unique = true }],
            Units = [new(actor.Id, "blue", new(0, 0), UnitId: "actor"),
                new("friend", "blue", new(1, 0), InitialHp: 1, UnitId: "friend")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        });
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false);
        return refresh ? GameEngine.RefreshChoices(state, new Dice(), false).State : state;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string choice) => GameEngine.Advance(state, new Choice(choice), new Dice(), false);

    [Fact]
    public void OwnedClericAbilitiesKeepGenericCountersAndPendingChoices()
    {
        var state = Ready(UnitRoster.Cleric("owned-cleric"));
        var typeIndex = state.Types.FindIndex(type => type.Id == "owned-cleric");
        state.Types[typeIndex] = state.Types[typeIndex] with
        {
            DisplayName = "Field Medic",
            LocalAbilities = [.. state.Types[typeIndex].LocalAbilities.Select(ability => ability.Key == "heal"
                ? ability with { Name = "Field Dressing" } : ability)]
        };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var restored = Restore(state);
        GameEngine.ValidateScenario(restored);
        Assert.Equal(state.Pending!.Candidates.Select(candidate => candidate.Key), restored.Pending!.Candidates.Select(candidate => candidate.Key));
        var heal = restored.AbilitiesOf("actor").Single(ability => ability.Key == "heal");
        Assert.Equal("Field Dressing", heal.Definition.Name);
        Assert.Equal("Field Medic", heal.SourceName);
        Assert.Equal(("ability:heal", "ability:heal", "unit:heal"), (heal.EntryId, heal.CandidatePrefix, heal.AccountingId));
        Assert.Equal(new AbilityUses(2, 2), restored.AbilityUsesOf("actor", heal));
        Assert.Equal(1, restored.EffectiveDefOf("friend"));

        var result = Use(restored, "ability:heal:friend");
        Assert.Equal(3, result.State.Units.Single(unit => unit.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 1), result.State.AbilityUsesOf("actor", heal));
        Assert.Equal(1, result.State.Units[0].AbilityUseCounts["unit:heal"]);
        var after = Restore(result.State);
        GameEngine.ValidateScenario(after);
        Assert.Equal(new AbilityUses(2, 1), after.AbilityUsesOf("actor", heal));
        Assert.Equal("Field Dressing", after.Types[typeIndex].CardEntries()
            .Single(entry => entry.Id == "ability:heal").Name);
    }

    [Fact]
    public void OwnedSecondWindKeepsPhysicalConsumptionAllowanceAndProjectionAcrossResume()
    {
        var state = Ready(new UnitType("bearer", 1, 1, 0, 0, 4, Actions: UnitAction.None) { Unique = true, CanHaveItems = true }, refresh: false);
        state.Units[0] = state.Units[0] with
        { Inventory = [new("wind-one", LootCardKind.SecondWind), new("wind-two", LootCardKind.SecondWind)] };
        var itemIndex = state.ItemTypes.FindIndex(item => item.Id == "second-wind");
        state.ItemTypes[itemIndex] = state.ItemTypes[itemIndex] with
        {
            Name = "Fresh Supplies",
            LocalAbilities = [Assert.Single(state.ItemTypes[itemIndex].LocalAbilities) with { Name = "Second Breath" }]
        };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        Assert.Contains("Gain +1 Action", state.ObjectContent(state.Units[0].Inventory[0]).Text);
        var grants = state.AbilitiesOf("actor").Where(ability => ability.SourceKind == AbilitySourceKind.Item).ToArray();
        Assert.Equal(2, grants.Length);
        Assert.All(grants, grant =>
        {
            Assert.Equal("item:second-wind:use", grant.AccountingId);
            Assert.Equal("item:second-wind:use", grant.EntryId);
            Assert.Equal("Second Breath", grant.Definition.Name);
            Assert.Equal("Fresh Supplies", grant.SourceName);
        });
        Assert.Equal(new[] { "wind-one", "wind-two" }, grants.Select(grant => grant.SourceInstanceId));

        var resumed = Restore(state);
        GameEngine.ValidateScenario(resumed);
        var expected = Use(state, "item:wind-one:use");
        var actual = Use(resumed, "item:wind-one:use");
        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal("wind-two", Assert.Single(actual.State.Units[0].Inventory).Id);
        Assert.Equal("wind-one", Assert.Single(actual.State.LootDeck!.DiscardPile).Id);
        var used = Assert.Single(actual.Events.Where(gameEvent => gameEvent.Kind == "ItemUsed"));
        Assert.Equal("item:second-wind:use", used.ActionId);
        Assert.Equal("Second Breath", used.AbilityName);
        var useIndex = actual.Events.IndexOf(used);
        var committed = Assert.Single(actual.ResolutionSteps.Where(step => step.EventIndex == useIndex)).StateAfter;
        Assert.Equal(1, committed.BonusActionUseCountsThisActivation["item:second-wind:use"]);
        Assert.False(committed.CanUseAbility("actor", Assert.Single(committed.AbilitiesOf("actor"))));
    }

}
