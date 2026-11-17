using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MandatoryConnectivityRepairTests
{
    private const int Width = 24, Height = 10;
    private static BoardDraft Draft(PlanningBoard board, string owner) => board.BeginDraft(new(0, 0, Width, Height), owner, new());
    private static GeneratedArea Area(string id, IEnumerable<int> indices) => GeneratedArea.From(id, indices, Width);
    private static RequiredLocation Location(string id, params RequiredAccessGroup[] groups) => new(id, "fixture", id, groups.ToImmutableArray());
    private static RequiredAccessGroup All(string id, params int[] indices) => new(id, RequiredAccessCoverage.All, indices.ToImmutableArray());
    private static (PlanningBoard Board, CompletionGuard Guard, HeroDeployment Deployment) Context(Action<BoardDraft>? terrain = null)
    {
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = Width, Height = Height, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        if (terrain is not null)
        { var draft = Draft(board, "natural"); terrain(draft); Assert.True(board.TryCommit(draft.BuildPatch(), out _)); }
        var area = Enumerable.Range(8, 2).SelectMany(y => Enumerable.Range(0, 5).Select(x => y * Width + x)).ToArray();
        var core = area.Where(i => i % Width < 4).ToArray();
        var deployment = new HeroDeployment(5, Area("spawn", area), Area("core", core),
            [.. core, .. area.Except(core)], 220, 221, 0);
        var reserve = Draft(board, "hero-deployment"); reserve.Reserve(area.Append(221));
        Assert.True(board.TryCommit(reserve.BuildPatch(), out _));
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), deployment));
        return (board, new(board, deployment, Enumerable.Range(0, Width * Height)), deployment);
    }
    private static void Register(PlanningBoard board, CompletionGuard guard, params RequiredLocation[] locations) =>
        Assert.True(guard.TryPublish(Draft(board, "fixture").BuildPatch(), "fixture", "fixture", out var diagnostic, locations), diagnostic.Detail);
    private static void TreeBarrier(BoardDraft draft)
    { foreach (var i in Enumerable.Range(Width * 3, Width)) draft.SetTerrain(i, "tree"); }
    private static MandatoryRepairSettings Fallback(int budget = 2_000_000) => new(AnalysisWorkBudget: budget, StyledAttemptsPerPass: 0);
    private static void Covered(PlanningBoard board, CompletionGuard guard, HeroDeployment deployment) =>
        Assert.Empty(guard.ActualFailures(GameEngine.CreateGame(board.Export())));

    [Fact]
    public void AlreadyConnectedDistinctLocationsRequireNoPatchOrRevisionChange()
    {
        var (board, guard, _) = Context();
        Register(board, guard, Location("first-location", All("usable", 2)), Location("second-location", All("usable", 22)));
        var revision = board.Revision; var before = ScenarioDefinitionJson.ToJson(board.Export());
        var report = MandatoryConnectivityRepair.Run(board, guard);
        Assert.True(report.Success); Assert.Empty(report.Attempts); Assert.Equal(0, report.Conversions);
        Assert.Equal(0, report.AcceptedConnections); Assert.Equal(revision, board.Revision);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
    }

    [Fact]
    public void LongRockTunnelEscalatesPastMinimalAndConnectsDistinctLocations()
    {
        var (board, guard, deployment) = Context(draft =>
        {
            foreach (var y in Enumerable.Range(0, Height))
            foreach (var x in Enumerable.Range(6, 15)) draft.SetTerrain(y * Width + x, "solid-rock");
        });
        Register(board, guard, Location("northeast", All("usable", 23)), Location("southeast", All("usable", 239)));
        var report = MandatoryConnectivityRepair.Run(board, guard);
        Assert.True(report.Success); Assert.False(report.UsedFallback); Assert.InRange(report.Conversions, 15, 30);
        Assert.Equal(2, report.AcceptedConnections); Assert.Equal(report.Conversions, report.ConvertedByTerrain["solid-rock"]);
        Assert.Equal(new[] { "minimal", "moderate" }, report.Attempts.Select(a => a.Stage));
        Assert.False(report.Attempts[0].Accepted); Assert.True(report.Attempts[1].Accepted);
        Covered(board, guard, deployment);
    }

    [Fact]
    public void StyledPreparationUsesExplicitActionsAndCreatesNoRouteReservations()
    {
        var (board, guard, deployment) = Context(TreeBarrier);
        Register(board, guard, Location("north", All("all", 2)));
        var reservations = JsonSerializer.Serialize(board.State.Reservations); var revision = board.Revision;
        var report = MandatoryConnectivityRepair.Run(board, guard);
        Assert.True(report.Success); Assert.False(report.UsedFallback); Assert.Equal(1, report.Conversions);
        Assert.Equal("minimal", Assert.Single(report.Attempts).Stage); Assert.Equal(revision + 1, board.Revision);
        Assert.Equal(reservations, JsonSerializer.Serialize(board.State.Reservations));
        Assert.Empty(board.State.RouteCorridors); Assert.Equal(1, report.AcceptedConnections);
        Assert.Equal(1, report.ConvertedByTerrain["tree"]); Assert.Equal(1, report.TotalDisturbance);
        Assert.Equal(report.Conversions, report.Attempts.Sum(a => a.PreparationWork));
        Assert.NotEmpty(report.Attempts[0].WitnessCells); Assert.True(report.Attempts[0].MetadataComplete);
        Covered(board, guard, deployment);
    }

    [Fact]
    public void FallbackUnionsMoreThanEightBranchesAndAllGroupsWhileSpecialRemainsExcluded()
    {
        var (board, guard, deployment) = Context(draft =>
        {
            foreach (var i in Enumerable.Range(Width, Width)) draft.SetTerrain(i, "tree");
            foreach (var x in Enumerable.Range(0, Width).Where(x => x % 2 == 1)) draft.SetTerrain(x, "tree");
        });
        var goals = Enumerable.Range(0, 12).Select(x => 2 * x).ToArray();
        Register(board, guard, Location("branches", All("all", goals), new("choice", RequiredAccessCoverage.Any, [0, 23])),
            Location("special", All("remote", 23)) with { AccessClass = RequiredAccessClass.DeliberateSpecial, SpecialReason = "Deliberate fixture exclusion" });
        var report = MandatoryConnectivityRepair.Run(board, guard, Fallback());
        Assert.True(report.Success); Assert.True(report.UsedFallback); Assert.True(report.Conversions > 8);
        Assert.Equal("tree", board.TerrainAt(23)); Assert.Single(report.Attempts); Covered(board, guard, deployment);
    }

    [Fact]
    public void EscalationAndFallbackHaveNoOptionalRouteOrPreservationBudget()
    {
        var (board, guard, deployment) = Context(draft =>
        {
            foreach (var i in Enumerable.Range(Width * 3, Width)) draft.SetTerrain(i, "water");
            foreach (var i in Enumerable.Range(Width * 4, Width)) draft.SetTerrain(i, "solid-rock");
        });
        Register(board, guard, Location("north", All("all", 2)));
        var unusedOptionalBudget = new PlanningBudget(maxAttempts: 0, maxWork: 0);
        var optional = board.BeginDraft(new(0, 0, Width, Height), "optional-routes", unusedOptionalBudget);
        var route = RouteGenerator.Plan(new("connection", new(0, 0, Width, Height), new("start", [220]), new("goal", [2])),
            new GenerationScope(42, "optional-routes", optional));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, Assert.IsType<Rejected<RoutePlan>>(route).Failure.Code);
        var report = MandatoryConnectivityRepair.Run(board, guard, new(MinimalConversionLimit: 0, ModerateConversionLimit: 0, AggressiveConversionLimit: 0));
        Assert.True(report.Success); Assert.True(report.UsedFallback); Assert.Equal(4, report.Attempts.Length);
        Assert.Equal(2, report.Conversions); Assert.Equal(0, unusedOptionalBudget.Work);
        Assert.Contains(report.Attempts[^1].Actions, a => a.After == "stepping-stones");
        Assert.Contains(report.Attempts[^1].Actions, a => a.After == "stone-floor"); Covered(board, guard, deployment);
    }

    [Fact]
    public void AggressivePolicyCanCompleteWhenEarlierStyledAllowancesAreExhausted()
    {
        var (board, guard, _) = Context(TreeBarrier); Register(board, guard, Location("north", All("usable", 2)));
        var report = MandatoryConnectivityRepair.Run(board, guard, new(MinimalConversionLimit: 0, ModerateConversionLimit: 0));
        Assert.True(report.Success); Assert.False(report.UsedFallback); Assert.Equal(1, report.Conversions);
        Assert.Equal(new[] { "minimal", "moderate", "aggressive" }, report.Attempts.Select(a => a.Stage));
        Assert.True(report.Attempts[^1].Accepted);
    }

    [Fact]
    public void MinimalPassCannotPrepareWaterAndModerateCan()
    {
        var (board, guard, deployment) = Context(draft =>
        { foreach (var i in Enumerable.Range(Width * 3, Width)) draft.SetTerrain(i, "water"); });
        Register(board, guard, Location("north", All("first", 2), All("second", 22)));
        var report = MandatoryConnectivityRepair.Run(board, guard);
        Assert.True(report.Success); Assert.False(report.UsedFallback); Assert.Equal(2, report.Attempts.Length);
        Assert.Equal("minimal", report.Attempts[0].Stage); Assert.False(report.Attempts[0].Accepted);
        Assert.Empty(report.Attempts[0].Actions); Assert.Equal("moderate", report.Attempts[1].Stage);
        Assert.Equal(2, report.AcceptedConnections); Assert.Equal(report.Conversions, report.ConvertedByTerrain["water"]);
        Assert.Equal(report.Conversions, report.TotalDisturbance); Assert.True(report.Attempts[1].Accepted);
        Covered(board, guard, deployment);
    }

    [Theory]
    [InlineData(false, "fixture-press")]
    [InlineData(true, "unrelated-identity")]
    public void MachineDiagonalAccessMaterializesItsExplicitGeometrySupport(bool fallback, string machineId)
    {
        var (board, guard, deployment) = Context(draft =>
        {
            foreach (var i in new[] { 34, 35, 57, 59, 81, 82, 83 }) draft.SetTerrain(i, "tree");
            draft.PlaceMachine(new(machineId, new(10, 2), MachineTypes.DiamondPress));
        });
        var machine = Assert.Single(board.State.Machines);
        Assert.DoesNotContain(new Cell(9, 1), MachineGeometry.PotentialOperatingPositions(GameEngine.CreateGame(board.Export()).Physical.Board, machine));
        Register(board, guard);
        var declaration = Assert.Single(guard.Registry.Locations, l => l.SourceId == machineId);
        var preparation = Assert.Single(declaration.Groups[0].Preparation, p => p.Position == 33);
        Assert.Equal(new[] { 34 }, preparation.Cells);
        var report = MandatoryConnectivityRepair.Run(board, guard, fallback ? Fallback() : new());
        Assert.True(report.Success); Assert.Equal(1, report.Conversions); Assert.Equal(fallback, report.UsedFallback);
        Assert.Equal(new[] { 34 }, Assert.Single(report.Attempts).WitnessSupportCells);
        Assert.Contains(new Cell(9, 1), MachineGeometry.PotentialOperatingPositions(GameEngine.CreateGame(board.Export()).Physical.Board, machine));
        Assert.Empty(guard.ActualFailures(GameEngine.CreateGame(board.Export())));
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), deployment));
    }

    [Fact]
    public void AccessSupportMetadataUsesTheSharedRegistryValidationBoundary()
    {
        var invalid = Location("invalid", All("access", 2) with { Preparation =
        [new(3, [Width * Height]), new(3, [2, 2]), new(2, [2])] });
        var registry = RequiredLocationRegistry.Create([invalid], Width, Height);
        Assert.Contains(registry.Errors, e => e.Contains("duplicate preparation positions"));
        Assert.Contains(registry.Errors, e => e.Contains("does not match an access position"));
        Assert.Contains(registry.Errors, e => e.Contains("outside the Board"));
        Assert.Contains(registry.Errors, e => e.Contains("duplicate support Cells"));
        Assert.Contains(registry.Errors, e => e.Contains("exclude the access position"));
    }

    [Fact]
    public void ProtectedNonlandAccessSupportCannotBePublishedAsACompletionWitness()
    {
        var (board, guard, _) = Context();
        var rock = Area("immutable-rock", [58]);
        var site = new SiteInfo("protected-rock", GeneratedSiteKind.Settlement, rock, Area("outside", []), [], [],
            new(0, 0, true, false, 0), [], [], [], null, PreparationSummary.Empty);
        var draft = Draft(board, site.Id); draft.SetTerrain(58, "solid-rock"); draft.Reserve([58]);
        Assert.True(guard.TryPublish(draft.BuildPatch(), "rock", "fixture", out _, newSites: [site]));
        var group = All("access", 2) with { Preparation = [new(2, [58])] };
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        Assert.False(guard.TryPublish(Draft(board, "invalid-producer").BuildPatch(), "invalid-support", "fixture", out var diagnostic,
            [Location("unsupported-access", group)]));
        Assert.Equal(CompletionStatus.Error, diagnostic.Status);
        Assert.Contains("support Cell 58", diagnostic.Detail);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(revision, board.Revision);
        Assert.DoesNotContain(guard.Registry.Locations, l => l.Id == "unsupported-access");
    }

    [Fact]
    public void GenericAccessSupportMustBePreparedEvenWhenItsDestinationIsAlreadyReachable()
    {
        var (board, guard, _) = Context(draft => draft.SetTerrain(58, "tree"));
        Register(board, guard, Location("prepared-access", All("access", 2) with { Preparation = [new(2, [58])] }));
        Assert.NotEmpty(guard.ActualFailures(GameEngine.CreateGame(board.Export())));
        var report = MandatoryConnectivityRepair.Run(board, guard);
        Assert.True(report.Success); Assert.Equal(1, report.Conversions);
        Assert.Equal("grass", board.TerrainAt(58));
        Assert.Empty(guard.ActualFailures(GameEngine.CreateGame(board.Export())));
    }

    [Fact]
    public void AdjacentProtectedStructuresAndClosedDoorRemainReadOnlyTransit()
    {
        var (board, guard, deployment) = Context(TreeBarrier);
        var cells = Enumerable.Range(Width * 5, Width).ToArray(); var passage = Width * 5 + 5;
        var west = cells.Take(6).ToArray(); var east = cells.Skip(6).ToArray();
        var site = new SiteInfo("gate", GeneratedSiteKind.Campsite, Area("gate", west), Area("passage", [passage]), [], [],
            new(1, 1, true, false, 0), [], [], [], null, PreparationSummary.Empty);
        var gate = Draft(board, "gate");
        foreach (var cell in west.Where(i => i != passage)) gate.SetTerrain(cell, "solid-rock");
        gate.SetEdge(EdgeKey.Between(passage, passage - Width, Width), EdgeKind.ClosedDoor); gate.Reserve(west);
        Assert.True(guard.TryPublish(gate.BuildPatch(), "gate", "fixture", out var diagnostic,
            [Location("north", All("all", 2))], [site]), diagnostic.Detail);
        var adjacent = new SiteInfo("east", GeneratedSiteKind.Settlement, Area("east", east), Area("empty", []), [], [],
            new(0, 0, true, false, 0), [], [], [], null, PreparationSummary.Empty);
        var eastDraft = Draft(board, "east");
        foreach (var cell in east) eastDraft.SetTerrain(cell, "solid-rock");
        eastDraft.Reserve(east);
        Assert.True(guard.TryPublish(eastDraft.BuildPatch(), "east", "fixture", out var eastDiagnostic, newSites: [adjacent]), eastDiagnostic.Detail);
        var before = cells.Select(board.TerrainAt).ToArray(); var edges = JsonSerializer.Serialize(board.State.Edges.ToArray());
        var report = MandatoryConnectivityRepair.Run(board, guard, Fallback());
        Assert.True(report.Success); Assert.Equal(before, cells.Select(board.TerrainAt));
        Assert.Equal(edges, JsonSerializer.Serialize(board.State.Edges.ToArray()));
        Assert.Contains(report.Attempts[0].Interfaces, i => i.ExactOwnerId == "gate"); Covered(board, guard, deployment);
        Assert.Equal(2, guard.Sites.Length);
    }

    [Fact]
    public void ExhaustedAnalysisIsIncompleteAndNonmutating()
    {
        var (board, guard, _) = Context(TreeBarrier); Register(board, guard, Location("north", All("all", 2)));
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        var report = MandatoryConnectivityRepair.Run(board, guard, Fallback(0));
        Assert.Equal(MandatoryRepairStatus.Incomplete, report.Status); Assert.Equal(0, report.WorkUsed);
        Assert.NotEmpty(report.Failures); Assert.False(report.Attempts[0].SearchComplete);
        Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(MandatoryConnectivityRepair.Run(board, guard, Fallback(0))));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(revision, board.Revision);
    }

    [Theory]
    [InlineData(false, "Impossible")]
    [InlineData(true, "Incomplete")]
    public void NegativeProofRequiresCompleteMetadataAndCompleteExploration(bool unknownOwner, string expected)
    {
        var (board, guard, _) = Context(); Register(board, guard, Location("north", All("all", 2)));
        // Deliberately bypass publication to exercise defensive failure on an invalid accepted input.
        var block = Draft(board, unknownOwner ? "unpublished-owner" : "fixture");
        foreach (var cell in Enumerable.Range(Width * 3, Width))
            block.SetEdge(EdgeKey.Between(cell, cell + Width, Width), EdgeKind.Wall);
        if (unknownOwner) block.Reserve([23]);
        Assert.True(board.TryCommit(block.BuildPatch(), out _));
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var registry = JsonSerializer.Serialize(guard.Registry.Locations);
        var report = MandatoryConnectivityRepair.Run(board, guard, Fallback());
        Assert.Equal(expected, report.Status.ToString()); Assert.NotEmpty(report.Attempts[0].Frontier);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(registry, JsonSerializer.Serialize(guard.Registry.Locations));
    }

    [Fact]
    public void InvalidDeploymentIsReportedWithoutAttemptingPreparation()
    {
        var (board, guard, deployment) = Context(TreeBarrier); Register(board, guard, Location("north", All("all", 2)));
        // An externally damaged deployment must be reported rather than hidden by repairing the north.
        var damage = Draft(board, "hero-deployment"); damage.ReplaceTerrain(deployment.PlacementOrder[1], "solid-rock");
        Assert.True(board.TryCommit(damage.BuildPatch(), out _));
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        var registry = JsonSerializer.Serialize(guard.Registry.Locations); var witness = guard.Witness;
        var report = MandatoryConnectivityRepair.Run(board, guard, Fallback());
        Assert.Equal(MandatoryRepairStatus.InvariantError, report.Status); Assert.Equal(0, report.Conversions);
        Assert.Contains(report.Diagnostics, s => s.Contains("deployment invariant"));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(revision, board.Revision);
        Assert.Equal(registry, JsonSerializer.Serialize(guard.Registry.Locations)); Assert.Same(witness, guard.Witness);
    }

    [Fact]
    public void PublicationRejectsPreparationAndInvariantDamageAtomically()
    {
        var (board, guard, deployment) = Context(TreeBarrier); Register(board, guard, Location("north", All("all", 2)));
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        var registry = JsonSerializer.Serialize(guard.Registry.Locations); var witness = guard.Witness;
        var candidate = Draft(board, "hero-deployment");
        candidate.ReplaceTerrain(Width * 3 + 2, "grass");
        candidate.ReplaceTerrain(deployment.PlacementOrder[1], "solid-rock");
        Assert.False(guard.TryPublish(candidate.BuildPatch(), "invalid-repair", "fixture", out var diagnostic));
        Assert.Equal(CompletionStatus.Error, diagnostic.Status);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(revision, board.Revision);
        Assert.Equal(registry, JsonSerializer.Serialize(guard.Registry.Locations)); Assert.Same(witness, guard.Witness);
    }

    [Fact]
    public void IdenticalInputsProduceIdenticalAttemptsActionsAndGeometry()
    {
        string Run()
        {
            var (board, guard, _) = Context(TreeBarrier); Register(board, guard, Location("north", All("all", 2, 22)));
            var report = MandatoryConnectivityRepair.Run(board, guard, new(MinimalConversionLimit: 0));
            return JsonSerializer.Serialize(new { report, Board = ScenarioDefinitionJson.ToJson(board.Export()) });
        }
        Assert.Equal(Run(), Run());
    }
}
