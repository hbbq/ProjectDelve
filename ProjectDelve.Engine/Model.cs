namespace ProjectDelve.Engine;

public sealed record Cell(int X, int Y);
public enum EdgeKind { Wall, ClosedDoor, OpenDoor, None, WallWithWindow }
public enum TerrainKind { Grass, Tree, Water, StoneFloor, StoneFloorWithTable }
public sealed record TerrainTile(Cell Position, TerrainKind Kind);
public sealed record Edge(Cell A, Cell B, EdgeKind Kind);
public enum Posture { Upright, Lying }
public sealed record Figure(string Id, Cell Position, Posture Posture = Posture.Upright);
[Flags]
public enum UnitAction { None = 0, NormalAttack = 1, OpenDoor = 2 }
[Flags]
public enum UnitBehavior { None = 0, ApproachThroughClosedDoors = 1 }
public sealed record TryOpenDoor(int SuccessCount);
public sealed record UnitType(string Id, int Mov, int Rng, int Atk, int Def, int Hp,
    UnitAction Actions = UnitAction.NormalAttack, TryOpenDoor? TryOpenDoor = null,
    UnitBehavior Behaviors = UnitBehavior.None)
{
    // Hero content uses these Action defaults independently of side or agency.
    public static UnitType Hero(string id, int mov, int rng, int atk, int def, int hp) =>
        new(id, mov, rng, atk, def, hp, UnitAction.NormalAttack | UnitAction.OpenDoor);

    public static UnitType Zombie(string id = "zombie-type") =>
        new(id, 2, 1, 3, 3, 1, TryOpenDoor: new(2),
            Behaviors: UnitBehavior.ApproachThroughClosedDoors);
}
public sealed record Unit(string Id, string TypeId, string SideId, int CurrentHp);

public sealed record Board(int Width, int Height, List<Edge> Edges)
{
    // Unlisted cells contain stone floor; absent edges have kind None.
    public List<TerrainTile> Terrain { get; init; } = [];
    public TerrainKind TerrainAt(Cell cell) =>
        Terrain.FirstOrDefault(tile => tile.Position == cell)?.Kind ?? TerrainKind.StoneFloor;
    public EdgeKind EdgeBetween(Cell a, Cell b) =>
        Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind ?? EdgeKind.None;
}

public static class BoardProperties
{
    public static bool Passable(this TerrainKind kind) => kind is TerrainKind.Grass or TerrainKind.StoneFloor;
    public static bool BlocksLos(this TerrainKind kind) => kind == TerrainKind.Tree;
    public static bool Passable(this EdgeKind kind) => kind is EdgeKind.None or EdgeKind.OpenDoor;
    public static bool BlocksLos(this EdgeKind kind) => kind is EdgeKind.Wall or EdgeKind.ClosedDoor;
}
public sealed record PhysicalState(Board Board, List<Figure> Figures);
public enum Phase { BonusAction, Move, Act }
public enum DecisionKind { SelectUnit, Move, Act }
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null,
    UnitAction? Action = null, string? TargetId = null, Edge? Door = null, TryOpenDoor? TryOpenDoor = null);
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0,
    Edge? Door = null, int? DieRoll = null, int? SuccessCount = null, bool? Succeeded = null);

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
            [.. Physical.Board.Edges]) { Terrain = [.. Physical.Board.Terrain] }, [.. Physical.Figures]),
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
    int RollD6();
}

public enum AttackFace { Hit, Miss }
public enum DefenceFace { Block, Miss }
