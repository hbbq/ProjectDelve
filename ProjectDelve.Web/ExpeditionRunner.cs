using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

public enum ExpeditionFailureStage { SourceCheckpoint, ContentRefresh, RegionGeneration, PartySetup, SuccessorCheckpoint, HeadCommit }

public sealed class ExpeditionOperationException(ExpeditionFailureStage stage, ExpeditionRegion region, ulong worldSeed, Exception cause,
    ExpeditionRegionPlan? plan = null)
    : InvalidOperationException($"Expedition {stage} failed at region ({region.X}, {region.Y}), world seed {worldSeed:X16}. {cause.Message}", cause)
{
    public ExpeditionFailureStage Stage { get; } = stage;
    public ExpeditionRegion Region { get; } = region;
    public ulong WorldSeed { get; } = worldSeed;
    public ExpeditionRegionPlan? Plan { get; } = plan;
}

// Explicitly opt-in headless runner. All gameplay calls resolve synchronously to an Engine boundary.
public sealed class ExpeditionRunner
{
    private readonly object gate = new();
    private readonly ExpeditionStore store;
    private readonly Func<ContentBundle> latest;
    private readonly Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition> generate;
    private readonly (string Recipe, ExpeditionWorldProfile Profile)? frontierUpgrade;
    private ExpeditionHead head;
    private ExpeditionCheckpoint current;
    private bool dirty;

    private ExpeditionRunner(ExpeditionStore store, Func<ContentBundle> latest,
        Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition> generate,
        ExpeditionHead head, ExpeditionCheckpoint current, (string Recipe, ExpeditionWorldProfile Profile)? frontierUpgrade = null)
    {
        this.store = store;
        this.latest = latest;
        this.generate = generate;
        this.head = head;
        this.current = current;
        this.frontierUpgrade = frontierUpgrade;
    }

    /// <summary>Creates a run with full WLD1 defaults and optional WLD1 settings. The Expedition supplies the window, seed and Hero capacity.</summary>
    public static ExpeditionRunner Create(string storageRoot, string id, ulong worldSeed, Func<ContentBundle>? latest = null,
        WorldGenerationRecipe? generationSettings = null, ExpeditionSpatialSettings? spatialSettings = null, string? displayName = null,
        ExpeditionCreditsSettings? creditsSettings = null)
    {
        var recipe = ExpeditionWorld.Configure(worldSeed, generationSettings);
        var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain, spatialSettings);
        if (creditsSettings is not null) profile = profile with { CreditsSettings = creditsSettings.Validate() };
        return Create(new ExpeditionStore(storageRoot, id), worldSeed, latest ?? ContentBundle.Canonical,
            (_, plan, content) => ExpeditionWorld.Generate(plan, content, recipe, profile), recipe.ToString(), profile, displayName);
    }

    public static ExpeditionRunner Open(string storageRoot, string id, Func<ContentBundle>? latest = null)
    {
        var store = new ExpeditionStore(storageRoot, id);
        var saved = store.ReadHead();
        var recipe = saved.GenerationRecipe is { } code ? WorldGenerationRecipe.Parse(code) : ExpeditionWorld.Configure(saved.WorldSeed);
        var recordedProfile = saved.WorldProfile ?? ExpeditionWorldProfile.CreateV1(recipe.NaturalTerrain);
        recordedProfile.Validate();
        var profile = recordedProfile with { StationVersion = ExpeditionWorldProfile.CurrentStationVersion,
            EconomyVersion = ExpeditionWorldProfile.CurrentEconomyVersion,
            CreditsVersion = ExpeditionWorldProfile.CurrentCreditsVersion, CreditsSettings = recordedProfile.CreditsSettings ?? new() };
        return new(store, latest ?? ContentBundle.Canonical, (_, plan, content) => ExpeditionWorld.Generate(plan, content, recipe, profile),
            saved, Load(store, saved), saved.Version == ExpeditionStore.LegacyHeadVersion || profile != recordedProfile
                ? (recipe.ToString(), profile) : null);
    }

    internal static ExpeditionRunner Create(ExpeditionStore store, ulong worldSeed, Func<ContentBundle> latest,
        Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition> generate, string? generationRecipe = null,
        ExpeditionWorldProfile? profile = null, string? displayName = null)
    {
        var initial = Build(store.Id, worldSeed, ExpeditionWorld.Initial(worldSeed), latest, generate, null);
        var reference = AtStage(ExpeditionFailureStage.SuccessorCheckpoint, initial.Plan, worldSeed, () => store.WriteCheckpoint(initial));
        var head = new ExpeditionHead(profile is null ? ExpeditionStore.LegacyHeadVersion : ExpeditionStore.HeadVersion,
            store.Id, worldSeed, ExpeditionWorld.Stride, 1,
            [.. ExpeditionParty.Originals.Select(hero => hero.InstanceId)], reference, [])
        { GenerationRecipe = generationRecipe, WorldProfile = profile, DisplayName = displayName, SavedAtUtc = DateTimeOffset.UtcNow };
        AtStage(ExpeditionFailureStage.HeadCommit, initial.Plan, worldSeed, () => store.ReplaceHead(null, head));
        return new(store, latest, generate, head, initial);
    }

    internal static ExpeditionRunner Open(ExpeditionStore store, Func<ContentBundle> latest,
        Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition> generate)
    {
        var saved = store.ReadHead();
        return new(store, latest, generate, saved, Load(store, saved));
    }

    public ExpeditionHead Head { get { lock (gate) return head; } }
    public ExpeditionRegionPlan Region { get { lock (gate) return current.Plan; } }
    public GameState Snapshot() { lock (gate) return PlaytestGame.Detach(current.State); }
    internal ScenarioDefinition Scenario { get { lock (gate) return current.Scenario; } }
    internal bool HasUnsavedChanges { get { lock (gate) return dirty; } }
    internal bool AutoChoose { get { lock (gate) return current.AutoChooseSingleRelevantChoice; } }
    internal EquipmentAllocation? EquipmentDraft { get { lock (gate) return current.EquipmentDraft; } }
    internal string? RecruitmentDraft { get { lock (gate) return current.RecruitmentTypeId; } }
    internal bool CanContinue { get { lock (gate) return MayContinue(current.State); } }

    // The web runner supplies the same ordinary Engine resolution and automation loop,
    // while this owner records the exact RNG and resulting stable boundary.
    internal EngineResult ResolveBoundary(Func<GameState, IRandomProvider, EngineResult> command, bool autoChoose) => Apply(command, autoChoose);

    public EngineResult StartRound() => Apply((state, random) => GameEngine.StartRound(state, random, autoChooseSingleRelevantChoice: false));

    public EngineResult Decide(string? candidateKey) => Apply((state, random) =>
        GameEngine.Advance(state, new SubmittedChoice(candidateKey), random, autoChooseSingleRelevantChoice: false));

    /// <summary>Resolves one pending decision for an Automated group using the standard WLD1 Orders and Behavior.</summary>
    public EngineResult DecideAutomatically() => Apply((state, random) =>
    {
        if (state.Pending is not { } request || state.ControllerFor(request) != ControllerKind.Automated)
            throw new InvalidOperationException("There is no pending automated Expedition decision.");
        return GameEngine.Advance(state, new OrderAwareAutomatedProvider(), random, autoChooseSingleRelevantChoice: false);
    });

    public ExpeditionHead Save() => Save(null);

    internal ExpeditionHead Save(EquipmentAllocation? equipmentDraft, string? recruitmentDraft = null)
    {
        lock (gate)
        {
            if (equipmentDraft is not null && current.State.MachineInteraction?.EquipmentStation is null)
                throw new ExpeditionInputException("An Equipment draft requires a pending DelveMart.");
            if (recruitmentDraft is not null &&
                (string.IsNullOrWhiteSpace(recruitmentDraft) || equipmentDraft is not null ||
                 current.State.MachineInteraction is not { EquipmentStation: not null } station || current.State.Party?.SideId != station.SideId))
                throw new ExpeditionInputException("A recruitment draft requires its Party's pending DelveMart and cannot include Equipment changes.");
            if (equipmentDraft is not null) CommitCheckpoint(current with { EquipmentDraft = equipmentDraft, RecruitmentTypeId = null });
            else if (recruitmentDraft is not null) CommitCheckpoint(current with { RecruitmentTypeId = recruitmentDraft, EquipmentDraft = null });
            else if (dirty) CommitCheckpoint(current);
            return head;
        }
    }

    public ExpeditionTransition Continue(string transitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transitionId);
        lock (gate)
        {
            // A retry after an acknowledgement was lost, or another writer won the same handoff.
            var persisted = AtStage(ExpeditionFailureStage.SourceCheckpoint, current.Plan, head.WorldSeed, store.ReadHead);
            if (persisted.Transitions.FirstOrDefault(transition => transition.Id == transitionId) is { } previous)
            {
                if (persisted.Revision != head.Revision && !dirty) Install(persisted, Load(store, persisted));
                return previous;
            }
            if (persisted.Revision != head.Revision || persisted.ActiveCheckpoint != head.ActiveCheckpoint ||
                persisted.GenerationRecipe != head.GenerationRecipe || persisted.Version != head.Version || persisted.WorldProfile != head.WorldProfile)
                throw new InvalidOperationException("The Expedition head changed. Reopen the Expedition before continuing.");
            if (transitionId != current.Plan.TransitionId)
                throw new ArgumentException("The transition ID does not match the current region.", nameof(transitionId));
            if (!MayContinue(current.State))
                throw new InvalidOperationException("The Expedition Side must complete the current Reach Area objective before continuing.");

            // Load the committed source from disk. Even a valid in-memory completion cannot authorize a handoff.
            if (dirty) CommitCheckpoint(current);
            var source = AtStage(ExpeditionFailureStage.SourceCheckpoint, current.Plan, head.WorldSeed, () => Load(store, head));
            if (!MayContinue(source.State))
                throw new InvalidDataException("The committed Expedition source checkpoint does not contain an eligible completion.");
            var plan = AtStage(ExpeditionFailureStage.RegionGeneration, source.Plan, head.WorldSeed,
                () => ExpeditionWorld.Next(head.WorldSeed, source.Plan));
            var successor = Build(store.Id, head.WorldSeed, plan, latest, generate, source.State) with
            { AutoChooseSingleRelevantChoice = source.AutoChooseSingleRelevantChoice };
            var saved = AtStage(ExpeditionFailureStage.SuccessorCheckpoint, plan, head.WorldSeed, () => store.WriteCheckpoint(successor));
            var receipt = new ExpeditionTransition(transitionId, head.ActiveCheckpoint, saved);
            var next = head with { Revision = checked(head.Revision + 1), ActiveCheckpoint = saved, Transitions = head.Transitions.Add(receipt), CurrentRegionCompleted = false, SavedAtUtc = DateTimeOffset.UtcNow };
            if (frontierUpgrade is { } upgrade)
                next = next with { Version = ExpeditionStore.HeadVersion, GenerationRecipe = upgrade.Recipe, WorldProfile = upgrade.Profile };

            try { store.ReplaceHead(head, next); }
            catch (Exception error) when (IsRecoverable(error))
            {
                // The rename may have succeeded even if the caller did not get its acknowledgement.
                var committed = TryReadCommittedTransition(transitionId);
                if (committed is not null) return committed;
                throw new ExpeditionOperationException(ExpeditionFailureStage.HeadCommit, plan.Region, head.WorldSeed, error, plan);
            }
            Install(next, successor);
            return receipt;
        }
    }

    private EngineResult Apply(Func<GameState, IRandomProvider, EngineResult> command, bool? autoChoose = null)
    {
        lock (gate)
        {
            var random = new ExpeditionRandom(current.GameplayRandomState);
            var result = command(current.State, random);
            current = current with { State = result.State, GameplayRandomState = random.State,
                AutoChooseSingleRelevantChoice = autoChoose ?? current.AutoChooseSingleRelevantChoice,
                EquipmentDraft = Equals(current.State.MachineInteraction, result.State.MachineInteraction) ? current.EquipmentDraft : null,
                RecruitmentTypeId = Equals(current.State.MachineInteraction, result.State.MachineInteraction) ? current.RecruitmentTypeId : null };
            dirty = true;
            if (result.State.IsCompleted) CommitCheckpoint(current);
            return PlaytestGame.Detach(result);
        }
    }

    private void CommitCheckpoint(ExpeditionCheckpoint boundary)
    {
        boundary = boundary with { Version = ExpeditionStore.CheckpointVersion };
        var reference = AtStage(ExpeditionFailureStage.SourceCheckpoint, boundary.Plan, head.WorldSeed, () => store.WriteCheckpoint(boundary));
        var next = head with { Revision = checked(head.Revision + 1), ActiveCheckpoint = reference, CurrentRegionCompleted = MayContinue(boundary.State), SavedAtUtc = DateTimeOffset.UtcNow };
        try { store.ReplaceHead(head, next); }
        catch (Exception error) when (IsRecoverable(error))
        {
            // A retry can save the same stable state without repeating the Engine decision.
            ExpeditionHead? persisted = null;
            try { persisted = store.ReadHead(); }
            catch (Exception readError) when (IsRecoverable(readError)) { }
            if (persisted?.Revision == next.Revision && persisted.ActiveCheckpoint == reference)
            {
                Install(persisted, boundary);
                return;
            }
            throw new ExpeditionOperationException(ExpeditionFailureStage.SourceCheckpoint, boundary.Plan.Region, head.WorldSeed, error, boundary.Plan);
        }
        Install(next, boundary);
    }

    private ExpeditionTransition? TryReadCommittedTransition(string transitionId)
    {
        try
        {
            var saved = store.ReadHead();
            var receipt = saved.Transitions.FirstOrDefault(transition => transition.Id == transitionId);
            if (receipt is not null) Install(saved, Load(store, saved));
            return receipt;
        }
        catch (Exception error) when (IsRecoverable(error)) { return null; }
    }

    private void Install(ExpeditionHead saved, ExpeditionCheckpoint checkpoint)
    {
        head = saved;
        current = checkpoint;
        dirty = false;
    }

    private static bool MayContinue(GameState state) => state.Pending is null &&
        state.Objectives.Any(objective => objective.SideId == ExpeditionParty.SideId && objective.Objective == ScenarioObjective.ReachArea) &&
        state.Completion?.AchievedSideIds.Contains(ExpeditionParty.SideId) == true;

    private static ExpeditionCheckpoint Build(string id, ulong seed, ExpeditionRegionPlan plan, Func<ContentBundle> latest,
        Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition> generate, GameState? previous)
    {
        var members = ExpeditionParty.Members(previous);
        var content = AtStage(ExpeditionFailureStage.ContentRefresh, plan, seed,
            () => ExpeditionParty.ResolveContent(latest(), previous, members));
        var terrain = AtStage(ExpeditionFailureStage.RegionGeneration, plan, seed,
            () => generate(seed, plan with { DeploymentCapacity = members.Count }, content));
        return AtStage(ExpeditionFailureStage.PartySetup, plan, seed, () =>
        {
            var scenario = ExpeditionParty.Prepare(terrain, content, plan.ExitArea, members, previous);
            var state = GameEngine.CreateGame(scenario, content);
            if (state.Physical.Board.Width != ExpeditionWorld.Stride || state.Physical.Board.Height != ExpeditionWorld.Stride)
                throw new ArgumentException("Expedition regions must be 60 by 60 Cells.");
            return new ExpeditionCheckpoint(ExpeditionStore.CheckpointVersion, id, seed, plan, scenario, content, state,
                ExpeditionWorld.Seed(seed, plan.Region, "gameplay"));
        });
    }

    private static ExpeditionCheckpoint Load(ExpeditionStore store, ExpeditionHead head)
    {
        var checkpoint = store.ReadCheckpoint(head.ActiveCheckpoint);
        var expectedPlan = ExpeditionWorld.Initial(head.WorldSeed);
        foreach (var _ in head.Transitions) expectedPlan = ExpeditionWorld.Next(head.WorldSeed, expectedPlan);
        if (checkpoint.WorldSeed != head.WorldSeed || checkpoint.Plan != expectedPlan ||
            checkpoint.State.Physical.Board.Width != head.RegionStride || checkpoint.State.Physical.Board.Height != head.RegionStride ||
            checkpoint.Scenario.Board.Width != head.RegionStride || checkpoint.Scenario.Board.Height != head.RegionStride ||
            ExpeditionParty.Originals.Any(hero => !checkpoint.State.Units.Any(unit => unit.Id == hero.InstanceId &&
                unit.TypeId == hero.TypeId && unit.SideId == ExpeditionParty.SideId)))
            throw new InvalidDataException("The Expedition checkpoint does not match its region plan or original roster.");
        GameEngine.ValidateRestoredState(checkpoint.State);
        if (checkpoint.State.Party is null && checkpoint.Version == ExpeditionStore.LegacyCheckpointVersion)
        {
            // The stored bytes were verified before this in-memory migration.
            // Ordinary Scenario Initial Unit selectors retain their existing meaning.
            var state = checkpoint.State;
            var party = ExpeditionParty.Founders;
            SideObjective Objective(SideObjective objective) => objective.SideId == ExpeditionParty.SideId &&
                objective.Objective == ScenarioObjective.ReachArea
                    ? objective with { Selectors = [UnitSelector.Party(ExpeditionParty.SideId)] } : objective;
            state.Party = party;
            state.Objectives = state.Objectives.Select(Objective).ToList();
            checkpoint = checkpoint with
            {
                Version = ExpeditionStore.CheckpointVersion,
                Scenario = checkpoint.Scenario with
                {
                    Party = party,
                    Objectives = checkpoint.Scenario.Objectives?.Select(Objective).ToList()
                }
            };
            GameEngine.ValidateRestoredState(state);
        }
        if (checkpoint.State.Party is not { } roster || roster.SideId != ExpeditionParty.SideId ||
            !roster.MemberIds.Take(head.OriginalHeroIds.Length).SequenceEqual(head.OriginalHeroIds))
            throw new InvalidDataException("The Expedition Party does not preserve its founding roster.");
        return checkpoint;
    }

    private static T AtStage<T>(ExpeditionFailureStage stage, ExpeditionRegionPlan plan, ulong seed, Func<T> work)
    {
        try { return work(); }
        catch (Exception error) when (IsRecoverable(error))
        { throw new ExpeditionOperationException(stage, plan.Region, seed, error, plan); }
    }

    private static void AtStage(ExpeditionFailureStage stage, ExpeditionRegionPlan plan, ulong seed, Action work) =>
        AtStage(stage, plan, seed, () => { work(); return true; });

    private static bool IsRecoverable(Exception error) => error is not (OperationCanceledException or OutOfMemoryException);

    private sealed class SubmittedChoice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
}
