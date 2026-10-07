using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class BrowserProjectionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DragonActionsUseGenericCardsDirectAndUnitChoicesWithAuthoritativeTargets(bool aliases)
    {
        var dragon = UnitType.RedDragon();
        if (aliases) dragon = dragon with { AbilityNames = new() { FireBreath = "Embers", ClawAttack = "Slash" } };
        var enemy = new UnitType("enemy", 0, 0, 0, 0, 1);
        var state = new GameState
        {
            Physical = new(new Board(8, 6, []), [new("dragon", new(1, 1)), new("a", new(3, 1)), new("b", new(1, 4))]),
            Types = [dragon, enemy], Units = [dragon.CreateUnit("dragon", "red"), enemy.CreateUnit("a", "blue"), enemy.CreateUnit("b", "blue")],
            Round = 1, ActiveToken = new(dragon.Id, "red"), CurrentUnitId = "dragon", MoveDone = true
        };
        var request = GameEngine.RefreshChoices(state, new Dice(), false).NextInput!;
        var candidates = request.Candidates;
        var presentation = BrowserProjection.Decision(request, state)!;
        var cards = BrowserProjection.Cards(state)["dragon"].Entries.ToDictionary(e => e.Content.Id);
        Assert.Equal("Attack all enemies within RNG.", cards["fire-breath"].Content.Description);
        Assert.Equal("Attack an adjacent enemy with ATK +1.", cards["claw-attack"].Content.Description);
        Assert.Null(cards["fire-breath"].Uses); Assert.Null(cards["claw-attack"].Uses);
        var breath = Assert.Single(presentation.Candidates, c => c.EntryId == "fire-breath");
        Assert.Equal(InteractionKind.Direct, breath.Interaction.Kind);
        Assert.Equal(new[] { "a", "b" }, breath.AffectedUnitIds);
        Assert.Contains(aliases ? "Embers" : "Fire Breath", breath.Label);
        Assert.True(breath.Relevant);
        var claw = Assert.Single(presentation.Candidates, c => c.EntryId == "claw-attack");
        Assert.Equal(new ChoiceInteraction(InteractionKind.Unit, UnitId: "a"), claw.Interaction);
        Assert.Equal(new[] { "a" }, claw.AffectedUnitIds);
        Assert.Contains(aliases ? "Slash" : "Claw Attack", claw.Label);
        Assert.Equal(candidates.Single(c => c.Action == UnitAction.ClawAttack).Key, claw.Key);
    }

    [Fact]
    public void SummonCardsUseConfiguredTypeNamePostureAndTypedChoiceDespitePrintedAlias()
    {
        var summoner = UnitType.Define("caller", "Caller", UnitAuthoring.Stats(2, 0, 0, 3, 1),
            UnitAuthoring.CantAttack(),
            UnitAuthoring.Ability("Call Sage", UnitAuthoring.Unlimited(),
                UnitAuthoring.SummonAdjacent(UnitTypeIds.Wizard, Posture.Upright)));
        var sage = UnitType.Wizard() with { DisplayName = "Sage" };
        var state = new GameState
        {
            Physical = new(new Board(3, 3, []), [new("actor", new(1, 1))]),
            Types = [summoner, sage], Units = [summoner.CreateUnit("actor", "red")]
        };
        state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var entry = Assert.Single(BrowserProjection.Cards(state)["actor"].Entries);
        Assert.Equal("Call Sage", entry.Content.Name);
        Assert.Equal("Action", entry.Content.Category);
        Assert.Equal("Place one Upright Sage in an adjacent empty Cell.", entry.Content.Description);
        Assert.Null(entry.Content.MaxUses);
        Assert.Null(entry.Uses);
        var request = new DecisionRequest(DecisionKind.Activation, summoner.Id, "actor",
            [new("opaque-summon-choice", Destination: new(0, 0), Action: UnitAction.SummonAdjacent)], false);
        var choice = Assert.Single(BrowserProjection.Decision(request, state)!.Candidates);
        Assert.Equal("spawn-goblin", choice.EntryId);
        Assert.Equal("opaque-summon-choice", choice.Key);
        Assert.Equal(new ChoiceInteraction(InteractionKind.Position, Position: new(0, 0)), choice.Interaction);
        Assert.Contains("Call Sage (Action)", choice.Label);
    }

    [Fact]
    public void ConfiguredMechanicValuesReachBrowserCardsThroughDomainDescriptions()
    {
        var type = UnitType.Define("unfamiliar", "Unfamiliar", UnitAuthoring.Stats(1, 1, 3, 2, 4),
            UnitAuthoring.Unique(),
            UnitAuthoring.CantAttack(),
            UnitAuthoring.Ability("Heal", UnitAuthoring.Uses(2), UnitAuthoring.Heal(amount: 3)),
            UnitAuthoring.Ability("Fury", UnitAuthoring.Unlimited(), UnitAuthoring.Fury(atkBonus: 2, adjacentEnemies: 3)),
            UnitAuthoring.Ability("Backstab", UnitAuthoring.Unlimited(), UnitAuthoring.Backstab(atkBonus: 4)),
            UnitAuthoring.Ability("Cleave", UnitAuthoring.Uses(2), UnitAuthoring.Cleave(triggerDamage: 4, damage: 2)));
        var state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State(type)))!;
        var entries = BrowserProjection.Cards(state)["actor"].Entries.ToDictionary(e => e.Content.Id);
        Assert.Equal("Heal an adjacent damaged friendly Unit for 3 HP.", entries["heal"].Content.Description);
        Assert.Equal("ATK +2 while adjacent to 3 or more enemies", entries["passive:Fury"].Content.Description);
        Assert.Equal("+4 ATK when attacking an enemy adjacent to another friendly Unit", entries["passive:Backstab"].Content.Description);
        Assert.Equal("After this Unit's Attack, if it dealt 4 or more damage to a Unit, you may immediately deal 2 damage to an adjacent enemy.", entries["cleave"].Content.Description);
        Assert.Equal(new AbilityUses(2, 2), entries["heal"].Uses);
        Assert.Equal(new AbilityUses(2, 2), entries["cleave"].Uses);
        Assert.Equal(type.CardEntries(), entries.Values.Select(e => e.Content));
    }

    [Fact]
    public void AuthoredBonusDisplayNameLeavesBrowserEntryAndCandidateIdentityStable()
    {
        var type = UnitType.Define("named", "Named", UnitAuthoring.Stats(1, 1, 3, 2, 4),
            UnitAuthoring.Unique(),
            UnitAuthoring.Ability("Battle Cry", UnitAuthoring.Uses(3),
                UnitAuthoring.BonusActionSelfModifier(UnitAuthoring.Modifier(Stat.Atk, 2)), id: "stable-key"));
        var state = State(type);
        var ability = type.BonusActions[0];
        var candidate = new Candidate("bonus-action:stable-key", Kind: ActivationChoiceKind.BonusAction, BonusAction: ability);
        var request = new DecisionRequest(DecisionKind.Activation, type.Id, "actor", [candidate], false);
        var card = Assert.Single(BrowserProjection.Cards(state)["actor"].Entries, e => e.Content.Id == "bonus:stable-key");
        var choice = Assert.Single(BrowserProjection.Decision(request, state)!.Candidates);
        Assert.Equal("Battle Cry", card.Content.Name);
        Assert.Equal(new AbilityUses(3, 3), card.Uses);
        Assert.Equal("bonus:stable-key", choice.EntryId);
        Assert.Equal(candidate.Key, choice.Key);
        Assert.Contains("Battle Cry", choice.Label);
    }

    [Fact]
    public void SpawnUsesGenericPositionSelectionAndProgressiveOrdinaryGoblinCard()
    {
        var shaman = UnitType.Shaman();
        var state = new GameState
        {
            Physical = new(new Board(3, 3, []), [new("actor", new(1, 1))]),
            Types = [shaman], Units = [shaman.CreateUnit("actor", "red")]
        };
        var started = TestGame.StartRound(state, new Dice(), false);
        var action = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
        var decision = BrowserProjection.Create(action).Decision!;
        var spawn = decision.Candidates.Single(c => c.Key == "spawn-goblin:0,0");
        Assert.Equal("spawn-goblin", spawn.EntryId);
        Assert.Equal(new ChoiceInteraction(InteractionKind.Position, Position: new(0, 0)), spawn.Interaction);
        Assert.Contains("Summon Goblin (Action)", spawn.Label);
        Assert.Empty(spawn.AffectedUnitIds);
        Assert.Equal("spawn-goblin", Assert.Single(BrowserProjection.Cards(action.State)["actor"].Entries).Content.Id);
        var result = TestGame.Advance(action.State, new Choice(spawn.Key!), new Dice(), false);
        var presentation = BrowserProjection.Create(result);
        var step = Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == "UnitCreated");
        var goblin = Assert.Single(step.StateAfter.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(Posture.Lying, step.StateAfter.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        var card = presentation.ResolutionSteps.Single(s => s.EventIndex == step.EventIndex).Cards[goblin.Id];
        Assert.Equal("Goblin", card.DisplayName);
        Assert.Equal(UnitType.Goblin().CardEntries(), card.Entries.Select(e => e.Content));
    }

    [Fact]
    public void TrollCardAndLethalOutcomeUseGenericContentAndAuthoritativePostureProjection()
    {
        var wizard = UnitType.Wizard();
        var troll = UnitType.Troll();
        var state = new GameState
        {
            Physical = new(new Board(3, 2, []), [new("actor", new(0, 0)), new("target", new(1, 0))]),
            Types = [wizard, troll], Units = [wizard.CreateUnit("actor", "blue"), troll.CreateUnit("target", "red")]
        };
        var started = TestGame.StartRound(state, new Dice(), false);
        var action = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
        var result = TestGame.Advance(action.State, new Choice("attack:target"), new Dice(), false);
        var presentation = BrowserProjection.Create(result);
        var card = presentation.Cards["target"];
        Assert.Equal("Troll", card.DisplayName);
        var capability = Assert.Single(card.Entries, e => e.Content.Id == "undying");
        Assert.Equal("Undying", capability.Content.Name);
        Assert.Equal("Capability", capability.Content.Category);
        Assert.Null(capability.Uses);
        Assert.Contains("4 of 6", card.Entries.Single(e => e.Content.Id == "try-open-door").Content.Description);
        Assert.Equal(new[] { "AttackResolved", "PostureChanged" }, TestGame.OperationEvents(result).Select(e => e.Kind));
        Assert.Equal("target: Lying", presentation.Events.Last().Text);
        Assert.All(result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind is "AttackResolved" or "PostureChanged"), step =>
        {
            Assert.Equal(1, step.StateAfter.Units[1].CurrentHp);
            Assert.Equal(new Figure("target", new(1, 0), Posture.Lying), step.StateAfter.Physical.Figures[1]);
        });
        Assert.All(presentation.ResolutionSteps, step =>
        {
            Assert.Equal(card.DisplayName, step.Cards["target"].DisplayName);
            Assert.Equal(card.Entries, step.Cards["target"].Entries);
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new GameResponse(1, result), options));
        foreach (var step in json.RootElement.GetProperty("result").GetProperty("resolutionSteps").EnumerateArray().Where(s => result.Events[s.GetProperty("eventIndex").GetInt32()].Kind is "AttackResolved" or "PostureChanged"))
        {
            var after = step.GetProperty("stateAfter");
            Assert.Equal(1, after.GetProperty("units")[1].GetProperty("currentHp").GetInt32());
            Assert.Equal("Lying", after.GetProperty("physical").GetProperty("figures")[1].GetProperty("posture").GetString());
        }
    }

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
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
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
        var started = TestGame.StartRound(state, new Dice(), false);
        var action = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
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
        var result = TestGame.Advance(action.State, new Choice(fireball.Key!), new Dice(), false);
        var resolved = BrowserProjection.Create(result);
        Assert.Equal(OutcomeRole.AttackSummary, resolved.Events.Last(e => e.Role == OutcomeRole.AttackSummary).Role);
        Assert.All(resolved.ResolutionSteps, s => Assert.Equal(1,
            s.Cards["actor"].Entries.Single(e => e.Content.Id == "fireball").Uses!.RemainingUses));
        Assert.Contains(result.ResolutionSteps[0].StateAfter.Physical.Figures, f => f.Id == "target");
        Assert.DoesNotContain(result.ResolutionSteps.First(s => result.Events[s.EventIndex].Kind == "UnitDefeated").StateAfter.Physical.Figures, f => f.Id == "target");
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

    [Fact]
    public void TelekinesisUsesGenericUnitInteractionDomainCardAndAuthoritativePostureSnapshots()
    {
        var state = State(UnitType.Wizard());
        state.Units[1] = state.Units[1] with { SideId = "hostile" };
        var started = TestGame.StartRound(state, new Dice(), false);
        var action = TestGame.Advance(started.State, new Choice("stay"), new Dice(), false);
        var presentation = BrowserProjection.Create(action);
        var card = presentation.Cards["actor"].Entries.Single(e => e.Content.Id == "telekinesis");
        Assert.Equal(new CardEntryDescription("telekinesis", "Telekinesis", "Action",
            "Lay down an upright enemy within RNG and LOS."), card.Content);
        Assert.Null(card.Uses);
        var choice = presentation.Decision!.Candidates.Single(c => c.EntryId == "telekinesis");
        Assert.Equal(new ChoiceInteraction(InteractionKind.Unit, UnitId: "target"), choice.Interaction);
        Assert.Equal("Telekinesis (Action) → target", choice.Label);
        Assert.Equal(new[] { "target" }, choice.AffectedUnitIds);
        var result = TestGame.Advance(action.State, new Choice(choice.Key!), new Dice(), false);
        var resolved = BrowserProjection.Create(result);
        Assert.Equal(OutcomeRole.Notice, resolved.Events.Last().Role);
        Assert.Equal(Posture.Lying, result.ResolutionSteps.Last().StateAfter.Physical.Figures[1].Posture);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        using var choicesJson = JsonDocument.Parse(JsonSerializer.Serialize(new GameResponse(1, action), options));
        var projected = choicesJson.RootElement.GetProperty("presentation").GetProperty("decision")
            .GetProperty("candidates").EnumerateArray().Single(c => c.GetProperty("key").GetString() == choice.Key);
        Assert.Equal("Unit", projected.GetProperty("interaction").GetProperty("kind").GetString());
        Assert.Equal("target", projected.GetProperty("interaction").GetProperty("unitId").GetString());
        using var resolvedJson = JsonDocument.Parse(JsonSerializer.Serialize(new GameResponse(2, result), options));
        Assert.Equal("Lying", resolvedJson.RootElement.GetProperty("result").GetProperty("resolutionSteps")[1]
            .GetProperty("stateAfter").GetProperty("physical").GetProperty("figures")[1].GetProperty("posture").GetString());
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
