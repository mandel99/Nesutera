using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Pharaoh.Assets.Save;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Tutorials;

namespace Pharaoh.Engine.Loading;

public enum LegacyMissionFlowState
{
    SerializedStateReady,
    CityEntered
}

public enum LegacyPostLoadCondition
{
    Always,
    ForEachCoordinatePairTwice,
    SaveVersionBefore141,
    SaveVersionBefore142
}

public sealed record LegacyPostLoadStep(uint Address, LegacyPostLoadCondition Condition);

/// <summary>
/// Ordered reconstruction of Pharaoh.exe FUN_004d52a0. The addresses are kept
/// explicit until each subsystem has independently been identified and rewritten.
/// </summary>
public static class LegacyPostLoadPlan
{
    [OriginalAddress(0x004D52A0, "FUN_004d52a0")]
    public static IReadOnlyList<LegacyPostLoadStep> Steps { get; } = Array.AsReadOnly(
    [
        Step(0x0040F8F0), Step(0x0046F7B0), Step(0x00540C90), Step(0x00540ED0),
        Step(0x00541160), Step(0x004F4520), Step(0x0053D970), Step(0x004BC720),
        Step(0x004771C0), Step(0x0046F930, LegacyPostLoadCondition.ForEachCoordinatePairTwice),
        Step(0x004E7E20), Step(0x004677C0), Step(0x00468170), Step(0x004E59A0),
        Step(0x00485F70), Step(0x00486E70, LegacyPostLoadCondition.SaveVersionBefore141),
        Step(0x00489840, LegacyPostLoadCondition.SaveVersionBefore142), Step(0x004DEA50),
        Step(0x004BE160), Step(0x00430530), Step(0x004DDF90), Step(0x004DE080),
        Step(0x00517A20), Step(0x005178B0), Step(0x00517370), Step(0x00517660),
        Step(0x005178E0), Step(0x004B32C0), Step(0x00468860), Step(0x00457AF0),
        Step(0x0045E040), Step(0x00404350), Step(0x004051A0), Step(0x004E1830),
        Step(0x0041D730), Step(0x0041DF60), Step(0x00417E70), Step(0x00476750),
        Step(0x0046D840), Step(0x004BC7A0), Step(0x004BCA90), Step(0x00442660),
        Step(0x004426C0), Step(0x0046F190), Step(0x004D5AD0), Step(0x004D5BC0),
        Step(0x00443330),
        Step(0x005596D0), Step(0x0045AA30), Step(0x004F61E0), Step(0x004A3560)
    ]);

    private static LegacyPostLoadStep Step(
        uint address,
        LegacyPostLoadCondition condition = LegacyPostLoadCondition.Always) =>
        new(address, condition);
}

public sealed class LegacyMissionSession
{
    private readonly Dictionary<string, ReadOnlyMemory<byte>> logicalChunks;
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> replayChunks;

    internal LegacyMissionSession(
        GameLoadRequest request,
        LegacyFileDocument document,
        IReadOnlyDictionary<int, ReadOnlyMemory<byte>> descriptors,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks)
    {
        Request = request;
        Document = document;
        DescriptorData = descriptors;
        this.logicalChunks = logicalChunks.ToDictionary(
            item => item.Key,
            item => (ReadOnlyMemory<byte>)item.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        replayChunks = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(
            this.logicalChunks.ToDictionary(
                item => item.Key,
                item => (ReadOnlyMemory<byte>)item.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));
        LogicalChunks = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(this.logicalChunks);
    }

    public GameLoadRequest Request { get; }
    public LegacyFileDocument Document { get; }
    public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> DescriptorData { get; }
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> LogicalChunks { get; }
    public IReadOnlyList<LegacyPostLoadStep> PendingPostLoadSteps => LegacyPostLoadPlan.Steps;
    public LegacyMissionFlowState FlowState { get; private set; } = LegacyMissionFlowState.SerializedStateReady;

    [OriginalAddress(0x004D4E20, "serialize current runtime grids, tutorial flags and campaign rank")]
    public LegacyFileDocument CreateSaveDocument()
    {
        using MemoryStream stream = new();
        Document.Write(stream);
        stream.Position = 0;
        LegacyFileDocument snapshot = LegacyFileDocument.Load(stream);
        foreach ((string name, ReadOnlyMemory<byte> bytes) in logicalChunks)
            if (!snapshot.ReadLogicalChunk(name).AsSpan().SequenceEqual(bytes.Span))
                snapshot.ReplaceLogicalChunk(name, bytes.Span);
        if (Request.Kind == GameLoadKind.NewCampaign)
            snapshot.SetCampaignMissionRank(Request.MissionIndex);
        return snapshot;
    }

    [RewriteOnly("Publishes a validated runtime grid replacement while save serialization remains a separate boundary")]
    public void ReplaceLogicalChunk(string name, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!logicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> previous))
            throw new KeyNotFoundException($"Logical chunk '{name}' is not present in this mission.");
        if (data.Length != previous.Length)
            throw new ArgumentException(
                $"Replacement chunk '{name}' must remain {previous.Length} bytes.", nameof(data));
        if(name.Equals("terrain_grid",StringComparison.OrdinalIgnoreCase)||name.Equals("scenario_info",StringComparison.OrdinalIgnoreCase))
            Pharaoh.Engine.Rendering.LegacyTerrainAccess.Invalidate(LogicalChunks);
        if (MemoryMarshal.TryGetArray(previous, out ArraySegment<byte> segment) &&
            segment.Array is not null)
        {
            data.Span.CopyTo(segment.Array.AsSpan(segment.Offset, segment.Count));
            return;
        }
        logicalChunks[name] = data.ToArray();
    }

    /// <summary>
    /// Returns the persistent runtime buffer that corresponds to Pharaoh's
    /// fixed global grids. Simulation systems mutate this buffer in place and
    /// avoid cloning complete save chunks on every legacy tick.
    /// </summary>
    [RewriteOnly("Exposes persistent runtime grids equivalent to the original fixed global buffers")]
    internal Memory<byte> GetWritableLogicalChunk(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!logicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> current))
            throw new KeyNotFoundException($"Logical chunk '{name}' is not present in this mission.");
        if (MemoryMarshal.TryGetArray(current, out ArraySegment<byte> segment) &&
            segment.Array is not null)
            return segment.Array.AsMemory(segment.Offset, segment.Count);

        byte[] copy = current.ToArray();
        logicalChunks[name] = copy;
        return copy;
    }

    /// <summary>
    /// Recreates the immutable mission-start state while a campaign or custom
    /// mission remains in memory. A session opened from a SAV must instead load
    /// the family's autosave_replay.sav file through <see cref="LegacyReplayMission"/>.
    /// </summary>
    [OriginalAddress(0x004183D0, "FUN_004183d0")]
    [OriginalAddress(0x00418510, "Replay mission menu branch")]
    public LegacyMissionSession CreateReplaySession()
    {
        if (Request.Kind == GameLoadKind.SavedGame)
            throw new InvalidOperationException(
                "A loaded saved game must replay from its autosave_replay.sav mission-start file.");
        return new(Request, Document, DescriptorData, replayChunks);
    }

    /// <summary>Rewrites the state transition performed by FUN_00418220 for briefing state 0x35.</summary>
    public void EnterCity()
    {
        if (FlowState != LegacyMissionFlowState.SerializedStateReady)
            throw new InvalidOperationException("The mission has already entered city state 0x01.");
        FlowState = LegacyMissionFlowState.CityEntered;
    }
}

/// <summary>Resolves Pharaoh's mission-start autosave used by File -> Replay Mission.</summary>
public static class LegacyReplayMission
{
    public const string StartSaveFileName = "autosave_replay.sav";
    public const string HistorySaveFileName = "autosave_history.sav";

    [OriginalAddress(0x004183D0, "FUN_004183d0")]
    public static string? ResolveStartSavePath(GameLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != GameLoadKind.SavedGame)
            return null;

        string fullSavePath = Path.GetFullPath(request.Path);
        string? familyDirectory = Path.GetDirectoryName(fullSavePath);
        if (string.IsNullOrWhiteSpace(familyDirectory))
            throw new InvalidDataException("The saved game has no family save directory.");
        return Path.Combine(familyDirectory, StartSaveFileName);
    }
}

/// <summary>
/// Rewrites the file-facing portion of FUN_005405b0 -> FUN_004d58b0 -> FUN_004d41e0.
/// It deliberately stops before executing the still-unidentified engine subsystems
/// called by FUN_004d52a0; their exact order remains available in LegacyPostLoadPlan.
/// </summary>
public sealed class LegacyMissionLoader : IGameLoader<LegacyMissionSession>
{
    public const int LegacySaveDescriptorCount = 175;
    public const int ExtendedSaveDescriptorCount = 181;
    public const int ExtendedSaveDescriptorVersion = 154;

    /// <summary>
    /// Mirrors the compatibility branch in Pharaoh.exe FUN_004d41e0: versions
    /// before 154 stop at descriptor index 175; newer streams consume all 181.
    /// </summary>
    public static int ExpectedDescriptorCount(int saveVersion) =>
        saveVersion < ExtendedSaveDescriptorVersion
            ? LegacySaveDescriptorCount
            : ExtendedSaveDescriptorCount;

    public Task<LegacyMissionSession> LoadAsync(
        GameLoadRequest request,
        IProgress<GameLoadProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Load(request, progress, cancellationToken), cancellationToken);

    [OriginalAddress(0x004D5100, "FUN_004d5100")]
    [OriginalAddress(0x004D5820, "FUN_004d5820")]
    [OriginalAddress(0x004D58B0, "FUN_004d58b0")]
    private static LegacyMissionSession Load(
        GameLoadRequest request,
        IProgress<GameLoadProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new(GameLoadStage.Validating, 0.02f, "Validating mission source"));
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(new(GameLoadStage.ReadingFiles, 0.12f, "Opening mission PAK entry"));
        LegacyFileDocument document = request.Kind switch
        {
            GameLoadKind.NewCampaign => MissionPakDocument.Load(request.Path).OpenEntry(request.MissionIndex),
            GameLoadKind.CustomMission or GameLoadKind.SavedGame => LegacyFileDocument.Load(request.Path),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        cancellationToken.ThrowIfCancellationRequested();

        int expectedDescriptorCount = ExpectedDescriptorCount(document.Layout.Version);
        if (request.Kind == GameLoadKind.NewCampaign &&
            document.Layout.DeclaredChunkCount != expectedDescriptorCount)
        {
            throw new InvalidDataException(
                $"Campaign entry v{document.Layout.Version} declares " +
                $"{document.Layout.DeclaredChunkCount} descriptors; Pharaoh.exe " +
                $"FUN_004d41e0 expects {expectedDescriptorCount}.");
        }

        progress.Report(new(GameLoadStage.Decompressing, 0.28f, "Decoding all stored descriptors"));
        Dictionary<int, ReadOnlyMemory<byte>> descriptorData = [];
        int decoded = 0;
        foreach (LegacyDocumentChunk chunk in document.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptorData.Add(chunk.Location.Descriptor.Index, chunk.Decode());
            decoded++;
            float fraction = 0.28f + (0.32f * decoded / Math.Max(1, document.Chunks.Count));
            progress.Report(new(GameLoadStage.Decompressing, fraction, $"Decoded descriptor {chunk.Location.Descriptor.Index}"));
        }

        progress.Report(new(GameLoadStage.ParsingMap, 0.64f, "Materializing logical save chunks"));
        IReadOnlyList<LegacyLogicalChunk> catalog = LegacyLogicalChunkCatalog.Resolve(document.Layout);
        Dictionary<string, ReadOnlyMemory<byte>> logicalChunks = new(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyLogicalChunk logical in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] data = new byte[logical.Length];
            int outputOffset = 0;
            foreach (LegacyLogicalChunkSlice slice in logical.Slices)
            {
                ReadOnlyMemory<byte> descriptor = descriptorData[slice.DescriptorIndex];
                descriptor.Span.Slice(slice.OffsetInDescriptor, slice.Length)
                    .CopyTo(data.AsSpan(outputOffset));
                outputOffset += slice.Length;
            }
            logicalChunks.Add(logical.Name, data);
        }

        progress.Report(new(GameLoadStage.LoadingScenario, 0.82f, "Scenario state is ready"));
        if (!logicalChunks.ContainsKey("scenario_info"))
            throw new InvalidDataException("The mission has no scenario_info logical chunk.");

        progress.Report(new(GameLoadStage.CreatingSession, 0.95f, "Creating mission session"));
        LegacyMissionSession session = new(
            request,
            document,
            new ReadOnlyDictionary<int, ReadOnlyMemory<byte>>(descriptorData),
            new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(logicalChunks));
        if (request.Kind == GameLoadKind.NewCampaign)
            LegacyTutorialManager.InitializeNewCampaign(session, request.MissionIndex);
        progress.Report(new(GameLoadStage.Completed, 1f, "Mission serialized state prepared"));
        return session;
    }

    private static void Validate(GameLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        if (!File.Exists(request.Path))
            throw new FileNotFoundException("Mission source was not found.", request.Path);
        if (request.Difficulty < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Difficulty cannot be negative.");
        if (request.Kind == GameLoadKind.NewCampaign && request.MissionIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Campaign mission index is required.");
    }
}
