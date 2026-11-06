using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DormantTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken? First;
        public AttackFace Attack = AttackFace.Miss;
        public DefenceFace Defence = DefenceFace.Block;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => First is { } first && bag.Contains(first) ? first : bag[0];
        public AttackFace RollAttackDie() => Attack;
        public DefenceFace RollDefenceDie() => Defence;
        public int RollD6() => 6;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture(int distance = 5, int enemyX = 10) => new()
    {
        Board = new() { Width = 32, Height = 5 }, UnitTypeIds = ["hero", "enemy"],
        UnitTypes = [new("hero", 5, 6, 2, 0, 1), new("enemy", 1, 1, 1, 1, 1)],
        Units = [new("hero", "blue", new(0, 2), UnitId: "hero"),
            new("enemy", "red", new(enemyX, 2), UnitId: "sleeper", GroupId: "camp", Dormant: true, WakeDistance: distance)],
        Agency = [new("hero", "blue", ControllerKind.Human), new("enemy", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static EngineResult Start(ScenarioDefinition scenario, Dice? dice = null) =>
        GameEngine.StartRound(GameEngine.CreateGame(scenario), dice ?? new(), false);
    private static EngineResult Step(EngineResult result, string key, Dice? dice = null) =>
        GameEngine.Advance(result.State, new Pick(key), dice ?? new(), false);
    private static EngineResult Restore(EngineResult result) => result with
        { State = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))! };

    [Fact]
    public void InitialWakeUsesCompleteSetupAndContributesToken()
    {
        var scenario = Fixture(5, 5);
        // Reversing authored placements must not make initialization order-sensitive.
        var first = Start(scenario); var reversed = Start(scenario with { Units = scenario.Units.AsEnumerable().Reverse().ToList() });
        Assert.False(first.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.False(reversed.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.Contains(new("enemy", "red", "camp"), first.State.Bag);
        Assert.Equal("UnitWoken", first.Events[0].Kind);
    }

    [Fact]
    public void ProximityWaitsForWholeGroupAndDoesNotAddMissingToken()
    {
        var scenario = Fixture();
        scenario.Units.Insert(1, new("hero", "blue", new(0, 4), UnitId: "second"));
        var result = Step(Start(scenario), "hero");
        result = Step(result, "5,2");
        Assert.True(result.State.Units.Single(u => u.Id == "sleeper").Dormant);
        result = Step(result, "end-turn");
        Assert.True(result.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.DoesNotContain(result.Events, e => e.Kind == "UnitWoken");
        Assert.Equal("second", result.State.CurrentUnitId);
        result = Step(result, "stay");
        if (!result.State.RoundComplete) result = Step(result, "end-turn");
        Assert.False(result.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.Contains(result.Events, e => e.Kind == "UnitWoken" && e.ActionId == "proximity");
        Assert.True(result.State.RoundComplete);
        Assert.Empty(result.State.Bag);
        var next = GameEngine.StartRound(result.State, new Dice(), false);
        Assert.Contains(new("enemy", "red", "camp"), next.State.Bag);
    }

    [Fact]
    public void WakeBeforeExistingTokenDrawJoinsItsCapturedParticipants()
    {
        var scenario = Fixture();
        scenario.Units.Add(new("enemy", "red", new(28, 2), UnitId: "awake", GroupId: "camp"));
        var result = Step(Start(scenario), "5,2");
        result = Step(result, "end-turn");
        Assert.Equal(new("enemy", "red", "camp"), result.State.ActiveToken);
        Assert.Equal(new[] { "awake", "sleeper" }, result.State.ResolvingTokenUnitIds.Order());
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == "sleeper");
    }

    [Fact]
    public void AttackWakeDuringMatchingGroupDoesNotExpandCapturedMembership()
    {
        var scenario = Fixture(0, 10);
        // Fireball can target a Cell and include a friendly Dormant group member.
        var caster = UnitRoster.Wizard("caster");
        scenario = scenario with { UnitTypeIds = ["caster", "enemy"], UnitTypes = [caster, scenario.UnitTypes![1]],
            Agency = [new("caster", "red", ControllerKind.Human), new("enemy", "blue", ControllerKind.Human)],
            Units = [new("caster", "red", new(7, 2), UnitId: "caster", GroupId: "camp"),
                new("caster", "red", new(9, 2), UnitId: "sleeper", GroupId: "camp", Dormant: true, WakeDistance: 0),
                new("caster", "red", new(10, 2), UnitId: "second-sleeper", GroupId: "camp", Dormant: true, WakeDistance: 0),
                new("enemy", "blue", new(11, 2), UnitId: "enemy") ] };
        // Canonical Wizard is Unique. This test-owned variant permits two HP-1 casters.
        scenario.UnitTypes[0] = caster with { Unique = false, Hp = 1 };
        var state = GameEngine.CreateGame(scenario);
        var dice = new Dice { First = new("caster", "red", "camp"), Defence = DefenceFace.Miss };
        var result = GameEngine.StartRound(state, dice, false);
        Assert.DoesNotContain("sleeper", result.State.ResolvingTokenUnitIds);
        result = Step(result, "stay", dice);
        var fireball = result.NextInput!.Candidates.First(c => c.Action == UnitAction.Fireball &&
            c.TargetIds.Contains("sleeper") && c.TargetIds.Contains("second-sleeper"));
        result = Step(result, fireball.Key, dice);
        Assert.False(result.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.False(result.State.Units.Single(u => u.Id == "second-sleeper").Dormant);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "UnitWoken"));
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
        Assert.DoesNotContain("sleeper", result.State.ResolvingTokenUnitIds);
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Step(Restore(result), "roll-dice", dice);
        if (result.State.ActiveToken == new ActivationToken("caster", "red", "camp")) result = Step(result, "end-turn", dice);
        Assert.DoesNotContain(result.Events, e => e.Kind == "ActivationStarted" && e.UnitId == "sleeper");
    }

    [Theory]
    [InlineData(AttackFace.Miss, DefenceFace.Miss, false)]
    [InlineData(AttackFace.Hit, DefenceFace.Block, false)]
    [InlineData(AttackFace.Hit, DefenceFace.Miss, true)]
    public void ValidAttackWakesBeforeDiceWhetherMissedBlockedOrLethal(AttackFace attack, DefenceFace defence, bool defeated)
    {
        var dice = new Dice { Attack = attack, Defence = defence };
        var scenario = Fixture(0, 1);
        scenario = scenario with { UnitTypes = [scenario.UnitTypes![0] with { Atk = 1 }, scenario.UnitTypes[1]] };
        var result = Step(Start(scenario, dice), "stay", dice);
        var saved = JsonSerializer.Serialize(result.State);
        Assert.Throws<ArgumentException>(() => Step(result, "attack:missing", dice));
        Assert.Equal(saved, JsonSerializer.Serialize(result.State));
        result = Step(result, "attack:sleeper", dice);
        Assert.False(result.State.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.Equal("UnitWoken", result.Events.Single(e => e.Kind == "UnitWoken").Kind);
        Assert.True(result.Events.FindIndex(e => e.Kind == "UnitWoken") < result.Events.FindIndex(e => e.Kind == "AttackStarted"));
        Assert.Empty(result.State.Bag);
        var replay = Step(Restore(result), "roll-dice", dice);
        result = Step(result, "roll-dice", dice);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Step(result, "roll-dice", dice);
        Assert.Equal(defeated, result.State.DefeatedUnitIds.Contains("sleeper"));
    }

    [Fact]
    public void WakeDefaultsOverridesZeroAndValidationUseTheSameModel()
    {
        var scenario = Fixture() with { UnitTypes = [new("hero", 5, 6, 2, 0, 1), new("enemy", 1, 1, 1, 1, 1) { WakeDistance = 12 }] };
        scenario.Units[1] = scenario.Units[1] with { WakeDistance = null };
        Assert.Equal(12, GameEngine.CreateGame(scenario).Units[1].WakeDistance);
        scenario.Units[1] = scenario.Units[1] with { WakeDistance = 0 };
        Assert.True(Start(scenario).State.Units[1].Dormant);
        Assert.Equal(DormantRules.DefaultWakeDistance, GameEngine.CreateGame(scenario).Units[0].WakeDistance);
        scenario.Units[1] = scenario.Units[1] with { WakeDistance = -1 };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario));
        Assert.Throws<ArgumentException>(() => ContentDefinitions.ValidateStructure(new UnitType("bad", 1, 1, 1, 1, 1) { WakeDistance = -1 }));
    }

    [Fact]
    public void MinimumFootprintDistanceIgnoresWallsAndFogButNotHostilityOrLife()
    {
        var scenario = Fixture(5, 8) with { UnitTypes = [new("hero", 1, 1, 1, 0, 1),
            new("enemy", 1, 1, 1, 1, 1) { Footprint = Footprint.TwoByTwo }] };
        scenario.Units[0] = scenario.Units[0] with { Anchor = new(14, 2) };
        scenario = scenario with { FogOfWarSides = ["blue"], Board = scenario.Board with
            { Edges = Enumerable.Range(0, 5).Select(y => new EdgeDefinition(new(11, y), EdgeDirection.Right, EdgeKind.Wall)).ToList() } };
        var result = Start(scenario);
        Assert.False(result.State.Units[1].Dormant); // 14 - 9 = 5, not anchor distance 6.
        Assert.Null(FogOfWar.ObserveEvent(result.ResolutionSteps.First(s => result.Events[s.EventIndex].Kind == "UnitWoken").StateAfter,
            "blue", result.Events.Single(e => e.Kind == "UnitWoken")));
        Assert.True(Start(scenario with { NeutralSides = ["red"] }).State.Units[1].Dormant);
    }

    [Fact]
    public void EquipmentGrantedAttackWakesEvenWhenPrintedAttackIsZero()
    {
        var scenario = Fixture(0, 5);
        scenario = scenario with { EquipmentTypes = [new("wake-wand", "Wand")
            { LocalAbilities = [new("bolt", "Bolt", AbilityTiming.Action, new TargetUnitAttackMechanic(6, 1))] }],
            UnitTypes = [scenario.UnitTypes![0] with { Atk = 0, Actions = UnitAction.None, CanHaveEquipment = true }, scenario.UnitTypes[1]] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("wand-instance", "wake-wand")] };
        var result = Step(Start(scenario), "stay");
        var attack = result.NextInput!.Candidates.Single(c => c.Ability?.SourceKind == AbilitySourceKind.Equipment && c.TargetId == "sleeper");
        result = Step(result, attack.Key);
        Assert.False(result.State.Units[1].Dormant);
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
        Assert.Equal("attack", result.Events.Single(e => e.Kind == "UnitWoken").ActionId);
    }

    [Fact]
    public void DormantPassivesAndWorldDamageRemainOrdinaryMechanics()
    {
        var scenario = Fixture(0, 10);
        scenario = scenario with { UnitTypes = [scenario.UnitTypes![0], scenario.UnitTypes[1] with
            { LocalAbilities = [new("aura", "Defence Aura", AbilityTiming.Passive,
                new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Def, 2)]))] }],
            WorldEffects = new(1, 1) { Composition = [new("repulsion")] } };
        scenario.Units.Add(new("enemy", "red", new(11, 2), UnitId: "friend", WakeDistance: 0));
        var state = GameEngine.CreateGame(scenario);
        Assert.True(state.Units.Single(u => u.Id == "sleeper").Dormant);
        Assert.Equal(3, state.EffectiveDefOf("friend"));
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Contains(result.Events, e => e.Kind == "WorldDamageResolved" && e.TargetId == "sleeper");
        Assert.Contains("sleeper", result.State.DefeatedUnitIds);
        Assert.DoesNotContain(result.Events, e => e.Kind == "UnitWoken");
    }

    [Fact]
    public void LaterRoundChecksAfterWorldEffectsAndBeforePopulation()
    {
        var scenario = Fixture(5, 10) with { WorldEffects = new(1, 1) { Composition = [new("earthquake")] } };
        var state = GameEngine.CreateGame(scenario);
        state.Round = 1; state.RoundComplete = true;
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(5, 2) };
        var result = GameEngine.StartRound(state, new Dice(), false);
        var wake = result.Events.FindIndex(e => e.Kind == "UnitWoken");
        Assert.True(result.Events.FindIndex(e => e.Kind == "WorldCardDiscarded") < wake);
        Assert.True(wake < result.Events.FindIndex(e => e.Kind == "ActivationBagPopulated"));
        var atWake = result.ResolutionSteps.Single(s => s.EventIndex == wake).StateAfter;
        Assert.Equal(Posture.Lying, atWake.Physical.Figures.Single(f => f.Id == "sleeper").Posture);
        Assert.False(atWake.Units.Single(u => u.Id == "sleeper").Dormant);
    }

    [Fact]
    public void DistantDormantCrowdSkipsActivationsWhileRetainingEveryFigure()
    {
        EngineResult Resolve(bool dormant)
        {
            var scenario = Fixture() with
            {
                Board = new() { Width = 32, Height = 14 },
                Units = [new("hero", "blue", new(0, 2), UnitId: "hero"), .. Enumerable.Range(0, 100).Select(i =>
                    new UnitPlacement("enemy", "red", new(20 + i % 10, i / 10), UnitId: $"crowd-{i}", GroupId: "crowd", Dormant: dormant))],
                UnitTypes = [new("hero", 0, 0, 0, 0, 1, Actions: UnitAction.None), new("enemy", 0, 0, 0, 0, 1, Actions: UnitAction.None)],
                Orders = [new("hero", "blue", OrderKind.DoNothing), new("enemy", "red", OrderKind.DoNothing)]
            };
            var random = new Dice();
            var result = Start(scenario, random);
            var events = result.Events.ToList();
            var decisions = 0;
            while (!result.State.RoundComplete)
            {
                Assert.True(++decisions < 300);
                result = GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), random, false);
                events.AddRange(result.Events);
            }
            return result with { Events = events };
        }
        var active = Resolve(false); var sleeping = Resolve(true);
        Assert.Equal(101, active.Events.Count(e => e.Kind == "ActivationStarted"));
        Assert.Equal(1, sleeping.Events.Count(e => e.Kind == "ActivationStarted"));
        Assert.Equal(2, active.Events.Count(e => e.Kind == "TokenDrawn"));
        Assert.Equal(1, sleeping.Events.Count(e => e.Kind == "TokenDrawn"));
        Assert.Equal(101, sleeping.State.Physical.Figures.Count);
        Assert.Equal(active.State.Physical.Figures, sleeping.State.Physical.Figures);
        Assert.Equal(100, sleeping.State.Units.Count(u => u.Dormant));
    }
}
