using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class PublicationOwnershipTests
{
    private static GameState State() => GameEngine.CreateGame(Scenario.Define(Scenario.Map(3, 1),
        [Scenario.Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, Scenario.At(0, 0, unitId: "actor"))]));

    private sealed class Random(bool fail = false, bool failAttack = false) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => fail ? throw new InvalidOperationException("test fault") : bag[0];
        public AttackFace RollAttackDie() => failAttack ? throw new InvalidOperationException("test attack fault") : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }

    [Fact]
    public void RawResponseCopyDoesNotEvaluatePresentationAndPreservesStateReferenceTopology()
    {
        // Deliberately incomplete raw state: presentation would need its missing Type.
        var state = State(); state.Types.Clear();
        var earlier = state.DetachedCopy();
        var source = new GameResponse(0, new(state,
            [new("UnitMoved", "actor", Path: [new(0, 0), new(1, 0)])
                { ObservedMovement = [new() { new(0, 0), new(1, 0) }] }],
            new(DecisionKind.Move, UnitTypeIds.Wizard, "actor", [new("move", Path: [new(0, 0), new(1, 0)])], false))
            { ResolutionSteps = [new(0, state), new(1, earlier)] });
        var copy = PlaytestGame.Detach(source);
        Assert.Null(copy.StoredPresentation);
        Assert.NotSame(state, copy.Result.State);
        Assert.Same(copy.Result.State, copy.Result.ResolutionSteps[0].StateAfter);
        Assert.NotSame(copy.Result.State, copy.Result.ResolutionSteps[1].StateAfter);
        copy.Result.State.Units.Clear();
        copy.Result.Events[0].Path!.Clear();
        copy.Result.Events[0].ObservedMovement![0].Clear();
        copy.Result.NextInput!.Candidates[0].Path!.Clear();
        Assert.Single(state.Units);
        Assert.Equal(2, source.Result.Events[0].Path!.Count);
        Assert.Equal(2, source.Result.Events[0].ObservedMovement![0].Count);
        Assert.Equal(2, source.Result.NextInput!.Candidates[0].Path!.Count);
        Assert.Single(copy.Result.ResolutionSteps[1].StateAfter.Units);
    }

    [Fact]
    public void ExplicitReceivedPresentationSurvivesRawCopyAndOwnsItsCollections()
    {
        var source = new GameResponse(0, new(State(), [], null))
        { Presentation = new(new Dictionary<string, UnitCard>(), null,
            [new(OutcomeRole.Movement, "received preview", Path: [new(0, 0)])], []) };
        var received = JsonSerializer.Deserialize<GameResponse>(JsonSerializer.Serialize(source))!;
        var copy = PlaytestGame.Detach(received);
        Assert.Equal("received preview", copy.Presentation.Events[0].Text);
        Assert.NotSame(received.Presentation, copy.Presentation);
        copy.Presentation.Events[0].Path!.Clear();
        Assert.Single(received.Presentation.Events[0].Path!);
        Assert.Null((copy with { Result = new(State(), [], null) }).StoredPresentation);
    }

    [Fact]
    public async Task SubscribersSnapshotsAndReplayCannotMutateEachOtherOrExecutor()
    {
        using var game = new PlaytestGame(new Random(), State());
        using var first = game.Subscribe(null);
        using var second = game.Subscribe(null);
        var baseline = await first.Reader.ReadAsync();
        var otherBaseline = await second.Reader.ReadAsync();
        baseline.Snapshot.Result.State.Units.Clear();
        Assert.Single(otherBaseline.Snapshot.Result.State.Units);
        var snapshot = game.Snapshot(); snapshot.Result.State.Physical.Figures.Clear();
        Assert.Single(game.Snapshot().Result.State.Physical.Figures);
        var result = game.StartRound(0);
        Assert.NotNull(result.Result.NextInput);
        var updates = new List<GameUpdate>();
        while (first.Reader.TryRead(out var update)) updates.Add(update);
        Assert.NotEmpty(updates);
        foreach (var update in updates) update.Snapshot.Result.State.Units.Clear();
        while (second.Reader.TryRead(out var update)) Assert.Single(update.Snapshot.Result.State.Units);
        using var replay = game.Subscribe($"{baseline.SessionId}:0");
        var count = 0;
        while (replay.Reader.TryRead(out var update)) { count++; Assert.Single(update.Snapshot.Result.State.Units); }
        Assert.Equal(updates.Count, count);
        result.Result.State.Units.Clear(); result.Result.NextInput!.Candidates.Clear();
        Assert.Single(game.Snapshot().Result.State.Units);
        Assert.NotEmpty(game.Snapshot().Result.NextInput!.Candidates);
    }

    [Fact]
    public async Task FaultRestoresOwnedStableCheckpointAfterConsumerMutation()
    {
        using var game = new PlaytestGame(new Random(fail: true), State());
        using var subscription = game.Subscribe(null);
        var baseline = await subscription.Reader.ReadAsync();
        baseline.Snapshot.Result.State.Round = 99;
        baseline.Snapshot.Result.State.Units.Clear();
        Assert.Throws<InvalidOperationException>(() => game.StartRound(0));
        Assert.Equal(0, game.Snapshot().Result.State.Round);
        Assert.Single(game.Snapshot().Result.State.Units);
        var updates = new List<GameUpdate>();
        while (subscription.Reader.TryRead(out var update)) updates.Add(update);
        Assert.Equal("fault", updates[^1].Kind);
        Assert.Equal(0, updates[^1].Snapshot.Result.State.Round);
    }

    [Fact]
    public void ProjectionUsesEventTimeVisibilityAndSharesOnlyIdenticalObservedStateReferences()
    {
        var before = State(); before.FogOfWarSides = ["blue"];
        before.ExploredCells = new() { ["blue"] = [] };
        var after = before.DetachedCopy(); after.ExploredCells["blue"].Add(new(0, 0));
        var result = new EngineResult(after,
            [new("ActivationStarted", "actor"), new("ActivationStarted", "actor"), new("ActivationCompleted", "actor")], null)
        { ResolutionSteps = [new(0, before), new(1, after), new(2, after)] };
        var observed = BrowserProjection.Observe(result, "blue");
        Assert.Equal(2, observed.Events.Count);
        Assert.Equal(new[] { 0, 1 }, observed.ResolutionSteps.Select(s => s.EventIndex));
        Assert.Same(observed.State, observed.ResolutionSteps[0].StateAfter);
        Assert.Same(observed.State, observed.ResolutionSteps[1].StateAfter);
        Assert.Empty(before.ExploredCells["blue"]);
    }

    [Fact]
    public void ObservedSnapshotRetainsHiddenAuraValueThroughRawCopyAndSerialization()
    {
        var state = State();
        var recipient = new UnitType("recipient", 1, 0, 0, 1, 1);
        var aura = new UnitType("provider", 1, 0, 0, 0, 1)
        {
            LocalAbilities = [new("defence", "Defence", AbilityTiming.Passive,
                new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Def, 2)]))]
        };
        state.Types.AddRange([recipient, aura]);
        state.Units.AddRange([recipient.CreateUnit("recipient", "red"), aura.CreateUnit("provider", "red")]);
        state.Physical.Figures.AddRange([new("recipient", new(1, 0)), new("provider", new(2, 0))]);
        state.FogOfWarSides = ["blue"];
        state.ExploredCells = new() { ["blue"] = [new(0, 0), new(1, 0)] };
        using var game = new PlaytestGame(new Random(), state);
        var source = new GameResponse(0, new(state, [new("ActivationStarted", "recipient")], null)
            { ResolutionSteps = [new(0, state)] });
        var browser = game.BrowserResponse(source);
        var copy = PlaytestGame.Detach(browser);
        Assert.DoesNotContain(copy.Result.State.Units, u => u.Id == "provider");
        Assert.Equal(3, copy.Result.State.EffectiveDef["recipient"]);
        Assert.Equal(3, copy.Result.ResolutionSteps[0].StateAfter.EffectiveDef["recipient"]);
        var serialized = JsonSerializer.SerializeToElement(copy.Result.State);
        Assert.Equal(3, serialized.GetProperty("EffectiveDef").GetProperty("recipient").GetInt32());
        Assert.Contains(state.Units, u => u.Id == "provider");
    }

    private static async Task<GameUpdate> Next(GameSubscription subscription, Func<GameUpdate, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var update = await subscription.Reader.ReadAsync(timeout.Token);
            if (predicate(update)) return update;
        }
    }

    private static GameCommand Command(GameUpdate current, string operation, string? key = null) => new()
    {
        CommandId = Guid.NewGuid().ToString("N"), SessionId = current.SessionId,
        ExpectedRevision = current.Revision, DecisionId = current.DecisionId,
        Operation = operation, CandidateKey = key
    };

    [Fact]
    public async Task FaultAfterMovementDiscoveryRestoresExplorationAndHiddenAuraWithoutChangingReplay()
    {
        var actor = new UnitType("scout", 4, 1, 1, 0, 1);
        var target = new UnitType("target", 0, 1, 0, 1, 1);
        var aura = new UnitType("aura", 0, 0, 0, 0, 1)
        {
            LocalAbilities = [new("defence", "Defence", AbilityTiming.Passive,
                new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Def, 2)]))]
        };
        var state = new GameState
        {
            ResourceDefinitions = [.. ResourceTypes.All], Types = [actor, target, aura],
            Units = [actor.CreateUnit("scout", "blue"), target.CreateUnit("target", "red"), aura.CreateUnit("aura", "red")],
            Physical = new(new Board(12, 1, []), [new("scout", new(0, 0)), new("target", new(5, 0)), new("aura", new(6, 0))]),
            Controllers = [new(new(actor.Id, "blue"), ControllerKind.Automated),
                new(new(target.Id, "red"), ControllerKind.Human), new(new(aura.Id, "red"), ControllerKind.Human)],
            FogOfWarSides = ["blue"]
        };
        FogOfWar.Refresh(state);
        using var game = new PlaytestGame(new Random(failAttack: true), state);
        using var stream = game.Subscribe(null);
        var baseline = await Next(stream, _ => true);
        var before = game.BrowserUpdate(baseline);
        var beforeJson = JsonSerializer.Serialize(before);
        Assert.DoesNotContain(before.Snapshot.Result.State.Units, u => u.Id == "aura");
        Assert.Equal(3, before.Snapshot.Result.State.EffectiveDef["target"]);
        game.Accept(Command(baseline, "round"));
        var movement = await Next(stream, u => u.Snapshot.Result.Events.Any(e => e.Kind == "MovementCompleted"));
        var movementJson = JsonSerializer.Serialize(game.BrowserUpdate(movement));
        var observed = game.BrowserUpdate(movement);
        Assert.Contains(new Cell(6, 0), observed.Snapshot.Result.State.ExploredCells["blue"]);
        Assert.Contains(observed.Snapshot.Result.State.Units, u => u.Id == "aura");
        observed.Snapshot.Result.State.ExploredCells["blue"].Clear();
        observed.Snapshot.Result.ResolutionSteps[0].StateAfter.Units.Clear();
        observed.Snapshot.Result.Events[0].Path!.Clear();
        observed.Snapshot.Presentation.Events[0].MovementSegments![0].Clear();
        var fault = await Next(stream, u => u.Kind == "fault");
        var restored = game.BrowserUpdate(fault).Snapshot.Result.State;
        Assert.Equal(0, restored.Round);
        Assert.Equal(new Cell(0, 0), restored.Physical.Figures.Single(f => f.Id == "scout").Position);
        Assert.True(restored.ExploredCells["blue"].SetEquals(state.ExploredCells["blue"]));
        Assert.DoesNotContain(restored.Units, u => u.Id == "aura");
        Assert.Equal(3, restored.EffectiveDef["target"]);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(before));
        using var replay = game.Subscribe($"{baseline.SessionId}:0");
        var replayed = await Next(replay, u => u.Sequence == movement.Sequence);
        Assert.Equal(movementJson, JsonSerializer.Serialize(game.BrowserUpdate(replayed)));
        Assert.Equal(3, game.BrowserResponse(game.Snapshot()).Result.State.EffectiveDef["target"]);
    }

    [Fact]
    public void NoFogResponseMatchesIndependentLegacyProjectionWithWorldLootAndSummonGeometry()
    {
        var summoner = UnitType.Define("caller", "Caller", UnitAuthoring.Stats(2, 0, 0, 1, 1),
            UnitAuthoring.CantAttack(), UnitAuthoring.Ability("Call Large Ally", UnitAuthoring.Unlimited(),
                UnitAuthoring.AdjacentUnitSummon("large-ally", Posture.Upright), id: "call-large"))
            with { CanHaveItems = true, CanHaveEquipment = true };
        var large = new UnitType("large-ally", 1, 1, 1, 1, 1) { Footprint = Footprint.TwoByTwo };
        var state = new GameState
        {
            Types = [summoner, large], ResourceDefinitions = [.. ResourceTypes.All],
            EquipmentTypes = [.. EquipmentCatalog.All], ItemTypes = [.. ItemCatalog.All], LootCardDefinitions = [.. LootCardCatalog.All],
            Physical = new(new Board(5, 3, []), [new("caller", new(0, 1))]),
            Units = [summoner.CreateUnit("caller", "blue") with
                { Inventory = [new("boots", LootCardKind.LuckyBoots), new("wind", LootCardKind.SecondWind)] }],
            GroundObjects = [new(new("key", LootCardKind.OfficialKey), new(4, 2))],
            WorldCardDefinitions = [new("breeze", "Breeze", WorldCardLifecycle.Continuous)
                { Modifiers = [new(WorldModifierTarget.Mov, 2), new(WorldModifierTarget.Def, 1)] }],
            WorldEffects = new(1, 1),
            WorldDeck = new() { ActiveContinuous = [new("breeze-copy", "breeze")] }
        };
        var earlier = state.DetachedCopy(); earlier.WorldDeck!.ActiveContinuous.Clear();
        var pending = new DecisionRequest(DecisionKind.Activation, summoner.Id, "caller",
            [new("summon-large", Destination: new(1, 0), Action: UnitAction.AdjacentUnitSummon)
                { Ability = WebAbilityTestQueries.Ability(state, "caller", "call-large") }], false);
        var result = new EngineResult(state, [new("ActivationStarted", "caller")], pending)
            { ResolutionSteps = [new(0, earlier)] };
        // Reproduce the original no-FoW presentation path from its independent
        // public component projections, without Observe/Create/CreateObserved.
        var expected = new BrowserPresentation(BrowserProjection.Cards(state), BrowserProjection.Decision(pending, state),
            result.Events.Select(e => BrowserProjection.Outcome(e, state)).ToArray(),
            [new BrowserResolutionStep(0, BrowserProjection.Cards(earlier))
            {
                GroundObjects = BrowserProjection.GroundObjects(earlier), Machines = BrowserProjection.Machines(earlier),
                Figures = BrowserProjection.Figures(earlier), WorldEffects = BrowserProjection.WorldEffects(earlier),
                Objectives = BrowserProjection.Objectives(earlier), Completion = earlier.Completion, Orders = earlier.Orders.ToArray()
            }])
        {
            GroundObjects = BrowserProjection.GroundObjects(state), Machines = BrowserProjection.Machines(state),
            Figures = BrowserProjection.Figures(state), WorldEffects = BrowserProjection.WorldEffects(state),
            Objectives = BrowserProjection.Objectives(state), Completion = state.Completion, Orders = state.Orders.ToArray()
        };
        using var game = new PlaytestGame(new Random(), state);
        var actual = game.BrowserResponse(new(0, result));
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual.Presentation));
        Assert.Equal(5, actual.Result.State.EffectiveMov["caller"]);
        Assert.Equal(2, actual.Result.State.EffectiveDef["caller"]);
        Assert.Equal(2, actual.Presentation.Cards["caller"].Inventory.Count);
        Assert.Single(actual.Presentation.GroundObjects);
        Assert.Equal(4, Assert.Single(actual.Presentation.Decision!.Candidates).PlacementCells.Count);
        Assert.DoesNotContain(actual.Result.State.Types, t => t.Id == large.Id);
    }

    [Fact]
    public async Task AcceptStreamingOwnsNestedChoicesEventPathsAndReplayForEverySubscriber()
    {
        using var game = new PlaytestGame(new Random(), State());
        using var first = game.Subscribe(null); using var second = game.Subscribe(null);
        var baseline = await Next(first, _ => true); await Next(second, _ => true);
        Assert.True(game.Accept(Command(baseline, "round")).Accepted);
        var firstBoundary = await Next(first, u => u.Status == "waiting-human");
        var secondBoundary = await Next(second, u => u.Sequence == firstBoundary.Sequence);
        var move = secondBoundary.Snapshot.Result.NextInput!.Candidates.First(c => c.Kind == ActivationChoiceKind.Move);
        var originalPath = move.Path!.ToArray();
        firstBoundary.Snapshot.Result.NextInput!.Candidates.Single(c => c.Key == move.Key).Path!.Clear();
        firstBoundary.Snapshot.Result.State.Pending!.Candidates.Clear();
        ((Dictionary<string, string>)firstBoundary.Snapshot.Controllers).Clear();
        Assert.Equal(originalPath, move.Path);
        Assert.NotEmpty(secondBoundary.Snapshot.Result.State.Pending!.Candidates);
        Assert.NotEmpty(game.Snapshot().Result.NextInput!.Candidates);
        Assert.True(game.Accept(Command(secondBoundary, "decision", move.Key)).Accepted);
        var firstEvent = await Next(first, u => u.Snapshot.Result.Events.Any(e => e.Kind == "MovementCompleted"));
        var secondEvent = await Next(second, u => u.Sequence == firstEvent.Sequence);
        var eventJson = JsonSerializer.Serialize(secondEvent);
        firstEvent.Snapshot.Result.Events[0].Path!.Clear();
        firstEvent.Snapshot.Result.ResolutionSteps[0].StateAfter.Physical.Figures.Clear();
        Assert.Equal(originalPath, secondEvent.Snapshot.Result.Events[0].Path);
        Assert.Single(secondEvent.Snapshot.Result.ResolutionSteps[0].StateAfter.Physical.Figures);
        var finished = await Next(first, u => u.Kind == "boundary" && u.Revision > secondBoundary.Revision);
        Assert.Equal(originalPath[^1], game.Snapshot().Result.State.Physical.Figures.Single().Position);
        using var replay = game.Subscribe($"{baseline.SessionId}:{secondBoundary.Sequence}");
        var replayed = await Next(replay, u => u.Sequence == secondEvent.Sequence);
        Assert.Equal(eventJson, JsonSerializer.Serialize(replayed));
        Assert.True(finished.Revision > secondBoundary.Revision);
    }
}
