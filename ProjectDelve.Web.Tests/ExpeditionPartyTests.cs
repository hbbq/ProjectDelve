using System.Collections.Immutable;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionPartyTests
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
    private static readonly IReadOnlyList<ExpeditionHero> Heroes = ExpeditionParty.Originals;
    private static readonly ObjectiveArea Exit = new(10, 6, 2, 2);
    private static ScenarioDefinition Terrain()
    {
        var cells = Enumerable.Range(3, 2).SelectMany(y => Enumerable.Range(1, 5).Select(x => new Cell(x, y))).ToList();
        var core = cells.Where(c => c.X < 5).ToList();
        return new()
        {
            Board = new() { Width = 12, Height = 8 }, UnitTypeIds = [UnitTypeIds.Goblin], Units = [],
            Agency = [new(UnitTypeIds.Goblin, "opposition", ControllerKind.Automated)], Loot = LootSettings.Empty,
            Setup = new(1, new(5, cells, core, core.Concat(cells.Except(core)).ToList(), new(5, 4), new(6, 4), 10, 8), [])
        };
    }
    private static ContentBundle Content()
    {
        var content = ContentBundle.Canonical();
        var gear = new EquipmentDefinition("test-summon-bell", "Test Bell")
        {
            LocalAbilities = [new("summon", "Summon", AbilityTiming.Action, new AdjacentUnitSummonMechanic("test-companion", Posture.Upright)) { Usage = new(3) }]
        };
        return content with
        {
            Equipment = content.Equipment.Add(gear),
            Units = content.Units.Select(t => t.Id == Heroes[0].TypeId ? t with
            {
                LocalAbilities = t.LocalAbilities.Add(new("test", "Test Rally", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 1)])) { Usage = new(2) })
            } : t).Append(new("test-companion", 2, 1, 1, 1, 1) { CanHaveItems = true }).ToImmutableArray(),
            LootCards = content.LootCards.Add(new("test-bell-card", "Test Bell Card") { EquipmentTypeId = gear.Id })
        };
    }
    private static GameState Previous(ContentBundle content)
    {
        var definition = ExpeditionParty.Prepare(Terrain(), content, Exit, Heroes, null);
        definition.Units[0] = definition.Units[0] with
        {
            Inventory = [new("loot-card-1", "test-summon-bell")
                { LootCardId = "test-bell-card", AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("summon", 2) },
                new("retained-item", LootCardKind.SecondWind), new("consumed-item", LootCardKind.SecondWind)]
        };
        definition.Units[1] = definition.Units[1] with { Inventory = [new("dropped-boots", "lucky-boots")] };
        return GameEngine.CreateGame(definition, content);
    }

    [Fact]
    public void DeclaresAllOriginalsWithHumanAgencyFogAndDynamicPartyExitSelector()
    {
        var content = Content(); var terrain = Terrain();
        var prepared = ExpeditionParty.Prepare(terrain, content, Exit, Heroes, null);
        var state = GameEngine.CreateGame(prepared, content);
        Assert.Empty(terrain.Units);
        Assert.Equal(5, state.Units.Count);
        Assert.Equal(Heroes.Select(h => h.InstanceId), state.Units.Select(u => u.Id));
        Assert.All(state.Controllers.Where(c => c.Token.SideId == ExpeditionParty.SideId), c => Assert.Equal(ControllerKind.Human, c.Controller));
        Assert.Contains(ExpeditionParty.SideId, state.FogOfWarSides);
        Assert.NotEmpty(state.ExploredCells[ExpeditionParty.SideId]);
        var objective = Assert.Single(state.Objectives, o => o.SideId == ExpeditionParty.SideId);
        Assert.Equal(ScenarioObjective.ReachArea, objective.Objective); Assert.Equal(2, objective.Count); Assert.Equal(Exit, objective.Area);
        var selector = Assert.Single(objective.Selectors);
        Assert.Equal(UnitSelectorKind.Party, selector.Kind);
        Assert.Equal(ExpeditionParty.SideId, selector.SideId);
        Assert.Equal(Heroes.Select(h => h.InstanceId), state.Party!.MemberIds);
        Assert.Equal(ExpeditionParty.InitialCapacity, state.Party.Capacity);
        Assert.All(state.Objectives.Where(o => o.SideId != ExpeditionParty.SideId), o => Assert.Equal(ScenarioObjective.NoObjective, o.Objective));
        Assert.Equal(0, state.Round);
    }

    [Fact]
    public void HandoffUsesOnlyCurrentOriginalInventoryAndResetsFreshMapState()
    {
        var content = Content(); var previous = Previous(content);
        // Dedicated terminal history: consumption removed the Item; defeat dropped
        // the second original's gear. Neither the old ground nor discard transfers.
        var first = previous.Units[0]; var consumed = first.Inventory.Single(o => o.Id == "consumed-item");
        previous.Units[0] = first with { CurrentHp = 1, AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("unit:test", 1), Inventory = first.Inventory.Remove(consumed) };
        previous.LootDeck!.DiscardPile.Add(consumed);
        var defeated = previous.Units[1];
        previous.GroundObjects.Add(new(Assert.Single(defeated.Inventory), previous.Physical.Figures.Single(f => f.Id == defeated.Id).Position));
        previous.Units[1] = defeated with { CurrentHp = 0, Inventory = [] };
        previous.Physical.Figures.RemoveAll(f => f.Id == defeated.Id); previous.DefeatedUnitIds.Add(defeated.Id);
        previous.Physical.Figures[0] = previous.Physical.Figures[0] with { Posture = Posture.Lying };
        previous.PlaceUnit("test-companion", "summoned", ExpeditionParty.SideId, new(8, 2), summonerUnitId: first.Id,
            inventory: [new("summon-item", LootCardKind.SecondWind)]);
        previous.Controllers.Add(new(new("test-companion", ExpeditionParty.SideId), ControllerKind.Human));
        previous.Round = 7;
        GameEngine.ValidateRestoredState(previous);
        var refreshed = content with
        {
            Units = content.Units.Select(t => t.Id == first.TypeId ? t with { Hp = t.Hp + 2, Mov = t.Mov + 1 } : t).ToImmutableArray(),
            Equipment = content.Equipment.Select(e => e.Id == "test-summon-bell" ? e with
            { Name = "Refreshed Bell", LocalAbilities = [e.LocalAbilities[0] with { Usage = new(5) }] } : e).ToImmutableArray()
        };
        var effective = ExpeditionParty.ResolveContent(refreshed, previous, Heroes);
        var terrain = Terrain() with { Loot = new() { Composition = [new(LootCardKind.SecondWind, 2)] } };
        var next = GameEngine.CreateGame(ExpeditionParty.Prepare(terrain, effective, Exit, Heroes, previous), effective);
        Assert.Equal(5, next.Units.Count); Assert.DoesNotContain(next.Units, u => u.Id == "summoned");
        Assert.Equal(new[] { "loot-card-1", "retained-item" }, next.Units.SelectMany(u => u.Inventory).Select(o => o.Id));
        Assert.All(next.Units, u => { Assert.Equal(next.Types.Single(t => t.Id == u.TypeId).Hp, u.CurrentHp); Assert.Empty(u.AbilityUseCounts); });
        Assert.All(next.Physical.Figures, f => Assert.Equal(Posture.Upright, f.Posture));
        Assert.All(next.Units.SelectMany(u => u.Inventory), o => Assert.Empty(o.AbilityUseCounts));
        Assert.Equal("Refreshed Bell", next.Equipment("test-summon-bell").Name);
        Assert.Equal(5, next.Equipment("test-summon-bell").LocalAbilities[0].Usage.MaxUses);
        Assert.Equal(refreshed.Units.Single(t => t.Id == first.TypeId).Mov, next.Types.Single(t => t.Id == first.TypeId).Mov);
        Assert.Equal(new[] { "loot-card-2", "loot-card-3" }, next.LootDeck!.DrawPile.Select(o => o.Id));
        Assert.Empty(next.GroundObjects); Assert.Empty(next.LootDeck.DiscardPile); Assert.Equal(0, next.Round);
        Assert.Equal(7, previous.Round); Assert.Equal(2, previous.Units[0].Inventory[0].AbilityUseCounts["summon"]);
    }

    [Fact]
    public void MissingGearSourceAndSummonDependencyFallBackToSavedWholeDefinitions()
    {
        var original = Content(); var previous = Previous(original);
        var latest = original with
        {
            Equipment = original.Equipment.RemoveAll(e => e.Id == "test-summon-bell"),
            Units = original.Units.RemoveAll(u => u.Id == "test-companion" || u.Id == Heroes[0].TypeId),
            LootCards = original.LootCards.RemoveAll(c => c.Id == "test-bell-card")
        };
        var resolved = ExpeditionParty.ResolveContent(latest, previous, Heroes);
        Assert.Equal(original.Equipment.Single(e => e.Id == "test-summon-bell"), resolved.Equipment.Single(e => e.Id == "test-summon-bell"));
        Assert.Contains(resolved.Units, u => u.Id == "test-companion"); Assert.Contains(resolved.LootCards, c => c.Id == "test-bell-card");
        Assert.Equal(original.Units.Single(t => t.Id == Heroes[0].TypeId), resolved.Units.Single(t => t.Id == Heroes[0].TypeId));
        var state = GameEngine.CreateGame(ExpeditionParty.Prepare(Terrain(), resolved, Exit, Heroes, previous), resolved);
        Assert.Equal("test-bell-card", state.Units[0].Inventory[0].LootCardId);
    }

    [Fact]
    public void DepartingMemberReturnsWhileDroppedCardsStayBehindAndSeizedGearKeepsItsUses()
    {
        var content = Content();
        var founder = Heroes[0];
        content = content with { Units = [.. content.Units.Select(t => t.Id == founder.TypeId ? t with {
            LocalAbilities = t.LocalAbilities.Add(new("collect", "Collect", AbilityTiming.Action,
                new TryRepossessRandomEquipment(6, StorageRules.RepossessedEquipmentId))) } : t)] };
        var terrain = Terrain() with
        {
            Units = [new(UnitTypeIds.Goblin, "opposition", new(1, 2), UnitId: "carrier") {
                Inventory = [new("loot-card-1", "test-summon-bell") { LootCardId = "test-bell-card",
                    AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("summon", 2) }] }],
            LootUnitOverrides = new() { [UnitTypeIds.Goblin] = new(CanHaveEquipment: true) }
        };
        var ready = ExpeditionParty.Prepare(terrain, content, Exit, Heroes, null);
        var index = ready.Units.FindIndex(u => u.UnitId == founder.InstanceId);
        ready.Units[index] = ready.Units[index] with { Inventory = [new("dropped", LootCardKind.SecondWind)] };
        var state = GameEngine.CreateGame(ready, content);
        var actor = state.Units.Single(u => u.Id == founder.InstanceId);
        state.Round = 1; state.CurrentUnitId = actor.Id; state.ActiveToken = actor.Token;
        state.ResolvingTokenUnitIds = [actor.Id];
        state.MoveDone = true;
        var dice = new Dice();
        var active = GameEngine.RefreshChoices(state, dice, false).State;
        var collect = active.Pending!.Candidates.Single(c => c.Ability?.Key == "collect");
        var attempt = GameEngine.Advance(active, new Choice(collect.Key), dice, false);
        var departed = GameEngine.Advance(attempt.State, new Choice("roll-dice"), dice, false).State;
        GameEngine.ValidateRestoredState(departed);
        Assert.Contains(actor.Id, departed.Party!.MemberIds);
        Assert.DoesNotContain(actor.Id, departed.DefeatedUnitIds);
        Assert.DoesNotContain(departed.Physical.Figures, f => f.Id == actor.Id);
        Assert.Equal("dropped", Assert.Single(departed.GroundObjects).Object.Id);
        var latest = content with {
            Equipment = content.Equipment.RemoveAll(e => e.Id == "test-summon-bell"),
            LootCards = content.LootCards.RemoveAll(c => c.Id == "test-bell-card") };
        var resolved = ExpeditionParty.ResolveContent(latest, departed, Heroes);
        var next = GameEngine.CreateGame(ExpeditionParty.Prepare(Terrain(), resolved, Exit, Heroes, departed), resolved);
        var returned = next.Units.Single(u => u.Id == actor.Id);
        Assert.Empty(returned.Inventory);
        Assert.True(returned.CurrentHp > 0);
        Assert.Contains(next.Physical.Figures, f => f.Id == actor.Id);
        Assert.Empty(next.GroundObjects);
        var seized = Assert.Single(Assert.Single(next.Storages).Contents);
        Assert.Equal("loot-card-1", seized.Id);
        Assert.Equal("test-bell-card", seized.LootCardId);
        Assert.Equal(2, seized.AbilityUseCounts["summon"]);
        Assert.Contains(resolved.Equipment, e => e.Id == seized.EquipmentTypeId);
    }

    [Fact]
    public void IncompatibleCurrentOwnershipAndRetargetedProvenanceFailWithoutDroppingGear()
    {
        var content = Content(); var previous = Previous(content);
        var noEquipment = content with { Units = content.Units.Select(t => t.Id == Heroes[0].TypeId ? t with { CanHaveEquipment = false } : t).ToImmutableArray() };
        var effective = ExpeditionParty.ResolveContent(noEquipment, previous, Heroes);
        Assert.Contains("ownership", Assert.Throws<ArgumentException>(() => ExpeditionParty.Prepare(Terrain(), effective, Exit, Heroes, previous)).Message);
        var retargeted = content with { LootCards = content.LootCards.Select(c => c.Id == "test-bell-card" ? c with { EquipmentTypeId = "lucky-boots" } : c).ToImmutableArray() };
        Assert.Contains("provenance", Assert.Throws<ArgumentException>(() => ExpeditionParty.ResolveContent(retargeted, previous, Heroes)).Message);
        Assert.Equal("test-summon-bell", previous.Units[0].Inventory[0].EquipmentTypeId);
    }

    [Fact]
    public void GeneratedIdentityConflictsAndInvalidPrepartySetupFail()
    {
        var content = Content();
        var conflict = Terrain() with
        {
            Units = [new(UnitTypeIds.Goblin, "opposition", new(8, 2), UnitId: Heroes[0].InstanceId)]
        };
        Assert.Contains("identities", Assert.Throws<ArgumentException>(() => ExpeditionParty.Prepare(conflict, content, Exit, Heroes, null)).Message);
        var occupied = Terrain() with { Units = [new(UnitTypeIds.Goblin, "opposition", new(1, 3), UnitId: "occupant")] };
        Assert.Contains("PlayReady", Assert.Throws<ArgumentException>(() => ExpeditionParty.Prepare(occupied, content, Exit, Heroes, null)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalBeastmasterRetainsAllSummonTargetsAndSummonsThroughEngineWithMinimumRoster(bool generate)
    {
        var content = ExpeditionParty.ResolveContent(ContentBundle.Canonical(), null, Heroes);
        var plan = ExpeditionWorld.Initial(123);
        var terrain = generate ? ExpeditionWorld.Generate(123, plan, content) : Terrain() with { UnitTypeIds = [], Agency = [] };
        var prepared = ExpeditionParty.Prepare(terrain, content, generate ? plan.ExitArea : Exit, Heroes, null);
        var state = GameEngine.CreateGame(prepared, content);
        var beastmaster = state.Units.Single(u => u.TypeId == UnitTypeIds.Beastmaster);
        var mechanics = state.Types.Single(t => t.Id == beastmaster.TypeId).LocalAbilities
            .Select(a => a.Mechanic).OfType<AdjacentUnitSummonMechanic>().ToArray();
        Assert.Equal(Heroes.Select(h => h.TypeId), prepared.Units.Where(u => u.SideId == ExpeditionParty.SideId).Select(u => u.UnitTypeId));
        Assert.Equal(5, mechanics.Length);
        Assert.All(mechanics, mechanic => Assert.Contains(state.Types, t => t.Id == mechanic.UnitTypeId));
        state.Round = 1; state.ActiveToken = beastmaster.Token; state.CurrentUnitId = beastmaster.Id;
        state.ResolvingTokenUnitIds = [beastmaster.Id]; state.MoveDone = true;
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var choices = state.Pending!.Candidates.Where(c => c.Action == UnitAction.AdjacentUnitSummon).ToArray();
        Assert.All(mechanics, mechanic => Assert.Contains(choices, c =>
            c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic offered && offered.UnitTypeId == mechanic.UnitTypeId));
        var summon = choices.First(c => c.Ability!.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.Jackalope });
        var result = GameEngine.Advance(state, new Choice(summon.Key), new Dice(), false);
        var child = Assert.Single(result.State.Units, u => u.TypeId == UnitTypeIds.Jackalope);
        Assert.Equal(beastmaster.Id, child.SummonerUnitId); Assert.Equal(ExpeditionParty.SideId, child.SideId);
        Assert.Contains(result.Events, e => e.Kind == "UnitCreated" && e.UnitId == child.Id);
        GameEngine.ValidateRestoredState(result.State);
    }
}
