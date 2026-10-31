using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class AbilityProjectionTests
{
    [Theory]
    [InlineData("cleric-type", "attack,heal,holy-wave,open-door,passive:Aura")]
    [InlineData("wizard-type", "attack,fireball,telekinesis,open-door,bonus:Focus")]
    [InlineData("barbarian-type", "attack,open-door,bonus:Rage,passive:Fury,cleave")]
    [InlineData("rogue-type", "attack,open-door,bonus:Dash,bonus:Throwing Knife,passive:Backstab")]
    public void CanonicalGrantsKeepPrintedEntryOrderAndAppearOnceInGambitTypes(string typeId, string ids)
    {
        var type = UnitContent.Find(typeId)!;
        var state = new GameState
        {
            Physical = new(new Board(3, 3, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")]
        };
        var expected = ids.Split(',');
        var entries = BrowserProjection.Cards(state)["actor"].Entries;
        Assert.Equal(expected, entries.Select(entry => entry.Content.Id));
        Assert.Equal(expected.Length, entries.Select(entry => entry.Content.Id).Distinct().Count());
        var observation = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        Assert.Equal(expected, Assert.Single(observation.Types).Abilities.Select(ability => ability.Id));
        Assert.DoesNotContain(Assert.Single(observation.Units).Abilities ?? [], ability => ability.Source == "Native");
        foreach (var entry in entries.Where(entry => entry.Content.MaxUses is not null))
            Assert.Equal(entry.Content.MaxUses, entry.Uses!.RemainingUses);
    }

    [Fact]
    public void NativeAndEquipmentDescriptionsUseTheSuppliedCatalogWithoutDuplicateGrantText()
    {
        var type = LegacyReferenceCleric();
        var state = new GameState
        {
            Physical = new(new Board(3, 3, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")]
        };
        var index = state.AbilityDefinitions.FindIndex(ability => ability.Id == "heal");
        state.AbilityDefinitions[index] = state.AbilityDefinitions[index] with
        { Name = "Mending Verse", Mechanic = new HealMechanic(4) };
        state.AbilityDefinitions.Add(new("portable-remedy", "Lantern Remedy", AbilityTiming.Action, new HealMechanic(7)));
        var equipment = new EquipmentDefinition("restoring-lantern", "Restoring Lantern")
        { Grants = [new("restore", "portable-remedy")] };
        state.EquipmentTypes.Add(equipment);
        state.Units[0] = state.Units[0] with { Inventory = [new("lantern-one", equipment.Id)] };

        var entries = BrowserProjection.Cards(state)["actor"].Entries;
        var native = Assert.Single(entries, entry => entry.Content.Id == "heal");
        Assert.Equal("Mending Verse", native.Content.Name);
        Assert.Equal("Heal an adjacent damaged friendly Unit for 4 HP.", native.Content.Description);
        var acquired = Assert.Single(entries, entry => entry.Source == "Equipment");
        Assert.Equal("Lantern Remedy", acquired.Content.Name);
        Assert.Equal("Action: Heal an adjacent damaged friendly Unit for 7 HP.",
            Assert.Single(BrowserProjection.Cards(state)["actor"].Inventory).Text);
        Assert.Equal(acquired.Content, Assert.Single(equipment.CardEntries(state.AbilityDefinitions)));
        var observation = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        Assert.Contains(Assert.Single(observation.Types).Abilities,
            ability => ability.Id == "heal" && ability.Name == "Mending Verse" && ability.Description.Contains("4 HP"));
        Assert.Contains(Assert.Single(observation.Units).Abilities!, ability => ability.Content.Name == "Lantern Remedy");
        Assert.Equal("Heal", AbilityCatalog.All.Single(ability => ability.Id == "heal").Name);
        Assert.Throws<ArgumentException>(() => type.CardEntries(state.Types, []));
        Assert.Throws<ArgumentException>(() => equipment.CardEntries([]));
    }

    [Fact]
    public void HiddenNativeGrantsDoNotRevealTheirResolvedDefinitions()
    {
        var visible = UnitType.Grunt();
        var hidden = LegacyReferenceCleric();
        var state = new GameState
        {
            Physical = new(new Board(20, 3, []), [new("observer", new(0, 0)), new("hidden", new(19, 0))]),
            Types = [visible, hidden], Units = [visible.CreateUnit("observer", "blue"), hidden.CreateUnit("hidden", "red")],
            FogOfWarSides = ["blue"]
        };
        var index = state.AbilityDefinitions.FindIndex(ability => ability.Id == "heal");
        state.AbilityDefinitions[index] = state.AbilityDefinitions[index] with { Name = "Secret Mending Verse" };
        Assert.Contains(state.AbilitiesOf("hidden"), ability => ability.Definition.Name == "Secret Mending Verse");
        FogOfWar.Refresh(state);
        var observed = FogOfWar.Observe(state, "blue");
        Assert.DoesNotContain("Secret Mending Verse", JsonSerializer.Serialize(observed));
        Assert.DoesNotContain("Secret Mending Verse", JsonSerializer.Serialize(BrowserProjection.Cards(observed)));
        Assert.DoesNotContain("Secret Mending Verse", JsonSerializer.Serialize(
            GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>())));
    }

    // Retain the historical reference representation to exercise supplied-catalog
    // resolution independently of the canonical Cleric's owned declarations.
    private static UnitType LegacyReferenceCleric() => UnitType.Cleric() with
    {
        LocalAbilities = [],
        AbilityGrants =
        [
            new("heal", "heal") { Compatibility = new(LegacyAbilityKind.Heal) },
            new("holy-wave", "holy-wave") { Compatibility = new(LegacyAbilityKind.HolyWave) },
            new("aura", "aura") { Compatibility = new(LegacyAbilityKind.Aura, Key: "Aura") }
        ]
    };

    private static GameState State(bool fog = false)
    {
        var type = new UnitType("wave-bearer", 3, 1, 1, 0, 2)
        {
            Actions = UnitAction.HolyWave, HolyWave = new(2),
            CanHaveItems = true, CanHaveEquipment = true
        };
        var state = new GameState
        {
            Physical = new(new Board(20, 3, []), [new("a", new(0, 0)), new("b", new(19, 0))]),
            Types = [type], Units = [type.CreateUnit("a", "blue"), type.CreateUnit("b", "red")],
            Controllers = [new(new(type.Id, "blue"), ControllerKind.Human), new(new(type.Id, "red"), ControllerKind.Automated)],
            FogOfWarSides = fog ? ["blue"] : []
        };
        state.AbilityDefinitions.AddRange([
            new("dusk-call", "Gathering Dusk", AbilityTiming.Action, new HolyWaveMechanic()),
            new("unfold", "Fold the Horizon", AbilityTiming.Action, new HolyWaveMechanic()),
            new("blue-ember", "Blue Ember", AbilityTiming.Action, new FireballMechanic())]);
        state.EquipmentTypes.Add(new("sunken-chime", "Sunken Chime") { Grants = [new("ring", "dusk-call")] });
        state.ItemTypes.Add(new("paper-horizon", "Paper Horizon") { Grants = [new("read", "unfold")] });
        state.ItemTypes.Add(new("blue-seed", "Blue Seed") { Grants = [new("plant", "blue-ember")] });
        state.Units[0] = state.Units[0] with
        {
            Inventory = [new("chime-physical", "sunken-chime"),
                new("paper-one") { ItemTypeId = "paper-horizon" }, new("paper-two") { ItemTypeId = "paper-horizon" }]
        };
        FogOfWar.Refresh(state);
        return state;
    }

    [Fact]
    public void SharedMechanicProjectsDistinctNamesSourcesCountersAndGroupedPhysicalItems()
    {
        var state = State();
        var entries = BrowserProjection.Cards(state)["a"].Entries;
        var native = Assert.Single(entries.Where(e => e.Content.Id == "holy-wave"));
        Assert.Equal("Holy Wave", native.Content.Name);
        Assert.Equal(new AbilityUses(2, 2), native.Uses);
        var equipment = Assert.Single(entries.Where(e => e.Source == "Equipment"));
        Assert.Equal("Gathering Dusk", equipment.Content.Name);
        Assert.Equal("Sunken Chime", equipment.SourceName);
        Assert.Equal("chime-physical", equipment.SourceInstanceId);
        Assert.Null(equipment.Uses);
        var item = Assert.Single(entries.Where(e => e.Source == "Item"));
        Assert.Equal("Fold the Horizon", item.Content.Name);
        Assert.Equal("Paper Horizon", item.SourceName);
        Assert.Equal("paper-horizon", item.SourceTypeId);
        Assert.Equal(2, item.CopyCount);
        Assert.Null(item.SourceInstanceId);
        Assert.Null(item.Uses);
        Assert.All(new[] { native, equipment, item }, e => Assert.Equal("Action", e.Content.Category));
        Assert.Equal(3, new[] { native, equipment, item }.Select(e => e.AccountingId).Distinct().Count());
        Assert.Equal(native.Content.Description, equipment.Content.Description);
        Assert.Equal(native.Content.Description, item.Content.Description);
        Assert.DoesNotContain(BrowserProjection.Cards(state)["b"].Entries, e => e.Source != "Native");

        var grants = state.AbilitiesOf("a").ToArray();
        var candidates = grants.Select(g => new Candidate(g.CandidatePrefix, Action: UnitAction.HolyWave, Kind: ActivationChoiceKind.Action)
            { GrantedAbility = g, ObjectId = g.SourceKind == AbilitySourceKind.Unit ? null : g.SourceInstanceId }).ToList();
        var request = new DecisionRequest(DecisionKind.Activation, "wave-bearer", "a", candidates, false);
        var choices = BrowserProjection.Decision(request, state)!.Candidates;
        Assert.Equal(2, choices.Count(c => c.AccountingId == item.AccountingId));
        Assert.All(choices.Where(c => c.AccountingId == item.AccountingId), c => Assert.Equal("Fold the Horizon (Action)", c.Label));
        Assert.Contains(choices, c => c.AccountingId == equipment.AccountingId && c.Label == "Gathering Dusk (Action)");
        var observation = GambitProjection.Observation(state, "blue", state.Controllers.ToDictionary(c => c.Token, _ => "Gambit"));
        var observerUnit = observation.Units.Single(u => u.Id == "a");
        Assert.Contains(observerUnit.Abilities!, e => e.Content.Name == item.Content.Name && e.CopyCount == 2);
        Assert.Equal(2, observerUnit.Inventory!.Count(o => o.ItemTypeId == "paper-horizon"));
        Assert.DoesNotContain(":null", JsonSerializer.Serialize(observerUnit.Abilities));
    }

    [Fact]
    public void RenamedAreaAbilityUsesDeclaredNameInBoardPreviewAndChoice()
    {
        var state = State();
        state.Units[0] = state.Units[0] with { Inventory = [new("seed") { ItemTypeId = "blue-seed" }] };
        var grant = state.AbilitiesOf("a").Single(g => g.SourceKind == AbilitySourceKind.Item);
        var candidate = new Candidate(grant.CandidatePrefix + ":1,0", Destination: new(1, 0), Action: UnitAction.Fireball, Kind: ActivationChoiceKind.Action)
            { GrantedAbility = grant, ObjectId = grant.SourceInstanceId };
        var request = new DecisionRequest(DecisionKind.Activation, "wave-bearer", "a", [candidate], false);
        var choice = Assert.Single(BrowserProjection.Decision(request, state)!.Candidates);
        Assert.Equal("Blue Ember (Action) → (1,0)", choice.Label);
        Assert.Equal("Blue Ember at (1,0)", choice.BoardVisual!.Preview);
        Assert.Equal(grant.EntryId, choice.EntryId);
        Assert.Equal(grant.AccountingId, choice.AccountingId);
    }

    [Fact]
    public void HiddenItemsAndTheirAbilityDefinitionsDoNotLeakThroughTransportOrObservation()
    {
        var state = State(true);
        state.Units[0] = state.Units[0] with { Inventory = [] };
        state.Units[1] = state.Units[1] with { Inventory = [new("hidden-paper") { ItemTypeId = "paper-horizon" }] };
        state.GroundObjects = [new(new("hidden-seed") { ItemTypeId = "blue-seed" }, new(19, 1))];
        using var game = new PlaytestGame(new Dice(), state);
        var response = game.BrowserResponse(game.Snapshot());
        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("Paper Horizon", json);
        Assert.DoesNotContain("Fold the Horizon", json);
        Assert.DoesNotContain("Blue Ember", json);
        Assert.DoesNotContain("hidden-paper", json);
        Assert.Empty(response.Presentation.GroundObjects);
        var observation = GambitProjection.Observation(state, "blue", state.Controllers.ToDictionary(c => c.Token, _ => "Gambit"));
        var observedJson = JsonSerializer.Serialize(observation);
        Assert.DoesNotContain("Paper Horizon", observedJson);
        Assert.DoesNotContain("Fold the Horizon", observedJson);
        Assert.DoesNotContain("hidden-seed", observedJson);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
}
