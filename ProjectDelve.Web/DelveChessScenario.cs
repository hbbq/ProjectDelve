using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal static class DelveChessScenario
{
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
