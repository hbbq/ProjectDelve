using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed partial class DelveChessTests
{
    private static GameState Queen(int mov = 16) => State(UnitRoster.RoyalQueen() with { Mov = mov }, new(6, 5));
    private static GameState King(int mov = 2) => State(UnitRoster.RoyalKing() with { Mov = mov }, new(6, 5));

    [Theory]
    [InlineData(-1, -1)] [InlineData(-1, 0)] [InlineData(-1, 1)] [InlineData(0, -1)]
    [InlineData(0, 1)] [InlineData(1, -1)] [InlineData(1, 0)] [InlineData(1, 1)]
    public void QueenAndKingDirectionsCostsAndMovThresholds(int dx, int dy)
    {
        var cost = Math.Abs(dx) + Math.Abs(dy);
        var adjacent = new Cell(3 + dx, 3 + dy);
        var distant = new Cell(3 + 2 * dx, 3 + 2 * dy);
        Assert.Equal(cost, MovementRules.Cost(Paths(Queen())[adjacent]));
        Assert.Equal(2 * cost, MovementRules.Cost(Paths(Queen())[distant]));
        Assert.DoesNotContain(adjacent, Paths(Queen(cost - 1)).Keys);
        Assert.Contains(adjacent, Paths(Queen(cost)).Keys);
        Assert.Equal(cost, MovementRules.Cost(Paths(King())[adjacent]));
        Assert.DoesNotContain(adjacent, Paths(King(cost - 1)).Keys);
        Assert.DoesNotContain(distant, Paths(King(16)).Keys);
        Assert.Equal(0, MovementRules.Cost(Paths(King())[new(3, 3)]));
        Assert.Single(Paths(King(0)));
        var restricted = King(0);
        restricted.ModifiersThisTurn.Add(new(Stat.Mov, cost));
        Assert.Contains(adjacent, Paths(restricted).Keys);
    }

    [Fact]
    public void QueenNeverTurnsOrMixesModesEvenWhenAllowanceIsLarge()
    {
        var state = Queen();
        Assert.DoesNotContain(new Cell(5, 4), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(4, 6), Paths(state).Keys);
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.Wall));
        Assert.DoesNotContain(new Cell(5, 3), Paths(state).Keys);
        Assert.Contains(new Cell(5, 3), MovementRules.FindPaths(state, "m", new(3, 3), 16, ignoreMovementPattern: true).Keys);
    }

    [Theory]
    [InlineData("a", true)] [InlineData("b", false)]
    public void QueenIntermediateDiagonalUnitsAndCardinalPeelsRetainPassageRules(string side, bool legal)
    {
        var state = Queen();
        state.Units.Add(new("transit", "target", side, 1));
        state.Physical.Figures.Add(new("transit", new(4, 4)));
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
        Assert.Equal(legal, Paths(state).ContainsKey(new(5, 5)));
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(3, 4));
        Assert.Contains(new Cell(3, 4), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(3, 5), Paths(state).Keys);
    }

    [Theory]
    [InlineData(-1, -1)] [InlineData(-1, 0)] [InlineData(-1, 1)] [InlineData(0, -1)]
    [InlineData(0, 1)] [InlineData(1, -1)] [InlineData(1, 0)] [InlineData(1, 1)]
    public void KingAuraCoversEveryAdjacentFriendlyAndStacksWithEffectiveModifiers(int dx, int dy)
    {
        var state = State(UnitRoster.RoyalKing(), new(3 + dx, 3 + dy));
        state.Units[1] = state.Units[1] with { SideId = "a" };
        Assert.Equal(1, state.EffectiveDefOf("t"));
        state.CurrentUnitId = "t";
        state.ModifiersThisTurn.Add(new(Stat.Def, 2));
        Assert.Equal(3, state.EffectiveDefOf("t"));
        Assert.Equal(3, state.EffectiveDefOf("m"));
    }

    [Theory]
    [InlineData(false, EdgeKind.Wall, false)] [InlineData(true, EdgeKind.Wall, false)]
    [InlineData(false, EdgeKind.WallWithWindow, false)] [InlineData(true, EdgeKind.WallWithWindow, false)]
    [InlineData(false, EdgeKind.ClosedDoor, false)] [InlineData(true, EdgeKind.ClosedDoor, false)]
    [InlineData(false, EdgeKind.OpenDoor, true)] [InlineData(true, EdgeKind.OpenDoor, true)]
    public void Phase3BothCornerPathsRespectEdgesAndCannotDetour(bool king, EdgeKind edge, bool legal)
    {
        var state = king ? King(16) : Queen();
        state.Physical.Board.Edges.Add(new(new(4, 3), new(4, 4), edge));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(3, 4), edge));
        Assert.Equal(legal, Paths(state).ContainsKey(new(4, 4)));
        if (king) Assert.DoesNotContain(new Cell(5, 3), Paths(state).Keys);
        else Assert.Equal(legal, Paths(state).ContainsKey(new(3, 5)));
    }

    [Theory]
    [InlineData(false, true)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(true, false)]
    public void Phase3EitherCornerUsesTheExistingCanonicalOrdering(bool king, bool horizontalBlocked)
    {
        var state = king ? King() : Queen();
        var blocked = horizontalBlocked ? new Cell(4, 3) : new Cell(3, 4);
        var free = horizontalBlocked ? new Cell(3, 4) : new Cell(4, 3);
        state.Physical.Board.Terrain.Add(new(blocked, TerrainKind.Water));
        Assert.Equal(free, Paths(state)[new(4, 4)][1]);
        state.Physical.Board.Terrain.Add(new(free, TerrainKind.Tree));
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
    }

    [Theory]
    [InlineData(false, "a", true)] [InlineData(false, "b", false)]
    [InlineData(true, "a", true)] [InlineData(true, "b", false)]
    public void Phase3FriendlyCornerTransitAndHostileBlocking(bool king, string side, bool legal)
    {
        var state = king ? King() : Queen();
        foreach (var c in new Cell[] { new(4, 3), new(3, 4) })
        {
            var id = $"blocker-{c}";
            state.Units.Add(new(id, "target", side, 1));
            state.Physical.Figures.Add(new(id, c));
            Assert.DoesNotContain(c, Paths(state).Keys);
        }
        Assert.Equal(legal, Paths(state).ContainsKey(new(4, 4)));
        Assert.Equal(legal && !king, Paths(state).ContainsKey(new(3, 5)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Phase3PeelTransitAndAtomicArrival(bool king)
    {
        var state = king ? King() : Queen();
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 3));
        Assert.Equal(new Cell(3, 4), Paths(state)[new(4, 4)][1]);
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(3, 4));
        Assert.DoesNotContain(new Cell(4, 4), Paths(state).Keys);
        state = king ? King() : Queen();
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 4));
        if (!king) Assert.DoesNotContain(new Cell(5, 5), Paths(state).Keys);
        var moved = TestGame.Advance(Choices(state).State, new Choice("4,4"), new Dice(), false);
        var arrival = Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(3, arrival.Path!.Count);
        Assert.Equal(Posture.Lying, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        Assert.Empty(moved.State.CellMarkers);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Phase3BoostCreatesAnotherIndependentOrdinaryMove(bool king)
    {
        var type = UnitType.Define("boost", "Boost", UnitAuthoring.Stats(king ? 2 : 16, 1, 3, 4, 1),
            king ? UnitAuthoring.Adjacent() : UnitAuthoring.StraightOrthogonalOrDiagonalCorridor(),
            UnitAuthoring.ResourceStorage(ResourceTypes.Mushroom, 1), UnitAuthoring.Boost(1));
        var state = State(type, new(6, 5));
        state.StoredResources.Add(new("a", type.Id, ResourceTypes.Mushroom, 1));
        var moved = TestGame.Advance(Choices(state).State, new Choice("4,4"), new Dice(), false);
        var boost = moved.NextInput!.Candidates.Single(c => c.Ability?.Definition.Mechanic is Boost);
        var boosted = TestGame.Advance(moved.State, new Choice(boost.Key), new Dice(), false);
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Destination == new Cell(5, 3));
        Assert.DoesNotContain(boosted.NextInput.Candidates, c => c.Destination == new Cell(6, 3));
        var completed = TestGame.Advance(boosted.State, new Choice("5,3"), new Dice(), false);
        Assert.Equal(new Cell(5, 3), completed.State.Physical.Figures.Single(f => f.Id == "m").Position);
    }

    [Fact]
    public void QueenAdvanceIgnoresOrdinaryPatternAndMovButKingRetainsNormalMelee()
    {
        var state = State(UnitRoster.RoyalQueen() with { Mov = 0 }, new(4, 4));
        var result = Attack(state);
        Assert.True(result.NextInput!.IsMoveAfterAttack);
        Assert.Contains(result.NextInput.Candidates, c => c.Destination == new Cell(4, 4));
        var moved = TestGame.Advance(result.State, new Choice("4,4"), new Dice(), false);
        Assert.Equal(3, moved.Events.Single(e => e.Kind == "MovementCompleted").Path!.Count);
        state = State(UnitRoster.RoyalKing(), new(4, 4));
        state.MoveDone = true;
        Assert.Contains(Choices(state).NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack && c.TargetId == "t");
        Assert.Null(Attack(state).State.AdvanceOnKillCells);
    }

    [Theory]
    [InlineData(UnitTypeIds.RoyalQueen, false)] [InlineData(UnitTypeIds.WitchQueen, false)]
    [InlineData(UnitTypeIds.RoyalKing, true)] [InlineData(UnitTypeIds.GoblinKing, true)]
    public void Phase3DefinitionsTransportChoicesExecutionAndBehaviorAgree(string id, bool king)
    {
        var type = CanonicalUnitTypes.Find(id)!;
        Assert.Equal(king ? (2, 1, 2, 3, 5) : (16, 1, 4, 5, 3), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.True(type.Unique);
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Equal(!king, type.Mechanic<AdvanceOnKill>() is not null);
        Assert.Equal(king, AbilityQueries.NativeAbilities(type, "piece").Any(g => g.Definition.Mechanic is AdjacentModifiersMechanic));
        if (king) Assert.Equal(
            JsonSerializer.Serialize(Assert.Single(AbilityQueries.NativeAbilities(UnitRoster.Cleric(), "cleric"), ability => ability.Key == "aura").Definition),
            JsonSerializer.Serialize(Assert.Single(AbilityQueries.NativeAbilities(type, "king")).Definition));
        var counterpart = king ? UnitRoster.GoblinKing(id) : UnitRoster.WitchQueen(id);
        Assert.Equal(JsonSerializer.Serialize(type with { DisplayName = "pair" }), JsonSerializer.Serialize(counterpart with { DisplayName = "pair" }));
        var definition = Scenario.Define(Scenario.Map(7, 7), [
            Scenario.Group(id, "arbitrary", ControllerKind.Human, Scenario.At(3, 3)),
            Scenario.Group(id, "another", ControllerKind.Human, Scenario.At(6, 5))]);
        var loaded = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        Assert.Equal(type.MovementPattern, loaded.Types.Single().MovementPattern);
        Assert.Throws<ArgumentException>(() => loaded.PlaceUnit(id, "duplicate", "arbitrary", new(0, 0)));
        var state = State(type, new(6, 5));
        var pending = Choices(state);
        var expected = Paths(state);
        Assert.Equal(expected.Count - 1, pending.NextInput!.Candidates.Count(c => c.Kind == ActivationChoiceKind.Move));
        var key = new DefaultAutomatedProvider().Choose(pending.NextInput, new GameplayQueries(pending.State));
        Assert.Contains(pending.NextInput.Candidates, c => c.Key == key);
        foreach (var candidate in pending.NextInput.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move))
        {
            var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(pending.State))!;
            var moved = TestGame.Advance(saved, new Choice(candidate.Key), new Dice(), false);
            Assert.Equal(expected[candidate.Destination!], Assert.Single(moved.Events, e => e.Kind == "MovementCompleted").Path);
            Assert.Equal(candidate.Destination, moved.State.Physical.Figures.Single(f => f.Id == "m").Position);
        }
    }

    [Theory]
    [InlineData(UnitTypeIds.RoyalKing)] [InlineData(UnitTypeIds.GoblinKing)]
    public void KingAuraReusesClericAdjacencyStackingAndActiveConditions(string id)
    {
        var state = State(CanonicalUnitTypes.Find(id), new(4, 4));
        state.Units[1] = state.Units[1] with { SideId = "a" };
        Assert.Equal(1, state.EffectiveDefOf("t"));
        Assert.Equal(3, state.EffectiveDefOf("m"));
        state.Types.Add(UnitRoster.Cleric());
        state.Units.Add(UnitRoster.Cleric().CreateUnit("cleric", "a"));
        state.Physical.Figures.Add(new("cleric", new(4, 3)));
        Assert.Equal(2, state.EffectiveDefOf("t"));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(1, state.EffectiveDefOf("t"));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Upright };
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(3, 4), EdgeKind.Wall));
        Assert.Equal(1, state.EffectiveDefOf("t"));
        state.Physical.Board.Edges.Clear();
        state.Units[0] = state.Units[0] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "m");
        state.DefeatedUnitIds.Add("m");
        Assert.Equal(1, state.EffectiveDefOf("t"));
        state.Units[1] = state.Units[1] with { SideId = "b" };
        Assert.Equal(0, state.EffectiveDefOf("t"));
    }
}
