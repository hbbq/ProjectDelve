using ProjectDelve.Engine;

namespace ProjectDelve.Web.Tests;

// A real JSON provider owned by each test, independent of installed game content.
internal sealed class CanonicalJsonFixture : IDisposable
{
    internal const string PackageId = "test-canonical-json";
    internal const string EquipmentTypeId = "test-canonical-bell";
    internal const string LootCardId = "test-canonical-gift";
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "delve-canonical-json-" + Guid.NewGuid().ToString("N"));
    internal ContentPackage Package { get; } = new()
    {
        Id = PackageId,
        Title = "Test-owned JSON provider",
        Definitions = new()
        {
            Equipment = [new(EquipmentTypeId, "Test Canonical Bell")
            {
                LocalAbilities = [new("encourage", "Encourage", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright)) { Usage = new(2) }]
            }],
            LootCards = [new(LootCardId, "Test Canonical Gift") { EquipmentTypeId = EquipmentTypeId }]
        }
    };

    internal CanonicalJsonFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "test-provider.json"), ContentPackageJson.ToJson(Package));
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
