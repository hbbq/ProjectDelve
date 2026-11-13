using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class CompactUnitCardProjectionTests
{
    [Fact]
    public void LayoutUsesMechanicsAndTimingRatherThanPrintedNames()
    {
        var state = State(new("door", "Surprising name", AbilityTiming.FreeAction, new OpenDoor()),
            new("operate", "Attack", AbilityTiming.Action, new Operate()),
            new("passive", "Operate", AbilityTiming.Passive, new Phase()),
            new("healing", "Open Door", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1)),
            new("limited-door", "Door", AbilityTiming.FreeAction, new OpenDoor()) { Usage = new(MaxUses: 2) });
        var card = BrowserProjection.Cards(state)["actor"];

        Assert.Equal("StandardAttack", card.Entries.Single(e => e.Content.Id == "attack").Layout);
        Assert.Equal("Utility", Entry(card, "door").Layout);
        Assert.Equal("Utility", Entry(card, "operate").Layout);
        Assert.Equal("Trait", Entry(card, "passive").Layout);
        Assert.Equal("Action", Entry(card, "healing").Layout);
        Assert.Equal("Action", Entry(card, "limited-door").Layout);
    }

    [Fact]
    public void CostsAndFollowUpRestrictionsComeFromTypedDeclarations()
    {
        var paid = new LocalAbilityDefinition("paid", "Custom remedy", AbilityTiming.Action,
            new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2))
            { Costs = [new LayDownSelf(), new SpendResource("diamond", 2)] };
        var state = State(paid,
            new("cleave", "Another name", AbilityTiming.AfterAttack, new Cleave { TriggerDamage = 3, Damage = 2 })
                { Usage = new(MaxUses: 4) },
            new("explosion", "Goodbye", AbilityTiming.WhenDefeated, new Explosion { Damage = 1 }));
        var card = BrowserProjection.Cards(state)["actor"];

        Assert.Equal(string.Join(" ", paid.Costs.Select(CostCatalog.Describe)), Entry(card, "paid").CostText);
        Assert.Equal("After Attack", Entry(card, "cleave").TimingHint);
        Assert.Equal("Attack dealt 3+ damage to a Unit", Entry(card, "cleave").RestrictionText);
        Assert.Equal(4, Entry(card, "cleave").Uses!.RemainingUses);
        Assert.Equal("When defeated", Entry(card, "explosion").TimingHint);
        Assert.Equal("Action", Entry(card, "explosion").Layout);
    }

    [Fact]
    public void NativeAndPhysicalEquipmentCopiesKeepSeparateCountersAndProvenance()
    {
        var ability = new LocalAbilityDefinition("remedy", "Shared name", AbilityTiming.Action,
            new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1)) { Usage = new(MaxUses: 3) };
        var state = State(ability);
        state.EquipmentTypes = [new("kit", "Kit") { LocalAbilities = [ability] }, new("other-kit", "Kit") { LocalAbilities = [ability] }];
        state.Units[0] = state.Units[0] with
        {
            AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("unit:remedy", 1),
            Inventory = [new("kit-one", "kit") { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("remedy", 2) },
                new("kit-two", "other-kit")]
        };
        var before = JsonSerializer.Serialize(state);
        var card = BrowserProjection.Cards(state)["actor"];
        var entries = card.Entries.Where(e => e.Content.Name == "Shared name").ToArray();

        Assert.Equal(3, entries.Length);
        Assert.Equal(3, entries.Select(e => e.AccountingId).Distinct().Count());
        Assert.Equal(new[] { 2, 1, 3 }, entries.Select(e => e.Uses!.RemainingUses));
        Assert.Equal(new[] { "kit-one", "kit-two" }, card.OwnedObjects.Select(o => o.Id));
        Assert.Equal(new[] { "kit", "other-kit" }, card.OwnedObjects.Select(o => o.EquipmentTypeId));
        Assert.Equal(new[] { 1, 3 }, card.OwnedObjects.Select(o => Assert.Single(o.Entries).Uses!.RemainingUses));
        Assert.Equal(2, card.Inventory.Count);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void ItemCopiesRemainGroupedWhileOwnedObjectsRetainPhysicalIdentity()
    {
        var state = State();
        var ability = new LocalAbilityDefinition("remedy", "One remedy", AbilityTiming.Action,
            new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1));
        state.ItemTypes = [new("remedy-item", "Remedy Item") { LocalAbilities = [ability] }];
        state.Units[0] = state.Units[0] with
        {
            Inventory = [new("item-one") { ItemTypeId = "remedy-item" }, new("item-two") { ItemTypeId = "remedy-item" }]
        };

        var card = BrowserProjection.Cards(state)["actor"];

        var entry = Assert.Single(card.Entries.Where(e => e.Source == "Item"));
        Assert.Equal(2, entry.CopyCount);
        Assert.Null(entry.SourceInstanceId);
        Assert.Equal(new[] { "item-one", "item-two" }, card.OwnedObjects.Select(o => o.Id));
        Assert.All(card.OwnedObjects, o =>
        {
            Assert.Equal("remedy-item", o.ItemTypeId);
            var objectEntry = Assert.Single(o.Entries);
            Assert.Equal(o.Id, objectEntry.SourceInstanceId);
            Assert.Equal("Action", objectEntry.Layout);
            Assert.Null(objectEntry.Uses);
        });
    }

    [Fact]
    public void IntrinsicAttackRestrictionDistinguishesExplicitAttackFromNoAttack()
    {
        var state = State();
        // ATK 0 and no targets do not remove the intrinsically granted standard Attack.
        state.Types[0] = state.Types[0] with { Atk = 0 };
        Assert.Null(BrowserProjection.Cards(state)["actor"].AttackRestriction);

        state.Types[0] = state.Types[0] with { Actions = UnitAction.None };
        Assert.Equal("Can't attack", BrowserProjection.Cards(state)["actor"].AttackRestriction);
        state.Types[0] = state.Types[0] with
        {
            LocalAbilities = [new("alternate", "Unrelated name", AbilityTiming.Action, new TargetUnitAttackMechanic(4, 3))]
        };
        var card = BrowserProjection.Cards(state)["actor"];
        Assert.Equal("No standard Attack", card.AttackRestriction);
        Assert.DoesNotContain(card.Entries, e => e.Layout == "StandardAttack");
        Assert.Equal("Action", Entry(card, "alternate").Layout);
    }

    [Fact]
    public void ActivationUseAndGameDepletionHaveSeparateTypedSignals()
    {
        var state = State(new LocalAbilityDefinition("bonus", "Remedy", AbilityTiming.BonusAction,
            new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 1)) { Usage = new(MaxUses: 2) });
        var grant = Assert.Single(state.AbilitiesOf("actor"));
        state.CurrentUnitId = "actor";
        state.BonusActionUseCountsThisActivation[grant.AccountingId] = 1;
        var request = new DecisionRequest(DecisionKind.Activation, "compact-fixture", "actor", [], false);

        Assert.True(BrowserProjection.Decision(request, state)!.EntryStatuses[grant.AccountingId].UsedThisActivation);
        Assert.Equal(2, Entry(BrowserProjection.Cards(state)["actor"], "bonus").Uses!.RemainingUses);
        state.Units[0] = state.Units[0] with
            { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(grant.AccountingId, 2) };
        var depleted = BrowserProjection.Decision(request, state)!.EntryStatuses[grant.AccountingId];
        Assert.Equal(BrowserEntryAvailability.Exhausted, depleted.Status);
        Assert.False(depleted.UsedThisActivation);
        Assert.Equal(0, Entry(BrowserProjection.Cards(state)["actor"], "bonus").Uses!.RemainingUses);
        Assert.True(Entry(BrowserProjection.Cards(state)["actor"], "bonus").IsDepleted);
    }

    private static CardEntry Entry(UnitCard card, string key) => card.Entries.Single(e => e.Content.Id == "ability:" + key);

    private static GameState State(params LocalAbilityDefinition[] abilities)
    {
        var type = new UnitType("compact-fixture", 2, 1, 1, 1, 3) { LocalAbilities = [.. abilities] };
        return new()
        {
            Physical = new(new Board(2, 1, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")]
        };
    }
}
