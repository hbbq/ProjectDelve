namespace ProjectDelve.Engine;

public sealed record Cell(int X, int Y);
public enum EdgeKind { Wall, ClosedDoor, OpenDoor }
public sealed record Edge(Cell A, Cell B, EdgeKind Kind);
public enum Posture { Upright, Lying }
public sealed record Figure(string Id, Cell Position, Posture Posture = Posture.Upright);
public sealed record UnitType(string Id, int Mov, int Rng, int Atk, int Def, int Hp);
public sealed record Unit(string Id, string TypeId, string SideId, int CurrentHp);

// Every cell on this first-slice board is Floor. An absent edge feature is open.
public sealed record Board(int Width, int Height, List<Edge> Edges);
public sealed record PhysicalState(Board Board, List<Figure> Figures);
public enum Phase { BonusAction, Move, Act }
public enum DecisionKind { SelectUnit, Move, Attack }
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null);
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0);

public sealed class GameState
{
    public required PhysicalState Physical { get; set; }
    public required List<UnitType> Types { get; set; }
    public required List<Unit> Units { get; set; }
    public int Round { get; set; }
    public List<string> Bag { get; set; } = [];
    public string? ActiveTypeId { get; set; }
    public Phase Phase { get; set; }
    public List<string> CompletedUnitIds { get; set; } = [];
    public string? CurrentUnitId { get; set; }
    public DecisionRequest? Pending { get; set; }
    public bool RoundComplete { get; set; }

    internal GameState Copy() => new()
    {
        Physical = new PhysicalState(new Board(Physical.Board.Width, Physical.Board.Height,
            [.. Physical.Board.Edges]), [.. Physical.Figures]),
        Types = [.. Types], Units = [.. Units], Round = Round, Bag = [.. Bag],
        ActiveTypeId = ActiveTypeId, Phase = Phase, CompletedUnitIds = [.. CompletedUnitIds],
        CurrentUnitId = CurrentUnitId, Pending = Pending, RoundComplete = RoundComplete
    };
}

public sealed record EngineResult(GameState State, List<RulesEvent> Events, DecisionRequest? NextInput);

public interface IDecisionProvider
{
    string? Choose(DecisionRequest request, IGameplayQueries queries);
}

public interface IRandomProvider
{
    string DrawToken(IReadOnlyList<string> bag);
    AttackFace RollAttackDie();
    DefenceFace RollDefenceDie();
}

public enum AttackFace { Hit, Miss }
public enum DefenceFace { Block, Miss }
