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
public enum UnitBehavior { None = 0, ApproachThroughClosedDoors = 1, MaximizeAttackDistance = 2, BackAwayAfterAttack = 4 }
public sealed record TryOpenDoor(int SuccessCount);
public sealed record MoveAfterAttack(int MaxSteps);
public sealed record UnitType(string Id, int Mov, int Rng, int Atk, int Def, int Hp,
    UnitAction Actions = UnitAction.NormalAttack, TryOpenDoor? TryOpenDoor = null,
    UnitBehavior Behaviors = UnitBehavior.None, MoveAfterAttack? MoveAfterAttack = null)
{
    // Hero content uses these Action defaults independently of side or agency.
    public static UnitType Hero(string id, int mov, int rng, int atk, int def, int hp) =>
        new(id, mov, rng, atk, def, hp, UnitAction.NormalAttack | UnitAction.OpenDoor);

    public static UnitType Barbarian(string id = "barbarian-type") => new(id, 3, 1, 4, 3, 5);

    public static UnitType Rogue(string id = "rogue-type") => new(id, 4, 1, 3, 2, 4);

    public static UnitType Grunt(string id = "grunt-type") => new(id, 3, 1, 3, 3, 1);

    public static UnitType Zombie(string id = "zombie-type") =>
        new(id, 2, 1, 3, 3, 1, TryOpenDoor: new(2),
            Behaviors: UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType SkeletonArcher(string id = "skeleton-archer-type") =>
        new(id, 3, 4, 3, 3, 1, Behaviors: UnitBehavior.MaximizeAttackDistance);

    public static UnitType Goblin(string id = "goblin-type") =>
        new(id, 4, 1, 2, 2, 1, Behaviors: UnitBehavior.BackAwayAfterAttack, MoveAfterAttack: new(1));
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
// Normal Unit choices use Activation. Move is also used for the narrow post-attack
// continuation; Move/Act requests support the providers' existing ranking routines.
public enum DecisionKind { SelectUnit, Activation, Move, Act }
public enum ActivationChoiceKind { Action, Move, Stay, EndTurn, SelectUnit }
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null,
    UnitAction? Action = null, string? TargetId = null, Edge? Door = null, TryOpenDoor? TryOpenDoor = null,
    ActivationChoiceKind Kind = ActivationChoiceKind.Action);
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone,
    bool IsMoveAfterAttack = false);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0,
    Edge? Door = null, int? DieRoll = null, int? SuccessCount = null, bool? Succeeded = null,
    bool IsMoveAfterAttack = false);

// Old group-phase saves cannot be resumed as per-unit activations.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class GameState
{
    public required PhysicalState Physical { get; set; }
    public required List<UnitType> Types { get; set; }
    public required List<Unit> Units { get; set; }
    public int Round { get; set; }
    public List<string> Bag { get; set; } = [];
    public string? ActiveTypeId { get; set; }
    public bool MoveDone { get; set; }
    public bool ActionDone { get; set; }
    public bool BonusActionUsed { get; set; }
    public List<string> CompletedUnitIds { get; set; } = [];
    public string? CurrentUnitId { get; set; }
    // Mandatory post-attack movement resolves before the activation may end.
    public int? MoveAfterAttackAllowance { get; set; }
    public DecisionRequest? Pending { get; set; }
    public bool RoundComplete { get; set; }

    internal GameState Copy() => new()
    {
        Physical = new PhysicalState(new Board(Physical.Board.Width, Physical.Board.Height,
            [.. Physical.Board.Edges]) { Terrain = [.. Physical.Board.Terrain] }, [.. Physical.Figures]),
        Types = [.. Types], Units = [.. Units], Round = Round, Bag = [.. Bag],
        ActiveTypeId = ActiveTypeId, MoveDone = MoveDone, ActionDone = ActionDone,
        BonusActionUsed = BonusActionUsed, CompletedUnitIds = [.. CompletedUnitIds],
        CurrentUnitId = CurrentUnitId, MoveAfterAttackAllowance = MoveAfterAttackAllowance,
        Pending = Pending, RoundComplete = RoundComplete
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
