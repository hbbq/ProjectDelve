using ProjectDelve.Engine;
using ProjectDelve.Generation;
using System.Collections.Immutable;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionStationIntegrationTests
{
    [Fact]
    public void RefreshedOperateSourceCanChangeOriginalOperatorWithoutChangingPhysicalIdentities()
    {
        var content = ContentBundle.Canonical();
        var chosen = ExpeditionParty.Originals[^1];
        content = content with { Units = content.Units.Select(type =>
            ExpeditionParty.Originals.Any(hero => hero.TypeId == type.Id)
                ? type with { LocalAbilities = type.LocalAbilities.Where(ability => ability.Mechanic is not Operate).ToImmutableArray() }
                : type).ToImmutableArray() };
        content = content with { Units = content.Units.Select(type => type.Id == chosen.TypeId
            ? type with { LocalAbilities = type.LocalAbilities.Add(new("refreshed-operate", "Operate", AbilityTiming.Action, new Operate())) }
            : type).ToImmutableArray() };
        var settings = ExpeditionWorld.Configure(17, new(new(0, 0, 60, 60), new(0, 0, 0),
            Civilization: new(Enabled: false), Routes: new(Enabled: false), Population: new(PopulationDensity.Off)));
        var plan = ExpeditionWorld.Initial(17);
        var geometry = ExpeditionWorld.Generate(plan, content, settings, ExpeditionWorldProfile.Create(settings.NaturalTerrain));
        var scenario = ExpeditionParty.Prepare(geometry, content, plan.ExitArea, ExpeditionParty.Originals, null);
        var state = GameEngine.CreateGame(scenario, content);
        Assert.True(GameEngine.CanOperateMachineWithAction(state, chosen.InstanceId, ExpeditionRegionGeometry.ArrivalStationId));
        Assert.Equal(ExpeditionParty.Originals.Select(hero => hero.InstanceId).Order(), state.Units.Select(u => u.Id).Order());
        Assert.All(state.Physical.Figures, figure => Assert.Contains(figure.Position, geometry.Setup!.Deployment.Cells));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleRefreshedOperateSourcesFailPartyPreparation(bool unaffordable)
    {
        var content = ContentBundle.Canonical();
        content = content with { Units = content.Units.Select(type =>
        {
            if (!ExpeditionParty.Originals.Any(hero => hero.TypeId == type.Id)) return type;
            var abilities = type.LocalAbilities.Where(ability => ability.Mechanic is not Operate).ToImmutableArray();
            if (unaffordable) abilities = abilities.Add(new("unavailable-operate", "Operate", AbilityTiming.Action,
                new Operate()) { Costs = [new SpendResource("diamond", 1)] });
            return type with { LocalAbilities = abilities };
        }).ToImmutableArray() };
        var settings = ExpeditionWorld.Configure(17, new(new(0, 0, 60, 60), new(0, 0, 0),
            Civilization: new(Enabled: false), Routes: new(Enabled: false), Population: new(PopulationDensity.Off)));
        var plan = ExpeditionWorld.Initial(17);
        var geometry = ExpeditionWorld.Generate(plan, content, settings, ExpeditionWorldProfile.Create(settings.NaturalTerrain));
        Assert.Contains("cannot immediately Operate", Assert.Throws<ArgumentException>(() =>
            ExpeditionParty.Prepare(geometry, content, plan.ExitArea, ExpeditionParty.Originals, null)).Message);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(42)]
    public void EveryNewRegionPlacesHeroStationOperableBeforeAnyMoveAndKeepsPopulationAccess(int seed)
    {
        var content = ContentBundle.Canonical();
        var settings = ExpeditionWorld.Configure((ulong)seed);
        var profile = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
        var plan = ExpeditionWorld.Initial((ulong)seed);
        for (var visit = 0; visit < 2; visit++, plan = ExpeditionWorld.Next((ulong)seed, plan))
        {
            var context = ExpeditionRegionGeometry.Execute(ExpeditionWorld.RecipeFor(plan, settings),
                plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile: profile).Context;
            var geometry = context.Materialize();
            var scenario = ExpeditionParty.Prepare(geometry, content, plan.ExitArea, ExpeditionParty.Originals, null);
            var state = GameEngine.CreateGame(scenario, content);
            var station = Assert.Single(state.Physical.Board.Machines,
                machine => machine.Id == ExpeditionRegionGeometry.ArrivalStationId);
            Assert.Equal(ExpeditionParty.SideId, station.OwnerSide);
            Assert.All(state.Physical.Board.Machines.Where(m => m.Id == ExpeditionRegionGeometry.AdditionalStationId),
                machine => Assert.Null(machine.OwnerSide));
            Assert.True(context.Expedition!.HasOccupiedAccess(GameEngine.CreateGame(geometry, content), context.Deployment!,
                ExpeditionGeometryPlan.OutpostDoorCells(context.Sites)));
            var operatorId = ExpeditionParty.Originals[0].InstanceId;
            var actor = state.Units.Single(u => u.Id == operatorId);
            state.Round = 1; state.RoundComplete = false; state.ActiveToken = actor.Token;
            state.CurrentUnitId = operatorId; state.MoveDone = true;
            state.ResolvingTokenUnitIds = [operatorId];
            state.Pending = new(DecisionKind.Activation, actor.TypeId, operatorId, [], false) { Token = actor.Token };
            state = GameEngine.RefreshChoices(state, new ExpeditionRandom(1), false).State;
            Assert.Contains(state.Pending!.Candidates, candidate => candidate.Action == UnitAction.Operate &&
                candidate.MachineId == station.Id && candidate.RequiresMachineInteraction);
            Assert.Equal(scenario.Setup!.Deployment.PlacementOrder[0],
                state.Physical.Figures.Single(f => f.Id == operatorId).Position);
        }
    }
}
