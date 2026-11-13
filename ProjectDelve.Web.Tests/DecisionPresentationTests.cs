using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class DecisionPresentationTests
{
    [Theory]
    [InlineData(ActivationChoiceKind.Move, "Choose a destination or use an available ability.")]
    [InlineData(ActivationChoiceKind.Action, "Choose an available Action or use an available ability.")]
    [InlineData(ActivationChoiceKind.EndTurn, "Use an available ability or end the turn.")]
    public void PromptDescribesAvailableKindsWithoutImplyingTheirOrder(ActivationChoiceKind kind, string expected)
    {
        var state = State();
        var bonus = state.AbilitiesOf("actor").Single(a => a.Definition.Key == "bonus");
        var request = new DecisionRequest(DecisionKind.Activation, "fixture", "actor",
            [new("opaque-choice", Kind: kind, Destination: kind == ActivationChoiceKind.Move ? new(1, 0) : null),
             new("opaque-bonus", Kind: ActivationChoiceKind.BonusAction) { Ability = bonus }], false);
        var before = JsonSerializer.Serialize(request);

        var decision = BrowserProjection.Decision(request, state)!;

        Assert.Equal(expected, decision.DisplayPrompt);
        Assert.Equal("Choose an activation choice", decision.Prompt);
        Assert.Equal(request.Candidates.Select(c => c.Key), decision.Candidates.Select(c => c.Key));
        Assert.Equal(before, JsonSerializer.Serialize(request));
    }

    [Fact]
    public void StatusesUseSourceAccountingAndKeepIrrelevantChoicesLegal()
    {
        var state = State();
        var equipment = new EquipmentDefinition("fixture-lantern", "Lantern")
        { LocalAbilities = [new("bonus", "Shared Name", AbilityTiming.BonusAction, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1))] };
        state.EquipmentTypes.Add(equipment);
        state.Units[0] = state.Units[0] with { Inventory = [new("lantern-copy", equipment.Id)] };
        var abilities = state.AbilitiesOf("actor").ToArray();
        var native = abilities.Single(a => a.SourceKind == AbilitySourceKind.Unit && a.Definition.Key == "bonus");
        var acquired = abilities.Single(a => a.SourceKind == AbilitySourceKind.Equipment);
        var spent = abilities.Single(a => a.Definition.Key == "limited");
        state.Units[0] = state.Units[0] with { AbilityUseCounts = state.Units[0].AbilityUseCounts.Add(spent.AccountingId, 2) };
        var request = new DecisionRequest(DecisionKind.Activation, "fixture", "actor",
            [new("native", Kind: ActivationChoiceKind.BonusAction, Relevant: false) { Ability = native },
             new("acquired", Kind: ActivationChoiceKind.BonusAction) { Ability = acquired }], false);
        var before = JsonSerializer.Serialize(state);

        var decision = BrowserProjection.Decision(request, state)!;

        Assert.Equal(new(BrowserEntryAvailability.Irrelevant, "Legal, but not relevant now"), decision.EntryStatuses[native.AccountingId]);
        Assert.Equal(BrowserEntryAvailability.Available, decision.EntryStatuses[acquired.AccountingId].Status);
        Assert.Equal(new(BrowserEntryAvailability.Exhausted, "No uses remaining"), decision.EntryStatuses[spent.AccountingId]);
        Assert.Equal(new(BrowserEntryAvailability.Unavailable, "Not available at this step"), decision.EntryStatuses["attack"]);
        Assert.DoesNotContain("ability:passive", decision.EntryStatuses.Keys);
        Assert.Equal(2, decision.Candidates.Count);
        Assert.False(decision.Candidates[0].Relevant);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void ActivationUseReasonRequiresTheCurrentUnitAndAnActuallySpentUse()
    {
        var state = State();
        var bonus = state.AbilitiesOf("actor").Single(a => a.Definition.Key == "bonus");
        var request = new DecisionRequest(DecisionKind.Activation, "fixture", "actor", [], false);
        state.BonusActionUseCountsThisActivation[bonus.AccountingId] = 1;
        state.CurrentUnitId = "actor";
        Assert.Equal(new(BrowserEntryAvailability.Exhausted, "Used this activation") { UsedThisActivation = true },
            BrowserProjection.Decision(request, state)!.EntryStatuses[bonus.AccountingId]);

        state.CurrentUnitId = null;
        Assert.Equal(new(BrowserEntryAvailability.Unavailable, "Not available at this step"),
            BrowserProjection.Decision(request, state)!.EntryStatuses[bonus.AccountingId]);
    }

    [Fact]
    public void WorldBonusAllowanceAndRemainingUsesStayDistinct()
    {
        var state = State();
        state.CurrentUnitId = "actor";
        state.WorldCardDefinitions = [
            new("extra-bonus", "Extra Bonus", WorldCardLifecycle.Continuous)
            { Modifiers = [new(WorldModifierTarget.BonusActionUses, 1)] },
            new("fewer-bonuses", "Fewer Bonuses", WorldCardLifecycle.Continuous)
            { Modifiers = [new(WorldModifierTarget.BonusActionUses, -1)] }];
        state.WorldDeck = new() { ActiveContinuous = [new("extra-bonus-copy", "extra-bonus")] };
        var bonus = state.AbilitiesOf("actor").Single(a => a.Definition.Key == "bonus");
        state.BonusActionUseCountsThisActivation[bonus.AccountingId] = 1;
        var request = new DecisionRequest(DecisionKind.Activation, "fixture", "actor",
            [new("bonus-again", Kind: ActivationChoiceKind.BonusAction) { Ability = bonus }], false);
        Assert.Equal(BrowserEntryAvailability.Available,
            BrowserProjection.Decision(request, state)!.EntryStatuses[bonus.AccountingId].Status);

        state.BonusActionUseCountsThisActivation[bonus.AccountingId] = 2;
        Assert.Equal(new(BrowserEntryAvailability.Exhausted, "Used this activation") { UsedThisActivation = true },
            BrowserProjection.Decision(request with { Candidates = [] }, state)!.EntryStatuses[bonus.AccountingId]);

        state.WorldDeck = new() { ActiveContinuous = [new("fewer-bonuses-copy", "fewer-bonuses")] };
        state.BonusActionUseCountsThisActivation.Clear();
        Assert.Equal(new(BrowserEntryAvailability.Unavailable, "Not available at this step"),
            BrowserProjection.Decision(request with { Candidates = [] }, state)!.EntryStatuses[bonus.AccountingId]);
    }

    [Fact]
    public void OptionalActionPromptIncludesTakingNoAction()
    {
        var state = State();
        var request = new DecisionRequest(DecisionKind.Act, "fixture", "actor",
            [new("opaque-attack", Action: UnitAction.NormalAttack)], true);

        var projected = BrowserProjection.Decision(request, state)!;

        Assert.Equal("Choose an available Action or take no action.", projected.DisplayPrompt);
        Assert.Equal("Take no action", projected.NoneChoice!.Label);
    }

    [Fact]
    public void MoveDecisionIncludesOptionalStayAndUnitSelectionHasNoCardStatuses()
    {
        var state = State();
        var move = new DecisionRequest(DecisionKind.Move, "fixture", "actor",
            [new("move", Destination: new(1, 0), Kind: ActivationChoiceKind.Move)], true);
        Assert.Equal("Choose a destination or stay here.", BrowserProjection.Decision(move, state)!.DisplayPrompt);
        var selection = new DecisionRequest(DecisionKind.SelectUnit, "fixture", null,
            [new("actor", Kind: ActivationChoiceKind.SelectUnit)], false);
        var projected = BrowserProjection.Decision(selection, state)!;
        Assert.Equal("Choose a Unit", projected.DisplayPrompt);
        Assert.Empty(projected.EntryStatuses);
    }

    private static GameState State()
    {
        var type = new UnitType("fixture", 1, 1, 1, 1, 3)
        {
            LocalAbilities = [
                new("bonus", "Shared Name", AbilityTiming.BonusAction, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1)),
                new("limited", "Limited Remedy", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2)) { Usage = new(MaxUses: 2) },
                new("passive", "Steady", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
        };
        return new GameState
        {
            Physical = new(new Board(2, 1, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")]
        };
    }
}
