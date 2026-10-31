using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SummoningSourceIntegrationTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static LocalAbilityDefinition Summon(Posture posture, int? uses, string target = "local-spirit", string key = "call") =>
        new(key, "Different Printed Name", AbilityTiming.Action, new SummonAdjacent(target, posture)) { Usage = new(uses) };
    private static ScenarioDefinition Scenario(Posture posture = Posture.Lying) => new()
    {
        Board = new() { Width = 20, Height = 5 }, UnitTypeIds = ["carrier", "local-spirit"],
        UnitTypes = [new("carrier", 2, 1, 1, 0, 1)
        {
            CanHaveEquipment = true, CanHaveItems = true,
            LocalAbilities = [Summon(posture, 1), new("linger", "Linger", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Mov, 1)]))]
        }, new("local-spirit", 3, 1, 2, 1, 1) { DisplayName = "Local Spirit", Actions = UnitAction.None }],
        EquipmentTypes = [new("charm", "Charm") { LocalAbilities = [Summon(posture, 1)] }],
        ItemTypes = [new("scroll", "Scroll") { LocalAbilities = [Summon(posture, null)] }],
        Units = [new("carrier", "blue", new(2, 2), UnitId: "actor")
        { Inventory = [new("charm-one", "charm"), new("scroll-one") { ItemTypeId = "scroll" }, new("scroll-two") { ItemTypeId = "scroll" }] },
            new("carrier", "red", new(19, 4), UnitId: "enemy")],
        Agency = [new("carrier", "blue", ControllerKind.Human), new("carrier", "red", ControllerKind.Automated)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition scenario)
    {
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        state.Round = 1; state.ActiveToken = new("carrier", "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Bag = [new("carrier", "red")];
        state.Pending = new(DecisionKind.Activation, "carrier", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static Candidate Candidate(GameState state, AbilitySourceKind source) => state.Pending!.Candidates
        .First(candidate => candidate.Ability?.SourceKind == source && candidate.Ability.Key == "call");

    [Theory]
    [InlineData(AbilitySourceKind.Unit, Posture.Upright)]
    [InlineData(AbilitySourceKind.Unit, Posture.Lying)]
    [InlineData(AbilitySourceKind.Equipment, Posture.Upright)]
    [InlineData(AbilitySourceKind.Equipment, Posture.Lying)]
    [InlineData(AbilitySourceKind.Item, Posture.Upright)]
    [InlineData(AbilitySourceKind.Item, Posture.Lying)]
    public void LocalSummonFromEverySourceUsesCapturedPayloadAndIndependentAccountingAcrossReplay(AbilitySourceKind source, Posture posture)
    {
        var initial = Ready(Scenario(posture));
        var choice = Candidate(initial, source);
        var expected = Use(initial, choice.Key);
        var replay = Use(Restore(initial), choice.Key);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(replay));
        var state = Restore(replay.State);
        GameEngine.ValidateScenario(state);
        var created = Assert.Single(state.Units.Where(unit => unit.TypeId == "local-spirit"));
        Assert.Equal(("blue", 1), (created.SideId, created.CurrentHp));
        Assert.Equal(posture, state.Physical.Figures.Single(figure => figure.Id == created.Id).Posture);
        Assert.Equal(choice.Destination, state.Physical.Figures.Single(figure => figure.Id == created.Id).Position);
        Assert.Equal("Local Spirit", state.Types.Single(type => type.Id == created.TypeId).DisplayName);
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(created.TypeId, "blue")));
        Assert.Equal(initial.Bag, state.Bag);
        Assert.DoesNotContain(state.Bag, token => token.TypeId == "local-spirit");
        Assert.Equal(1, state.ActionsUsedThisActivation);
        var actor = state.Units.Single(unit => unit.Id == "actor");
        Assert.Equal(source == AbilitySourceKind.Unit ? 1 : 0, actor.AbilityUseCounts.GetValueOrDefault("unit:call"));
        Assert.Equal(source == AbilitySourceKind.Equipment ? 1 : 0, actor.Inventory.Single(obj => obj.Id == "charm-one").AbilityUseCounts.GetValueOrDefault("call"));
        Assert.Equal(source == AbilitySourceKind.Item ? 1 : 2, actor.Inventory.Count(obj => obj.ItemTypeId == "scroll"));
        Assert.Equal(source == AbilitySourceKind.Item ? "scroll-one" : null, state.LootDeck!.DiscardPile.SingleOrDefault()?.Id);
        Assert.Empty(initial.Units.Single(unit => unit.Id == "actor").AbilityUseCounts);
        Assert.Empty(initial.Units.Single(unit => unit.Id == "actor").Inventory.Single(obj => obj.Id == "charm-one").AbilityUseCounts);
        Assert.Equal(2, initial.Units.Single(unit => unit.Id == "actor").Inventory.Count(obj => obj.ItemTypeId == "scroll"));
    }

    [Fact]
    public void ExistingTargetControllerAndBagArePreservedAndUniqueLimitUsesActorsSide()
    {
        var scenario = Scenario(Posture.Upright);
        scenario.UnitTypes![1] = scenario.UnitTypes[1] with { Unique = true, Hp = 3 };
        scenario.Agency.Add(new("local-spirit", "blue", ControllerKind.Automated));
        scenario.Agency.Add(new("local-spirit", "red", ControllerKind.Human));
        scenario.Units.Add(new("local-spirit", "red", new(10, 3), UnitId: "red-spirit"));
        var initial = Ready(scenario);
        initial.Bag.Add(new("local-spirit", "blue"));
        var result = Use(initial, Candidate(initial, AbilitySourceKind.Equipment).Key);
        var created = Assert.Single(result.State.Units.Where(unit => unit.TypeId == "local-spirit" && unit.SideId == "blue"));
        Assert.Equal(3, created.CurrentHp);
        Assert.Equal(ControllerKind.Automated, result.State.ControllerFor(new ActivationToken("local-spirit", "blue")));
        Assert.Equal(initial.Bag, result.State.Bag);
        Assert.Single(result.State.Units.Where(unit => unit.Id == "red-spirit"));
        result.State.ExtraActionsThisActivation = 1;
        var refreshed = GameEngine.RefreshChoices(result.State, new Dice(), false).State;
        Assert.DoesNotContain(refreshed.Pending!.Candidates, candidate => candidate.Action == UnitAction.SummonAdjacent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalSummonTargetMayBeUndeclaredAndScenarioLocalVariantTakesPrecedence(bool localVariant)
    {
        var scenario = Scenario();
        scenario.EquipmentTypes![0] = scenario.EquipmentTypes[0] with
        { LocalAbilities = [Summon(Posture.Lying, 1, UnitTypeIds.Goblin)] };
        var expected = CanonicalUnitTypes.Find(UnitTypeIds.Goblin)!;
        if (localVariant)
        {
            expected = expected with { DisplayName = "Scenario Goblin", Mov = 4 };
            scenario.UnitTypeIds.Add(expected.Id);
            scenario.UnitTypes!.Add(expected);
        }
        var initial = Ready(scenario);
        Assert.Equal(localVariant, initial.Types.Any(type => type.Id == UnitTypeIds.Goblin));
        var result = Use(initial, Candidate(initial, AbilitySourceKind.Equipment).Key);
        var created = Assert.Single(result.State.Units.Where(unit => unit.TypeId == UnitTypeIds.Goblin));
        Assert.Equal("blue", created.SideId);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(result.State.Types.Single(type => type.Id == expected.Id)));
        Assert.DoesNotContain(result.State.Bag, token => token.TypeId == expected.Id);
        GameEngine.ValidateScenario(Restore(result.State));
    }

    [Fact]
    public void NewlyCreatedGroupWaitsForNextRoundBagAndLyingSummonOnlyStandsWhenActivated()
    {
        var state = Ready(Scenario());
        state = Use(state, Candidate(state, AbilitySourceKind.Item).Key).State;
        var createdId = state.Units.Single(unit => unit.TypeId == "local-spirit").Id;
        var transitions = 0;
        while (!state.RoundComplete && transitions++ < 8)
        {
            var choice = state.Pending!.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)
                ?? state.Pending.Candidates.Single(candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
            state = Use(state, choice.Key).State;
        }
        Assert.True(state.RoundComplete);
        Assert.Equal(Posture.Lying, state.Physical.Figures.Single(figure => figure.Id == createdId).Posture);
        var next = GameEngine.StartRound(Restore(state), new Dice(), false);
        Assert.Contains(new ActivationToken("local-spirit", "blue"), next.State.Bag);
        state = next.State;
        transitions = 0;
        while (!state.RoundComplete && transitions++ < 12)
        {
            var choice = state.Pending!.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)
                ?? state.Pending.Candidates.Single(candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
            var result = Use(state, choice.Key);
            Assert.DoesNotContain(result.Events, gameEvent => gameEvent.Kind == "ActionUsed" && gameEvent.UnitId == createdId);
            state = result.State;
        }
        Assert.True(state.RoundComplete);
        Assert.Equal(Posture.Upright, state.Physical.Figures.Single(figure => figure.Id == createdId).Posture);
    }

    [Theory]
    [InlineData(AbilitySourceKind.Unit)]
    [InlineData(AbilitySourceKind.Equipment)]
    [InlineData(AbilitySourceKind.Item)]
    public void StalePlacementSelectionCannotSpendUsesOrConsumeItem(AbilitySourceKind source)
    {
        var initial = Ready(Scenario());
        var choice = Candidate(initial, source);
        initial.PlaceUnit("local-spirit", "blocker", "blue", choice.Destination!);
        initial.Controllers.Add(new(new("local-spirit", "blue"), ControllerKind.Human));
        var before = JsonSerializer.Serialize(initial);
        Assert.Throws<ArgumentException>(() => Use(initial, choice.Key));
        Assert.Equal(before, JsonSerializer.Serialize(initial));
        Assert.Empty(initial.LootDeck!.DiscardPile);
    }

    [Fact]
    public void EquipmentSummonUsesStayWithPhysicalObjectWhenAnotherUnitCollectsIt()
    {
        var initial = Ready(Scenario());
        var state = Use(initial, Candidate(initial, AbilitySourceKind.Equipment).Key).State;
        var destination = state.Physical.Figures.Single(figure => figure.Id == "actor").Position;
        GameEngine.ResolveDefeat(state, "actor");
        state = Restore(state);
        var enemyIndex = state.Physical.Figures.FindIndex(figure => figure.Id == "enemy");
        state.Physical.Figures[enemyIndex] = state.Physical.Figures[enemyIndex] with { Position = destination };
        state.ActiveToken = new("carrier", "red"); state.CurrentUnitId = "enemy";
        state.ActionsUsedThisActivation = 0; state.BonusActionUseCountsThisActivation.Clear(); state.CompletedUnitIds.Clear();
        state.Pending = new(DecisionKind.Activation, "carrier", "enemy", [], false);
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        state = Use(state, "pickup-object:charm-one").State;
        Assert.Equal(1, state.Units.Single(unit => unit.Id == "enemy").Inventory.Single(obj => obj.Id == "charm-one").AbilityUseCounts["call"]);
        Assert.DoesNotContain(state.Pending!.Candidates, candidate => candidate.Ability?.SourceInstanceId == "charm-one");
        GameEngine.ValidateScenario(Restore(state));
    }

    [Fact]
    public void SummonedPhysicalIdentityAvoidsExistingMachineAndUnitIdsDeterministically()
    {
        var scenario = Scenario() with { Machines = [new("local-spirit-1", new(10, 1), MachineTypes.DiamondPress)] };
        scenario.Units.Add(new("local-spirit", "red", new(10, 3), UnitId: "local-spirit-2"));
        scenario.Agency.Add(new("local-spirit", "red", ControllerKind.Human));
        var initial = Ready(scenario);
        var selected = Candidate(initial, AbilitySourceKind.Item).Key;
        var result = Use(initial, selected);
        Assert.Equal("local-spirit-3", result.Events.Single(gameEvent => gameEvent.Kind == "UnitCreated").UnitId);
        Assert.Contains(result.State.Physical.Board.Machines, machine => machine.Id == "local-spirit-1");
        Assert.Contains(result.State.Units, unit => unit.Id == "local-spirit-2");
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Use(Restore(initial), selected)));
        GameEngine.ValidateScenario(Restore(result.State));
    }

    [Theory]
    [InlineData(AbilitySourceKind.Unit, "unitTypes[0]")]
    [InlineData(AbilitySourceKind.Equipment, "equipmentTypes[0]")]
    [InlineData(AbilitySourceKind.Item, "itemTypes[0]")]
    public void UnknownSummonReferenceUsesSameOwnerFieldDiagnosticInScenarioValidation(AbilitySourceKind source, string path)
    {
        var scenario = Scenario();
        var invalid = Summon(Posture.Lying, source == AbilitySourceKind.Item ? null : 1, "missing-unit", "other");
        switch (source)
        {
            case AbilitySourceKind.Unit: scenario.UnitTypes![0] = scenario.UnitTypes[0] with { LocalAbilities = [Summon(Posture.Lying, 1), invalid] }; break;
            case AbilitySourceKind.Equipment: scenario.EquipmentTypes![0] = scenario.EquipmentTypes[0] with { LocalAbilities = [Summon(Posture.Lying, 1), invalid] }; break;
            case AbilitySourceKind.Item: scenario.ItemTypes![0] = scenario.ItemTypes[0] with { LocalAbilities = [Summon(Posture.Lying, null), invalid] }; break;
        }
        var error = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        Assert.Equal(path + ".localAbilities[1].mechanic.unitTypeId", ScenarioValidationContext.PathOf(error));
    }

    [Fact]
    public void DefinitionOnlyResolutionUsesLocalUnitPrecedenceAndRejectsMissingReferences()
    {
        var scenario = Scenario();
        var resolved = UnitContent.Resolve(scenario.UnitTypes);
        AbilityContent.ValidateObjectCatalogs(scenario.EquipmentTypes!, scenario.ItemTypes!, [], resolved);
        var error = Assert.Throws<ArgumentException>(() => AbilityContent.ValidateObjectCatalogs(scenario.EquipmentTypes!, scenario.ItemTypes!, []));
        Assert.Equal("equipmentTypes[0].localAbilities[0].mechanic.unitTypeId", ScenarioValidationContext.PathOf(error));
        var canonical = CanonicalUnitTypes.Find(UnitTypeIds.Goblin)!;
        var variant = canonical with { DisplayName = "Local Variant" };
        var variants = UnitContent.Resolve([variant]);
        Assert.Equal("Local Variant", UnitContent.Find(canonical.Id, variants)!.DisplayName);
        Assert.Equal(canonical.DisplayName, CanonicalUnitTypes.Find(canonical.Id)!.DisplayName);
        Assert.Throws<ArgumentException>(() => UnitContent.Resolve([variant, variant]));
        Assert.Throws<ArgumentException>(() => UnitContent.Resolve([variant with { Id = "" }]));
    }

    [Theory]
    [InlineData(Footprint.OneByOne, 1, 1)]
    [InlineData(Footprint.TwoByTwo, 0, 0)]
    public void SummonCandidatesRespectTargetsTerrainRestrictionAcrossItsWholeFootprint(Footprint footprint, int x, int y)
    {
        var scenario = Scenario(Posture.Upright);
        scenario.UnitTypes![1] = scenario.UnitTypes[1] with
        { Footprint = footprint, TerrainRestriction = new([TerrainCategories.RacingGround]) };
        scenario.Board.Cells.AddRange([new(new(0, 0), TerrainKind.Asphalt), new(new(1, 0), TerrainKind.Asphalt),
            new(new(0, 1), TerrainKind.Asphalt), new(new(1, 1), TerrainKind.Asphalt)]);
        var initial = Ready(scenario);
        var choice = Assert.Single(initial.Pending!.Candidates.Where(candidate => candidate.Ability?.SourceKind == AbilitySourceKind.Equipment));
        Assert.Equal(new Cell(x, y), choice.Destination);
        var result = Use(initial, choice.Key);
        var created = Assert.Single(result.State.Units.Where(unit => unit.TypeId == "local-spirit"));
        Assert.All(FootprintGeometry.OccupiedCells(result.State, created.Id), cell =>
            Assert.Contains(TerrainCategories.RacingGround, result.State.Physical.Board.TerrainDefinitionAt(cell).Categories));
        GameEngine.ValidateScenario(Restore(result.State));
    }

    [Fact]
    public void SummonEventsAndPhysicalSourceCountersRespectFogOfWar()
    {
        var scenario = Scenario() with { FogOfWarSides = ["blue"] };
        scenario.Units[1] = scenario.Units[1] with { Inventory = [new("hidden-charm", "charm")] };
        var initial = Ready(scenario);
        var result = Use(initial, Candidate(initial, AbilitySourceKind.Equipment).Key);
        var observed = FogOfWar.Observe(result.State, "blue");
        var created = result.Events.Single(gameEvent => gameEvent.Kind == "UnitCreated");
        Assert.NotNull(FogOfWar.ObserveEvent(result.State, "blue", created));
        Assert.Contains(observed.Units, unit => unit.Id == created.UnitId);
        Assert.Equal(1, observed.Units.Single(unit => unit.Id == "actor").Inventory.Single(obj => obj.Id == "charm-one").AbilityUseCounts["call"]);
        Assert.DoesNotContain("hidden-charm", JsonSerializer.Serialize(observed));
        Assert.DoesNotContain(observed.Units, unit => unit.Id == "enemy");
    }
}
