using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FriendlySwapTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Beastmaster) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static Swap Friendly() => new() { Selector = RelationshipSelector.Friendlies };
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static Candidate[] Swaps(GameState state) => state.Pending?.Candidates.Where(c => c.Ability?.Definition.Mechanic is Swap).ToArray() ?? [];
    private static Cell Position(GameState state, string id) => state.Physical.Figures.Single(f => f.Id == id).Position;

    private static ScenarioDefinition Definition(AbilityTiming timing = AbilityTiming.BonusAction, Swap? mechanic = null,
        bool equipment = false, UnitType? actor = null, UnitType? target = null, BoardDefinition? board = null,
        Cell? targetCell = null, Posture targetPosture = Posture.Upright)
    {
        var ability = new LocalAbilityDefinition("exchange", "Exchange", timing, mechanic ?? Friendly());
        actor ??= new UnitType("actor-type", 3, 1, 2, 0, 1) { CanHaveEquipment = true };
        actor = actor with { LocalAbilities = [.. actor.LocalAbilities,
            .. equipment ? Array.Empty<LocalAbilityDefinition>() : [ability],
            new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))] };
        target ??= new("target-type", 0, 1, 0, 0, 1);
        var third = new UnitType("third-type", 0, 1, 0, 0, 1);
        return new()
        {
            Board = board ?? new() { Width = 8, Height = 6 }, UnitTypeIds = [actor.Id, target.Id, third.Id], UnitTypes = [actor, target, third],
            EquipmentTypes = equipment ? [new("swap-kit", "Swap kit") { LocalAbilities = [ability] }] : [],
            Units = [new(actor.Id, "blue", new(2, 2), UnitId: "actor") { Inventory = equipment ? [new("physical-kit", "swap-kit")] : [] },
                new(target.Id, "blue", targetCell ?? new(3, 2), targetPosture, UnitId: "friend"),
                new(third.Id, "red", new(5, 4), UnitId: "enemy")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new(target.Id, "blue", ControllerKind.Human), new(third.Id, "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
    }
    private static GameState Ready(ScenarioDefinition? definition = null)
    {
        var state = GameEngine.CreateGame(definition ?? Definition());
        var actor = state.Units.Single(u => u.Id == "actor");
        state.Round = 1; state.ActiveToken = actor.Token; state.CurrentUnitId = actor.Id; state.ResolvingTokenUnitIds = [actor.Id];
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    [Theory]
    [InlineData(UnitTypeIds.Barbarian, AbilityTiming.Action)]
    [InlineData(UnitTypeIds.Rogue, AbilityTiming.Action)]
    [InlineData(UnitTypeIds.Cleric, AbilityTiming.Action)]
    [InlineData(UnitTypeIds.Wizard, AbilityTiming.Action)]
    [InlineData(UnitTypeIds.Beastmaster, AbilityTiming.BonusAction)]
    public void FiveHeroesDeclareExactlyOneUnlimitedFriendlySwap(string id, AbilityTiming timing)
    {
        var type = CanonicalUnitTypes.Find(id)!;
        var ability = Assert.Single(type.LocalAbilities, a => a.Mechanic is Swap);
        Assert.Equal("friendly-swap", ability.Key); Assert.Equal(timing, ability.Timing);
        Assert.Equal(RelationshipSelector.Friendlies, Assert.IsType<Swap>(ability.Mechanic).Selector);
        Assert.Null(ability.Usage.MaxUses);
        var card = Assert.Single(type.CardEntries(), c => c.Id == "ability:friendly-swap");
        Assert.Equal("Swap places with an adjacent friendly Unit.", card.Description);
        Assert.DoesNotContain(new UnitType("future-hero", 3, 1, 3, 3, 4).LocalAbilities, a => a.Mechanic is Swap);
    }

    [Fact]
    public void EnemyDefaultExplicitAndLegacyPayloadPreserveExistingChoicesAndExecution()
    {
        var legacy = JsonSerializer.Deserialize<AbilityMechanic>("{\"kind\":\"swap\"}")!;
        Assert.Equal(new Swap(), Assert.IsType<Swap>(legacy));
        var definition = Definition(mechanic: new Swap());
        definition = definition with { Units = [definition.Units[0], definition.Units[1] with { SideId = "red" }, definition.Units[2] with { Anchor = new(1, 1) }],
            Agency = [definition.Agency[0], new("target-type", "red", ControllerKind.Human), definition.Agency[2]] };
        var state = Ready(definition);
        Assert.Equal(new[] { "friend", "enemy" }, Swaps(state).Select(c => c.TargetId));
        var explicitType = definition.UnitTypes![0] with { LocalAbilities = [definition.UnitTypes[0].LocalAbilities[0] with { Mechanic = new Swap { Selector = RelationshipSelector.Enemies } }, definition.UnitTypes[0].LocalAbilities[1]] };
        var explicitState = Ready(definition with { UnitTypes = [explicitType, .. definition.UnitTypes.Skip(1)] });
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(explicitState));
        Assert.Equal(JsonSerializer.Serialize(Use(state, Swaps(state)[0].Key)), JsonSerializer.Serialize(Use(explicitState, Swaps(explicitState)[0].Key)));
    }

    [Fact]
    public void FriendlyCandidatesExcludeSelfEnemiesAndNeutralSidesAndKeepStableOrder()
    {
        var definition = Definition();
        definition = definition with { Units = [definition.Units[0], definition.Units[1],
            new("target-type", "blue", new(1, 1), UnitId: "second"), definition.Units[2] with { Anchor = new(2, 3) },
            new("third-type", "neutral", new(1, 2), UnitId: "neutral")], Agency = [.. definition.Agency, new("third-type", "neutral", ControllerKind.Human)], NeutralSides = ["neutral"] };
        var state = Ready(definition);
        Assert.Equal(new[] { "friend", "second" }, Swaps(state).Select(c => c.TargetId));
        Assert.Equal(Swaps(state).Select(c => c.Key), Swaps(GameEngine.RefreshChoices(Restore(state), new Dice(), false).State).Select(c => c.Key));
        Assert.Throws<ArgumentException>(() => Use(state, "ability:exchange:enemy"));
        Assert.Throws<ArgumentException>(() => Use(state, "ability:exchange:actor"));
        Assert.Throws<ArgumentException>(() => Use(state, "ability:exchange:neutral"));
    }

    [Theory]
    [InlineData(AbilityTiming.Action, false)]
    [InlineData(AbilityTiming.BonusAction, false)]
    [InlineData(AbilityTiming.Action, true)]
    [InlineData(AbilityTiming.BonusAction, true)]
    public void TimingAndPhysicalSourceUseNormalBudgetsWithoutExtraActions(AbilityTiming timing, bool equipment)
    {
        var definition = Definition(timing, equipment: equipment);
        var state = Ready(definition with { Units = [definition.Units[0], definition.Units[1], definition.Units[2] with { Anchor = new(3, 3) }] });
        if (timing == AbilityTiming.Action) state = Use(state, "stay").State;
        var choice = Assert.Single(Swaps(state)); var result = Use(state, choice.Key);
        Assert.Equal(timing == AbilityTiming.Action ? 1 : 0, result.State.ActionsUsedThisActivation);
        Assert.Equal(timing == AbilityTiming.BonusAction ? 1 : 0, result.State.BonusActionUsesThisActivation(choice.Ability!.AccountingId));
        Assert.Equal(0, result.State.ExtraActionsThisActivation);
        Assert.Empty(Swaps(result.State));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action && timing == AbilityTiming.Action);
        Assert.Contains(result.NextInput.Candidates, c => c.Ability?.Key == "reserve");
        Assert.Null(result.State.AbilityUsesOf("actor", choice.Ability));
        Assert.Empty(result.State.Units.Single(u => u.Id == "actor").AbilityUseCounts);
        Assert.Equal(new(3, 2), Position(result.State, "actor")); Assert.Equal(new(2, 2), Position(result.State, "friend"));
        Assert.Equal(choice.Kind, Assert.Single(result.Events, e => e.Kind == "ActionUsed").Category);
        Assert.DoesNotContain(result.Events, e => e.Kind is "MovementCompleted" or "AttackStarted");
        Assert.Throws<ArgumentException>(() => Use(result.State, choice.Key));
        GameEngine.ValidateScenario(Restore(result.State));
        if (timing == AbilityTiming.BonusAction)
        {
            var afterStay = Use(result.State, "stay").State;
            Assert.Contains(afterStay.Pending!.Candidates, c => c.Key == "attack:enemy");
            var attacked = Use(afterStay, "attack:enemy");
            while (attacked.NextInput?.Kind == DecisionKind.RollDice) attacked = Use(attacked.State, "roll-dice");
            Assert.Equal(1, attacked.State.ActionsUsedThisActivation);
            Assert.DoesNotContain(attacked.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
            Assert.Empty(Swaps(attacked.State));
        }
    }

    [Theory]
    [InlineData(AbilityTiming.Action)]
    [InlineData(AbilityTiming.BonusAction)]
    public void UnlimitedSwapBecomesAvailableAgainInTheNextActivation(AbilityTiming timing)
    {
        var state = Ready(Definition(timing));
        for (var activation = 0; activation < 2; activation++)
        {
            if (timing == AbilityTiming.Action) state = Use(state, "stay").State;
            state = Use(state, Assert.Single(Swaps(state)).Key).State;
            Assert.Empty(Swaps(state)); Assert.Empty(state.Units.Single(u => u.Id == "actor").AbilityUseCounts);
            for (var steps = 0; !state.RoundComplete && steps < 30; steps++)
            {
                var candidates = state.Pending!.Candidates;
                var choice = candidates.FirstOrDefault(c => c.Key == "stay") ?? candidates.FirstOrDefault(c => c.Key == "end-turn") ?? candidates[0];
                state = Use(state, choice.Key).State;
            }
            Assert.True(state.RoundComplete);
            if (activation == 0) state = GameEngine.StartRound(Restore(state), new Dice(), false).State;
        }
    }

    [Theory]
    [InlineData(EdgeKind.None, true)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void FriendlySwapPreservesOrdinaryAdjacencyEdgesEvenWithPhase(EdgeKind kind, bool legal)
    {
        foreach (var phase in new[] { false, true })
        {
            var actor = new UnitType("actor-type", 3, 1, 2, 0, 1);
            if (phase) actor = actor.WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
            var state = Ready(Definition(actor: actor, board: new() { Width = 8, Height = 6,
                Edges = kind == EdgeKind.None ? [] : [new(new(2, 2), EdgeDirection.Right, kind)] }));
            Assert.Equal(legal, Swaps(state).Length == 1);
            if (legal) GameEngine.ValidateScenario(Use(state, Swaps(state).Single().Key).State);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagonalCornersRequireAnOpenLosPassage(bool blocked)
    {
        var edges = new List<EdgeDefinition> { new(new(2, 2), EdgeDirection.Right, EdgeKind.Wall) };
        if (blocked) edges.Add(new(new(2, 2), EdgeDirection.Down, EdgeKind.ClosedDoor));
        var state = Ready(Definition(targetCell: new(3, 3), board: new() { Width = 8, Height = 6, Edges = edges }));
        Assert.Equal(!blocked, Swaps(state).Any());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoByTwoParticipantsRemainIneligibleAndInternalEdgesRemainIllegal(bool largeActor)
    {
        var actor = new UnitType("actor-type", 3, 1, 2, 0, 1) { Footprint = largeActor ? Footprint.TwoByTwo : Footprint.OneByOne };
        var target = new UnitType("target-type", 0, 1, 0, 0, 1) { Footprint = largeActor ? Footprint.OneByOne : Footprint.TwoByTwo };
        var definition = Definition(actor: actor, target: target, targetCell: new(4, 2));
        if (!largeActor) definition = definition with { Units = [definition.Units[0] with { Anchor = new(3, 2) }, .. definition.Units.Skip(1)] };
        Assert.Empty(Swaps(Ready(definition)));
        var inside = largeActor ? new Cell(2, 2) : new Cell(4, 2);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Board = definition.Board with { Edges = [new(inside, EdgeDirection.Right, EdgeKind.Wall)] } }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothReceivingTypesNeedWaterPermissionEvenWhenLyingOrPhasing(bool actorAmphibious, bool waterAtActor)
    {
        var actor = new UnitType("actor-type", 3, 1, 2, 0, 1) { Amphibious = waterAtActor || actorAmphibious,
            LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] };
        var target = new UnitType("target-type", 0, 1, 0, 0, 1) { Amphibious = !waterAtActor || actorAmphibious };
        var state = Ready(Definition(actor: actor, target: target, targetPosture: Posture.Lying, board: new() { Width = 8, Height = 6,
            Cells = [new(waterAtActor ? new(2, 2) : new(3, 2), TerrainKind.Water)] }));
        Assert.Equal(actorAmphibious, Swaps(state).Any());
        if (actorAmphibious)
        {
            var result = Use(state, Swaps(state).Single().Key);
            Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "friend").Posture);
            GameEngine.ValidateScenario(result.State);
        }
    }

    [Fact]
    public void LyingActorCannotInvokeAndOccupiedEndpointsDoNotBecomeOrdinaryMoves()
    {
        var state = Ready();
        Assert.DoesNotContain(new Cell(3, 2), MovementRules.FindPaths(state, "actor", new(2, 2)).Keys);
        Assert.Contains(new Cell(4, 2), MovementRules.FindPaths(state, "actor", new(2, 2)).Keys); // Friendly pass-through remains normal.
        var snapshot = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("target-type", "overlap", "blue", new(3, 2)));
        Assert.Equal(snapshot, JsonSerializer.Serialize(state));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Empty(Swaps(GameEngine.RefreshChoices(state, new Dice(), false).State));
    }

    [Fact]
    public void FriendlySwapExplicitSelectionAndAtomicVisionUseExistingFogRules()
    {
        var state = Ready(); var candidate = Assert.Single(Swaps(state));
        state.FogOfWarSides = ["blue"]; state.ExploredCells["blue"] = [new(2, 2)];
        // Observation must not invent knowledge even for a candidate supplied from a fuller snapshot.
        Assert.False(FogOfWar.Allows(state, "blue", candidate));
        Assert.DoesNotContain(FogOfWar.Observe(state, "blue").Units, u => u.Id == "friend");
        state.ExploredCells["blue"].Add(new(3, 2));
        Assert.True(FogOfWar.Allows(state, "blue", candidate));
        var result = Use(state, candidate.Key);
        var step = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "PlacesSwapped");
        Assert.Equal(new(3, 2), Position(step.StateAfter, "actor"));
        Assert.Equal(new(2, 2), Position(step.StateAfter, "friend"));
        Assert.True(step.StateAfter.ExploredCells["blue"].IsSupersetOf(state.ExploredCells["blue"]));
    }

    [Fact]
    public void SaveRestoreAndDecisionReplayPreserveTargetsAtomicSnapshotsAndUsage()
    {
        var initial = Ready(); var key = Assert.Single(Swaps(initial)).Key;
        var restored = Restore(initial); GameEngine.ValidateScenario(restored);
        var live = Use(initial, key); var replay = Use(restored, key);
        Assert.Equal(JsonSerializer.Serialize(live), JsonSerializer.Serialize(replay));
        var step = live.ResolutionSteps.Single(s => live.Events[s.EventIndex].Kind == "PlacesSwapped");
        Assert.Equal(new(3, 2), Position(step.StateAfter, "actor")); Assert.Equal(new(2, 2), Position(step.StateAfter, "friend"));
        var resumed = GameEngine.RefreshChoices(Restore(live.State), new Dice(), false).State;
        Assert.Empty(Swaps(resumed));
        Assert.Equal(JsonSerializer.Serialize(Use(live.State, "stay")), JsonSerializer.Serialize(Use(resumed, "stay")));
    }

    [Fact]
    public void BeastmasterCanSwapPastSummonsOverSuccessiveActivationsWithoutChangingMovement()
    {
        string[] summons = [UnitTypeIds.Jackalope, UnitTypeIds.GiantToad, UnitTypeIds.DireBear, UnitTypeIds.Raptor, UnitTypeIds.LightElemental];
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 8, Height = 3, DefaultTerrain = TerrainKind.SolidRock,
                Cells = Enumerable.Range(0, 8).Select(x => new CellDefinition(new(x, 1), TerrainKind.StoneFloor)).ToList() },
            UnitTypeIds = [UnitTypeIds.Beastmaster, .. summons],
            Units = [new(UnitTypeIds.Beastmaster, "blue", new(1, 1), UnitId: "master"),
                .. summons.Select((type, i) => new UnitPlacement(type, "blue", new(i + 2, 1), UnitId: "summon-" + i))],
            Agency = new[] { UnitTypeIds.Beastmaster }.Concat(summons).Select(t => new AgencyAssignment(t, "blue", ControllerKind.Human)).ToList(),
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(definition);
        for (var i = 1; i < state.Units.Count; i++) state.Units[i] = state.Units[i] with { SummonerUnitId = "master" };
        for (var round = 0; round < 3; round++)
        {
            state = GameEngine.StartRound(Restore(state), new Dice(), false).State;
            Assert.Equal("master", state.CurrentUnitId);
            var start = Position(state, "master");
            if (round < 2) Assert.DoesNotContain(MovementRules.FindPaths(state, "master", start, state.EffectiveMovOf("master")).Keys, c => c.X > start.X);
            state = Use(state, "stay").State;
            var key = Swaps(state).Single(c => c.TargetId == "summon-" + round).Key;
            var swapped = Use(state, key);
            state = swapped.State;
            Assert.Equal(new(round + 2, 1), Position(state, "master"));
            Assert.Equal(start, Position(state, "summon-" + round));
            var atomic = swapped.ResolutionSteps.Single(s => swapped.Events[s.EventIndex].Kind == "PlacesSwapped").StateAfter;
            Assert.Equal(0, atomic.ActionsUsedThisActivation);
            Assert.Equal(1, atomic.BonusActionUsesThisActivation("unit:friendly-swap"));
            Assert.Single(swapped.Events, e => e.Kind == "PlacesSwapped");
            // With no remaining legal Action, ordinary forced End Turn can advance to a summon.
            Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Key == "friendly-swap");
            Assert.DoesNotContain(swapped.Events, e => e.Kind == "MovementCompleted");
            Assert.Empty(state.Units.Single(u => u.Id == "master").AbilityUseCounts);
            Assert.Throws<ArgumentException>(() => Use(state, key));
            for (var steps = 0; !state.RoundComplete && steps < 40; steps++)
            {
                var candidates = state.Pending!.Candidates;
                var choice = candidates.FirstOrDefault(c => c.Key == "stay") ?? candidates.FirstOrDefault(c => c.Key == "end-turn") ?? candidates[0];
                state = Use(state, choice.Key).State;
            }
            Assert.True(state.RoundComplete); GameEngine.ValidateScenario(Restore(state));
        }
    }
}
