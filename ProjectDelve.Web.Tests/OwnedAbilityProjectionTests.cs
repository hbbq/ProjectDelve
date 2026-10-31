using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class OwnedAbilityProjectionTests
{
    [Fact]
    public void InlineSourcesProjectNamesCountersPassivesAndItemCopiesWithoutGlobalDefinitions()
    {
        var state = OwnedAbilityWebFixture.State();
        var card = BrowserProjection.Cards(state)["actor"];
        var native = Assert.Single(card.Entries, entry => entry.Content.Name == "Silver Stitch");
        Assert.Equal("Native", native.Source);
        Assert.Equal(new AbilityUses(2, 2), native.Uses);
        Assert.Equal("Heal an adjacent damaged friendly Unit for 2 HP.", native.Content.Description);
        var passive = Assert.Single(card.Entries, entry => entry.Content.Name == "Woven Shade");
        Assert.Equal("Passive", passive.Content.Category);
        Assert.Null(passive.Uses);
        Assert.Equal(3, state.EffectiveDefOf("friend"));

        var equipment = Assert.Single(card.Entries, entry => entry.Source == "Equipment");
        Assert.Equal("Lantern Hush", equipment.Content.Name);
        Assert.Equal("Hollow Lantern", equipment.SourceName);
        Assert.Equal("lantern-one", equipment.SourceInstanceId);
        Assert.Equal("Action: " + equipment.Content.Description,
            Assert.Single(card.Inventory, item => item.EquipmentTypeId == "hollow-lantern").Text);
        var item = Assert.Single(card.Entries, entry => entry.Content.Name == "Paper Stitch");
        var bonus = Assert.Single(card.Entries, entry => entry.Content.Name == "Borrow a Moment");
        Assert.Equal("Action", item.Content.Category);
        Assert.Equal("Bonus Action", bonus.Content.Category);
        Assert.All(new[] { item, bonus }, entry =>
        {
            Assert.Equal("Item", entry.Source);
            Assert.Equal(2, entry.CopyCount);
            Assert.Null(entry.SourceInstanceId);
            Assert.Null(entry.Uses);
        });
        Assert.Equal(4, new[] { native, equipment, item, bonus }.Select(entry => entry.AccountingId).Distinct().Count());
        Assert.DoesNotContain(state.AbilityDefinitions, definition =>
            new[] { "Silver Stitch", "Woven Shade", "Lantern Hush", "Paper Stitch", "Borrow a Moment" }.Contains(definition.Name));

        var observation = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        var type = Assert.Single(observation.Types, type => type.Id == "woven-keeper");
        Assert.Equal(new[] { "Silver Stitch", "Woven Shade" }, type.Abilities.Select(ability => ability.Name));
        var unit = Assert.Single(observation.Units, unit => unit.Id == "actor");
        Assert.DoesNotContain(unit.Abilities!, ability => ability.Source == "Native");
        Assert.Contains(unit.Abilities!, ability => ability.Content.Name == "Paper Stitch" && ability.CopyCount == 2);
        Assert.Contains(unit.Abilities!, ability => ability.Content.Name == "Borrow a Moment" && ability.CopyCount == 2);
    }

    [Fact]
    public void RenamingOwnedContentChangesLabelsWithoutChangingCandidateOrAccountingIdentity()
    {
        var state = Ready(OwnedAbilityWebFixture.State());
        var original = state.AbilitiesOf("actor").Where(ability => ability.GrantId == "pulse")
            .ToDictionary(ability => ability.SourceInstanceId, ability => (ability.EntryId, ability.AccountingId, ability.CandidatePrefix));
        var keys = state.Pending!.Candidates.Select(candidate => candidate.Key).ToArray();
        var index = state.Types.FindIndex(type => type.Id == "woven-keeper");
        var type = state.Types[index];
        state.Types[index] = type with { LocalAbilities = type.LocalAbilities.SetItem(0, type.LocalAbilities[0] with { Name = "A New Stitch" }) };
        index = state.EquipmentTypes.FindIndex(type => type.Id == "hollow-lantern");
        var equipment = state.EquipmentTypes[index];
        state.EquipmentTypes[index] = equipment with
        { LocalAbilities = equipment.LocalAbilities.SetItem(0, equipment.LocalAbilities[0] with { Name = "A New Hush" }) };
        index = state.ItemTypes.FindIndex(type => type.Id == "folded-remedy");
        var item = state.ItemTypes[index];
        state.ItemTypes[index] = item with { LocalAbilities = item.LocalAbilities.SetItem(0, item.LocalAbilities[0] with { Name = "A New Fold" }) };
        state = Ready(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        Assert.Equal(keys, state.Pending!.Candidates.Select(candidate => candidate.Key));
        foreach (var ability in state.AbilitiesOf("actor").Where(ability => ability.GrantId == "pulse"))
            Assert.Equal(original[ability.SourceInstanceId], (ability.EntryId, ability.AccountingId, ability.CandidatePrefix));
        var decision = BrowserProjection.Decision(state.Pending, state)!;
        Assert.Contains(decision.Candidates, choice => choice.Label.StartsWith("A New Stitch (Action)"));
        Assert.Contains(decision.Candidates, choice => choice.Label.StartsWith("A New Hush (Action)"));
        Assert.Equal(2, decision.Candidates.Count(choice => choice.Label.StartsWith("A New Fold (Action)")));
    }

    [Fact]
    public void ItemConsumptionAndNativeUsesStaySourceAwareAcrossSavedPendingChoices()
    {
        var state = Ready(OwnedAbilityWebFixture.State());
        var bonus = state.Pending!.Candidates.Single(candidate => candidate.ObjectId == "moment-one" && candidate.GrantedAbility is not null);
        var used = GameEngine.Advance(state, new Choice(bonus.Key), new Dice(), false);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(used.State))!;
        var heal = restored.Pending!.Candidates.Single(candidate => candidate.GrantedAbility is
            { SourceKind: AbilitySourceKind.Unit, GrantId: "pulse" });
        var healed = GameEngine.Advance(restored, new Choice(heal.Key), new Dice(), false);
        var card = BrowserProjection.Cards(healed.State)["actor"];
        Assert.Equal(new AbilityUses(2, 1), Assert.Single(card.Entries, entry => entry.Content.Name == "Silver Stitch").Uses);
        Assert.Equal(3, healed.State.Units.Single(unit => unit.Id == "friend").CurrentHp);
        Assert.Equal(1, Assert.Single(card.Entries, entry => entry.Content.Name == "Borrow a Moment").CopyCount);
        Assert.DoesNotContain(healed.NextInput!.Candidates, candidate => candidate.ItemTypeId == "spare-moment" && candidate.GrantedAbility is not null);
        var itemHeal = healed.NextInput.Candidates.Single(candidate => candidate.ObjectId == "remedy-two" && candidate.GrantedAbility is not null);
        var after = GameEngine.Advance(healed.State, new Choice(itemHeal.Key), new Dice(), false);
        Assert.Equal(5, after.State.Units.Single(unit => unit.Id == "friend").CurrentHp);
        var entries = BrowserProjection.Cards(after.State)["actor"].Entries;
        Assert.Equal(1, Assert.Single(entries, entry => entry.Content.Name == "Paper Stitch").CopyCount);
        Assert.Equal(new AbilityUses(2, 1), Assert.Single(entries, entry => entry.Content.Name == "Silver Stitch").Uses);
        Assert.Equal(new[] { "moment-one", "remedy-two" }, after.State.LootDeck!.DiscardPile.Select(item => item.Id));
    }

    [Fact]
    public void HiddenOwnedDefinitionsStayWithTheirHiddenSourcesInBrowserGambitAndObservation()
    {
        var state = OwnedAbilityWebFixture.State(fog: true);
        using var game = new PlaytestGame(new Dice(), state);
        var projections = new object[]
        {
            FogOfWar.Observe(state, "blue"), game.BrowserResponse(game.Snapshot()),
            GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>())
        };
        foreach (var projection in projections)
        {
            var json = JsonSerializer.Serialize(projection);
            Assert.Contains("Silver Stitch", json);
            foreach (var hidden in new[] { "Hidden Native Whisper", "Hidden Lantern Whisper", "Hidden Paper Whisper",
                         "concealed-keeper", "concealed-lantern", "concealed-paper", "hidden-ground-paper" })
                Assert.DoesNotContain(hidden, json);
        }
    }

    private static GameState Ready(GameState state)
    {
        state.Round = 1;
        state.ActiveToken = new("woven-keeper", "blue");
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "woven-keeper", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
}
