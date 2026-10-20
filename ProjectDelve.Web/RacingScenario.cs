using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class RacingScenario
{
    internal static ScenarioDefinition Definition()
    {
        // Broad corners allow Lookahead to choose an exit line. The workshop floors
        // connect to the central Coal source only by crossing the Asphalt circuit.
        var cells = new List<CellDefinition>();
        for (var y = 2; y <= 10; y++)
        for (var x = 3; x <= 15; x++)
            if (x <= 5 || x >= 13 || y <= 4 || y >= 8)
                cells.Add(new(new(x, y), TerrainKind.Asphalt));
        cells.Add(new(new(9, 6), TerrainKind.Lava));
        var route = new FollowRoute([new(12, 2, 4, 3), new(13, 7, 3, 4),
            new(3, 8, 4, 3), new(3, 2, 3, 4)], RouteMode.Loop);
        return new()
        {
            Board = new() { Width = 19, Height = 13, Cells = cells },
            UnitTypeIds = [UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker],
            Units = [new(UnitTypeIds.DiamondKart, "Blue", new(6, 3)),
                new(UnitTypeIds.DiamondKart, "Red", new(12, 9)),
                new(UnitTypeIds.ChiefMechanic, "Blue", new(1, 6)),
                new(UnitTypeIds.ChiefMechanic, "Red", new(17, 6)),
                new(UnitTypeIds.Worker, "Blue", new(8, 5)), new(UnitTypeIds.Worker, "Blue", new(7, 7)),
                new(UnitTypeIds.Worker, "Red", new(10, 7)), new(UnitTypeIds.Worker, "Red", new(11, 5))],
            Agency = (from side in new[] { "Blue", "Red" }
                from type in new[] { UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker }
                select new AgencyAssignment(type, side, ControllerKind.Automated)).ToList(),
            Orders = [new(UnitTypeIds.DiamondKart, "Blue", OrderKind.FollowRoute, Route: route),
                new(UnitTypeIds.DiamondKart, "Red", OrderKind.FollowRoute, Route: route with { CurrentIndex = 2 })],
            Objectives = [new("Blue", ScenarioObjective.NoObjective), new("Red", ScenarioObjective.NoObjective)],
            Machines = [new("blue-press", new(1, 5), MachineTypes.DiamondPress) { OwnerSide = "Blue" },
                new("red-press", new(17, 7), MachineTypes.DiamondPress) { OwnerSide = "Red" }],
            Resources = [new(ResourceTypes.Coal, new(8, 5)), new(ResourceTypes.Coal, new(10, 7)),
                new(ResourceTypes.Coal, new(7, 7)), new(ResourceTypes.Coal, new(11, 5))],
            StoredResources = [new("Blue", UnitTypeIds.DiamondKart, ResourceTypes.Diamond, 1),
                new("Red", UnitTypeIds.DiamondKart, ResourceTypes.Diamond, 1)]
        };
    }
}
