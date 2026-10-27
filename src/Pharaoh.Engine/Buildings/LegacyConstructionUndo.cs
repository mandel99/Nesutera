using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyConstructionAdvance(
    IReadOnlyList<int> ChangedTiles,
    bool UndoAvailabilityChanged)
{
    public bool TerrainChanged => ChangedTiles.Count != 0;
}

public readonly record struct LegacyConstructionUndoResult(
    bool Restored,
    int Refund,
    IReadOnlyList<int> ChangedTiles);

/// <summary>
/// Reconstructs the single construction undo slot used by Pharaoh. Clear-land
/// buildings remain in state 6 until dispatcher phase 0x28; the undo slot then
/// remains live for 500 legacy ticks.
/// </summary>
public sealed class LegacyConstructionUndo
{
    public const int UndoLifetimeTicks = 500;
    public const int DemolitionCleanupPhase = 0x28;

    private const int BuildingRecordSize = 264;
    private const int MaximumBuildings = 4000;
    private const int DemolitionMarkerOffset = 191;
    private const uint ClearedBuildingTerrainMask = 0x03052e84;

    private static readonly string[] SnapshotChunks =
    [
        "terrain_grid", "building_grid", "buildings", "image_grid",
        "bitfields_grid", "edge_grid", "aqueduct_grid", "sprite_grid", "building_storages"
    ];

    private readonly LegacyMissionSession session;
    private Dictionary<string, byte[]>? capture;
    private readonly HashSet<int> capturedBuildingIds = [];
    private int refund;
    private int ticksRemaining;
    private bool committedDemolition;

    private LegacyConstructionUndo(LegacyMissionSession session) => this.session = session;

    public bool UndoAvailable => capture is not null && ticksRemaining > 0;
    public int TicksRemaining => UndoAvailable ? ticksRemaining : 0;

    public static LegacyConstructionUndo Create(LegacyMissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new LegacyConstructionUndo(session);
    }

    [OriginalAddress(0x00484F60, "FUN_00484f60")]
    public void BeginAction()
    {
        capture = SnapshotChunks.ToDictionary(
            name => name,
            name => session.LogicalChunks[name].ToArray(),
            StringComparer.OrdinalIgnoreCase);
        refund = 0;
        ticksRemaining = 0;
        committedDemolition = false;
        capturedBuildingIds.Clear();
    }

    public void CancelAction()
    {
        if (!committedDemolition && ticksRemaining == 0)
        {
            capture = null;
            capturedBuildingIds.Clear();
        }
    }

    [OriginalAddress(0x00485200, "FUN_00485200")]
    public void CommitImmediate(int amount)
    {
        EnsureCapture();
        refund = amount;
        committedDemolition = false;
        CaptureChangedBuildingRecords();
        ticksRemaining = UndoLifetimeTicks;
    }

    public void CommitDemolition(int amount, IReadOnlyList<int> pendingBuildingIds)
    {
        ArgumentNullException.ThrowIfNull(pendingBuildingIds);
        EnsureCapture();
        refund = amount;
        committedDemolition = pendingBuildingIds.Count != 0;
        capturedBuildingIds.Clear();
        capturedBuildingIds.UnionWith(pendingBuildingIds);
        if (!committedDemolition)
        {
            capture = null;
            capturedBuildingIds.Clear();
        }
    }

    [OriginalAddress(0x00451C50, "FUN_00451c50")]
    [OriginalAddress(0x004525F0, "FUN_004525f0")]
    [OriginalAddress(0x00485DC0, "FUN_00485dc0")]
    public LegacyConstructionAdvance Advance(long firstLegacyTick, long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstLegacyTick);
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        bool availableBefore = UndoAvailable;
        HashSet<int> changed = [];
        for (long offset = 0; offset < legacyTicks; offset++)
        {
            bool activatedThisTick = false;
            int phase = (int)((firstLegacyTick + offset) %
                LegacySimulationClock.DispatcherTicksPerDay);
            if (phase == DemolitionCleanupPhase)
                ActivatePendingBuildings();
            if (phase == DemolitionCleanupPhase && CleanupDemolishedBuildings(changed))
            {
                if (committedDemolition && capture is not null)
                {
                    ticksRemaining = UndoLifetimeTicks;
                    committedDemolition = false;
                    activatedThisTick = true;
                }
            }

            if (!activatedThisTick && ticksRemaining > 0 && --ticksRemaining == 0)
            {
                capture = null;
                refund = 0;
                capturedBuildingIds.Clear();
            }
        }
        return new LegacyConstructionAdvance(
            changed.Order().ToArray(), availableBefore != UndoAvailable);
    }

    [OriginalAddress(0x004525F0, "FUN_004525f0")]
    private void ActivatePendingBuildings()
    {
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        for (int id = 1; id < MaximumBuildings; id++)
            if (buildings[id * BuildingRecordSize] == 3)
                buildings[id * BuildingRecordSize] = 1;
    }

    [OriginalAddress(0x00485230, "Undo construction callback")]
    public LegacyConstructionUndoResult Undo()
    {
        if (!UndoAvailable || capture is null)
            return new LegacyConstructionUndoResult(false, 0, []);

        IReadOnlyList<int> changed = FindChangedTiles(capture);
        foreach ((string name, byte[] data) in capture)
        {
            if (name.Equals("building_storages", StringComparison.OrdinalIgnoreCase))
            {
                Span<byte> current = session.GetWritableLogicalChunk(name).Span;
                for (int offset = 0; offset + 196 <= current.Length; offset += 196)
                    if (capturedBuildingIds.Contains(BinaryPrimitives.ReadInt16LittleEndian(current[(offset + 4)..])) ||
                        capturedBuildingIds.Contains(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 4))))
                        data.AsSpan(offset, 196).CopyTo(current.Slice(offset, 196));
                continue;
            }
            if (!name.Equals("buildings", StringComparison.OrdinalIgnoreCase))
            {
                session.ReplaceLogicalChunk(name, data);
                continue;
            }
            Span<byte> currentBuildings = session.GetWritableLogicalChunk(name).Span;
            foreach (int id in capturedBuildingIds)
                data.AsSpan(id * BuildingRecordSize, BuildingRecordSize).CopyTo(
                    currentBuildings.Slice(id * BuildingRecordSize, BuildingRecordSize));
        }
        int restoredRefund = refund;
        capture = null;
        refund = 0;
        ticksRemaining = 0;
        committedDemolition = false;
        capturedBuildingIds.Clear();
        return new LegacyConstructionUndoResult(true, restoredRefund, changed);
    }

    private bool CleanupDemolishedBuildings(HashSet<int> changed)
    {
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        Span<byte> buildingGrid = session.GetWritableLogicalChunk("building_grid").Span;
        Span<byte> terrain = session.GetWritableLogicalChunk("terrain_grid").Span;
        Span<byte> images = session.GetWritableLogicalChunk("image_grid").Span;
        Span<byte> bitfields = session.GetWritableLogicalChunk("bitfields_grid").Span;
        Span<byte> edge = session.GetWritableLogicalChunk("edge_grid").Span;
        Span<byte> aqueduct = session.GetWritableLogicalChunk("aqueduct_grid").Span;
        Span<byte> sprites = session.GetWritableLogicalChunk("sprite_grid").Span;
        HashSet<int> removed = [];
        for (int id = 1; id < MaximumBuildings; id++)
        {
            int recordOffset = id * BuildingRecordSize;
            if (buildings[recordOffset] == 6)
                removed.Add(id);
        }
        if (removed.Count == 0)
            return false;

        for (int tile = 0; tile < LegacyCityMapSnapshot.TileCount; tile++)
        {
            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.Slice(tile * sizeof(short), sizeof(short)));
            if (!removed.Contains(id))
                continue;
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.Slice(tile * sizeof(uint), sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                terrain.Slice(tile * sizeof(uint)), flags & ClearedBuildingTerrainMask);
            BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.Slice(tile * sizeof(short)), 0);
            BinaryPrimitives.WriteInt32LittleEndian(images.Slice(tile * sizeof(int)), 0);
            aqueduct[tile] = 0;
            sprites[tile] = 0;
            bitfields[tile] &= 0xe0;
            edge[tile] = (byte)((edge[tile] & 0xc0) | 0x40);
            changed.Add(tile);
        }
        foreach (int id in removed)
        {
            int recordOffset = id * BuildingRecordSize;
            buildings[recordOffset + DemolitionMarkerOffset] = 0;
            buildings.Slice(recordOffset, BuildingRecordSize).Clear();
        }
        return true;
    }

    private IReadOnlyList<int> FindChangedTiles(IReadOnlyDictionary<string, byte[]> snapshot)
    {
        ReadOnlySpan<byte> oldTerrain = snapshot["terrain_grid"];
        ReadOnlySpan<byte> oldBuildings = snapshot["building_grid"];
        ReadOnlySpan<byte> oldImages = snapshot["image_grid"];
        ReadOnlySpan<byte> terrain = session.LogicalChunks["terrain_grid"].Span;
        ReadOnlySpan<byte> buildings = session.LogicalChunks["building_grid"].Span;
        ReadOnlySpan<byte> images = session.LogicalChunks["image_grid"].Span;
        List<int> changed = [];
        for (int tile = 0; tile < LegacyCityMapSnapshot.TileCount; tile++)
        {
            if (!oldTerrain.Slice(tile * 4, 4).SequenceEqual(terrain.Slice(tile * 4, 4)) ||
                !oldBuildings.Slice(tile * 2, 2).SequenceEqual(buildings.Slice(tile * 2, 2)) ||
                !oldImages.Slice(tile * 4, 4).SequenceEqual(images.Slice(tile * 4, 4)))
                changed.Add(tile);
        }
        return changed;
    }

    private void CaptureChangedBuildingRecords()
    {
        capturedBuildingIds.Clear();
        ReadOnlySpan<byte> before = capture!["buildings"];
        ReadOnlySpan<byte> current = session.LogicalChunks["buildings"].Span;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * BuildingRecordSize;
            if (!before.Slice(offset, BuildingRecordSize)
                    .SequenceEqual(current.Slice(offset, BuildingRecordSize)))
                capturedBuildingIds.Add(id);
        }
    }

    private void EnsureCapture()
    {
        if (capture is null)
            throw new InvalidOperationException("Construction action was not captured before commit.");
    }
}
