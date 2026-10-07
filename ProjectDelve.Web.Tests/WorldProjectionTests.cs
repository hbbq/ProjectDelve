using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class WorldProjectionTests
{
    [Fact]
    public void DrawAndAtomicReplacementProjectTheirOwnAuthoritativeRowsAndContent()
    {
        var state = GameEngine.CreateGame(Scenario.Define(Scenario.Map(4, 4),
            [Scenario.Group(UnitTypeIds.Barbarian, "red", ControllerKind.Human, Scenario.At(0, 0))],
            worldEffects: new(2, 1)));
        // Deterministic top cards; card identities and all remaining copies stay physical.
        var calm = state.WorldDeck!.DrawPile.Single(c => c.Id == "world-Calm-1");
        state.WorldDeck.DrawPile.Remove(calm); state.WorldDeck.DrawPile.Insert(1, calm);
        var result = GameEngine.StartRound(state, new Dice(), false);
        var projection = BrowserProjection.Create(result);
        var draws = result.Events.Select((e, i) => (e, i)).Where(x => x.e.Kind == "WorldCardDrawn").ToArray();
        var firstDraw = projection.ResolutionSteps.Single(s => s.EventIndex == draws[0].i).WorldEffects!;
        Assert.Empty(firstDraw.ActiveContinuous);
        Assert.Equal("Bloodlust", firstDraw.ResolvingCard!.Name);
        Assert.Equal("All Units have ATK +1.", firstDraw.ResolvingCard.Text);
        var secondDraw = projection.ResolutionSteps.Single(s => s.EventIndex == draws[1].i).WorldEffects!;
        Assert.Equal("Bloodlust", Assert.Single(secondDraw.ActiveContinuous).Name);
        Assert.Equal("Calm", secondDraw.ResolvingCard!.Name);
        Assert.Equal("Calm", Assert.Single(projection.WorldEffects!.ActiveContinuous).Name);
        Assert.Equal(38, projection.WorldEffects.DrawPileCount);
        Assert.Equal(1, projection.WorldEffects.DiscardPileCount);
        Assert.Contains(projection.Events, e => e.Text.Contains("simultaneously cycles out"));
        Assert.Contains(projection.Events, e => e.Text.Contains("Continuous") && e.Text.Contains("No Effect."));
        Assert.Equal(projection.WorldEffects, JsonSerializer.Deserialize<BrowserWorldEffects>(JsonSerializer.Serialize(projection.WorldEffects))!
            with { ActiveContinuous = projection.WorldEffects.ActiveContinuous });
    }

    [Theory]
    [InlineData("WorldDamageResolved", OutcomeRole.Damage)]
    [InlineData("HealResolved", OutcomeRole.Healing)]
    [InlineData("DoorOpened", OutcomeRole.DoorOpened)]
    [InlineData("DoorClosed", OutcomeRole.DoorClosed)]
    public void WorldOutcomesReuseOrdinaryDamageHealingAndDoorPresentation(string kind, OutcomeRole role)
    {
        var state = GameEngine.CreateGame(Scenario.Define(Scenario.Map(3, 3), []));
        var effect = kind switch
        {
            "WorldDamageResolved" => WorldEffect.Repulsion, "HealResolved" => WorldEffect.Miracle,
            "DoorOpened" => WorldEffect.OpenSesame, _ => WorldEffect.Lockdown
        };
        var outcome = BrowserProjection.Outcome(new RulesEvent(kind, TargetId: "recipient", Damage: 1, Healing: 2,
            Door: new(new(0, 0), new(1, 0), kind == "DoorClosed" ? EdgeKind.ClosedDoor : EdgeKind.OpenDoor))
            { WorldCard = new("physical-card", effect) }, state);
        Assert.Equal(role, outcome.Role);
        Assert.Contains(WorldCards.Content(effect).Name, outcome.Text);
        Assert.DoesNotContain("Dice", outcome.Text);
    }

    private sealed class Dice : IRandomProvider
    {
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
