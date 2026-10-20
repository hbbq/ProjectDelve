using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class FocusedRacingScenarios
{
    internal static ScenarioDefinition Definition(bool industry)
    {
        List<CellDefinition> cells = [];
        // A three-Cell-wide ring separates the orchards from both workshops.
        for (var y = 2; y <= 12; y++)
        for (var x = 3; x <= 17; x++)
            if (x <= 5 || x >= 15 || y <= 4 || y >= 10)
                cells.Add(new(new(x, y), TerrainKind.Asphalt));
        if (industry)
            foreach (var cell in new Cell[] { new(8, 6), new(12, 6), new(8, 8), new(12, 8) })
                cells.Add(new(cell, TerrainKind.FruitTree));
        var sides = new[] { "Blue", "Red" };
        var types = industry ? new[] { UnitTypeIds.DiamondKart, UnitTypeIds.ChiefMechanic, UnitTypeIds.Worker } : [UnitTypeIds.DiamondKart];
        List<UnitPlacement> units = [new(UnitTypeIds.DiamondKart, "Blue", new(8, 3)), new(UnitTypeIds.DiamondKart, "Red", new(9, 4))];
        if (industry)
            units.AddRange([new(UnitTypeIds.ChiefMechanic, "Blue", new(1, 7)), new(UnitTypeIds.ChiefMechanic, "Red", new(19, 7)),
                new(UnitTypeIds.Worker, "Blue", new(6, 6)), new(UnitTypeIds.Worker, "Blue", new(7, 8)),
                new(UnitTypeIds.Worker, "Red", new(14, 6)), new(UnitTypeIds.Worker, "Red", new(13, 8))]);
        return new()
        {
            Board = new() { Width = 21, Height = 15, DefaultTerrain = TerrainKind.Grass, Cells = cells },
            Waypoints = [new("north-east", new(15, 2, 3, 3)), new("south-east", new(15, 10, 3, 3)),
                new("south-west", new(3, 10, 3, 3)), new("north-west", new(3, 2, 3, 3))],
            Routes = [new("circuit", ["north-east", "south-east", "south-west", "north-west"], RouteMode.Loop)],
            UnitTypeIds = [.. types], Units = units,
            Agency = (from side in sides from type in types select new AgencyAssignment(type, side, ControllerKind.Automated)).ToList(),
            Orders = sides.Select(side => new InitialGroupOrder(UnitTypeIds.DiamondKart, side, OrderKind.FollowRoute, Route: new("circuit"))).ToList(),
            Objectives = sides.Select(side => new SideObjective(side, ScenarioObjective.NoObjective)).ToList(),
            StoredResources = (from side in sides from resource in new[] { ResourceTypes.Banana, ResourceTypes.Mushroom, ResourceTypes.Shell }
                select new StoredResource(side, UnitTypeIds.DiamondKart, resource, 1)).ToList(),
            Machines = industry ? [new("blue-slot", new(1, 6), MachineTypes.SlotMachine) { OwnerSide = "Blue", StoredResources = [new(ResourceTypes.Fruit, 3)] },
                new("red-slot", new(19, 8), MachineTypes.SlotMachine) { OwnerSide = "Red", StoredResources = [new(ResourceTypes.Fruit, 3)] }] : [],
            Resources = industry ? [new(ResourceTypes.Fruit, new(6, 6)), new(ResourceTypes.Fruit, new(14, 6)),
                new(ResourceTypes.Fruit, new(7, 8)), new(ResourceTypes.Fruit, new(13, 8))] : []
        };
    }
}
