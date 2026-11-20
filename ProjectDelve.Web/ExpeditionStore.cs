using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

public sealed record ExpeditionCheckpointRef(ExpeditionRegion Region, string Digest);

public sealed record ExpeditionTransition(string Id, ExpeditionCheckpointRef Source, ExpeditionCheckpointRef Successor);

public sealed record ExpeditionHead(int Version, string Id, ulong WorldSeed, int RegionStride, long Revision,
    ImmutableArray<string> OriginalHeroIds, ExpeditionCheckpointRef ActiveCheckpoint,
    ImmutableArray<ExpeditionTransition> Transitions)
{
    // Legacy heads can omit the recipe. Their region checkpoints remain concrete.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GenerationRecipe { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExpeditionWorldProfile? WorldProfile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisplayName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? SavedAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool CurrentRegionCompleted { get; init; }
    [JsonIgnore] public ExpeditionRegion CurrentRegion => ActiveCheckpoint.Region;
    [JsonIgnore] public ImmutableArray<ExpeditionRegion> VisitedRegions =>
        [.. Transitions.Select(transition => transition.Source.Region), CurrentRegion];
}

internal sealed record ExpeditionCheckpoint(int Version, string ExpeditionId, ulong WorldSeed, ExpeditionRegionPlan Plan,
    ScenarioDefinition Scenario, ContentBundle Content, GameState State, ulong GameplayRandomState)
{
    public bool AutoChooseSingleRelevantChoice { get; init; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentAllocation? EquipmentDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecruitmentTypeId { get; init; }
}

internal enum ExpeditionWritePoint { CheckpointBeforeReplace, HeadBeforeReplace, HeadAfterReplace }

internal sealed class ExpeditionStore
{
    internal const int LegacyHeadVersion = 1;
    internal const int HeadVersion = 2;
    internal const int LegacyCheckpointVersion = 1;
    internal const int CheckpointVersion = 2;
    private const int HeadLimit = 2 * 1024 * 1024;
    private const int CheckpointLimit = 32 * 1024 * 1024;
    private static readonly Regex ValidId = new(@"\A[a-z0-9][a-z0-9-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ValidDigest = new(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly string directory;
    private readonly Action<ExpeditionWritePoint>? writeProbe;
    private string HeadPath => Path.Combine(directory, "head.json");
    internal string Id { get; }
    internal bool HasHead => File.Exists(HeadPath);
    internal static bool IsValidId(string? id) => id is not null && ValidId.IsMatch(id);

    internal ExpeditionStore(string root, string id, Action<ExpeditionWritePoint>? writeProbe = null)
    {
        if (!IsValidId(id))
            throw new ArgumentException("Expedition IDs must have 1–80 lowercase letters, digits or hyphens and start with a letter or digit.", nameof(id));
        Id = id;
        // The prefix also keeps Windows device names out of filesystem names.
        directory = Path.Combine(Path.GetFullPath(root), "expedition-" + id);
        this.writeProbe = writeProbe;
    }

    internal ExpeditionHead ReadHead()
    {
        var head = Decode<ExpeditionHead>(ReadBounded(HeadPath, HeadLimit));
        ValidateHead(head);
        // Also completes a previous publication whose directory flush failed after rename.
        if (!OperatingSystem.IsWindows()) FlushDirectory(directory);
        return head.SavedAtUtc is null ? head with { SavedAtUtc = File.GetLastWriteTimeUtc(HeadPath) } : head;
    }

    internal ExpeditionCheckpoint ReadCheckpoint(ExpeditionCheckpointRef reference)
    {
        ValidateReference(reference);
        var bytes = ReadBounded(CheckpointPath(reference), CheckpointLimit);
        if (Digest(bytes) != reference.Digest) throw new InvalidDataException($"Expedition checkpoint {reference.Digest} failed its integrity check.");
        var checkpoint = Decode<ExpeditionCheckpoint>(bytes);
        if (checkpoint.Version is not (LegacyCheckpointVersion or CheckpointVersion) || checkpoint.ExpeditionId != Id || checkpoint.Plan is null || checkpoint.Plan.Region != reference.Region ||
            checkpoint.Scenario is null || checkpoint.Content is null || checkpoint.State is null)
            throw new InvalidDataException("The Expedition checkpoint does not match its reference or supported schema.");
        ValidateEquipmentDraft(checkpoint);
        return checkpoint;
    }

    internal ExpeditionCheckpointRef WriteCheckpoint(ExpeditionCheckpoint checkpoint)
    {
        PrepareDirectory();
        var bytes = Encode(checkpoint, CheckpointLimit);
        // A codec regression must fail before the new digest can become authoritative.
        var decoded = Decode<ExpeditionCheckpoint>(bytes);
        GameEngine.ValidateRestoredState(decoded.State);
        ValidateEquipmentDraft(decoded);
        if (decoded.State.Round == 0) _ = GameEngine.CreateGame(decoded.Scenario, decoded.Content);
        var reference = new ExpeditionCheckpointRef(checkpoint.Plan.Region, Digest(bytes));
        var path = CheckpointPath(reference);
        if (!File.Exists(path))
        {
            try
            {
                WriteAtomic(path, bytes, replace: false, ExpeditionWritePoint.CheckpointBeforeReplace);
                return reference;
            }
            catch (IOException) when (File.Exists(path))
            { /* A concurrent writer published the same immutable data. */ }
        }
        if (Digest(ReadBounded(path, CheckpointLimit)) != reference.Digest)
        {
            throw new InvalidDataException($"An existing Expedition checkpoint at {reference.Digest} is corrupt.");
        }
        if (!OperatingSystem.IsWindows()) FlushDirectory(directory);
        return reference;
    }

    private static void ValidateEquipmentDraft(ExpeditionCheckpoint checkpoint)
    {
        if (checkpoint.EquipmentDraft is { } draft &&
            (draft.Assignments.IsDefault || draft.SoldObjectIds.IsDefault || draft.UpgradedUnitIds.IsDefault ||
             draft.UpgradedEquipmentIds.IsDefault || checkpoint.State.MachineInteraction?.EquipmentStation is null ||
             checkpoint.State.Pending?.Kind != DecisionKind.MachineInteraction))
            throw new InvalidDataException("An Equipment draft requires a matching pending Machine interaction.");
        if (checkpoint.RecruitmentTypeId is { } recruit &&
            (string.IsNullOrWhiteSpace(recruit) || checkpoint.EquipmentDraft is not null ||
             checkpoint.State.MachineInteraction is not { EquipmentStation: not null } interaction ||
             checkpoint.State.Pending?.Kind != DecisionKind.MachineInteraction ||
             checkpoint.State.Party?.SideId != interaction.SideId))
            throw new InvalidDataException("A recruitment draft requires its Party's pending DelveMart and cannot include Equipment changes.");
    }

    internal void ReplaceHead(ExpeditionHead? expected, ExpeditionHead next)
    {
        ValidateHead(next);
        var bytes = Encode(next, HeadLimit);
        PrepareDirectory();
        var lockPath = Path.Combine(directory, "writer.lock");
        CanonicalContent.CheckNoLinks(lockPath);
        using var writer = CanonicalContent.FileLock(lockPath);
        var observed = File.Exists(HeadPath) ? ReadHead() : null;
        if (next.Revision != (expected?.Revision ?? 0) + 1 || observed?.Revision != expected?.Revision ||
            observed?.ActiveCheckpoint != expected?.ActiveCheckpoint || observed?.GenerationRecipe != expected?.GenerationRecipe ||
            observed?.Version != expected?.Version || observed?.WorldProfile != expected?.WorldProfile)
            throw new InvalidOperationException("The Expedition head changed. Reopen the Expedition before writing another checkpoint.");
        WriteAtomic(HeadPath, bytes, replace: expected is not null, ExpeditionWritePoint.HeadBeforeReplace);
        writeProbe?.Invoke(ExpeditionWritePoint.HeadAfterReplace);
    }

    private void PrepareDirectory()
    {
        CanonicalContent.CheckNoLinks(directory);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else CreateDirectoryAndFlushParent(directory);
        CanonicalContent.CheckNoLinks(directory);
    }

    private string CheckpointPath(ExpeditionCheckpointRef reference) => Path.Combine(directory, reference.Digest + ".json");

    private void ValidateHead(ExpeditionHead head)
    {
        if (head.Version is not (LegacyHeadVersion or HeadVersion))
            throw new InvalidDataException($"Unsupported Expedition head version {head.Version}.");
        if (head.Id != Id || head.RegionStride != ExpeditionWorld.Stride || head.Revision < 1 ||
            head.OriginalHeroIds.IsDefault || !head.OriginalHeroIds.SequenceEqual(ExpeditionParty.Originals.Select(hero => hero.InstanceId)) || head.Transitions.IsDefault)
            throw new InvalidDataException("Invalid Expedition head identity, roster, or schema.");
        if (head.DisplayName is { } name && !ExpeditionCatalog.IsValidName(name))
            throw new InvalidDataException("The saved Expedition name is invalid.");
        if ((head.Version == HeadVersion && (head.GenerationRecipe is null || head.WorldProfile is null)) ||
            (head.Version == LegacyHeadVersion && head.WorldProfile is not null))
            throw new InvalidDataException("The Expedition head is missing its pinned world profile or has one in an unsupported format.");

        if (head.GenerationRecipe is { } code)
        {
            WorldGenerationRecipe recipe;
            try
            {
                recipe = WorldGenerationRecipe.Parse(code);
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { throw new InvalidDataException("The saved Expedition WLD1 recipe is invalid.", error); }
            if (recipe != ExpeditionWorld.Configure(head.WorldSeed, recipe))
                throw new InvalidDataException("The saved WLD1 recipe does not match the Expedition seed, stride, or Hero capacity.");
            if (head.WorldProfile is { } profile)
            {
                try { profile.Validate(); }
                catch (ArgumentException error) { throw new InvalidDataException(error.Message, error); }
                if (profile.Nature != recipe.NaturalTerrain)
                    throw new InvalidDataException("The pinned base nature does not match the saved WLD1 baseline recipe.");
            }
        }
        ValidateReference(head.ActiveCheckpoint);
        var plan = ExpeditionWorld.Initial(head.WorldSeed);
        foreach (var transition in head.Transitions)
        {
            if (transition is null) throw new InvalidDataException("A transition entry is missing.");
            ValidateReference(transition.Source);
            ValidateReference(transition.Successor);
            if (transition.Id != plan.TransitionId || transition.Source.Region != plan.Region ||
                transition.Successor.Region != plan.NextRegion)
                throw new InvalidDataException("Invalid Expedition transition history or region plan.");
            plan = ExpeditionWorld.Next(head.WorldSeed, plan);
        }
        if (head.CurrentRegion != plan.Region) throw new InvalidDataException("The Expedition head points outside its transition history.");
    }

    private static void ValidateReference(ExpeditionCheckpointRef reference)
    {
        if (reference?.Digest is not { } digest || !ValidDigest.IsMatch(digest))
            throw new InvalidDataException("Invalid Expedition checkpoint reference.");
    }

    private static byte[] Encode<T>(T value, int limit)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > limit) throw new IOException($"Expedition data exceeds the {limit} byte limit.");
        return bytes;
    }

    private static T Decode<T>(byte[] bytes) where T : class => JsonSerializer.Deserialize<T>(bytes, Json)
        ?? throw new InvalidDataException("Expedition data is empty.");

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ReadBounded(string path, int limit)
    {
        CanonicalContent.CheckNoLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > limit) throw new InvalidDataException($"Expedition data exceeds the {limit} byte limit.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Expedition data changed length during a read.");
        return bytes;
    }

    private void WriteAtomic(string path, byte[] bytes, bool replace, ExpeditionWritePoint beforeReplace)
    {
        CanonicalContent.CheckNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            writeProbe?.Invoke(beforeReplace);
            if (OperatingSystem.IsWindows())
            {
                const uint replaceExisting = 1, writeThrough = 8;
                if (!MoveFileEx(ExtendedWindowsPath(temporary), ExtendedWindowsPath(path), writeThrough | (replace ? replaceExisting : 0)))
                    throw IoFailure("publish file", path);
            }
            else
            {
                File.Move(temporary, path, replace);
                FlushDirectory(directory);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CreateDirectoryAndFlushParent(string path)
    {
        if (Directory.Exists(path)) return;
        var parent = Path.GetDirectoryName(path) ?? throw new IOException("The Expedition storage root does not exist.");
        CreateDirectoryAndFlushParent(parent);
        Directory.CreateDirectory(path);
        FlushDirectory(parent);
    }

    private static void FlushDirectory(string path)
    {
        var descriptor = Open(path, 0); // O_RDONLY
        if (descriptor < 0) throw IoFailure("open directory", path);
        try { if (Fsync(descriptor) != 0) throw IoFailure("flush directory", path); }
        finally { _ = Close(descriptor); }
    }

    private static IOException IoFailure(string operation, string path)
    {
        var error = new Win32Exception(Marshal.GetLastPInvokeError());
        return new IOException($"Could not {operation} '{path}'. {error.Message}", error);
    }

    private static string ExtendedWindowsPath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path :
        path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int descriptor);
}
