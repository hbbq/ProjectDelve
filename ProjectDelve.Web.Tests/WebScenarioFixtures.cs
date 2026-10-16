using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Web.Tests;

// Test-owned setups. Never derive these from production scenario definitions or exports.
internal static class WebScenarioFixtures
{
    internal static ScenarioDefinition Combat() => Define(Map(5, 4),
        [Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 1, unitId: "hero")),
         Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated,
             At(3, 1, unitId: "target"), At(3, 2, unitId: "bystander"))]);

    internal static ScenarioDefinition Serialization() => Define(
        Map(5, 5, defaultTerrain: TerrainKind.Grass, cells: [Tile(3, 3, TerrainKind.StoneFloor)],
            edges: [Edge(1, 1, EdgeDirection.Right, EdgeKind.None), Edge(2, 1, EdgeDirection.Down, EdgeKind.OpenDoor)]),
        [Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(0, 0, unitId: "scout")),
         Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 2, Posture.Lying, initialHp: 2, unitId: "mage")),
         Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(4, 0, unitId: "guard")),
         Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated)],
        unitTypeIds: [UnitTypeIds.Goblin, UnitTypeIds.Wizard, UnitTypeIds.Grunt],
        orders: [new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForUnit("scout"))]) with
    {
        Objectives = [new("blue", ScenarioObjective.ReachArea)
            { Selectors = [UnitSelector.SideAtStart("blue")], Area = new(3, 3, 2, 2) },
            new("red", ScenarioObjective.DefeatUnits) { Count = 1, Selectors = [UnitSelector.SideAtStart("blue")] }],
        Resources = [new(ResourceTypes.Coal, new(1, 1))]
    };

    internal static ScenarioDefinition Shaman() => Define(Map(6, 4),
        [Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, At(2, 1)),
         Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated),
         Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(5, 3))]);

    internal static ScenarioDefinition Shaper() => Define(Map(9, 8,
        cells: [Tile(5, 3, TerrainKind.Water), Tile(6, 3, TerrainKind.Water)],
        edges: [Edge(4, 4, EdgeDirection.Right, EdgeKind.OpenDoor),
            Edge(3, 4, EdgeDirection.Down, EdgeKind.Wall), Edge(4, 2, EdgeDirection.Down, EdgeKind.ClosedDoor),
            Edge(2, 3, EdgeDirection.Right, EdgeKind.ClosedDoor), Edge(7, 6, EdgeDirection.Down, EdgeKind.WallWithWindow)]),
        [Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(1, 4)),
         Group(UnitTypeIds.LabyrinthShaper, "red", ControllerKind.Automated, At(4, 4))]);

    internal static ScenarioDefinition Dragon() => Define(Map(8, 5),
        [Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(0, 0)),
         Group(UnitTypeIds.RedDragon, "red", ControllerKind.Automated, At(5, 2))]);

    internal static ScenarioDefinition Resources() => Define(Map(4, 3),
        [Group(UnitTypeIds.Zombie, "red", ControllerKind.Human, At(0, 0, unitId: "carrier")),
         Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(3, 2))]) with
    { Resources = [new(ResourceTypes.Coal, new(0, 0)), new(new("test-ore", "Test Ore"), new(2, 1))] };

    internal static ScenarioDefinition Named(string name) => name switch
    {
        "combat" => Combat(), "serialization" => Serialization(), "shaman" => Shaman(),
        "shaper" => Shaper(), "dragon" => Dragon(), "resources" => Resources(),
        _ => throw new ArgumentException("Unknown test fixture.", nameof(name))
    };
}
