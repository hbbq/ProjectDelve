using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public enum ScenarioObjective { EliminateEnemies, ReachArea, DefeatUnits, NoObjective }
public enum UnitSelectorKind { Unit, SideAtStart, Group }
public sealed record UnitSelector(UnitSelectorKind Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SideId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitTypeId = null)
{
    public static UnitSelector ForUnit(string unitId) => new(UnitSelectorKind.Unit, UnitId: unitId);
    public static UnitSelector SideAtStart(string sideId) => new(UnitSelectorKind.SideAtStart, SideId: sideId);
    public static UnitSelector Group(string unitTypeId, string sideId) => new(UnitSelectorKind.Group, SideId: sideId, UnitTypeId: unitTypeId);
}
public sealed record ObjectiveArea(int X, int Y, int W, int H);
public sealed record SideObjective(string SideId, ScenarioObjective Objective = ScenarioObjective.EliminateEnemies)
{
    public int Count { get; init; }
    public ImmutableArray<UnitSelector> Selectors { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ObjectiveArea? Area { get; init; }
}
public sealed record GameCompletion(ImmutableArray<string> AchievedSideIds);

public sealed record ActivationToken(string TypeId, string SideId);
public enum OrderKind { Normal, DoNothing, HoldPosition, MoveTo, Attack, Protect, Retreat, InteractWith, FollowRoute }
public enum OrderTargetKind { Unit, Group, Side, Door }
public sealed record DoorTarget(Cell A, Cell B)
{
    public static DoorTarget Canonical(Cell a, Cell b) => Compare(a, b) <= 0 ? new(a, b) : new(b, a);
    internal bool IsCanonical => A is not null && B is not null && Compare(A, B) <= 0;
    private static int Compare(Cell a, Cell b)
    {
        var byY = a.Y.CompareTo(b.Y);
        return byY != 0 ? byY : a.X.CompareTo(b.X);
    }
}
public sealed record OrderTarget(OrderTargetKind Kind, string? UnitId = null, ActivationToken? Group = null,
    string? SideId = null, DoorTarget? Door = null)
{
    public static OrderTarget ForUnit(string unitId) => new(OrderTargetKind.Unit, UnitId: unitId);
    public static OrderTarget ForGroup(string unitTypeId, string sideId) =>
        new(OrderTargetKind.Group, Group: new(unitTypeId, sideId));
    public static OrderTarget ForSide(string sideId) => new(OrderTargetKind.Side, SideId: sideId);
    public static OrderTarget ForDoor(Cell a, Cell b) => new(OrderTargetKind.Door, Door: DoorTarget.Canonical(a, b));
}
public sealed record GroupOrder(ActivationToken Token, OrderKind Kind, Cell? Destination = null,
    OrderTarget? Target = null, FollowRoute? Route = null);
public enum ControllerKind { Human, Automated }
public sealed record ControllerAssignment(ActivationToken Token, ControllerKind Controller);
public enum DiceFamily { Attack, Defence, D6 }
public sealed record DicePool(DiceFamily Family, int Count, string OwnerUnitId, string SourceUnitId,
    string SourceActionId, string Purpose, string? TargetId = null, Edge? Door = null, int? SuccessCount = null)
{
    public Cell? SelectedCell { get; init; }
    public ImmutableArray<string> TargetIds { get; init; } = [];
}
public sealed record DiceResult(DicePool Pool, ImmutableArray<string> Faces, int Successes);
public enum AttackStage { AttackRoll, DefenceRoll }
public sealed record AttackTargetDice(string TargetId, int DefenceDice);
// Immutable, narrowly scoped progress for one committed Attack, safe to share in detached copies.
public sealed record AttackContinuation(string AttackerId, string ActionId, string? AbilityName,
    Cell? SelectedCell, ImmutableArray<AttackTargetDice> Targets, int AttackDice,
    AttackStage Stage = AttackStage.AttackRoll, int Hits = 0, int TargetIndex = 0,
    ImmutableArray<AttackTargetResult> Results = default)
{
    public DiceResult? AttackRoll { get; init; }
    public ImmutableArray<Cell> DefeatedHostileCells { get; init; } = [];
    public bool IsNormalMelee { get; init; }
    public UnitAction? SemanticAction { get; init; }
}
public sealed record DoorContinuation(string UnitId, Edge Door, int SuccessCount, string ActionId);

public sealed record Cell(int X, int Y);
public enum Footprint { OneByOne, TwoByTwo }
public enum EdgeKind { Wall, ClosedDoor, OpenDoor, None, WallWithWindow }
public enum TerrainKind { Grass, Tree, Water, StoneFloor, StoneFloorWithTable, Lava, Rail, Gravel, Asphalt, FruitTree, SolidRock }
public enum RailDirection { North, East, South, West }
public sealed record TerrainTile(Cell Position, TerrainKind Kind = TerrainKind.StoneFloor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RailDirection? Direction = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CoalGeneration { get; init; }
}
public sealed record Edge(Cell A, Cell B, EdgeKind Kind);
public enum Posture { Upright, Lying }
public sealed record Figure(string Id, Cell Position, Posture Posture = Posture.Upright);
[Flags]
public enum UnitAction { None = 0, NormalAttack = 1, Heal = 2, HolyWave = 4, Fireball = 8, Telekinesis = 16, SummonAdjacent = 32, FireBreath = 64, ClawAttack = 128, PickupResource = 256, GiveResource = 512, Operate = 1024, SummonWorker = 2048, SummonTroll = 4096, PlaceBananaPeel = 8192, ShootShell = 16384 }
[Flags]
public enum UnitFreeAction { None = 0, OpenDoor = 1 }
[Flags]
public enum UnitBehavior
{
    None = 0,
    ApproachThroughClosedDoors = 1,
    MaximizeAttackDistance = 2,
    BackAwayAfterAttack = 4,
    Flee = 8,
    UseSummon = 16,
    SwapThenAttackThenDisplace = 64,
    PreferReshapeRoom = 128,
    PreferHostileAdjacency = 256,
    ProtectWithoutAdjacency = 512,
    Worker = 1024,
    SeekDiamond = 2048,
    PreferTrollThenDiamondThenWorker = 4096,
    PreferSummonWorker = 8192,
    RouteLookahead = 16384,
    PreferBananaPeel = 32768,
    PreferLowestEffectiveDefense = 65536
}
public enum MovementPattern { StraightOrthogonal, DiagonalCorridor, LShape, StraightOrthogonalOrDiagonalCorridor, Adjacent }
public sealed record AdvanceOnKill;
public sealed record ReshapeRoom;
public sealed record Swap;
// Current external movement supports zero or one ordinary step only.
public sealed record Displace(int MaxMove);
public sealed record SummonAdjacent(string UnitTypeId, Posture InitialPosture);
public sealed record SummonWorker(int SideLimit);
public sealed record SummonTroll(int DiamondCost);
public sealed record PlaceBananaPeel(int BananaCost);
public sealed record Boost(int MushroomCost);
public sealed record ShootShell(int ShellCost, int Rng, int Atk);
public sealed record WaypointSlotMachine;
public sealed record TerrainRestrictedMovement(TerrainKind Terrain);
public sealed record Telekinesis;
// Presentation only: fixed mechanic/counter identities never depend on these names.
public sealed record AbilityPresentationNames
{
    public string? Heal { get; init; }
    public string? Cleave { get; init; }
    public string? HolyWave { get; init; }
    public string? Fireball { get; init; }
    public string? FireBreath { get; init; }
    public string? ClawAttack { get; init; }
    public string? Aura { get; init; }
    public string? Fury { get; init; }
    public string? Backstab { get; init; }
    public string? Undying { get; init; }
    public string? Explosion { get; init; }
    public string? Telekinesis { get; init; }
    public string? TryOpenDoor { get; init; }
    public string? Summon { get; init; }
    public string? MoveAfterAttack { get; init; }
}
public sealed record TryOpenDoor(int SuccessCount);
public sealed record MoveAfterAttack(int MaxSteps);
public sealed record Undying;
public sealed record Explosion
{
    public int Damage { get; init; } = 1;
}
public sealed record RunRails(int Steps);
public sealed record Phase;
public sealed record TransportResource;
public sealed record Cleave(int MaxUses = 2)
{
    public int TriggerDamage { get; init; } = 2;
    public int Damage { get; init; } = 1;
}
// The positional argument remains MaxUses; configure healing through the named Amount property.
public sealed record Heal(int MaxUses = 2)
{
    public int Amount { get; init; } = 2;
}
public sealed record HolyWave(int MaxUses = 2);
public sealed record Fireball(int MaxUses = 2);
public sealed record FireBreath;
public sealed record ClawAttack(int AtkBonus = 1);
public sealed record AdjacentFriendlyUnitsDefenceBonus(int Amount, string Name = "Aura");
public sealed record Fury
{
    public int AdjacentEnemyThreshold { get; init; } = 2;
    public int AtkBonus { get; init; } = 1;
    [JsonIgnore] public string Name => "Fury";
    [JsonIgnore] public string DisplayText => $"ATK {AtkBonus:+0;-0;0} while adjacent to {AdjacentEnemyThreshold} or more enemies";
}
public sealed record Backstab
{
    public int AtkBonus { get; init; } = 1;
    [JsonIgnore] public string Name => "Backstab";
    [JsonIgnore] public string DisplayText => $"{AtkBonus:+0;-0;0} ATK when attacking an enemy adjacent to another friendly Unit";
}
// Presentation metadata only; passive rules retain their concrete representations.
public sealed record PassiveDescription(string Name, string DisplayText);
// Current HP is never a modifier stat.
public enum Stat { Atk, Mov, Rng, Def }
public sealed record ModifierThisTurn(Stat Stat, int Amount);
// Immutable content and per-ability counters may be shared safely by state copies.
public sealed record BonusActionAbility(string Name, int? MaxUses, ImmutableArray<ModifierThisTurn> Modifiers)
{
    public PlaceBananaPeel? PlaceBananaPeel { get; init; }
    public Boost? Boost { get; init; }
    public ReshapeRoom? ReshapeRoom { get; init; }
    public Swap? Swap { get; init; }
    public Displace? Displace { get; init; }
    // Name remains the existing persisted key; presentation can vary independently.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
}
public sealed record AbilityUses
{
    public int MaxUses { get; }
    public int RemainingUses { get; }

    public AbilityUses(int maxUses, int remainingUses)
    {
        if (maxUses < 0 || remainingUses < 0 || remainingUses > maxUses)
            throw new ArgumentOutOfRangeException(nameof(remainingUses));
        MaxUses = maxUses;
        RemainingUses = remainingUses;
    }
}
public sealed record UnitType(string Id, int Mov, int Rng, int Atk, int Def, int Hp,
    UnitAction Actions = UnitAction.NormalAttack, TryOpenDoor? TryOpenDoor = null,
    UnitBehavior Behaviors = UnitBehavior.None, MoveAfterAttack? MoveAfterAttack = null,
    UnitFreeAction FreeActions = UnitFreeAction.None)
{
    public bool CanHaveItems { get; init; }
    public bool CanHaveEquipment { get; init; }
    public int LootDropSuccesses { get; init; }
    public ImmutableArray<UnitAction> AttackDefinitionOrder { get; init; } = [];
    public bool Unique { get; init; }
    public Footprint Footprint { get; init; } = Footprint.OneByOne;
    public string? DisplayName { get; init; }
    public AbilityPresentationNames AbilityNames { get; init; } = new();
    public SummonAdjacent? SummonAdjacent { get; init; }
    public SummonWorker? SummonWorker { get; init; }
    public SummonTroll? SummonTroll { get; init; }
    public ShootShell? ShootShell { get; init; }
    public WaypointSlotMachine? WaypointSlotMachine { get; init; }
    public TerrainRestrictedMovement? TerrainRestrictedMovement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TerrainRestriction? TerrainRestriction { get; init; }
    public MovementPattern? MovementPattern { get; init; }
    public AdvanceOnKill? AdvanceOnKill { get; init; }
    public AdjacentFriendlyUnitsDefenceBonus? AdjacentFriendlyUnitsDefenceBonus { get; init; }
    public Fury? Fury { get; init; }
    public Backstab? Backstab { get; init; }
    public Undying? Undying { get; init; }
    public Explosion? Explosion { get; init; }
    public RunRails? RunRails { get; init; }
    public Phase? Phase { get; init; }
    public TransportResource? TransportResource { get; init; }
    public ResourceStorage? ResourceStorage { get; init; }
    public Cleave? Cleave { get; init; }
    public Heal? Heal { get; init; }
    public ImmutableArray<AbilityGrant> AbilityGrants { get; init; } = [];
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
    public HolyWave? HolyWave { get; init; }
    public Fireball? Fireball { get; init; }
    public ClawAttack? ClawAttack { get; init; }
    [JsonIgnore]
    public IReadOnlyList<PassiveDescription> Passives
    {
        get
        {
            List<PassiveDescription> passives = [];
            if (AdjacentFriendlyUnitsDefenceBonus is { } aura)
                passives.Add(new(AbilityNames.Aura ?? aura.Name, $"Adjacent friendly Units get DEF +{aura.Amount}"));
            if (Fury is { } fury) passives.Add(new(AbilityNames.Fury ?? fury.Name, fury.DisplayText));
            if (Backstab is { } backstab) passives.Add(new(AbilityNames.Backstab ?? backstab.Name, backstab.DisplayText));
            return passives;
        }
    }
    public ImmutableArray<BonusActionAbility> BonusActions { get; init; } = [];
    public static UnitType Hero(string id, int mov, int rng, int atk, int def, int hp) =>
        new(id, mov, rng, atk, def, hp);

    public Unit CreateUnit(string id, string sideId, IReadOnlyList<AbilityDefinition>? abilityDefinitions = null) =>
        AbilityCompatibility.InitializeUses(this, new(id, Id, sideId, Hp), abilityDefinitions ?? AbilityCatalog.All);

    public static UnitType Define(string id, string name, UnitAuthoring.BaseStats stats, params UnitAuthoring.Entry[] entries) =>
        UnitAuthoring.Define(id, name, stats, entries);

    public static UnitType Locomotive(string id = UnitTypeIds.Locomotive) => UnitRoster.Locomotive(id);
    public static UnitType RoyalPeasant(string id = UnitTypeIds.RoyalPeasant) => UnitRoster.RoyalPeasant(id);
    public static UnitType GoblinPeasant(string id = UnitTypeIds.GoblinPeasant) => UnitRoster.GoblinPeasant(id);
    public static UnitType CastleGuard(string id = UnitTypeIds.CastleGuard) => UnitRoster.CastleGuard(id);
    public static UnitType TowerTroll(string id = UnitTypeIds.TowerTroll) => UnitRoster.TowerTroll(id);
    public static UnitType Barbarian(string id = UnitTypeIds.Barbarian) => UnitRoster.Barbarian(id);
    public static UnitType Rogue(string id = UnitTypeIds.Rogue) => UnitRoster.Rogue(id);
    public static UnitType Cleric(string id = UnitTypeIds.Cleric) => UnitRoster.Cleric(id);
    public static UnitType Wizard(string id = UnitTypeIds.Wizard) => UnitRoster.Wizard(id);
    public static UnitType Grunt(string id = UnitTypeIds.Grunt) => UnitRoster.Grunt(id);
    public static UnitType Zombie(string id = UnitTypeIds.Zombie) => UnitRoster.Zombie(id);
    public static UnitType Ghost(string id = UnitTypeIds.Ghost) => UnitRoster.Ghost(id);
    public static UnitType SkeletonArcher(string id = UnitTypeIds.SkeletonArcher) => UnitRoster.SkeletonArcher(id);
    public static UnitType BombImp(string id = UnitTypeIds.BombImp) => UnitRoster.BombImp(id);
    public static UnitType DisplacerDemon(string id = UnitTypeIds.DisplacerDemon) => UnitRoster.DisplacerDemon(id);
    public static UnitType Troll(string id = UnitTypeIds.Troll) => UnitRoster.Troll(id);
    public static UnitType Goblin(string id = UnitTypeIds.Goblin) => UnitRoster.Goblin(id);
    public static UnitType Worker(string id = UnitTypeIds.Worker) => UnitRoster.Worker(id);
    public static UnitType Geomancer(string id = UnitTypeIds.Geomancer) => UnitRoster.Geomancer(id);
    public static UnitType Shaman(string id = UnitTypeIds.Shaman) => UnitRoster.Shaman(id);
    public static UnitType LabyrinthShaper(string id = UnitTypeIds.LabyrinthShaper) => UnitRoster.LabyrinthShaper(id);
    public static UnitType RedDragon(string id = UnitTypeIds.RedDragon) => UnitRoster.RedDragon(id);
}
public sealed record Unit(string Id, string TypeId, string SideId, int CurrentHp)
{
    private ImmutableArray<OwnedObject> inventory = ImmutableArray<OwnedObject>.Empty;
    public ImmutableArray<OwnedObject> Inventory
    {
        get => inventory;
        init => inventory = value.IsDefaultOrEmpty ? ImmutableArray<OwnedObject>.Empty : value;
    }
    public ImmutableDictionary<string, int> AbilityUseCounts { get; init; } = ImmutableDictionary<string, int>.Empty;
    public AbilityUses? CleaveUses { get; init; }
    public AbilityUses? HealUses { get; init; }
    public AbilityUses? HolyWaveUses { get; init; }
    public AbilityUses? FireballUses { get; init; }
    public ImmutableDictionary<string, AbilityUses> BonusActionUses { get; init; } = ImmutableDictionary<string, AbilityUses>.Empty;
}

public sealed record Board(int Width, int Height, List<Edge> Edges) : IJsonOnDeserializing, IJsonOnDeserialized
{
    // Unlisted cells contain stone floor; absent edges have kind None.
    public List<Machine> Machines { get; init; } = [];
    public List<TerrainTile> Terrain { get; init; } = [];
    private ImmutableArray<TerrainDefinition> terrainTypes = TerrainCatalog.All;
    private ImmutableDictionary<string, TerrainDefinition> terrainIndex = TerrainCatalog.All.ToImmutableDictionary(d => d.Id, StringComparer.Ordinal);
    private int terrainContentVersion = 2;
    // Runtime snapshot format only; scenario serialization retains its legacy shape.
    public int TerrainContentVersion { get => terrainContentVersion; init => terrainContentVersion = value; }
    void IJsonOnDeserializing.OnDeserializing() => terrainContentVersion = 1;
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (terrainContentVersion is not (1 or 2)) throw new ArgumentException("Unsupported Terrain content snapshot version.");
        if (terrainContentVersion == 1)
        {
            terrainTypes = TerrainCompatibility.UpgradeRuntimeCatalog(terrainTypes);
            terrainIndex = terrainTypes.ToImmutableDictionary(d => d.Id, StringComparer.Ordinal);
            terrainContentVersion = 2;
        }
    }
    public ImmutableArray<TerrainDefinition> TerrainTypes
    {
        get => terrainTypes;
        init
        {
            terrainTypes = value;
            terrainIndex = value.IsDefault ? ImmutableDictionary<string, TerrainDefinition>.Empty :
                value.ToImmutableDictionary(d => d.Id, StringComparer.Ordinal);
        }
    }
    public TerrainDefinition TerrainDefinition(TerrainTile? tile)
    {
        var id = TerrainCompatibility.ResolveTypeId(tile?.Kind ?? TerrainKind.StoneFloor, tile?.TypeId, tile?.CoalGeneration);
        return terrainIndex.TryGetValue(id, out var definition) ? definition :
            throw new ArgumentException($"Unknown Terrain Type ID '{id}'.");
    }
    public TerrainDefinition TerrainDefinitionAt(Cell cell)
    {
        var tile = Terrain.FirstOrDefault(t => t.Position == cell);
        return TerrainDefinition(tile);
    }
    // Compatibility identity is not a property lookup. Local Types have no enum identity.
    public TerrainKind TerrainAt(Cell cell)
    {
        var tile = Terrain.FirstOrDefault(t => t.Position == cell);
        return tile?.TypeId is null ? tile?.Kind ?? TerrainKind.StoneFloor : TerrainDefinition(tile).LegacyKind ?? (TerrainKind)(-1);
    }
    public EdgeKind EdgeBetween(Cell a, Cell b) =>
        Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind ?? EdgeKind.None;
}

public static class BoardProperties
{
    public static bool Passable(this TerrainKind kind) => TerrainCatalog.Legacy(kind).Passable;
    public static bool BlocksLos(this TerrainKind kind) => TerrainCatalog.Legacy(kind).BlocksLos;
    public static bool Passable(this EdgeKind kind) => kind is EdgeKind.None or EdgeKind.OpenDoor;
    public static bool BlocksLos(this EdgeKind kind) => kind is EdgeKind.Wall or EdgeKind.ClosedDoor;
}
public sealed record PhysicalState(Board Board, List<Figure> Figures);
// Normal Unit choices use Activation. Move is also used for the narrow post-attack
// continuation; Move/Act requests support the providers' existing ranking routines.
public enum DecisionKind { SelectUnit, Activation, Move, Act, Cleave, RollDice }
public enum ActivationChoiceKind { Action, Move, Stay, EndTurn, SelectUnit, FreeAction, BonusAction, Cleave, RollDice }
// Every candidate is legal. Relevance guides decision stops and presentation only;
// choices default to relevant unless their rule component supplies a narrower policy.
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null,
    UnitAction? Action = null, string? TargetId = null, Edge? Door = null, TryOpenDoor? TryOpenDoor = null,
    ActivationChoiceKind Kind = ActivationChoiceKind.Action, UnitFreeAction? FreeAction = null,
    bool Relevant = true, BonusActionAbility? BonusAction = null)
{
    public ResolvedAbility? GrantedAbility { get; init; }
    public string? ItemTypeId { get; init; }
    public string? ObjectId { get; init; }
    public LootCardKind? ObjectKind { get; init; }
    public string? EquipmentTypeId { get; init; }
    public string? EquipmentAbilityId { get; init; }
    public string? AbilityName { get; init; }
    public EquipmentActionMechanic? EquipmentMechanic { get; init; }
    public bool EquipmentGranted { get; init; }
    public string? MachineId { get; init; }
    public ImmutableArray<string> TargetIds { get; init; } = [];
}
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone,
    bool IsMoveAfterAttack = false)
{
    public ActivationToken? Token { get; init; }
    public DicePool? Roll { get; init; }
}
public sealed record AttackTargetResult(string TargetId, int DefenceDice, int Blocks, int Damage);
// One roll and separate per-Unit Damage; there is deliberately no total Damage.
public sealed record AttackResult(int AttackDice, int Hits, ImmutableArray<AttackTargetResult> Targets);
// Event-local source information remains available after the Figure has left play.
// Retained only for events and the currently resolving automatic sequence, never an in-play Unit.
public sealed record DefeatContext(Unit Unit, UnitType Type, Figure Figure, ImmutableArray<Cell> OccupiedCells);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0,
    Edge? Door = null, int? DieRoll = null, int? SuccessCount = null, bool? Succeeded = null,
    bool IsMoveAfterAttack = false, string? AbilityName = null, int Healing = 0,
    AttackResult? Attack = null, Posture? Posture = null)
{
    public OwnedObject? ObjectCard { get; init; }
    public LootMarker? LootMarker { get; init; }
    // Observation-only contiguous paths, with at most one boundary step at each end.
    // Hidden interiors are never transported or joined into an invented route.
    public IReadOnlyList<List<Cell>>? ObservedMovement { get; init; }
    public string? RouteId { get; init; }
    public string? WaypointId { get; init; }
    public string? MachineId { get; init; }
    public ImmutableArray<MachineResource> ConsumedResources { get; init; } = [];
    public CellMarkerType? CellMarkerType { get; init; }
    public ResourceType? ResourceType { get; init; }
    public string? TerrainTypeId { get; init; }
    public string? TerrainName { get; init; }
    public int? StoredQuantity { get; init; }
    public GameCompletion? Completion { get; init; }
    public ActivationToken? Token { get; init; }
    public string? SideId { get; init; }
    public string? SourceUnitId { get; init; }
    public string? ActionId { get; init; }
    public ActivationChoiceKind? Category { get; init; }
    public Cell? Cell { get; init; }
    public AttackContinuation? AttackContext { get; init; }
    public DefeatContext? DefeatContext { get; init; }
    public DiceResult? Dice { get; init; }
    public int? Round { get; init; }
    public WorldCard? WorldCard { get; init; }
    public WorldCard? CycledWorldCard { get; init; }
    public int? ReplenishedUses { get; init; }
}

// Old group-phase saves cannot be resumed as per-unit activations.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class GameState : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized()
    {
        AbilityCatalogCompatibility.NormalizeMissingEquipmentCatalog(this, equipmentCatalogProvided);
        AbilityCatalogCompatibility.NormalizeMissingItemCatalog(this, itemCatalogProvided);
        LootCompatibility.MigrateLegacyCatalog(this);
        EquipmentCompatibility.MigrateLegacyDuplicateOwnership(this);
    }
    public List<WorldCardDefinition> WorldCardDefinitions { get; set; } = WorldCardCatalog.Resolve(null);
    public List<AbilityDefinition> AbilityDefinitions { get; set; } = AbilityCatalog.Resolve(null);
    private List<ItemDefinition> itemTypes = ItemCatalog.Resolve(null);
    private bool itemCatalogProvided;
    public List<ItemDefinition> ItemTypes
    {
        get => itemTypes;
        set { itemTypes = value; itemCatalogProvided = true; }
    }
    public List<LootCardDefinition> LootCardDefinitions { get; set; } = LootCardCatalog.Resolve(null);
    private List<EquipmentDefinition> equipmentTypes = EquipmentCatalog.Resolve(null);
    private bool equipmentCatalogProvided;
    public List<EquipmentDefinition> EquipmentTypes
    {
        get => equipmentTypes;
        set { equipmentTypes = value; equipmentCatalogProvided = true; }
    }
    public LootDeck? LootDeck { get; set; }
    public List<LootMarker> LootMarkers { get; set; } = [];
    public List<GroundObject> GroundObjects { get; set; } = [];
    // Only detached Side observations carry these values. Removing hidden providers
    // from presentation must not recalculate the visible Units' authoritative stats.
    internal Dictionary<string, (int Mov, int Rng, int Atk, int Def)>? ObservationValues { get; set; }
    public required PhysicalState Physical { get; set; }
    public required List<UnitType> Types { get; set; }
    public required List<Unit> Units { get; set; }

    internal bool CanPlaceUnitType(UnitType type, string sideId) =>
        !type.Unique || !Units.Any(u => u.TypeId == type.Id && u.SideId == sideId && u.CurrentHp > 0);

    // Creating detached Unit data does not enter play; placement is the runtime entry boundary.
    public void PlaceUnit(string typeId, string id, string sideId, Cell cell, Posture posture = Posture.Upright)
    {
        var type = UnitContent.Find(typeId, Types)
            ?? throw new ArgumentException("Unit Type is not defined.", nameof(typeId));
        if (type.Hp > 1 && !type.Unique)
            throw new ArgumentException("Maximum HP greater than 1 requires a Unique Unit Type.", nameof(typeId));
        if (!CanPlaceUnitType(type, sideId))
            throw new ArgumentException("A Unit of this Unique Unit Type is already in play on this Side.", nameof(typeId));
        if (Physical.Board.Machines.Any(m => m.Id == id) || Units.Any(u => u.Id == id) || string.IsNullOrWhiteSpace(sideId) ||
            !Enum.IsDefined(posture) || !Enum.IsDefined(type.Footprint) || !SpatialRules.CanPlaceUnit(this, type.Footprint, cell) ||
            FootprintGeometry.OccupiedCells(type.Footprint, cell)
                .Any(c => !TerrainCatalog.Accepts(type, Physical.Board.TerrainDefinitionAt(c))))
            throw new ArgumentException("Invalid Unit placement.");
        if (!Types.Any(t => t.Id == type.Id)) Types.Add(type);
        Units.Add(type.CreateUnit(id, sideId, AbilityDefinitions));
        Physical.Figures.Add(new(id, cell, posture));
        FogOfWar.Refresh(this);
    }
    public List<string> NeutralSides { get; set; } = [];
    public List<string> FogOfWarSides { get; set; } = [];
    public Dictionary<string, HashSet<Cell>> ExploredCells { get; set; } = new(StringComparer.Ordinal);
    public bool AreHostile(string firstSide, string secondSide) =>
        firstSide != secondSide && !NeutralSides.Contains(firstSide) && !NeutralSides.Contains(secondSide);
    public List<SideObjective> Objectives { get; set; } = [];
    public Dictionary<string, ImmutableArray<string>> ObjectiveStartUnitIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> DefeatedUnitIds { get; set; } = new(StringComparer.Ordinal);
    public GameCompletion? Completion { get; set; }
    public bool IsCompleted => Completion is not null;
    public WorldEffectsSettings? WorldEffects { get; set; }
    public WorldDeck? WorldDeck { get; set; }
    public int Round { get; set; }
    public List<ActivationToken> Bag { get; set; } = [];
    public ActivationToken? ActiveToken { get; set; }
    public List<ControllerAssignment> Controllers { get; set; } = [];
    // Sparse authoritative assignments: absence means Normal.
    public List<GroupOrder> Orders { get; set; } = [];
    public List<Waypoint> Waypoints { get; set; } = [];
    public List<Route> Routes { get; set; } = [];
    public GroupOrder OrderFor(ActivationToken token) =>
        Orders.SingleOrDefault(order => order.Token == token) ?? new(token, OrderKind.Normal);
    public void AssignOrder(ActivationToken token, OrderKind kind, Cell? destination = null, OrderTarget? target = null, FollowRoute? route = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!Controllers.Any(assignment => assignment.Token == token))
            throw new ArgumentException("The Order group is not part of the game.", nameof(token));
        var order = new GroupOrder(token, kind, destination, target, route);
        if (!OrderValidation.IsValidAssignment(order, this))
            throw new ArgumentException("Invalid Order kind, destination, target, or target relationship.", nameof(kind));
        Orders.RemoveAll(order => order.Token == token);
        if (kind != OrderKind.Normal) Orders.Add(order);
    }
    public ControllerKind ControllerFor(ActivationToken token) => Controllers.Single(c => c.Token == token).Controller;
    public ControllerKind ControllerFor(DecisionRequest request) => ControllerFor(request.Token
        ?? throw new InvalidOperationException("Decision has no controlling group."));
    public AttackContinuation? AttackInProgress { get; set; }
    public DoorContinuation? DoorInProgress { get; set; }
    // FIFO defeat consequences, retained across dice boundaries only until resolution completes.
    public List<DefeatContext> PendingExplosions { get; set; } = [];
    public int MovesCompletedThisActivation { get; set; }
    public int MoveOpportunityModifierThisActivation { get; set; }
    public int EffectiveMovesPerActivation => Math.Max(0, 1 + MoveOpportunityModifierThisActivation);
    public bool MoveDone
    {
        get => MovesCompletedThisActivation >= EffectiveMovesPerActivation;
        set { if (value != MoveDone) MovesCompletedThisActivation = value ? EffectiveMovesPerActivation : 0; }
    }
    public bool WaypointAdvancedThisActivation { get; set; }
    // Existing flag records whether an Action has been performed; counts govern opportunities.
    public bool ActionDone
    {
        get => ActionsUsedThisActivation > 0;
        set => ActionsUsedThisActivation = value ? Math.Max(1, ActionsUsedThisActivation) : 0;
    }
    public int ActionsUsedThisActivation { get; set; }
    public int ExtraActionsThisActivation { get; set; }
    public Dictionary<string, int> BonusActionUseCountsThisActivation { get; set; } = [];
    public int EffectiveActionsPerActivation => Math.Max(0, 1 + ExtraActionsThisActivation +
        this.WorldModifier(WorldModifierTarget.Actions));
    public int EffectiveBonusActionUsesPerActivation => Math.Max(0, 1 +
        this.WorldModifier(WorldModifierTarget.BonusActionUses));
    public int ActionsRemaining => Math.Max(0, EffectiveActionsPerActivation - ActionsUsedThisActivation);
    public int BonusActionUsesThisActivation(string name) => Math.Max(
        BonusActionUseCountsThisActivation.GetValueOrDefault(name), BonusActionsUsedThisActivation.Contains(name) ? 1 : 0);
    // Ability names are the existing content identities, shared with persistent use counters.
    public HashSet<string> BonusActionsUsedThisActivation { get; set; } = [];
    public List<ModifierThisTurn> ModifiersThisTurn { get; set; } = [];
    // Derived authoritative values are also serialized for rule-independent clients.
    public Dictionary<string, int> EffectiveAtk => Units.ToDictionary(u => u.Id, u => EffectiveAtkOf(u.Id));
    public Dictionary<string, int> EffectiveMov => Units.ToDictionary(u => u.Id, u => EffectiveMovOf(u.Id));
    public Dictionary<string, int> EffectiveRng => Units.ToDictionary(u => u.Id, u => EffectiveRngOf(u.Id));
    public Dictionary<string, int> EffectiveDef => Units.ToDictionary(u => u.Id, u => EffectiveDefOf(u.Id));

    public bool IsUpright(string unitId) =>
        Physical.Figures.Any(f => f.Id == unitId && f.Posture == Posture.Upright);

    public bool HasEquipment(string unitId, LootCardKind kind) => IsUpright(unitId) &&
        this.ActiveEquipment(unitId).Any(e => e.Id == EquipmentCatalog.LegacyTypeId(kind));
    public bool HasPhase(string unitId) => Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Phase is not null ||
        this.EquipmentGrants(unitId, EquipmentCapability.Phase);
    public bool HasOpenDoor(string unitId) => Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).FreeActions.HasFlag(UnitFreeAction.OpenDoor) ||
        this.EquipmentGrants(unitId, EquipmentCapability.OpenDoor);

    public int EffectiveAtkOf(string unitId)
    {
        if (ObservationValues?.TryGetValue(unitId, out var observed) == true) return observed.Atk;
        var unit = Units.Single(u => u.Id == unitId);
        var type = Types.Single(t => t.Id == unit.TypeId);
        var attack = type.Atk + this.EquipmentModifier(unitId, Stat.Atk) + this.AbilityModifier(unitId, Stat.Atk) + this.WorldModifier(WorldModifierTarget.Atk) + (CurrentUnitId == unitId
            ? ModifiersThisTurn.Where(m => m.Stat == Stat.Atk).Sum(m => m.Amount) : 0);
        var figure = Physical.Figures.SingleOrDefault(f => f.Id == unitId);
        if (type.Fury is not { } fury || unit.CurrentHp <= 0 || figure is null || !IsUpright(unitId)) return Math.Max(0, attack);

        // Derive Fury solely from the evaluated world, including hypothetical copies.
        var adjacentEnemies = Units.Where(u => u.CurrentHp > 0 && AreHostile(unit.SideId, u.SideId))
            .Count(enemy => Physical.Figures.SingleOrDefault(f => f.Id == enemy.Id) is { } enemyFigure &&
                SpatialRules.AreAdjacent(this, unitId, enemy.Id));
        return Math.Max(0, attack + (adjacentEnemies >= fury.AdjacentEnemyThreshold ? fury.AtkBonus : 0));
    }
    // Target-specific Attack Dice, shared by legality, effectiveness and resolution.
    // General EffectiveAtk remains independent of the selected target.
    public int EffectiveAtkAgainst(string attackerId, string targetId)
    {
        var attack = EffectiveAtkOf(attackerId);
        var attacker = Units.Single(u => u.Id == attackerId);
        var target = Units.Single(u => u.Id == targetId);
        if (Types.Single(t => t.Id == attacker.TypeId).Backstab is not { } backstab ||
            attacker.CurrentHp <= 0 || !IsUpright(attackerId) || target.CurrentHp <= 0 || !AreHostile(attacker.SideId, target.SideId))
            return Math.Max(0, attack);

        var targetFigure = Physical.Figures.SingleOrDefault(f => f.Id == targetId);
        if (targetFigure is null) return Math.Max(0, attack);
        var supported = Units.Any(u => u.Id != attackerId && u.CurrentHp > 0 && u.SideId == attacker.SideId &&
            Physical.Figures.SingleOrDefault(f => f.Id == u.Id) is { } friendlyFigure &&
            SpatialRules.AreAdjacent(this, targetId, u.Id));
        return Math.Max(0, attack + (supported ? backstab.AtkBonus : 0));
    }
    public int EffectiveMovOf(string unitId) =>
        ObservationValues?.TryGetValue(unitId, out var observed) == true ? observed.Mov :
        Math.Max(0, Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Mov +
        this.EquipmentModifier(unitId, Stat.Mov) + this.AbilityModifier(unitId, Stat.Mov) +
        this.WorldModifier(WorldModifierTarget.Mov) +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Mov).Sum(m => m.Amount) : 0));
    public int EffectiveRngOf(string unitId) =>
        ObservationValues?.TryGetValue(unitId, out var observed) == true ? observed.Rng :
        Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Rng + this.EquipmentModifier(unitId, Stat.Rng) + this.AbilityModifier(unitId, Stat.Rng) +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Rng).Sum(m => m.Amount) : 0);
    public int EffectiveDefOf(string unitId)
    {
        if (ObservationValues?.TryGetValue(unitId, out var observed) == true) return observed.Def;
        var unit = Units.Single(u => u.Id == unitId);
        var type = Types.Single(t => t.Id == unit.TypeId);
        var defence = type.Def + this.EquipmentModifier(unitId, Stat.Def) + this.AbilityModifier(unitId, Stat.Def) + this.WorldModifier(WorldModifierTarget.Def) + (CurrentUnitId == unitId
            ? ModifiersThisTurn.Where(m => m.Stat == Stat.Def).Sum(m => m.Amount) : 0);
        var figure = Physical.Figures.SingleOrDefault(f => f.Id == unitId);
        if (unit.CurrentHp <= 0 || figure is null) return Math.Max(0, defence);

        return Math.Max(0, defence);
    }
    public List<string> CompletedUnitIds { get; set; } = [];
    public string? CurrentUnitId { get; set; }
    // Mandatory post-attack movement resolves before the activation may end.
    public int? MoveAfterAttackAllowance { get; set; }
    // Only the immediate post-Attack continuation retains these former Cells.
    public ImmutableArray<Cell>? AdvanceOnKillCells { get; set; }
    // Optional immediate resolution created by a qualifying Attack, never activation history.
    public bool CleavePending { get; set; }
    public DecisionRequest? Pending { get; set; }
    public bool RoundComplete { get; set; }

    public List<CellMarker> CellMarkers { get; set; } = [];
    public void PlaceCellMarker(CellMarkerType type, Cell position)
    {
        var marker = new CellMarker(type, position);
        CellMarkerRules.Validate(marker, Physical.Board);
        if (CellMarkers.Any(m => m.Position == position) || FootprintGeometry.UnitAtCell(this, position) is not null)
            throw new ArgumentException("A Cell Marker requires a Cell without a Unit or another Cell Marker.");
        CellMarkers.Add(marker);
    }
    public List<Resource> Resources { get; set; } = [];
    public List<StoredResource> StoredResources { get; set; } = [];

    public int StoredQuantity(string sideId, string typeId, string resourceTypeId) =>
        StoredResources.SingleOrDefault(r => r.SideId == sideId && r.UnitTypeId == typeId && r.Type.Id == resourceTypeId)?.Quantity ?? 0;

    public void PlaceResource(ResourceType type, Cell position)
    {
        ResourceRules.Validate(new Resource(type, position), Physical.Board);
        if (Resources.Any(resource => resource.Position == position))
            throw new ArgumentException("A Cell may contain at most one Resource.", nameof(position));
        Resources.Add(new(type, position));
    }

    internal GameState Copy() => new()
    {
        WorldCardDefinitions = [.. WorldCardDefinitions],
        AbilityDefinitions = [.. AbilityDefinitions], ItemTypes = [.. ItemTypes], LootCardDefinitions = [.. LootCardDefinitions],
        EquipmentTypes = [.. EquipmentTypes],
        LootDeck = LootDeck?.Copy(), LootMarkers = [.. LootMarkers], GroundObjects = [.. GroundObjects],
        Physical = new PhysicalState(new Board(Physical.Board.Width, Physical.Board.Height,
            [.. Physical.Board.Edges]) { Terrain = [.. Physical.Board.Terrain], TerrainTypes = Physical.Board.TerrainTypes, Machines = [.. Physical.Board.Machines] }, [.. Physical.Figures]),
        Types = [.. Types], Units = [.. Units], CellMarkers = [.. CellMarkers], Resources = [.. Resources], StoredResources = [.. StoredResources], Round = Round, Bag = [.. Bag],
        NeutralSides = [.. NeutralSides], Objectives = [.. Objectives], Completion = Completion,
        FogOfWarSides = [.. FogOfWarSides],
        ExploredCells = ExploredCells.ToDictionary(p => p.Key, p => new HashSet<Cell>(p.Value), StringComparer.Ordinal),
        ObjectiveStartUnitIds = new(ObjectiveStartUnitIds, StringComparer.Ordinal),
        DefeatedUnitIds = new(DefeatedUnitIds, StringComparer.Ordinal),
        WorldEffects = WorldEffects, WorldDeck = WorldDeck?.Copy(),
        ActionsUsedThisActivation = ActionsUsedThisActivation,
        ExtraActionsThisActivation = ExtraActionsThisActivation,
        BonusActionUseCountsThisActivation = new(BonusActionUseCountsThisActivation),
        ActiveToken = ActiveToken, Controllers = [.. Controllers], Orders = [.. Orders], Waypoints = [.. Waypoints], Routes = [.. Routes],
        MovesCompletedThisActivation = MovesCompletedThisActivation, MoveOpportunityModifierThisActivation = MoveOpportunityModifierThisActivation,
        WaypointAdvancedThisActivation = WaypointAdvancedThisActivation,
        AttackInProgress = AttackInProgress, DoorInProgress = DoorInProgress, ActionDone = ActionDone,
        BonusActionsUsedThisActivation = [.. BonusActionsUsedThisActivation], CompletedUnitIds = [.. CompletedUnitIds],
        ModifiersThisTurn = [.. ModifiersThisTurn],
        CurrentUnitId = CurrentUnitId, MoveAfterAttackAllowance = MoveAfterAttackAllowance,
        AdvanceOnKillCells = AdvanceOnKillCells,
        CleavePending = CleavePending, PendingExplosions = [.. PendingExplosions],
        Pending = Pending, RoundComplete = RoundComplete
    };
}

// EventIndex associates a snapshot with Events without duplicating semantic events.
// StateAfter is a detached engine state, not a patch or a resumable decision point.
public sealed record ResolutionStep(int EventIndex, GameState StateAfter);
public sealed record EngineResult(GameState State, List<RulesEvent> Events, DecisionRequest? NextInput)
{
    public List<ResolutionStep> ResolutionSteps { get; init; } = [];
}

public interface IDecisionProvider
{
    string? Choose(DecisionRequest request, IGameplayQueries queries);
}

public interface IRandomProvider
{
    // Uniform finite-deck selection using only injected physical d6 randomness.
    int LootDrawIndex(int exclusiveMax)
    {
        if (exclusiveMax < 1) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        if (exclusiveMax == 1) return 0;
        long range = 1;
        var dice = 0;
        while (range < exclusiveMax) { range *= 6; dice++; }
        while (true)
        {
            long value = 0;
            for (var i = 0; i < dice; i++) value = value * 6 + RollD6() - 1;
            if (value < range - range % exclusiveMax) return (int)(value % exclusiveMax);
        }
    }
    ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag);
    // Fisher-Yates shuffle selection, replaceable independently of combat dice and token draws.
    int WorldShuffleIndex(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
    AttackFace RollAttackDie();
    DefenceFace RollDefenceDie();
    int RollD6();
    int RollD8() => throw new NotSupportedException("This Random Provider does not support d8 rolls.");
}

public enum AttackFace { Hit, Miss }
public enum DefenceFace { Block, Miss }
