using System.Collections.Immutable;
using System.Text.Json;

namespace ProjectDelve.Generation;

/// <summary>Engineering failures, separate from rejected input configuration.</summary>
public enum WorldGenerationFailureKind
{
    ProvenImpossibleConnectivity, IncompleteAnalysis, WitnessMaterializationMismatch,
    ConstructionInvariantViolation, FailedFinalValidation, InsufficientDeployment, UnexpectedImplementationFailure
}

/// <summary>Reproducible internal authoring failure; never a failed-seed/retry result.</summary>
public sealed class PlayReadyGenerationException : InvalidOperationException
{
    public ulong Seed { get; }
    public string Stage { get; }
    public long BoardRevision { get; }
    public string ConfigurationJson { get; }
    public WorldGenerationFailureKind FailureKind { get; }
    public string DiagnosticsJson { get; }
    internal MandatoryRepairReport? Repair { get; }
    internal ImmutableArray<CompletionFailure> Failures { get; }

    internal PlayReadyGenerationException(WorldGenerationPlan plan, string stage, long revision, string detail,
        MandatoryRepairReport? repair = null, ImmutableArray<CompletionFailure> failures = default,
        WorldGenerationFailureKind? kind = null, Exception? inner = null, CompletionDiagnostic? completion = null)
        : base($"WLD1 internal generation failure; seed {plan.World.Seed:X16}; stage {stage}; revision {revision}: {detail}", inner)
    {
        Seed = plan.World.Seed; Stage = stage; BoardRevision = revision; Repair = repair;
        Failures = failures.IsDefault ? [] : failures;
        FailureKind = kind ?? Classify(stage, repair, completion);
        DiagnosticsJson = JsonSerializer.Serialize(new { Repair = repair, Completion = completion, Failures, Detail = detail });
        ConfigurationJson = JsonSerializer.Serialize(new
        {
            plan.World, plan.Window, plan.LootPreset,
            Steps = plan.Steps.Select(s => new { s.Id, s.Kind, s.DependsOn,
                Parameters = JsonSerializer.SerializeToElement(s.Parameters, s.Parameters.GetType()) })
        });
    }

    private static WorldGenerationFailureKind Classify(string stage, MandatoryRepairReport? repair, CompletionDiagnostic? completion)
    {
        if (completion is not null) return completion.Status switch
        {
            CompletionStatus.Incomplete => WorldGenerationFailureKind.IncompleteAnalysis,
            CompletionStatus.Impossible => WorldGenerationFailureKind.ProvenImpossibleConnectivity,
            _ when !completion.Failures.IsEmpty => WorldGenerationFailureKind.FailedFinalValidation,
            CompletionStatus.Error => WorldGenerationFailureKind.ConstructionInvariantViolation,
            _ => WorldGenerationFailureKind.UnexpectedImplementationFailure
        };
        if (stage == "final-materialization") return WorldGenerationFailureKind.FailedFinalValidation;
        return repair?.Status switch
        {
            MandatoryRepairStatus.Impossible => WorldGenerationFailureKind.ProvenImpossibleConnectivity,
            MandatoryRepairStatus.Incomplete => WorldGenerationFailureKind.IncompleteAnalysis,
            MandatoryRepairStatus.WitnessMismatch => WorldGenerationFailureKind.WitnessMaterializationMismatch,
            MandatoryRepairStatus.InvariantError => WorldGenerationFailureKind.ConstructionInvariantViolation,
            _ => WorldGenerationFailureKind.UnexpectedImplementationFailure
        };
    }
}
