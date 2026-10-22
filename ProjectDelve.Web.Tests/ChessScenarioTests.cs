using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ChessScenarioTests
{
    // Phase 3 explicitly requests verification of this introductory deployment.
    [Fact]
    public void ChessHasClassicalDeploymentAndOnlyOrdinaryCheckerboardContent()
    {
        var definition = PlaytestScenarios.Definition("chess");
        var state = GameEngine.CreateGame(definition);
        Assert.Equal((8, 8), (state.Physical.Board.Width, state.Physical.Board.Height));
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            Assert.Equal((x + y) % 2 == 0 ? TerrainKind.StoneFloor : TerrainKind.Asphalt,
                state.Physical.Board.TerrainAt(new(x, y)));
        Assert.Empty(state.Physical.Board.Edges);
        Assert.Empty(state.Physical.Board.Machines);
        Assert.Empty(state.CellMarkers);
        Assert.Empty(state.Resources);
        Assert.Null(state.WorldEffects);
        Assert.Empty(state.Orders);
        string[] blue = [UnitTypeIds.CastleGuard, UnitTypeIds.RoyalKnight, UnitTypeIds.RoyalBishop,
            UnitTypeIds.RoyalQueen, UnitTypeIds.RoyalKing, UnitTypeIds.RoyalBishop, UnitTypeIds.RoyalKnight, UnitTypeIds.CastleGuard];
        string[] red = [UnitTypeIds.TowerTroll, UnitTypeIds.HobbyKnight, UnitTypeIds.GoblinBishop,
            UnitTypeIds.WitchQueen, UnitTypeIds.GoblinKing, UnitTypeIds.GoblinBishop, UnitTypeIds.HobbyKnight, UnitTypeIds.TowerTroll];
        foreach (var (side, backRank, peasantRank, types, peasant, enemyKing, enemySide) in new[] {
            ("blue", 7, 6, blue, UnitTypeIds.RoyalPeasant, UnitTypeIds.GoblinKing, "red"),
            ("red", 0, 1, red, UnitTypeIds.GoblinPeasant, UnitTypeIds.RoyalKing, "blue") })
        {
            Assert.Equal(16, state.Units.Count(u => u.SideId == side));
            for (var x = 0; x < 8; x++)
            {
                Assert.Contains(definition.Units, u => u.SideId == side && u.Anchor == new Cell(x, backRank) && u.UnitTypeId == types[x]);
                Assert.Contains(definition.Units, u => u.SideId == side && u.Anchor == new Cell(x, peasantRank) && u.UnitTypeId == peasant);
            }
            Assert.Equal(6, definition.Agency.Count(a => a.SideId == side));
            var objective = Assert.Single(state.Objectives, o => o.SideId == side);
            Assert.Equal(ScenarioObjective.DefeatUnits, objective.Objective);
            Assert.Equal(UnitSelector.Group(enemyKing, enemySide), Assert.Single(objective.Selectors));
        }
    }

    [Theory]
    [InlineData(false, "blue")] [InlineData(false, "red")]
    [InlineData(true, "blue")] [InlineData(true, "red")]
    public void OnlyActualOpposingKingDefeatWins(bool king, string side)
    {
        var enemy = side == "blue" ? "red" : "blue";
        var ownKing = side == "blue" ? UnitTypeIds.RoyalKing : UnitTypeIds.GoblinKing;
        var enemyKing = side == "blue" ? UnitTypeIds.GoblinKing : UnitTypeIds.RoyalKing;
        var enemyQueen = side == "blue" ? UnitTypeIds.WitchQueen : UnitTypeIds.RoyalQueen;
        // A small test-owned combat fixture, independent of the authored Chess setup.
        var definition = Scenario.Define(Scenario.Map(5, 5), [
            Scenario.Group(UnitTypeIds.CastleGuard, side, ControllerKind.Human, Scenario.At(1, 1, unitId: "attacker")),
            Scenario.Group(ownKing, side, ControllerKind.Human, Scenario.At(0, 4)),
            Scenario.Group(enemyKing, enemy, ControllerKind.Human, Scenario.At(king ? 2 : 4, king ? 1 : 4, initialHp: 1, unitId: "king")),
            Scenario.Group(enemyQueen, enemy, ControllerKind.Human, Scenario.At(king ? 4 : 2, king ? 0 : 1, initialHp: 1, unitId: "queen"))]) with
        {
            Objectives = [new(side, ScenarioObjective.DefeatUnits) { Selectors = [UnitSelector.Group(enemyKing, enemy)] },
                new(enemy, ScenarioObjective.DefeatUnits) { Selectors = [UnitSelector.Group(ownKing, side)] }]
        };
        var dice = new Dice();
        var result = TestGame.StartRound(GameEngine.CreateGame(definition), dice, false);
        result = TestGame.Advance(result.State, new Choice("stay"), dice, false);
        result = TestGame.Advance(result.State, new Choice(king ? "attack:king" : "attack:queen"), dice, false);
        Assert.Contains(king ? "king" : "queen", result.State.DefeatedUnitIds);
        if (result.NextInput?.IsMoveAfterAttack == true)
            result = TestGame.Advance(result.State, new Choice(null), dice, false);
        Assert.Equal(king, result.State.IsCompleted);
        if (king) Assert.Equal(new[] { side }, result.State.Completion!.AchievedSideIds);
    }

    [Fact]
    public void EightPeasantsShareOneTokenAndActivateSequentially()
    {
        var definition = Scenario.Define(Scenario.Map(8, 3), [
            Scenario.Group(UnitTypeIds.RoyalPeasant, "any", ControllerKind.Human,
                [.. Enumerable.Range(0, 8).Select(x => Scenario.At(x, 0, unitId: $"p{x}"))])]) with
        { Objectives = [new("any", ScenarioObjective.NoObjective)] };
        var dice = new Dice();
        var result = TestGame.StartRound(GameEngine.CreateGame(definition), dice, false);
        var activated = new List<string>();
        while (!result.State.RoundComplete)
        {
            if (result.NextInput!.Kind == DecisionKind.SelectUnit)
                result = TestGame.Advance(result.State, new Choice(result.NextInput.Candidates[0].Key), dice, false);
            var id = result.State.CurrentUnitId!;
            result = TestGame.Advance(result.State, new Choice("stay"), dice, false);
            activated.Add(id);
        }
        Assert.Equal(8, activated.Distinct().Count());
        Assert.Empty(result.State.Bag);
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.CastleGuard) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
