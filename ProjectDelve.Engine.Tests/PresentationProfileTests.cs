using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PresentationProfileTests
{
    private static LocalAbilityDefinition Ability(string? profile = null) =>
        new("heal", "A printed name", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2))
        { PresentationProfileId = profile };

    [Fact]
    public void OptionalProfilePreservesLegacyEnvelopeAndUnknownProfilesRoundTrip()
    {
        // This is the exact legacy default-serializer envelope, including its casing.
        const string legacy = "{\"Key\":\"heal\",\"Name\":\"A printed name\",\"Timing\":0,\"Mechanic\":{\"kind\":\"adjacentUnitHeal\",\"Selector\":0,\"Amount\":2},\"Usage\":{\"MaxUses\":null},\"DescriptionOverride\":null}";
        Assert.Equal(legacy, JsonSerializer.Serialize(Ability()));
        Assert.Equal(legacy, JsonSerializer.Serialize(JsonSerializer.Deserialize<LocalAbilityDefinition>(legacy)));
        foreach (var options in new[] { new JsonSerializerOptions(), ContentPackageJson.CreateOptions() })
        {
            var json = JsonSerializer.Serialize(Ability("future.unfamiliar"), options);
            Assert.Contains("\"presentationProfileId\":", json);
            Assert.Equal("future.unfamiliar", JsonSerializer.Deserialize<LocalAbilityDefinition>(json, options)!.PresentationProfileId);
            Assert.DoesNotContain("presentationProfileId", JsonSerializer.Serialize(Ability(), options));
        }
        AbilityContent.Validate([Ability("future.unfamiliar")], AbilitySourceKind.Unit);
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate([Ability(" ")], AbilitySourceKind.Unit));
    }

    [Fact]
    public void PackageAndPinnedDigestsIncludeExplicitProfilesAndPreserveOmittedProfiles()
    {
        ContentBundle Bundle(LocalAbilityDefinition ability) => new()
        { Equipment = [new("test-lantern", "Test Lantern") { LocalAbilities = [ability] }] };
        var legacy = new ContentPackage { Id = "test-package", Definitions = Bundle(Ability()) };
        var serialized = ContentPackageJson.ToJson(legacy);
        var restored = ContentPackageJson.FromJson(serialized);
        Assert.Equal(serialized, ContentPackageJson.ToJson(restored));
        var identity = new ContentIdentity(ContentFamily.Equipment, "test-lantern");
        Assert.Equal(CanonicalPublications.PayloadDigest(legacy.Definitions, identity), CanonicalPublications.PayloadDigest(restored.Definitions, identity));
        var authored = legacy with { Definitions = Bundle(Ability("future.unfamiliar")) };
        var package = ContentPackageJson.FromJson(ContentPackageJson.ToJson(authored));
        Assert.Equal("future.unfamiliar", package.Definitions.Equipment[0].LocalAbilities[0].PresentationProfileId);
        Assert.NotEqual(CanonicalPublications.PayloadDigest(legacy.Definitions, identity), CanonicalPublications.PayloadDigest(package.Definitions, identity));
        var digest = new string('a', 64);
        var pin = new ScenarioContentReference(1, "test-library", "test-package", 1, digest, [identity], [identity],
            [new(identity, "test-library", "test-package", 1, digest)], legacy.Definitions, "");
        pin = pin with { SnapshotDigest = ScenarioContentReferences.Digest(pin) };
        _ = ScenarioContentReferences.Validate(pin);
        var pinCopy = JsonSerializer.Deserialize<ScenarioContentReference>(JsonSerializer.Serialize(pin, ContentPackageJson.CreateOptions()), ContentPackageJson.CreateOptions())!;
        Assert.Equal(pin.SnapshotDigest, ScenarioContentReferences.Digest(pinCopy));
        var changed = pin with { Snapshot = package.Definitions };
        Assert.NotEqual(pin.SnapshotDigest, ScenarioContentReferences.Digest(changed));
        changed = changed with { SnapshotDigest = ScenarioContentReferences.Digest(changed) };
        _ = ScenarioContentReferences.Validate(changed);
        var roundTrip = JsonSerializer.Deserialize<ScenarioContentReference>(JsonSerializer.Serialize(changed, ContentPackageJson.CreateOptions()), ContentPackageJson.CreateOptions())!;
        Assert.Equal("future.unfamiliar", roundTrip.Snapshot.Equipment[0].LocalAbilities[0].PresentationProfileId);
        Assert.Equal(changed.SnapshotDigest, ScenarioContentReferences.Digest(roundTrip));
    }
}
