using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BreakObjectTests
{
    private sealed class Dice(params int[] rolls) : IRandomProvider
    {
        public int Count;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => rolls[Count++];
    }
    private sealed class Choice(Func<DecisionRequest, string?> select) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => select(request);
    }
    private static LocalAbilityDefinition Ability(AbilityMechanic mechanic, AbilityTiming timing = AbilityTiming.Action, string key = "break") =>
        new(key, "Test " + key, timing, mechanic);
    private static GameState State(AbilityMechanic? mechanic = null, AbilityTiming timing = AbilityTiming.Action)
    {
        var type = new UnitType("actor-type", 1, 1, 1, 0, 5)
            { Unique = true, CanHaveItems = true, CanHaveEquipment = true, LocalAbilities = [Ability(mechanic ?? new BreakObject(6), timing)] };
        return new()
        {
            Physical = new(new Board(5, 5, []) { Terrain = [new(new(2, 1), TerrainKind.SolidRock)] }, [new("actor", new(1, 1))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")],
            Round = 1, ActiveToken = new(type.Id, "blue"), CurrentUnitId = "actor", MoveDone = true,
            Pending = new(DecisionKind.Activation, type.Id, "actor", [], false)
        };
    }
    private static EngineResult Use(GameState state, Dice? dice = null, Func<Candidate, bool>? target = null) =>
        TestGame.Advance(state, new Choice(r => r.Candidates.Single(c => c.Ability?.Definition.Key == "break" && (target?.Invoke(c) ?? true)).Key), dice ?? new(), false);

    [Fact]
    public void CanonicalMappingsAndEveryStructureAreExplicit()
    {
        Assert.Equal(new Dictionary<string, string> { ["tree"] = "grass", ["fruit-tree"] = "grass", ["solid-rock"] = "gravel", ["stone-floor-with-table"] = "stone-floor", ["campfire"] = "grass" },
            TerrainCatalog.All.Where(t => t.DestroyedTerrainTypeId is not null).ToDictionary(t => t.Id, t => t.DestroyedTerrainTypeId!));
        foreach (var kind in Enum.GetValues<EdgeKind>()) Assert.Equal(kind == EdgeKind.None ? null : EdgeKind.None, BreakObjectRules.DestroyedEdgeKind(kind));
    }

    [Fact]
    public void HiddenAutomaticRecipientsDoNotLeakThroughDiceEventCounts()
    {
        var state = State(new BreakAdjacentObjects(3));
        state.FogOfWarSides.Add("blue");
        state.ExploredCells["blue"] = [new(1, 1)];
        var hiddenRoll = new RulesEvent("DiceRolled", "actor")
        { SourceUnitId = "actor", Dice = new(new(DiceFamily.D6, 1, "actor", "actor", "break", "Break")
            { BreakTarget = new(Cell: new(2, 1)) }, ["1"], 1) };
        Assert.Null(FogOfWar.ObserveEvent(state, "blue", hiddenRoll));
        state.ExploredCells["blue"].Add(new(2, 1));
        Assert.NotNull(FogOfWar.ObserveEvent(state, "blue", hiddenRoll));
    }

    [Fact]
    public void GuaranteedDescriptionsDoNotInstructPlayersToRoll()
    {
        Assert.Equal("Choose a neighboring destructible object or bordering structure. Break it without rolling.",
            ContentDescriptions.AbilityEntry(Ability(new BreakObject(6)), "break").Description);
        Assert.Equal("When this Unit is defeated, break every neighboring destructible object and bordering structure without rolling.",
            ContentDescriptions.AbilityEntry(Ability(new BreakAdjacentObjects(6), AbilityTiming.WhenDefeated), "break").Description);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(5, 6, false)]
    [InlineData(5, 5, true)]
    [InlineData(6, 0, true)]
    public void ThresholdsDriveMutationAndDice(int threshold, int roll, bool success)
    {
        var state = State(new BreakObject(threshold));
        var dice = new Dice(roll);
        var result = Use(state, dice);
        var outcome = Assert.Single(Assert.Single(result.Events, e => e.Kind == "ObjectBreakResolved").BreakOutcomes);
        Assert.Equal(success, outcome.Succeeded);
        Assert.Equal(threshold == 6 ? 0 : 1, dice.Count);
        Assert.Equal(threshold == 6 ? null : (int?)roll, outcome.DieRoll);
        Assert.Equal(success ? "gravel" : "solid-rock", result.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal("solid-rock", state.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        var breakStep = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ObjectBreakResolved");
        Assert.True(breakStep.StateAfter.ActionDone);
        if (threshold == 6) Assert.DoesNotContain(result.Events, e => e.Kind == "DiceRolled");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidThresholdsRejected(int threshold) => Assert.Throws<ArgumentException>(() => AbilityContent.Validate([Ability(new BreakObject(threshold))], AbilitySourceKind.Unit));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidDieRejectedWithoutChangingInput(int roll)
    {
        var state = State(new BreakObject(1));
        Assert.Throws<ArgumentException>(() => Use(state, new Dice(roll)));
        Assert.False(state.ActionDone);
        Assert.Equal("solid-rock", state.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
    }

    [Fact]
    public void BonusActionUsesSharedAccountingAndLeavesActionAvailable()
    {
        var result = Use(State(new BreakObject(6), AbilityTiming.BonusAction));
        var snapshot = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ObjectBreakResolved").StateAfter;
        Assert.False(snapshot.ActionDone);
        Assert.Equal(1, snapshot.BonusActionUsesThisActivation("unit:break"));
    }

    [Fact]
    public void TargetTerrainAloneIsExemptFromLosAndEdgesRemainBlocking()
    {
        var state = State();
        Assert.Single(BreakObjectRules.Targets(state, [new(1, 1)]));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        var target = Assert.Single(BreakObjectRules.Targets(state, [new(1, 1)]));
        Assert.NotNull(target.Edge);
        Assert.Null(target.Cell);
    }

    [Fact]
    public void FootprintRingExcludesOwnCellsAndCanonicalizesSharedInternalEdges()
    {
        var state = State();
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        state.Physical.Board.Terrain.Clear();
        state.Physical.Board.Terrain.Add(new(new(1, 1)) { TypeId = "campfire" });
        state.Physical.Board.Terrain.Add(new(new(3, 2), TerrainKind.Tree));
        state.Physical.Board.Edges.Add(new(new(2, 1), new(1, 1), EdgeKind.OpenDoor));
        var targets = BreakObjectRules.Targets(state, FootprintGeometry.OccupiedCells(state, "actor"));
        Assert.Equal(2, targets.Length);
        Assert.DoesNotContain(targets, t => t.Cell == new Cell(1, 1));
        Assert.Equal(new Edge(new(1, 1), new(2, 1), EdgeKind.OpenDoor), targets.Single(t => t.Edge is not null).Edge);
    }

    [Fact]
    public void AreaBatchFreezesRecipientsRollsEachOnceAndPublishesOnce()
    {
        var state = State(new BreakAdjacentObjects(3));
        state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        state.Physical.Board.Edges.Add(new(new(0, 1), new(1, 1), EdgeKind.WallWithWindow));
        var dice = new Dice(1, 6, 2);
        var result = Use(state, dice);
        var resolved = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
        Assert.Equal(3, dice.Count);
        Assert.Equal(new[] { true, false, true }, resolved.BreakOutcomes.Select(o => o.Succeeded));
        Assert.Equal(new Cell(1, 0), resolved.BreakOutcomes[0].Target.Cell);
        Assert.Equal(new Cell(2, 1), resolved.BreakOutcomes[1].Target.Cell);
        Assert.Empty(result.State.Physical.Board.Edges);
        Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == "AdjacentObjectsBreakResolved");
    }

    [Fact]
    public void AreaDoesNotRecursivelyBreakReplacementOrNewlyExposedObjects()
    {
        var state = State(new BreakAdjacentObjects(6));
        var custom = new TerrainDefinition("box", "Box", false, true) { DestroyedTerrainTypeId = "tree" };
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = TerrainCatalog.All.Add(custom) } };
        state.Physical.Board.Terrain[0] = new(new(2, 1)) { TypeId = "box" };
        var result = Use(state);
        Assert.Equal("tree", result.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Single(Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved").BreakOutcomes);
    }

    private static GameState ConflictingWaterState(int threshold, bool reverse)
    {
        var state = State(new BreakAdjacentObjects(threshold));
        var basin = new TerrainDefinition("basin", "Dry Basin", true, false) { DestroyedTerrainTypeId = "water" };
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = TerrainCatalog.All.Add(basin) } };
        state.Physical.Board.Terrain.Clear();
        state.Physical.Board.Terrain.AddRange([new(new(1, 0)) { TypeId = basin.Id },
            new(new(2, 0)) { TypeId = basin.Id }, new(new(0, 1)) { TypeId = basin.Id }]);
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.OpenDoor));
        if (reverse)
        {
            state.Physical.Board.Terrain.Reverse();
            state.Physical.Board.Edges.Reverse();
            for (var i = 0; i < state.Physical.Board.Edges.Count; i++)
            {
                var edge = state.Physical.Board.Edges[i];
                state.Physical.Board.Edges[i] = edge with { A = edge.B, B = edge.A };
            }
        }
        return state;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void AreaPreflightExcludesConflictingWaterBeforeRollingForEveryAcceptedSubset(int successes)
    {
        string? outcomes = null;
        foreach (var reverse in new[] { false, true })
        {
            var state = ConflictingWaterState(3, reverse);
            var excluded = new Cell(2, 0);
            Assert.True(BreakObjectRules.CanBreak(state, new(Cell: excluded))); // Individually legal.
            var rolls = Enumerable.Range(0, 3).Select(i => (successes & (1 << i)) != 0 ? 1 : 6).ToArray();
            var dice = new Dice(rolls);
            var result = Use(state, dice);
            var resolved = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
            Assert.Equal(3, dice.Count);
            Assert.Equal(new BreakTarget[] { new(Cell: new(1, 0)), new(Cell: new(0, 1)),
                new(Edge: new(new(1, 1), new(1, 2), EdgeKind.OpenDoor)) }, resolved.BreakOutcomes.Select(o => o.Target));
            Assert.Equal(rolls.Select(r => r == 1), resolved.BreakOutcomes.Select(o => o.Succeeded));
            var diceEvents = result.Events.Where(e => e.Kind == "DiceRolled").ToArray();
            Assert.Equal(3, diceEvents.Length);
            Assert.DoesNotContain(diceEvents, e => e.Dice!.Pool.BreakTarget?.Cell == excluded);
            Assert.DoesNotContain(resolved.BreakOutcomes, o => o.Target.Cell == excluded);
            Assert.Equal("basin", result.State.Physical.Board.TerrainDefinitionAt(excluded).Id);
            Assert.Equal(EdgeKind.Wall, result.State.Physical.Board.EdgeBetween(new(1, 0), excluded));
            Assert.Equal((successes & 1) != 0 ? "water" : "basin", result.State.Physical.Board.TerrainDefinitionAt(new(1, 0)).Id);
            Assert.Equal((successes & 2) != 0 ? "water" : "basin", result.State.Physical.Board.TerrainDefinitionAt(new(0, 1)).Id);
            Assert.Equal((successes & 4) != 0 ? EdgeKind.None : EdgeKind.OpenDoor,
                result.State.Physical.Board.EdgeBetween(new(1, 1), new(1, 2)));
            var batch = Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == resolved.Kind);
            Assert.True(batch.StateAfter.ActionDone);
            GameEngine.ValidateScenario(batch.StateAfter);
            foreach (var step in result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "DiceRolled"))
                Assert.All(step.StateAfter.Physical.Board.Terrain, t => Assert.Equal("basin", t.TypeId));
            var serialized = JsonSerializer.Serialize(resolved.BreakOutcomes);
            if (outcomes is not null) Assert.Equal(outcomes, serialized);
            outcomes = serialized;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuaranteedAreaUsesTheSameConservativeWaterPreflightWithoutDice(bool reverse)
    {
        var dice = new Dice();
        var result = Use(ConflictingWaterState(6, reverse), dice);
        Assert.Equal(0, dice.Count);
        Assert.DoesNotContain(result.Events, e => e.Kind == "DiceRolled");
        var resolved = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
        Assert.Equal(3, resolved.BreakOutcomes.Length);
        Assert.All(resolved.BreakOutcomes, o => { Assert.True(o.Succeeded); Assert.Null(o.DieRoll); });
        Assert.DoesNotContain(resolved.BreakOutcomes, o => o.Target.Cell == new Cell(2, 0));
        Assert.Equal("basin", result.State.Physical.Board.TerrainDefinitionAt(new(2, 0)).Id);
        GameEngine.ValidateScenario(result.State);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void AreaPreflightDoesNotDependOnEarlierWaterRemovalSucceeding(int roll)
    {
        var state = ConflictingWaterState(3, false);
        var definitions = state.Physical.Board.TerrainTypes;
        var water = definitions.Single(t => t.Id == "water");
        definitions = definitions.SetItem(definitions.IndexOf(water), water with { DestroyedTerrainTypeId = "grass" });
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = definitions } };
        state.Physical.Board.Terrain.RemoveAll(t => t.Position == new Cell(0, 1));
        state.Physical.Board.Terrain[0] = new(new(1, 0)) { TypeId = "water" };
        state.Physical.Board.Edges.RemoveAll(e => e.Kind == EdgeKind.OpenDoor);
        var dice = new Dice(roll);
        var result = Use(state, dice);
        var outcome = Assert.Single(Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved").BreakOutcomes);
        Assert.Equal(new Cell(1, 0), outcome.Target.Cell);
        Assert.Equal(1, dice.Count);
        Assert.Equal(roll == 1 ? "grass" : "water", result.State.Physical.Board.TerrainDefinitionAt(new(1, 0)).Id);
        Assert.Equal("basin", result.State.Physical.Board.TerrainDefinitionAt(new(2, 0)).Id);
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void PendingRollRoundTripsAndDetachedCopiesDoNotShareQueue()
    {
        var state = State(new BreakObject(2));
        TestGame.Author(state);
        var result = GameEngine.Advance(state, new Choice(r => r.Candidates.Single(c => c.BreakTarget is not null).Key), new Dice(), false);
        Assert.Equal(DecisionKind.RollDice, result.NextInput!.Kind);
        Assert.Equal(new BreakTarget(Cell: new(2, 1)), result.NextInput.Roll!.BreakTarget);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        var final = GameEngine.Advance(restored, new Choice(_ => "roll-dice"), new Dice(2), false);
        Assert.Equal("gravel", final.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.NotNull(restored.BreakInProgress);
        var copy = restored.Copy();
        copy.PendingDefeatEffects.Clear();
        Assert.NotNull(restored.BreakInProgress);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rail")]
    [InlineData("box")]
    public void InvalidDestroyedReferencesRejected(string result)
    {
        var box = new TerrainDefinition("box", "Box", false, true) { DestroyedTerrainTypeId = result };
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([box]));
    }

    [Fact]
    public void DestructionCycleRejectedAndDependencyIsDiscoverable()
    {
        var a = new TerrainDefinition("a", "A", false, true) { DestroyedTerrainTypeId = "b" };
        var b = new TerrainDefinition("b", "B", false, true) { DestroyedTerrainTypeId = "a" };
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve([a, b]));
        var graph = new ContentDependencyGraph([new(a), new(b)]);
        Assert.Contains(graph.Diagnostics, d => d.Code == "invalidDestruction");
        Assert.Contains(graph.DirectDependencies(new(ContentFamily.Terrain, "a")), d => d.Target == new ContentIdentity(ContentFamily.Terrain, "b"));
    }

    [Fact]
    public void UnsafeOccupiedReplacementAndRequiredMachineTerrainAreExcluded()
    {
        var state = State();
        var box = new TerrainDefinition("box", "Box", true, false) { DestroyedTerrainTypeId = "solid-rock" };
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = TerrainCatalog.All.Add(box) } };
        state.Physical.Board.Terrain[0] = new(new(2, 1)) { TypeId = "box" };
        state.Physical.Figures.Add(new("other", new(2, 1), Posture.Lying));
        var occupant = new UnitType("other-type", 0, 0, 0, 0, 1);
        state.Types.Add(occupant);
        state.Units.Add(occupant.CreateUnit("other", "blue"));
        Assert.Empty(BreakObjectRules.Targets(state, [new(1, 1)]));
        state.Physical.Figures.RemoveAt(1);
        state.Units.RemoveAt(1);
        state.Physical.Board.Machines.Add(new("machine", new(2, 1), MachineTypes.Beacon with { RequiredTerrainTypeId = "box" }));
        Assert.Empty(BreakObjectRules.Targets(state, [new(1, 1)]));
    }

    [Fact]
    public void BatchObservationUsesPerTargetBeforeStateKnowledgeAndRedactsHiddenSource()
    {
        var state = State(new BreakAdjacentObjects(6));
        state.FogOfWarSides.Add("viewer");
        state.ExploredCells["viewer"] = [new(2, 1)];
        var known = new BreakOutcome(new(Cell: new(2, 1)), 6, null, true, "solid-rock", "gravel") { BeforeVisibleSideIds = ["viewer"] };
        var hidden = new BreakOutcome(new(Cell: new(1, 0)), 6, null, true, "tree", "grass");
        var observed = FogOfWar.ObserveEvent(state, "viewer", new("AdjacentObjectsBreakResolved", "actor")
            { SourceUnitId = "actor", ActionId = "secret", BreakOutcomes = [known, hidden] });
        Assert.NotNull(observed);
        Assert.Single(observed.BreakOutcomes);
        Assert.Empty(observed.BreakOutcomes[0].BeforeVisibleSideIds);
        Assert.Null(observed.UnitId);
        Assert.Null(observed.SourceUnitId);
        Assert.Null(observed.ActionId);
    }

    private static UnitType Bomb(int threshold = 6) => new("test-bomb", 0, 0, 0, 0, 1, Actions: UnitAction.None)
    {
        LocalAbilities = [Ability(new DefeatSelf(), AbilityTiming.InsteadOfActivation, "fuse"),
            Ability(new Explosion(), AbilityTiming.WhenDefeated, "damage"),
            Ability(new BreakAdjacentObjects(threshold), AbilityTiming.WhenDefeated, "break")]
    };

    [Fact]
    public void LyingActivationStandsThenUprightActivationDefeatsAndFinishesEffectsBeforeCompletion()
    {
        var type = Bomb();
        var state = new GameState
        {
            Types = [type], Units = [type.CreateUnit("bomb", "blue")],
            Physical = new(new Board(4, 4, []) { Terrain = [new(new(2, 1), TerrainKind.Tree)] }, [new("bomb", new(1, 1), Posture.Lying)])
        };
        TestGame.WithIsolatedOpponent(state);
        var first = TestGame.StartRound(state, new Dice(), false);
        Assert.Equal(Posture.Upright, first.State.Physical.Figures.Single(f => f.Id == "bomb").Posture);
        Assert.DoesNotContain(first.Events, e => e.Kind == "UnitDefeated");
        var second = TestGame.StartRound(first.State, new Dice(), false);
        Assert.DoesNotContain(second.State.Physical.Figures, f => f.Id == "bomb");
        Assert.Equal("grass", second.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.True(second.Events.FindIndex(e => e.Kind == "AdjacentObjectsBreakResolved") < second.Events.FindIndex(e => e.Kind == "ActivationCompleted"));
        Assert.Empty(second.State.PendingDefeatEffects);
        Assert.DoesNotContain(second.Events, e => e.Kind == "DiceRolled");
    }

    [Fact]
    public void SelfDefeatUsesUndyingReplacementAndEndsActivationCleanly()
    {
        var type = Bomb() with { LocalAbilities = Bomb().LocalAbilities.Add(Ability(new Undying(), AbilityTiming.InsteadOfDefeat, "undying")) };
        var state = new GameState { Types = [type], Units = [type.CreateUnit("bomb", "blue")], Physical = new(new Board(3, 3, []), [new("bomb", new(1, 1))]) };
        var result = TestGame.StartRound(state, new Dice(), false);
        Assert.Equal(1, result.State.Units.Single().CurrentHp);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single().Posture);
        Assert.DoesNotContain(result.Events, e => e.Kind is "UnitDefeated" or "AdjacentObjectsBreakResolved");
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted");
    }

    [Fact]
    public void DeclaredConsequencesResolveBeforeSecondaryDefeatConsequences()
    {
        var state = State();
        state.Physical.Board.Terrain.Clear();
        var bomb = Bomb(2);
        state.Types.Add(bomb);
        state.Units.Add(bomb.CreateUnit("a", "red"));
        state.Units.Add(bomb.CreateUnit("b", "red"));
        state.Physical.Figures.Add(new("a", new(2, 1), Posture.Lying));
        state.Physical.Figures.Add(new("b", new(3, 1), Posture.Lying));
        state.Physical.Board.Terrain.Add(new(new(2, 0), TerrainKind.Tree));
        var dice = new Dice(1);
        var result = TestGame.Advance(state, new Choice(_ => "attack:a"), dice, false);
        var aDamage = result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved" && e.UnitId == "a");
        var aBreak = result.Events.FindIndex(e => e.Kind == "AdjacentObjectsBreakResolved" && e.UnitId == "a");
        var bBreak = result.Events.FindIndex(e => e.Kind == "AdjacentObjectsBreakResolved" && e.UnitId == "b");
        Assert.True(aDamage >= 0 && aDamage < aBreak && aBreak < bBreak);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "actor").CurrentHp);
        Assert.Equal(1, dice.Count);
        Assert.Empty(result.State.PendingDefeatEffects);
    }

    [Fact]
    public void CompetingActivationReplacementsRejected()
    {
        var abilities = Bomb().LocalAbilities.Add(Ability(new RunRails(1), AbilityTiming.InsteadOfActivation, "rail"));
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate(abilities, AbilitySourceKind.Unit));
    }

    [Fact]
    public void EarlierBreakOpensLineOfSightForQueuedSecondaryExplosion()
    {
        var state = State();
        state.Physical.Board.Terrain.Clear();
        var bomb = Bomb();
        var fragile = new UnitType("fragile", 0, 0, 0, 0, 1);
        state.Types.AddRange([bomb, fragile]);
        state.Units.AddRange([bomb.CreateUnit("a", "red"), bomb.CreateUnit("b", "red"), fragile.CreateUnit("beyond", "blue")]);
        state.Physical.Figures.AddRange([new("a", new(2, 1), Posture.Lying), new("b", new(3, 1), Posture.Lying), new("beyond", new(4, 0))]);
        state.Physical.Board.Terrain.Add(new(new(3, 0), TerrainKind.Tree));
        state.Physical.Board.Edges.Add(new(new(3, 1), new(4, 1), EdgeKind.Wall));
        var result = TestGame.Advance(state, new Choice(_ => "attack:a"), new Dice(), false);
        var opened = result.Events.FindIndex(e => e.Kind == "AdjacentObjectsBreakResolved" && e.UnitId == "a");
        var damage = result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved" && e.UnitId == "b" && e.TargetId == "beyond");
        Assert.True(opened >= 0 && damage > opened);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "beyond");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedSourcesDriveExecutionConsumptionAndAttribution(bool item)
    {
        var state = State();
        state.Types[0] = state.Types[0] with { LocalAbilities = [] };
        var ability = Ability(new BreakObject(6)) with { Usage = new(item ? null : 2) };
        if (item) state.ItemTypes.Add(new("tool", "Tool") { LocalAbilities = [ability] });
        else state.EquipmentTypes.Add(new("tool", "Tool") { LocalAbilities = [ability] });
        state.Units[0] = state.Units[0] with { Inventory = [new OwnedObject("physical-tool") { ItemTypeId = item ? "tool" : null, EquipmentTypeId = item ? null : "tool" }] };
        var result = Use(state);
        var resolved = Assert.Single(result.Events, e => e.Kind == "ObjectBreakResolved");
        Assert.Equal(item ? AbilitySourceKind.Item : AbilitySourceKind.Equipment, resolved.AbilitySource!.Kind);
        Assert.Equal("physical-tool", resolved.AbilitySource.InstanceId);
        var inventory = result.State.Units.Single().Inventory;
        if (item) Assert.Empty(inventory);
        else Assert.Equal(1, Assert.Single(inventory).AbilityUseCounts["break"]);
    }

    [Fact]
    public void StaleCandidateRejectsWithoutPaymentOrMutation()
    {
        var state = State();
        TestGame.Author(state);
        var key = GameEngine.GameplayCandidates(state, state.Units[0]).Single(c => c.BreakTarget is not null).Key;
        state.Physical.Board.Terrain.Clear();
        state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(state, new Choice(_ => key), new Dice(), false));
        Assert.False(state.ActionDone);
        Assert.Empty(state.Units[0].AbilityUseCounts);
    }

    [Fact]
    public void DiagonalTargetRequiresCornerPassageAndBoardBoundaryNeverSuppliesEdges()
    {
        var state = State();
        state.Physical.Board.Terrain[0] = new(new(2, 2), TerrainKind.Tree);
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor));
        Assert.DoesNotContain(BreakObjectRules.Targets(state, [new(1, 1)]), t => t.Cell == new Cell(2, 2));
        Assert.Empty(BreakObjectRules.Targets(state, [new(0, 0)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccupiedWaterReplacementUsesAmphibiousEvenWhileLying(bool amphibious)
    {
        var state = State();
        var box = new TerrainDefinition("box", "Box", true, false) { DestroyedTerrainTypeId = "water" };
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = TerrainCatalog.All.Add(box) } };
        state.Physical.Board.Terrain[0] = new(new(2, 1)) { TypeId = "box" };
        var occupant = new UnitType("other", 0, 0, 0, 0, 1) { Amphibious = amphibious, LocalAbilities = [Ability(new Phase(), AbilityTiming.Passive, "phase")] };
        state.Types.Add(occupant);
        state.Units.Add(occupant.CreateUnit("other", "blue"));
        state.Physical.Figures.Add(new("other", new(2, 1), Posture.Lying));
        Assert.Equal(amphibious, BreakObjectRules.CanBreak(state, new(Cell: new(2, 1))));
    }

    [Fact]
    public void AssemblyMappingIsAuthoredExplicitlyAndRoundTripsWithDependency()
    {
        var assembly = new TerrainAssemblyDefinition("custom-table", "Portable Table", "asphalt")
            { Components = [new("object", "table")], DestroyedTerrainTypeId = "asphalt" };
        var restored = JsonSerializer.Deserialize<TerrainAssemblyDefinition>(JsonSerializer.Serialize(assembly))!;
        var definitions = TerrainCatalog.Resolve([], [restored]);
        Assert.Equal("asphalt", TerrainCatalog.Find(definitions, "custom-table").DestroyedTerrainTypeId);
        Assert.Null(TerrainCatalog.Find(definitions, "asphalt").DestroyedTerrainTypeId);
        Assert.Contains(ContentDefinitions.Dependencies(restored), d => d.Path == "destroyedTerrainTypeId" && d.Target == new ContentIdentity(ContentFamily.Terrain, "asphalt"));
    }

    [Fact]
    public void BreakingOccupiedNonblockingAssemblyPreservesUnitAndOtherLayers()
    {
        var state = State();
        var decal = new FixedComponentDefinition("chalk", "Chalk", false, false)
            { Presentation = new("chalk", FixedComponentLayer.Decal) };
        var assembly = new TerrainAssemblyDefinition("chalk-floor", "Chalk Floor", "stone-floor")
            { Components = [new("mark", decal.Id)], DestroyedTerrainTypeId = "grass" };
        var definitions = TerrainCatalog.Resolve([], [assembly], [decal]);
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = definitions } };
        state.Physical.Board.Terrain[0] = new(new(2, 1)) { TypeId = assembly.Id };
        var occupant = new UnitType("occupant", 0, 0, 0, 0, 1);
        state.Types.Add(occupant); state.Units.Add(occupant.CreateUnit("other", "blue"));
        state.Physical.Figures.Add(new("other", new(2, 1), Posture.Lying));
        state.Resources.Add(new(ResourceTypes.Coal, new(2, 1)));
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(2, 1)));
        var result = Use(state);
        Assert.Equal("grass", result.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal(new Cell(2, 1), result.State.Physical.Figures.Single(f => f.Id == "other").Position);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "other").Posture);
        Assert.Single(result.State.Resources); Assert.Single(result.State.CellMarkers);
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "other").CurrentHp);
    }

    [Fact]
    public void FailedBonusBreakSpendsLimitedUseAndBonusOpportunityOnly()
    {
        var state = State(new BreakObject(2), AbilityTiming.BonusAction);
        state.Types[0] = state.Types[0] with { LocalAbilities = [state.Types[0].LocalAbilities[0] with { Usage = new(2) }] };
        var result = Use(state, new Dice(6));
        var snapshot = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ObjectBreakResolved").StateAfter;
        Assert.False(snapshot.ActionDone);
        Assert.Equal(1, snapshot.BonusActionUsesThisActivation("unit:break"));
        Assert.Equal(1, snapshot.Units.Single().AbilityUseCounts["unit:break"]);
        Assert.Equal("solid-rock", snapshot.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnsafeResourceOrMarkerReplacementIsNotATarget(bool resource)
    {
        var state = State();
        var box = new TerrainDefinition("box", "Box", true, false) { DestroyedTerrainTypeId = "ordinary-lava" };
        state.Physical = state.Physical with { Board = state.Physical.Board with { TerrainTypes = TerrainCatalog.All.Add(box) } };
        state.Physical.Board.Terrain[0] = new(new(2, 1)) { TypeId = "box" };
        if (resource) state.Resources.Add(new(ResourceTypes.Coal, new(2, 1)));
        else state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(2, 1)));
        Assert.Empty(BreakObjectRules.Targets(state, [new(1, 1)]));
    }

    [Fact]
    public void SuccessRevealsThroughRockAndPreservesDetachedExploration()
    {
        var state = State();
        state.FogOfWarSides.Add("blue");
        state.Types[0] = state.Types[0] with { SightDistance = 4 };
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new(new(1, 2), new(2, 2), EdgeKind.Wall));
        FogOfWar.Refresh(state);
        var originalExploration = state.ExploredCells["blue"].ToHashSet();
        Assert.DoesNotContain(new Cell(2, 1), MovementRules.FindPaths(state, "actor", new(1, 1)).Keys);
        var result = Use(state, target: c => c.BreakTarget?.Cell == new Cell(2, 1));
        Assert.True(FogOfWar.IsExplored(result.State, "blue", new(3, 1)));
        Assert.Equal(originalExploration.OrderBy(c => c.Y).ThenBy(c => c.X), state.ExploredCells["blue"].OrderBy(c => c.Y).ThenBy(c => c.X));
        Assert.Equal("solid-rock", state.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal("gravel", result.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Contains(new Cell(2, 1), MovementRules.FindPaths(result.State, "actor", new(1, 1)).Keys);
    }

    [Theory]
    [InlineData(AbilitySourceKind.Unit, AbilityTiming.Action, 2, true)]
    [InlineData(AbilitySourceKind.Equipment, AbilityTiming.BonusAction, 2, true)]
    [InlineData(AbilitySourceKind.Item, AbilityTiming.Action, null, true)]
    [InlineData(AbilitySourceKind.Unit, AbilityTiming.WhenDefeated, null, true)]
    [InlineData(AbilitySourceKind.Unit, AbilityTiming.WhenDefeated, 2, false)]
    [InlineData(AbilitySourceKind.Equipment, AbilityTiming.WhenDefeated, null, false)]
    [InlineData(AbilitySourceKind.Item, AbilityTiming.WhenDefeated, null, false)]
    [InlineData(AbilitySourceKind.Unit, AbilityTiming.FreeAction, null, false)]
    public void AreaProfilesExposeOnlySupportedSourceTimingAndUsage(AbilitySourceKind source, AbilityTiming timing, int? limit, bool valid)
    {
        var error = Record.Exception(() => AbilityContent.Validate([Ability(new BreakAdjacentObjects(4), timing) with { Usage = new(limit) }], source));
        if (valid) Assert.Null(error); else Assert.IsType<ArgumentException>(error);
    }

    [Theory]
    [InlineData(AbilitySourceKind.Unit, null, true)]
    [InlineData(AbilitySourceKind.Unit, 2, false)]
    [InlineData(AbilitySourceKind.Equipment, null, false)]
    [InlineData(AbilitySourceKind.Item, null, false)]
    public void SelfDefeatIsNativeUnlimitedOnly(AbilitySourceKind source, int? limit, bool valid)
    {
        var error = Record.Exception(() => AbilityContent.Validate([Ability(new DefeatSelf(), AbilityTiming.InsteadOfActivation) with { Usage = new(limit) }], source));
        if (valid) Assert.Null(error); else Assert.IsType<ArgumentException>(error);
    }
}
