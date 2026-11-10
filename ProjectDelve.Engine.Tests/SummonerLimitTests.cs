using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SummonerLimitTests
{
    [Fact]
    public void EquipmentAndItemGrantsShareTheirBearersExistingPopulation()
    {
        var state = SummonerFixture.Use(SummonerFixture.Ready()).State;
        var ability = new LocalAbilityDefinition("grant", "Another Source", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic("child-type", Posture.Upright, SameTypeSummonerLimit: 1));
        state.EquipmentTypes.Add(new("another-wand", "Another Wand") { LocalAbilities = [ability] });
        state.ItemTypes.Add(new("another-scroll", "Another Scroll") { LocalAbilities = [ability] });
        state.Units[0] = state.Units[0] with { Inventory = [new("wand-instance", "another-wand"),
            new("scroll-instance") { ItemTypeId = "another-scroll" }] };
        Assert.False(SummonerFixture.CanSummon(state));
        GameEngine.ResolveDefeat(state, state.Units.Single(u => u.TypeId == "child-type").Id);
        var choices = SummoningRules.Candidates(state, state.Units[0]).ToArray();
        Assert.Contains(choices, c => c.Ability!.SourceKind == AbilitySourceKind.Equipment);
        Assert.Contains(choices, c => c.Ability!.SourceKind == AbilitySourceKind.Item);
    }

    [Fact]
    public void SameTypeAndSideSummonersHaveIndependentLimitsAndInitialUnitsDoNotCount()
    {
        var state = SummonerFixture.Ready();
        state.PlaceUnit("child-type", "initial", "blue", new(5, 4));
        Assert.True(SummonerFixture.CanSummon(state));
        state = SummonerFixture.Use(state).State;
        state.ActionsUsedThisActivation = 0;
        Assert.False(SummonerFixture.CanSummon(state));
        Assert.True(SummonerFixture.CanSummon(state, "second"));
        state.CurrentUnitId = "second";
        state = GameEngine.RefreshChoices(state, new SummonerFixture.Dice(), false).State;
        state = SummonerFixture.Use(state).State;
        Assert.Equal(new[] { "first", "second" }, state.Units.Where(u => u.SummonerUnitId is not null)
            .Select(u => u.SummonerUnitId).Order().ToArray());
    }

    [Fact]
    public void CreatorLimitIgnoresSideGroupControllerPostureAndAbilityIdentity()
    {
        var state = SummonerFixture.Use(SummonerFixture.Ready()).State;
        state.ActionsUsedThisActivation = 0;
        var childIndex = state.Units.FindIndex(u => u.TypeId == "child-type");
        var child = state.Units[childIndex];
        state.Units[childIndex] = child with { SideId = "red", GroupId = "unrelated" };
        state.Controllers.Add(new(state.Units[childIndex].Token, ControllerKind.Automated));
        var figureIndex = state.Physical.Figures.FindIndex(f => f.Id == child.Id);
        state.Physical.Figures[figureIndex] = state.Physical.Figures[figureIndex] with { Posture = Posture.Lying };
        state.Types[0] = state.Types[0] with { LocalAbilities = state.Types[0].LocalAbilities.Add(
            new("another-source", "Different Summon", AbilityTiming.Action,
                new AdjacentUnitSummonMechanic("child-type", Posture.Lying, SameTypeSummonerLimit: 1))) };
        Assert.False(SummonerFixture.CanSummon(state));
        state.Units[0] = state.Units[0] with { SideId = "red" };
        Assert.False(SummonerFixture.CanSummon(state));
        state.Units[childIndex] = state.Units[childIndex] with { SummonerUnitId = "second" };
        Assert.True(SummonerFixture.CanSummon(state));
    }

    [Fact]
    public void DifferentTypeDoesNotCountAndDefeatAllowsReplacementWithFreshIdentity()
    {
        var state = SummonerFixture.Ready();
        state.Units[1] = state.Units[1] with { SummonerUnitId = "first" };
        Assert.True(SummonerFixture.CanSummon(state));
        state = SummonerFixture.Use(state).State;
        var child = state.Units.Single(u => u.TypeId == "child-type");
        state.ActionsUsedThisActivation = 0;
        Assert.False(SummonerFixture.CanSummon(state));
        GameEngine.ResolveDefeat(state, child.Id);
        Assert.True(SummonerFixture.CanSummon(state));
        state = GameEngine.RefreshChoices(state, new SummonerFixture.Dice(), false).State;
        var replaced = SummonerFixture.Use(state).State;
        var replacement = Assert.Single(replaced.Units, u => u.TypeId == "child-type" && u.CurrentHp > 0);
        Assert.NotEqual(child.Id, replacement.Id);
        Assert.Equal("first", replacement.SummonerUnitId);
        Assert.Equal("first", replaced.Units.Single(u => u.Id == child.Id).SummonerUnitId);
    }

    [Fact]
    public void LimitsCountOnlyLivingUnitsWithFigures()
    {
        var state = SummonerFixture.Use(SummonerFixture.Ready()).State;
        state.ActionsUsedThisActivation = 0;
        var childIndex = state.Units.FindIndex(u => u.TypeId == "child-type");
        var child = state.Units[childIndex];
        var figure = state.Physical.Figures.Single(f => f.Id == child.Id);
        state.Physical.Figures.Remove(figure);
        Assert.True(SummonerFixture.CanSummon(state));
        state.Physical.Figures.Add(figure);
        state.Units[childIndex] = child with { CurrentHp = 0 };
        Assert.True(SummonerFixture.CanSummon(state));
    }

    [Fact]
    public void SideAndCreatorCapsAreConjunctiveAndSideCapKeepsExistingSemantics()
    {
        var state = SummonerFixture.Ready(creatorLimit: 1, sideLimit: 2);
        state.PlaceUnit("child-type", "initial-one", "blue", new(4, 4), groupId: "outsiders");
        state.Controllers.Add(new(new("child-type", "blue", "outsiders"), ControllerKind.Automated));
        state.PlaceUnit("child-type", "opponent", "red", new(8, 4));
        state.Controllers.Add(new(new("child-type", "red"), ControllerKind.Human));
        Assert.True(SummonerFixture.CanSummon(state));
        state.PlaceUnit("child-type", "initial-two", "blue", new(6, 4));
        Assert.False(SummonerFixture.CanSummon(state)); // Side full despite no owned child.
        GameEngine.ResolveDefeat(state, "initial-two");
        Assert.True(SummonerFixture.CanSummon(state));
        state = GameEngine.RefreshChoices(state, new SummonerFixture.Dice(), false).State;
        state = SummonerFixture.Use(state).State;
        state.ActionsUsedThisActivation = 0;
        GameEngine.ResolveDefeat(state, "initial-one");
        Assert.False(SummonerFixture.CanSummon(state)); // Creator full despite room on Side.
        var childIndex = state.Units.FindIndex(u => u.SummonerUnitId == "first");
        state.Units[childIndex] = state.Units[childIndex] with { SideId = "red" };
        Assert.False(SummonerFixture.CanSummon(state));
        var sideOnly = SummonerFixture.Ready(creatorLimit: null, sideLimit: 1);
        sideOnly.PlaceUnit("child-type", "unowned", "blue", new(4, 4));
        Assert.False(SummonerFixture.CanSummon(sideOnly));
        sideOnly.Units[^1] = sideOnly.Units[^1] with { SideId = "red" };
        Assert.True(SummonerFixture.CanSummon(sideOnly));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TypedValidationRejectsNonpositiveCreatorLimit(int limit)
    {
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate([
            new("summon", "Summon", AbilityTiming.Action,
                new AdjacentUnitSummonMechanic("child-type", Posture.Upright, SameTypeSummonerLimit: limit))], AbilitySourceKind.Unit));
    }

    [Fact]
    public void AuthoringDescriptionAndJsonPreserveBothCaps()
    {
        var state = SummonerFixture.Restore(SummonerFixture.Ready(creatorLimit: 2, sideLimit: 3));
        var ability = state.Types.Single(t => t.Id == "summoner-type").LocalAbilities.Single(a => a.Key == "summon");
        var mechanic = Assert.IsType<AdjacentUnitSummonMechanic>(ability.Mechanic);
        Assert.Equal(2, mechanic.SameTypeSummonerLimit); Assert.Equal(3, mechanic.SameTypeSideLimit);
        var text = ContentDescriptions.AbilityEntry(ability, "summon", state.Types).Description;
        Assert.Contains("fewer than 3", text); Assert.Contains("fewer than 2", text);
        Assert.Contains("summoned by this Unit", text); Assert.DoesNotContain("{", text);
        var parameter = MechanicCatalog.Find("adjacentUnitSummon")!.Parameters.Single(p => p.Name == "sameTypeSummonerLimit");
        Assert.Equal(1, parameter.Minimum); Assert.True(parameter.AllowNull); Assert.False(parameter.Required);
    }
}
