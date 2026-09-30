using ProjectDelve.Engine;
using ProjectDelve.ConsoleHost;

Console.WriteLine("Project Delve: exploratory rounds. Hero decisions are manual; Monster decisions are automatic.");
Console.WriteLine("Tokens and dice are rolled automatically. Coordinates start at top-left (0,0).");

var state = new GameState
{
    Physical = new PhysicalState(new Board(5, 5, []),
        [new Figure("hero", new Cell(1, 2)), new Figure("monster-1", new Cell(3, 2)),
            new Figure("monster-2", new Cell(3, 3))]),
    Types = [new UnitType("hero-type", 2, 1, 1, 0, 2), new UnitType("monster-type", 2, 1, 1, 1, 1)],
    Units = [new Unit("hero", "hero-type", "blue", 2), new Unit("monster-1", "monster-type", "red", 1),
        new Unit("monster-2", "monster-type", "red", 1)]
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
        IDecisionProvider decisions = result.NextInput!.TypeId == "monster-type"
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
        Console.WriteLine($"Phase: {state.Phase} | Current Unit: {state.CurrentUnitId ?? "-"} | Completed Units: {string.Join(", ", state.CompletedUnitIds)}");
    Console.WriteLine($"Bag: [{string.Join(", ", state.Bag)}]");
    Console.Write("    ");
    for (var x = 0; x < state.Physical.Board.Width; x++) Console.Write($"{x,-3}");
    Console.WriteLine();
    for (var y = 0; y < state.Physical.Board.Height; y++)
    {
        Console.Write($" {y}  ");
        for (var x = 0; x < state.Physical.Board.Width; x++)
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
        }
        Console.WriteLine();
    }
    Console.WriteLine("H = hero, M1 = monster-1, M2 = monster-2, . = Floor");
    foreach (var unit in state.Units)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var figure = state.Physical.Figures.FirstOrDefault(f => f.Id == unit.Id);
        var placement = figure is null ? "off board" : $"({figure.Position.X},{figure.Position.Y}), {figure.Posture}";
        Console.WriteLine($"{unit.Id}: side {unit.SideId}, HP {unit.CurrentHp}/{type.Hp}, {placement} | MOV {type.Mov}, RNG {type.Rng}, ATK {type.Atk}, DEF {type.Def}");
    }
    Console.WriteLine();
}

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
            "UnitDied" => $"UnitDied: {e.UnitId} (figure removed)",
            _ => e.Kind
        };
        Console.WriteLine($"  {description}");
    }
}
