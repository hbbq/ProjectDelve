using ProjectDelve.Engine;
using Xunit;
using static ProjectDelve.Engine.Scenario;
using static ProjectDelve.Engine.EdgeDirection;

namespace ProjectDelve.Engine.Tests;

public sealed class ScenarioDefinitionTests
{
    private static ScenarioDefinition Basic() => Define(Map(5, 5), [
        Group(UnitTypeIds.Grunt, "amber", ControllerKind.Human, At(0, 0))
    ]);

    [Fact]
    public void CreationIsFreshRoundZeroAndInitializesNormalUnitContent()
    {
        var definition = Define(Map(5, 5), [
            Group(UnitTypeIds.Barbarian, "violet", ControllerKind.Automated, At(0, 0)),
            Group(UnitTypeIds.Wizard, "amber", ControllerKind.Human, At(2, 2, Posture.Lying, initialHp: 2))
        ]);
        var first = GameEngine.CreateGame(definition);
        var second = GameEngine.CreateGame(definition);
        Assert.Equal(0, first.Round);
        Assert.False(first.RoundComplete);
        Assert.Empty(first.Bag);
        Assert.Null(first.ActiveToken);
        Assert.Null(first.CurrentUnitId);
        Assert.Null(first.Pending);
        Assert.Null(first.AttackInProgress);
        Assert.Null(first.DoorInProgress);
        Assert.Empty(first.ModifiersThisTurn);
        Assert.Empty(first.CompletedUnitIds);
        Assert.False(first.MoveDone);
        Assert.False(first.ActionDone);
        Assert.Equal(5, first.Units[0].CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), first.Units[0].CleaveUses);
        Assert.Equal(new AbilityUses(2, 2), first.Units[0].BonusActionUses["Rage"]);
        Assert.Equal(2, first.Units[1].CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), first.Units[1].FireballUses);
        Assert.Equal(new AbilityUses(2, 2), first.Units[1].BonusActionUses["Focus"]);
        Assert.Equal(Posture.Lying, first.Physical.Figures[1].Posture);
        Assert.Equal(first.Units.Select(u => (u.Id, u.TypeId, u.SideId, u.CurrentHp)),
            second.Units.Select(u => (u.Id, u.TypeId, u.SideId, u.CurrentHp)));
        Assert.Equal(first.Units[0].BonusActionUses.OrderBy(p => p.Key), second.Units[0].BonusActionUses.OrderBy(p => p.Key));
        Assert.NotSame(first.Units, second.Units);
        Assert.NotSame(first.Units[0], second.Units[0]);
        Assert.NotSame(first.Types, second.Types);
        Assert.NotSame(first.Types[0], second.Types[0]);
        Assert.NotSame(first.Controllers, second.Controllers);
        Assert.NotSame(first.Physical.Board, second.Physical.Board);
        Assert.NotSame(first.Physical.Board.Edges, second.Physical.Board.Edges);
        Assert.NotSame(first.Physical.Board.Terrain, second.Physical.Board.Terrain);
        first.Units.Clear(); first.Types.Clear(); first.Controllers.Clear(); first.Physical.Figures.Clear();
        first.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        first.Physical.Board.Terrain.Add(new(new(0, 0), TerrainKind.Tree));
        Assert.Equal(2, second.Units.Count);
        Assert.Empty(second.Physical.Board.Edges);
        Assert.Empty(second.Physical.Board.Terrain);
        Assert.Empty(definition.Board.Edges);
        Assert.Equal(2, definition.Units.Count);
        // Creation has no random/provider dependency. Randomness begins only on StartRound.
        Assert.Throws<InvalidOperationException>(() => GameEngine.StartRound(second, new NoRandom()));
    }

    [Theory]
    [InlineData(TerrainKind.Grass)]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.Water)]
    [InlineData(TerrainKind.StoneFloor)]
    [InlineData(TerrainKind.StoneFloorWithTable)]
    public void DefaultTerrainAndSparseOverridesMaterializeFaithfully(TerrainKind terrain)
    {
        var definition = Define(Map(3, 2, cells: [Tile(1, 1, TerrainKind.StoneFloor)], defaultTerrain: terrain), []);
        var board = GameEngine.CreateGame(definition).Physical.Board;
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 3; x++)
            Assert.Equal(x == 1 && y == 1 ? TerrainKind.StoneFloor : terrain, board.TerrainAt(new(x, y)));
    }

    [Fact]
    public void CanonicalDirectionsMaterializeCorrectlyWithoutChangingDoorState()
    {
        var definition = Basic() with { Board = Map(5, 5) with { Edges = [
            new(new(1, 1), Right, EdgeKind.ClosedDoor),
            new(new(3, 1), Down, EdgeKind.OpenDoor),
            new(new(0, 2), Right, EdgeKind.WallWithWindow)
        ] } };
        var board = GameEngine.CreateGame(definition).Physical.Board;
        Assert.Equal(new Cell(1, 1), board.Edges[0].A);
        Assert.Equal(new Cell(2, 1), board.Edges[0].B);
        Assert.Equal(new Cell(3, 1), board.Edges[1].A);
        Assert.Equal(new Cell(3, 2), board.Edges[1].B);
        Assert.Equal(new EdgeDefinition(new(1, 1), Right, EdgeKind.ClosedDoor), definition.Board.Edges[0]);
        Assert.Equal(EdgeKind.ClosedDoor, board.EdgeBetween(new(1, 1), new(2, 1)));
        Assert.Equal(EdgeKind.OpenDoor, board.EdgeBetween(new(3, 1), new(3, 2)));
        Assert.Equal(EdgeKind.WallWithWindow, board.EdgeBetween(new(0, 2), new(1, 2)));
    }

    [Fact]
    public void GroupsMergeWithIndependentAgencyAndExplicitTypeOrder()
    {
        var definition = Define(Map(5, 5), [
            Group(UnitTypeIds.Grunt, "amber", ControllerKind.Human, At(0, 0), At(1, 0)),
            Group(UnitTypeIds.Grunt, "violet", ControllerKind.Automated, At(2, 0)),
            Group(UnitTypeIds.Grunt, "amber", ControllerKind.Human, At(3, 0)),
            Group(UnitTypeIds.Goblin, "violet", ControllerKind.Human)
        ], unitTypeIds: [UnitTypeIds.Goblin, UnitTypeIds.Grunt]);
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(new[] { UnitTypeIds.Goblin, UnitTypeIds.Grunt }, state.Types.Select(t => t.Id));
        Assert.Equal(4, state.Units.Count);
        Assert.Equal(3, state.Controllers.Count);
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "amber")));
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "violet")));
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Goblin, "violet")));
        Assert.Equal(4, state.Units.Select(u => u.Id).Distinct().Count());
        Assert.Equal(state.Units.Select(u => u.Id), GameEngine.CreateGame(definition).Units.Select(u => u.Id));
        Assert.Throws<ArgumentException>(() => Define(Map(5, 5), [
            Group(UnitTypeIds.Grunt, "amber", ControllerKind.Human, At(0, 0)),
            Group(UnitTypeIds.Grunt, "amber", ControllerKind.Automated, At(1, 0))
        ]));
    }

    [Fact]
    public void DragonIsOneUnitWithResolvedFootprintAndOpenInternalEdge()
    {
        var definition = Define(Map(4, 4, edges: [Edge(1, 1, Right, EdgeKind.OpenDoor)]), [
            Group(UnitTypeIds.RedDragon, "amber", ControllerKind.Human, At(1, 1, Posture.Lying))
        ]);
        var state = GameEngine.CreateGame(definition);
        var unit = Assert.Single(state.Units);
        Assert.Single(state.Physical.Figures);
        Assert.Equal(8, unit.CurrentHp);
        Assert.Equal(new[] { new Cell(1, 1), new(2, 1), new(1, 2), new(2, 2) }, FootprintGeometry.OccupiedCells(state, unit.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SummoningResolvesCanonicalContentAndPreservesOrInheritsAgency(bool initialGoblin, bool explicitAgency)
    {
        var groups = new List<UnitGroup> { Group(UnitTypeIds.Shaman, "amber", ControllerKind.Human, At(2, 2)) };
        if (initialGoblin || explicitAgency)
            groups.Add(Group(UnitTypeIds.Goblin, "amber", explicitAgency ? ControllerKind.Automated : ControllerKind.Human,
                initialGoblin ? [At(0, 0)] : []));
        var definition = Define(Map(5, 5), groups.ToArray());
        var initial = GameEngine.CreateGame(definition);
        var random = new FirstToken();
        var result = GameEngine.StartRound(initial, random, false);
        result = GameEngine.Advance(result.State, new Pick("stay"), random, false);
        var summon = result.NextInput!.Candidates.First(c => c.Action == UnitAction.SummonAdjacent);
        result = GameEngine.Advance(result.State, new Pick(summon.Key), random, false);
        var created = Assert.Single(result.Events, e => e.Kind == "UnitCreated");
        Assert.DoesNotContain(initial.Units, u => u.Id == created.UnitId);
        Assert.Equal(result.State.Units.Count, result.State.Units.Select(u => u.Id).Distinct().Count());
        Assert.Equal(UnitType.Goblin(), result.State.Types.Single(t => t.Id == UnitTypeIds.Goblin));
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == created.UnitId).Posture);
        Assert.Equal(explicitAgency ? ControllerKind.Automated : ControllerKind.Human,
            result.State.ControllerFor(new ActivationToken(UnitTypeIds.Goblin, "amber")));
        // With no initial Goblin there was no Goblin token in this round, even with explicit agency.
        if (!initialGoblin)
        {
            Assert.DoesNotContain(result.Events, e => e.Kind == "TokenDrawn" && e.TypeId == UnitTypeIds.Goblin);
            Assert.DoesNotContain(result.State.Bag, t => t.TypeId == UnitTypeIds.Goblin);
        }
    }

    [Fact]
    public void TypeOrderControlsDeterministicFirstTokenAndBagSideOrdering()
    {
        var definition = Define(Map(5, 5), [
            Group(UnitTypeIds.Grunt, "violet", ControllerKind.Human, At(0, 0)),
            Group(UnitTypeIds.Grunt, "amber", ControllerKind.Human, At(2, 0)),
            Group(UnitTypeIds.Shaman, "amber", ControllerKind.Human, At(4, 4))
        ], unitTypeIds: [UnitTypeIds.Shaman, UnitTypeIds.Grunt]);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new FirstToken(), false);
        Assert.Equal(new ActivationToken(UnitTypeIds.Shaman, "amber"), result.State.ActiveToken);
        Assert.Equal(new[] { new ActivationToken(UnitTypeIds.Grunt, "amber"), new ActivationToken(UnitTypeIds.Grunt, "violet") }, result.State.Bag);
    }

    [Theory]
    [InlineData("board-null")]
    [InlineData("types-null")]
    [InlineData("units-null")]
    [InlineData("agency-null")]
    [InlineData("cells-null")]
    [InlineData("edges-null")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("default-enum")]
    [InlineData("terrain-enum")]
    [InlineData("terrain-outside")]
    [InlineData("terrain-duplicate")]
    [InlineData("terrain-null")]
    [InlineData("edge-null")]
    [InlineData("edge-enum")]
    [InlineData("edge-outside")]
    [InlineData("edge-direction-enum")]
    [InlineData("edge-right-boundary")]
    [InlineData("edge-down-boundary")]
    [InlineData("edge-duplicate")]
    [InlineData("type-unknown")]
    [InlineData("type-duplicate")]
    [InlineData("type-blank")]
    [InlineData("placement-undeclared")]
    [InlineData("placement-null")]
    [InlineData("anchor-null")]
    [InlineData("side-blank")]
    [InlineData("posture-enum")]
    [InlineData("agency-missing")]
    [InlineData("agency-duplicate")]
    [InlineData("agency-conflict")]
    [InlineData("agency-null-entry")]
    [InlineData("agency-enum")]
    [InlineData("agency-undeclared")]
    [InlineData("agency-side-blank")]
    [InlineData("overlap")]
    [InlineData("outside")]
    [InlineData("impassable")]
    [InlineData("hp-zero")]
    [InlineData("hp-negative")]
    [InlineData("hp-excess")]
    public void MalformedDefinitionFailsAtCreation(string invalid)
    {
        var definition = Basic();
        var board = definition.Board;
        var unit = definition.Units[0];
        var agency = definition.Agency[0];
        definition = invalid switch
        {
            "board-null" => definition with { Board = null! },
            "types-null" => definition with { UnitTypeIds = null! },
            "units-null" => definition with { Units = null! },
            "agency-null" => definition with { Agency = null! },
            "cells-null" => definition with { Board = board with { Cells = null! } },
            "edges-null" => definition with { Board = board with { Edges = null! } },
            "width" => definition with { Board = board with { Width = 0 } },
            "height" => definition with { Board = board with { Height = -1 } },
            "default-enum" => definition with { Board = board with { DefaultTerrain = (TerrainKind)99 } },
            "terrain-enum" => definition with { Board = board with { Cells = [Tile(0, 0, (TerrainKind)99)] } },
            "terrain-outside" => definition with { Board = board with { Cells = [Tile(-1, 0, TerrainKind.Grass)] } },
            "terrain-duplicate" => definition with { Board = board with { Cells = [Tile(0, 0, TerrainKind.Grass), Tile(0, 0, TerrainKind.Tree)] } },
            "terrain-null" => definition with { Board = board with { Cells = [null!] } },
            "edge-null" => definition with { Board = board with { Edges = [new(null!, Right, EdgeKind.Wall)] } },
            "edge-enum" => definition with { Board = board with { Edges = [new(new(0, 0), Right, (EdgeKind)99)] } },
            "edge-outside" => definition with { Board = board with { Edges = [new(new(-1, 0), Right, EdgeKind.Wall)] } },
            "edge-direction-enum" => definition with { Board = board with { Edges = [new(new(0, 0), (EdgeDirection)99, EdgeKind.Wall)] } },
            "edge-right-boundary" => definition with { Board = board with { Edges = [new(new(4, 0), Right, EdgeKind.Wall)] } },
            "edge-down-boundary" => definition with { Board = board with { Edges = [new(new(0, 4), Down, EdgeKind.Wall)] } },
            "edge-duplicate" => definition with { Board = board with { Edges = [new(new(0, 0), Right, EdgeKind.Wall), new(new(0, 0), Right, EdgeKind.OpenDoor)] } },
            "type-unknown" => definition with { UnitTypeIds = [.. definition.UnitTypeIds, "missing-type"] },
            "type-duplicate" => definition with { UnitTypeIds = [UnitTypeIds.Grunt, UnitTypeIds.Grunt] },
            "type-blank" => definition with { UnitTypeIds = [" "] },
            "placement-undeclared" => definition with { Units = [unit with { UnitTypeId = UnitTypeIds.Goblin }] },
            "placement-null" => definition with { Units = [null!] },
            "anchor-null" => definition with { Units = [unit with { Anchor = null! }] },
            "side-blank" => definition with { Units = [unit with { SideId = " " }] },
            "posture-enum" => definition with { Units = [unit with { Posture = (Posture)99 }] },
            "agency-missing" => definition with { Agency = [] },
            "agency-duplicate" => definition with { Agency = [agency, agency] },
            "agency-conflict" => definition with { Agency = [agency, agency with { Controller = ControllerKind.Automated }] },
            "agency-null-entry" => definition with { Agency = [null!] },
            "agency-enum" => definition with { Agency = [agency with { Controller = (ControllerKind)99 }] },
            "agency-undeclared" => definition with { Agency = [agency with { UnitTypeId = UnitTypeIds.Goblin }] },
            "agency-side-blank" => definition with { Agency = [agency with { SideId = " " }] },
            "overlap" => definition with { Units = [unit, unit] },
            "outside" => definition with { Units = [unit with { Anchor = new(5, 0) }] },
            "impassable" => definition with { Board = board with { Cells = [Tile(0, 0, TerrainKind.Water)] } },
            "hp-zero" => definition with { Units = [unit with { InitialHp = 0 }] },
            "hp-negative" => definition with { Units = [unit with { InitialHp = -1 }] },
            "hp-excess" => definition with { Units = [unit with { InitialHp = 2 }] },
            _ => throw new InvalidOperationException(invalid)
        };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void ImpassableInternalFootprintEdgeIsRejected(EdgeKind edge)
    {
        var definition = Define(Map(4, 4, edges: [Edge(1, 1, Right, edge)]), [
            Group(UnitTypeIds.RedDragon, "side", ControllerKind.Human, At(1, 1))
        ]);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    public void FullFootprintMustBeInsideBoard(int x, int y)
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Define(Map(4, 4), [
            Group(UnitTypeIds.RedDragon, "side", ControllerKind.Human, At(x, y))
        ])));
    }

    [Fact]
    public void FootprintOverlapAndGlobalUniqueAreRejectedAcrossSidesAndPostures()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Define(Map(5, 5), [
            Group(UnitTypeIds.RedDragon, "amber", ControllerKind.Human, At(1, 1)),
            Group(UnitTypeIds.Grunt, "violet", ControllerKind.Automated, At(2, 2))
        ])));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Define(Map(5, 5), [
            Group(UnitTypeIds.Barbarian, "amber", ControllerKind.Human, At(0, 0)),
            Group(UnitTypeIds.Barbarian, "violet", ControllerKind.Automated, At(3, 3, Posture.Lying))
        ])));
        // Phase affects traversal, never initial stopping legality.
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Define(Map(3, 3, cells: [Tile(0, 0, TerrainKind.Tree)]), [
            Group(UnitTypeIds.Ghost, "side", ControllerKind.Human, At(0, 0))
        ])));
    }

    private sealed class Pick(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class FirstToken : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
    private sealed class NoRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => throw new InvalidOperationException();
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
}
