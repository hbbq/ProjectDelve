using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class OwnedAbilityExecutionTests
{
    private static ScenarioDefinition Example() => new()
    {
        Board = new() { Width = 6, Height = 4 },
        UnitTypeIds = ["owned-bearer", "owned-friend", "owned-enemy"],
        UnitTypes =
        [
            new("owned-bearer", 2, 3, 1, 0, 4)
            {
                Unique = true, CanHaveEquipment = true, CanHaveItems = true,
                LocalAbilities =
                [
                    new("mend", "Silver Thread", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(2) },
                    new("ward", "Nearby Shelter", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 1)]))
                ]
            },
            new("owned-friend", 0, 0, 0, 0, 4) { Unique = true },
            new("owned-enemy", 0, 0, 0, 0, 6) { Unique = true }
        ],
        EquipmentTypes =
        [
            new("woven-band", "Woven Band")
            {
                LocalAbilities =
                [
                    new("mend", "Woven Light", AbilityTiming.Action, new HealMechanic(3)),
                    new("ward", "Close Shelter", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Def, 2)])),
                    new("use", "Resolve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 1)]))
                ]
            }
        ],
        ItemTypes =
        [
            new("paper-flame", "Paper Flame")
            { LocalAbilities = [new("use", "Bright Fold", AbilityTiming.Action, new FireballMechanic())] },
            new("paper-moment", "Paper Moment")
            { LocalAbilities = [new("use", "Another Moment", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] },
            new("paper-pause", "Paper Pause")
            { LocalAbilities = [new("use", "A Different Moment", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] }
        ],
        Units =
        [
            new("owned-bearer", "blue", new(1, 1), UnitId: "actor"),
            new("owned-friend", "blue", new(1, 2), InitialHp: 1, UnitId: "friend"),
            new("owned-enemy", "red", new(3, 1), UnitId: "enemy")
        ],
        Agency =
        [
            new("owned-bearer", "blue", ControllerKind.Human),
            new("owned-friend", "blue", ControllerKind.Human),
            new("owned-enemy", "red", ControllerKind.Human)
        ],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    private static GameState Ready(params OwnedObject[] inventory)
    {
        var scenario = Example();
        scenario.Units[0] = scenario.Units[0] with { Inventory = [.. inventory] };
        scenario = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario));
        var state = GameEngine.CreateGame(scenario);
        // A complete owned-content game does not need any global Ability catalog.
        state.AbilityDefinitions = [];
        state.EquipmentTypes = [.. scenario.EquipmentTypes!];
        state.ItemTypes = [.. scenario.ItemTypes!];
        state.LootCardDefinitions = [];
        GameEngine.ValidateScenario(state);
        state.Round = 1;
        state.ActiveToken = new("owned-bearer", "blue");
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "owned-bearer", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);

    [Fact]
    public void UnitAndEquipmentPassivesWorkWithAnEmptyGlobalCatalogAndRespectPosture()
    {
        var state = Ready(new OwnedObject("band", "woven-band"));
        Assert.Empty(state.AbilityDefinitions);
        Assert.Equal(2, state.EffectiveDefOf("actor"));
        Assert.Equal(1, state.EffectiveDefOf("friend"));
        Assert.Equal(0, state.EffectiveDefOf("enemy"));
        var restored = Restore(state);
        Assert.Empty(restored.AbilityDefinitions);
        Assert.Equal(1, restored.EffectiveDefOf("friend"));
        var figure = restored.Physical.Figures.FindIndex(f => f.Id == "actor");
        restored.Physical.Figures[figure] = restored.Physical.Figures[figure] with { Posture = Posture.Lying };
        Assert.Equal(0, restored.EffectiveDefOf("actor"));
        Assert.Equal(0, restored.EffectiveDefOf("friend"));
    }

    [Fact]
    public void SameLocalKeyOnUnitAndEquipmentUsesTheSelectedPayloadAndIndependentAllowance()
    {
        var state = Ready(new OwnedObject("band", "woven-band"));
        var native = state.AbilitiesOf("actor").Single(a => a.SourceKind == AbilitySourceKind.Unit && a.GrantId == "mend");
        var equipment = state.Pending!.Candidates.Single(c => c.GrantedAbility is
            { SourceKind: AbilitySourceKind.Equipment, GrantId: "mend" });
        var result = Use(state, equipment.Key);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", native));
        Assert.Equal("Woven Light", result.Events.Single(e => e.Kind == "HealResolved").AbilityName);
        Assert.Equal("band", Assert.Single(result.State.Units.Single(u => u.Id == "actor").Inventory).Id);
    }

    [Fact]
    public void ItemFireballKeepsItsOwnedPayloadThroughConsumptionPendingDiceAndReplay()
    {
        var state = Ready(new("flame-one") { ItemTypeId = "paper-flame" }, new("flame-two") { ItemTypeId = "paper-flame" });
        var candidate = state.Pending!.Candidates.Single(c => c.ObjectId == "flame-one" && c.Destination == new Cell(3, 1));
        var original = Use(state, candidate.Key);
        var replayed = Use(Restore(state), candidate.Key);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(replayed));
        Assert.Equal(DecisionKind.RollDice, original.NextInput!.Kind);
        Assert.Equal("Bright Fold", original.State.AttackInProgress!.AbilityName);
        Assert.Equal("flame-one", Assert.Single(original.State.LootDeck!.DiscardPile).Id);
        Assert.Equal("flame-two", Assert.Single(original.State.Units.Single(u => u.Id == "actor").Inventory).Id);
        Assert.Empty(original.State.AbilityDefinitions);

        var restored = Restore(original.State);
        // The authoritative pending Attack already contains the invoked mechanic's context.
        Assert.Equal(JsonSerializer.Serialize(original.NextInput), JsonSerializer.Serialize(restored.Pending));
        var continued = Use(original.State, "roll-dice");
        var resumed = Use(restored, "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(resumed));
        Assert.Equal(5, resumed.State.Units.Single(u => u.Id == "enemy").CurrentHp);
    }

    [Fact]
    public void DuplicateItemCopiesShareOneBonusAllowanceButOtherTypesAndEquipmentRemainIndependent()
    {
        var state = Ready(new("moment-one") { ItemTypeId = "paper-moment" },
            new("moment-two") { ItemTypeId = "paper-moment" }, new("pause") { ItemTypeId = "paper-pause" },
            new("band", "woven-band"));
        var copies = state.Pending!.Candidates.Where(c => c.ItemTypeId == "paper-moment").ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal(copies[0].GrantedAbility!.AccountingId, copies[1].GrantedAbility!.AccountingId);
        var used = Use(state, copies.Single(c => c.ObjectId == "moment-one").Key);
        Assert.Equal(1, used.State.ExtraActionsThisActivation);
        Assert.DoesNotContain(used.NextInput!.Candidates, c => c.ItemTypeId == "paper-moment");
        Assert.Contains(used.NextInput.Candidates, c => c.ItemTypeId == "paper-pause");
        var restored = Restore(used.State);
        var other = restored.Pending!.Candidates.Single(c => c.ItemTypeId == "paper-pause");
        var second = Use(restored, other.Key);
        Assert.Equal(2, second.State.ExtraActionsThisActivation);
        var equipment = second.NextInput!.Candidates.Single(c => c.GrantedAbility is
            { SourceKind: AbilitySourceKind.Equipment, GrantId: "use" });
        var third = Use(second.State, equipment.Key);
        Assert.Equal(2, third.State.EffectiveAtkOf("actor"));
        Assert.Equal(0, third.State.ActionsUsedThisActivation);
        Assert.Equal(new[] { "moment-one", "pause" }, third.State.LootDeck!.DiscardPile.Select(o => o.Id));
        Assert.Equal(new[] { "moment-two", "band" }, third.State.Units.Single(u => u.Id == "actor").Inventory.Select(o => o.Id));
        Assert.Equal(3, third.State.BonusActionUseCountsThisActivation.Count);
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
