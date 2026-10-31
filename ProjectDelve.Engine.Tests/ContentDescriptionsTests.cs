using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ContentDescriptionsTests
{
    [Fact]
    public void AllCurrentPlayableContentHasDomainOwnedCardWording()
    {
        var types = new[] { UnitType.Barbarian("b"), UnitType.Rogue("r"), UnitType.Cleric("c"),
            UnitType.Grunt("g"), UnitType.Zombie("z"), UnitType.SkeletonArcher("s"), UnitType.Goblin("o"), UnitType.Troll("t") };
        Assert.Equal(new[] { "Barbarian", "Rogue", "Cleric", "Grunt", "Zombie", "Skeleton Archer", "Goblin", "Troll" },
            types.Select(t => t.DisplayName));
        Assert.Equal(new[] { "Attack", "Open Door", "Fury", "Cleave", "Rage" }, types[0].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Open Door", "Backstab", "Dash", "Throwing Knife" }, types[1].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Open Door", "Heal", "Holy Wave", "Aura" }, types[2].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack" }, types[3].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Transport Resource", "Break Door" }, types[4].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack" }, types[5].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Move After Attack" }, types[6].CardEntries().Select(e => e.Name));
        Assert.Equal(new[] { "Attack", "Smash Door", "Undying" }, types[7].CardEntries().Select(e => e.Name));
        Assert.All(types.SelectMany(t => t.CardEntries()), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
        });
        var wave = Assert.Single(types[2].CardEntries(), e => e.Id == "ability:holy-wave");
        Assert.Equal(new CardEntryDescription("ability:holy-wave", "Holy Wave", "Action", "Lay down all adjacent upright enemies.\nThen lay down this Unit.", 2), wave);
        Assert.Equal("2/game", wave.UseLimitText);
        Assert.Equal("ATK +2 this activation.", Assert.Single(types[0].CardEntries(), e => e.Name == "Rage").Description);
        Assert.Equal("RNG +2; ATK -1 this activation.", Assert.Single(types[1].CardEntries(), e => e.Name == "Throwing Knife").Description);
        Assert.Contains("2 of 6 faces", Assert.Single(types[4].CardEntries(), e => e.Name == "Break Door").Description);
        Assert.DoesNotContain(types.SelectMany(t => t.CardEntries()), e => e.Category == "Behavior");
    }

    [Fact]
    public void DescriptionsRespectActualComponentsAndParametersInsteadOfTypeIdentity()
    {
        var type = new UnitType("unfamiliar", 2, 3, 4, 5, 6, Actions: UnitAction.None)
        { Unique = true,
            DisplayName = "A different printed name",
            LocalAbilities = [
                new("door", "Try Open Door", AbilityTiming.Action, new TryOpenDoor(5)),
                new("unfamiliar", "Unfamiliar ability", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, -2)])) { Usage = new(7) },
                new("follow-up", "Move After Attack", AbilityTiming.AfterAttack, new MoveAfterAttack(3))]
        };
        Assert.Equal(new[] { "Try Open Door", "Unfamiliar ability", "Move After Attack" }, type.CardEntries().Select(e => e.Name));
        Assert.Contains("5 of 6", type.CardEntries()[0].Description);
        Assert.Equal("DEF -2 this activation.", type.CardEntries()[1].Description);
        Assert.Contains("3 steps", type.CardEntries()[2].Description);
        Assert.Equal(7, type.CardEntries()[1].MaxUses);
    }
}
