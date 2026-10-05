using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class BrowserProjectionTests
{
    [Fact]
    public void GenericPostureEventsAndPhysicalSnapshotsSerializeWithoutAbilityKnowledge()
    {
        var state = State();
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        var outcome = new RulesEvent("PostureChanged", "target", Posture: Posture.Lying);
        var result = new EngineResult(state, [outcome], null) { ResolutionSteps = [new(0, state)] };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new GameResponse(0, result), options));
        var raw = json.RootElement.GetProperty("result");
        Assert.Equal("Lying", raw.GetProperty("state").GetProperty("physical").GetProperty("figures")[1].GetProperty("posture").GetString());
        Assert.Equal("Lying", raw.GetProperty("resolutionSteps")[0].GetProperty("stateAfter").GetProperty("physical").GetProperty("figures")[1].GetProperty("posture").GetString());
        Assert.Equal("target: Lying", BrowserProjection.Create(result).Events[0].Text);
        Assert.Equal(OutcomeRole.Notice, BrowserProjection.Create(result).Events[0].Role);
    }

    private sealed class Dice : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Fact]
    public void FireballProjectsDomainCardCellBindingAuthoritativeTargetsAndProgressiveCounters()
    {
        var state = State(UnitType.Wizard());
        state.Units[1] = state.Units[1] with { SideId = "hostile" };
        var started = GameEngine.StartRound(state, new Dice(), false);
        var action = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var presentation = BrowserProjection.Create(action);
        var card = presentation.Cards["actor"].Entries.Single(e => e.Content.Id == "fireball");
        Assert.Equal(new AbilityUses(2, 2), card.Uses);
        Assert.Equal("Choose a Cell within RNG and LOS.\nAttack all Units on or adjacent to that Cell.", card.Content.Description);
        var fireball = presentation.Decision!.Candidates.Single(c => c.Key == "fireball:4,0");
        var attack = presentation.Decision.Candidates.Single(c => c.Key == "attack:target");
        Assert.Equal(InteractionKind.Position, fireball.Interaction.Kind);
        Assert.Equal(new Cell(4, 0), fireball.Interaction.Position);
        Assert.Equal(InteractionKind.Unit, attack.Interaction.Kind);
        Assert.Equal("target", attack.Interaction.UnitId);
        Assert.Equal(new[] { "target" }, fireball.AffectedUnitIds);
        Assert.Equal("Fireball (Action) → (4,0)", fireball.Label);
        Assert.Empty(presentation.Decision.Candidates.Single(c => c.Key == "fireball:2,0").AffectedUnitIds);
        var result = GameEngine.Advance(action.State, new Choice(fireball.Key!), new Dice(), false);
        var resolved = BrowserProjection.Create(result);
        Assert.Equal(OutcomeRole.AttackSummary, resolved.Events.Last(e => e.Role == OutcomeRole.AttackSummary).Role);
        Assert.All(resolved.ResolutionSteps, s => Assert.Equal(1,
            s.Cards["actor"].Entries.Single(e => e.Content.Id == "fireball").Uses!.RemainingUses));
        Assert.Contains(result.ResolutionSteps[0].StateAfter.Physical.Figures, f => f.Id == "target");
        Assert.DoesNotContain(result.ResolutionSteps[1].StateAfter.Physical.Figures, f => f.Id == "target");
    }

    [Fact]
    public void PositionProjectionCopiesSuppliedAffectedUnitsWithoutInferringExplosion()
    {
        var state = State(UnitType.Wizard());
        var request = new DecisionRequest(DecisionKind.Activation, state.Types[0].Id, "actor",
            [new("opaque cell", Destination: new(4, 0), Action: UnitAction.Fireball)
                { TargetIds = ["not-on-board", "actor"] }], false);
        var projected = BrowserProjection.Decision(request, state)!.Candidates[0];
        Assert.Equal(new[] { "not-on-board", "actor" }, projected.AffectedUnitIds);
        Assert.Equal("opaque cell", projected.Key);
        Assert.Equal("fireball", projected.EntryId);
    }

    private static GameState State(UnitType? type = null)
    {
        type ??= UnitType.Cleric();
        return new()
        {
            Physical = new(new(5, 1, []), [new("actor", new(0, 0)), new("target", new(4, 0))]),
            Types = [type, UnitType.Grunt()],
            Units = [type.CreateUnit("actor", "same"), UnitType.Grunt().CreateUnit("target", "same")]
        };
    }

    [Fact]
    public void CardsProjectDescriptionsAndCurrentCountersWithoutMutatingState()
    {
        foreach (var type in new[] { UnitType.Cleric(), UnitType.Barbarian(), UnitType.Rogue(), UnitType.Goblin() })
        {
            var state = State(type);
            state.Units[0] = state.Units[0] with
            {
                HealUses = type.Heal is null ? null : new(2, 0),
                HolyWaveUses = type.HolyWave is null ? null : new(2, 1),
                CleaveUses = type.Cleave is null ? null : new(2, 0),
                BonusActionUses = type.BonusActions.ToImmutableDictionary(a => a.Name, a => new AbilityUses(a.MaxUses, 1))
            };
            var card = BrowserProjection.Cards(state)["actor"];
            Assert.Equal(type.DisplayName, card.DisplayName);
            Assert.Equal(type.CardEntries(), card.Entries.Select(e => e.Content).ToArray());
            foreach (var entry in card.Entries)
                Assert.Equal(type.UsesFor(state.Units[0], entry.Content.Id), entry.Uses);
            if (type.HolyWave is not null) Assert.Equal(new AbilityUses(2, 1), card.Entries.Single(e => e.Content.Id == "holy-wave").Uses);
            if (type.Heal is not null) Assert.Equal(new AbilityUses(2, 0), card.Entries.Single(e => e.Content.Id == "heal").Uses);
            if (type.Cleave is not null) Assert.Equal(new AbilityUses(2, 0), card.Entries.Single(e => e.Content.Id == "cleave").Uses);
            Assert.Null(card.Entries.Single(e => e.Content.Id == "attack").Uses);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InteractionsAndAffectedMembershipSerializeFromAuthoritativeCandidates(int targetCount)
    {
        var state = State();
        var door = new Edge(new(0, 0), new(1, 0), EdgeKind.ClosedDoor);
        var targets = new[] { "target", "not-on-board" }.Take(targetCount).ToImmutableArray();
        var request = new DecisionRequest(DecisionKind.Activation, state.Types[0].Id, "actor", [
            new("opaque direct", Action: UnitAction.HolyWave) { TargetIds = targets },
            new("opaque unit", Action: UnitAction.Heal, TargetId: "target"),
            new("opaque position", Destination: new(3, 0), Kind: ActivationChoiceKind.Move),
            new("opaque door", Door: door, Kind: ActivationChoiceKind.FreeAction, FreeAction: UnitFreeAction.OpenDoor)
        ], false);
        // Same-side full-HP distant targets deliberately contradict ability legality.
        var result = new EngineResult(state, [], request);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new GameResponse(9, result), options));
        var projected = json.RootElement.GetProperty("presentation").GetProperty("decision").GetProperty("candidates");
        Assert.Equal(new[] { "Direct", "Unit", "Position", "Door" }, projected.EnumerateArray().Select(c => c.GetProperty("interaction").GetProperty("kind").GetString()));
        Assert.Equal(targets, projected[0].GetProperty("affectedUnitIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("opaque direct", projected[0].GetProperty("key").GetString());
        Assert.Equal("holy-wave", projected[0].GetProperty("entryId").GetString());
        Assert.Equal("target", projected[1].GetProperty("interaction").GetProperty("unitId").GetString());
        Assert.Equal(3, projected[2].GetProperty("interaction").GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(1, projected[3].GetProperty("interaction").GetProperty("door").GetProperty("b").GetProperty("x").GetInt32());
        Assert.Null(BrowserProjection.Decision(request, state)!.NoneChoice);
    }

    [Fact]
    public void OptionalChoicesAndUnitSelectionHaveExplicitReferencesAndWording()
    {
        var state = State(UnitType.Barbarian());
        var cleave = BrowserProjection.Decision(new(DecisionKind.Cleave, state.Types[0].Id, "actor",
            [new("opaque", TargetId: "target", Kind: ActivationChoiceKind.Cleave)], true), state)!;
        Assert.Equal("Decline Cleave", cleave.NoneChoice!.Label);
        Assert.Null(cleave.NoneChoice.Key);
        Assert.Equal(InteractionKind.Unit, cleave.Candidates[0].Interaction.Kind);
        var move = BrowserProjection.Decision(new(DecisionKind.Move, state.Types[0].Id, "actor", [], true, true), state)!;
        Assert.Equal("Stay here", move.NoneChoice!.Label);
        Assert.Equal("actor", move.NoneChoice.Interaction.UnitId);
        var select = BrowserProjection.Decision(new(DecisionKind.SelectUnit, state.Types[0].Id, null,
            [new("actor", Kind: ActivationChoiceKind.SelectUnit)], false), state)!;
        Assert.Equal("actor", select.Candidates[0].Interaction.UnitId);
    }

    [Fact]
    public void UnfamiliarBonusNameMatchesItsEntryAndPreservesIrrelevantChoice()
    {
        var ability = new BonusActionAbility("Unknown name", 4, [new(Stat.Mov, 2)]);
        var state = State(UnitType.Grunt("unfamiliar-type") with { BonusActions = [ability] });
        var candidate = new Candidate("unparsed:opaque", Kind: ActivationChoiceKind.BonusAction, BonusAction: ability, Relevant: false);
        var choice = BrowserProjection.Decision(new(DecisionKind.Activation, state.Types[0].Id, "actor", [candidate], false), state)!.Candidates[0];
        Assert.Equal(BrowserProjection.Cards(state)["actor"].Entries.Single(e => e.Content.Name == ability.Name).Content.Id, choice.EntryId);
        Assert.Equal(InteractionKind.Direct, choice.Interaction.Kind);
        Assert.False(choice.Relevant);
        Assert.Equal(candidate.Key, choice.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unfamiliar attack")]
    public void OutcomeRolesUseStructureAndPreserveProgressiveCards(string? name)
    {
        var before = State();
        var after = State();
        after.Units[0] = after.Units[0] with { HolyWaveUses = new(2, 0), CurrentHp = 1 };
        var aggregate = new RulesEvent("AttackResolved", "actor", AbilityName: name,
            Attack: new(2, 1, [new("target", 3, 0, 1)]));
        var result = new EngineResult(after, [aggregate,
            new("CleaveResolved", "actor", "target", Damage: 1, AbilityName: "Different damage ability"),
            new("HealResolved", "actor", "target", Healing: 2, AbilityName: "Different healing ability")], null)
        { ResolutionSteps = [new(0, before), new(2, after)] };
        var presentation = BrowserProjection.Create(result);
        Assert.Equal(new[] { OutcomeRole.AttackSummary, OutcomeRole.Damage, OutcomeRole.Healing }, presentation.Events.Select(e => e.Role));
        Assert.Contains("2 Attack Dice, 1 shared Hits", presentation.Events[0].Text);
        Assert.Equal(2, presentation.ResolutionSteps[0].Cards["actor"].Entries.Single(e => e.Content.Id == "holy-wave").Uses!.RemainingUses);
        Assert.Equal(0, presentation.ResolutionSteps[1].Cards["actor"].Entries.Single(e => e.Content.Id == "holy-wave").Uses!.RemainingUses);
        Assert.Equal(new[] { 0, 2 }, presentation.ResolutionSteps.Select(s => s.EventIndex));
        Assert.Equal(OutcomeRole.AttackTarget, BrowserProjection.Outcome(aggregate with { TargetId = "target" }, after).Role);
    }
}
