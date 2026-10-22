using ProjectDelve.Engine;
using static ProjectDelve.Engine.Scenario;
using static ProjectDelve.Engine.EdgeDirection;

namespace ProjectDelve.Web;

public sealed record ScenarioDescription(string Id, string Name, string Description)
{
    public IReadOnlyDictionary<string, int> SideHues => Id == "genombrottet" ? new Dictionary<string, int> { ["side-1"] = 210, ["side-2"] = 0 } : new Dictionary<string, int>();
    public IReadOnlyDictionary<string,string> Controllers => PlaytestScenarios.Definition(Id).Agency.GroupBy(a => a.SideId).ToDictionary(g => g.Key, g => g.All(a => a.Controller == ControllerKind.Human) ? "Human" : "Default Behavior");
    public IReadOnlyList<ControllerSelection> ControllerAssignments => PlaytestScenarios.Definition(Id).Agency
        .Select(a => new ControllerSelection(a.UnitTypeId, a.SideId, a.Controller == ControllerKind.Human ? "Human" : "Default Behavior")).ToArray();
    public IReadOnlyDictionary<string,string> UnitTypeNames => PlaytestScenarios.Create(Id).Types
        .ToDictionary(type => type.Id, type => type.DisplayName ?? type.Id);
    public IReadOnlyList<ActivationToken> InitiallyPresentTokens => PlaytestScenarios.Definition(Id).Units
        .Select(unit => new ActivationToken(unit.UnitTypeId, unit.SideId)).Distinct().ToArray();
    public IReadOnlyList<string> SideIds => PlaytestScenarios.Definition(Id).Agency.Select(a => a.SideId).Distinct().ToArray();
}

// Catalog presentation stays separate from initial setup definitions.
public static class PlaytestScenarios
{
    public const string DefaultId = "basic-combat";
    public static IReadOnlyList<ScenarioDescription> Catalog { get; } = Array.AsReadOnly<ScenarioDescription>([
        new("chess", "Chess", "Defeat the opposing King."),
        new("delve-chess-peasants-rooks", "Delve Chess — Peasants and Rooks", "Two courts test straight Rook Moves, diagonal melee and Advance On Kill around Walls, Windows and Doors. A neutral Rogue opens Doors. Change controllers to compare manual and automated play."),
        new("delve-chess-phase-2", "Delve Chess — Phase 2: Bishops and Knights", "Both courts add diagonal corridors and single L-shaped leaps to Peasants and Rooks. Test blocked corners, a trapped Bishop, jumping, landing hazards and Advance On Kill. A neutral Rogue opens Doors."),
        new("simple-racing", "Simple Racing", "Two autonomous Diamond Karts share a circuit Route, collecting waypoint items and using Boost, Banana Peels and Shells. No victory objective."),
        new("racing-and-industry", "Racing and Industry", "Workers cross the racing circuit to collect orchard Fruit, supply owned Slot Machines outside, and deliver racing items to their Kart. No victory objective."),
        new("a-day-at-the-race-track", "A day at the race track", "Two autonomous Diamond Karts follow the same looping route around a winding Asphalt track. Chief Mechanics and Geomancers support each Side beside owned Diamond Presses."),
        new("diamond-kart-racing", "Köra runt banan och kasta banan", "Legacy racing and Diamond Press economy with shared Routes. Karts receive racing items from Waypoints; use Racing and Industry to exercise Fruit and Slot Machines. No victory objectives."),
        new("chief-mechanic-production", "Chief Mechanic Production", "Two Sides each field a Chief Mechanic. Watch Summon Worker take priority over delivery and operation until each Side reaches three Workers. Asphalt workshop; no victory objectives."),
        new("banana-peel", "Banana Peel", "Move along the top corridor: the Peel is a legal destination but cannot be crossed. The Locomotive slips on its second Run Rails step. No victory objectives."),
        new("railway", "Railway", "A neutral Locomotive runs four Rail steps each activation. Upright and Lying figures demonstrate collisions; move the Dragon to clear the loop and watch across rounds."),
        new(DefaultId, "Basic Combat", "Barbarian against Grunts: movement, Normal Attack and activation flow."),
        new("goblins", "Goblins", "Two Heroes, flanking opportunities and space for Goblins to retreat."),
        new("archers", "Archers", "Mixed melee and ranged enemies around broken sight lines."),
        new("wizard-doors", "Wizard / Doors", "Wizard control and ranged encounters beside a sealed room: Zombies try Doors while a Ghost phases through Walls to reach normal attack positions."),
        new("resource-storage", "Resource Pickup / Give", "Manually control a Coal Collector and Coal Courier to exercise Pickup and Give with shared Unit Type storage."),
        new("geomancer-production", "Geomancer Production", "Autonomous Coal to Troll production: Geomancer summons Workers, Workers supply the Diamond Press and deliver Diamonds, then Geomancer summons Trolls."),
        new("lava-production", "Lava Production", "Renewable autonomous production: Lava creates Coal, Workers supply and operate the Diamond Press, and Geomancer summons Workers and Trolls. Eastern defenders guard the approach."),
        new("autonomous-production", "Autonomous Production", "Workers collect Coal, supply and operate the Diamond Press, then deliver Diamond to the Geomancer."),
        new("machine-ownership", "Machine Ownership", "Blue and Red Workers ignore opposing owned Presses. Each Side has its own Press; the central public Press is usable by both. Watch deliveries and operations across rounds."),
        new("diamond-press", "Diamond Press", "Manual production: transport Coal with the Courier and Give it to the Press. Operate from an empty Cell, then move away so Geomancer can collect the Diamond. The Closed Door blocks the western operating position until opened."),
        new("resource-transport", "Resource Transport", "Wizard / Doors with Coal under Zombies, along their routes and near Doors to playtest atomic Resource transport."),
        new("full-party-trolls", "Full Party / Trolls", "All four Heroes, Undying Trolls and encounters across rooms and Doors."),
        new("shaman-hunt", "Shaman Hunt", "Pursue a fleeing Shaman as it spawns Goblins, with a 2x2 Red Dragon guarding the broad eastern approach."),
        new("orders-playground", "Orders Playground", "Compare standing Orders: an idle Grunt, a holding Archer, and Zombie and Goblin groups moving toward marked Cells."),
        new("orders-assault", "Orders: Assault", "Observe a specific-Hero Attack, a Side-wide Attack, and a Zombie completing a standing Door Order."),
        new("orders-escort-retreat", "Orders: Escort & Retreat", "Observe a moving escort target, its following protector, and a group retreating from nearby Heroes."),
        new("labyrinth-shaper", "Labyrinth Shaper (digital-only)", "Digital-only encounter: approach the Labyrinth Shaper as it rebuilds a 3x3 room before moving. Old Walls and Doors disappear, four open gaps remain, and Water prevents a Wall on the northeast side."),
        new("genombrottet", "Genombrottet (The Breakthrough)", "Get all three Blue Heroes into the eastern exit area together. Red wins by defeating one initial Hero.")
    ]);

    public static GameState Create(string id) => GameEngine.CreateGame(Definition(id));

    public static ScenarioDefinition Definition(string id) => id switch
    {
        "chess" => DelveChessScenario.ChessDefinition(),
        "delve-chess-peasants-rooks" => DelveChessScenario.Definition(),
        "delve-chess-phase-2" => DelveChessScenario.Phase2Definition(),
        "simple-racing" => FocusedRacingScenarios.Definition(false),
        "racing-and-industry" => FocusedRacingScenarios.Definition(true),
        "a-day-at-the-race-track" => ADayAtTheRaceTrackScenario.Definition(),
        "diamond-kart-racing" => RacingScenario.Definition(),
        "chief-mechanic-production" => ChiefMechanicProductionScenario.Definition(),
        "banana-peel" => BananaPeelScenario.Definition(),
        "railway" => RailwayScenario.Definition(),
        DefaultId => BasicCombat(),
        "goblins" => Goblins(),
        "archers" => Archers(),
        "wizard-doors" => WizardDoors(),
        "resource-storage" => ResourceStoragePlaytest(),
        "geomancer-production" => GeomancerProduction(),
        "lava-production" => LavaProduction(),
        "autonomous-production" => AutonomousProduction(),
        "machine-ownership" => MachineOwnership(),
        "diamond-press" => DiamondPressPlaytest(),
        "resource-transport" => WizardDoors() with
        {
            Resources = [new(ResourceTypes.Coal, new(8, 3)), new(ResourceTypes.Coal, new(10, 5)),
                new(ResourceTypes.Coal, new(9, 3)), new(ResourceTypes.Coal, new(7, 3)),
                new(ResourceTypes.Coal, new(10, 6)), new(ResourceTypes.Coal, new(8, 6))]
        },
        "full-party-trolls" => FullParty(),
        "shaman-hunt" => ShamanHunt(),
        "orders-playground" => OrdersPlayground(),
        "orders-assault" => OrdersAssault(),
        "orders-escort-retreat" => OrdersEscortRetreat(),
        "labyrinth-shaper" => LabyrinthShaper(),
        "genombrottet" => GenombrottetScenario.Definition(),
        _ => throw new ArgumentException("Unknown playtest scenario.", nameof(id))
    };

    private static ScenarioDefinition MachineOwnership() => new()
    {
        Board = new() { Width = 11, Height = 7 },
        UnitTypeIds = [UnitTypeIds.Worker],
        Units = [new(UnitTypeIds.Worker, "blue", new(7, 2)), new(UnitTypeIds.Worker, "blue", new(1, 5)),
            new(UnitTypeIds.Worker, "red", new(3, 4)), new(UnitTypeIds.Worker, "red", new(9, 1))],
        Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Worker, "red", ControllerKind.Automated)],
        Machines = [new("blue-press", new(2, 3), MachineTypes.DiamondPress) { OwnerSide = "blue" },
            new("red-press", new(8, 3), MachineTypes.DiamondPress) { OwnerSide = "red" },
            new("public-press", new(5, 3), MachineTypes.DiamondPress)],
        Resources = [new(ResourceTypes.Coal, new(7, 2)), new(ResourceTypes.Coal, new(1, 5)),
            new(ResourceTypes.Coal, new(3, 4)), new(ResourceTypes.Coal, new(9, 1)),
            new(ResourceTypes.Coal, new(1, 1)), new(ResourceTypes.Coal, new(9, 5)),
            new(ResourceTypes.Coal, new(5, 1)), new(ResourceTypes.Coal, new(5, 5)),
            new(ResourceTypes.Coal, new(4, 3))]
    };

    private static ScenarioDefinition ResourceStoragePlaytest() => new()
    {
        Board = new() { Width = 7, Height = 5 },
        UnitTypeIds = ["coal-collector", UnitTypeIds.Zombie, UnitTypeIds.Grunt],
        UnitTypes = [UnitType.Define("coal-collector", "Coal Collector", UnitAuthoring.Stats(2, 0, 0, 3, 1), UnitAuthoring.CantAttack(), UnitAuthoring.ResourceStorage(ResourceTypes.Coal, 3), UnitAuthoring.PickupResource()), UnitRoster.Zombie() with { Actions = UnitAction.NormalAttack | UnitAction.GiveResource, DisplayName = "Coal Courier" }],
        Units = [new("coal-collector", "blue", new(2, 2)), new(UnitTypeIds.Zombie, "blue", new(3, 2)),
            new(UnitTypeIds.Grunt, "red", new(6, 4))],
        Agency = [new("coal-collector", "blue", ControllerKind.Human), new(UnitTypeIds.Zombie, "blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Resources = [new(ResourceTypes.Coal, new(2, 2)), new(ResourceTypes.Coal, new(3, 2)),
            new(ResourceTypes.Coal, new(3, 1)), new(ResourceTypes.Coal, new(2, 3)), new(ResourceTypes.Coal, new(1, 2))]
    };

    private static ScenarioDefinition GeomancerProduction() => new()
    {
        Board = new() { Width = 14, Height = 10 },
        Machines = [new("press", new(4, 3), MachineTypes.DiamondPress)],
        UnitTypeIds = [UnitTypeIds.Worker, UnitTypeIds.Geomancer, UnitTypeIds.Troll,
            UnitTypeIds.Grunt, UnitTypeIds.SkeletonArcher],
        Units = [new(UnitTypeIds.Worker, "blue", new(2, 3)),
            new(UnitTypeIds.Geomancer, "blue", new(1, 1)),
            new(UnitTypeIds.Grunt, "red", new(12, 7)), new(UnitTypeIds.Grunt, "red", new(11, 8)),
            new(UnitTypeIds.SkeletonArcher, "red", new(12, 9))],
        Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Geomancer, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Troll, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Automated),
            new(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated)],
        Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.HoldPosition),
            new(UnitTypeIds.SkeletonArcher, "red", OrderKind.HoldPosition)],
        Resources = [new(ResourceTypes.Coal, new(2, 3)), new(ResourceTypes.Coal, new(3, 2)),
            new(ResourceTypes.Coal, new(3, 4)), new(ResourceTypes.Coal, new(5, 2)),
            new(ResourceTypes.Coal, new(5, 4)), new(ResourceTypes.Coal, new(4, 5)),
            new(ResourceTypes.Coal, new(6, 3)), new(ResourceTypes.Coal, new(2, 5))]
    };

    private static ScenarioDefinition LavaProduction() => new()
    {
        Board = new() { Width = 14, Height = 10,
            Cells = [Tile(1, 3, TerrainKind.Lava), Tile(7, 4, TerrainKind.Tree), Tile(7, 5, TerrainKind.Tree)] },
        Machines = [new("press", new(4, 3), MachineTypes.DiamondPress)],
        UnitTypeIds = [UnitTypeIds.Worker, UnitTypeIds.Geomancer, UnitTypeIds.Troll,
            UnitTypeIds.Grunt, UnitTypeIds.SkeletonArcher],
        Units = [new(UnitTypeIds.Worker, "blue", new(2, 3)),
            new(UnitTypeIds.Geomancer, "blue", new(4, 1)),
            new(UnitTypeIds.Grunt, "red", new(12, 7)), new(UnitTypeIds.Grunt, "red", new(11, 8)),
            new(UnitTypeIds.SkeletonArcher, "red", new(12, 9))],
        Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Geomancer, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Troll, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Automated),
            new(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated)],
        Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.HoldPosition),
            new(UnitTypeIds.SkeletonArcher, "red", OrderKind.HoldPosition)]
    };

    private static ScenarioDefinition AutonomousProduction() => new()
    {
        Board = new() { Width = 10, Height = 7 },
        Machines = [new("press", new(5, 3), MachineTypes.DiamondPress)],
        UnitTypeIds = [UnitTypeIds.Worker, UnitTypeIds.Geomancer, UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Worker, "blue", new(1, 1)),
            new(UnitTypeIds.Worker, "blue", new(1, 3)), new(UnitTypeIds.Worker, "blue", new(1, 5)),
            new(UnitTypeIds.Geomancer, "blue", new(8, 3)), new(UnitTypeIds.Grunt, "red", new(9, 6))],
        Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Automated)],
        Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing)],
        Resources = [new(ResourceTypes.Coal, new(2, 1)), new(ResourceTypes.Coal, new(2, 3)),
            new(ResourceTypes.Coal, new(2, 5)), new(ResourceTypes.Coal, new(3, 0)),
            new(ResourceTypes.Coal, new(3, 6)), new(ResourceTypes.Coal, new(0, 3))]
    };

    private static ScenarioDefinition DiamondPressPlaytest() => new()
    {
        Board = new() { Width = 8, Height = 6, Edges = [new(new(3, 2), Right, EdgeKind.ClosedDoor)] },
        Machines = [new("press", new(4, 2), MachineTypes.DiamondPress)],
        UnitTypeIds = ["coal-courier", "operator", UnitTypeIds.Geomancer, UnitTypeIds.Grunt],
        UnitTypes = [
            UnitType.Define("coal-courier", "Coal Courier", UnitAuthoring.Stats(3, 0, 0, 1, 1),
                UnitAuthoring.CantAttack(), UnitAuthoring.TransportResource(), UnitAuthoring.GiveResource()),
            UnitType.Define("operator", "Press Operator", UnitAuthoring.Stats(3, 0, 0, 1, 1),
                UnitAuthoring.CantAttack(), UnitAuthoring.Operate(), UnitAuthoring.OpenDoor())],
        Units = [new("coal-courier", "blue", new(2, 2)), new("operator", "blue", new(3, 2)),
            new(UnitTypeIds.Geomancer, "blue", new(3, 4)), new(UnitTypeIds.Grunt, "red", new(7, 5))],
        Agency = [new("coal-courier", "blue", ControllerKind.Human), new("operator", "blue", ControllerKind.Human),
            new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Resources = [new(ResourceTypes.Coal, new(2, 2)), new(ResourceTypes.Coal, new(2, 1)), new(ResourceTypes.Coal, new(2, 3))]
    };

    private static ScenarioDefinition BasicCombat() => Define(
        board: Map(8, 8, cells: [
            Tile(3, 2, TerrainKind.Tree),
            Tile(3, 3, TerrainKind.Grass),
            Tile(4, 5, TerrainKind.StoneFloorWithTable)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 4)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(4, 4), At(4, 3), At(6, 6))
        ]);

    private static ScenarioDefinition Goblins() => Define(
        board: Map(10, 10, cells: [
            Tile(4, 2, TerrainKind.Tree),
            Tile(4, 3, TerrainKind.Grass),
            Tile(5, 7, TerrainKind.Water),
            Tile(6, 7, TerrainKind.Water),
            Tile(2, 6, TerrainKind.StoneFloorWithTable)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 4)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 5)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 4), At(6, 6)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(4, 5), At(7, 3))
        ]);

    private static ScenarioDefinition Archers() => Define(
        board: Map(12, 12, cells: [
            .. Tiles(TerrainKind.Tree, At(5, 3), At(5, 4)),
            .. Tiles(TerrainKind.StoneFloorWithTable, At(7, 8), At(8, 8))
        ], edges: [
            .. Vertical(6, 5, 7, EdgeKind.Wall),
            Edge(6, 6, Right, EdgeKind.WallWithWindow)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 5)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 5), At(7, 7)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(5, 6), At(8, 3)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(6, 2), At(8, 6))
        ]);

    private static ScenarioDefinition WizardDoors() => Define(
        board: Map(15, 15, cells: [
            Tile(5, 5, TerrainKind.Tree),
            Tile(7, 9, TerrainKind.StoneFloorWithTable),
            Tile(11, 11, TerrainKind.Tree)
        ], edges: [
            .. Room(8, 1, 12, 5),
            Edge(7, 3, Right, EdgeKind.ClosedDoor),
            Edge(10, 5, Down, EdgeKind.ClosedDoor),
            .. Horizontal(9, 1, 5, EdgeKind.Wall),
            Edge(3, 9, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(3, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(6, 6), At(7, 8)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(7, 4), At(10, 8)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 3), At(10, 5)),
            Group(UnitTypeIds.Ghost, "red", ControllerKind.Automated, At(8, 5))
        ]);

    private static ScenarioDefinition FullParty() => Define(
        board: Map(15, 15, cells: [
            .. Tiles(TerrainKind.Tree, At(5, 4), At(3, 12)),
            Tile(6, 10, TerrainKind.StoneFloorWithTable)
        ], edges: [
            .. Room(8, 1, 12, 5),
            Edge(7, 3, Right, EdgeKind.ClosedDoor),
            Edge(10, 5, Down, EdgeKind.ClosedDoor),
            .. Room(9, 9, 13, 13),
            Edge(8, 11, Right, EdgeKind.ClosedDoor),
            Edge(11, 13, Down, EdgeKind.ClosedDoor),
            .. Vertical(4, 0, 3, EdgeKind.Wall),
            Edge(4, 2, Right, EdgeKind.ClosedDoor),
            .. Horizontal(9, 0, 4, EdgeKind.Wall),
            Edge(2, 9, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(3, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(2, 7)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(6, 6), At(7, 7)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(6, 3), At(8, 8)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 3), At(10, 5)),
            Group(UnitTypeIds.Troll, "red", ControllerKind.Automated, At(9, 11), At(11, 9))
        ]);

    private static ScenarioDefinition ShamanHunt() => Define(
        board: Map(15, 15, cells: [
            .. Tiles(TerrainKind.Tree, At(6, 6), At(7, 6)),
            Tile(6, 7, TerrainKind.StoneFloorWithTable),
            Tile(10, 10, TerrainKind.Tree),
            .. Tiles(TerrainKind.Water, At(3, 11), At(4, 11))
        ], edges: [
            .. Vertical(4, 0, 4, EdgeKind.Wall),
            Edge(4, 2, Right, EdgeKind.OpenDoor),
            .. Vertical(9, 8, 14, EdgeKind.Wall),
            Edge(9, 11, Right, EdgeKind.OpenDoor),
            .. Horizontal(4, 7, 13, EdgeKind.Wall),
            Edge(10, 4, Down, EdgeKind.OpenDoor),
            .. Horizontal(10, 0, 6, EdgeKind.Wall),
            Edge(2, 10, Down, EdgeKind.OpenDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 6)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 7)),
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(2, 7)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(1, 7)),
            Group(UnitTypeIds.Shaman, "red", ControllerKind.Automated, At(7, 5)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(5, 8)),
            Group(UnitTypeIds.RedDragon, "red", ControllerKind.Automated, At(11, 5))
        ]);

    private static ScenarioDefinition OrdersPlayground() => Define(
        board: Map(10, 10, cells: [
            Tile(5, 6, TerrainKind.Grass),
            Tile(5, 9, TerrainKind.Grass)
        ], edges: [
            .. Vertical(7, 4, 8, EdgeKind.Wall)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(3, 2)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(5, 5)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(3, 3)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(7, 2)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(9, 6)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(1, 6))
        ],
        orders: [
            new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing),
            new(UnitTypeIds.SkeletonArcher, "red", OrderKind.HoldPosition),
            new(UnitTypeIds.Zombie, "red", OrderKind.MoveTo, new(5, 9)),
            new(UnitTypeIds.Goblin, "red", OrderKind.MoveTo, new(5, 6))
        ]);

    private static ScenarioDefinition OrdersAssault() => Define(
        board: Map(11, 11, cells: [
            Tile(4, 1, TerrainKind.Tree),
            Tile(4, 6, TerrainKind.StoneFloorWithTable)
        ], edges: [
            .. Vertical(5, 0, 3, EdgeKind.Wall),
            .. Vertical(5, 5, 10, EdgeKind.Wall),
            Edge(5, 4, Right, EdgeKind.ClosedDoor)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 2, unitId: "assault-hero")),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(2, 5, unitId: "assault-rogue")),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(8, 2)),
            Group(UnitTypeIds.SkeletonArcher, "red", ControllerKind.Automated, At(8, 6)),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(8, 4))
        ],
        orders: [
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack,
                Target: OrderTarget.ForUnit("assault-hero")),
            new(UnitTypeIds.SkeletonArcher, "red", OrderKind.Attack,
                Target: OrderTarget.ForSide("blue")),
            new(UnitTypeIds.Zombie, "red", OrderKind.InteractWith,
                Target: OrderTarget.ForDoor(new(5, 4), new(6, 4)))
        ]);

    private static ScenarioDefinition OrdersEscortRetreat() => Define(
        board: Map(12, 12, cells: [
            Tile(3, 3, TerrainKind.Grass),
            Tile(6, 5, TerrainKind.Tree)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(2, 5)),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(3, 6)),
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(9, 3, unitId: "escort-leader")),
            Group(UnitTypeIds.Zombie, "red", ControllerKind.Automated, At(10, 4)),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated, At(5, 5), At(6, 6))
        ],
        orders: [
            new(UnitTypeIds.Grunt, "red", OrderKind.MoveTo, new(3, 3)),
            new(UnitTypeIds.Zombie, "red", OrderKind.Protect,
                Target: OrderTarget.ForUnit("escort-leader")),
            new(UnitTypeIds.Goblin, "red", OrderKind.Retreat)
        ]);

    private static ScenarioDefinition LabyrinthShaper() => Define(
        board: Map(9, 9, cells: [
            .. Tiles(TerrainKind.Water, At(5, 3), At(6, 3))
        ], edges: [
            Edge(4, 4, Right, EdgeKind.OpenDoor),
            Edge(3, 4, Down, EdgeKind.Wall),
            Edge(4, 2, Down, EdgeKind.ClosedDoor),
            Edge(2, 3, Right, EdgeKind.ClosedDoor),
            Edge(7, 6, Down, EdgeKind.WallWithWindow)
        ]),
        groups: [
            Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 4)),
            Group(UnitTypeIds.Cleric, "blue", ControllerKind.Human, At(1, 5)),
            Group(UnitTypeIds.LabyrinthShaper, "red", ControllerKind.Automated, At(4, 4))
        ]);
}
