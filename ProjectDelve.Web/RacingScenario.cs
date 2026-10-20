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
        for (var x = 3; x <= 13; x++)
            if (x <= 3 || x >= 12 || y <= 4 || y >= 8)
                cells.Add(new(new(x, y), TerrainKind.Asphalt));
        cells.Add(new(new(8, 6), TerrainKind.Lava));
        var route = new FollowRoute("circuit");
        return new()
        {
            Board = new() { Width = 17, Height = 13, Cells = cells, DefaultTerrain = TerrainKind.Grass },
            Waypoints = [new("east-north", new(10, 2, 4, 3)), new("east-south", new(9, 8, 4, 3)),
                new("west-south", new(3, 8, 4, 3)), new("west-north", new(3, 2, 4, 3))],
            Routes = [new("circuit", ["east-north", "east-south", "west-south", "west-north"], RouteMode.Loop)],
            UnitTypeIds = [UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker],
            Units = [new(UnitTypeIds.DiamondKart, "Blue", new(7, 3)),
                new(UnitTypeIds.DiamondKart, "Red", new(6, 4)),
                new(UnitTypeIds.ChiefMechanic, "Blue", new(1, 6)),
                new(UnitTypeIds.ChiefMechanic, "Red", new(15, 6))],
            Agency = (from side in new[] { "Blue", "Red" }
                from type in new[] { UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker }
                select new AgencyAssignment(type, side, ControllerKind.Automated)).ToList(),
            Orders = [new(UnitTypeIds.DiamondKart, "Blue", OrderKind.FollowRoute, Route: route),
                new(UnitTypeIds.DiamondKart, "Red", OrderKind.FollowRoute, Route: route)],
            Objectives = [new("Blue", ScenarioObjective.NoObjective), new("Red", ScenarioObjective.NoObjective)],
            Machines = [new("blue-press", new(1, 5), MachineTypes.DiamondPress) { OwnerSide = "Blue" },
                new("red-press", new(15, 7), MachineTypes.DiamondPress) { OwnerSide = "Red" }],
            Resources = [],
            StoredResources = []};
    }
}
