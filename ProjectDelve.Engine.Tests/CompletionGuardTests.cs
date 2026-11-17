using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CompletionGuardTests
{
    private const int Width = 12, Height = 8;
    private static (PlanningBoard Board, CompletionGuard Guard, HeroDeployment Deployment) Context(int budget = 2_000_000)
    {
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = Width, Height = Height, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        var area = Enumerable.Range(5, 2).SelectMany(y => Enumerable.Range(0, 5).Select(x => y * Width + x)).ToArray();
        var core = area.Where(i => i % Width < 4).ToArray();
        var deployment = new HeroDeployment(5, Area("spawn", area), Area("core", core),
            core.Concat(area.Except(core)).ToImmutableArray(), 76, 77, 0);
        var reserve = Draft(board, "hero-deployment"); reserve.Reserve(area.Append(77));
        Assert.True(board.TryCommit(reserve.BuildPatch(), out _));
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), deployment));
        return (board, new(board, deployment, Enumerable.Range(0, Width * Height), budget), deployment);
    }
    private static BoardDraft Draft(PlanningBoard board, string owner) => board.BeginDraft(new(0, 0, Width, Height), owner, new());
    private static GeneratedArea Area(string id, params int[] cells) => GeneratedArea.From(id, cells, Width);
    private static RequiredLocation Location(string id, params int[] positions) => new(id, "fixture", id,
        [new("usable", RequiredAccessCoverage.All, positions.ToImmutableArray())]);
    private static SiteInfo Site(string id, int[] rocks, int[]? passage = null)
    {
        var open = passage ?? [];
        return new(id, open.Length > 0 ? GeneratedSiteKind.Campsite : GeneratedSiteKind.Settlement,
            Area(id + "/footprint", rocks.Concat(open).ToArray()), Area(id + "/exterior", open), [], [],
            new(open.Length, open.Length, true, false, 0), [], [], [], null, PreparationSummary.Empty);
    }
    private static BoardPatch Barrier(PlanningBoard board, SiteInfo site)
    {
        var draft = Draft(board, site.Id);
        var usable = site.Exterior.Indices(Width).ToHashSet();
        foreach (var i in site.Footprint.Indices(Width)) if (!usable.Contains(i)) draft.SetTerrain(i, "solid-rock");
        draft.Reserve(site.Footprint.Indices(Width));
        return draft.BuildPatch();
    }
    private static void RequireNorth(PlanningBoard board, CompletionGuard guard)
    {
        Assert.True(guard.TryPublish(Draft(board, "initial").BuildPatch(), "initial", "fixture", out var result,
            [Location("northern-destination", 2)]), result.Detail);
    }

    [Fact]
    public void CompleteNorthSouthGateMayForceAllTransitThroughItsProtectedInterior()
    {
        var (board, guard, _) = Context(); RequireNorth(board, guard);
        var gate = Site("rock-gate", Enumerable.Range(36, 12).Where(i => i != 41).ToArray(), [41]);
        Assert.True(guard.TryPublish(Barrier(board, gate), "gate", "fixture", out var result, newSites: [gate]), result.Detail);
        Assert.Equal(CompletionStatus.Allowed, result.Status); Assert.Contains(41, guard.Witness!.Cells);
        Assert.Contains(guard.Registry.Locations, l => l.SourceId == "rock-gate" && l.AccessClass == RequiredAccessClass.Ordinary);
        Assert.Equal("rock-gate", board.State.Reservations[41]);
    }

    [Fact]
    public void AdjacentProtectedStructuresCanShareAnOpenPassage()
    {
        var (board, guard, _) = Context(); RequireNorth(board, guard);
        var west = Site("west", Enumerable.Range(36, 5).ToArray(), [41]);
        var east = Site("east", Enumerable.Range(42, 6).ToArray());
        Assert.True(guard.TryPublish(Barrier(board, west), "west", "fixture", out _, newSites: [west]));
        Assert.True(guard.TryPublish(Barrier(board, east), "east", "fixture", out var result, newSites: [east]), result.Detail);
        Assert.Contains(41, guard.Witness!.Cells); Assert.Equal(2, guard.Sites.Length);
        Assert.Equal("west", board.State.Reservations[41]); Assert.Equal("east", board.State.Reservations[42]);
    }

    [Fact]
    public void JointBlockageIsRejectedAtomicallyAndAlternativePlacementSucceeds()
    {
        var (board, guard, _) = Context(); RequireNorth(board, guard);
        var west = Site("west", Enumerable.Range(36, 6).ToArray());
        Assert.True(guard.TryPublish(Barrier(board, west), "west", "fixture", out var rerouted, newSites: [west]));
        Assert.False(rerouted.ReusedWitness);
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        var oldRegistry = JsonSerializer.Serialize(guard.Registry.Locations); var oldWitness = guard.Witness;
        var blocked = Site("east", Enumerable.Range(42, 6).ToArray());
        Assert.False(guard.TryPublish(Barrier(board, blocked), "east/attempt-0", "fixture", out var rejection,
            [Location("unpublished-candidate-location", 4)], [blocked]));
        Assert.Equal(CompletionStatus.Impossible, rejection.Status);
        Assert.Contains(rejection.Failures, f => f.LocationId == "northern-destination");
        Assert.Contains(rejection.Frontier, f => f.OwnerId == "west");
        Assert.Contains(rejection.Frontier, f => f.OwnerId == "east");
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(revision, board.Revision);
        Assert.Equal(oldRegistry, JsonSerializer.Serialize(guard.Registry.Locations)); Assert.Same(oldWitness, guard.Witness);
        Assert.Single(guard.Sites); Assert.False(board.IsReserved(42)); Assert.DoesNotContain(guard.Registry.Locations, l => l.Id == "unpublished-candidate-location");
        var alternative = Site("east", Enumerable.Range(42, 5).ToArray());
        Assert.True(guard.TryPublish(Barrier(board, alternative), "east/attempt-1", "fixture", out var success, newSites: [alternative]), success.Detail);
        Assert.Contains(guard.Registry.Locations, l => l.Id == "northern-destination");
        Assert.True(guard.TryPublish(Draft(board, "inspection").BuildPatch(), "inspection", "fixture", out var reused));
        Assert.True(reused.ReusedWitness);
        Assert.Contains(guard.Witness!.Cells, i => !board.IsReserved(i));
    }

    [Fact]
    public void IncompleteSearchCannotPublishOrClaimImpossibility()
    {
        var (board, guard, _) = Context(budget: 0); var revision = board.Revision;
        Assert.False(guard.TryPublish(Draft(board, "candidate").BuildPatch(), "candidate", "fixture", out var result, [Location("north", 2)]));
        Assert.Equal(CompletionStatus.Incomplete, result.Status); Assert.Equal(0, result.WorkUsed);
        Assert.Equal(revision, board.Revision); Assert.Empty(guard.Registry.Locations); Assert.Null(guard.Witness);
    }

    [Fact]
    public void OrdinaryAnyAndAllGroupsHaveDifferentAcceptanceAndSpecialMustBeExplicit()
    {
        var (board, guard, _) = Context();
        var block = Site("barrier", Enumerable.Range(36, 12).ToArray());
        var choice = new RequiredLocation("choice", "fixture", "choice", [new("one", RequiredAccessCoverage.Any, [2, 80])]);
        var special = Location("special", 2) with { AccessClass = RequiredAccessClass.DeliberateSpecial, SpecialReason = "Intentional isolated fixture" };
        Assert.True(guard.TryPublish(Barrier(board, block), "barrier", "fixture", out var accepted, [choice, special], [block]), accepted.Detail);
        var both = Location("both", 2, 80);
        Assert.False(guard.TryPublish(Draft(board, "other").BuildPatch(), "other", "fixture", out var rejected, [both]));
        Assert.Equal(CompletionStatus.Impossible, rejected.Status);
        Assert.Equal(new[] { 2 }, Assert.Single(rejected.Failures).Unreachable);
        Assert.DoesNotContain(guard.Registry.Locations, l => l.Id == "both");
        Assert.Equal(RequiredAccessClass.Ordinary, guard.Registry.Locations.Single(l => l.Id == "choice").AccessClass);
        Assert.False(guard.TryPublish(Draft(board, "provenance").BuildPatch(), "provenance", "fixture", out var namedProducer,
            [both with { Id = "producer-name-is-not-a-dispatch-key", ProducerId = "interactive" }]));
        Assert.Equal(CompletionStatus.Impossible, namedProducer.Status);
    }

    [Fact]
    public void EmptyBuildingAndMachineRegisterActualOrdinaryAccess()
    {
        var (board, guard, _) = Context(); var cells = new[] { 8, 9, 20, 21 };
        var room = new RoomInfo("vacant-room", "empty", Area("interior", cells), Area("usable", cells), [], 1);
        var building = new BuildingInfo("house", "empty", Area("house", cells), [room], [], new(4, 4, true, false, 1), []);
        var site = Site("village", [], cells) with { Buildings = [building], Kind = GeneratedSiteKind.Settlement };
        var draft = Draft(board, "village"); draft.Reserve(cells);
        Assert.True(guard.TryPublish(draft.BuildPatch(), "house", "fixture", out var buildingResult, newSites: [site]), buildingResult.Detail);
        var declaredRoom = Assert.Single(guard.Registry.Locations, l => l.SourceId == "vacant-room");
        Assert.Equal(RequiredAccessClass.Ordinary, declaredRoom.AccessClass);
        Assert.Equal(cells, declaredRoom.Groups[0].Positions);
        var machine = Draft(board, "beacon"); machine.PlaceMachine(new("press", new(10, 1), MachineTypes.DiamondPress));
        Assert.True(guard.TryPublish(machine.BuildPatch(), "machine", "fixture", out var machineResult), machineResult.Detail);
        var operation = Assert.Single(guard.Registry.Locations, l => l.SourceId == "press");
        Assert.Equal("fixture", operation.ProducerId);
        Assert.Equal(RequiredAccessCoverage.Any, operation.Groups[0].Coverage);
        Assert.DoesNotContain(22, operation.Groups[0].Positions);
        Assert.NotEmpty(operation.Groups[0].Positions); Assert.Empty(board.Export().Units);
        var original = ScenarioDefinitionJson.ToJson(board.Export());
        var damage = Draft(board, "village"); damage.ReplaceTerrain(cells[0], "solid-rock");
        Assert.False(guard.TryPublish(damage.BuildPatch(), "house-damage", "fixture", out var protectedRoom));
        Assert.Equal(CompletionStatus.Error, protectedRoom.Status);
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Contains(guard.Registry.Locations, l => l.SourceId == "vacant-room");
    }

    [Fact]
    public void EvenSameOwnerCannotConsumeSpawn()
    {
        var (board, guard, deployment) = Context(); RequireNorth(board, guard);
        var malicious = Draft(board, "hero-deployment"); var cell = deployment.PlacementOrder[0]; malicious.ReplaceTerrain(cell, "solid-rock");
        var revision = board.Revision;
        Assert.False(guard.TryPublish(malicious.BuildPatch(), "spawn-damage", "fixture", out var result));
        Assert.Equal(CompletionStatus.Error, result.Status); Assert.Equal(revision, board.Revision);
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), deployment));
    }

    [Fact]
    public void ExistingMachineCannotBeStrandedAndEnclosedMachineCannotPublish()
    {
        var (board, guard, _) = Context();
        var press = Draft(board, "press"); press.PlaceMachine(new("press", new(9, 1), MachineTypes.DiamondPress));
        Assert.True(guard.TryPublish(press.BuildPatch(), "press", "fixture", out _));
        var block = Site("barrier", Enumerable.Range(36, 12).ToArray());
        Assert.False(guard.TryPublish(Barrier(board, block), "barrier", "fixture", out var failure, newSites: [block]));
        Assert.Equal(CompletionStatus.Impossible, failure.Status);
        Assert.Contains(failure.Failures, f => f.LocationId.Contains("machine/press"));
        Assert.Single(board.State.Machines);

        var (other, otherGuard, _) = Context(); var sealedPress = Draft(other, "sealed");
        foreach (var neighbor in other.Neighbors(21)) sealedPress.SetEdge(EdgeKey.Between(21, neighbor, Width), EdgeKind.Wall);
        sealedPress.PlaceMachine(new("sealed", new(9, 1), MachineTypes.DiamondPress));
        Assert.False(otherGuard.TryPublish(sealedPress.BuildPatch(), "sealed", "fixture", out var invalid));
        Assert.Equal(CompletionStatus.Error, invalid.Status); Assert.Empty(other.State.Machines);
        Assert.Empty(otherGuard.Registry.Locations);
    }

    [Fact]
    public void PostGeometryInteractiveRegistrationKeepsIdentityAndFiltersPlacementDomain()
    {
        var (board, guard, _) = Context();
        var block = Site("barrier", Enumerable.Range(36, 12).ToArray());
        Assert.True(guard.TryPublish(Barrier(board, block), "barrier", "fixture", out _, newSites: [block]));
        var domain = guard.InteractivePlacementDomain();
        Assert.Contains(80, domain); Assert.DoesNotContain(2, domain);
        var valid = board.Export() with { LootMarkers = [new("reachable-loot", new(8, 6))] };
        Assert.Equal(CompletionStatus.Allowed, guard.RegisterInteractives(valid, "loot").Status);
        var before = JsonSerializer.Serialize(guard.Registry.Locations);
        var invalid = valid with { LootMarkers = [.. valid.LootMarkers!, new("unreachable-loot", new(2, 0))] };
        var rejected = guard.RegisterInteractives(invalid, "loot");
        Assert.Equal(CompletionStatus.Impossible, rejected.Status);
        Assert.Equal(before, JsonSerializer.Serialize(guard.Registry.Locations));
        Assert.Contains(guard.Registry.Locations, l => l.SourceId == "reachable-loot");
        Assert.Equal("loot", guard.Registry.Locations.Single(l => l.SourceId == "reachable-loot").ProducerId);
        Assert.DoesNotContain(guard.Registry.Locations, l => l.SourceId == "unreachable-loot");
    }

    [Fact]
    public void PopulationReportsLootShortfallCausedByCompletionFiltering()
    {
        const int width = 20, height = 12;
        var content = ContentBundle.Canonical();
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, content);
        GeneratedArea Part(string id, IEnumerable<int> cells) => GeneratedArea.From(id, cells, width);
        var area = Enumerable.Range(10, 2).SelectMany(y => Enumerable.Range(0, 5).Select(x => y * width + x)).ToArray();
        var core = area.Where(i => i % width < 4).ToArray();
        var deployment = new HeroDeployment(5, Part("spawn", area), Part("core", core), [.. core, .. area.Except(core)], 224, 225, 0);
        var reserve = board.BeginDraft(new(0, 0, width, height), "hero-deployment", new()); reserve.Reserve(area.Append(225));
        Assert.True(board.TryCommit(reserve.BuildPatch(), out _));
        var guard = new CompletionGuard(board, deployment, Enumerable.Range(0, width * height));
        var rocks = Enumerable.Range(160, 20).ToArray();
        var barrier = new SiteInfo("barrier", GeneratedSiteKind.Settlement, Part("barrier", rocks), Part("outside", []), [], [],
            new(0, 0, true, false, 0), [], [], [], null, PreparationSummary.Empty);
        var draft = board.BeginDraft(new(0, 0, width, height), "barrier", new());
        foreach (var i in rocks) draft.SetTerrain(i, "solid-rock"); draft.Reserve(rocks);
        Assert.True(guard.TryPublish(draft.BuildPatch(), "barrier", "fixture", out _, newSites: [barrier]));
        var context = new WorldGenerationContext(new(42, new(0, 0, 0)), new(0, 0, width, height));
        context.SampleNaturalTerrain(content); context.ConstructionBoard = board; context.Completion = guard;
        context.Deployment = deployment; context.Sites.Add(barrier); context.PopulationProtectedAreas.Add(deployment.Area);
        context.Loot = PopulationLoot.FromPreset("Classic"); context.RefreshConstructionAnalysis();
        var population = PopulationGenerator.Generate(context, new(PopulationDensity.Medium, 3), content);
        Assert.Empty(population.LootMarkers);
        Assert.Contains(guard.Diagnostics, d => d.Status == CompletionStatus.Omitted && d.CandidateId.EndsWith("/loot") &&
            d.Detail!.Contains("ordinary potential deployment access"));
    }

    [Fact]
    public void CompleteRejectionAndAcceptanceSequenceIsReproducible()
    {
        string Run()
        {
            var (board, guard, _) = Context(); RequireNorth(board, guard);
            foreach (var (id, cells) in new[] { ("west", Enumerable.Range(36, 6).ToArray()), ("east", Enumerable.Range(42, 6).ToArray()) })
            { var site = Site(id, cells); guard.TryPublish(Barrier(board, site), id, "fixture", out _, newSites: [site]); }
            return JsonSerializer.Serialize(new { board = ScenarioDefinitionJson.ToJson(board.Export()), guard.Registry.Locations, guard.Diagnostics });
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void NormalProducersRegisterSitesAndPostGeometryLootDeterministically()
    {
        var preset = WorldGenerationPresets.All.Single(p => p.Id == "Grassland");
        var recipe = new WorldGenerationRecipe(new(-17, -13, 30, 24), preset.Settings, 42, preset.Enrichment,
            preset.Civilization, preset.Routes, preset.Population, "Classic");
        var a = WorldGenerator.Generate(recipe);
        var b = WorldGenerator.Generate(recipe);
        Assert.NotNull(a.Context.Completion); Assert.NotEmpty(a.Context.Sites);
        Assert.Empty(a.Context.Completion!.Registry.Errors);
        Assert.Contains(a.Context.Completion.Registry.Locations, l => l.ProducerId == "civilization");
        Assert.All(a.Definition.LootMarkers ?? [], marker => Assert.Contains(a.Context.Completion.Registry.Locations, l => l.SourceId == marker.Id));
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Context.Completion.Diagnostics), JsonSerializer.Serialize(b.Context.Completion!.Diagnostics));
    }
}
