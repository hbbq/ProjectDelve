using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PopulationPursuitTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.SideId == "red") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 1;
    }

    [Theory]
    [InlineData(UnitTypeIds.Goblin)]
    [InlineData(UnitTypeIds.Zombie)]
    [InlineData(UnitTypeIds.Troll)]
    public void LoneNormalSleeperWakesThenPursuesKnownReachableHeroBeyondWakeDistance(string type)
    {
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 40, Height = 3 }, UnitTypeIds = [type, UnitTypeIds.Barbarian],
            Units = [new(type, "red", new(0, 1), UnitId: "actor", GroupId: "lone", Dormant: true, WakeDistance: 6),
                new(UnitTypeIds.Barbarian, "blue", new(6, 1), UnitId: "hero")],
            Agency = [new(type, "red", ControllerKind.Automated, "lone"), new(UnitTypeIds.Barbarian, "blue", ControllerKind.Automated)],
            Orders = [new(type, "red", OrderKind.Normal, GroupId: "lone"), new(UnitTypeIds.Barbarian, "blue", OrderKind.DoNothing)],
            Objectives = [new("red", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var dice = new Dice(); var result = GameEngine.StartRound(GameEngine.CreateGame(scenario), dice, false);
        Assert.False(result.State.Units.Single(u => u.Id == "actor").Dormant);
        Assert.Contains(result.Events, e => e.Kind == "UnitWoken" && e.UnitId == "actor");
        // Arrange the already-awakened pursuit boundary after the scout has left.
        var index = result.State.Physical.Figures.FindIndex(f => f.Id == "hero");
        result.State.Physical.Figures[index] = result.State.Physical.Figures[index] with { Position = new(35, 1) };
        var provider = new OrderAwareAutomatedProvider();
        for (var step = 0; step < 150 && result.State.Round < 4; step++)
            result = result.State.RoundComplete ? GameEngine.StartRound(result.State, dice, false) : GameEngine.Advance(result.State, provider, dice, false);
        var actor = result.State.Physical.Figures.Single(f => f.Id == "actor");
        Assert.True(actor.Position.X >= 4); Assert.True(35 - actor.Position.X > 6);
        Assert.Equal(OrderKind.Normal, new GameplayQueries(result.State).EffectiveOrderFor("actor").Kind);

        // The same awake figure must not select an unknown Hero for a FoW Side.
        var hidden = result.State.Copy(); hidden.FogOfWarSides = ["red"];
        hidden.ExploredCells["red"] = Enumerable.Range(0, actor.Position.X + 6).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Cell(x, y))).ToHashSet();
        var moves = new DecisionRequest(DecisionKind.Move, type, "actor",
            MovementRules.FindPaths(hidden, "actor", actor.Position, hidden.Types.Single(t => t.Id == type).Mov)
                .Where(p => p.Value.Count > 1).Select(p => new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);
        Assert.Null(provider.Choose(moves, new GameplayQueries(hidden)));
    }
}
