using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionPopulationIntroductionTests
{
    private static readonly ExpeditionWorldProfile Profile = ExpeditionWorldProfile.Create(new());

    [Theory]
    [InlineData(0, 0, new[] { 1, 1, 1, 1, 1 }, PopulationDensity.Low, 2)]
    [InlineData(-1, 0, new[] { 1, 1, 2, 2, 2 }, PopulationDensity.Medium, 3)]
    [InlineData(0, 2, new[] { 1, 2, 2, 3, 3 }, PopulationDensity.High, null)]
    [InlineData(-1, -2, new[] { 1, 2, 3, 3, 4 }, PopulationDensity.High, null)]
    public void AdjustsEachResolvedBandWithoutRaisingLowerDensityOrEnablingOff(
        int x, int y, int[] expectedDifficulties, PopulationDensity cap, int? arrivalBudget)
    {
        var introduction = Assert.IsType<ExpeditionPopulationIntroduction>(
            ExpeditionPopulationIntroduction.For(Profile, new(x * 60, y * 60, 60, 60)));
        Assert.Equal(arrivalBudget, introduction.ArrivalBudget);
        foreach (var density in Enum.GetValues<PopulationDensity>())
        for (var normal = 1; normal <= 5; normal++)
        {
            var settings = new PopulationSettings(density, normal);
            var expected = density == PopulationDensity.Off ? settings : new PopulationSettings(
                (PopulationDensity)Math.Min((int)density, (int)cap), expectedDifficulties[normal - 1]);
            Assert.Equal(expected, introduction.Adjust(settings));
        }
    }

    [Theory]
    [InlineData(-120, 120)]
    [InlineData(60, -240)]
    [InlineData(-2147483640, 0)]
    public void DistantWindowsAndOlderProfilesHaveNoIntroduction(int x, int y)
    {
        Assert.Null(ExpeditionPopulationIntroduction.For(Profile, new(x, y, 60, 60)));
        Assert.Null(ExpeditionPopulationIntroduction.For(Profile with { PopulationVersion = null }, new()));
        Assert.Null(ExpeditionPopulationIntroduction.For(null, new()));
    }

    [Fact]
    public void ArrivalCostCountsEachNearbyFigureOnceAcrossFootprintsAndFormation()
    {
        var melee = new UnitType("melee", 3, 1, 2, 2, 1);
        var ranged = melee with { Id = "ranged", Rng = 7, Footprint = Footprint.TwoByTwo };
        var types = new[] { melee, ranged }.ToDictionary(type => type.Id);
        Cell[] formation = [new(5, 5), new(5, 6)];
        UnitPlacement Monster(UnitType type, int x, int y) => new(type.Id, "monsters", new(x, y));
        Assert.Equal(2, ExpeditionPopulationIntroduction.ArrivalCost(
            [Monster(melee, 11, 8), Monster(melee, 5, 14), Monster(melee, 5, 15)], types, formation));
        // The anchor is eleven Cells away. The right edge of the 2x2 footprint is exactly ten away.
        Assert.Equal(2, ExpeditionPopulationIntroduction.ArrivalCost(
            [Monster(ranged, 1, 8)], types, [new(12, 8), new(12, 9)]));
        Assert.Equal(0, ExpeditionPopulationIntroduction.ArrivalCost(
            [Monster(ranged, 1, 8)], types, [new(13, 8)]));
    }
}
