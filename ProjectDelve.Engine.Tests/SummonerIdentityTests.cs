using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

internal static class SummonerFixture
{
    internal sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    internal sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    internal static GameState Ready(int? creatorLimit = 1, int? sideLimit = null,
        AbilitySourceKind source = AbilitySourceKind.Unit)
    {
        var ability = new LocalAbilityDefinition("summon", "Create Companion", AbilityTiming.Action,
            UnitAuthoring.AdjacentUnitSummon("child-type", Posture.Upright, sideLimit, creatorLimit));
        var actor = new UnitType("summoner-type", 2, 0, 0, 2, 1, Actions: UnitAction.None)
        {
            CanHaveEquipment = true, CanHaveItems = true,
            LocalAbilities = [new("reserve", "Reserve", AbilityTiming.BonusAction,
                new SelfModifiersMechanic([new(Stat.Def, 1)])), .. (source == AbilitySourceKind.Unit ? new[] { ability } : [])]
        };
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 10, Height = 5 },
            UnitTypeIds = [actor.Id, "child-type"],
            UnitTypes = [actor, new("child-type", 1, 0, 0, 1, 1, Actions: UnitAction.None)],
            EquipmentTypes = source == AbilitySourceKind.Equipment
                ? [new("wand", "Wand") { LocalAbilities = [ability] }] : [],
            ItemTypes = source == AbilitySourceKind.Item
                ? [new("scroll", "Scroll") { LocalAbilities = [ability] }] : [],
            Units = [new(actor.Id, "blue", new(1, 2), UnitId: "first")
                { Inventory = source switch
                    {
                        AbilitySourceKind.Equipment => [new("physical-source", "wand")],
                        AbilitySourceKind.Item => [new("physical-source") { ItemTypeId = "scroll" }],
                        _ => []
                    } }, new(actor.Id, "blue", new(7, 2), UnitId: "second")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new("child-type", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "first";
        state.ResolvingTokenUnitIds = ["first", "second"]; state.MoveDone = true;
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    internal static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    internal static Candidate Summon(GameState state) => state.Pending!.Candidates.First(c => c.Action == UnitAction.AdjacentUnitSummon);
    internal static bool CanSummon(GameState state, string actor = "first") =>
        SummoningRules.Candidates(state, state.Units.Single(u => u.Id == actor)).Any();
    internal static EngineResult Use(GameState state) => GameEngine.Advance(state, new Choice(Summon(state).Key), new Dice(), false);
}

public sealed class SummonerIdentityTests
{
    [Theory]
    [InlineData(AbilitySourceKind.Unit)]
    [InlineData(AbilitySourceKind.Equipment)]
    [InlineData(AbilitySourceKind.Item)]
    public void SharedSummonRecordsActingBearerBeforeEventsAndSurvivesReplay(AbilitySourceKind source)
    {
        var initial = SummonerFixture.Ready(source: source);
        var result = SummonerFixture.Use(initial);
        var replay = SummonerFixture.Use(SummonerFixture.Restore(initial));
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        var child = Assert.Single(result.State.Units, u => u.TypeId == "child-type");
        Assert.Equal("first", child.SummonerUnitId);
        Assert.Null(initial.Units[0].SummonerUnitId);
        Assert.Equal(child.SummonerUnitId, result.Events.Single(e => e.Kind == "UnitCreated").SourceUnitId);
        var creation = Assert.Single(result.ResolutionSteps, step => result.Events[step.EventIndex].Kind == "UnitCreated");
        Assert.Equal("first", creation.StateAfter.Units.Single(u => u.Id == child.Id).SummonerUnitId);
        var restored = SummonerFixture.Restore(result.State.Copy());
        Assert.Equal("first", restored.Units.Single(u => u.Id == child.Id).SummonerUnitId);
        GameEngine.ResolveDefeat(restored, "first");
        Assert.Equal("first", restored.Units.Single(u => u.Id == child.Id).SummonerUnitId);
        restored.Units.RemoveAll(u => u.Id == "first");
        GameEngine.ValidateScenario(restored);
        Assert.Equal(1, restored.Units.Single(u => u.Id == child.Id).CurrentHp);
    }

    [Fact]
    public void NestedSummonRecordsImmediateCreator()
    {
        var state = SummonerFixture.Use(SummonerFixture.Ready()).State;
        var child = state.Units.Single(u => u.TypeId == "child-type");
        state.Types.Add(new("grandchild-type", 1, 0, 0, 1, 1));
        var index = state.Types.FindIndex(t => t.Id == "child-type");
        state.Types[index] = state.Types[index] with { LocalAbilities = [new("nested", "Nested", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic("grandchild-type", Posture.Upright)), new("reserve", "Reserve", AbilityTiming.BonusAction,
                new SelfModifiersMechanic([new(Stat.Def, 1)]))] };
        state.CurrentUnitId = child.Id; state.ActiveToken = child.Token; state.ResolvingTokenUnitIds = [child.Id];
        state.ActionsUsedThisActivation = 0; state.MoveDone = true;
        state = GameEngine.RefreshChoices(state, new SummonerFixture.Dice(), false).State;
        var result = SummonerFixture.Use(state).State;
        Assert.Equal(child.Id, result.Units.Single(u => u.TypeId == "grandchild-type").SummonerUnitId);
        Assert.Equal("first", result.Units.Single(u => u.Id == child.Id).SummonerUnitId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("first")]
    public void ValidationRejectsMalformedOrSelfProvenance(string creator)
    {
        var state = SummonerFixture.Ready();
        state.Units[0] = state.Units[0] with { SummonerUnitId = creator };
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<Unit>(JsonSerializer.Serialize(state.Units[0])));
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(SummonerFixture.Restore(state)));
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("child-type", "child", "blue", new(4, 4), summonerUnitId: " "));
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("child-type", "child", "blue", new(4, 4), summonerUnitId: "child"));
    }

    [Fact]
    public void OldJsonDefaultsToNullAndUnresolvedCreatorReservesIdentity()
    {
        var old = JsonSerializer.Deserialize<Unit>("{\"Id\":\"old\",\"TypeId\":\"child-type\",\"SideId\":\"blue\",\"CurrentHp\":1}")!;
        Assert.Null(old.SummonerUnitId);
        var state = SummonerFixture.Ready();
        state.Units[1] = state.Units[1] with { SummonerUnitId = "child-1" };
        GameEngine.ValidateScenario(SummonerFixture.Restore(state));
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("child-type", "child-1", "blue", new(4, 4)));
        var result = SummonerFixture.Use(state).State;
        Assert.Equal("child-2", result.Units.Single(u => u.TypeId == "child-type").Id);
    }
}
