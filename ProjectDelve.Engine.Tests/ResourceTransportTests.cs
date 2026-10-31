using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ResourceTransportTests
{
    private sealed class Dice(bool hit = false) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => hit ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState World(UnitType? actor = null, Cell? enemy = null)
    {
        actor ??= UnitRoster.Zombie() with { Mov = 4 };
        var target = UnitRoster.Zombie("target");
        return new()
        {
            Physical = new(new Board(8, 4, []), [new("actor", new(1, 1)), new("enemy", enemy ?? new(6, 1))]),
            Types = [actor, target], Units = [actor.CreateUnit("actor", "red"), target.CreateUnit("enemy", "blue")]
        };
    }
    private static EngineResult Start(GameState state) => TestGame.StartRound(state, new Dice(), false);
    private static EngineResult Pick(EngineResult result, string? key, bool hit = false) => TestGame.Advance(result.State, new Choice(key), new Dice(hit), false);

    [Fact]
    public void OwnMoveTransportsAtomicallyAndCopiesAreDetached()
    {
        var world = World();
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        var start = Start(world);
        var moved = Pick(start, "4,1");
        Assert.Equal(new(4, 1), moved.State.Resources.Single().Position);
        Assert.Equal(new(1, 1), start.State.Resources.Single().Position);
        Assert.Equal(new(1, 1), world.Resources.Single().Position);
        var step = Assert.Single(moved.ResolutionSteps.Where(s => moved.Events[s.EventIndex].Kind == "MovementCompleted"));
        Assert.Equal(new(4, 1), step.StateAfter.Resources.Single().Position);
        Assert.Equal(new(4, 1), step.StateAfter.Physical.Figures.Single(f => f.Id == "actor").Position);
        moved.State.Resources.Clear();
        Assert.Single(step.StateAfter.Resources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccupiedResourceDestinationDoesNotBlockMoveOrDisplaceEitherType(bool otherType)
    {
        var world = World();
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        world.PlaceResource(otherType ? new ResourceType("test-type", "Test Type") : ResourceTypes.Coal, new(4, 1));
        var moved = Pick(Start(world), "4,1");
        Assert.Equal(world.Resources, moved.State.Resources);
        Assert.Equal(new(4, 1), moved.State.Physical.Figures[0].Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntermediateResourcesAreUnaffectedWhetherOrNotMoveStartsOnOne(bool atStart)
    {
        var world = World();
        if (atStart) world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        world.PlaceResource(ResourceTypes.Coal, new(2, 1));
        var start = Start(world);
        var move = start.NextInput!.Candidates.Single(c => c.Key == "4,1");
        Assert.Contains(new Cell(2, 1), move.Path!);
        var moved = Pick(start, move.Key);
        Assert.Contains(moved.State.Resources, r => r.Position == new Cell(2, 1));
        Assert.Equal(atStart, moved.State.Resources.Any(r => r.Position == new Cell(4, 1)));
    }

    [Fact]
    public void StayAndUnitsWithoutCapabilityLeaveResourcesInPlace()
    {
        var world = World();
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        Assert.Equal(world.Resources, Pick(Start(world), "stay").State.Resources);
        world.Types[0] = world.Types[0] .Without<TransportResource>();
        Assert.Equal(world.Resources, Pick(Start(world), "4,1").State.Resources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveAfterAttackTransportsAndZeroStepStayDoesNot(bool stay)
    {
        var world = World(UnitRoster.Goblin() .WithLocal(new("transport-resource", "Transport Resource", AbilityTiming.Passive, new TransportResource())), new(2, 1));
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        var extra = Pick(Pick(Start(world), "stay"), "attack:enemy");
        Assert.True(extra.NextInput!.IsMoveAfterAttack);
        var moved = Pick(extra, stay ? null : "1,2");
        Assert.Equal(stay ? new(1, 1) : new Cell(1, 2), moved.State.Resources.Single().Position);
    }

    [Fact]
    public void MoveAfterAttackIntoResourceCellLeavesBothResourcesInPlace()
    {
        var world = World(UnitRoster.Goblin() .WithLocal(new("transport-resource", "Transport Resource", AbilityTiming.Passive, new TransportResource())), new(2, 1));
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        world.PlaceResource(new("test", "Test"), new(1, 2));
        var extra = Pick(Pick(Start(world), "stay"), "attack:enemy");
        var moved = Pick(extra, "1,2");
        Assert.Equal(world.Resources, moved.State.Resources);
        Assert.Equal(new(1, 2), moved.State.Physical.Figures.Single(f => f.Id == "actor").Position);
    }

    [Fact]
    public void DefeatRemovesFigureAndLeavesItsResourceOnTheBoard()
    {
        var world = World(enemy: new(2, 1));
        world.PlaceResource(ResourceTypes.Coal, new(2, 1));
        var result = Pick(Pick(Start(world), "stay"), "attack:enemy", hit: true);
        Assert.DoesNotContain(result.State.Physical.Figures, figure => figure.Id == "enemy");
        Assert.Equal(world.Resources, result.State.Resources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalDisplaceAndSwapNeverTransportEvenWhenBothUnitsHaveCapability(bool swap)
    {
        var world = World(UnitRoster.DisplacerDemon() .WithLocal(new("transport-resource", "Transport Resource", AbilityTiming.Passive, new TransportResource())), new(2, 1));
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        world.PlaceResource(ResourceTypes.Coal, new(2, 1));
        var start = Start(world);
        var candidate = start.NextInput!.Candidates.First(c => swap ? c.Ability?.Definition.Mechanic is Swap :
            c.Ability?.Definition.Mechanic is Displace && c.Destination == new Cell(2, 2));
        var moved = Pick(start, candidate.Key);
        Assert.Equal(world.Resources, moved.State.Resources);
        Assert.Equal(swap ? new(1, 1) : new Cell(2, 2), moved.State.Physical.Figures.Single(f => f.Id == "enemy").Position);
        Assert.DoesNotContain(moved.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void ResourcesDoNotAffectLegalChoicesLineOfSightOrBehavior()
    {
        var world = World();
        var original = Start(world);
        var before = new DefaultAutomatedProvider().Choose(original.NextInput!, new GameplayQueries(original.State));
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        world.PlaceResource(ResourceTypes.Coal, new(2, 1));
        world.PlaceResource(ResourceTypes.Coal, new(5, 1));
        var marked = Start(world);
        Assert.Equal(original.NextInput!.Candidates.Select(c => c.Key), marked.NextInput!.Candidates.Select(c => c.Key));
        Assert.Equal(before, new DefaultAutomatedProvider().Choose(marked.NextInput!, new GameplayQueries(marked.State)));
        Assert.Equal(new GameplayQueries(original.State).CanAttackHostileFrom("actor", new(5, 1)),
            new GameplayQueries(marked.State).CanAttackHostileFrom("actor", new(5, 1)));
        Assert.NotNull(UnitRoster.Zombie().Mechanic<TransportResource>());
        Assert.Equal(UnitBehavior.ApproachThroughClosedDoors, UnitRoster.Zombie().Behaviors);
        Assert.Contains(UnitRoster.Zombie().CardEntries(), e => e.Id == "ability:transport-resource");
    }

    [Fact]
    public void PlacementAndRawStateValidationRejectInvalidResourcesButAllowTerrainAndUnits()
    {
        var world = World();
        world.PlaceResource(ResourceTypes.Coal, new(1, 1));
        Assert.Throws<ArgumentException>(() => world.PlaceResource(new("other", "Other"), new(1, 1)));
        Assert.Throws<ArgumentException>(() => world.PlaceResource(ResourceTypes.Coal, new(-1, 0)));
        Assert.Throws<ArgumentException>(() => world.PlaceResource(new("", "Bad"), new(0, 0)));
        world.Physical.Board.Terrain.Add(new(new(0, 0), TerrainKind.Water));
        world.PlaceResource(ResourceTypes.Coal, new(0, 0));
        _ = Start(world);
        world.Resources.Add(new(ResourceTypes.Coal, new(1, 1)));
        Assert.Throws<ArgumentException>(() => Start(world));
        world.Resources.RemoveAt(2);
        world.Resources.Add(new(ResourceTypes.Coal, new(8, 1)));
        Assert.Throws<ArgumentException>(() => Start(world));
    }

    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 5, Height = 5 }, UnitTypeIds = [UnitTypeIds.Zombie],
        Units = [new(UnitTypeIds.Zombie, "red", new(1, 1))],
        Agency = [new(UnitTypeIds.Zombie, "red", ControllerKind.Human)],
        Resources = [new(ResourceTypes.Coal, new(1, 1)), new(new("test", "Test"), new(3, 3))]
    };

    [Fact]
    public void ScenarioJsonAndPortableTransportRoundTripMultipleTypesAndState()
    {
        var definition = Definition();
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(definition.Resources, decoded.Resources);
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(decoded)));
        Assert.Equal(definition.Resources, state.Resources);
        Assert.Equal(state.Resources, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!.Resources);
        state.Resources.Clear();
        Assert.Equal(2, GameEngine.CreateGame(definition).Resources.Count);
        Assert.Empty(GameEngine.CreateGame(definition with { Resources = null }).Resources);
    }

    [Fact]
    public void ScenarioRejectsOverlappingOutOfBoundsAndMalformedResources()
    {
        var definition = Definition();
        foreach (var resource in new Resource[] { new(ResourceTypes.Coal, new(1, 1)),
            new(ResourceTypes.Coal, new(5, 1)), new(null!, new(0, 0)), null! })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Resources = [.. definition.Resources!, resource] }));
    }
}
