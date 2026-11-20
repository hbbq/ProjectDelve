using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class MonsterDropContentTests
{
    private static ContentBundle Published() => new CanonicalContent(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/Content/Canonical"))).Bundle;

    [Fact]
    public void ExpeditionMonsterRewardsKeepTheirLootFacesThroughCatalogSerialization()
    {
        var package = new ContentPackage { Id = "reward-audit", Definitions = Published() };
        var exported = ContentPackageJson.FromJson(ContentPackageJson.ToJson(package)).Definitions;
        (string Id, int Loot, int Credits, int Amount)[] expected =
        [
            (UnitTypeIds.Goblin, 1, 0, 0), (UnitTypeIds.Grunt, 1, 3, 3), (UnitTypeIds.Zombie, 1, 2, 3),
            (UnitTypeIds.BombImp, 1, 2, 4), ("demolition-imp", 1, 2, 5), (UnitTypeIds.SkeletonArcher, 1, 2, 5),
            (UnitTypeIds.Ghost, 1, 2, 5), (UnitTypeIds.Troll, 2, 0, 0),
            (UnitTypeIds.DisplacerDemon, 2, 3, 6), (UnitTypeIds.RedDragon, 3, 2, 12)
        ];
        foreach (var row in expected)
        {
            var type = exported.Units.Single(t => t.Id == row.Id);
            Assert.Equal((row.Loot, row.Credits, row.Amount), (type.LootDropSuccesses, type.CreditsDropSuccesses, type.CreditsDropAmount));
            Assert.Empty(ContentDefinitions.Validate(new(type)));
        }
    }

    [Fact]
    public void PublishedSummonTargetsDoNotProduceCredits()
    {
        var content = Published();
        var owners = content.Units.Cast<object>().Concat(content.Equipment).Concat(content.Items);
        var targets = owners.SelectMany(owner => ContentDefinitions.Dependencies(owner))
            .Select(reference => reference.Target).Where(identity => identity.Family == ContentFamily.Unit)
            .Select(identity => identity.TypeId).Distinct().ToArray();
        Assert.Contains(UnitTypeIds.Goblin, targets);
        Assert.Contains(UnitTypeIds.HobbyKnight, targets);
        Assert.Contains("budget-barbarian", targets);
        Assert.All(targets, id => Assert.Equal(0, content.Units.Single(t => t.Id == id).CreditsDropSuccesses));
    }
}
