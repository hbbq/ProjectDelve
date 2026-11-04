using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AbilityPaymentTests
{
    public static TheoryData<AbilitySourceKind, AbilityTiming> FireballContexts => new()
    {
        { AbilitySourceKind.Unit, AbilityTiming.Action }, { AbilitySourceKind.Unit, AbilityTiming.BonusAction },
        { AbilitySourceKind.Unit, AbilityTiming.FreeAction }, { AbilitySourceKind.Equipment, AbilityTiming.Action },
        { AbilitySourceKind.Equipment, AbilityTiming.BonusAction }, { AbilitySourceKind.Equipment, AbilityTiming.FreeAction },
        { AbilitySourceKind.Item, AbilityTiming.Action }, { AbilitySourceKind.Item, AbilityTiming.BonusAction },
        { AbilitySourceKind.Item, AbilityTiming.FreeAction }
    };

    private static GameState Ready(AbilityMechanic mechanic, ImmutableArray<AbilityCost> costs,
        AbilitySourceKind source = AbilitySourceKind.Unit, AbilityTiming timing = AbilityTiming.FreeAction,
        int coal = 3, int diamond = 3, int shell = 3, int banana = 3)
    {
        var ability = new LocalAbilityDefinition("paid", "Authored Ability", timing, mechanic)
        { Costs = costs, Usage = new(source == AbilitySourceKind.Item ? null : 1) };
        var actor = new UnitType("payer", 2, 4, 2, 0, 6, Actions: UnitAction.None)
        {
            Unique = true, CanHaveEquipment = true, CanHaveItems = true,
            ResourceStorage = new([new(ResourceTypes.Coal, 6), new(ResourceTypes.Diamond, 6),
                new(ResourceTypes.Shell, 6), new(ResourceTypes.Banana, 6), new(ResourceTypes.Mushroom, 6)]),
            LocalAbilities = source == AbilitySourceKind.Unit
                ? [ability, new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
                : [new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 4,
                Edges = [new(new(1, 0), EdgeDirection.Down, EdgeKind.ClosedDoor)] },
            UnitTypeIds = [actor.Id, "target", "spawn", UnitTypeIds.Troll],
            UnitTypes = [actor, new("target", 0, 0, 0, 0, 8) { Unique = true }, new("spawn", 1, 1, 1, 0, 1)],
            EquipmentTypes = source == AbilitySourceKind.Equipment ? [new("paid-equipment", "Wand") { LocalAbilities = [ability] }] : [],
            ItemTypes = source == AbilitySourceKind.Item ? [new("paid-item", "Scroll") { LocalAbilities = [ability] }] : [],
            Units = [new(actor.Id, "blue", new(1, 1), UnitId: "actor")
                { Inventory = source switch
                    {
                        AbilitySourceKind.Equipment => [new("physical-source", "paid-equipment")],
                        AbilitySourceKind.Item => [new("physical-source") { ItemTypeId = "paid-item" }],
                        _ => []
                    } }, new("target", "red", new(4, 1), UnitId: "enemy")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human),
                new("spawn", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            StoredResources = [new("blue", actor.Id, ResourceTypes.Coal, coal),
                new("blue", actor.Id, ResourceTypes.Diamond, diamond), new("blue", actor.Id, ResourceTypes.Shell, shell),
                new("blue", actor.Id, ResourceTypes.Banana, banana), new("blue", actor.Id, ResourceTypes.Mushroom, 2)],
            Loot = LootSettings.Empty
        })));
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "actor";
        state.MoveDone = timing == AbilityTiming.Action;
        state.Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false);
        return Refresh(state);
    }

    private static GameState Refresh(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false).State;
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string? key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static Candidate Paid(GameState state) => state.Pending!.Candidates.First(c => c.Ability?.Key == "paid");
    private static int Coal(GameState state) => state.StoredQuantity("blue", "payer", "coal");

    [Theory, MemberData(nameof(FireballContexts))]
    public void MissedFireballPaysBeforeDiceAndResumesWhileLyingOrItemConsumed(AbilitySourceKind source, AbilityTiming timing)
    {
        var state = Ready(new FireballMechanic(), [new SpendResource("coal", 3), new LayDownSelf()], source, timing);
        var candidate = state.Pending!.Candidates.Single(c => c.Ability?.Key == "paid" && c.Destination == new Cell(4, 1));
        var initial = JsonSerializer.Serialize(state);
        var started = Use(state, candidate.Key);
        Assert.Equal(initial, JsonSerializer.Serialize(state));
        Assert.Equal(0, Coal(started.State));
        Assert.False(started.State.IsUpright("actor"));
        Assert.Equal(DecisionKind.RollDice, started.NextInput!.Kind);
        Assert.Equal(timing, started.State.AttackInProgress!.OriginTiming);
        Assert.Equal(timing == AbilityTiming.Action ? 1 : 0, started.State.ActionsUsedThisActivation);
        var spent = Assert.Single(started.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.Equal(("actor", "payer", "blue", candidate.Ability!.ActionId),
            (spent.SourceUnitId, spent.TypeId, spent.SideId, spent.ActionId));
        Assert.Equal(ResourceTypes.Coal, spent.ResourceType);
        Assert.Equal(0, spent.StoredQuantity);
        Assert.True(started.Events.IndexOf(spent) < started.Events.FindIndex(e => e.Kind == "PostureChanged"));
        Assert.True(started.Events.FindIndex(e => e.Kind == "PostureChanged") < started.Events.FindIndex(e => e.Kind == "ActionUsed"));
        if (source == AbilitySourceKind.Item)
        {
            Assert.Empty(started.State.Units.Single(u => u.Id == "actor").Inventory);
            Assert.Equal("physical-source", Assert.Single(started.State.LootDeck!.DiscardPile).Id);
            Assert.Single(started.Events.Where(e => e.Kind == "ItemUsed"));
        }
        else
        {
            Assert.Equal(new AbilityUses(1, 0), started.State.AbilityUsesOf("actor", candidate.Ability));
            if (source == AbilitySourceKind.Equipment)
                Assert.Equal("physical-source", Assert.Single(started.State.Units.Single(u => u.Id == "actor").Inventory).Id);
        }
        var restored = Restore(started.State);
        GameEngine.ValidateScenario(restored);
        var continued = Use(started.State, "roll-dice");
        var resumed = Use(restored, "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(resumed));
        Assert.Equal(8, resumed.State.Units.Single(u => u.Id == "enemy").CurrentHp);
        Assert.Equal(0, Coal(resumed.State));
        Assert.DoesNotContain(resumed.Events, e => e.Kind is "ResourceSpent" or "ItemUsed");
        Assert.Contains(resumed.Events, e => e.Kind == "AttackTargetResolved" && e.Damage == 0);
    }

    [Fact]
    public void FailedDoorAttemptPaysBeforeRollAndNeverRepaysAfterSave()
    {
        var state = Ready(new TryOpenDoor(0), [new LayDownSelf(), new SpendResource("coal", 3)], timing: AbilityTiming.Action);
        var started = Use(state, Paid(state).Key);
        Assert.False(started.State.IsUpright("actor"));
        Assert.Equal(0, Coal(started.State));
        Assert.NotNull(started.State.DoorInProgress);
        Assert.True(started.Events.FindIndex(e => e.Kind == "PostureChanged") < started.Events.FindIndex(e => e.Kind == "ResourceSpent"));
        var restored = Restore(started.State);
        GameEngine.ValidateScenario(restored);
        var actual = Use(restored, "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(Use(started.State, "roll-dice")), JsonSerializer.Serialize(actual));
        Assert.Single(actual.State.Physical.Board.Edges.Where(e => e.Kind == EdgeKind.ClosedDoor));
        Assert.DoesNotContain(actual.Events, e => e.Kind is "ResourceSpent" or "DoorOpened");
        Assert.Equal(0, Coal(actual.State));
    }

    [Fact]
    public void RepeatedCostsReserveCumulativeResourcesAndEmitInAuthoredOrder()
    {
        var costs = ImmutableArray.Create<AbilityCost>(new SpendResource("coal", 1), new SpendResource("diamond", 2), new SpendResource("coal", 2));
        var state = Ready(new OpenDoor(), costs);
        var result = Use(state, Paid(state).Key);
        var payments = result.Events.Where(e => e.Kind == "ResourceSpent").ToArray();
        Assert.Equal(new[] { "coal", "diamond", "coal" }, payments.Select(e => e.ResourceType!.Id));
        Assert.Equal(new int?[] { 2, 1, 0 }, payments.Select(e => e.StoredQuantity));
        Assert.Single(result.State.Physical.Board.Edges.Where(e => e.Kind == EdgeKind.OpenDoor));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Ability?.Key == "paid");
        foreach (var payment in payments)
        {
            var snapshot = result.ResolutionSteps.Single(s => s.EventIndex == result.Events.IndexOf(payment)).StateAfter;
            Assert.Equal(payment.StoredQuantity, snapshot.StoredQuantity("blue", "payer", payment.ResourceType!.Id));
        }
        var insufficient = Ready(new OpenDoor(), costs, coal: 2);
        Assert.DoesNotContain(insufficient.Pending!.Candidates, c => c.Ability?.Key == "paid");
        Assert.Equal(2, Coal(insufficient));
        Assert.Equal(3, insufficient.StoredQuantity("blue", "payer", "diamond"));
    }

    [Fact]
    public void StaleUnaffordableSelectionAndEndTurnDoNotPayAnyPartialCost()
    {
        var state = Ready(new OpenDoor(), [new LayDownSelf(), new SpendResource("coal", 2), new SpendResource("diamond", 3)], AbilitySourceKind.Item);
        var stale = Paid(state).Key;
        var index = state.StoredResources.FindIndex(r => r.Type.Id == "diamond");
        state.StoredResources[index] = state.StoredResources[index] with { Quantity = 2 };
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => Use(state, stale));
        Assert.Throws<ArgumentException>(() => Use(state, "unknown-choice"));
        Assert.Throws<ArgumentException>(() => Use(state, null));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.True(state.IsUpright("actor"));
        Assert.Single(state.Units.Single(u => u.Id == "actor").Inventory);
        state.MoveDone = true;
        var skipped = Use(Refresh(state), "end-turn");
        Assert.Equal(3, Coal(skipped.State));
        Assert.DoesNotContain(skipped.Events, e => e.Kind is "ResourceSpent" or "ItemUsed" or "PostureChanged");
    }

    [Theory]
    [InlineData("summon", AbilitySourceKind.Unit)]
    [InlineData("troll", AbilitySourceKind.Unit)]
    [InlineData("banana", AbilitySourceKind.Unit)]
    [InlineData("shell", AbilitySourceKind.Unit)]
    [InlineData("shell", AbilitySourceKind.Item)]
    public void FinalUseCompletesAfterPostureAndLastDeclaredResourcePayment(string kind, AbilitySourceKind source)
    {
        AbilityMechanic mechanic = kind switch
        {
            "summon" => new AdjacentUnitSummonMechanic("spawn", Posture.Lying),
            "troll" => new AdjacentUnitSummonMechanic(UnitTypeIds.Troll, Posture.Upright), "banana" => new PlaceBananaPeel(),
            _ => new TargetUnitAttackMechanic(4, 3)
        };
        var resourceId = kind switch { "banana" => "banana", "shell" => "shell", _ => "diamond" };
        var timing = kind == "banana" ? AbilityTiming.BonusAction : AbilityTiming.Action;
        var state = Ready(mechanic, [new SpendResource(resourceId, 1), new LayDownSelf()], source, timing,
            diamond: 1, shell: 1, banana: 1);
        var candidate = Paid(state);
        var started = Use(state, candidate.Key);
        var result = kind == "shell" ? Use(Restore(started.State), "roll-dice") : started;
        Assert.False(result.State.IsUpright("actor"));
        Assert.Equal(0, result.State.StoredQuantity("blue", "payer", resourceId));
        if (source == AbilitySourceKind.Item)
        {
            Assert.Empty(result.State.Units.Single(u => u.Id == "actor").Inventory);
            Assert.Equal("physical-source", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        }
        else Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", candidate.Ability!));
        if (kind == "banana") Assert.Single(result.State.CellMarkers);
        else if (kind == "shell") Assert.Contains(result.Events, e => e.Kind == "AttackTargetResolved");
        else Assert.Contains(result.State.Units, u => u.SideId == "blue" && u.Id != "actor");
        if (kind != "summon")
        {
            var insufficient = Ready(mechanic, [new SpendResource(resourceId, 1)], timing: timing, diamond: 0, shell: 0, banana: 0);
            Assert.DoesNotContain(insufficient.Pending!.Candidates, c => c.Ability?.Key == "paid");
            Assert.Equal(0, insufficient.StoredQuantity("blue", "payer", resourceId));
        }
    }

    [Fact]
    public void HiddenEquipmentPaymentDoesNotRevealStorageSourceOrPosture()
    {
        var state = Ready(new OpenDoor(), [new SpendResource("coal", 3), new LayDownSelf()], AbilitySourceKind.Equipment);
        for (var y = 0; y < state.Physical.Board.Height; y++)
            state.Physical.Board.Edges.Add(new(new(2, y), new(3, y), EdgeKind.Wall));
        state.FogOfWarSides = ["red"];
        state.ExploredCells["red"] = [];
        state = Refresh(state);
        Assert.False(FogOfWar.IsUnitObservable(state, "red", "actor"));
        var result = Use(state, Paid(state).Key);
        var payments = result.Events.Where(e => e.Kind is "ResourceSpent" or "PostureChanged").ToArray();
        Assert.Equal(2, payments.Length);
        foreach (var payment in payments)
        {
            var snapshot = result.ResolutionSteps.Single(s => s.EventIndex == result.Events.IndexOf(payment)).StateAfter;
            Assert.Null(FogOfWar.ObserveEvent(snapshot, "red", payment));
            var observed = FogOfWar.Observe(snapshot, "red");
            Assert.DoesNotContain(observed.Units, u => u.Id == "actor");
            Assert.DoesNotContain(observed.StoredResources, r => r.SideId == "blue");
            Assert.DoesNotContain("physical-source", JsonSerializer.Serialize(observed));
        }
    }

    [Fact]
    public void PaymentKeepsStoredPayloadAndDoesNotBorrowAnotherSidesOrTypesStorage()
    {
        var state = Ready(new OpenDoor(), [new SpendResource("coal", 3)], AbilitySourceKind.Equipment);
        var ownIndex = state.StoredResources.FindIndex(r => r.SideId == "blue" && r.UnitTypeId == "payer" && r.Type.Id == "coal");
        var ownedCoal = new ResourceType("coal", "Owned Coal");
        state.StoredResources[ownIndex] = state.StoredResources[ownIndex] with { Type = ownedCoal };
        var payment = Assert.Single(Use(state, Paid(state).Key).Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.Equal(ownedCoal, payment.ResourceType);

        // Other storage belongs to real authored activation groups and capacities.
        var payer = state.Types.Single(t => t.Id == "payer");
        var otherType = new UnitType("other-payer", 0, 0, 0, 0, 1)
            { ResourceStorage = payer.ResourceStorage };
        state.Types.Add(otherType);
        state.Controllers.Add(new(new("payer", "red"), ControllerKind.Human));
        state.Controllers.Add(new(new(otherType.Id, "blue"), ControllerKind.Human));
        state.StoredResources.Add(new("red", "payer", ResourceTypes.Coal, 6));
        state.StoredResources.Add(new("blue", otherType.Id, ResourceTypes.Coal, 6));
        state.StoredResources[ownIndex] = state.StoredResources[ownIndex] with { Quantity = 2 };
        state = Refresh(state);
        GameEngine.ValidateScenario(state);
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Key == "paid");
        Assert.Equal(2, Coal(state));
        Assert.Equal(6, state.StoredQuantity("red", "payer", "coal"));
        Assert.Equal(6, state.StoredQuantity("blue", otherType.Id, "coal"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaidBreathAndClawKeepTheirBoundItemAfterLayingDown(bool claw)
    {
        AbilityMechanic mechanic = claw ? new ClawAttack(1) : new FireBreath();
        var state = Ready(mechanic, [new LayDownSelf(), new SpendResource("coal", 3)],
            AbilitySourceKind.Item, AbilityTiming.Action);
        if (claw)
        {
            var index = state.Physical.Figures.FindIndex(f => f.Id == "enemy");
            state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = new(2, 1) };
            state = Refresh(state);
        }
        var started = Use(state, Paid(state).Key);
        Assert.False(started.State.IsUpright("actor"));
        Assert.Empty(started.State.Units.Single(u => u.Id == "actor").Inventory);
        Assert.Equal(0, Coal(started.State));
        var resumed = Use(Restore(started.State), "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(Use(started.State, "roll-dice")), JsonSerializer.Serialize(resumed));
        Assert.Contains(resumed.Events, e => e.Kind == "AttackTargetResolved" && e.TargetId == "enemy");
        Assert.DoesNotContain(resumed.Events, e => e.Kind is "ResourceSpent" or "ItemUsed");
    }

    [Fact]
    public void PaidExtraMovesPaysBothDeclaredMushroomCostsBeforeApplyingMove()
    {
        var state = Ready(new SelfExtraMovesMechanic(1), [new SpendResource("mushroom", 1), new SpendResource("mushroom", 1), new LayDownSelf()],
            AbilitySourceKind.Item, AbilityTiming.BonusAction);
        var result = Use(state, Paid(state).Key);
        Assert.Equal(0, result.State.StoredQuantity("blue", "payer", "mushroom"));
        Assert.Equal("physical-source", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        var payments = result.Events.Where(e => e.Kind == "ResourceSpent").ToArray();
        Assert.Equal(new int?[] { 1, 0 }, payments.Select(e => e.StoredQuantity));
        var paid = result.ResolutionSteps.Single(s => s.EventIndex == result.Events.IndexOf(payments[1])).StateAfter;
        Assert.Equal(0, paid.MoveOpportunityModifierThisActivation);
        var applied = result.ResolutionSteps.Single(s => result.Events[s.EventIndex] is { Kind: "ActionUsed", Category: ActivationChoiceKind.BonusAction }).StateAfter;
        Assert.Equal(1, applied.MoveOpportunityModifierThisActivation);
        Assert.False(applied.IsUpright("actor"));
        var index = state.StoredResources.FindIndex(r => r.Type.Id == "mushroom");
        state.StoredResources[index] = state.StoredResources[index] with { Quantity = 1 };
        var insufficient = Refresh(state);
        Assert.DoesNotContain(insufficient.Pending!.Candidates, c => c.Ability?.Key == "paid");
        Assert.Equal(1, insufficient.StoredQuantity("blue", "payer", "mushroom"));
    }

    [Theory]
    [InlineData("pickup")]
    [InlineData("give")]
    [InlineData("operate")]
    public void PaidResourceOperationFinishesWithConsumedItemAndLyingActor(string kind)
    {
        AbilityMechanic mechanic = kind switch
        { "pickup" => new PickupResource(), "give" => new GiveResource(), _ => new Operate() };
        var state = Ready(mechanic, [new SpendResource("coal", 3), new LayDownSelf()],
            AbilitySourceKind.Item, AbilityTiming.Action);
        if (kind == "operate")
            state.Physical.Board.Machines.Add(new("press", new(2, 1), MachineTypes.DiamondPress)
                { StoredResources = [new(ResourceTypes.Coal, 3)] });
        else state.Resources.Add(new(ResourceTypes.Coal, new(1, 1)));
        if (kind == "give")
        {
            var index = state.Units.FindIndex(u => u.Id == "enemy");
            state.Units[index] = state.Units[index] with { SideId = "blue" };
            var figure = state.Physical.Figures.FindIndex(f => f.Id == "enemy");
            state.Physical.Figures[figure] = state.Physical.Figures[figure] with { Position = new(2, 1) };
            var type = state.Types.FindIndex(t => t.Id == "target");
            state.Types[type] = state.Types[type] with { ResourceStorage = new([new(ResourceTypes.Coal, 1)]) };
            state.Controllers.Add(new(new("target", "blue"), ControllerKind.Human));
        }
        state = Refresh(state);
        GameEngine.ValidateScenario(state);
        var result = Use(state, Paid(state).Key);
        Assert.False(result.State.IsUpright("actor"));
        Assert.Empty(result.State.Units.Single(u => u.Id == "actor").Inventory);
        Assert.Equal("physical-source", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.Single(result.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.Equal(kind == "pickup" ? 1 : 0, Coal(result.State));
        if (kind == "operate")
        {
            Assert.Equal(0, Assert.Single(result.State.Physical.Board.Machines).StoredQuantity("coal"));
            Assert.Equal(new Resource(ResourceTypes.Diamond, new(1, 1)), Assert.Single(result.State.Resources));
        }
        else
        {
            Assert.Empty(result.State.Resources);
            Assert.Contains(result.Events, e => e.Kind == "ResourceStored" && e.TargetId == (kind == "give" ? "enemy" : "actor"));
            if (kind == "give") Assert.Equal(1, result.State.StoredQuantity("blue", "target", "coal"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingOperationsRetainTheSelectedPhysicalItemAmongDuplicateCopies(bool door)
    {
        AbilityMechanic mechanic = door ? new TryOpenDoor(0) : new FireballMechanic();
        var state = Ready(mechanic, [new SpendResource("coal", 3), new LayDownSelf()],
            AbilitySourceKind.Item, AbilityTiming.Action);
        var actorIndex = state.Units.FindIndex(u => u.Id == "actor");
        state.Units[actorIndex] = state.Units[actorIndex] with
        { Inventory = state.Units[actorIndex].Inventory.Add(new("spare-source") { ItemTypeId = "paid-item" }) };
        state = Refresh(state);
        var choice = state.Pending!.Candidates.First(c => c.Ability?.SourceInstanceId == "physical-source" &&
            (door || c.Destination == new Cell(4, 1)));
        var started = Use(state, choice.Key);
        var source = new AbilitySourceReference(AbilitySourceKind.Item, "paid-item", "physical-source", "paid");
        Assert.Equal(source, door ? started.State.DoorInProgress!.AbilitySource : started.State.AttackInProgress!.AbilitySource);
        Assert.Equal(source, started.NextInput!.Roll!.AbilitySource);
        Assert.Equal("spare-source", Assert.Single(started.State.Units.Single(u => u.Id == "actor").Inventory).Id);
        var saved = Restore(started.State);
        GameEngine.ValidateScenario(saved);
        Assert.Equal(source, door ? saved.DoorInProgress!.AbilitySource : saved.AttackInProgress!.AbilitySource);
        var resumed = Use(saved, "roll-dice");
        Assert.Contains(resumed.Events, e => e.Kind == "DiceRolled" && e.AbilitySource == source);
        Assert.Equal(source, Assert.Single(resumed.Events.Where(e => e.Kind == "DiceRolled")).Dice!.Pool.AbilitySource);
        Assert.DoesNotContain(resumed.Events, e => e.Kind is "ResourceSpent" or "ItemUsed");

        if (!door)
        {
            // Exercise the partial-observation branch: the recipient is known,
            // while its attack source and physical Item identity are not.
            saved.FogOfWarSides = ["red"];
            saved.ExploredCells["red"] = [new(4, 1)];
            var observed = FogOfWar.Observe(saved, "red");
            Assert.NotNull(observed.AttackInProgress);
            Assert.Null(observed.AttackInProgress!.AbilitySource);
            Assert.Null(FogOfWar.ObserveDecision(saved, "red", saved.Pending));
            var observedEvent = FogOfWar.ObserveEvent(saved, "red", started.Events.Single(e => e.Kind == "AttackStarted"));
            Assert.NotNull(observedEvent);
            Assert.Null(observedEvent!.AbilitySource);
            Assert.Null(observedEvent.AttackContext!.AbilitySource);
            Assert.DoesNotContain("physical-source", JsonSerializer.Serialize(observed));
        }
    }

    private sealed class Choice(string? key) : IDecisionProvider
    { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }
}
