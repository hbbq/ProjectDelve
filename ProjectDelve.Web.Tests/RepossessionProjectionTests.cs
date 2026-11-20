using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class RepossessionProjectionTests
{
    [Theory]
    [InlineData(true, "success")]
    [InlineData(false, "failed; Action consumed")]
    public void CustomAbilityAndOutcomeUseSharedTypedProjection(bool success, string expected)
    {
        var actor = new UnitType("collector", 0, 0, 0, 0, 1)
        {
            LocalAbilities = [new("invoice", "Invoice", AbilityTiming.Action,
                new TryRepossessRandomEquipment(2, StorageRules.RepossessedEquipmentId))]
        };
        var state = new GameState
        {
            Physical = new(new Board(3, 2, []), [new("actor", new(0, 0)), new("target", new(1, 0))]),
            Types = [actor], Units = [actor.CreateUnit("actor", "blue"), actor.CreateUnit("target", "red")]
        };
        var entry = BrowserProjection.Cards(state)["actor"].Entries.Single(e => e.Content.Name == "Invoice");
        Assert.Contains("2 of 6 faces", entry.Content.Description);
        var outcome = BrowserProjection.Outcome(new("EquipmentRepossessionAttemptResolved", "actor", "target",
            DieRoll: success ? 1 : 6, SuccessCount: 2, Succeeded: success)
            { RepossessedEquipmentId = success ? "physical-boots-7" : null, RepossessedEquipmentName = success ? "Custom Boots" : null }, state);
        Assert.Contains(expected, outcome.Text);
        Assert.Contains("2/6", outcome.Text);
        if (success)
        {
            Assert.Contains("Custom Boots", outcome.Text); Assert.Contains("physical-boots-7", outcome.Text);
            Assert.Contains("Custom Boots", outcome.DisplayText); Assert.Contains("physical-boots-7", outcome.DisplayText);
        }
        Assert.Contains("left play", BrowserProjection.Outcome(new("UnitRemoved", "actor"), state).Text);
    }
}
