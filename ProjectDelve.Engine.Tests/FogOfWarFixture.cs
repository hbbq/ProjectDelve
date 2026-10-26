using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Testing;

// Test-owned Sleeping Dungeon: a starting chamber, a closed door and a running world beyond it.
// Deliberately independent of mutable playtest catalog content.
internal static class FogOfWarFixture
{
    internal static ScenarioDefinition SleepingDungeon(bool enabled = true, ControllerKind controller = ControllerKind.Human) =>
        Define(Map(20, 9, edges: [.. Vertical(5, 0, 8, EdgeKind.Wall),
            Edge(5, 4, EdgeDirection.Right, EdgeKind.ClosedDoor)]),
        [Group(UnitTypeIds.Barbarian, "explorers", controller, At(3, 4, unitId: "hero")),
         Group(UnitTypeIds.Grunt, "dungeon", ControllerKind.Automated,
             At(8, 4, unitId: "guard"), At(17, 4, Posture.Lying, unitId: "sleeper")),
         Group(UnitTypeIds.SkeletonArcher, "dungeon", ControllerKind.Automated, At(15, 6, unitId: "archer"))]) with
        {
            FogOfWarSides = enabled ? ["explorers"] : null,
            Objectives = [new("explorers", ScenarioObjective.NoObjective), new("dungeon", ScenarioObjective.NoObjective)]
        };
}
