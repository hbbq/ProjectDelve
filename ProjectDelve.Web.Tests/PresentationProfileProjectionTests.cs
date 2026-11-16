using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class PresentationProfileProjectionTests
{
    private static ScenarioDefinition Definition(bool fog = false)
    {
        var source = OwnedAbilityWebFixture.Definition(fog);
        return source with
        {
            UnitTypes = source.UnitTypes!.Select(type => type with
            { LocalAbilities = [.. type.LocalAbilities.Select(a => a with { PresentationProfileId = type.Id == "concealed-keeper" ? "secret.native" : a.Key == "pulse" ? "native.healing" : null })] }).ToList(),
            EquipmentTypes = source.EquipmentTypes!.Select(type => type with
            { LocalAbilities = [.. type.LocalAbilities.Select(a => a with { PresentationProfileId = type.Id == "concealed-lantern" ? "secret.equipment" : "unfamiliar.lantern" })] }).ToList(),
            ItemTypes = source.ItemTypes!.Select(type => type with
            { LocalAbilities = [.. type.LocalAbilities.Select(a => a with { PresentationProfileId = type.Id == "concealed-paper" ? "secret.item" : type.Id == "folded-remedy" ? "item.healing" : null })] }).ToList()
        };
    }

    [Fact]
    public void ScenarioTransportSaveAndWorkspacePreserveProfiles()
    {
        var source = Definition();
        var definitions = new[] { ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(source)),
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(source)) };
        foreach (var definition in definitions)
        {
            var state = GameEngine.CreateGame(definition);
            var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
            Assert.Equal("native.healing", restored.Types.Single(t => t.Id == "woven-keeper").LocalAbilities[0].PresentationProfileId);
            Assert.Equal("unfamiliar.lantern", restored.Equipment("hollow-lantern").LocalAbilities[0].PresentationProfileId);
            Assert.Equal("item.healing", restored.Item("folded-remedy").LocalAbilities[0].PresentationProfileId);
        }
        var directory = Path.Combine(Path.GetTempPath(), "delve-profile-workspace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new ContentWorkspace(directory, () => new ContentBundle());
            var saved = workspace.Save(new ContentBundle
            {
                Units = [.. source.UnitTypes!], Equipment = [.. source.EquipmentTypes!], Items = [.. source.ItemTypes!]
            }, workspace.Snapshot().Token);
            var loaded = new ContentWorkspace(directory, () => new ContentBundle()).Snapshot();
            Assert.Equal(saved.Token, loaded.Token);
            Assert.Equal("native.healing", loaded.Definitions.Units.Single(t => t.Id == "woven-keeper").LocalAbilities[0].PresentationProfileId);
            Assert.Equal("unfamiliar.lantern", loaded.Definitions.Equipment.Single(t => t.Id == "hollow-lantern").LocalAbilities[0].PresentationProfileId);
            Assert.Equal("item.healing", loaded.Definitions.Items.Single(t => t.Id == "folded-remedy").LocalAbilities[0].PresentationProfileId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ResolvedSourcesOverrideSharedMechanicsAndKeepAccountingAndGroupedItems()
    {
        var state = Ready(GameEngine.CreateGame(Definition()));
        var withoutProfiles = Ready(OwnedAbilityWebFixture.State());
        Assert.Equal(withoutProfiles.Pending!.Candidates.Select(c => c.Key), state.Pending!.Candidates.Select(c => c.Key));
        Assert.Equal(withoutProfiles.AbilitiesOf("actor").Select(a => a.AccountingId), state.AbilitiesOf("actor").Select(a => a.AccountingId));
        Assert.DoesNotContain("presentationProfileId", JsonSerializer.Serialize(withoutProfiles));
        var before = JsonSerializer.Serialize(state);
        var decision = BrowserProjection.Decision(state.Pending, state)!;
        var card = BrowserProjection.Cards(state)["actor"];
        foreach (var candidate in state.Pending!.Candidates.Where(c => c.Ability is not null))
        {
            var grant = candidate.Ability!;
            var projected = Assert.Single(decision.Candidates, c => c.Key == candidate.Key);
            var mechanicId = MechanicCatalog.FindByPayload(grant.Definition.Mechanic)!.Id;
            Assert.Equal(grant.Definition.PresentationProfileId ?? mechanicId, projected.PresentationProfileId);
            Assert.Equal(grant.AccountingId, projected.AccountingId);
            var entry = Assert.Single(card.Entries, e => e.AccountingId == grant.AccountingId);
            Assert.Equal(mechanicId, entry.MechanicId);
            Assert.Equal(projected.PresentationProfileId, entry.PresentationProfileId);
        }
        var healEntries = card.Entries.Where(e => e.MechanicId == "adjacentUnitHeal").ToArray();
        Assert.Equal(new[] { "native.healing", "item.healing" }, healEntries.Select(e => e.PresentationProfileId));
        var item = Assert.Single(healEntries, e => e.Source == "Item");
        Assert.Equal(2, item.CopyCount);
        Assert.Equal(2, decision.Candidates.Count(c => c.AccountingId == item.AccountingId));
        Assert.Equal("unfamiliar.lantern", Assert.Single(card.Entries, e => e.Source == "Equipment").PresentationProfileId);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void DefinitionCatalogExposesExplicitAndEffectiveIdentityAndFallback()
    {
        var catalog = ContentBrowserCatalog.Scenario(Definition());
        var native = Assert.Single(catalog.Entries, e => e.Id == "woven-keeper").Abilities;
        var heal = Assert.Single(native, a => a.Key == "pulse");
        Assert.Equal("adjacentUnitHeal", heal.MechanicId);
        Assert.Equal("native.healing", heal.PresentationProfileId);
        Assert.Equal("native.healing", heal.EffectivePresentationProfileId);
        var passive = Assert.Single(native, a => a.Key == "shade");
        Assert.Null(passive.PresentationProfileId);
        Assert.Equal(passive.MechanicId, passive.EffectivePresentationProfileId);
        Assert.Equal("unfamiliar.lantern", Assert.Single(catalog.Entries.Single(e => e.Id == "hollow-lantern").Abilities).EffectivePresentationProfileId);
    }

    [Fact]
    public void DuplicateEquipmentMechanicsKeepEachSourceProfileAndResolvedCandidateIsAuthoritative()
    {
        var source = Definition();
        var second = source.EquipmentTypes!.Single(e => e.Id == "hollow-lantern") with
        {
            Id = "second-lantern", Name = "Same printed name",
            LocalAbilities = [.. source.EquipmentTypes!.Single(e => e.Id == "hollow-lantern").LocalAbilities.Select(a => a with { PresentationProfileId = "second.lantern" })]
        };
        var actor = source.Units.Single(u => u.UnitId == "actor");
        var state = Ready(GameEngine.CreateGame(source with
        {
            EquipmentTypes = [.. source.EquipmentTypes!, second],
            Units = source.Units.Select(u => u.UnitId == "actor" ? actor with { Inventory = [.. actor.Inventory!, new("lantern-two", second.Id)] } : u).ToList()
        }));
        var entries = BrowserProjection.Cards(state)["actor"].Entries.Where(e => e.Source == "Equipment").ToArray();
        Assert.Equal(new[] { "unfamiliar.lantern", "second.lantern" }, entries.Select(e => e.PresentationProfileId));
        Assert.Single(entries.Select(e => e.MechanicId).Distinct());
        Assert.Equal(2, entries.Select(e => e.AccountingId).Distinct().Count());
        var original = state.Pending!.Candidates.First(c => c.Ability?.SourceTypeId == second.Id);
        var resolved = original.Ability! with { Definition = original.Ability.Definition with { PresentationProfileId = "resolved.override", Name = "Renamed" } };
        var request = state.Pending with { Candidates =
        [
            original with { Key = "unit-choice", Ability = resolved, TargetId = "target" },
            original with { Key = "position-choice", Ability = resolved, Destination = new(0, 1) },
            original with { Key = "mixed-choice", Ability = resolved, TargetId = "target", Destination = new(0, 1) }
        ] };
        var projected = BrowserProjection.Decision(request, state)!;
        Assert.All(projected.Candidates, c => Assert.Equal("resolved.override", c.PresentationProfileId));
        Assert.All(projected.Candidates, c => Assert.Equal(resolved.AccountingId, c.AccountingId));
        Assert.Equal(new[] { InteractionKind.Unit, InteractionKind.Position, InteractionKind.Position }, projected.Candidates.Select(c => c.Interaction.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovementFollowUpsUseTheirDeclarationAndStayUsesSystemProfile(bool advance)
    {
        var source = Definition();
        var type = source.UnitTypes!.Single(t => t.Id == "woven-keeper");
        var followUp = new LocalAbilityDefinition("follow", "Printed continuation", AbilityTiming.AfterAttack,
            advance ? new AdvanceOnKill() : new MoveAfterAttack(2)) { PresentationProfileId = "custom.follow-up" };
        var state = GameEngine.CreateGame(source with { UnitTypes = source.UnitTypes!.Select(t => t.Id == type.Id ? t with { LocalAbilities = t.LocalAbilities.Add(followUp) } : t).ToList() });
        state.MoveAfterAttackAllowance = 2;
        if (advance) state.AdvanceOnKillCells = [new(2, 1)];
        var request = new DecisionRequest(DecisionKind.Move, type.Id, "actor", [new("destination", Destination: new(0, 1), Kind: ActivationChoiceKind.Move)], true, IsMoveAfterAttack: true);
        var before = JsonSerializer.Serialize(state);
        var projected = BrowserProjection.Decision(request, state)!;
        Assert.Equal("custom.follow-up", Assert.Single(projected.Candidates).PresentationProfileId);
        Assert.Null(request.Candidates[0].Ability);
        Assert.Equal("stay", projected.NoneChoice!.PresentationProfileId);
        Assert.Equal(before, JsonSerializer.Serialize(state));
        var fallback = followUp with { PresentationProfileId = null };
        state.Types[state.Types.FindIndex(t => t.Id == type.Id)] = type with { LocalAbilities = type.LocalAbilities.Add(fallback) };
        Assert.Equal(advance ? "advanceOnKill" : "moveAfterAttack", Assert.Single(BrowserProjection.Decision(request, state)!.Candidates).PresentationProfileId);
    }

    [Fact]
    public void BuiltinChoicesHaveStableProfilesRegardlessOfLabelsOrKeys()
    {
        var state = GameEngine.CreateGame(Definition());
        var candidates = new List<Candidate>
        {
            new("arbitrary-m", Destination: new(0, 1), Kind: ActivationChoiceKind.Move),
            new("arbitrary-s", Kind: ActivationChoiceKind.Stay),
            new("arbitrary-e", Kind: ActivationChoiceKind.EndTurn),
            new("actor", Kind: ActivationChoiceKind.SelectUnit),
            new("arbitrary-r", Kind: ActivationChoiceKind.RollDice),
            new("arbitrary-a", Action: UnitAction.NormalAttack, TargetId: "target"),
            new("arbitrary-c", Kind: ActivationChoiceKind.FreeAction) { ObjectId = "hidden-ground-paper", ItemTypeId = "concealed-paper" }
        };
        var projected = BrowserProjection.Decision(new(DecisionKind.Activation, "woven-keeper", "actor", candidates, false), state)!;
        Assert.Equal(new[] { "move", "stay", "end-turn", "activate", "roll-dice", "standard-attack", "collect-object" }, projected.Candidates.Select(c => c.PresentationProfileId));
        var decline = BrowserProjection.Decision(new(DecisionKind.Cleave, "woven-keeper", "actor", [], true), state)!;
        Assert.Equal("decline-follow-up", decline.NoneChoice!.PresentationProfileId);
    }

    [Fact]
    public void HiddenSourcesDoNotPublishProfileMetadata()
    {
        var state = GameEngine.CreateGame(Definition(fog: true));
        using var game = new PlaytestGame(new Dice(), state);
        var json = JsonSerializer.Serialize(game.BrowserResponse(game.Snapshot()));
        Assert.Contains("native.healing", json);
        foreach (var hidden in new[] { "secret.native", "secret.equipment", "secret.item" }) Assert.DoesNotContain(hidden, json);
    }

    [Fact]
    public void PresentationCatalogCoversAllTypedMechanicsAndSystemChoices()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/wwwroot/presentation/profiles.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var ids = document.RootElement.GetProperty("profiles").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in MechanicCatalog.All.Select(m => m.Id).Concat(new[] { "move", "stay", "standard-attack", "end-turn", "activate", "collect-object", "roll-dice", "decline-follow-up", "start-round" }))
            Assert.Contains(id, ids);
    }

    private static GameState Ready(GameState state)
    {
        state.Round = 1;
        state.ActiveToken = new("woven-keeper", "blue");
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "woven-keeper", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
}
