using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SupplementalContentDiscoveryTests
{
    [Fact]
    public void MachineCatalogExposesCanonicalDefinitionsAndConcreteOperationData()
    {
        Assert.Equal(new[] { MachineTypes.DiamondPress, MachineTypes.SlotMachine }, MachineTypes.All);
        Assert.Equal(MachineTypes.All.Length, MachineTypes.All.Select(type => type.Id).Distinct().Count());

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var type in MachineTypes.All)
        {
            var json = JsonSerializer.SerializeToElement(type, options);
            var storage = json.GetProperty("storage").GetProperty("capacities");
            Assert.Equal(type.Storage.Capacities.Length, storage.GetArrayLength());
            var operation = json.GetProperty("operation");
            Assert.Equal(type.Operation.Costs.Length, operation.GetProperty("costs").GetArrayLength());
            Assert.Equal(1, operation.GetProperty("outputQuantity").GetInt32());
            if (type.Operation.OutputTable is { } table)
                Assert.Equal(table.Faces.Select(face => face.Id), operation.GetProperty("outputTable")
                    .GetProperty("faces").EnumerateArray().Select(face => face.GetProperty("id").GetString()));
            else
                Assert.Equal(type.Operation.OutputType!.Id, operation.GetProperty("outputType").GetProperty("id").GetString());
        }
    }

    [Fact]
    public void CellMarkerCatalogDescribesFixedArrivalBehaviorWithoutClaimingAnAuthoredEffect()
    {
        Assert.Same(CellMarkerTypes.BananaPeel, Assert.Single(CellMarkerTypes.All));
        var description = CellMarkerTypes.Describe(CellMarkerTypes.BananaPeel);
        Assert.Contains("When a Unit arrives by movement or placement", description);
        Assert.Contains("Starting an activation on the marker does not trigger it", description);
        Assert.Contains("Hypothetical route planning ignores the marker", description);
        Assert.Throws<ArgumentException>(() => CellMarkerTypes.Describe(new("custom-marker", "Custom Marker")));

        var json = JsonSerializer.SerializeToElement(CellMarkerTypes.BananaPeel, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new[] { "id", "name" }, json.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("openDoor", MechanicTargetSelection.Edge)]
    [InlineData("tryOpenDoor", MechanicTargetSelection.Edge)]
    [InlineData("operate", MechanicTargetSelection.Machine)]
    [InlineData("giveResource", MechanicTargetSelection.UnitOrMachine)]
    [InlineData("advanceOnKill", MechanicTargetSelection.Cell)]
    public void MechanicInspectionIdentifiesExistingExplicitChoices(string mechanicId, MechanicTargetSelection selection)
    {
        var descriptor = MechanicCatalog.Find(mechanicId)!;
        Assert.Equal(selection, descriptor.Targeting.Selection);
        var json = JsonSerializer.SerializeToElement(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(selection.ToString(), json.GetProperty("targeting").GetProperty("selection").GetString());
        if (selection is MechanicTargetSelection.Machine or MechanicTargetSelection.UnitOrMachine)
        {
            Assert.Contains("public or owned by this Unit's Side", descriptor.Targeting.Recipients);
            if (selection == MechanicTargetSelection.UnitOrMachine)
                Assert.Contains("friendly Unit", descriptor.Targeting.Recipients);
        }
    }

    [Fact]
    public void DefinitionOnlyItemDescriptionUsesTheSameProseAsPhysicalItemPresentation()
    {
        var item = new ItemDefinition("restorative-note", "Restorative Note")
        {
            LocalAbilities = [new("restore", "Restore", AbilityTiming.Action, new HealMechanic(4))]
        };
        var state = new GameState { Physical = new(new Board(1, 1, []), []), Types = [], Units = [], ItemTypes = [item] };
        var physical = new OwnedObject("note-instance") { ItemTypeId = item.Id };
        Assert.Equal(state.ItemContent(physical).Text, item.Describe());
        Assert.Contains("4", item.Describe());
        Assert.EndsWith("Consume this Item.", item.Describe());
    }
}
