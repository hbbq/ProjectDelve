using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ConsolidatedMechanicContractTests
{
    [Theory]
    [InlineData("{\"kind\":\"adjacentUnitHeal\",\"amount\":2}")]
    [InlineData("{\"kind\":\"adjacentUnitPostureChange\",\"selector\":0}")]
    [InlineData("{\"kind\":\"adjacentUnitsPostureChange\",\"newPosture\":0}")]
    [InlineData("{\"kind\":\"adjacentUnitsStatModifiers\",\"modifiers\":[]}")]
    [InlineData("{\"kind\":\"adjacentUnitSummon\",\"unitTypeId\":\"worker-type\"}")]
    [InlineData("{\"kind\":\"selfExtraMoves\"}")]
    [InlineData("{\"kind\":\"targetUnitAttack\",\"atk\":3}")]
    [InlineData("{\"kind\":\"placeBananaPeel\",\"bananaCost\":1}")]
    public void MissingParametersAndRemovedEmbeddedPaymentAreRejected(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AbilityMechanic>(json));

    [Theory]
    [InlineData("holyWave")]
    [InlineData("heal")]
    [InlineData("standAdjacentFriendly")]
    [InlineData("adjacentModifiers")]
    [InlineData("summonAdjacent")]
    [InlineData("summonWorker")]
    [InlineData("summonTroll")]
    [InlineData("boost")]
    [InlineData("shootShell")]
    public void ReplacedDiscriminatorsHaveNoCompatibilityDispatch(string kind)
    {
        Assert.Null(MechanicCatalog.Find(kind));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AbilityMechanic>("{\"kind\":\"" + kind + "\"}"));
    }

    [Fact]
    public void ContractsRoundTripAndValidateTheirExplicitParameters()
    {
        AbilityMechanic[] mechanics =
        [
            new AdjacentUnitsPostureChangeMechanic(RelationshipSelector.All, Posture.Upright),
            new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Enemies, Posture.Lying),
            new AdjacentUnitHealMechanic(RelationshipSelector.Enemies, int.MaxValue),
            new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.All, [new(Stat.Def, -2)]),
            new AdjacentUnitSummonMechanic("authored-unit", Posture.Lying, 4),
            new SelfExtraMovesMechanic(int.MaxValue), new TargetUnitAttackMechanic(4, 3), new PlaceBananaPeel()
        ];
        foreach (var mechanic in mechanics)
        {
            var json = JsonSerializer.Serialize<AbilityMechanic>(mechanic, ContentPackageJson.CreateOptions());
            var restored = JsonSerializer.Deserialize<AbilityMechanic>(json, ContentPackageJson.CreateOptions())!;
            Assert.Equal(json, JsonSerializer.Serialize(restored, ContentPackageJson.CreateOptions()));
            Assert.Empty(MechanicCatalog.Validate(restored));
        }
        Assert.Contains(MechanicCatalog.Validate(new AdjacentUnitHealMechanic((RelationshipSelector)99, 0)), d => d.Path == "mechanic.selector");
        Assert.Contains(MechanicCatalog.Validate(new AdjacentUnitPostureChangeMechanic(RelationshipSelector.All, (Posture)99)), d => d.Path == "mechanic.newPosture");
        Assert.Contains(MechanicCatalog.Validate(new AdjacentUnitSummonMechanic("authored-unit", Posture.Upright, 0)), d => d.Path == "mechanic.sameTypeSideLimit");
    }

    [Fact]
    public void SummoningAndPaymentsHaveOnlyExplicitTypedDependencies()
    {
        var owner = new EquipmentDefinition("test-gear", "Test gear")
        {
            LocalAbilities = [new("call", "Call", AbilityTiming.Action,
                new AdjacentUnitSummonMechanic("authored-unit", Posture.Upright, 3))
                { Costs = [new SpendResource("authored-resource", 2)] }]
        };
        var dependencies = ContentDefinitions.Dependencies(owner);
        Assert.Equal(2, dependencies.Length);
        Assert.All(dependencies, d => Assert.Equal(ContentDependencyKind.Explicit, d.Kind));
        Assert.Contains(dependencies, d => d.Target == new ContentIdentity(ContentFamily.Unit, "authored-unit") && d.Path == "localAbilities[0].mechanic.unitTypeId");
        Assert.Contains(dependencies, d => d.Target == new ContentIdentity(ContentFamily.Resource, "authored-resource") && d.Path == "localAbilities[0].costs[0].resourceTypeId");
        var free = owner with { LocalAbilities = [owner.LocalAbilities[0] with { Mechanic = new PlaceBananaPeel(), Costs = [] }] };
        Assert.Empty(ContentDefinitions.Dependencies(free));
    }
}
