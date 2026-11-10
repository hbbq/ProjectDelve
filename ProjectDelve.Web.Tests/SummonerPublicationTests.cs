using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class SummonerPublicationTests
{
    private static GameState State() => GameEngine.CreateGame(Scenario.Define(Scenario.Map(16, 2),
        [Scenario.Group(UnitTypeIds.Jackalope, "blue", ControllerKind.Human, Scenario.At(0, 0, unitId: "child")),
         Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(15, 0, unitId: "creator"))]) with
        { FogOfWarSides = ["blue"] });

    [Fact]
    public void DetachedSnapshotsPreserveProvenanceButObservationsCannotIdentifyHiddenCreator()
    {
        var state = State();
        state.Units[0] = state.Units[0] with { SummonerUnitId = "creator" };
        using var game = new PlaytestGame(new Dice(), state);
        var snapshot = game.Snapshot();
        Assert.Equal("creator", snapshot.Result.State.Units.Single(u => u.Id == "child").SummonerUnitId);
        var detached = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(snapshot.Result.State))!;
        Assert.Equal("creator", detached.Units.Single(u => u.Id == "child").SummonerUnitId);
        var browser = game.BrowserResponse(snapshot);
        Assert.Null(browser.Result.State.Units.Single(u => u.Id == "child").SummonerUnitId);
        var gambit = GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>());
        Assert.Null(gambit.Units.Single(u => u.Id == "child").SummonerUnitId);
        Assert.DoesNotContain("creator", JsonSerializer.Serialize(browser));
        Assert.DoesNotContain("creator", JsonSerializer.Serialize(gambit));
        Assert.Equal("creator", state.Units[0].SummonerUnitId);

        state.ExploredCells["blue"].Add(new(15, 0));
        var seen = FogOfWar.Observe(state, "blue");
        Assert.Equal("creator", seen.Units.Single(u => u.Id == "child").SummonerUnitId);
        Assert.Equal("creator", GambitProjection.Observation(state, "blue", new Dictionary<ActivationToken, string>())
            .Units.Single(u => u.Id == "child").SummonerUnitId);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
