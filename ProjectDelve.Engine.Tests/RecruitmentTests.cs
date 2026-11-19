using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RecruitmentTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Calls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Calls++; return bag[0]; }
        public AttackFace RollAttackDie() { Calls++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Calls++; return DefenceFace.Miss; }
        public int RollD6() { Calls++; return 1; }
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static ScenarioDefinition Fixture(int credits = 90, int capacity = 6) => new()
    {
        Board = new() { Width = 10, Height = 9 },
        UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Cleric, UnitTypeIds.Wizard, UnitTypeIds.Beastmaster],
        Units =
        [
            new(UnitTypeIds.Barbarian, "heroes", new(4, 4), UnitId: "operator"),
            new(UnitTypeIds.Rogue, "heroes", new(1, 1), UnitId: "rogue"),
            new(UnitTypeIds.Cleric, "heroes", new(2, 1), UnitId: "cleric"),
            new(UnitTypeIds.Wizard, "heroes", new(3, 1), UnitId: "wizard"),
            new(UnitTypeIds.Beastmaster, "heroes", new(4, 1), UnitId: "beastmaster")
        ],
        Agency =
        [
            new(UnitTypeIds.Barbarian, "heroes", ControllerKind.Human),
            new(UnitTypeIds.Rogue, "heroes", ControllerKind.Human),
            new(UnitTypeIds.Cleric, "heroes", ControllerKind.Human),
            new(UnitTypeIds.Wizard, "heroes", ControllerKind.Human),
            new(UnitTypeIds.Beastmaster, "heroes", ControllerKind.Human)
        ],
        Party = new("heroes", capacity, ["operator", "rogue", "cleric", "wizard", "beastmaster"]),
        SideCredits = new() { ["heroes"] = credits },
        Machines = [new("station", new(5, 4), MachineTypes.EquipmentStation)],
        Objectives = [new("heroes", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition? scene = null)
    {
        var state = GameEngine.CreateGame(scene ?? Fixture());
        state.Round = 1; state.RoundComplete = false; state.ActiveToken = state.Units[0].Token;
        state.CurrentUnitId = "operator"; state.MoveDone = true;
        state.ResolvingTokenUnitIds = ["operator"];
        state.Pending = new(DecisionKind.Activation, UnitTypeIds.Barbarian, "operator", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static GameState Open(GameState? state = null)
    {
        state ??= Ready();
        var action = state.Pending!.Candidates.First(c => c.Action == UnitAction.Operate && c.MachineId == "station");
        return GameEngine.Advance(state, new Choice(action.Key), new Dice(), false).State;
    }

    [Fact]
    public void TycoonHasTheAuthoredVanillaHeroConfigurationAndOnlyOneLevel()
    {
        var type = UnitRoster.Tycoon();
        Assert.Equal((4, 1, 2, 2, 5), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(75, type.RecruitmentCost);
        Assert.True(type.Unique && type.CanHaveEquipment && type.CanHaveItems);
        Assert.Equal(ControllerKind.Human, type.DefaultController);
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(new[] { "open-door", "operate" }, type.LocalAbilities.Select(a => a.Key));
        Assert.True(type.AdditionalLevels.IsDefaultOrEmpty);
        Assert.Empty(ContentDefinitions.Validate(new(type)));
        Assert.Null(UnitRoster.Beastmaster().RecruitmentCost);
    }

    [Fact]
    public void RecruitmentCreatesAStableHumanMemberSpendsOneActionAndNoRng()
    {
        var state = Open();
        var before = Json(state);
        var preview = GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon);
        Assert.True(preview.CanConfirm);
        Assert.Equal(5, preview.PartySize);
        Assert.Equal(6, preview.PartyCapacity);
        Assert.Equal(15, preview.ResultingCredits);
        Assert.NotNull(preview.SpawnCell);
        Assert.Equal(before, Json(state));
        var dice = new Dice();
        var result = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, dice, false);
        Assert.Equal(0, dice.Calls);
        Assert.Equal(6, result.State.Party!.MemberIds.Length);
        Assert.Equal(15, result.State.SideCredits["heroes"]);
        Assert.Equal(1, result.State.ActionsUsedThisActivation);
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
        var created = Assert.Single(result.Events, e => e.Kind == "UnitCreated");
        var recruit = result.State.Units.Single(u => u.Id == created.UnitId);
        Assert.Equal("tycoon-1", recruit.Id);
        Assert.Equal(ControllerKind.Human, result.State.Controllers.Single(c => c.Token == recruit.Token).Controller);
        Assert.Null(recruit.SummonerUnitId);
        Assert.Equal(1, recruit.Level);
        Assert.Equal(5, recruit.CurrentHp);
        Assert.Equal(preview.SpawnCell, result.State.Physical.Figures.Single(f => f.Id == recruit.Id).Position);
        Assert.Equal(recruit.Id, result.State.Party.MemberIds[^1]);
        Assert.Equal(before, Json(state));
        Assert.Throws<InvalidOperationException>(() => GameEngine.ConfirmRecruitment(result.State, UnitTypeIds.Tycoon, dice, false));
        GameEngine.ValidateRestoredState(result.State);
    }

    [Fact]
    public void InvalidOffersAndInsufficientFundsLeaveTheOpeningBoundaryUntouched()
    {
        foreach (var scene in new[] { Fixture(74), Fixture(capacity: 5), Fixture() with { Party = null } })
        {
            var state = Open(Ready(scene));
            var before = Json(state);
            Assert.False(GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).CanConfirm);
            Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false));
            Assert.Equal(before, Json(state));
        }
        var open = Open();
        Assert.Contains(GameEngine.PreviewRecruitment(open, UnitTypeIds.Barbarian).Diagnostics, d => d.Code == "unavailable-recruit");
        Assert.Equal(Json(open), Json(JsonSerializer.Deserialize<GameState>(Json(open))!));
    }

    [Fact]
    public void ExplicitZeroPriceAllowsPurchaseAndCancelPreservesEverything()
    {
        var scene = Fixture(0) with { UnitTypes = [UnitRoster.Tycoon() with { RecruitmentCost = 0 }] };
        var state = Open(Ready(scene));
        var cancel = GameEngine.CancelMachineInteraction(state, new Dice(), false).State;
        Assert.Equal(0, cancel.ActionsUsedThisActivation);
        Assert.Equal(5, cancel.Party!.MemberIds.Length);
        Assert.Equal(0, cancel.SideCredits["heroes"]);
        Assert.DoesNotContain(cancel.Units, u => u.TypeId == UnitTypeIds.Tycoon);
        var confirmed = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false).State;
        Assert.Equal(1, confirmed.ActionsUsedThisActivation);
        Assert.Equal(0, confirmed.SideCredits["heroes"]);
        Assert.Equal(6, confirmed.Party!.MemberIds.Length);
    }

    [Fact]
    public void PriceValidationAndLegacyJsonKeepMissingDistinctFromFree()
    {
        var type = UnitRoster.Tycoon();
        Assert.Contains(ContentDefinitions.Validate(new(type with { RecruitmentCost = -1 })), d => d.Path == "recruitmentCost");
        Assert.DoesNotContain("RecruitmentCost", Json(type with { RecruitmentCost = null }));
        Assert.Contains("RecruitmentCost", Json(type with { RecruitmentCost = 0 }));
        var source = Fixture() with { Party = null };
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(source));
        Assert.Null(restored.Party);
        Assert.Null(GameEngine.CreateGame(restored).Party);
        var withParty = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture()));
        Assert.Equal(Json(Fixture().Party), Json(GameEngine.CreateGame(withParty).Party));
    }

    [Fact]
    public void PartyDeclarationRejectsMissingDuplicateAndForeignIdentities()
    {
        var scene = Fixture();
        foreach (var roster in new ExpeditionPartyRoster[]
        {
            scene.Party! with { MemberIds = ["operator", "missing"] },
            scene.Party! with { MemberIds = ["operator", "operator"] },
            scene.Party! with { Capacity = 4 },
            scene.Party! with { SideId = "other" },
            scene.Party! with { MemberIds = default }
        })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene with { Party = roster }));
    }

    [Fact]
    public void DefeatedPermanentMemberStillBlocksUniqueRecruitment()
    {
        var scene = Fixture(capacity: 7);
        scene = scene with
        {
            UnitTypeIds = [.. scene.UnitTypeIds, UnitTypeIds.Tycoon],
            Units = [.. scene.Units, new(UnitTypeIds.Tycoon, "heroes", new(7, 7), UnitId: "old-tycoon")],
            Agency = [.. scene.Agency, new(UnitTypeIds.Tycoon, "heroes", ControllerKind.Human)],
            Party = scene.Party! with { MemberIds = scene.Party.MemberIds.Add("old-tycoon") }
        };
        var state = Ready(scene);
        state.Units[^1] = state.Units[^1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "old-tycoon");
        state.DefeatedUnitIds.Add("old-tycoon");
        state = Open(state);
        Assert.Contains(GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).Diagnostics, d => d.Code == "unique-recruit");
        Assert.Contains("old-tycoon", state.Party!.MemberIds);
    }

    [Fact]
    public void TemporaryUniqueAllyBlocksWhileAliveAndNeverBecomesAPartyMember()
    {
        var scene = Fixture();
        scene = scene with
        {
            UnitTypeIds = [.. scene.UnitTypeIds, UnitTypeIds.Tycoon],
            Units = [.. scene.Units, new(UnitTypeIds.Tycoon, "heroes", new(7, 7), UnitId: "temporary")],
            Agency = [.. scene.Agency, new(UnitTypeIds.Tycoon, "heroes", ControllerKind.Human)]
        };
        var state = Ready(scene);
        state.Units[^1] = state.Units[^1] with { SummonerUnitId = "operator" };
        Assert.DoesNotContain("temporary", state.Party!.MemberIds);
        state = Open(state);
        Assert.Contains(GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).Diagnostics, d => d.Code == "unique-recruit");
        state = Ready(scene);
        state.Units[^1] = state.Units[^1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "temporary");
        state.DefeatedUnitIds.Add("temporary");
        state = Open(state);
        Assert.True(GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).CanConfirm);
        Assert.DoesNotContain("temporary", state.Party!.MemberIds);
    }

    [Fact]
    public void PermanentSelectorCountsRecruitsAndExcludesTemporaryAllies()
    {
        var initial = Fixture() with { Objectives = [new("heroes", ScenarioObjective.ReachArea)
            { Count = 2, Area = new(0, 5, 3, 3), Selectors = [UnitSelector.SideAtStart("heroes")] }] };
        var state = GameEngine.ConfirmRecruitment(Open(Ready(initial)), UnitTypeIds.Tycoon, new Dice(), false).State;
        var recruitId = state.Party!.MemberIds[^1];
        var objective = new SideObjective("heroes", ScenarioObjective.ReachArea)
            { Count = 2, Area = new(0, 5, 3, 3), Selectors = [UnitSelector.Party("heroes")] };
        var indices = new[] { "operator", recruitId }.Select(id => state.Physical.Figures.FindIndex(f => f.Id == id)).ToArray();
        state.Physical.Figures[indices[0]] = state.Physical.Figures[indices[0]] with { Position = new(0, 5) };
        state.Physical.Figures[indices[1]] = state.Physical.Figures[indices[1]] with { Position = new(1, 5) };
        Assert.Equal(2, GameEngine.ObjectiveQualifiedCount(state, objective));
        state.Party = state.Party with { MemberIds = state.Party.MemberIds.Remove(recruitId) };
        Assert.Equal(1, GameEngine.ObjectiveQualifiedCount(state, objective));
        Assert.Equal(5, state.ObjectiveStartUnitIds.GetValueOrDefault("heroes", []).Length);
    }

    [Fact]
    public void SerializedConfirmReplaysTheSameStateEventsAndIdentityWithoutRng()
    {
        var state = Open();
        var first = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false);
        var second = GameEngine.ConfirmRecruitment(JsonSerializer.Deserialize<GameState>(Json(state))!, UnitTypeIds.Tycoon, new Dice(), false);
        Assert.Equal(Json(first), Json(second));
        var observed = FogOfWar.Observe(first.State, "other");
        Assert.Null(observed.Party);
        var audit = Assert.Single(first.Events, e => e.Kind == "MachineOperated");
        var hidden = FogOfWar.ObserveEvent(first.State, "other", audit)!;
        Assert.Null(hidden.RecruitedUnitId);
        Assert.Null(hidden.RecruitmentCost);
        Assert.Null(hidden.CreditsAmount);
        Assert.Null(hidden.CreditsBalance);
    }

    [Fact]
    public void RecruitIdentitySkipsAnExistingMachineWithoutRandomness()
    {
        var scene = Fixture();
        scene = scene with { Machines = [.. scene.Machines!, new("tycoon-1", new(8, 8), MachineTypes.EquipmentStation)] };
        var dice = new Dice();
        var result = GameEngine.ConfirmRecruitment(Open(Ready(scene)), UnitTypeIds.Tycoon, dice, false);
        Assert.Equal("tycoon-2", result.State.Party!.MemberIds[^1]);
        Assert.Equal(0, dice.Calls);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("equipment")]
    [InlineData("item")]
    public void RecruitmentReservesAndConsumesTheOpeningOperateSourceOnce(string kind)
    {
        var ability = new LocalAbilityDefinition("service", "Recruit", AbilityTiming.Action, new Operate()) { Usage = new(2) };
        var scene = Fixture();
        var operatorType = UnitRoster.Barbarian() with { LocalAbilities = kind == "native" ? [ability] : [] };
        scene = scene with { UnitTypes = [operatorType] };
        if (kind == "equipment")
            scene = scene with
            {
                EquipmentTypes = [new("console", "Console") { LocalAbilities = [ability] }],
                Units = [scene.Units[0] with { Inventory = [new("source", "console")] }, .. scene.Units.Skip(1)]
            };
        if (kind == "item")
            scene = scene with
            {
                ItemTypes = [new("permit", "Permit") { LocalAbilities = [ability with { Usage = new(null) }] }],
                Units = [scene.Units[0] with { Inventory = [new OwnedObject("source") { ItemTypeId = "permit" }] }, .. scene.Units.Skip(1)]
            };
        var state = Open(Ready(scene));
        var original = Json(state);
        Assert.Equal(kind == "item" ? "source" : null, state.MachineInteraction!.ReservedConsumptionObjectId);
        state.SideCredits["heroes"] = 74;
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false));
        state.SideCredits["heroes"] = 90;
        Assert.Equal(original, Json(state));
        var source = state.MachineInteraction.Operation.Ability!;
        var result = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false);
        var actor = result.State.Units.Single(u => u.Id == "operator");
        if (kind == "item")
        {
            Assert.Empty(actor.Inventory);
            Assert.Single(result.Events, e => e.Kind == "ItemUsed");
            Assert.Single(result.State.LootDeck!.DiscardPile, o => o.Id == "source");
        }
        else Assert.Equal(1, result.State.AbilityUsesOf(actor.Id, source)!.RemainingUses);
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
    }

    [Fact]
    public void RecruitmentArrivalConsumesBananaAndCollectsCreditsAfterThePurchase()
    {
        var state = Open();
        var cell = GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).SpawnCell!;
        state.PlaceCredits(3, cell);
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, cell);
        var result = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false);
        var id = result.State.Party!.MemberIds[^1];
        Assert.Equal(18, result.State.SideCredits["heroes"]);
        Assert.False(result.State.IsUpright(id));
        Assert.Empty(result.State.CreditsPiles);
        Assert.Empty(result.State.CellMarkers);
        Assert.True(result.Events.FindIndex(e => e.Kind == "CellMarkerConsumed") <
            result.Events.FindIndex(e => e.Kind == "CreditsCollected"));
    }

    [Fact]
    public void SpawnUsesShortestOpenRouteWithRowThenColumnTiesAndAnExactThreeStepLimit()
    {
        var state = Open();
        var type = state.Types.Single(t => t.Id == UnitTypeIds.Tycoon);
        Assert.Equal(new Cell(4, 3), GameEngine.PreviewRecruitment(state, type.Id).SpawnCell);
        state.Physical.Board.Edges.Add(new(new(4, 4), new(4, 3), EdgeKind.Wall));
        var cells = GameEngine.RecruitmentSpawnCells(state, "operator", type).ToArray();
        Assert.Equal(new Cell(3, 4), cells[0]);
        Assert.Equal(new Cell(4, 5), cells[1]);
        Assert.Contains(new Cell(4, 3), cells); // Three open steps around the Wall.
        Assert.DoesNotContain(new Cell(4, 2), cells); // Manhattan 2, shortest open route 4.
        Assert.Equal(cells.Length, cells.Distinct().Count());
        Assert.Equal(new Cell(3, 4), GameEngine.PreviewRecruitment(state, type.Id).SpawnCell);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    public void BlockedCornersOccupantsAndMachinesPreventRecruitmentEvenWithPhase(EdgeKind barrier)
    {
        var type = UnitRoster.Tycoon();
        var scene = Fixture() with { UnitTypes = [type with
            { LocalAbilities = type.LocalAbilities.Add(new("phase", "Phase", AbilityTiming.Passive, new Phase())) }] };
        var state = Open(Ready(scene));
        state.Physical.Board.Edges.Add(new(new(4, 4), new(4, 3), barrier));
        state.Physical.Board.Edges.Add(new(new(4, 4), new(3, 4), barrier));
        var blocker = state.Physical.Figures.FindIndex(f => f.Id == "rogue");
        state.Physical.Figures[blocker] = state.Physical.Figures[blocker] with { Position = new(4, 5) };
        var before = Json(state);
        var preview = GameEngine.PreviewRecruitment(state, type.Id);
        Assert.False(preview.CanConfirm);
        Assert.Null(preview.SpawnCell);
        Assert.Contains(preview.Diagnostics, d => d.Code == "no-recruitment-cell");
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmRecruitment(state, type.Id, new Dice(), false));
        Assert.Equal(before, Json(state));
    }

    [Fact]
    public void ImpassableAndUnknownCellsAreExcludedFromTheSpawnRoute()
    {
        var scene = Fixture();
        scene = scene with { Board = scene.Board with { Cells = [new(new(4, 3), TerrainKind.SolidRock)] } };
        var state = Open(Ready(scene));
        Assert.Equal(new Cell(3, 4), GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon).SpawnCell);
        state.FogOfWarSides.Add("heroes");
        state.ExploredCells["heroes"] = [new(4, 4), new(5, 4), new(4, 5)];
        var preview = GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon);
        Assert.Equal(new Cell(4, 5), preview.SpawnCell);
        var type = state.Types.Single(t => t.Id == UnitTypeIds.Tycoon);
        Assert.Equal(new[] { new Cell(4, 5) }, GameEngine.RecruitmentSpawnCells(state, "operator", type));
    }

    [Fact]
    public void ConfirmRecalculatesSpawnAfterAnotherFigureOccupiesThePreviewedCell()
    {
        var state = Open();
        var preview = GameEngine.PreviewRecruitment(state, UnitTypeIds.Tycoon);
        var blocker = state.Physical.Figures.FindIndex(f => f.Id == "rogue");
        state.Physical.Figures[blocker] = state.Physical.Figures[blocker] with { Position = preview.SpawnCell! };
        var confirmed = GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, new Dice(), false);
        var recruit = confirmed.State.Party!.MemberIds[^1];
        Assert.Equal(new Cell(3, 4), confirmed.State.Physical.Figures.Single(f => f.Id == recruit).Position);
    }

    [Fact]
    public void RecruitmentAcceptsBroadLandRestrictionsAndRejectsUnsupportedDeploymentSurfaces()
    {
        var type = UnitRoster.Tycoon();
        var restricted = type with { TerrainRestriction = new([TerrainCategories.Land]) };
        // A catalog may also declare surfaces outside the generator's ordinary Land deployment.
        var land = Open(Ready(Fixture() with { UnitTypes = [restricted],
            TerrainTypes = [new("unused-local-surface", "Unused surface", [MovementKinds.Land], false)] }));
        Assert.True(GameEngine.PreviewRecruitment(land, type.Id).CanConfirm);
        foreach (var unsupported in new[]
        {
            type with { BaseMovementCapabilities = [MovementKinds.Water] },
            type with { TerrainRestriction = new([TerrainCategories.Vegetation]) },
            type with { Footprint = Footprint.TwoByTwo }
        })
        {
            var state = Open(Ready(Fixture() with { UnitTypes = [unsupported] }));
            Assert.False(GameEngine.PreviewRecruitment(state, type.Id).CanConfirm);
            Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmRecruitment(state, type.Id, new Dice(), false));
        }
    }
}
