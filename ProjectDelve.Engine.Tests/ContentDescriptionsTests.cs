using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ContentDescriptionsTests
{
    [Fact]
    public void AllCurrentPlayableContentHasDomainOwnedCardWording()
    {
        var types = new[] { UnitType.Barbarian("b"), UnitType.Rogue("r"), UnitType.Cleric("c"),
            UnitType.Grunt("g"), UnitType.Zombie("z"), UnitType.SkeletonArcher("s"), UnitType.Goblin("o") };
        Assert.Equal(new[] { "Barbarian", "Rogue", "Cleric", "Grunt", "Zombie", "Skeleton Archer", "Goblin" },
            types.Select(t => t.DisplayName));
        Assert.Equal(new[] { "Attack", "Open Door", "Rage", "Fury", "Cleave" }, types[0].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Open Door", "Dash", "Throwing Knife", "Backstab" }, types[1].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Heal", "Holy Wave", "Open Door", "Aura" }, types[2].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack" }, types[3].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Try Open Door" }, types[4].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack" }, types[5].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Move After Attack" }, types[6].CardEntries().Select(e => e.Name));
        Assert.All(types.SelectMany(t => t.CardEntries()), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
        });
        var wave = Assert.Single(types[2].CardEntries(), e => e.Id == "holy-wave");
        Assert.Equal(new CardEntryDescription("holy-wave", "Holy Wave", "Action", "Lay down all adjacent upright enemies.\nThen lay down this Unit.", 2), wave);
        Assert.Equal("2/game", wave.UseLimitText);
        Assert.Equal("+2 ATK this turn", Assert.Single(types[0].CardEntries(), e => e.Name == "Rage").Description);
        Assert.Equal("+2 RNG & -1 ATK this turn", Assert.Single(types[1].CardEntries(), e => e.Name == "Throwing Knife").Description);
        Assert.Contains("2 of 6 faces", Assert.Single(types[4].CardEntries(), e => e.Name == "Try Open Door").Description);
        Assert.DoesNotContain(types.SelectMany(t => t.CardEntries()), e => e.Category == "Behavior");
    }

    [Fact]
    public void DescriptionsRespectActualComponentsAndParametersInsteadOfTypeIdentity()
    {
        var type = new UnitType("unfamiliar", 2, 3, 4, 5, 6, Actions: UnitAction.None,
            TryOpenDoor: new(5), MoveAfterAttack: new(3))
        {
            DisplayName = "A different printed name",
            BonusActions = [new("Unfamiliar ability", 7, [new(Stat.Def, -2)])]
        };
        Assert.Equal(new[] { "Try Open Door", "Unfamiliar ability", "Move After Attack" }, type.CardEntries().Select(e => e.Name));
        Assert.Contains("5 of 6", type.CardEntries()[0].Description);
        Assert.Equal("-2 DEF this turn", type.CardEntries()[1].Description);
        Assert.Contains("3 steps", type.CardEntries()[2].Description);
        Assert.Equal(7, type.CardEntries()[1].MaxUses);
    }
}
