using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class AlertPropagationTests(ITestOutputHelper output)
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken? First;
        public AttackFace Attack = AttackFace.Miss;
        public DefenceFace Defence = DefenceFace.Miss;
        public int Rolls;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            First is { } first && bag.Contains(first) ? first : bag[0];
        public AttackFace RollAttackDie() { Rolls++; return Attack; }
        public DefenceFace RollDefenceDie() { Rolls++; return Defence; }
        public int RollD6() => 6;
    }

    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static UnitPlacement Unit(string id, int x, int y = 1, bool dormant = true,
        int wake = 0, string side = "red", string type = "enemy", string? group = null) =>
        new(type, side, new(x, y), UnitId: id, GroupId: group, Dormant: dormant, WakeDistance: wake);

    private static ScenarioDefinition Fixture(params UnitPlacement[] units) => new()
    {
        Board = new() { Width = 40, Height = 12 },
        UnitTypeIds = ["hero", "enemy"],
        UnitTypes = [new("hero", 5, 6, 1, 0, 1), new("enemy", 1, 1, 1, 1, 1)],
        Units = [.. units],
        Agency = units.Select(u => new AgencyAssignment(u.UnitTypeId, u.SideId, ControllerKind.Human))
            .Distinct().ToList(),
        Objectives = units.Select(u => u.SideId).Distinct().Select(s => new SideObjective(s, ScenarioObjective.NoObjective)).ToList(),
        Loot = LootSettings.Empty
    };

    private static EngineResult Start(ScenarioDefinition scenario, Dice? dice = null) =>
        GameEngine.StartRound(GameEngine.CreateGame(scenario), dice ?? new(), false);
    private static EngineResult Step(EngineResult result, string key, Dice? dice = null) =>
        GameEngine.Advance(result.State, new Pick(key), dice ?? new(), false);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string[] Woken(EngineResult result) => result.Events.Where(e => e.Kind == "UnitWoken").Select(e => e.UnitId!).ToArray();
    private static bool Sleeping(GameState state, string id) => state.Units.Single(u => u.Id == id).Dormant;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProximityInitiatesFromAwakeOrDormantUnitAndCrossesMixedFriendlyChain(bool dormant)
    {
        var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
            Unit("root", 1, dormant: dormant, wake: 1), Unit("first", 4),
            Unit("awake-relay", 7, dormant: false), Unit("last", 10), Unit("beyond-gap", 14));
        scenario.Units[3] = scenario.Units[3] with { Posture = Posture.Lying };
        var result = Start(scenario);
        Assert.Equal(dormant ? new[] { "root", "first", "last" } : new[] { "first", "last" }, Woken(result));
        Assert.True(Sleeping(result.State, "beyond-gap"));
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "awake-relay").Posture);
        Assert.All(result.Events.Where(e => e.Kind == "UnitWoken"), e =>
        {
            Assert.Equal("proximity", e.ActionId);
            Assert.Null(e.SourceUnitId);
            Assert.Null(e.TargetId);
        });
        var reversed = Start(scenario with { Units = scenario.Units.AsEnumerable().Reverse().ToList() });
        Assert.Equal(Woken(result), Woken(reversed));
    }

    [Theory]
    [InlineData("green", false)]
    [InlineData("neutral", true)]
    public void NonFriendlyUnitsCannotRelayAlert(string bridgeSide, bool neutral)
    {
        var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
            Unit("root", 1, dormant: false, wake: 1),
            Unit("bridge", 4, dormant: false, side: bridgeSide), Unit("last", 7));
        if (neutral) scenario = scenario with { NeutralSides = [bridgeSide] };
        var result = Start(scenario);
        Assert.True(Sleeping(result.State, "last"));
        Assert.Empty(Woken(result));
    }

    [Fact]
    public void RawFootprintDistanceCrossesTerrainWallsAndClosedDoorsAndTerminatesCycles()
    {
        var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
            Unit("root", 1, dormant: false, wake: 1), Unit("large", 4),
            Unit("right", 8), Unit("bottom-right", 8, 4), Unit("bottom-left", 5, 4));
        scenario = scenario with
        {
            UnitTypes = [scenario.UnitTypes![0], scenario.UnitTypes[1] with { Footprint = Footprint.TwoByTwo }],
            // Only the relay is large; the other Units use a separate test-owned type.
            UnitTypeIds = ["hero", "enemy", "small"],
            Board = scenario.Board with
            {
                Cells = [new(new(3, 1), TerrainKind.Water), new(new(7, 1), TerrainKind.SolidRock)],
                Edges = [new(new(2, 1), EdgeDirection.Right, EdgeKind.Wall),
                    new(new(6, 1), EdgeDirection.Right, EdgeKind.ClosedDoor)]
            }
        };
        scenario.UnitTypes!.Add(new("small", 1, 1, 1, 1, 1));
        scenario = scenario with { Units = scenario.Units.Select(u => u.UnitTypeId == "enemy" && u.UnitId != "large"
            ? u with { UnitTypeId = "small" } : u).ToList() };
        scenario.Agency.Add(new("small", "red", ControllerKind.Human));
        var result = Start(scenario);
        // Anchor distance large->right is 4; occupied-cell distance is 3.
        Assert.Equal(new[] { "large", "right", "bottom-left", "bottom-right" }, Woken(result));
        Assert.Equal(4, Woken(result).Distinct().Count());
    }

    [Theory]
    [InlineData(false, AttackFace.Miss, DefenceFace.Miss, false)]
    [InlineData(true, AttackFace.Miss, DefenceFace.Miss, false)]
    [InlineData(false, AttackFace.Hit, DefenceFace.Block, false)]
    [InlineData(true, AttackFace.Hit, DefenceFace.Miss, true)]
    public void AttackedGoblinAlertsChainBeforeDiceIncludingMissBlockAndDefeat(
        bool dormant, AttackFace attack, DefenceFace defence, bool defeated)
    {
        var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
            Unit("goblin", 1, dormant: dormant), Unit("awake-relay", 4, dormant: false),
            Unit("last", 7), Unit("farthest", 10));
        scenario = scenario with { UnitTypes = [scenario.UnitTypes![0], UnitRoster.Goblin("enemy") with { Hp = 1, Def = 1 }] };
        var dice = new Dice { Attack = attack, Defence = defence, First = new("hero", "blue") };
        var result = Step(Start(scenario, dice), "stay", dice);
        Assert.True(Sleeping(result.State, "last"));
        var before = JsonSerializer.Serialize(result.State);
        Assert.Throws<ArgumentException>(() => Step(result, "attack:missing", dice));
        Assert.Equal(before, JsonSerializer.Serialize(result.State));
        Assert.Equal(0, dice.Rolls);
        result = Step(result, "attack:goblin", dice);
        Assert.Equal(dormant ? new[] { "goblin", "last", "farthest" } : new[] { "last", "farthest" }, Woken(result));
        Assert.Equal(0, dice.Rolls);
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
        Assert.All(result.Events.Where(e => e.Kind == "UnitWoken"), e => Assert.Equal("attack", e.ActionId));
        Assert.True(result.Events.FindLastIndex(e => e.Kind == "UnitWoken") < result.Events.FindIndex(e => e.Kind == "AttackStarted"));
        var restored = result with { State = Copy(result.State) };
        var replay = Step(restored, "roll-dice", new Dice { Attack = attack, Defence = defence });
        result = Step(result, "roll-dice", dice);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Step(result, "roll-dice", dice);
        Assert.Equal(defeated, result.State.DefeatedUnitIds.Contains("goblin"));
    }

    [Fact]
    public void HiddenAlertChainDoesNotRevealRecipientsOrProximitySource()
    {
        var scenario = Fixture(Unit("observer", 0, dormant: false, side: "blue", type: "hero"),
            Unit("source", 15, dormant: false, side: "blue", type: "hero"),
            Unit("root", 16, dormant: false, wake: 1), Unit("hidden", 19));
        scenario = scenario with { FogOfWarSides = ["blue"], Board = scenario.Board with
        {
            Edges = Enumerable.Range(0, 12).Select(y => new EdgeDefinition(new(17, y), EdgeDirection.Right, EdgeKind.Wall)).ToList()
        } };
        var result = Start(scenario);
        var wake = Assert.Single(result.Events.Where(e => e.Kind == "UnitWoken"));
        Assert.Equal("hidden", wake.UnitId);
        Assert.Null(wake.SourceUnitId);
        var snapshot = result.ResolutionSteps.Single(s => result.Events[s.EventIndex] == wake).StateAfter;
        Assert.Null(FogOfWar.ObserveEvent(snapshot, "blue", wake));
    }

    [Fact]
    public void AlertBeforeUndrawnTokenJoinsParticipantsWithoutAddingTokens()
    {
        var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
            Unit("root", 10, wake: 5, group: "camp"), Unit("relay", 13, group: "camp"),
            Unit("awake", 28, dormant: false, group: "camp"));
        var dice = new Dice { First = new("hero", "blue") };
        var result = Step(Start(scenario, dice), "5,1", dice);
        result = Step(result, "end-turn", dice);
        Assert.Equal(new ActivationToken("enemy", "red", "camp"), result.State.ActiveToken);
        Assert.Equal(new[] { "awake", "relay", "root" }, result.State.ResolvingTokenUnitIds.Order());
        Assert.Equal(new[] { "root", "relay" }, Woken(result));
        Assert.Empty(result.State.Bag);
    }

    [Fact]
    public void AreaAttackAlertsRemoteFriendlyRecipientWithoutExpandingCapturedGroup()
    {
        var caster = UnitRoster.Wizard("hero") with { Unique = false, Hp = 1 };
        var scenario = Fixture(Unit("caster", 7, dormant: false, type: "hero", group: "camp"),
            Unit("recipient", 9, type: "hero", group: "camp"),
            Unit("remote", 12, type: "hero", group: "camp"), Unit("enemy", 10, dormant: false, side: "blue"));
        scenario = scenario with { UnitTypes = [caster, scenario.UnitTypes![1]] };
        var dice = new Dice { First = new("hero", "red", "camp") };
        var result = Step(Start(scenario, dice), "stay", dice);
        var attack = result.NextInput!.Candidates.First(c => c.Action == UnitAction.Fireball && c.TargetIds.Contains("recipient") && !c.TargetIds.Contains("remote"));
        result = Step(result, attack.Key, dice);
        Assert.False(Sleeping(result.State, "remote"));
        Assert.DoesNotContain("recipient", result.State.ResolvingTokenUnitIds);
        Assert.DoesNotContain("remote", result.State.ResolvingTokenUnitIds);
        Assert.Contains("remote", Woken(result));
        var saved = Copy(result.State);
        var resumed = GameEngine.Advance(saved, new Pick("roll-dice"), new Dice(), false);
        var continued = Step(result, "roll-dice", new Dice());
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(resumed));
    }

    [Fact]
    public void EverySimultaneousAttackRecipientInitiatesItsOwnFriendlyAlertNetwork()
    {
        var scenario = Fixture(Unit("caster", 7, 5, dormant: false, side: "blue", type: "hero"),
            Unit("red-recipient", 9, 5), Unit("red-remote", 12, 5),
            Unit("green-recipient", 9, 6, side: "green"), Unit("green-remote", 12, 6, side: "green"));
        scenario = scenario with { UnitTypes = [UnitRoster.Wizard("hero"), scenario.UnitTypes![1]] };
        var dice = new Dice { First = new("hero", "blue") };
        var result = Step(Start(scenario, dice), "stay", dice);
        var attack = result.NextInput!.Candidates.First(c => c.Action == UnitAction.Fireball &&
            c.TargetIds.Contains("red-recipient") && c.TargetIds.Contains("green-recipient") &&
            !c.TargetIds.Contains("red-remote") && !c.TargetIds.Contains("green-remote"));
        result = Step(result, attack.Key, dice);
        Assert.Equal(new[] { "red-recipient", "red-remote", "green-recipient", "green-remote" }, Woken(result));
        Assert.Equal(0, dice.Rolls);
        Assert.All(result.Events.Where(e => e.Kind == "UnitWoken"), e => Assert.Equal("attack", e.ActionId));
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
    }

    [Fact]
    public void DefeatedOrRemovedFriendlyUnitsCannotBridgeAnAlert()
    {
        foreach (var removed in new[] { false, true })
        {
            var scenario = Fixture(Unit("hero", 0, dormant: false, side: "blue", type: "hero"),
                Unit("root", 1, dormant: false, wake: 1), Unit("bridge", 4, dormant: false), Unit("last", 7));
            var state = GameEngine.CreateGame(scenario);
            state.Physical.Figures.RemoveAll(f => f.Id == "bridge");
            if (removed) state.Units.RemoveAll(u => u.Id == "bridge");
            else
            {
                state.Units[state.Units.FindIndex(u => u.Id == "bridge")] = state.Units.Single(u => u.Id == "bridge") with { CurrentHp = 0 };
                state.DefeatedUnitIds.Add("bridge");
            }
            var result = GameEngine.StartRound(state, new Dice(), false);
            Assert.True(Sleeping(result.State, "last"));
        }
    }

    [Fact]
    public void LargerDormantPopulationRetainsFiguresAndAvoidsActivationWork()
    {
        (EngineResult Result, int Decisions, long Milliseconds) Resolve(bool dormant)
        {
            var units = new List<UnitPlacement> { Unit("hero", 0, dormant: false, side: "blue", type: "hero") };
            units.AddRange(Enumerable.Range(0, 144).Select(i => Unit($"crowd-{i}", 20 + i % 12, i / 12, dormant: dormant, group: "crowd")));
            var scenario = Fixture([.. units]) with
            {
                UnitTypes = [new("hero", 0, 0, 0, 0, 1, Actions: UnitAction.None), new("enemy", 0, 0, 0, 0, 1, Actions: UnitAction.None)],
                Orders = [new("hero", "blue", OrderKind.DoNothing), new("enemy", "red", OrderKind.DoNothing)]
            };
            var clock = Stopwatch.StartNew();
            var result = Start(scenario);
            var events = result.Events.ToList();
            var decisions = 0;
            while (!result.State.RoundComplete)
            {
                Assert.True(++decisions < 500);
                result = GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), new Dice(), false);
                events.AddRange(result.Events);
            }
            clock.Stop();
            return (result with { Events = events }, decisions, clock.ElapsedMilliseconds);
        }
        var active = Resolve(false);
        var dormant = Resolve(true);
        output.WriteLine($"144 distant Units: awake {active.Milliseconds} ms / {active.Decisions} decisions; dormant {dormant.Milliseconds} ms / {dormant.Decisions} decisions.");
        Assert.Equal(145, active.Result.Events.Count(e => e.Kind == "ActivationStarted"));
        Assert.Equal(1, dormant.Result.Events.Count(e => e.Kind == "ActivationStarted"));
        Assert.True(dormant.Decisions < active.Decisions);
        Assert.Equal(144, dormant.Result.State.Units.Count(u => u.Dormant));
        Assert.Equal(active.Result.State.Physical.Figures, dormant.Result.State.Physical.Figures);
        Assert.Empty(Woken(dormant.Result));
    }
}
