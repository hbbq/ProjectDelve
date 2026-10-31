using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MechanicCatalogIntegrationTests
{
    [Fact]
    public void AllFamiliesHaveUniqueStableRegistrationsAndSerializableDiscoveryMetadata()
    {
        var descriptors = MechanicCatalog.All;
        Assert.NotEmpty(descriptors);
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enum.GetValues<MechanicFamily>(), descriptors.Select(d => d.Family).Distinct().Order().ToArray());
        foreach (var descriptor in descriptors)
        {
            Assert.Same(descriptor, MechanicCatalog.Find(descriptor.Id));
            Assert.NotEmpty(descriptor.Profiles);
            Assert.Equal(descriptor.Profiles.Length,
                descriptor.Profiles.Select(p => (p.Source, p.Timing)).Distinct().Count());
        }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(descriptors, options);
        var restored = JsonSerializer.Deserialize<MechanicDescriptor[]>(json, options)!;
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
        Assert.DoesNotContain("PayloadType", json);
        Assert.DoesNotContain("System.", json);
        Assert.Null(MechanicCatalog.Find("unregistered-mechanic"));
    }

    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 5, Height = 4 },
        UnitTypeIds = ["catalog-bearer", "catalog-friend", "catalog-opponent"],
        UnitTypes =
        [
            new("catalog-bearer", 2, 3, 1, 1, 4)
            {
                Unique = true, CanHaveEquipment = true, CanHaveItems = true,
                LocalAbilities =
                [
                    new("mend", "Silver Thread", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(2) },
                    new("wave", "Quiet Bell", AbilityTiming.Action, new HolyWaveMechanic()) { Usage = new(2) },
                    new("flame", "Orange Paper", AbilityTiming.Action, new FireballMechanic()),
                    new("focus", "Careful Moment", AbilityTiming.BonusAction,
                        new SelfModifiersMechanic([new(Stat.Atk, 1), new(Stat.Mov, -1)])) { Usage = new(3) },
                    new("ward", "Nearby Shelter", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 1)])),
                    new("pace", "Another Moment", AbilityTiming.BonusAction, new ExtraActionsMechanic(1)),
                    new("rise", "Helping Hand", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())
                ]
            },
            new("catalog-friend", 0, 0, 0, 0, 4) { Unique = true },
            new("catalog-opponent", 0, 0, 0, 0, 4) { Unique = true }
        ],
        EquipmentTypes = [new("woven-band", "Woven Band") { LocalAbilities = [new("restore", "Woven Light", AbilityTiming.Action, new HealMechanic(3))] }],
        ItemTypes = [new("paper-bell", "Paper Bell") { LocalAbilities = [new("unfold", "Folded Hush", AbilityTiming.Action, new HolyWaveMechanic())] }],
        Units =
        [
            new("catalog-bearer", "blue", new(2, 2), UnitId: "actor")
            {
                Inventory = [new("band-instance", "woven-band"), new("bell-instance") { ItemTypeId = "paper-bell" }]
            },
            new("catalog-friend", "blue", new(1, 2), InitialHp: 1, UnitId: "friend"),
            new("catalog-opponent", "red", new(3, 2), UnitId: "enemy")
        ],
        Agency =
        [
            new("catalog-bearer", "blue", ControllerKind.Human),
            new("catalog-friend", "blue", ControllerKind.Human),
            new("catalog-opponent", "red", ControllerKind.Human)
        ],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    [Fact]
    public void UnfamiliarJsonDefinitionsUseTheRegisteredContractsAndKeepSourceAccountingOnResume()
    {
        var scenario = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(Scenario()));
        var state = GameEngine.CreateGame(scenario);
        var grants = state.AbilitiesOf("actor").ToArray();
        Assert.Equal(9, grants.Length);
        foreach (var grant in grants)
        {
            var source = Enum.Parse<MechanicSource>(grant.SourceKind.ToString());
            var timing = Enum.Parse<MechanicTiming>(grant.Definition.Timing.ToString());
            Assert.Empty(MechanicCatalog.Validate(grant.Definition.Mechanic,
                new(source, timing, grant.Definition.Usage.MaxUses)));
            Assert.NotNull(MechanicCatalog.FindByPayload(grant.Definition.Mechanic));
        }

        state.Round = 1;
        state.ActiveToken = new("catalog-bearer", "blue");
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "catalog-bearer", "actor", [], false);
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var key = state.Pending!.Candidates.Single(c => c.Ability is
            { SourceKind: AbilitySourceKind.Equipment, Key: "restore" }).Key;
        Assert.Contains(resumed.Pending!.Candidates, c => c.Key == key);

        var result = GameEngine.Advance(state, new Choice(key), new Dice(), false);
        var replay = GameEngine.Advance(resumed, new Choice(key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        Assert.Equal(4, replay.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal("Woven Light", replay.Events.Single(e => e.Kind == "HealResolved").AbilityName);
        Assert.Equal(new AbilityUses(2, 2), replay.State.AbilityUsesOf("actor", grants.Single(g => g.Key == "mend")));
        Assert.Equal(2, replay.State.Units.Single(u => u.Id == "actor").Inventory.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void JsonValidationReportsTheSameInvalidParameterAsPublicDiscoveryValidation(int amount)
    {
        var invalid = new HealMechanic(amount);
        var diagnostics = MechanicCatalog.Validate(invalid, path: "unitTypes[0].localAbilities[0].mechanic");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("minimum", diagnostic.Code);
        Assert.EndsWith(".amount", diagnostic.Path);

        var scenario = Scenario();
        scenario.UnitTypes![0] = scenario.UnitTypes[0] with { LocalAbilities = scenario.UnitTypes[0].LocalAbilities.SetItem(0, scenario.UnitTypes[0].LocalAbilities[0] with { Mechanic = invalid }) };
        var error = Assert.Throws<ArgumentException>(() =>
            ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        Assert.Contains(".amount", error.Message);
        Assert.Contains(diagnostic.Message, error.Message);
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
