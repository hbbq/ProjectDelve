using System.Text.Json;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class AccessibilityQuickPlayTests
{
    [Fact]
    public void AuditUsesActualTemporaryPartyWithoutChangingPreparationOrSource()
    {
        var original = new ScenarioDefinition
        {
            Board = new() { Width = 10, Height = 2,
                Edges = [new(new(4, 0), EdgeDirection.Right, EdgeKind.ClosedDoor), new(new(4, 1), EdgeDirection.Right, EdgeKind.Wall)] },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        };
        var before = ScenarioDefinitionJson.ToJson(original);
        var content = ContentBundle.Canonical();
        var result = WorldQuickPlay.PrepareWithAccessibility(original, content);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(original));
        Assert.Equal(ScenarioDefinitionJson.ToJson(WorldQuickPlay.Prepare(original, content)), ScenarioDefinitionJson.ToJson(result.Definition));
        Assert.Equal(5, result.Accessibility.Units.Count);
        foreach (var unit in result.Accessibility.Units)
        {
            Assert.Equal(result.Definition.Units.Single(p => p.UnitId == unit.UnitId).Anchor, unit.Start);
            Assert.True(unit.ConditionalStoppingPositions > 0);
            Assert.Contains(unit.Doors, d => d.Status == AccessibilityDoorStatus.Deterministic);
        }
        Assert.Equal(JsonSerializer.Serialize(result.Accessibility),
            JsonSerializer.Serialize(WorldQuickPlay.PrepareWithAccessibility(original, content).Accessibility));
        Assert.DoesNotContain("accessibility", ScenarioDefinitionJson.ToJson(result.Definition), StringComparison.OrdinalIgnoreCase);
    }
}
