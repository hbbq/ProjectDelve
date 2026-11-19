using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionCivilizationTests
{
    private const ulong Seed = 42;
    private static readonly WorldGenerationRecipe Baseline = new(new(0, 0, 60, 60), new(), Seed, Civilization: new());

    [Fact]
    public void OutpostOpportunitiesAreBoundedAndIndependentOfTerrainAndSettlementBudgets()
    {
        var profile = ExpeditionWorldProfile.Create(Baseline.NaturalTerrain);
        var barren = Baseline with { NaturalTerrain = new(0, 100, 0),
            Civilization = new(Campsites: ContentDensity.None, Settlements: ContentDensity.None) };
        var barrenProfile = ExpeditionWorldProfile.Create(barren.NaturalTerrain);
        var windows = Enumerable.Range(-6, 13).SelectMany(y => Enumerable.Range(-6, 13)
            .Select(x => new WorldWindow(60 * x, 60 * y, 60, 60))).ToArray();
        var sampled = windows.ToDictionary(window => window, window =>
            ExpeditionCivilization.ResolveOutpost(Baseline with { Window = window }, profile));
        foreach (var window in windows.Reverse())
            Assert.Equal(sampled[window], ExpeditionCivilization.ResolveOutpost(barren with { Window = window }, barrenProfile));
        Assert.Contains(sampled.Values, allocation => allocation.Anchor is null);
        Assert.Contains(sampled.Values, allocation => allocation.Anchor is not null);
        Assert.All(sampled.Values.Where(allocation => allocation.Anchor is not null), allocation =>
        {
            Assert.InRange(allocation.Anchor!.X, 0, 59);
            Assert.InRange(allocation.Anchor.Y, 0, 59);
        });
    }

    [Fact]
    public void RemainsOpportunitiesVaryGeographicallyWithoutDependingOnTerrainOrLivingTargets()
    {
        var profile = ExpeditionWorldProfile.Create(Baseline.NaturalTerrain);
        var changed = Baseline with { NaturalTerrain = new(0, 60, 40),
            Civilization = new(Campsites: ContentDensity.None, Settlements: ContentDensity.None) };
        var changedProfile = ExpeditionWorldProfile.Create(changed.NaturalTerrain);
        var windows = Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17)
            .Select(x => new WorldWindow(60 * x, 60 * y, 60, 60))).ToArray();
        var sampled = windows.ToDictionary(window => window, window =>
            ExpeditionCivilization.ResolveRemains(Baseline with { Window = window }, profile));
        foreach (var window in windows.Reverse())
            Assert.Equal(sampled[window], ExpeditionCivilization.ResolveRemains(changed with { Window = window }, changedProfile));

        Assert.Contains(sampled.Values, value => value.Ruin is null && value.LandmarkAnchor is null);
        Assert.Contains(sampled.Values, value => value.Ruin is not null && value.LandmarkAnchor is null);
        Assert.Contains(sampled.Values, value => value.Ruin is null && value.LandmarkAnchor is not null);
        foreach (var kind in new[] { SiteConstructionProfile.RuinedCottage, SiteConstructionProfile.CollapsedBuilding, SiteConstructionProfile.RuinedHamlet })
            Assert.Contains(sampled.Values, value => value.Ruin == kind);

        double Change(int lag, Func<ExpeditionRemainsAllocation, double> signal) => windows
            .Where(window => sampled.ContainsKey(window with { OffsetX = window.OffsetX + lag * 60 }))
            .Average(window => Math.Abs(signal(sampled[window]) -
                signal(sampled[window with { OffsetX = window.OffsetX + lag * 60 }])));
        Assert.True(Change(1, value => value.RuinSignal) < Change(4, value => value.RuinSignal));
        Assert.True(Change(1, value => value.LandmarkSignal) < Change(4, value => value.LandmarkSignal));
    }

    [Fact]
    public void AllocationIsDeterministicAndChangesSmoothlyAcrossSignedRegions()
    {
        var profile = ExpeditionWorldProfile.Create(Baseline.NaturalTerrain);
        var sampler = new ExpeditionNatureSampler(Seed, profile);
        var coordinates = (from y in Enumerable.Range(-6, 13) from x in Enumerable.Range(-6, 13) select (x, y)).ToArray();
        ExpeditionCivilizationAllocation Resolve(int x, int y) =>
            ExpeditionCivilization.Resolve(Baseline with { Window = new(x * 60, y * 60, 60, 60) }, profile, sampler);
        var regions = coordinates.ToDictionary(p => p, p => Resolve(p.x, p.y));

        foreach (var (x, y) in coordinates.Reverse())
            Assert.Equal(regions[(x, y)], Resolve(x, y));

        Assert.Contains(regions.Values, a => a.SettlementTarget == 0 && a.DungeonTarget > 0);
        Assert.Contains(regions.Values, a => a.SettlementTarget == 1);
        Assert.Contains(regions.Values, a => a.SettlementTarget >= 4 && a.Hub is not null);
        Assert.Contains(regions.Values, a => a.SettlementTarget >= 4 && a.Hub is null);
        Assert.All(regions.Values, a => Assert.Equal(MountainDungeonGenerator.Target(Baseline.Window, Baseline.Civilization!), a.DungeonTarget));
        Assert.All(regions.Values.Where(a => a.Hub is not null), a =>
        {
            Assert.InRange(a.Hub!.X, 0, 59);
            Assert.InRange(a.Hub.Y, 0, 59);
        });

        double MeanDensityChange(int lag) => coordinates.Where(p => regions.ContainsKey((p.x + lag, p.y)))
            .Average(p => Math.Abs(regions[p].DensitySignal - regions[(p.x + lag, p.y)].DensitySignal));
        Assert.True(MeanDensityChange(1) < MeanDensityChange(4));
    }

    [Fact]
    public void ExplicitOptOutAndZeroRegionalVariationPreserveRecipeIntent()
    {
        var profile = ExpeditionWorldProfile.Create(Baseline.NaturalTerrain);
        var sampler = new ExpeditionNatureSampler(Seed, profile);
        var noSettlements = ExpeditionCivilization.Resolve(Baseline with { Civilization = new(Settlements: ContentDensity.None) }, profile, sampler);
        Assert.Equal(0, noSettlements.SettlementTarget);
        Assert.Null(noSettlements.Hub);
        Assert.True(noSettlements.DungeonTarget > 0);

        var noCamps = ExpeditionCivilization.Resolve(Baseline with { Civilization = new(Campsites: ContentDensity.None) }, profile, sampler);
        Assert.Equal(0, noCamps.CampsiteTarget);

        var constant = ExpeditionWorldProfile.Create(Baseline.NaturalTerrain, new() { RegionalVariation = 0 });
        var allocation = ExpeditionCivilization.Resolve(Baseline, constant, new(Seed, constant));
        Assert.Equal(DensityScaling.Settlements(Baseline.Window, Baseline.Civilization!.Settlements), allocation.SettlementTarget);
        Assert.Equal(DensityScaling.Campsites(Baseline.Window, Baseline.Civilization.Campsites), allocation.CampsiteTarget);
        Assert.Null(allocation.Hub);
        Assert.Equal(0, allocation.AnchorWeight);
    }
}
