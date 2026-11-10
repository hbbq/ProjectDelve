using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SummonCandidateEquivalenceTests
{
    // Frozen pre-optimization discovery/occupancy algorithm. Deliberately does not
    // call CanPlaceUnit or a bounded-anchor helper: omitted anchors and index bugs
    // must be visible to this oracle. Unchanged terrain/LOS rules remain shared.
    private static IEnumerable<Candidate> Exhaustive(GameState state, Unit actor, ResolvedAbility ability)
    {
        if (ability.Definition.Mechanic is not AdjacentUnitSummonMechanic summon) yield break;
        if (summon.SameTypeSideLimit is { } limit && state.Units.Count(u => u.CurrentHp > 0 &&
            u.SideId == actor.SideId && u.TypeId == summon.UnitTypeId &&
            state.Physical.Figures.Any(f => f.Id == u.Id)) >= limit) yield break;
        if (summon.SameTypeSummonerLimit is { } creatorLimit && state.Units.Count(u => u.CurrentHp > 0 &&
            u.SummonerUnitId == actor.Id && u.TypeId == summon.UnitTypeId &&
            state.Physical.Figures.Any(f => f.Id == u.Id)) >= creatorLimit) yield break;
        if (state.Types.SingleOrDefault(t => t.Id == summon.UnitTypeId) is not { } type ||
            !state.CanPlaceUnitType(type, actor.SideId)) yield break;
        var board = state.Physical.Board;
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var anchor = new Cell(x, y);
            var cells = FootprintGeometry.OccupiedCells(type.Footprint, anchor);
            if (SpatialRules.Fits(board, type.Footprint, anchor, MovementTraversal.OccupancyFor(type)) &&
                !state.Physical.Figures.Any(f => FootprintGeometry.OccupiedCells(state, f.Id).Intersect(cells).Any()) &&
                cells.All(c => TerrainCatalog.Accepts(type, board.TerrainDefinitionAt(c))) &&
                SpatialRules.AreFootprintsAdjacent(board, FootprintGeometry.OccupiedCells(state, actor.Id), cells))
                yield return new($"{ability.CandidatePrefix}:{x},{y}", Destination: anchor,
                    Action: UnitAction.AdjacentUnitSummon) { Ability = ability };
        }
    }

    private static void Equivalent(GameState state)
    {
        var actor = state.Units.Single(u => u.Id == "actor");
        var expected = state.AbilitiesOf(actor.Id).Where(a => a.Definition.Mechanic is AdjacentUnitSummonMechanic &&
            state.CanUseAbility(actor.Id, a)).SelectMany(a => Exhaustive(state, actor, a)).ToArray();
        var actual = SummoningRules.Candidates(state, actor).ToArray();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Equal(actual.Length, actual.Select(c => c.Key).Distinct().Count());
    }

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var source in new[] { Footprint.OneByOne, Footprint.TwoByTwo })
        foreach (var target in new[] { Footprint.OneByOne, Footprint.TwoByTwo })
        foreach (var x in new[] { 0, 3, source == Footprint.OneByOne ? 6 : 5 })
        foreach (var y in new[] { 0, 3, source == Footprint.OneByOne ? 6 : 5 })
        foreach (var variant in Enumerable.Range(0, 8))
            yield return [source, target, x, y, variant];
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void BoundedDiscoveryMatchesWholeBoardInStableOrder(Footprint source, Footprint target, int x, int y, int variant)
    {
        var actorType = new UnitType("caller", 2, 1, 1, 0, 1)
        {
            Footprint = source,
            LocalAbilities = [new("call", "Call", AbilityTiming.Action, new AdjacentUnitSummonMechanic("local", Posture.Lying))]
        };
        var targetType = new UnitType("local", 2, 1, 1, 0, 1)
        {
            Footprint = target, Amphibious = variant == 5,
            TerrainRestriction = variant == 6 ? new([TerrainCategories.RacingGround]) : null
        };
        if (variant == 7) targetType = targetType.WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        var state = new GameState
        {
            Physical = new(new Board(7, 7, []), [new("actor", new(x, y))]),
            Types = [actorType, targetType], Units = [actorType.CreateUnit("actor", "blue")],
            ResourceDefinitions = [.. ResourceTypes.All]
        };
        var sourceCells = FootprintGeometry.OccupiedCells(state, "actor");
        for (var by = 0; by < 7; by++)
        for (var bx = 0; bx < 7; bx++)
        {
            var cell = new Cell(bx, by);
            if (sourceCells.Contains(cell)) continue;
            var hash = (bx * 13 + by * 7) % 9;
            if (variant is 1 or 5 or 6 or 7 && hash < 3)
                state.Physical.Board.Terrain.Add(new(cell, variant == 6 ? TerrainKind.Asphalt :
                    variant == 5 ? TerrainKind.Water : TerrainKind.SolidRock));
            if (variant == 2 && hash == 0)
            {
                var id = $"blocker-{bx}-{by}";
                // Single-cell blockers, even when the summoner is large.
                state.Units.Add(new(id, "blocker", "red", 1));
                state.Physical.Figures.Add(new(id, cell, Posture.Lying));
            }
            if (variant == 3 && hash == 0) state.Physical.Board.Machines.Add(new($"machine-{bx}-{by}", cell, MachineTypes.DiamondPress));
            if (variant == 4 && bx < 6 && hash < 4)
            {
                var other = new Cell(bx + 1, by);
                if (!sourceCells.Contains(other)) state.Physical.Board.Edges.Add(new(cell, other,
                    new[] { EdgeKind.Wall, EdgeKind.ClosedDoor, EdgeKind.OpenDoor, EdgeKind.WallWithWindow }[hash]));
            }
        }
        if (variant == 2) state.Types.Add(new("blocker", 1, 1, 1, 0, 1));
        Equivalent(state);
    }

    [Fact]
    public void LargeSummonIncludesAnchorsTwoCellsAboveAndLeft()
    {
        var state = ScenarioState();
        var localIndex = state.Types.FindIndex(t => t.Id == "local");
        state.Types[localIndex] = state.Types[localIndex] with { Footprint = Footprint.TwoByTwo };
        Equivalent(state);
        var candidates = SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "actor")).ToArray();
        Assert.Contains(candidates, c => c.Destination == new Cell(1, 3));
        Assert.Contains(candidates, c => c.Destination == new Cell(3, 1));
    }

    [Fact]
    public void ReenumeratingQueryAfterMutationBuildsFreshOccupancy()
    {
        var state = ScenarioState();
        var actor = state.Units.Single(u => u.Id == "actor");
        var query = SummoningRules.Candidates(state, actor);
        var abilityQuery = SummoningRules.Candidates(state, actor, state.AbilitiesOf(actor.Id).First());
        var destination = query.First().Destination!;
        Assert.Contains(abilityQuery, c => c.Destination == destination);
        state.PlaceUnit("local", "new-occupant", "blue", destination);
        Assert.DoesNotContain(query, c => c.Destination == destination);
        Assert.DoesNotContain(abilityQuery, c => c.Destination == destination);
        Equivalent(state);
        var figureIndex = state.Physical.Figures.FindIndex(f => f.Id == "new-occupant");
        state.Physical.Figures[figureIndex] = state.Physical.Figures[figureIndex] with { Position = new(8, 8) };
        Assert.Contains(query, c => c.Destination == destination);
        Equivalent(state);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void FixedComponentObstructionsRemainPartOfAuthoritativePlacement(bool blocksMovement, bool blocksLos)
    {
        var state = ScenarioState();
        var component = new FixedComponentDefinition("local-feature", "Local Feature", blocksMovement, blocksLos);
        var assembly = new TerrainAssemblyDefinition("local-assembly", "Local Assembly", "grass")
            { Components = [new("feature", component.Id)] };
        state.Physical = state.Physical with { Board = state.Physical.Board with
            { TerrainTypes = TerrainCatalog.Resolve(null, [assembly], [component]) } };
        var destination = new Cell(2, 3);
        state.Physical.Board.Terrain.Add(new(destination, TerrainKind.Grass) { TypeId = assembly.Id });
        Equivalent(state);
        var candidates = SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "actor"));
        if (blocksMovement) Assert.DoesNotContain(candidates, c => c.Destination == destination);
        else if (!blocksLos) Assert.Contains(candidates, c => c.Destination == destination);
    }

    [Theory]
    [InlineData(null, null, false)] [InlineData(1, null, false)] [InlineData(null, 1, false)]
    [InlineData(2, 2, false)] [InlineData(null, null, true)]
    public void GrantedSourcesLimitsLocalTypesAndRestorationMatch(int? sideLimit, int? creatorLimit, bool unique)
    {
        var state = ScenarioState(sideLimit, creatorLimit, unique);
        Equivalent(state);
        state.PlaceUnit("local", "existing", "blue", new(8, 8), summonerUnitId: "actor");
        Equivalent(state);
        Equivalent(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        var existingIndex = state.Units.FindIndex(u => u.Id == "existing");
        state.Units[existingIndex] = state.Units[existingIndex] with { CurrentHp = 0 };
        Equivalent(state); // Defeated data cannot consume a living population limit.
        state.Physical.Figures.RemoveAll(f => f.Id == "existing");
        Equivalent(state);
    }

    private static GameState ScenarioState(int? sideLimit = null, int? creatorLimit = null, bool unique = false)
    {
        LocalAbilityDefinition Call() => new("call", "Call", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic("local", Posture.Lying, sideLimit, creatorLimit));
        return GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 10, Height = 10 }, UnitTypeIds = ["caller", "local"],
            UnitTypes = [new("caller", 2, 1, 1, 0, 1) { CanHaveEquipment = true, CanHaveItems = true, LocalAbilities = [Call()] },
                new("local", 2, 1, 1, 0, 1) { Unique = unique }],
            EquipmentTypes = [new("charm", "Charm") { LocalAbilities = [Call()] }],
            ItemTypes = [new("scroll", "Scroll") { LocalAbilities = [Call()] }],
            Units = [new("caller", "blue", new(3, 3), UnitId: "actor")
                { Inventory = [new("charm-one", "charm"), new("scroll-one") { ItemTypeId = "scroll" }] }],
            Agency = [new("caller", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        });
    }
}
