using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyWellAdvance(IReadOnlyList<int> ChangedTiles)
{
    public bool TerrainChanged => ChangedTiles.Count != 0;
}

/// <summary>Recomputes the well's 5x5 service area and house access flag.</summary>
public sealed class WellManager
{
    public const uint TerrainWellRange = 0x00002000;

    private const int RecordSize = 264;
    private const int MaximumBuildings = 4000;
    private const int WellAccessOffset = 63;
    private const int DispatcherPeriod = 36;
    private readonly LegacyMissionSession session;
    private long ticks;

    private WellManager(LegacyMissionSession session) => this.session = session;

    public static WellManager Create(LegacyMissionSession session)
    {
        WellManager manager = new(session);
        manager.Recalculate();
        return manager;
    }

    public LegacyWellAdvance Advance(long legacyTicks)
    {
        long previous = ticks;
        ticks += legacyTicks;
        if (legacyTicks == 0 || ticks / DispatcherPeriod == previous / DispatcherPeriod)
            return new LegacyWellAdvance([]);
        return new LegacyWellAdvance(Recalculate());
    }

    [OriginalAddress(0x00455540, "clears derived water-service terrain")]
    [OriginalAddress(0x004555D0, "FUN_004555d0")]
    [OriginalAddress(0x0047FC40, "sets building +63 in radius two")]
    [OriginalAddress(0x004824B0, "stamps terrain 0x2000 in radius two")]
    public IReadOnlyList<int> Recalculate()
    {
        byte[] originalTerrain = session.LogicalChunks["terrain_grid"].ToArray();
        byte[] terrain = (byte[])originalTerrain.Clone();
        byte[] originalBuildings = session.LogicalChunks["buildings"].ToArray();
        byte[] buildings = (byte[])originalBuildings.Clone();
        ReadOnlySpan<byte> buildingGrid = session.LogicalChunks["building_grid"].Span;
        LegacyWellPlacement.ReadGeometry(session, out int width, out int height, out int origin);
        HashSet<int> changedTiles = [];

        for (int tile = 0; tile < LegacyCityMapSnapshot.TileCount; tile++)
        {
            int offset = tile * sizeof(uint);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(offset));
            if ((flags & TerrainWellRange) == 0)
                continue;
            BinaryPrimitives.WriteUInt32LittleEndian(terrain.AsSpan(offset), flags & ~TerrainWellRange);
            changedTiles.Add(tile);
        }

        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * RecordSize;
            if (buildings[offset] == 0 || buildings[offset + WellAccessOffset] == 0)
                continue;
            buildings[offset + WellAccessOffset] = 0;
        }

        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * RecordSize;
            if (buildings[offset] is 0 or 5 ||
                (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16)) != BuildingType.Well)
                continue;

            int wellX = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 6));
            int wellY = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 8));
            for (int y = Math.Max(0, wellY - 2); y <= Math.Min(height - 1, wellY + 2); y++)
            for (int x = Math.Max(0, wellX - 2); x <= Math.Min(width - 1, wellX + 2); x++)
            {
                int tile = origin + x + y * LegacyCityMapSnapshot.MapLength;
                if ((uint)tile >= LegacyCityMapSnapshot.TileCount ||
                    !LegacyPlayableMapArea.Contains(tile, width, height, origin))
                    continue;
                int terrainOffset = tile * sizeof(uint);
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(terrainOffset));
                if ((flags & TerrainWellRange) == 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        terrain.AsSpan(terrainOffset), flags | TerrainWellRange);
                    changedTiles.Add(tile);
                }
                int targetId = BinaryPrimitives.ReadInt16LittleEndian(
                    buildingGrid[(tile * sizeof(short))..]);
                if (targetId <= 0 || targetId >= MaximumBuildings)
                    continue;
                int accessOffset = targetId * RecordSize + WellAccessOffset;
                if (buildings[accessOffset] != 0)
                    continue;
                buildings[accessOffset] = 1;
            }
        }

        changedTiles.RemoveWhere(tile =>
            BinaryPrimitives.ReadUInt32LittleEndian(
                originalTerrain.AsSpan(tile * sizeof(uint))) ==
            BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.AsSpan(tile * sizeof(uint))));
        if (changedTiles.Count != 0)
            session.ReplaceLogicalChunk("terrain_grid", terrain);
        if (!originalBuildings.AsSpan().SequenceEqual(buildings))
            session.ReplaceLogicalChunk("buildings", buildings);
        return [.. changedTiles];
    }
}
