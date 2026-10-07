using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioDefinitionJsonTests
{
    [Theory]
    [InlineData("full-party-trolls")]
    [InlineData("shaman-hunt")]
    public void AuthoredAndJsonLoadedScenariosProduceEquivalentRoundZeroStates(string id)
    {
        var authoredDefinition = PlaytestScenarios.Definition(id);
        var authoredState = GameEngine.CreateGame(authoredDefinition);
        var json = ScenarioDefinitionJson.ToJson(authoredDefinition);
        var loadedDefinition = ScenarioDefinitionJson.FromJson(json);
        var loadedState = GameEngine.CreateGame(loadedDefinition);

        Assert.Equal(authoredDefinition.UnitTypeIds, loadedDefinition.UnitTypeIds);
        Assert.Equal(authoredDefinition.Board.DefaultTerrain, loadedDefinition.Board.DefaultTerrain);
        Assert.Equal(authoredDefinition.Board.Cells, loadedDefinition.Board.Cells);
        Assert.Equal(authoredDefinition.Board.Edges, loadedDefinition.Board.Edges);
        Assert.Equal(authoredDefinition.Units, loadedDefinition.Units);
        Assert.Equal(authoredDefinition.Agency, loadedDefinition.Agency);

        AssertEquivalentRoundZeroStates(authoredState, loadedState);
    }

    internal static void AssertEquivalentRoundZeroStates(GameState authoredState, GameState loadedState)
    {
        var expectedBoard = authoredState.Physical.Board;
        var actualBoard = loadedState.Physical.Board;
        Assert.Equal((expectedBoard.Width, expectedBoard.Height), (actualBoard.Width, actualBoard.Height));
        Assert.Equal(expectedBoard.Terrain, actualBoard.Terrain);
        Assert.Equal(expectedBoard.Edges, actualBoard.Edges);
        for (var y = 0; y < expectedBoard.Height; y++)
        for (var x = 0; x < expectedBoard.Width; x++)
        {
            var cell = new Cell(x, y);
            Assert.Equal(expectedBoard.TerrainAt(cell), actualBoard.TerrainAt(cell));
            if (x + 1 < expectedBoard.Width)
                Assert.Equal(expectedBoard.EdgeBetween(cell, new(x + 1, y)), actualBoard.EdgeBetween(cell, new(x + 1, y)));
            if (y + 1 < expectedBoard.Height)
                Assert.Equal(expectedBoard.EdgeBetween(cell, new(x, y + 1)), actualBoard.EdgeBetween(cell, new(x, y + 1)));
        }

        Assert.Equal(authoredState.Types.Select(t => t.Id), loadedState.Types.Select(t => t.Id));
        Assert.Equal(authoredState.Types.Count, loadedState.Types.Count);
        foreach (var (expected, actual) in authoredState.Types.Zip(loadedState.Types))
        {
            // Record fields compare by value; ImmutableArray needs explicit sequence comparison.
            Assert.Equal(expected with { BonusActions = actual.BonusActions }, actual);
            Assert.Equal(expected.BonusActions.Length, actual.BonusActions.Length);
            foreach (var (expectedAbility, actualAbility) in expected.BonusActions.Zip(actual.BonusActions))
            {
                Assert.Equal(expectedAbility with { Modifiers = actualAbility.Modifiers }, actualAbility);
                Assert.Equal(expectedAbility.Modifiers.ToArray(), actualAbility.Modifiers.ToArray());
            }
        }
        Assert.Equal(authoredState.Units.Count, loadedState.Units.Count);
        foreach (var (expected, actual) in authoredState.Units.Zip(loadedState.Units))
        {
            // Includes deterministic ID, type identity, Side, HP and every named use counter.
            Assert.Equal(expected with { BonusActionUses = actual.BonusActionUses }, actual);
            Assert.Equal(expected.BonusActionUses.OrderBy(p => p.Key), actual.BonusActionUses.OrderBy(p => p.Key));
            Assert.Equal(FootprintGeometry.OccupiedCells(authoredState, expected.Id),
                FootprintGeometry.OccupiedCells(loadedState, actual.Id));
            Assert.Equal((authoredState.EffectiveAtkOf(expected.Id), authoredState.EffectiveMovOf(expected.Id),
                    authoredState.EffectiveRngOf(expected.Id), authoredState.EffectiveDefOf(expected.Id)),
                (loadedState.EffectiveAtkOf(actual.Id), loadedState.EffectiveMovOf(actual.Id),
                    loadedState.EffectiveRngOf(actual.Id), loadedState.EffectiveDefOf(actual.Id)));
        }
        Assert.Equal(authoredState.Physical.Figures, loadedState.Physical.Figures);
        Assert.Equal(authoredState.Controllers, loadedState.Controllers);
        Assert.Equal(authoredState.WorldEffects, loadedState.WorldEffects);
        Assert.Equal(authoredState.WorldDeck?.DrawPile, loadedState.WorldDeck?.DrawPile);
        Assert.Equal(authoredState.WorldDeck?.DiscardPile, loadedState.WorldDeck?.DiscardPile);
        Assert.Equal(authoredState.WorldDeck?.ActiveContinuous, loadedState.WorldDeck?.ActiveContinuous);
        AssertFreshRoundZero(authoredState);
        AssertFreshRoundZero(loadedState);
    }

    private static void AssertFreshRoundZero(GameState state)
    {
        Assert.Equal(0, state.Round);
        Assert.False(state.RoundComplete);
        Assert.Empty(state.Bag);
        Assert.Null(state.ActiveToken);
        Assert.Null(state.CurrentUnitId);
        Assert.Null(state.Pending);
        Assert.Null(state.AttackInProgress);
        Assert.Null(state.DoorInProgress);
        Assert.Null(state.MoveAfterAttackAllowance);
        Assert.False(state.CleavePending);
        Assert.False(state.MoveDone);
        Assert.False(state.ActionDone);
        Assert.Empty(state.BonusActionsUsedThisActivation);
        Assert.Empty(state.ModifiersThisTurn);
        Assert.Empty(state.CompletedUnitIds);
    }
}
