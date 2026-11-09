using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class WorldQuickPlayTests
{
    private static readonly ContentBundle Canonical = ContentBundle.Canonical();
    private static ScenarioDefinition Empty(int width = 8, int height = 6) => new()
    {
        Board = new() { Width = width, Height = height }, UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    };
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ContentPackageJson.CreateOptions());

    [Theory]
    [InlineData(1UL)]
    public void GeneratedForestQuickPlayExploresFullFriendlySightDiamondsThroughTrees(ulong seed)
    {
        var generated = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 30, Height: 24),
            new(ForestCoverage: 45, WaterCoverage: 0, RockCoverage: 0), seed,
            Enrichment: new(Enabled: false)), Canonical);
        var prepared = WorldQuickPlay.Prepare(generated.Definition, Canonical);
        var state = GameEngine.CreateGame(prepared, Canonical);
        var board = state.Physical.Board;
        Assert.Contains(board.Terrain, t => board.TerrainDefinition(t).Id == "tree");
        var sources = state.Units.Where(u => u.SideId == HeroesVsMonsters.Heroes)
            .SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToArray();
        var expected = Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width)
            .Select(x => new Cell(x, y))).Where(c => sources.Any(s => Math.Abs(c.X - s.X) + Math.Abs(c.Y - s.Y) <= 5)).ToHashSet();
        Assert.True(expected.SetEquals(state.ExploredCells[HeroesVsMonsters.Heroes]));
        // Ensure these generated cases actually exercise a Tree between a source and a revealed Cell.
        Assert.Contains(sources, s => expected.Any(c => (c.X == s.X || c.Y == s.Y) && board.Terrain.Any(t =>
            board.TerrainDefinition(t).Id == "tree" &&
            (c.X == s.X && t.Position.X == s.X && t.Position.Y > Math.Min(c.Y, s.Y) && t.Position.Y < Math.Max(c.Y, s.Y) ||
             c.Y == s.Y && t.Position.Y == s.Y && t.Position.X > Math.Min(c.X, s.X) && t.Position.X < Math.Max(c.X, s.X)))));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        FogOfWar.Refresh(restored);
        Assert.True(expected.SetEquals(restored.ExploredCells[HeroesVsMonsters.Heroes]));
        Assert.Equal(Json(prepared), Json(WorldQuickPlay.Prepare(generated.Definition, Canonical)));
    }

    [Fact]
    public void DeterministicCanonicalHumanPartyUsesEngineFogAndExplicitNonWinningObjectives()
    {
        var original = Empty() with
        {
            UnitTypeIds = [UnitTypeIds.Goblin],
            Agency = [new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, ControllerKind.Automated)]
        };
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        Assert.Equal(Json(prepared), Json(WorldQuickPlay.Prepare(original, Canonical)));
        Assert.Equal(HeroesVsMonsters.HeroTypes, prepared.Units.Select(u => u.UnitTypeId));
        var state = GameEngine.CreateGame(prepared, Canonical);
        Assert.All(state.Units, u =>
        {
            Assert.Equal(HeroesVsMonsters.Heroes, u.SideId);
            Assert.Equal(ControllerKind.Human, state.ControllerFor(u.Token));
            Assert.Equal(Json(Canonical.Units.Single(t => t.Id == u.TypeId)), Json(state.Types.Single(t => t.Id == u.TypeId)));
        });
        Assert.Equal(new[] { HeroesVsMonsters.Heroes }, state.FogOfWarSides);
        Assert.NotEmpty(state.ExploredCells[HeroesVsMonsters.Heroes]);
        Assert.All(prepared.Units, u => Assert.Contains(u.Anchor, state.ExploredCells[HeroesVsMonsters.Heroes]));
        Assert.Equal(new[] { HeroesVsMonsters.Heroes, HeroesVsMonsters.Monsters }, state.Objectives.Select(o => o.SideId));
        Assert.All(state.Objectives, o => Assert.Equal(ScenarioObjective.NoObjective, o.Objective));
        Assert.False(GameEngine.StartRound(state, new Dice(), autoChooseSingleRelevantChoice: false).State.IsCompleted);
        Assert.Equal(new Cell(0, 0), prepared.Units[0].Anchor);
    }

    [Fact]
    public void PreservesAndDeeplyDetachesOriginalPopulationOrdersRoutesLootAndTerrain()
    {
        var original = Empty() with
        {
            UnitTypeIds = [UnitTypeIds.Goblin],
            Units = [new(UnitTypeIds.Goblin, "world", new(6, 4), UnitId: "resident", GroupId: "residents") { Inventory = [] }],
            Agency = [new(UnitTypeIds.Goblin, "world", ControllerKind.Automated, "residents")],
            Orders = [new(UnitTypeIds.Goblin, "world", OrderKind.FollowRoute, Route: new("route"), GroupId: "residents")],
            Waypoints = [new("waypoint", new(5, 3, 2, 2))], Routes = [new("route", ["waypoint"])],
            NeutralSides = ["world"], Resources = [new(ResourceTypes.Coal, new(3, 3))]
        };
        var before = Json(original);
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        Assert.Equal(before, Json(original));
        Assert.Equal(Json(original.Board), Json(prepared.Board));
        Assert.Equal(Json(original.Units), Json(prepared.Units.Take(original.Units.Count).ToList()));
        Assert.Equal(Json(original.Agency), Json(prepared.Agency.Take(original.Agency.Count).ToList()));
        Assert.Equal(Json(original.Orders), Json(prepared.Orders));
        Assert.Equal(Json(original.Waypoints), Json(prepared.Waypoints));
        Assert.Equal(Json(original.Routes), Json(prepared.Routes));
        Assert.Equal(Json(original.Resources), Json(prepared.Resources));
        Assert.Equal(Json(original.Loot), Json(prepared.Loot));
        Assert.Equal(original.NeutralSides, prepared.NeutralSides);
        Assert.NotSame(original.Units[0], prepared.Units[0]);
        Assert.NotSame(original.Units[0].Inventory, prepared.Units[0].Inventory);
        Assert.NotSame(original.Routes![0], prepared.Routes![0]);
        prepared.Board.Cells.Add(new(new(7, 0), TerrainKind.SolidRock));
        prepared.Orders!.Clear(); prepared.Routes.Clear(); prepared.Waypoints!.Clear(); prepared.Resources!.Clear();
        prepared.Units[0].Inventory!.Add(new("object"));
        prepared.UnitTypeIds.Clear(); prepared.Agency.Clear(); prepared.NeutralSides!.Clear();
        Assert.Equal(before, Json(original));
    }

    [Fact]
    public void ExcludesCompleteLargeFootprintsMachinesAndImpassableTerrain()
    {
        var original = Empty() with
        {
            Board = new() { Width = 8, Height = 6, Cells = [new(new(2, 0), TerrainKind.SolidRock)] },
            UnitTypeIds = [UnitTypeIds.RedDragon],
            Units = [new(UnitTypeIds.RedDragon, "world", new(0, 0))],
            Agency = [new(UnitTypeIds.RedDragon, "world", ControllerKind.Automated)],
            Machines = [new("press", new(3, 0), MachineTypes.DiamondPress)]
        };
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        var occupied = FootprintGeometry.OccupiedCells(Footprint.TwoByTwo, new(0, 0))
            .Append(new(2, 0)).Append(new(3, 0)).ToHashSet();
        Assert.All(prepared.Units.Skip(1), u => Assert.DoesNotContain(u.Anchor, occupied));
        Assert.Equal(5, GameEngine.CreateGame(prepared, Canonical).Units.Count);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    public void CompactClusterDoesNotCrossClosedEdges(EdgeKind edge)
    {
        var original = Empty(6, 1) with
        {
            Board = new() { Width = 6, Height = 1, Edges = [new(new(1, 0), EdgeDirection.Right, edge)] }
        };
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        Assert.Equal(new[] { new Cell(2, 0), new(3, 0), new(4, 0), new(5, 0) }, prepared.Units.Select(u => u.Anchor));
    }

    [Fact]
    public void UsesResolvedLocalTerrainPassabilityInsteadOfLegacyCellKind()
    {
        var original = Empty(5, 1) with
        {
            Board = new() { Width = 5, Height = 1, Cells = [new(new(0, 0), TerrainKind.StoneFloor) { TerrainTypeId = "test-obstruction" }] },
            TerrainTypes = [new("test-obstruction", "Test obstruction", false, true)]
        };
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        Assert.All(prepared.Units, u => Assert.NotEqual(new Cell(0, 0), u.Anchor));
        Assert.Equal(Json(original.TerrainTypes), Json(prepared.TerrainTypes));
        _ = GameEngine.CreateGame(prepared, Canonical);
    }

    [Fact]
    public void OpenDoorAllowsFourCellCorridorCluster()
    {
        var original = Empty(4, 1) with
        {
            Board = new() { Width = 4, Height = 1, Edges = [new(new(1, 0), EdgeDirection.Right, EdgeKind.OpenDoor)] }
        };
        Assert.Equal(4, WorldQuickPlay.Prepare(original, Canonical).Units.Count);
    }

    [Fact]
    public void UnsuitableTerrainCrowdingAndDisconnectedFreeCellsFailClearly()
    {
        var cases = new[]
        {
            Empty(3, 1),
            Empty(8, 6) with { Board = new() { Width = 8, Height = 6, DefaultTerrain = TerrainKind.SolidRock } },
            Empty(4, 1) with { Board = new() { Width = 4, Height = 1, Edges = [new(new(1, 0), EdgeDirection.Right, EdgeKind.ClosedDoor)] } },
            Empty(4, 1) with { UnitTypeIds = [UnitTypeIds.Goblin], Units = [new(UnitTypeIds.Goblin, "world", new(0, 0))], Agency = [new(UnitTypeIds.Goblin, "world", ControllerKind.Automated)] }
        };
        foreach (var original in cases)
            Assert.Contains("four free passable Cells", Assert.Throws<ArgumentException>(() => WorldQuickPlay.Prepare(original, Canonical)).Message);
    }

    [Fact]
    public void ReservesOriginalGeneratedIdsMachineIdsAndGroupIds()
    {
        var original = Empty() with
        {
            UnitTypeIds = [UnitTypeIds.Goblin],
            Units = [new(UnitTypeIds.Goblin, "world", new(7, 3), GroupId: "quick-play-heroes"),
                new(UnitTypeIds.Goblin, "world", new(7, 4), UnitId: "quick-play-rogue-type", GroupId: "quick-play-heroes")],
            Agency = [new(UnitTypeIds.Goblin, "world", ControllerKind.Automated, "quick-play-heroes")],
            Machines = [new("quick-play-barbarian-type", new(7, 5), MachineTypes.DiamondPress)]
        };
        var before = GameEngine.CreateGame(original, Canonical);
        var prepared = WorldQuickPlay.Prepare(original, Canonical);
        var state = GameEngine.CreateGame(prepared, Canonical);
        Assert.Equal(before.Units.Select(u => u.Id), state.Units.Take(2).Select(u => u.Id));
        Assert.All(prepared.Units.Skip(2), u => Assert.Equal("quick-play-heroes-2", u.GroupId));
        Assert.Equal("quick-play-barbarian-type-2", prepared.Units[2].UnitId);
        Assert.Equal("quick-play-rogue-type-2", prepared.Units[3].UnitId);
        Assert.Equal(6, state.Units.Select(u => u.Id).Distinct().Count());
    }

    [Fact]
    public void ConflictingHeroDefinitionIsRejectedWithoutChangingExistingContent()
    {
        var original = Empty() with { UnitTypes = [UnitType.Barbarian() with { DisplayName = "World resident" }] };
        var before = Json(original);
        Assert.Contains("cannot replace", Assert.Throws<ArgumentException>(() => WorldQuickPlay.Prepare(original, Canonical)).Message);
        Assert.Equal(before, Json(original));
    }

    [Fact]
    public void EmbeddedHeroesOverrideLaterWorkspaceDefinitionsAtSharedImportBoundary()
    {
        var prepared = WorldQuickPlay.Prepare(Empty(), Canonical);
        var workspace = new ContentBundle { Units = [UnitType.Barbarian() with { DisplayName = "Workspace override" }] };
        var provider = ContentComposition.ComposeScopes(Canonical, workspace).Definitions;
        var imported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(prepared), null, provider);
        Assert.Equal(Json(Canonical.Units.Single(t => t.Id == UnitTypeIds.Barbarian)),
            Json(GameEngine.CreateGame(imported, provider).Types.Single(t => t.Id == UnitTypeIds.Barbarian)));
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
