using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ClosedCornerGameplayTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
        public int RollD8() => 4;
    }
    private sealed class Pick(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static GameState World(UnitType actor, bool open, string targetSide = "red", UnitType? target = null)
    {
        target ??= UnitType.Hero("target-type", 0, 1, 0, 1, 8) with { Unique = true };
        return new() { Types = [actor, target],
            Units = [actor.CreateUnit("source", "blue"), target.CreateUnit("target", targetSide)],
            Controllers = [new(new(actor.Id, "blue"), ControllerKind.Human), new(new(target.Id, targetSide), ControllerKind.Human)],
            Physical = new(new Board(9, 9, [new(new(3, 3), new(3, 4), open ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor)]) {
                Terrain = [new(new(4, 3), TerrainKind.SolidRock)] }, [new("source", new(3, 3)), new("target", new(4, 4))]) };
    }
    private static EngineResult Choices(GameState state)
    {
        var unit = state.Units.Single(u => u.Id == "source");
        state.Round = 1; state.CurrentUnitId = unit.Id; state.ActiveToken = new(unit.TypeId, unit.SideId); state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, unit.TypeId, unit.Id, [], false) { Token = state.ActiveToken };
        return GameEngine.RefreshChoices(state, new Dice(), false);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NormalAttackTelekinesisFireballCastingRecipientsAndAttackPositionQueriesShareClosure(bool open)
    {
        var state = World(UnitType.Wizard(), open);
        var ready = Choices(state);
        var candidates = ready.NextInput!.Candidates;
        Assert.Equal(open, candidates.Any(c => c.Key == "attack:target"));
        Assert.Equal(open, candidates.Any(c => c.Key == "telekinesis:target"));
        Assert.Equal(open, candidates.Any(c => c.Key == "fireball:4,4"));
        Assert.Equal(open, candidates.Single(c => c.Key == "fireball:3,3").TargetIds.Contains("target"));
        var queries = new GameplayQueries(ready.State);
        Assert.Equal(open, queries.CanAttackHostileFrom("source", new(3, 3)));
        var distance = queries.DistanceToAttackPositionFrom("source", new(3, 3));
        Assert.NotNull(distance);
        Assert.Equal(open, distance == 0);
        var cast = GameEngine.Advance(ready.State, new Pick("fireball:3,3"), new Dice(), false);
        var started = Assert.Single(cast.Events, e => e.Kind == "AttackStarted");
        Assert.Equal(open, started.AttackContext!.Targets.Any(t => t.TargetId == "target"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HealAndContinuousAuraUseTheSameMixedCorner(bool open)
    {
        var state = World(UnitType.Cleric(), open, targetSide: "blue");
        state.Units[1] = state.Units[1] with { CurrentHp = 3 };
        Assert.Equal(1 + (open ? 1 : 0), state.EffectiveDefOf("target"));
        var ready = Choices(state);
        Assert.Equal(open, ready.NextInput!.Candidates.Any(c => c.Key == "heal:target"));
        if (open)
        {
            var healed = GameEngine.Advance(ready.State, new Pick("heal:target"), new Dice(), false);
            Assert.Equal(5, healed.State.Units.Single(u => u.Id == "target").CurrentHp);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HolyWaveRecipientPreviewAndResolutionRespectClosure(bool open)
    {
        var ready = Choices(World(UnitType.Cleric(), open));
        Assert.Equal(open, ready.NextInput!.Candidates.Single(c => c.Key == "holy-wave").TargetIds.Contains("target"));
        var result = GameEngine.Advance(ready.State, new Pick("holy-wave"), new Dice(), false);
        Assert.Equal(open ? Posture.Lying : Posture.Upright, result.State.Physical.Figures.Single(f => f.Id == "target").Posture);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SummoningPlacementIsUnavailableThroughAClosedMixedCorner(bool open)
    {
        var state = World(UnitType.Shaman(), open);
        state.Types.Add(UnitType.Goblin());
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(7, 7) };
        var ready = Choices(state);
        Assert.Equal(open, ready.NextInput!.Candidates.Any(c => c.Key == "spawn-goblin:4,4"));
        if (open)
        {
            var result = GameEngine.Advance(ready.State, new Pick("spawn-goblin:4,4"), new Dice(), false);
            Assert.Contains(result.State.Physical.Figures, f => f.Position == new Cell(4, 4) && f.Posture == Posture.Lying);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GivingResourcesUsesTheSameAdjacencyClosure(bool open)
    {
        var courier = new UnitType("courier", 3, 1, 0, 0, 1, UnitAction.GiveResource);
        var store = UnitType.Hero("store", 0, 1, 0, 1, 8) with { Unique = true, ResourceStorage = new([new(ResourceTypes.Coal, 3)]) };
        var state = World(courier, open, "blue", store);
        state.Resources.Add(new(ResourceTypes.Coal, new(3, 3)));
        var ready = Choices(state);
        Assert.Equal(open, ready.NextInput?.Candidates.Any(c => c.Key == "give-resource:target") == true);
        if (open)
        {
            var result = GameEngine.Advance(ready.State, new Pick("give-resource:target"), new Dice(), false);
            Assert.Equal(1, result.State.StoredQuantity("blue", "store", "coal"));
            Assert.Empty(result.State.Resources);
        }
    }

    [Theory]
    [InlineData(TerrainKind.Lava, false)] [InlineData(TerrainKind.Lava, true)]
    [InlineData(TerrainKind.FruitTree, false)] [InlineData(TerrainKind.FruitTree, true)]
    public void RoundStartResourceProductionPreservesSourceExemptionAndChecksCornerClosure(TerrainKind source, bool open)
    {
        var definition = new ScenarioDefinition {
            Board = new() { Width = 9, Height = 9, Cells = [new(new(3, 3), source), new(new(4, 3), TerrainKind.SolidRock)],
                Edges = [new(new(3, 3), EdgeDirection.Down, open ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor)] },
            UnitTypeIds = [UnitTypeIds.Grunt],
            Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0)), new(UnitTypeIds.Grunt, "red", new(8, 8))],
            Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)] };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        Assert.Equal(2, result.Events.Count(e => e.Kind == (source == TerrainKind.Lava ? "LavaCoalRolled" : "FruitTreeRolled")));
        Assert.Equal(open, result.State.Resources.Any(r => r.Position == new Cell(4, 4) &&
            r.Type == (source == TerrainKind.Lava ? ResourceTypes.Coal : ResourceTypes.Fruit)));
    }
}
