using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PopulationMonsterPoolTests
{
    private const string DemolitionImp = "demolition-imp";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PublishedDemolitionImpHasHalfBombImpWeightAtEveryDifficulty(int difficulty)
    {
        var pool = PopulationComposition.Pool(ContentBundle.Canonical().Units.Select(t => t.Id).Append(DemolitionImp));
        var demolition = Assert.Single(pool, p => p.UnitTypeId == DemolitionImp);
        var bomb = Assert.Single(pool, p => p.UnitTypeId == UnitTypeIds.BombImp);
        Assert.Equal(bomb.Threat, demolition.Threat);
        Assert.Equal(bomb.Weight(difficulty), 2 * demolition.Weight(difficulty));
        Assert.DoesNotContain(pool, p => p.UnitTypeId == UnitTypeIds.Shaman);
    }

    [Fact]
    public void UnavailablePublicationIsExcludedAndStandaloneDungeonPoolIsUnchanged()
    {
        var pool = PopulationComposition.Pool(ContentBundle.Canonical().Units.Select(t => t.Id));
        Assert.DoesNotContain(pool, p => p.UnitTypeId == DemolitionImp);
        Assert.DoesNotContain(HeroesVsMonsters.Pool, p => p.UnitTypeId == DemolitionImp);
        Assert.Equal(HeroesVsMonsters.Pool.Where(p => p.UnitTypeId != UnitTypeIds.Shaman).Select(p => p.UnitTypeId),
            pool.Select(p => p.UnitTypeId));
    }

    [Fact]
    public void SelectionIsDeterministicAndIndependentOfContentEnumerationOrder()
    {
        var ids = ContentBundle.Canonical().Units.Select(t => t.Id).Append(DemolitionImp).ToArray();
        var first = PopulationComposition.Pool(ids);
        var second = PopulationComposition.Pool(ids.Reverse().Concat(ids));
        Assert.Equal(first, second);
        var randomA = Pcg32.Stream(147, "population-pool-test");
        var randomB = Pcg32.Stream(147, "population-pool-test");
        var draws = Enumerable.Range(0, 1024).Select(_ => PopulationComposition.Select(randomA, 3, first)).ToArray();
        Assert.Equal(draws, Enumerable.Range(0, draws.Length).Select(_ => PopulationComposition.Select(randomB, 3, second)));
        Assert.Contains(DemolitionImp, draws);
        Assert.True(draws.Count(id => id == DemolitionImp) < draws.Count(id => id == UnitTypeIds.BombImp));
    }
}
