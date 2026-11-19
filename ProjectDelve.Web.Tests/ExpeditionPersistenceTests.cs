using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionPersistenceTests(ITestOutputHelper output) : IDisposable
{
    private const ulong WorldSeed = 0xB42FD193;
    private readonly string root = Path.Combine(Path.GetTempPath(), "project-delve-expedition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PendingDecisionAndLaterTokenDrawsResumeWithoutConsumingRandomness()
    {
        var store = new ExpeditionStore(root, "pending");
        var direct = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, OpenBoard);
        var result = direct.StartRound();
        Assert.NotNull(result.NextInput);
        var watch = Stopwatch.StartNew();
        var saved = direct.Save();
        var saveTime = watch.Elapsed;
        var checkpoint = store.ReadCheckpoint(saved.ActiveCheckpoint);
        watch.Restart();
        var resumed = ExpeditionRunner.Open(store, ContentBundle.Canonical, OpenBoard);
        output.WriteLine($"60x60 open fixture stable save {saveTime.TotalMilliseconds:F1} ms, open {watch.Elapsed.TotalMilliseconds:F1} ms. {CheckpointBytes(saved.ActiveCheckpoint, "pending")} bytes.");
        Assert.Equal(JsonSerializer.Serialize(result.State), JsonSerializer.Serialize(resumed.Snapshot()));
        Assert.Equal(checkpoint.GameplayRandomState, store.ReadCheckpoint(resumed.Save().ActiveCheckpoint).GameplayRandomState);
        for (var i = 0; i < 30; i++)
        {
            var request = result.NextInput;
            var choice = request is null ? null : Pass(request);
            result = request is null ? direct.StartRound() : direct.Decide(choice);
            var replay = request is null ? resumed.StartRound() : resumed.Decide(choice);
            Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        }
        Assert.True(result.State.Round > 1);
        var final = resumed.Save();
        Assert.NotEqual(checkpoint.GameplayRandomState, store.ReadCheckpoint(final.ActiveCheckpoint).GameplayRandomState);
    }

    [Fact]
    public void RandomStateCoversEveryInjectedRandomMechanism()
    {
        var random = new ExpeditionRandom(WorldSeed);
        var past = Draws(random).ToArray();
        var saved = random.State;
        var next = Draws(random).ToArray();
        Assert.NotEqual(past, next);
        var resumed = new ExpeditionRandom(saved);
        Assert.Equal(next, Draws(resumed).ToArray());
        Assert.Equal(random.State, resumed.State);

        static IEnumerable<object> Draws(IRandomProvider rng)
        {
            ActivationToken[] bag = [new("a", "one"), new("b", "one"), new("c", "two")];
            for (var i = 0; i < 5; i++)
            {
                yield return rng.DrawToken(bag);
                yield return rng.RollAttackDie();
                yield return rng.RollDefenceDie();
                yield return rng.RollD6();
                yield return rng.RollD8();
                yield return rng.LootDrawIndex(137);
                yield return rng.WorldShuffleIndex(12 - i);
            }
        }
    }

    [Fact]
    public void PendingCombatDiceRoundTripPreservesContinuationAndRandomResults()
    {
        var store = new ExpeditionStore(root, "combat");
        ScenarioDefinition CombatBoard(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            var terrain = OpenBoard(seed, plan, content);
            var hero = terrain.Setup!.Deployment.PlacementOrder[0];
            var target = plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West ? hero with { X = hero.X - 1 } : hero with { Y = hero.Y - 1 };
            return terrain with
            {
                UnitTypeIds = [UnitTypeIds.Grunt],
                Units = [new(UnitTypeIds.Grunt, "opposition", target, UnitId: "target")],
                Agency = [new(UnitTypeIds.Grunt, "opposition", ControllerKind.Human)],
                WorldEffects = new(1, 1) { Composition = [new("calm", 3), new("iron-skin", 2)] }
            };
        }
        var direct = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, CombatBoard);
        var result = direct.StartRound();
        Assert.Contains(result.Events, occurrence => occurrence.Kind == "WorldDeckShuffled");
        for (var i = 0; i < 30 && result.NextInput?.Kind != DecisionKind.RollDice; i++)
        {
            var request = result.NextInput!;
            var choice = request.UnitId == direct.Head.OriginalHeroIds[0]
                ? request.Candidates.FirstOrDefault(candidate => candidate.TargetId == "target" && candidate.Kind == ActivationChoiceKind.Action)?.Key
                : null;
            result = direct.Decide(choice ?? Pass(request));
        }
        Assert.Equal(DiceFamily.Attack, result.NextInput?.Roll?.Family);
        var saved = direct.Save();
        var rng = store.ReadCheckpoint(saved.ActiveCheckpoint).GameplayRandomState;
        var resumed = ExpeditionRunner.Open(store, ContentBundle.Canonical, CombatBoard);
        Assert.Equal(JsonSerializer.Serialize(result.NextInput), JsonSerializer.Serialize(resumed.Snapshot().Pending));
        List<DiceFamily> dice = [];
        while (result.NextInput is { Kind: DecisionKind.RollDice } request)
        {
            result = direct.Decide(request.Candidates[0].Key);
            var replay = resumed.Decide(request.Candidates[0].Key);
            Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
            dice.Add(Assert.Single(result.Events, occurrence => occurrence.Kind == "DiceRolled").Dice!.Pool.Family);
        }
        Assert.Equal(new[] { DiceFamily.Attack, DiceFamily.Defence }, dice);
        Assert.NotEqual(rng, store.ReadCheckpoint(resumed.Save().ActiveCheckpoint).GameplayRandomState);
    }

    [Theory]
    [InlineData(ExpeditionFailureStage.ContentRefresh)]
    [InlineData(ExpeditionFailureStage.RegionGeneration)]
    [InlineData(ExpeditionFailureStage.PartySetup)]
    [InlineData(ExpeditionFailureStage.SuccessorCheckpoint)]
    [InlineData(ExpeditionFailureStage.HeadCommit)]
    public void FailedSuccessorLeavesCompletedSourceWithSameCheckpointAndRetriesSamePlan(ExpeditionFailureStage fault)
    {
        bool armed = false;
        ExpeditionHead? sourceHead = null;
        List<(ulong Seed, ExpeditionRegionPlan Plan)> successors = [];
        var store = new ExpeditionStore(root, "failure", point =>
        {
            if (armed && (fault == ExpeditionFailureStage.SuccessorCheckpoint && point == ExpeditionWritePoint.CheckpointBeforeReplace ||
                fault == ExpeditionFailureStage.HeadCommit && point == ExpeditionWritePoint.HeadBeforeReplace))
                throw new IOException("injected before replacement");
        });
        ContentBundle Latest()
        {
            if (sourceHead is not null)
            {
                Assert.Equal(sourceHead.ActiveCheckpoint, store.ReadHead().ActiveCheckpoint);
                Assert.True(store.ReadCheckpoint(sourceHead.ActiveCheckpoint).State.IsCompleted);
                if (armed && fault == ExpeditionFailureStage.ContentRefresh)
                    throw new InvalidDataException("injected incompatible catalog");
            }
            return ContentBundle.Canonical();
        }
        ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            if (plan.Region == default) return OpenBoard(seed, plan, content);
            successors.Add((seed, plan));
            Assert.Equal(sourceHead!.ActiveCheckpoint, store.ReadHead().ActiveCheckpoint);
            Assert.True(store.ReadCheckpoint(sourceHead.ActiveCheckpoint).State.IsCompleted);
            if (armed && fault == ExpeditionFailureStage.RegionGeneration) throw new InvalidDataException("injected generator defect");
            var scenario = OpenBoard(seed, plan, content);
            return armed && fault == ExpeditionFailureStage.PartySetup ? scenario with { Setup = null } : scenario;
        }
        var runner = ExpeditionRunner.Create(store, WorldSeed, Latest, Generate);
        Assert.Throws<InvalidOperationException>(() => runner.Continue(runner.Region.TransitionId));
        Assert.Empty(successors);
        FinishOpenBoard(runner);
        sourceHead = runner.Head;
        var source = store.ReadCheckpoint(sourceHead.ActiveCheckpoint);
        Assert.True(source.State.IsCompleted);
        var bytes = File.ReadAllBytes(CheckpointPath(sourceHead.ActiveCheckpoint, "failure"));
        armed = true;
        var error = Assert.Throws<ExpeditionOperationException>(() => runner.Continue(source.Plan.TransitionId));
        Assert.Equal(fault, error.Stage);
        Assert.Equal(WorldSeed, error.WorldSeed);
        Assert.Contains(fault == ExpeditionFailureStage.PartySetup ? "PlayReady" : "injected", error.InnerException!.Message);
        var reopened = ExpeditionRunner.Open(store, Latest, Generate);
        Assert.True(reopened.Snapshot().IsCompleted);
        Assert.Equal(sourceHead.ActiveCheckpoint, reopened.Head.ActiveCheckpoint);
        Assert.Empty(reopened.Head.Transitions);
        armed = false;
        var receipt = reopened.Continue(source.Plan.TransitionId);
        Assert.Equal(sourceHead.ActiveCheckpoint, receipt.Source);
        Assert.Equal(source.Plan.NextRegion, receipt.Successor.Region);
        Assert.All(successors, attempt =>
        {
            Assert.Equal(WorldSeed, attempt.Seed);
            Assert.Equal(ExpeditionWorld.Next(WorldSeed, source.Plan), attempt.Plan);
        });
        Assert.Equal(fault == ExpeditionFailureStage.ContentRefresh ? 1 : 2, successors.Count);
        Assert.Equal(receipt, reopened.Continue(source.Plan.TransitionId));
        Assert.Single(reopened.Head.Transitions);
        Assert.Equal(bytes, File.ReadAllBytes(CheckpointPath(receipt.Source, "failure")));
    }

    [Theory]
    [InlineData("primary-routes")]
    [InlineData("civilization")]
    [InlineData("population")]
    [InlineData("site-loot")]
    public void FaultInFullGenerationStageKeepsCompletedSourceAndRetriesSameRecipe(string expectedStage)
    {
        const ulong seed = 889;
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
        var store = new ExpeditionStore(root, "full-stage-failure");
        var failing = true;
        var reachedFaultStage = 0;
        ExpeditionCheckpointRef? committedSource = null;
        List<WorldGenerationRecipe> attempts = [];
        ScenarioDefinition Generate(ulong worldSeed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            if (plan.Region == default) return OpenBoard(worldSeed, plan, content);
            Assert.Equal(seed, worldSeed);
            Assert.Equal(committedSource, store.ReadHead().ActiveCheckpoint);
            Assert.True(store.ReadCheckpoint(committedSource!).State.IsCompleted);
            var recipe = ExpeditionWorld.RecipeFor(plan, settings);
            attempts.Add(recipe);
            return ExpeditionRegionGeometry.Execute(recipe, plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, step =>
            {
                if (step != expectedStage) return;
                reachedFaultStage++;
                if (failing) throw new IOException("injected WLD1 stage failure");
            }, profile).Context.Materialize();
        }
        var runner = ExpeditionRunner.Create(store, seed, ContentBundle.Canonical, Generate, settings.ToString(), profile);
        FinishOpenBoard(runner);
        var plan = runner.Region;
        var completed = runner.Head;
        committedSource = completed.ActiveCheckpoint;
        var bytes = File.ReadAllBytes(CheckpointPath(committedSource, "full-stage-failure"));

        var error = Assert.Throws<ExpeditionOperationException>(() => runner.Continue(plan.TransitionId));
        Assert.Equal(ExpeditionFailureStage.RegionGeneration, error.Stage);
        Assert.Equal(plan.NextRegion, error.Region);
        Assert.Equal(ExpeditionWorld.Next(seed, plan), error.Plan);
        var generation = Assert.IsType<PlayReadyGenerationException>(error.InnerException);
        Assert.Equal(seed, generation.Seed);
        Assert.Equal(expectedStage, generation.Stage);
        Assert.Equal(WorldGenerationFailureKind.UnexpectedImplementationFailure, generation.FailureKind);
        Assert.Contains("injected WLD1 stage failure", generation.DiagnosticsJson);
        Assert.Contains("LootAbundance", generation.ConfigurationJson);
        Assert.Contains("population", generation.ConfigurationJson);
        Assert.Contains(profile.NatureVersion, generation.ConfigurationJson);
        Assert.Equal(completed.Revision, store.ReadHead().Revision);
        Assert.Equal(profile, store.ReadHead().WorldProfile);
        Assert.Empty(store.ReadHead().Transitions);
        Assert.True(ExpeditionRunner.Open(root, "full-stage-failure").Snapshot().IsCompleted);

        failing = false;
        var reopened = ExpeditionRunner.Open(store, ContentBundle.Canonical, Generate);
        var receipt = reopened.Continue(plan.TransitionId);
        Assert.Equal(committedSource, receipt.Source);
        Assert.Contains(reopened.Snapshot().Units, unit => unit.SideId != ExpeditionParty.SideId);
        Assert.NotEmpty(reopened.Snapshot().LootMarkers);
        Assert.Equal(receipt, reopened.Continue(plan.TransitionId));
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0], attempts[1]);
        Assert.True(reachedFaultStage >= 2);
        Assert.Equal(bytes, File.ReadAllBytes(CheckpointPath(committedSource, "full-stage-failure")));
    }

    [Fact]
    public void PublicCreatePersistsSupportedWld1OverridesAndOpensTheExactPopulatedCheckpoint()
    {
        const ulong seed = 889;
        var requested = new WorldGenerationRecipe(new(18, -9, 42, 40), new(),
            Population: new(PopulationDensity.Low, Difficulty: 4), LootPreset: "Classic", LootAbundance: LootAbundance.Sparse);
        var spatial = new ExpeditionSpatialSettings { NatureScale = 180, RegionScale = 420, CoverageVariation = .4, RegionalVariation = .75 };
        var runner = ExpeditionRunner.Create(root, "overrides", seed, generationSettings: requested, spatialSettings: spatial);
        var saved = WorldGenerationRecipe.Parse(runner.Head.GenerationRecipe!);
        Assert.Equal(new WorldWindow(0, 0, 60, 60), saved.Window);
        Assert.Equal(seed, saved.Seed);
        Assert.Equal(5, saved.HeroCapacity);
        Assert.True(saved.Civilization!.Enabled);
        Assert.True(saved.Routes!.Enabled);
        Assert.True(saved.Enrichment!.Enabled);
        Assert.Equal(requested.Population, saved.Population);
        Assert.Equal("Classic", saved.LootPreset);
        Assert.Equal(LootAbundance.Sparse, saved.LootAbundance);
        Assert.Equal(ExpeditionStore.HeadVersion, runner.Head.Version);
        Assert.Equal(ExpeditionWorldProfile.Create(saved.NaturalTerrain, spatial), runner.Head.WorldProfile);
        using var head = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "expedition-overrides", "head.json")));
        output.WriteLine($"Pinned world profile JSON: {Encoding.UTF8.GetByteCount(head.RootElement.GetProperty("worldProfile").GetRawText())} bytes in the head.");
        var state = runner.Snapshot();
        Assert.Contains(state.Units, unit => unit.SideId != ExpeditionParty.SideId);
        var checkpoint = new ExpeditionStore(root, "overrides").ReadCheckpoint(runner.Head.ActiveCheckpoint);
        Assert.Equal("Classic", checkpoint.Scenario.Loot!.Preset);
        var reopened = ExpeditionRunner.Open(root, "overrides");
        Assert.Equal(runner.Head.GenerationRecipe, reopened.Head.GenerationRecipe);
        Assert.Equal(runner.Head.WorldProfile, reopened.Head.WorldProfile);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(reopened.Snapshot()));
    }

    [Fact]
    public void PhaseOneHeadWithoutRecipeStillOpensItsSavedAuthoritativeState()
    {
        var store = new ExpeditionStore(root, "legacy-head");
        var original = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, OpenBoard);
        original.StartRound();
        var saved = original.Save();
        Assert.Null(saved.GenerationRecipe);
        var reopened = ExpeditionRunner.Open(root, "legacy-head", () => throw new InvalidOperationException("Resume must use the saved catalog."));
        Assert.Equal(saved.ActiveCheckpoint, reopened.Head.ActiveCheckpoint);
        Assert.Equal(JsonSerializer.Serialize(original.Snapshot()), JsonSerializer.Serialize(reopened.Snapshot()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyFrontierPinsNatureOnlyAfterSuccessfulSuccessorCommit(bool hasExistingRecipe)
    {
        const ulong seed = 889;
        const string id = "legacy-frontier";
        var settings = ExpeditionWorld.Configure(seed);
        var store = new ExpeditionStore(root, id);
        var original = ExpeditionRunner.Create(store, seed, ContentBundle.Canonical, OpenBoard,
            hasExistingRecipe ? settings.ToString() : null);
        FinishOpenBoard(original);
        var source = original.Head.ActiveCheckpoint;
        var sourceBytes = File.ReadAllBytes(CheckpointPath(source, id));
        var headPath = Path.Combine(root, "expedition-" + id, "head.json");
        var headBytes = File.ReadAllBytes(headPath);
        var incompatible = ContentBundle.Canonical();
        incompatible = incompatible with
        { Terrain = incompatible.Terrain.Select(t => t.Id == "dirt" ? t with { BlocksVision = true } : t).ToImmutableArray() };

        var retry = ExpeditionRunner.Open(root, id, () => incompatible);
        Assert.Equal(ExpeditionStore.LegacyHeadVersion, retry.Head.Version);
        Assert.Null(retry.Head.WorldProfile);
        Assert.True(retry.Snapshot().IsCompleted);
        var fault = Assert.Throws<ExpeditionOperationException>(() => retry.Continue(original.Region.TransitionId));
        Assert.Equal(ExpeditionFailureStage.RegionGeneration, fault.Stage);
        Assert.Equal("natural", Assert.IsType<PlayReadyGenerationException>(fault.InnerException).Stage);
        Assert.Contains("natural terrain role 'dirt'", fault.Message);
        Assert.Equal(headBytes, File.ReadAllBytes(headPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));

        retry = ExpeditionRunner.Open(root, id);
        var receipt = retry.Continue(original.Region.TransitionId);
        Assert.Equal(source, receipt.Source);
        Assert.Equal(ExpeditionStore.HeadVersion, retry.Head.Version);
        Assert.Equal(settings.ToString(), retry.Head.GenerationRecipe);
        Assert.Equal(ExpeditionWorldProfile.CreateV1(settings.NaturalTerrain) with
            { StationVersion = ExpeditionWorldProfile.CurrentStationVersion }, retry.Head.WorldProfile);
        Assert.Equal(retry.Head.WorldProfile, ExpeditionRunner.Open(root, id).Head.WorldProfile);
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));
        Assert.Equal(receipt, retry.Continue(original.Region.TransitionId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StationsUpgradeOnlyAtSuccessfulFrontierAndKeepArchivedGeometryAndOtherPolicies(int policy)
    {
        const ulong seed = 889;
        const string id = "station-frontier";
        var settings = ExpeditionWorld.Configure(seed, new(new(0, 0, 60, 60), new(0, 0, 0),
            Civilization: new(Enabled: false), Routes: new(Enabled: false), Population: new(PopulationDensity.Off)));
        var profile = policy switch
        {
            0 => ExpeditionWorldProfile.CreateV1(settings.NaturalTerrain),
            1 => ExpeditionWorldProfile.CreateCivilizationV1(settings.NaturalTerrain),
            2 => ExpeditionWorldProfile.CreateConstructionV1(settings.NaturalTerrain),
            3 => ExpeditionWorldProfile.CreateConstructionV2(settings.NaturalTerrain),
            _ => ExpeditionWorldProfile.Create(settings.NaturalTerrain) with { StationVersion = null }
        };
        var store = new ExpeditionStore(root, id);
        var runner = ExpeditionRunner.Create(store, seed, ContentBundle.Canonical, OpenBoard, settings.ToString(), profile);
        FinishOpenBoard(runner);
        var source = runner.Head.ActiveCheckpoint;
        var sourceBytes = File.ReadAllBytes(CheckpointPath(source, id));
        var headPath = Path.Combine(root, "expedition-" + id, "head.json");
        var headBytes = File.ReadAllBytes(headPath);
        var incompatible = ContentBundle.Canonical();
        incompatible = incompatible with
        { Terrain = incompatible.Terrain.Select(t => t.Id == "dirt" ? t with { BlocksVision = true } : t).ToImmutableArray() };
        var failed = ExpeditionRunner.Open(root, id, () => incompatible);
        Assert.Equal(profile, failed.Head.WorldProfile);
        Assert.Equal(headBytes, File.ReadAllBytes(headPath));
        Assert.Throws<ExpeditionOperationException>(() => failed.Continue(runner.Region.TransitionId));
        Assert.Equal(headBytes, File.ReadAllBytes(headPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));

        var noOperate = ContentBundle.Canonical();
        noOperate = noOperate with { Units = noOperate.Units.Select(type =>
            ExpeditionParty.Originals.Any(hero => hero.TypeId == type.Id)
                ? type with { LocalAbilities = type.LocalAbilities.Where(ability => ability.Mechanic is not Operate).ToImmutableArray() }
                : type).ToImmutableArray() };
        var failedParty = ExpeditionRunner.Open(root, id, () => noOperate);
        Assert.Equal(ExpeditionFailureStage.PartySetup, Assert.Throws<ExpeditionOperationException>(() =>
            failedParty.Continue(runner.Region.TransitionId)).Stage);
        Assert.Equal(headBytes, File.ReadAllBytes(headPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));

        var reopened = ExpeditionRunner.Open(root, id);
        Assert.Equal(profile, reopened.Head.WorldProfile);
        Assert.Equal(ScenarioDefinitionJson.ToJson(runner.Scenario), ScenarioDefinitionJson.ToJson(reopened.Scenario));
        reopened.Continue(runner.Region.TransitionId);
        Assert.Equal(profile with { StationVersion = ExpeditionWorldProfile.CurrentStationVersion }, reopened.Head.WorldProfile);
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));
        var station = Assert.Single(reopened.Snapshot().Physical.Board.Machines,
            m => m.Id == ExpeditionStationGenerator.ArrivalId);
        Assert.Equal(ExpeditionParty.SideId, station.OwnerSide);
        Assert.Contains(reopened.Scenario.Setup!.Deployment.PlacementOrder[0],
            MachineGeometry.PotentialOperatingPositions(reopened.Snapshot().Physical.Board, station));
        var resumed = ExpeditionRunner.Open(root, id);
        Assert.Equal(reopened.Head.WorldProfile, resumed.Head.WorldProfile);
        Assert.Equal(ScenarioDefinitionJson.ToJson(reopened.Scenario), ScenarioDefinitionJson.ToJson(resumed.Scenario));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PinnedCivilizationPolicySurvivesSaveResumeAndAtomicSuccessor(int policy)
    {
        const string id = "civilization-policy";
        var settings = ExpeditionWorld.Configure(WorldSeed);
        var profile = policy switch
        {
            0 => ExpeditionWorldProfile.CreateV1(settings.NaturalTerrain),
            1 => ExpeditionWorldProfile.CreateCivilizationV1(settings.NaturalTerrain),
            2 => ExpeditionWorldProfile.CreateConstructionV1(settings.NaturalTerrain),
            3 => ExpeditionWorldProfile.CreateConstructionV2(settings.NaturalTerrain),
            _ => ExpeditionWorldProfile.Create(settings.NaturalTerrain)
        };
        var failHeadCommit = false;
        var store = new ExpeditionStore(root, id, point =>
        {
            if (failHeadCommit && point == ExpeditionWritePoint.HeadBeforeReplace)
                throw new IOException("injected policy head commit failure");
        });
        var canonical = ContentBundle.Canonical();
        var initialContent = policy >= 3 ? canonical : canonical with
        {
            Terrain = [.. canonical.Terrain.Where(t => t.Assembly?.Landmark != true &&
                (policy >= 2 || t.Id != "gravel-with-well"))],
            FixedComponents = [.. canonical.FixedComponents.Where(c => policy >= 2 || c.Id != "well")]
        };
        var runner = ExpeditionRunner.Create(store, WorldSeed, () => initialContent, OpenBoard,
            settings.ToString(), profile);
        if (policy < 2)
            Assert.DoesNotContain(store.ReadCheckpoint(runner.Head.ActiveCheckpoint).Content.Terrain, t => t.Id == "gravel-with-well");
        var headPath = Path.Combine(root, "expedition-" + id, "head.json");
        using (var json = JsonDocument.Parse(File.ReadAllBytes(headPath)))
        {
            var savedProfile = json.RootElement.GetProperty("worldProfile");
            Assert.Equal(policy > 0, savedProfile.TryGetProperty("civilizationVersion", out var version));
            if (policy > 0) Assert.Equal(ExpeditionWorldProfile.CurrentCivilizationVersion, version.GetString());
            Assert.Equal(policy >= 2, savedProfile.TryGetProperty("constructionVersion", out var construction));
            if (policy >= 2) Assert.Equal(profile.ConstructionVersion, construction.GetString());
        }
        FinishOpenBoard(runner);
        runner.Save();
        var reopened = ExpeditionRunner.Open(store, ContentBundle.Canonical, OpenBoard);
        Assert.Equal(profile, reopened.Head.WorldProfile);
        Assert.Equal(JsonSerializer.Serialize(runner.Snapshot()), JsonSerializer.Serialize(reopened.Snapshot()));
        var source = reopened.Head.ActiveCheckpoint;
        var sourceBytes = File.ReadAllBytes(CheckpointPath(source, id));
        var headBytes = File.ReadAllBytes(headPath);
        var transition = reopened.Region.TransitionId;
        failHeadCommit = true;
        Assert.Equal(ExpeditionFailureStage.HeadCommit,
            Assert.Throws<ExpeditionOperationException>(() => reopened.Continue(transition)).Stage);
        Assert.Equal(headBytes, File.ReadAllBytes(headPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));
        failHeadCommit = false;
        reopened = ExpeditionRunner.Open(store, ContentBundle.Canonical, OpenBoard);
        Assert.Equal(source, reopened.Head.ActiveCheckpoint);
        Assert.Equal(profile, reopened.Head.WorldProfile);
        var receipt = reopened.Continue(transition);
        Assert.Equal(source, receipt.Source);
        Assert.Equal(profile, reopened.Head.WorldProfile);
        Assert.Equal(profile, ExpeditionRunner.Open(store, ContentBundle.Canonical, OpenBoard).Head.WorldProfile);
        Assert.Equal(receipt, reopened.Continue(transition));
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));
    }

    [Fact]
    public void SuccessorCanBuildWithARefreshedCenterpieceWithoutChangingTheSource()
    {
        const ulong seed = 0;
        const string id = "construction-refresh";
        const string centerpieceId = "frontier-well";
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.CreateConstructionV1(settings.NaturalTerrain);
        var store = new ExpeditionStore(root, id);
        var runner = ExpeditionRunner.Create(store, seed, ContentBundle.Canonical, OpenBoard, settings.ToString(), profile);
        FinishOpenBoard(runner);
        var source = runner.Head.ActiveCheckpoint;
        var originalBytes = File.ReadAllBytes(CheckpointPath(source, id));
        var current = ContentBundle.Canonical();
        ContentBundle Latest() => current with
        {
            Terrain = [.. current.Terrain.Where(t => t.Id != "gravel-with-well")],
            Assemblies = [new(centerpieceId, "Frontier well", "gravel")
            { VillageCenterpiece = true, Components = [new("object", "well")] }]
        };

        runner = ExpeditionRunner.Open(root, id, Latest);
        var receipt = runner.Continue(runner.Region.TransitionId);
        var successor = store.ReadCheckpoint(receipt.Successor);
        Assert.Equal(source, receipt.Source);
        Assert.Equal(originalBytes, File.ReadAllBytes(CheckpointPath(source, id)));
        Assert.DoesNotContain(store.ReadCheckpoint(source).Content.Terrain, t => t.Id == centerpieceId);
        var built = Assert.Single(successor.Scenario.Board.Cells, c => c.TerrainTypeId == centerpieceId);
        var board = successor.State.Physical.Board;
        var terrain = board.TerrainDefinitionAt(built.Position);
        Assert.False(terrain.Passable);
        Assert.True(terrain.Assembly!.VillageCenterpiece);
        var square = Assert.Single(successor.Scenario.Setup!.RequiredLocations, location =>
            location.Groups.Any(group => group.Id == "public-space" &&
                group.Positions.Contains(built.Position with { X = built.Position.X + 1 })));
        Assert.DoesNotContain(built.Position, square.Groups.SelectMany(g => g.Positions));
        Assert.Equal(profile with { StationVersion = ExpeditionWorldProfile.CurrentStationVersion },
            ExpeditionRunner.Open(root, id, Latest).Head.WorldProfile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessorCanBuildWithARefreshedLandmarkWithoutChangingTheSource(bool useOutposts)
    {
        const string id = "landmark-refresh";
        const string assemblyId = "frontier-marker";
        var recipe = new WorldGenerationRecipe(new(), new(0, 0, 0),
            Civilization: new(Campsites: ContentDensity.None, Settlements: ContentDensity.None),
            Routes: new(Enabled: false), Population: new(PopulationDensity.Off));
        var profile = useOutposts ? ExpeditionWorldProfile.Create(recipe.NaturalTerrain)
            : ExpeditionWorldProfile.CreateConstructionV2(recipe.NaturalTerrain);
        var seed = Enumerable.Range(0, 256).Select(value => (ulong)value).First(candidate =>
        {
            var next = ExpeditionWorld.Next(candidate, ExpeditionWorld.Initial(candidate));
            var region = ExpeditionWorld.RecipeFor(next, ExpeditionWorld.Configure(candidate, recipe));
            return ExpeditionCivilization.ResolveRemains(region, profile).LandmarkAnchor is not null &&
                (!useOutposts || ExpeditionCivilization.ResolveOutpost(region, profile).Anchor is not null);
        });
        var settings = ExpeditionWorld.Configure(seed, recipe);
        var store = new ExpeditionStore(root, id);
        var runner = ExpeditionRunner.Create(store, seed, ContentBundle.Canonical, OpenBoard, settings.ToString(), profile);
        FinishOpenBoard(runner);
        var source = runner.Head.ActiveCheckpoint;
        var sourceBytes = File.ReadAllBytes(CheckpointPath(source, id));
        var canonical = ContentBundle.Canonical();
        ContentBundle Latest() => canonical with
        {
            Terrain = [.. canonical.Terrain.Where(terrain => terrain.Assembly?.Landmark != true)],
            Assemblies = [new(assemblyId, "Frontier marker", "gravel")
            { Landmark = true, Components = [new("object", "well")] }]
        };
        runner = ExpeditionRunner.Open(root, id, Latest);
        var receipt = runner.Continue(runner.Region.TransitionId);
        var successor = store.ReadCheckpoint(receipt.Successor);
        Assert.Equal(source, receipt.Source);
        Assert.Equal(sourceBytes, File.ReadAllBytes(CheckpointPath(source, id)));
        Assert.DoesNotContain(store.ReadCheckpoint(source).Content.Terrain, terrain => terrain.Id == assemblyId);
        var marker = Assert.Single(successor.Scenario.Board.Cells, cell => cell.TerrainTypeId == assemblyId);
        var board = successor.State.Physical.Board;
        var terrain = board.TerrainDefinitionAt(marker.Position);
        Assert.True(terrain.Assembly!.Landmark);
        Assert.False(terrain.Passable);
        var approach = Assert.Single(successor.Scenario.Setup!.RequiredLocations.SelectMany(location => location.Groups),
            group => group.Id == "approach" && group.Positions.Contains(marker.Position with { X = marker.Position.X + 1 }));
        Assert.Equal(8, approach.Positions.Count);
        var reached = MandatoryConnectivityRepair.ActualReachable(board,
            [successor.Plan.EntrySeam.Y * board.Width + successor.Plan.EntrySeam.X]);
        Assert.All(approach.Positions, cell => Assert.Contains(cell.Y * board.Width + cell.X, reached));
        Assert.Equal(useOutposts, successor.Scenario.Setup.RequiredLocations.Any(location =>
            location.Groups.Any(group => group.Id == "courtyard")));
        Assert.Contains(successor.Plan.ExitSeam.Y * board.Width + successor.Plan.ExitSeam.X, reached);
        Assert.Equal(profile with { StationVersion = ExpeditionWorldProfile.CurrentStationVersion },
            ExpeditionRunner.Open(root, id, Latest).Head.WorldProfile);
    }

    [Theory]
    [InlineData("natureVersion", "Unsupported Expedition nature version")]
    [InlineData("fieldVersion", "Unsupported Expedition field version")]
    [InlineData("coordinates", "Unsupported Expedition coordinate convention")]
    [InlineData("civilizationVersion", "Unsupported Expedition civilization version")]
    [InlineData("constructionVersion", "Unsupported Expedition construction version")]
    [InlineData("stationVersion", "Unsupported Expedition Station version")]
    public void UnknownPinnedVersionRejectsOpenWithoutTouchingEitherSaveFile(string property, string diagnostic)
    {
        const string id = "unsupported-profile";
        var settings = ExpeditionWorld.Configure(WorldSeed);
        var store = new ExpeditionStore(root, id);
        var runner = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, OpenBoard, settings.ToString(),
            ExpeditionWorldProfile.Create(settings.NaturalTerrain));
        var path = Path.Combine(root, "expedition-" + id, "head.json");
        var head = JsonNode.Parse(File.ReadAllText(path))!;
        head["worldProfile"]![property] = "Future";
        var badJson = head.ToJsonString();
        File.WriteAllText(path, badJson);
        var checkpoint = CheckpointPath(runner.Head.ActiveCheckpoint, id);
        var checkpointBytes = File.ReadAllBytes(checkpoint);
        Assert.Contains(diagnostic, Assert.Throws<InvalidDataException>(() => ExpeditionRunner.Open(root, id)).Message);
        Assert.Equal(badJson, File.ReadAllText(path));
        Assert.Equal(checkpointBytes, File.ReadAllBytes(checkpoint));
    }

    [Fact]
    public void RegionWindowsUseCheckedSignedCellBoundsWithoutTruncation()
    {
        Assert.Equal(new WorldWindow(-120, 180, 60, 60), ExpeditionWorld.RegionWindow(new(-2, 3)));
        const long first = int.MinValue / 60L;
        const long last = (int.MaxValue - 59L) / 60L;
        var limit = ExpeditionWorld.RegionWindow(new(first, last)).Validate();
        Assert.Equal(first * 60, limit.OffsetX);
        Assert.Equal(last * 60, limit.OffsetY);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpeditionWorld.RegionWindow(new(first - 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpeditionWorld.RegionWindow(new(0, last + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpeditionWorld.RegionWindow(new(long.MinValue, long.MaxValue)));
    }

    [Fact]
    public void LostAcknowledgementAfterReplaceLeavesSuccessorAuthoritativeAndRetryReadsReceipt()
    {
        bool armed = false;
        int builds = 0;
        var store = new ExpeditionStore(root, "committed", point =>
        {
            if (armed && point == ExpeditionWritePoint.HeadAfterReplace) throw new OperationCanceledException("simulated process interruption");
        });
        ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            builds++;
            return OpenBoard(seed, plan, content);
        }
        var runner = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, Generate);
        FinishOpenBoard(runner);
        var source = runner.Head.ActiveCheckpoint;
        var plan = runner.Region;
        armed = true;
        Assert.Throws<OperationCanceledException>(() => runner.Continue(plan.TransitionId));
        armed = false;
        var reopened = ExpeditionRunner.Open(store, ContentBundle.Canonical, Generate);
        Assert.Equal(plan.NextRegion, reopened.Head.CurrentRegion);
        Assert.Equal(0, reopened.Snapshot().Round);
        Assert.True(store.ReadCheckpoint(source).State.IsCompleted);
        var receipt = Assert.Single(reopened.Head.Transitions);
        Assert.Equal(receipt, reopened.Continue(plan.TransitionId));
        Assert.Equal(receipt, runner.Continue(plan.TransitionId));
        Assert.Equal(2, builds);
        Assert.NotNull(reopened.StartRound().NextInput);
        var playing = reopened.Save();
        Assert.Equal(receipt, reopened.Continue(plan.TransitionId));
        Assert.Equal(playing.ActiveCheckpoint, reopened.Head.ActiveCheckpoint);
    }

    [Fact]
    public void CompletionSaveFailureCannotStartSuccessorWorkAndCanRetryWithoutReplay()
    {
        bool blockWrites = false;
        int refreshes = 0;
        var store = new ExpeditionStore(root, "source-durability", point =>
        {
            if (blockWrites && point == ExpeditionWritePoint.CheckpointBeforeReplace) throw new IOException("injected source checkpoint failure");
        });
        ContentBundle Latest() { refreshes++; return ContentBundle.Canonical(); }
        var runner = ExpeditionRunner.Create(store, WorldSeed, Latest, OpenBoard);
        var plan = runner.Region;
        var before = runner.Head.ActiveCheckpoint;
        blockWrites = true;
        Assert.Equal(ExpeditionFailureStage.SourceCheckpoint, Assert.Throws<ExpeditionOperationException>(() => FinishOpenBoard(runner)).Stage);
        Assert.True(runner.Snapshot().IsCompleted);
        Assert.Equal(ExpeditionFailureStage.SourceCheckpoint, Assert.Throws<ExpeditionOperationException>(() => runner.Continue(plan.TransitionId)).Stage);
        Assert.Equal(1, refreshes);
        Assert.Equal(before, store.ReadHead().ActiveCheckpoint);
        Assert.False(store.ReadCheckpoint(before).State.IsCompleted);

        blockWrites = false;
        var receipt = runner.Continue(plan.TransitionId);
        Assert.Equal(2, refreshes);
        Assert.True(store.ReadCheckpoint(receipt.Source).State.IsCompleted);
        Assert.NotEqual(before, receipt.Source);
    }

    [Fact]
    public async Task ConcurrentContinueOperationsShareOneCommittedTransition()
    {
        using var bothPreparing = new Barrier(2);
        var store = new ExpeditionStore(root, "concurrent");
        ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            if (plan.Region != default && !bothPreparing.SignalAndWait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("The concurrent successor build did not start.");
            return OpenBoard(seed, plan, content);
        }
        var first = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, Generate);
        FinishOpenBoard(first);
        var second = ExpeditionRunner.Open(store, ContentBundle.Canonical, Generate);
        var id = first.Region.TransitionId;
        var receipts = await Task.WhenAll(Task.Run(() => first.Continue(id)), Task.Run(() => second.Continue(id)));
        Assert.Equal(receipts[0], receipts[1]);
        Assert.Equal(receipts[0], Assert.Single(store.ReadHead().Transitions));
        Assert.True(store.ReadCheckpoint(receipts[0].Source).State.IsCompleted);
        Assert.Equal(receipts[0].Successor, first.Head.ActiveCheckpoint);
        Assert.Equal(first.Head.ActiveCheckpoint, second.Head.ActiveCheckpoint);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(17UL)]
    [InlineData(9214UL)]
    public void RegionPlanKeepsOneUnvisitedForwardExitAndMatchesEverySeam(ulong seed)
    {
        var plan = ExpeditionWorld.Initial(seed);
        HashSet<ExpeditionRegion> seen = [];
        HashSet<ExpeditionEdge> exits = [];
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(plan.Region));
            exits.Add(plan.ExitEdge);
            Assert.NotEqual(plan.EntryEdge, plan.ExitEdge);
            var next = ExpeditionWorld.Next(seed, plan);
            Assert.Equal(plan.NextRegion, next.Region);
            Assert.Equal(1, Math.Abs(plan.Region.X - next.Region.X) + Math.Abs(plan.Region.Y - next.Region.Y));
            Assert.Equal(((int)plan.ExitEdge + 2) % 4, (int)next.EntryEdge);
            if (plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West) Assert.Equal(plan.ExitSeam.Y, next.EntrySeam.Y);
            else Assert.Equal(plan.ExitSeam.X, next.EntrySeam.X);
            plan = next;
        }
        Assert.Equal(2, exits.Count);
    }

    [Fact]
    public void CorruptCheckpointIsRejectedAndInvalidIdNeverBecomesAPath()
    {
        Assert.Throws<ArgumentException>(() => new ExpeditionStore(root, "../escape"));
        var store = new ExpeditionStore(root, "integrity");
        var runner = ExpeditionRunner.Create(store, WorldSeed, ContentBundle.Canonical, OpenBoard);
        var path = CheckpointPath(runner.Head.ActiveCheckpoint, "integrity");
        File.AppendAllText(path, " ");
        Assert.Contains("integrity", Assert.Throws<InvalidDataException>(() =>
            ExpeditionRunner.Open(store, ContentBundle.Canonical, OpenBoard)).Message);
    }

    private long CheckpointBytes(ExpeditionCheckpointRef checkpoint, string id) => new FileInfo(CheckpointPath(checkpoint, id)).Length;
    private string CheckpointPath(ExpeditionCheckpointRef checkpoint, string id) =>
        Path.Combine(root, "expedition-" + id, checkpoint.Digest + ".json");

    private static ScenarioDefinition OpenBoard(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
    {
        // A compact start next to the pad on a flat board keeps transaction tests independent of generation cost.
        var area = plan.ExitArea;
        var horizontal = plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West;
        var cells = (from u in Enumerable.Range(0, 5) from v in Enumerable.Range(0, 2)
                     select horizontal ? new Cell(area.X + v, area.Y - 5 + u) : new Cell(area.X - 5 + u, area.Y + v)).ToList();
        var edge = cells[^1];
        var departure = horizontal ? edge with { Y = edge.Y + 1 } : edge with { X = edge.X + 1 };
        return new()
        {
            Board = new() { Width = ExpeditionWorld.Stride, Height = ExpeditionWorld.Stride },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(5, cells, cells.Take(8).ToList(), cells, edge, departure, 10, 8), [])
        };
    }

    private static string Pass(DecisionRequest request) => request.Candidates.FirstOrDefault(candidate =>
        candidate.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)?.Key ?? request.Candidates[0].Key;

    private static void FinishOpenBoard(ExpeditionRunner runner)
    {
        var last = runner.StartRound();
        var area = runner.Region.ExitArea;
        var originals = runner.Head.OriginalHeroIds.Take(2).ToArray();
        var targets = new[] { new Cell(area.X, area.Y), new Cell(area.X + 1, area.Y) };
        for (var step = 0; step < 180 && !last.State.IsCompleted; step++)
        {
            if (last.NextInput is not { } request) { last = runner.StartRound(); continue; }
            var index = Array.IndexOf(originals, request.UnitId);
            var key = Pass(request);
            if (index >= 0)
            {
                var target = targets[index];
                var from = last.State.Physical.Figures.Single(figure => figure.Id == request.UnitId).Position;
                var move = request.Candidates.Where(candidate => candidate.Kind == ActivationChoiceKind.Move && candidate.Destination is not null)
                    .OrderBy(candidate => Distance(candidate.Destination!, target)).FirstOrDefault();
                if (move is not null && Distance(move.Destination!, target) < Distance(from, target)) key = move.Key;
            }
            last = runner.Decide(key);
        }
        Assert.Contains(last.Events, occurrence => occurrence.Kind == "GameCompleted");
        Assert.Contains(ExpeditionParty.SideId, last.State.Completion!.AchievedSideIds);

        static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
