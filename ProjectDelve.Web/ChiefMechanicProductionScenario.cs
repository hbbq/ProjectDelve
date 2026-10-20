using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class ChiefMechanicProductionScenario
{
    internal static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 8, Height = 5, DefaultTerrain = TerrainKind.Asphalt },
        UnitTypeIds = [UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker],
        Units = [new(UnitTypeIds.ChiefMechanic, "Blue", new(1, 2)),
            new(UnitTypeIds.ChiefMechanic, "Red", new(6, 2))],
        Agency = [new(UnitTypeIds.ChiefMechanic, "Blue", ControllerKind.Automated),
            new(UnitTypeIds.ChiefMechanic, "Red", ControllerKind.Automated),
            new(UnitTypeIds.Worker, "Blue", ControllerKind.Automated),
            new(UnitTypeIds.Worker, "Red", ControllerKind.Automated)],
        Objectives = [new("Blue", ScenarioObjective.NoObjective), new("Red", ScenarioObjective.NoObjective)],
        Machines = [new("blue-press", new(2, 2), MachineTypes.DiamondPress) { OwnerSide = "Blue" },
            new("red-press", new(5, 2), MachineTypes.DiamondPress)
                { OwnerSide = "Red", StoredResources = [new(ResourceTypes.Coal, 3)] }],
        Resources = [new(ResourceTypes.Coal, new(1, 2)), new(ResourceTypes.Coal, new(0, 0)),
            new(ResourceTypes.Coal, new(0, 4)), new(ResourceTypes.Coal, new(7, 0)),
            new(ResourceTypes.Coal, new(7, 4))]
    };
}
