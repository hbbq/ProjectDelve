using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class FriendlySwapProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Beastmaster) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Fact]
    public async Task FriendlySwapCardsCandidatesAtomicPlaybackAndFogFilteredReplayUseSharedPresentation()
    {
        var definition = Scenario.Define(Scenario.Map(20, 3),
        [Scenario.Group(UnitTypeIds.Beastmaster, "blue", ControllerKind.Human, Scenario.At(1, 1, unitId: "master")),
         Scenario.Group(UnitTypeIds.Jackalope, "blue", ControllerKind.Human, Scenario.At(2, 1, unitId: "friend")),
         Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(19, 1, unitId: "hidden"))]) with
        { FogOfWarSides = ["blue"], Loot = LootSettings.Empty, Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)] };
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(decoded));
        game.SetRelevanceAutoChoice(0, false); var start = game.StartRound(1);
        var presentation = game.BrowserResponse(start).Presentation;
        var entry = Assert.Single(presentation.Cards["master"].Entries, e => e.Content.Id == "ability:friendly-swap");
        Assert.Equal("Bonus Action", entry.Content.Category); Assert.Null(entry.Uses);
        Assert.Equal("Swap places with an adjacent friendly Unit.", entry.Content.Description);
        var candidate = Assert.Single(presentation.Decision!.Candidates, c => c.EntryId == entry.Content.Id);
        Assert.Equal(InteractionKind.Unit, candidate.Interaction.Kind);
        Assert.Equal(new[] { "friend" }, candidate.AffectedUnitIds);
        Assert.DoesNotContain("hidden", presentation.Cards.Keys);
        using var live = game.Subscribe(null); var baseline = await live.Reader.ReadAsync();
        var choice = start.Result.NextInput!.Candidates.Single(c => c.Ability?.Key == "friendly-swap");
        var swapped = game.Decide(start.Revision, choice.Key);
        var after = game.BrowserResponse(swapped).Presentation;
        var eventIndex = swapped.Result.Events.FindIndex(e => e.Kind == "PlacesSwapped");
        var step = after.ResolutionSteps.Single(s => s.EventIndex == eventIndex);
        Assert.Equal(new(2, 1), step.Figures["master"].Anchor); Assert.Equal(new(1, 1), step.Figures["friend"].Anchor);
        Assert.DoesNotContain(after.Decision!.Candidates, c => c.EntryId == entry.Content.Id);
        var updates = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) updates.Add(game.BrowserUpdate(update));
        Assert.NotEmpty(updates);
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(JsonSerializer.Serialize(updates), JsonSerializer.Serialize(replayed));
        Assert.DoesNotContain(updates.SelectMany(u => u.Snapshot.Result.State.Units), u => u.Id == "hidden");
    }
}
