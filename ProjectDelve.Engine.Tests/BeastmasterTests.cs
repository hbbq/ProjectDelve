using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BeastmasterTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 12, Height = 5 },
        UnitTypeIds = [UnitTypeIds.Beastmaster, UnitTypeIds.Grunt],
        // An unused test-only Bonus Action keeps the source activation reviewable
        // after summoning instead of automatically advancing to the next token.
        UnitTypes = [UnitRoster.Beastmaster().WithLocal(new("reserve", "Reserve", AbilityTiming.BonusAction,
            new SelfModifiersMechanic([new(Stat.Def, 1)])))],
        Units = [new(UnitTypeIds.Beastmaster, "blue", new(2, 2), UnitId: "master", GroupId: "party"),
            new(UnitTypeIds.Grunt, "red", new(11, 4), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition? definition = null)
    {
        var state = GameEngine.CreateGame(definition ?? Definition());
        state.Round = 1; state.ActiveToken = new(UnitTypeIds.Beastmaster, "blue", "party");
        state.ResolvingTokenUnitIds = ["master"]; state.CurrentUnitId = "master"; state.MoveDone = true;
        state.Bag = [new(UnitTypeIds.Grunt, "red")];
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static Candidate Summon(GameState state, string type) => state.Pending!.Candidates.First(c =>
        c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic summon && summon.UnitTypeId == type);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void CanonicalContentHasFiveIndependentUnlimitedSummonsAndHeroPermissions()
    {
        var type = UnitRoster.Beastmaster();
        Assert.Equal((3, 1, 2, 3, 5), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.True(type.Unique); Assert.True(type.CanHaveItems); Assert.True(type.CanHaveEquipment);
        Assert.Equal(1, type.LootDropSuccesses); Assert.NotNull(type.Mechanic<OpenDoor>());
        Assert.Equal(ControllerKind.Human, type.DefaultController);
        var summons = type.LocalAbilities.Where(a => a.Mechanic is AdjacentUnitSummonMechanic).ToArray();
        Assert.Equal(5, summons.Length); Assert.Equal(5, summons.Select(a => a.Key).Distinct().Count());
        Assert.Equal(new[] { "jackalope-type", "giant-toad-type", "dire-bear-type", "raptor-type", "light-elemental-type" },
            summons.Select(a => ((AdjacentUnitSummonMechanic)a.Mechanic).UnitTypeId));
        Assert.Equal(new[] { "Summon Jackalope", "Summon Giant Toad", "Summon Dire Bear", "Summon Raptor", "Summon Light Elemental" },
            summons.Select(a => a.Name));
        Assert.Equal(new[] { "summon-jackalope", "summon-giant-toad", "summon-dire-bear", "summon-raptor", "summon-light-elemental" },
            summons.Select(a => a.Key));
        Assert.All(summons, a => { Assert.Equal(AbilityTiming.Action, a.Timing); Assert.Null(a.Usage.MaxUses); });
        Assert.Null(MechanicCatalog.Find("adjacentUnitSummon")!.MaxPerSource);
        // The narrowly relaxed summon descriptor must not relax other mechanics.
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate([
            new("one", "One", AbilityTiming.AfterAttack, new MoveAfterAttack(1)),
            new("two", "Two", AbilityTiming.AfterAttack, new MoveAfterAttack(1))], AbilitySourceKind.Unit));
    }

    [Theory]
    [InlineData(UnitTypeIds.Jackalope, 5, 0, 0, 2, Posture.Upright, UnitBehavior.ExploreFoW | UnitBehavior.GoToFriendly)]
    [InlineData(UnitTypeIds.GiantToad, 3, 0, 0, 2, Posture.Upright, UnitBehavior.ExploreFoW | UnitBehavior.GoToFriendly)]
    [InlineData(UnitTypeIds.DireBear, 2, 1, 3, 3, Posture.Lying, UnitBehavior.GoToFriendly)]
    [InlineData(UnitTypeIds.Raptor, 4, 1, 2, 2, Posture.Lying, UnitBehavior.BackAwayAfterAttack | UnitBehavior.GoToFriendly)]
    [InlineData(UnitTypeIds.LightElemental, 3, 0, 0, 2, Posture.Upright, UnitBehavior.MaximizeFriendlyAdjacent | UnitBehavior.GoToFriendly)]
    public void EachSummonRetainsItsPayloadSideLimitAndReplayIdentity(string id, int mov, int rng, int atk, int def,
        Posture posture, UnitBehavior behaviors)
    {
        var initial = Ready();
        Assert.Equal(5, initial.Pending!.Candidates.Where(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic)
            .Select(c => c.Ability!.ActionId).Distinct().Count());
        var choice = Summon(initial, id);
        Assert.Equal(1, ((AdjacentUnitSummonMechanic)choice.Ability!.Definition.Mechanic).SameTypeSideLimit);
        var result = GameEngine.Advance(initial, new Choice(choice.Key), new Dice(), false);
        var replay = GameEngine.Advance(Restore(initial), new Choice(choice.Key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        var state = Restore(result.State);
        var unit = Assert.Single(state.Units, u => u.TypeId == id);
        var type = state.Types.Single(t => t.Id == id);
        Assert.Equal("Summon " + type.DisplayName, choice.Ability.Definition.Name);
        Assert.Equal(behaviors, type.Behaviors);
        Assert.Equal(id == UnitTypeIds.GiantToad, type.Amphibious);
        Assert.Equal((mov, rng, atk, def, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal("blue", unit.SideId); Assert.Equal(1, unit.CurrentHp); Assert.Null(unit.GroupId);
        Assert.Equal(posture, state.Physical.Figures.Single(f => f.Id == unit.Id).Posture);
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(unit.Token));
        Assert.Equal(initial.Bag, state.Bag); Assert.DoesNotContain(unit.Id, state.ResolvingTokenUnitIds);
        Assert.Equal(OrderKind.Normal, state.OrderFor(unit.Token).Kind);
        state.ExtraActionsThisActivation = 1;
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic s && s.UnitTypeId == id);
        Assert.Equal("UnitDefeated", GameEngine.ResolveDefeat(state, unit.Id).Kind);
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var replacement = GameEngine.Advance(state, new Choice(Summon(state, id).Key), new Dice(), false).State;
        Assert.Single(replacement.Units, u => u.TypeId == id && u.CurrentHp > 0);
        Assert.NotEqual(unit.Id, replacement.Units.Single(u => u.TypeId == id && u.CurrentHp > 0).Id);
        GameEngine.ValidateScenario(Restore(replacement));
    }

    [Fact]
    public void CapCountsOtherGroupsAndControllersOnSameSideButNotOpponents()
    {
        var definition = Definition(); definition.UnitTypeIds.Add(UnitTypeIds.Jackalope);
        definition.Units.Add(new(UnitTypeIds.Jackalope, "red", new(9, 4), UnitId: "opponent"));
        var state = Ready(definition);
        Assert.NotNull(Summon(state, UnitTypeIds.Jackalope));
        state.PlaceUnit(UnitTypeIds.Jackalope, "encounter", "blue", new(8, 3), groupId: "unrelated");
        state.Controllers.Add(new(new(UnitTypeIds.Jackalope, "blue", "unrelated"), ControllerKind.Human));
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic s && s.UnitTypeId == UnitTypeIds.Jackalope);
    }

    [Fact]
    public void ExactRuntimeControllerSurvivesTypeDefaultAndResummoning()
    {
        var state = Ready(); state.Controllers.Add(new(new(UnitTypeIds.Jackalope, "blue"), ControllerKind.Human));
        var result = GameEngine.Advance(state, new Choice(Summon(state, UnitTypeIds.Jackalope).Key), new Dice(), false);
        Assert.Equal(ControllerKind.Human, result.State.ControllerFor(new ActivationToken(UnitTypeIds.Jackalope, "blue")));
    }

    [Fact]
    public void SetupResolvesExactThenScenarioDefaultThenTypeDefaultWithoutChangingLegacyTypes()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 6, Height = 2 }, UnitTypeIds = [UnitTypeIds.Jackalope], Agency = [],
            Units = [new(UnitTypeIds.Jackalope, "blue", new(0, 0), GroupId: "one"),
                new(UnitTypeIds.Jackalope, "blue", new(2, 0), GroupId: "two")], Loot = LootSettings.Empty,
            Orders = [new(UnitTypeIds.Jackalope, "blue", OrderKind.HoldPosition, GroupId: "one")]
        };
        var state = GameEngine.CreateGame(definition);
        Assert.All(state.Controllers, a => Assert.Equal(ControllerKind.Automated, a.Controller));
        definition.Agency.Add(new(UnitTypeIds.Jackalope, "blue", ControllerKind.Human));
        definition.Agency.Add(new(UnitTypeIds.Jackalope, "blue", ControllerKind.Automated, "one"));
        state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(new ActivationToken(UnitTypeIds.Jackalope, "blue", "one")));
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Jackalope, "blue", "two")));
        Assert.Null(UnitType.Grunt().DefaultController);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { UnitTypeIds = [UnitTypeIds.Grunt], Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0))], Agency = [], Orders = null }));
    }

    [Fact]
    public void LightElementalAuraStacksWithClericAndRespectsPosture()
    {
        var definition = Definition();
        definition.UnitTypeIds.AddRange([UnitTypeIds.LightElemental, UnitTypeIds.Cleric]);
        definition.Units.Add(new(UnitTypeIds.LightElemental, "blue", new(1, 2), UnitId: "elemental"));
        definition.Units.Add(new(UnitTypeIds.Cleric, "blue", new(2, 1), UnitId: "cleric"));
        definition.Agency.Add(new(UnitTypeIds.Cleric, "blue", ControllerKind.Human));
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(5, state.EffectiveDefOf("master"));
        Assert.Equal(3, state.EffectiveDefOf("elemental")); // Cleric only, not its own Aura.
        var index = state.Physical.Figures.FindIndex(f => f.Id == "elemental");
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Posture = Posture.Lying };
        Assert.Equal(4, state.EffectiveDefOf("master"));
        Assert.Equal(3, state.EffectiveDefOf("elemental"));
        Assert.Equal(1, UnitRoster.Raptor().Mechanic<MoveAfterAttack>()!.MaxSteps);
    }

    [Fact]
    public void LightElementalAuraAddsOneBonusToLargeFriendlyRegardlessOfContactingCells()
    {
        var elemental = UnitRoster.LightElemental();
        var large = new UnitType("large", 1, 1, 1, 1, 1) { Footprint = Footprint.TwoByTwo };
        var state = new GameState
        {
            Physical = new(new Board(5, 5, []), [new("elemental", new(1, 1)), new("large", new(2, 1))]),
            Types = [elemental, large], Units = [elemental.CreateUnit("elemental", "blue"), large.CreateUnit("large", "blue")]
        };
        Assert.Equal(2, state.EffectiveDefOf("large"));
    }
}
