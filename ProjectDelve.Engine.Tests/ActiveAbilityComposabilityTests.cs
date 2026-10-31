using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ActiveAbilityComposabilityTests
{
    public static TheoryData<AbilitySourceKind, AbilityTiming> FireballContexts => new()
    {
        { AbilitySourceKind.Unit, AbilityTiming.Action }, { AbilitySourceKind.Unit, AbilityTiming.BonusAction },
        { AbilitySourceKind.Unit, AbilityTiming.FreeAction }, { AbilitySourceKind.Equipment, AbilityTiming.Action },
        { AbilitySourceKind.Equipment, AbilityTiming.BonusAction }, { AbilitySourceKind.Equipment, AbilityTiming.FreeAction },
        { AbilitySourceKind.Item, AbilityTiming.Action }, { AbilitySourceKind.Item, AbilityTiming.BonusAction },
        { AbilitySourceKind.Item, AbilityTiming.FreeAction }
    };

    public static TheoryData<AbilitySourceKind, AbilityTiming> DoorContexts => new()
    {
        { AbilitySourceKind.Unit, AbilityTiming.Action }, { AbilitySourceKind.Unit, AbilityTiming.FreeAction },
        { AbilitySourceKind.Equipment, AbilityTiming.Action }, { AbilitySourceKind.Equipment, AbilityTiming.FreeAction },
        { AbilitySourceKind.Item, AbilityTiming.Action }, { AbilitySourceKind.Item, AbilityTiming.FreeAction }
    };

    private static GameState Ready(AbilitySourceKind source, AbilityTiming timing, AbilityMechanic mechanic,
        int? maxUses = 2, params LocalAbilityDefinition[] native)
    {
        var ability = new LocalAbilityDefinition("first", "Unfamiliar Name", timing, mechanic)
        { Usage = new(source == AbilitySourceKind.Item ? null : maxUses) };
        var actor = new UnitType("actor-type", 2, 4, 2, 0, 5, Actions: UnitAction.None)
        {
            Unique = true, CanHaveEquipment = true, CanHaveItems = true,
            // Keep a second legal choice after an Action so the engine's ordinary
            // forced End Turn does not erase the activation being asserted.
            LocalAbilities = source == AbilitySourceKind.Unit
                ? [ability, .. native, new("reserve", "Reserve Guard", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
                : [.. native, new("reserve", "Reserve Guard", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
        };
        var inventory = source switch
        {
            AbilitySourceKind.Equipment => new[] { new OwnedObject("physical-source", "local-equipment") },
            AbilitySourceKind.Item => [new OwnedObject("physical-source") { ItemTypeId = "local-item" }],
            _ => []
        };
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 4,
                Edges = [new(new(1, 0), EdgeDirection.Down, EdgeKind.ClosedDoor), new(new(0, 1), EdgeDirection.Right, EdgeKind.ClosedDoor)] },
            UnitTypeIds = [actor.Id, "near-type", "far-type"],
            UnitTypes = [actor, new("near-type", 0, 1, 0, 0, 8) { Unique = true }, new("far-type", 0, 1, 0, 0, 8) { Unique = true }],
            EquipmentTypes = source == AbilitySourceKind.Equipment
                ? [new("local-equipment", "Equipment") { LocalAbilities = [ability] }] : [],
            ItemTypes = source == AbilitySourceKind.Item
                ? [new("local-item", "Item") { LocalAbilities = [ability] }] : [],
            Units = [new(actor.Id, "blue", new(1, 1), UnitId: "actor") { Inventory = [.. inventory] },
                new("near-type", "red", new(2, 2), UnitId: "near"), new("far-type", "red", new(4, 1), UnitId: "far")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new("near-type", "red", ControllerKind.Human),
                new("far-type", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "actor";
        state.Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false);
        return Refresh(state);
    }

    private static GameState Refresh(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false).State;
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static Candidate Fireball(GameState state) => state.Pending!.Candidates.Single(c => c.Ability?.Key == "first" && c.Destination == new Cell(4, 1));

    [Theory, MemberData(nameof(FireballContexts))]
    public void FireballUsesItsAuthoredTimingAndSourceThroughDiceResume(AbilitySourceKind source, AbilityTiming timing)
    {
        var state = Ready(source, timing, new FireballMechanic());
        if (timing == AbilityTiming.Action)
        {
            Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Key == "first");
            state = Use(state, "stay").State;
        }
        var candidate = Fireball(state);
        var started = Use(state, candidate.Key);
        Assert.Equal(timing, started.State.AttackInProgress!.OriginTiming);
        Assert.Equal(candidate.Kind, started.Events.Single(e => e.Kind == "ActionUsed").Category);
        Assert.Equal(timing == AbilityTiming.Action ? 1 : 0, started.State.ActionsUsedThisActivation);
        Assert.Equal(timing == AbilityTiming.BonusAction ? 1 : 0,
            started.State.BonusActionUsesThisActivation(candidate.Ability!.AccountingId));
        if (source == AbilitySourceKind.Item)
        {
            Assert.Empty(started.State.Units.Single(u => u.Id == "actor").Inventory);
            Assert.Equal("physical-source", Assert.Single(started.State.LootDeck!.DiscardPile).Id);
            Assert.Equal(candidate.Kind, started.Events.Single(e => e.Kind == "ItemUsed").Category);
        }
        else Assert.Equal(new AbilityUses(2, 1), started.State.AbilityUsesOf("actor", candidate.Ability));

        var restored = Restore(started.State);
        GameEngine.ValidateScenario(restored);
        var continued = Use(started.State, "roll-dice");
        var resumed = Use(restored, "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(resumed));
        Assert.Equal(6, resumed.State.Units.Single(u => u.Id == "far").CurrentHp);
        Assert.Equal(timing == AbilityTiming.Action, resumed.State.MoveDone);
        Assert.Equal(timing != AbilityTiming.Action, resumed.NextInput!.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay));
    }

    [Theory]
    [InlineData(AbilityTiming.BonusAction)]
    [InlineData(AbilityTiming.FreeAction)]
    public void FireballRemainsAvailableAfterOrdinaryActionBudgetIsSpent(AbilityTiming timing)
    {
        var state = Ready(AbilitySourceKind.Unit, timing, new FireballMechanic());
        state.MoveDone = true; state.ActionsUsedThisActivation = 1;
        state = Refresh(state);
        var result = Use(state, Fireball(state).Key);
        Assert.Equal(1, result.State.ActionsUsedThisActivation);
        Assert.Equal(timing, result.State.AttackInProgress!.OriginTiming);
    }

    [Theory]
    [InlineData(AbilityTiming.Action, true)]
    [InlineData(AbilityTiming.BonusAction, false)]
    [InlineData(AbilityTiming.FreeAction, false)]
    public void CleaveFollowsEveryQualifyingAttackButMoveAfterAttackRequiresAction(AbilityTiming timing, bool moveExpected)
    {
        var state = Ready(AbilitySourceKind.Unit, timing, new FireballMechanic(), 2,
            new("cleave", "Follow-up", AbilityTiming.AfterAttack, new Cleave { TriggerDamage = 2, Damage = 1 }),
            new("move", "After Action", AbilityTiming.AfterAttack, new MoveAfterAttack(1)));
        if (timing == AbilityTiming.Action) state = Use(state, "stay").State;
        var started = Use(state, Fireball(state).Key);
        var attacked = Use(Restore(started.State), "roll-dice");
        Assert.Equal(DecisionKind.Cleave, attacked.NextInput!.Kind);
        Assert.Equal(moveExpected, attacked.State.MoveAfterAttackAllowance is not null);
        var cleaved = Use(attacked.State, attacked.NextInput.Candidates.Single(c => c.TargetId == "near").Key);
        Assert.Equal(7, cleaved.State.Units.Single(u => u.Id == "near").CurrentHp);
        Assert.Equal(moveExpected ? DecisionKind.Move : DecisionKind.Activation, cleaved.NextInput!.Kind);
    }

    [Fact]
    public void UnlimitedFreeFireballCanRepeatWithoutAnArtificialUsageCap()
    {
        var state = Ready(AbilitySourceKind.Unit, AbilityTiming.FreeAction, new FireballMechanic(), null);
        for (var use = 0; use < 2; use++)
            state = Use(Use(state, Fireball(state).Key).State, "roll-dice").State;
        Assert.NotNull(Fireball(state));
        Assert.Equal(0, state.ActionsUsedThisActivation);
        Assert.Empty(state.Units.Single(u => u.Id == "actor").AbilityUseCounts);
        Assert.Equal(4, state.Units.Single(u => u.Id == "far").CurrentHp);
    }

    [Fact]
    public void UnlimitedCleaveCanFollowRepeatedFreeAttacksWithoutAnActivationUseCap()
    {
        var state = Ready(AbilitySourceKind.Unit, AbilityTiming.FreeAction, new FireballMechanic(), null,
            new LocalAbilityDefinition("cleave", "Follow-up", AbilityTiming.AfterAttack, new Cleave { TriggerDamage = 2, Damage = 1 }));
        for (var use = 0; use < 2; use++)
        {
            state = Use(Use(state, Fireball(state).Key).State, "roll-dice").State;
            Assert.Equal(DecisionKind.Cleave, state.Pending!.Kind);
            state = Use(state, state.Pending.Candidates.Single(c => c.TargetId == "near").Key).State;
        }
        Assert.Equal(6, state.Units.Single(u => u.Id == "near").CurrentHp);
        Assert.Empty(state.Units.Single(u => u.Id == "actor").AbilityUseCounts);
    }

    [Fact]
    public void ExistingAutomationCanFinishWithAnUnlimitedFreeAttackStillLegal()
    {
        var state = Ready(AbilitySourceKind.Unit, AbilityTiming.FreeAction, new FireballMechanic(), null);
        state = Use(state, "stay").State;
        Assert.NotNull(Fireball(state));
        Assert.Equal("end-turn", new DefaultAutomatedProvider().Choose(state.Pending!, new GameplayQueries(state)));
    }

    [Fact]
    public void ExistingAutomationRecognizesAnActionDoorFromAnItem()
    {
        var state = Ready(AbilitySourceKind.Item, AbilityTiming.Action, new OpenDoor());
        state = Use(state, "stay").State;
        var key = new DefaultAutomatedProvider().Choose(state.Pending!, new GameplayQueries(state));
        Assert.IsType<OpenDoor>(state.Pending!.Candidates.Single(c => c.Key == key).Ability!.Definition.Mechanic);
        var used = Use(state, key!);
        Assert.Single(used.Events.Where(e => e.Kind == "DoorOpened"));
        Assert.Empty(used.State.Units.Single(u => u.Id == "actor").Inventory);
    }

    [Theory, MemberData(nameof(DoorContexts))]
    public void OpenDoorUsesActionOrFreeTimingAndExhaustsOnlyTheSelectedSource(AbilitySourceKind source, AbilityTiming timing)
    {
        var state = Ready(source, timing, new OpenDoor(), 1);
        Assert.True(new GameplayQueries(state).CanInteractWithDoors("actor"));
        if (timing == AbilityTiming.Action)
        {
            Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is OpenDoor);
            state = Use(state, "stay").State;
        }
        var candidate = state.Pending!.Candidates.First(c => c.Ability?.Definition.Mechanic is OpenDoor);
        var result = Use(state, candidate.Key);
        Assert.Single(result.Events.Where(e => e.Kind == "DoorOpened"));
        Assert.Equal(timing == AbilityTiming.Action ? 1 : 0, result.State.ActionsUsedThisActivation);
        Assert.Equal(candidate.Kind, result.Events.Single(e => e.Kind == "ActionUsed").Category);
        Assert.False(result.State.HasOpenDoor("actor"));
        Assert.False(new GameplayQueries(result.State).CanInteractWithDoors("actor"));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Ability?.Definition.Mechanic is OpenDoor);
    }

    public static TheoryData<AbilitySourceKind, string> SpecializedContexts => new()
    {
        { AbilitySourceKind.Unit, "telekinesis" }, { AbilitySourceKind.Equipment, "telekinesis" }, { AbilitySourceKind.Item, "telekinesis" },
        { AbilitySourceKind.Unit, "tryOpenDoor" }, { AbilitySourceKind.Equipment, "tryOpenDoor" }, { AbilitySourceKind.Item, "tryOpenDoor" },
        { AbilitySourceKind.Unit, "swap" }, { AbilitySourceKind.Equipment, "swap" }, { AbilitySourceKind.Item, "swap" },
        { AbilitySourceKind.Unit, "displace" }, { AbilitySourceKind.Equipment, "displace" }, { AbilitySourceKind.Item, "displace" },
        { AbilitySourceKind.Unit, "reshapeRoom" }, { AbilitySourceKind.Equipment, "reshapeRoom" }, { AbilitySourceKind.Item, "reshapeRoom" }
    };

    [Theory, MemberData(nameof(SpecializedContexts))]
    public void SelectedSpecializedMechanicsExecuteTheirLastUseFromEverySource(AbilitySourceKind source, string mechanicId)
    {
        AbilityMechanic mechanic = mechanicId switch
        {
            "telekinesis" => new Telekinesis(), "tryOpenDoor" => new TryOpenDoor(6),
            "swap" => new Swap(), "displace" => new Displace(1), "reshapeRoom" => new ReshapeRoom(),
            _ => throw new InvalidOperationException()
        };
        var timing = mechanic is Telekinesis or TryOpenDoor ? AbilityTiming.Action : AbilityTiming.BonusAction;
        var state = Ready(source, timing, mechanic, 1);
        if (timing == AbilityTiming.Action) state = Use(state, "stay").State;
        var candidate = state.Pending!.Candidates.First(c => c.Ability?.Key == "first" &&
            (mechanic is not Displace || c.Destination != new Cell(2, 2)));
        var result = Use(state, candidate.Key);
        if (mechanic is TryOpenDoor) result = Use(Restore(result.State), "roll-dice");
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Ability?.Key == "first");
        if (source == AbilitySourceKind.Item)
            Assert.Equal("physical-source", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        else Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", candidate.Ability!));
        switch (mechanic)
        {
            case Telekinesis: Assert.False(result.State.IsUpright(candidate.TargetId!)); break;
            case TryOpenDoor: Assert.Single(result.Events.Where(e => e.Kind == "DoorOpened")); break;
            case Swap: Assert.Equal(new Cell(2, 2), result.State.Physical.Figures.Single(f => f.Id == "actor").Position); break;
            case Displace: Assert.Equal(candidate.Destination, result.State.Physical.Figures.Single(f => f.Id == candidate.TargetId).Position); break;
            case ReshapeRoom: Assert.DoesNotContain(result.State.Physical.Board.Edges, e => e.Kind == EdgeKind.ClosedDoor); break;
        }
    }

    [Fact]
    public void IndependentDoorAndBonusDeclarationsRetainTheirOwnParametersAndCounters()
    {
        var state = Ready(AbilitySourceKind.Unit, AbilityTiming.Action, new TryOpenDoor(0), 1,
            new("second", "Same Name", AbilityTiming.Action, new TryOpenDoor(6)) { Usage = new(1) },
            new("swap-one", "Same Name", AbilityTiming.BonusAction, new Swap()) { Usage = new(1) },
            new("swap-two", "Same Name", AbilityTiming.BonusAction, new Swap()) { Usage = new(1) });
        var firstSwap = state.Pending!.Candidates.Single(c => c.Ability?.Key == "swap-one");
        state = Use(state, firstSwap.Key).State;
        var secondSwap = state.Pending!.Candidates.Single(c => c.Ability?.Key == "swap-two");
        state = Use(state, secondSwap.Key).State;
        Assert.Equal(new Cell(1, 1), state.Physical.Figures.Single(f => f.Id == "actor").Position);
        state = Use(state, "stay").State;
        state.ExtraActionsThisActivation = 1;
        state = Refresh(state);
        var firstDoor = state.Pending!.Candidates.First(c => c.Ability?.Key == "first");
        state = Use(Use(state, firstDoor.Key).State, "roll-dice").State;
        Assert.Equal(2, state.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.ClosedDoor));
        var secondDoor = state.Pending!.Candidates.First(c => c.Ability?.Key == "second");
        state = Use(Use(state, secondDoor.Key).State, "roll-dice").State;
        Assert.Single(state.Physical.Board.Edges.Where(e => e.Kind == EdgeKind.ClosedDoor));
        Assert.Equal(4, state.Units.Single(u => u.Id == "actor").AbilityUseCounts.Count);
    }

    [Theory]
    [InlineData("summon")]
    [InlineData("pickup")]
    [InlineData("banana")]
    [InlineData("boost")]
    [InlineData("shell")]
    public void NativeResourceAndSummonMechanicsFinishTheirLastUseBeforeBecomingUnavailable(string kind)
    {
        AbilityMechanic mechanic = kind switch
        {
            "summon" => new SummonAdjacent("near-type", Posture.Lying),
            "pickup" => new PickupResource(), "banana" => new PlaceBananaPeel(1),
            "boost" => new Boost(1), "shell" => new ShootShell(1, 4, 3),
            _ => throw new InvalidOperationException()
        };
        var timing = mechanic is PlaceBananaPeel or Boost ? AbilityTiming.BonusAction : AbilityTiming.Action;
        var state = Ready(AbilitySourceKind.Unit, timing, mechanic, 1);
        var resource = kind switch
        {
            "banana" => ResourceTypes.Banana, "boost" => ResourceTypes.Mushroom,
            "shell" => ResourceTypes.Shell, _ => ResourceTypes.Diamond
        };
        if (kind != "summon")
        {
            var typeIndex = state.Types.FindIndex(t => t.Id == "actor-type");
            state.Types[typeIndex] = state.Types[typeIndex] with { ResourceStorage = new([new(resource, 2)]) };
            if (kind == "pickup") state.Resources.Add(new(resource, new(1, 1)));
            else state.StoredResources.Add(new("blue", "actor-type", resource, 2));
        }
        if (timing == AbilityTiming.Action) state = Use(Refresh(state), "stay").State;
        state = Refresh(state);
        GameEngine.ValidateScenario(state);
        var candidate = state.Pending!.Candidates.First(c => c.Ability?.Key == "first");
        var result = Use(state, candidate.Key);
        if (kind == "shell") result = Use(Restore(result.State), "roll-dice");
        Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", candidate.Ability!));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Ability?.Key == "first");
        switch (kind)
        {
            case "summon":
                var summoned = Assert.Single(result.State.Units.Where(u => u.TypeId == "near-type" && u.SideId == "blue"));
                Assert.False(result.State.IsUpright(summoned.Id));
                break;
            case "pickup":
                Assert.Empty(result.State.Resources);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor-type", resource.Id));
                break;
            case "banana":
                Assert.Single(result.State.CellMarkers);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor-type", resource.Id));
                break;
            case "boost":
                Assert.Equal(1, result.State.MoveOpportunityModifierThisActivation);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor-type", resource.Id));
                break;
            case "shell":
                Assert.Equal(5, result.State.Units.Single(u => u.Id == candidate.TargetId).CurrentHp);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor-type", resource.Id));
                break;
        }
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
