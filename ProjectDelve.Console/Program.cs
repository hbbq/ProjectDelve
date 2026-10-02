using ProjectDelve.Engine;
using ProjectDelve.ConsoleHost;

Console.WriteLine("Project Delve: exploratory rounds. Hero decisions are manual; Monster decisions are automatic.");
Console.WriteLine("Tokens and dice are rolled automatically. Coordinates start at top-left (0,0).");
Console.WriteLine("Detour scenario: walls and a closed door divide the board; rows 0 and 4 provide open routes.");
Console.WriteLine("Choose 0 to keep the Hero at (4,2) and observe the Monsters approach over successive rounds.");
Console.WriteLine("Barbarian faces two Grunts. The closed door blocks both sides; use the open end routes.");

var state = new GameState
{
    Physical = new PhysicalState(new Board(6, 5,
        [new Edge(new Cell(2, 1), new Cell(3, 1), EdgeKind.Wall),
            new Edge(new Cell(2, 2), new Cell(3, 2), EdgeKind.ClosedDoor),
            new Edge(new Cell(2, 3), new Cell(3, 3), EdgeKind.Wall),
            // The tempting cell (2,2) is a dead end, entered only from the left.
            new Edge(new Cell(2, 1), new Cell(2, 2), EdgeKind.Wall),
            new Edge(new Cell(2, 2), new Cell(2, 3), EdgeKind.Wall)]),
        [new Figure("hero", new Cell(4, 2)), new Figure("monster-1", new Cell(1, 2)),
            new Figure("monster-2", new Cell(1, 3))]),
    Types = [UnitType.Barbarian(), UnitType.Grunt()],
    Units = [new Unit("hero", "barbarian-type", "blue", 5), new Unit("monster-1", "grunt-type", "red", 1),
        new Unit("monster-2", "grunt-type", "red", 1)]
};
var manualDecisions = new ConsoleDecisionProvider();
var monsterDecisions = new DefaultMonsterProvider();
var random = new ConsoleRandomProvider();
ShowState(state);

try
{
    var result = GameEngine.StartRound(state, random);
    while (true)
    {
        ShowEvents(result.Events);
        ShowState(result.State);
        if (result.State.RoundComplete)
        {
            Console.WriteLine("Round complete.");
            while (true)
            {
                Console.Write("Press Enter to start the next round (q to quit): ");
                var input = Console.ReadLine();
                if (input is null || input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    throw new OperationCanceledException();
                if (input.Length == 0) break;
                Console.WriteLine("Press Enter or enter q.");
            }
            result = GameEngine.StartRound(result.State, random);
            continue;
        }
        IDecisionProvider decisions = result.NextInput!.TypeId == "grunt-type"
            ? monsterDecisions
            : manualDecisions;
        result = GameEngine.Advance(result.State, decisions, random);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Console session ended.");
}

static void ShowState(GameState state)
{
    Console.WriteLine();
    Console.WriteLine($"Round: {state.Round} | Complete: {state.RoundComplete} | Active type: {state.ActiveTypeId ?? "-"}");
    if (state.ActiveTypeId is not null)
        Console.WriteLine($"Move done: {state.MoveDone} | Action done: {state.ActionDone} | Current Unit: {state.CurrentUnitId ?? "-"} | Completed Units: {string.Join(", ", state.CompletedUnitIds)}");
    Console.WriteLine($"Bag: [{string.Join(", ", state.Bag)}]");
    var board = state.Physical.Board;
    Console.Write("     ");
    for (var x = 0; x < board.Width; x++) Console.Write($"{x,-4}");
    Console.WriteLine();
    Console.WriteLine("    +" + string.Concat(Enumerable.Repeat("---+", board.Width)));
    for (var y = 0; y < board.Height; y++)
    {
        Console.Write($"{y,2}  |");
        for (var x = 0; x < board.Width; x++)
        {
            var figure = state.Physical.Figures.FirstOrDefault(f => f.Position == new Cell(x, y));
            var symbol = figure?.Id switch
            {
                "hero" => "H",
                "monster-1" => "M1",
                "monster-2" => "M2",
                _ => "."
            };
            Console.Write($"{symbol,-3}");
            Console.Write(x == board.Width - 1 ? '|' : EdgeMarker(board, new Cell(x, y), new Cell(x + 1, y)));
        }
        Console.WriteLine();
        Console.Write("    +");
        for (var x = 0; x < board.Width; x++)
        {
            var marker = y == board.Height - 1 ? "---" :
                EdgeMarker(board, new Cell(x, y), new Cell(x, y + 1)) switch
                {
                    '#' => "###",
                    'D' => "-D-",
                    'o' => "-o-",
                    _ => "   "
                };
            Console.Write(marker + "+");
        }
        Console.WriteLine();
    }
    Console.WriteLine("H = hero, M1 = monster-1, M2 = monster-2, . = Floor");
    Console.WriteLine("# = wall, D = closed door (impassable), o = open door; blank edges are open.");
    foreach (var unit in state.Units)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var figure = state.Physical.Figures.FirstOrDefault(f => f.Id == unit.Id);
        var placement = figure is null ? "off board" : $"({figure.Position.X},{figure.Position.Y}), {figure.Posture}";
        Console.WriteLine($"{unit.Id}: side {unit.SideId}, HP {unit.CurrentHp}/{type.Hp}, {placement} | MOV {type.Mov}, RNG {type.Rng}, ATK {type.Atk}, DEF {type.Def}");
    }
    Console.WriteLine();
}

static char EdgeMarker(Board board, Cell a, Cell b) =>
    board.Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind switch
    {
        EdgeKind.Wall => '#',
        EdgeKind.ClosedDoor => 'D',
        EdgeKind.OpenDoor => 'o',
        _ => ' '
    };

static void ShowEvents(List<RulesEvent> events)
{
    Console.WriteLine();
    Console.WriteLine("Events:");
    if (events.Count == 0) Console.WriteLine("  (none)");
    foreach (var e in events)
    {
        var description = e.Kind switch
        {
            "TokenDrawn" => $"TokenDrawn: {e.TypeId}",
            "MovementCompleted" => $"MovementCompleted: {e.UnitId}, path {string.Join(" -> ", e.Path!.Select(c => $"({c.X},{c.Y})"))}",
            "AttackResolved" => $"AttackResolved: {e.UnitId} -> {e.TargetId}, Hits {e.Hits}, Blocks {e.Blocks}, Damage {e.Damage}",
            "DoorOpened" => $"DoorOpened: {e.UnitId}, ({e.Door!.A.X},{e.Door.A.Y}) <-> ({e.Door.B.X},{e.Door.B.Y})",
            "UnitDied" => $"UnitDied: {e.UnitId} (figure removed)",
            _ => e.Kind
        };
        Console.WriteLine($"  {description}");
    }
}
