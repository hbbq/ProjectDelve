using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class DelveChessScenario
{
    internal static ScenarioDefinition Phase2Definition()
    {
        var edges = new List<EdgeDefinition>
        {
            // SE from (1,1) has only the lower corner route; from (2,2)
            // both SE routes are blocked, despite the open space beyond.
            new(new(1, 1), EdgeDirection.Right, EdgeKind.Wall),
            new(new(2, 2), EdgeDirection.Right, EdgeKind.Wall),
            new(new(2, 2), EdgeDirection.Down, EdgeKind.Wall),
            new(new(7, 2), EdgeDirection.Right, EdgeKind.WallWithWindow),
            new(new(8, 2), EdgeDirection.Down, EdgeKind.ClosedDoor),
            new(new(7, 3), EdgeDirection.Right, EdgeKind.OpenDoor),
            // A Knight can leap out across this Wall and neighboring Water.
            new(new(4, 7), EdgeDirection.Right, EdgeKind.Wall),
            new(new(4, 6), EdgeDirection.Down, EdgeKind.WallWithWindow)
        };
        // One-cell-wide straight lane: the Bishop at (10,2) cannot leave.
        for (var y = 0; y <= 4; y++)
        {
            edges.Add(new(new(9, y), EdgeDirection.Right, EdgeKind.Wall));
            edges.Add(new(new(10, y), EdgeDirection.Right, EdgeKind.Wall));
        }
        return new()
        {
            Board = new()
            {
                Width = 12, Height = 10, Edges = edges,
                Cells = [new(new(5, 7), TerrainKind.Water), new(new(4, 6), TerrainKind.Water),
                    new(new(8, 5), TerrainKind.Tree)]
            },
            UnitTypeIds = [UnitTypeIds.RoyalBishop, UnitTypeIds.RoyalKnight, UnitTypeIds.CastleGuard,
                UnitTypeIds.RoyalPeasant, UnitTypeIds.GoblinBishop, UnitTypeIds.HobbyKnight,
                UnitTypeIds.TowerTroll, UnitTypeIds.GoblinPeasant, UnitTypeIds.Rogue],
            NeutralSides = ["gatekeeper"],
            Objectives = [new("gatekeeper", ScenarioObjective.NoObjective)],
            CellMarkers = [new(CellMarkerTypes.BananaPeel, new(7, 4)), new(CellMarkerTypes.BananaPeel, new(6, 8))],
            Units = [
                new(UnitTypeIds.RoyalBishop, "blue", new(1, 1)),
                new(UnitTypeIds.RoyalBishop, "blue", new(2, 5)),
                new(UnitTypeIds.RoyalKnight, "blue", new(4, 7)),
                new(UnitTypeIds.CastleGuard, "blue", new(0, 5)),
                new(UnitTypeIds.RoyalPeasant, "blue", new(3, 7)),
                new(UnitTypeIds.RoyalPeasant, "blue", new(6, 6)),
                new(UnitTypeIds.GoblinBishop, "red", new(8, 2)),
                new(UnitTypeIds.GoblinBishop, "red", new(10, 2)),
                new(UnitTypeIds.HobbyKnight, "red", new(9, 7)),
                new(UnitTypeIds.TowerTroll, "red", new(11, 8)),
                // Orthogonal victim lets the first Bishop change color on kill.
                new(UnitTypeIds.GoblinPeasant, "red", new(0, 1)),
                new(UnitTypeIds.GoblinPeasant, "red", new(4, 8)),
                new(UnitTypeIds.GoblinPeasant, "red", new(5, 5)),
                new(UnitTypeIds.Rogue, "gatekeeper", new(8, 3))
            ],
            Agency = [
                new(UnitTypeIds.RoyalBishop, "blue", ControllerKind.Human),
                new(UnitTypeIds.RoyalKnight, "blue", ControllerKind.Human),
                new(UnitTypeIds.CastleGuard, "blue", ControllerKind.Human),
                new(UnitTypeIds.RoyalPeasant, "blue", ControllerKind.Human),
                new(UnitTypeIds.GoblinBishop, "red", ControllerKind.Automated),
                new(UnitTypeIds.HobbyKnight, "red", ControllerKind.Automated),
                new(UnitTypeIds.TowerTroll, "red", ControllerKind.Automated),
                new(UnitTypeIds.GoblinPeasant, "red", ControllerKind.Automated),
                new(UnitTypeIds.Rogue, "gatekeeper", ControllerKind.Human)
            ]
        };
    }

    internal static ScenarioDefinition Definition() => new()
    {
        Board = new()
        {
            Width = 12, Height = 7,
            Edges = [
                new(new(3, 1), EdgeDirection.Right, EdgeKind.Wall),
                new(new(3, 1), EdgeDirection.Down, EdgeKind.Wall),
                new(new(5, 2), EdgeDirection.Right, EdgeKind.WallWithWindow),
                new(new(5, 3), EdgeDirection.Right, EdgeKind.ClosedDoor),
                new(new(5, 4), EdgeDirection.Right, EdgeKind.Wall),
                new(new(8, 4), EdgeDirection.Down, EdgeKind.WallWithWindow),
                new(new(7, 5), EdgeDirection.Right, EdgeKind.ClosedDoor)
            ]
        },
        UnitTypeIds = [UnitTypeIds.RoyalPeasant, UnitTypeIds.GoblinPeasant, UnitTypeIds.CastleGuard, UnitTypeIds.TowerTroll, UnitTypeIds.Rogue],
        // A neutral existing door-opener lets players change passages without
        // adding a door capability to either Chess role.
        NeutralSides = ["gatekeeper"],
        Objectives = [new("gatekeeper", ScenarioObjective.NoObjective)],
        Units = [
            new(UnitTypeIds.CastleGuard, "blue", new(1, 0)),
            new(UnitTypeIds.CastleGuard, "blue", new(5, 2)),
            new(UnitTypeIds.RoyalPeasant, "blue", new(2, 3)),
            new(UnitTypeIds.RoyalPeasant, "blue", new(7, 4)),
            new(UnitTypeIds.RoyalPeasant, "blue", new(7, 5)),
            new(UnitTypeIds.TowerTroll, "red", new(10, 6)),
            new(UnitTypeIds.TowerTroll, "red", new(8, 5)),
            new(UnitTypeIds.GoblinPeasant, "red", new(6, 2)),
            new(UnitTypeIds.GoblinPeasant, "red", new(6, 4)),
            new(UnitTypeIds.GoblinPeasant, "red", new(8, 4)),
            new(UnitTypeIds.Rogue, "gatekeeper", new(5, 3))
        ],
        Agency = [
            new(UnitTypeIds.CastleGuard, "blue", ControllerKind.Human),
            new(UnitTypeIds.RoyalPeasant, "blue", ControllerKind.Human),
            new(UnitTypeIds.TowerTroll, "red", ControllerKind.Automated),
            new(UnitTypeIds.GoblinPeasant, "red", ControllerKind.Automated),
            new(UnitTypeIds.Rogue, "gatekeeper", ControllerKind.Human)
        ]
    };
}
