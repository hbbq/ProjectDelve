using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class BananaPeelScenario
{
    internal static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 8, Height = 5,
            Cells = Enumerable.Range(0, 8).Select(x => new CellDefinition(new(x, 4), TerrainKind.Rail, RailDirection.East)).ToList(),
            Edges = Enumerable.Range(0, 7).Select(x => new EdgeDefinition(new(x, 0), EdgeDirection.Down, EdgeKind.Wall)).ToList() },
        UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Locomotive],
        Units = [new(UnitTypeIds.Barbarian, "blue", new(0, 0)), new(UnitTypeIds.Locomotive, "rail", new(0, 4))],
        Agency = [new(UnitTypeIds.Barbarian, "blue", ControllerKind.Human), new(UnitTypeIds.Locomotive, "rail", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("rail", ScenarioObjective.NoObjective)],
        NeutralSides = ["rail"],
        CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 0)), new(CellMarkerTypes.BananaPeel, new(3, 2)),
            new(CellMarkerTypes.BananaPeel, new(2, 4))]
    };
}
