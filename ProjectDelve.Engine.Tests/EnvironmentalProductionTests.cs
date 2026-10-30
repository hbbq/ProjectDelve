using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EnvironmentalProductionTests
{
    private sealed class Dice(params int[] rolls) : IRandomProvider
    {
        public int Calls { get; private set; }
        public int RollD8() => rolls[Calls++ % rolls.Length];
        public int RollD6() => throw new InvalidOperationException("Production must consume only d8 rolls.");
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private static TerrainDefinition Source(string id = "odd-fountain-62", string resource = "shell", int attempts = 2,
        bool blocksLos = false, ProductionSourceLos sourceLos = ProductionSourceLos.RespectTerrain) =>
        new(id, "The Unexpected Fountain", false, blocksLos)
        {
            Production = new DirectionalResourceProduction(resource, attempts, SourceLos: sourceLos),
            Presentation = new("an-unfamiliar-recipe")
        };

    private static CellDefinition Tile(int x, int y, string id) => new(new(x, y), TerrainKind.StoneFloor) { TerrainTypeId = id };

    private static ScenarioDefinition Fixture(params CellDefinition[] cells) => new()
    {
        Loot = LootSettings.Empty,
        Board = new() { Width = 10, Height = 9, Cells = [.. cells] },
        UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0), UnitId: "blue"),
            new(UnitTypeIds.Grunt, "red", new(9, 8), UnitId: "red")],
        Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)]
    };

    [Fact]
    public void ExplicitLavaDefinitionsSharePropertiesAndOnlyCoalLavaProduces()
    {
        var ordinary = TerrainCatalog.Find(TerrainCatalog.All, TerrainCompatibility.OrdinaryLavaId);
        var coal = TerrainCatalog.Find(TerrainCatalog.All, TerrainCompatibility.CoalLavaId);
        Assert.Null(ordinary.Production);
        Assert.Equal(new DirectionalResourceProduction("coal", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell), coal.Production);
        Assert.False(ordinary.Passable); Assert.False(ordinary.BlocksLos); Assert.False(ordinary.AllowsResourceMarkers);
        Assert.Equal(ordinary.Presentation, coal.Presentation);
        Assert.Equal(ordinary.Passable, coal.Passable);
        Assert.Equal(ordinary.BlocksLos, coal.BlocksLos);
        Assert.Equal(ordinary.AllowsResourceMarkers, coal.AllowsResourceMarkers);
        var dice = new Dice(1);
        var initial = GameEngine.CreateGame(Fixture(Tile(2, 2, ordinary.Id), Tile(5, 2, coal.Id)));
        var result = GameEngine.StartRound(initial, dice, false);
        Assert.Equal(2, dice.Calls);
        Assert.Equal(new Resource(ResourceTypes.Coal, new(5, 1)), Assert.Single(result.State.Resources));
        Assert.All(result.Events.Where(e => e.Kind == "LavaCoalRolled"), e => Assert.Equal(new Cell(5, 2), e.Cell));
        Assert.Throws<ArgumentException>(() => initial.PlaceResource(ResourceTypes.Coal, new(2, 2)));
    }

    [Theory]
    [InlineData("coal")] [InlineData("diamond")] [InlineData("fruit")]
    [InlineData("banana")] [InlineData("mushroom")] [InlineData("shell")]
    public void UnfamiliarContentSelectsExistingResourceAndAttemptCountFromDeclaration(string resource)
    {
        var source = Source(resource: resource, attempts: 3);
        var initial = GameEngine.CreateGame(Fixture(Tile(2, 2, source.Id)) with { TerrainTypes = [source] });
        var dice = new Dice(1, 3, 5);
        var result = GameEngine.StartRound(initial, dice, false);
        Assert.Equal(3, dice.Calls);
        Assert.Equal(new[] { new Cell(2, 1), new(3, 2), new(2, 3) }, result.State.Resources.Select(r => r.Position));
        Assert.All(result.State.Resources, r => Assert.Equal(ResourceTypes.Find(resource), r.Type));
        Assert.All(result.Events.Where(e => e.Kind == "TerrainProductionRolled"), e =>
        {
            Assert.Equal(source.Id, e.TerrainTypeId); Assert.Equal(source.Name, e.TerrainName);
            Assert.Equal(ResourceTypes.Find(resource), e.ResourceType); Assert.Equal("terrain-production", e.ActionId);
        });
        Assert.Empty(initial.Resources);
        Assert.Contains("3 d8 attempts", source.ProductionDescription);
        Assert.Contains(ResourceTypes.Find(resource).Name, source.ProductionDescription);
    }

    [Theory]
    [InlineData(1, 2, 1)] [InlineData(2, 3, 1)] [InlineData(3, 3, 2)] [InlineData(4, 3, 3)]
    [InlineData(5, 2, 3)] [InlineData(6, 1, 3)] [InlineData(7, 1, 2)] [InlineData(8, 1, 1)]
    public void GenericProducerUsesClockwiseD8DirectionsAndDoesNotRerollCollision(int roll, int x, int y)
    {
        var source = Source();
        var dice = new Dice(roll);
        var result = GameEngine.StartRound(GameEngine.CreateGame(Fixture(Tile(2, 2, source.Id)) with { TerrainTypes = [source] }), dice, false);
        Assert.Equal(2, dice.Calls);
        Assert.Equal(new Cell(x, y), Assert.Single(result.State.Resources).Position);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "TerrainProductionRolled"));
    }

    [Theory]
    [InlineData(ProductionSourceLos.RespectTerrain, false)]
    [InlineData(ProductionSourceLos.IgnoreProducingCell, true)]
    public void SourceLosPolicyControlsOnlyTheProducingCellsTerrain(ProductionSourceLos policy, bool creates)
    {
        var source = Source(blocksLos: true, sourceLos: policy);
        var dice = new Dice(1);
        var result = GameEngine.StartRound(GameEngine.CreateGame(Fixture(Tile(2, 2, source.Id)) with { TerrainTypes = [source] }), dice, false);
        Assert.Equal(2, dice.Calls);
        Assert.Equal(creates, result.State.Resources.Any());
    }

    [Theory]
    [InlineData(EdgeKind.OpenDoor, true)] [InlineData(EdgeKind.ClosedDoor, false)] [InlineData(EdgeKind.Wall, false)]
    public void SourceExemptionPreservesNeighborTerrainAndClosedCorners(EdgeKind edge, bool creates)
    {
        var source = Source(blocksLos: true, sourceLos: ProductionSourceLos.IgnoreProducingCell);
        var definition = Fixture(Tile(2, 2, source.Id), new(new(3, 2), TerrainKind.SolidRock));
        definition = definition with { TerrainTypes = [source], Board = definition.Board with
            { Edges = [new(new(2, 2), EdgeDirection.Down, edge)] } };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(4), false);
        Assert.Equal(creates, result.State.Resources.Any(r => r.Position == new Cell(3, 3)));
    }

    [Fact]
    public void FailedLocalAttemptsKeepExactRandomConsumptionAcrossDestinations()
    {
        var source = Source(attempts: 8);
        var definition = Fixture(Tile(2, 2, source.Id), new(new(2, 1), TerrainKind.Water), new(new(3, 1), TerrainKind.StoneFloorWithTable));
        definition = definition with { TerrainTypes = [source], Machines = [new("press", new(3, 2), MachineTypes.DiamondPress)],
            Resources = [new(ResourceTypes.Coal, new(3, 3))],
            Board = definition.Board with { Edges = [new(new(2, 2), EdgeDirection.Down, EdgeKind.ClosedDoor)] } };
        var dice = new Dice(1, 2, 3, 4, 5, 6, 7, 8);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        Assert.Equal(8, dice.Calls);
        Assert.Equal(8, result.Events.Count(e => e.Kind == "TerrainProductionRolled"));
        Assert.DoesNotContain(result.State.Resources, r => r.Position == new Cell(2, 1) || r.Position == new Cell(3, 1) ||
            r.Position == new Cell(3, 2) || r.Position == new Cell(2, 3));
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(1, 2) && r.Type == ResourceTypes.Shell);
        Assert.Equal(ResourceTypes.Coal, result.State.Resources.Single(r => r.Position == new Cell(3, 3)).Type);
    }

    [Fact]
    public void MixedProducerTypesResolveInCellOrderAgainstUpdatedState()
    {
        var source = Source(attempts: 1);
        var definition = Fixture(new(new(2, 4), TerrainKind.FruitTree), Tile(4, 1, TerrainCompatibility.CoalLavaId),
            Tile(2, 1, source.Id), Tile(0, 2, TerrainCompatibility.OrdinaryLavaId)) with { TerrainTypes = [source] };
        var dice = new Dice(3, 7, 5, 1, 3);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        var rolls = result.Events.Where(e => e.Kind.EndsWith("Rolled") && e.DieRoll is not null).ToArray();
        Assert.Equal(new[] { "TerrainProductionRolled", "LavaCoalRolled", "LavaCoalRolled", "FruitTreeRolled", "FruitTreeRolled" }, rolls.Select(e => e.Kind));
        Assert.Equal(new[] { new Cell(2, 1), new(4, 1), new(4, 1), new(2, 4), new(2, 4) }, rolls.Select(e => e.Cell));
        Assert.Equal(5, dice.Calls);
        Assert.Equal(ResourceTypes.Shell, result.State.Resources.Single(r => r.Position == new Cell(3, 1)).Type);
        Assert.Equal(new[] { new Cell(3, 1), new(4, 2), new(2, 3), new(3, 4) }, result.State.Resources.Select(r => r.Position));
    }

    [Fact]
    public void WorldResolutionCompletesBeforeGenericProductionAndBagPopulation()
    {
        var source = Source();
        var definition = Fixture(Tile(2, 2, source.Id));
        definition = definition with { TerrainTypes = [source], WorldEffects = new(1, 1),
            Board = definition.Board with { Edges = [new(new(2, 2), EdgeDirection.Right, EdgeKind.ClosedDoor)] } };
        var initial = GameEngine.CreateGame(definition);
        initial.WorldDeck = new() { DrawPile = [new("open", WorldEffect.OpenSesame)] };
        var result = GameEngine.StartRound(initial, new Dice(3), false);
        var kinds = result.Events.Select(e => e.Kind).ToList();
        Assert.True(kinds.LastIndexOf("WorldCardDiscarded") < kinds.IndexOf("TerrainProductionRolled"));
        Assert.True(kinds.LastIndexOf("ResourceCreated") < kinds.IndexOf("ActivationBagPopulated"));
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(3, 2));
        Assert.All(result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "ResourceCreated"), s => Assert.Empty(s.StateAfter.Bag));
    }

    [Fact]
    public void SaveResumeAndReplayKeepProductionContentRollsAndIntermediateSnapshots()
    {
        var source = Source(attempts: 3);
        var initial = GameEngine.CreateGame(Fixture(Tile(2, 2, source.Id)) with { TerrainTypes = [source] });
        var loaded = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(initial))!;
        var first = GameEngine.StartRound(initial, new Dice(1, 1, 3), false);
        var replay = GameEngine.StartRound(loaded, new Dice(1, 1, 3), false);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(source.Production, loaded.Physical.Board.TerrainDefinitionAt(new(2, 2)).Production);
        var counts = first.ResolutionSteps.Where(s => first.Events[s.EventIndex].Kind is "TerrainProductionRolled" or "ResourceCreated")
            .Select(s => s.StateAfter.Resources.Count);
        Assert.Equal(new[] { 0, 1, 1, 1, 2 }, counts);
    }

    [Fact]
    public void FogHidesUnknownProducerDefinitionsAndEventsButAllowsKnownDestinationResource()
    {
        var source = Source();
        var initial = GameEngine.CreateGame(Fixture(Tile(7, 6, source.Id)) with { TerrainTypes = [source] });
        initial.FogOfWarSides = ["blue"];
        initial.ExploredCells["blue"] = [new(7, 5)];
        var result = GameEngine.StartRound(initial, new Dice(1), false);
        var roll = result.Events.First(e => e.Kind == "TerrainProductionRolled");
        Assert.Null(FogOfWar.ObserveEvent(result.State, "blue", roll));
        var created = result.Events.Single(e => e.Kind == "ResourceCreated");
        var visible = Assert.IsType<RulesEvent>(FogOfWar.ObserveEvent(result.State, "blue", created));
        Assert.Null(visible.TerrainName); Assert.Null(visible.TerrainTypeId);
        var observed = FogOfWar.Observe(result.State, "blue");
        Assert.DoesNotContain(observed.Physical.Board.TerrainTypes, d => d.Id == source.Id);
        Assert.Single(observed.Resources);
        result.State.ExploredCells["blue"].Add(new(7, 6));
        var known = Assert.IsType<RulesEvent>(FogOfWar.ObserveEvent(result.State, "blue", roll));
        Assert.Equal(source.Id, known.TerrainTypeId); Assert.Equal(source.Name, known.TerrainName);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(101)]
    public void InvalidAttemptCountsAreRejected(int count) =>
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Source(attempts: count)]));

    [Fact]
    public void UnknownResourcesPoliciesAndCompatibilitySpoofingAreRejected()
    {
        var source = Source();
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Source(resource: "missing-resource")]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([source with { Production = new DirectionalResourceProduction("coal", 1, (ProductionPlacement)99) }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([Source(sourceLos: (ProductionSourceLos)99)]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([source with { LegacyProductionIdentity = LegacyTerrainProductionIdentity.LavaCoal }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([source with { Id = "lava" }]));
    }

    [Theory]
    [InlineData("""{"kind":"directionalResourceProduction","ResourceTypeId":"coal"}""")]
    [InlineData("""{"kind":"directionalResourceProduction","Attempts":1}""")]
    [InlineData("""{"kind":"directionalResourceProduction","ResourceTypeId":"coal","Attempts":1,"Trigger":"afterAttack"}""")]
    [InlineData("""{"kind":"arbitraryScript","ResourceTypeId":"coal","Attempts":1}""")]
    public void MalformedProductionJsonIsRejected(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EnvironmentalProduction>(json));
}
