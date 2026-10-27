using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public enum LegacyClearLandBlockReason
{
    None,
    ProtectedStructure,
    Monument,
    MilitaryStructure,
    WaterStructure
}

public readonly record struct LegacyClearLandResult(
    ReadOnlyMemory<byte> TerrainGrid,
    ReadOnlyMemory<byte> BuildingGrid,
    ReadOnlyMemory<byte> Buildings,
    ReadOnlyMemory<byte> ImageGrid,
    ReadOnlyMemory<byte> BitfieldsGrid,
    ReadOnlyMemory<byte> EdgeGrid,
    ReadOnlyMemory<byte> AqueductGrid,
    ReadOnlyMemory<byte> SpriteGrid,
    IReadOnlyList<int> ChangedTiles,
    IReadOnlyList<int> PendingBuildingIds,
    int ClearedItemCount,
    LegacyClearLandBlockReason BlockReason);

public readonly record struct LegacyClearLandTerrainPreview(ReadOnlyMemory<byte> TerrainGrid,IReadOnlyList<int> ChangedTiles);

/// <summary>
/// Reconstructs Pharaoh's type-9 clear-land rectangle. The executable first
/// scans the complete selection for protected structures, then clears every
/// eligible terrain cell and each eligible building at most once.
/// </summary>
public sealed class LegacyClearLandPlacement
{
    public const BuildingType ClearLandType = BuildingType.ClearLand;

    private const int BuildingRecordSize = 264;
    private const int MaximumBuildings = 4000;
    private const int DemolitionMarkerOffset = 191;
    private const int FirstLinkedBuildingOffset = 56;
    private const int SecondLinkedBuildingOffset = 58;
    private const uint TerrainWater = 0x00000004;
    private const uint TerrainBuilding = 0x00000008;
    private const uint TerrainCanal = 0x00000100;
    private const uint ClearableTerrainMask = 0xeefed77f;
    private const uint NonClearableTerrainMask = 0x02040202;
    private const uint TerrainPreserveMask = 0x03052e80;
    private const uint ClearedBuildingTerrainMask = 0x03052e84;
    private const uint SpecialBuildingBlock = 0x40000000;

    private static readonly HashSet<int> ProtectedTypes =
    [
        0xb7, 0xde, 0xd5, 0xcf, 0xd0, 0xd2, 0xe6, 0xe1,
        0xe3, 0xe5, 0xea, 0xeb, 0xec, 0xe4
    ];

    private static readonly HashSet<int> MonumentTypes =
    [
        0x41, 0x42, 0x43, 0x44, 0x45, 0x61,
        0x9c, 0x9d, 0x9e, 0x9f, 0xa0
    ];

    private static readonly HashSet<int> MilitaryTypes = [0x36, 0x39];

    // FUN_00474700 silently leaves these special records in place. They are
    // distinct from the preflight categories above, which open a warning and
    // cancel the complete operation.
    private static readonly HashSet<int> SilentlySkippedTypes =
        [0x63, 0x58, 0x59, 0x5d, 0xda, 0xdb, 0xdc, 0xd7];

    private IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? chunks;
    private int width;
    private int height;
    private int origin;
    private int startTile;
    private int endTile;
    private int[] previewTiles = [];

    public bool IsActive => chunks is not null;
    public IReadOnlyList<int> PreviewTiles => previewTiles;
    public int PreviewEndpoint => IsActive
        ? endTile
        : LegacyMapOrientationIndex.InvalidTileIndex;

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    public bool Begin(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks,
        int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(logicalChunks);
        chunks = logicalChunks;
        ReadGeometry(logicalChunks);
        if (!TryToLocal(tileIndex, out _, out _))
        {
            Reset();
            return false;
        }

        startTile = endTile = tileIndex;
        previewTiles = [tileIndex];
        return true;
    }

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    public bool Update(int tileIndex)
    {
        if (!IsActive || !TryToLocal(tileIndex, out _, out _) || tileIndex == endTile)
            return false;
        endTile = tileIndex;
        previewTiles = BuildRectangle();
        return true;
    }

    [OriginalAddress(0x00474380, "FUN_00474380")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    public LegacyClearLandBlockReason GetBlockReason()
    {
        EnsureActive();
        ReadOnlySpan<byte> buildingGrid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).Span;
        ReadOnlySpan<byte> buildings = RequiredChunk(
            "buildings", MaximumBuildings * BuildingRecordSize).Span;
        ReadOnlySpan<byte> terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;

        bool protectedStructure = false;
        bool monument = false;
        bool military = false;
        bool waterStructure = false;

        foreach (int tile in previewTiles)
        {
            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.Slice(tile * sizeof(short), sizeof(short)));
            if (id > 0 && id < MaximumBuildings)
            {
                int type = BinaryPrimitives.ReadInt16LittleEndian(
                    buildings.Slice(id * BuildingRecordSize + 16, sizeof(short)));
                protectedStructure |= ProtectedTypes.Contains(type);
                monument |= MonumentTypes.Contains(type);
                military |= MilitaryTypes.Contains(type);
            }

            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.Slice(tile * sizeof(uint), sizeof(uint)));
            if (id == 0 && (flags & TerrainWater) != 0 &&
                (flags & TerrainCanal) == 0 && HasWaterStructure(tile))
                waterStructure = true;
        }
        if (protectedStructure)
            return LegacyClearLandBlockReason.ProtectedStructure;
        if (monument)
            return LegacyClearLandBlockReason.Monument;
        if (military)
            return LegacyClearLandBlockReason.MilitaryStructure;
        return waterStructure
            ? LegacyClearLandBlockReason.WaterStructure
            : LegacyClearLandBlockReason.None;
    }

    [OriginalAddress(0x00474700, "FUN_00474700")]
    public int GetPreviewItemCount()
    {
        EnsureActive();
        if (GetBlockReason() != LegacyClearLandBlockReason.None)
            return 0;
        ReadOnlySpan<byte> terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        ReadOnlySpan<byte> buildingGrid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).Span;
        ReadOnlySpan<byte> buildings = RequiredChunk(
            "buildings", MaximumBuildings * BuildingRecordSize).Span;
        HashSet<int> buildingIds = [];
        int count = 0;
        foreach (int tile in previewTiles)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.Slice(tile * sizeof(uint), sizeof(uint)));
            if ((flags & NonClearableTerrainMask) != 0)
                continue;
            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.Slice(tile * sizeof(short), sizeof(short)));
            if (id > 0 && id < MaximumBuildings)
            {
                int offset = id * BuildingRecordSize;
                int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.Slice(offset + 16, 2));
                if (buildings[offset] != 6 && !SilentlySkippedTypes.Contains(type))
                    buildingIds.Add(id);
                continue;
            }
            if ((flags & (TerrainBuilding | SpecialBuildingBlock)) != 0 ||
                ((flags & TerrainCanal) == 0 && (flags & TerrainWater) != 0) ||
                (flags & ClearableTerrainMask) == 0)
                continue;
            count++;
        }
        return count + buildingIds.Count;
    }

    [OriginalAddress(0x004743F0, "Clear-land drag preview")]
    [OriginalAddress(0x00474700, "Building demolition marker selection")]
    public IReadOnlyList<int> GetPreviewBuildingIds()
    {
        EnsureActive();
        if (GetBlockReason() != LegacyClearLandBlockReason.None)
            return [];
        ReadOnlySpan<byte> terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        ReadOnlySpan<byte> buildingGrid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).Span;
        ReadOnlySpan<byte> buildings = RequiredChunk(
            "buildings", MaximumBuildings * BuildingRecordSize).Span;
        HashSet<int> pending = [];
        foreach (int tile in previewTiles)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.Slice(tile * sizeof(uint), sizeof(uint)));
            if ((flags & NonClearableTerrainMask) != 0)
                continue;
            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.Slice(tile * sizeof(short), sizeof(short)));
            if (id <= 0 || id >= MaximumBuildings)
                continue;
            int recordOffset = id * BuildingRecordSize;
            int type = BinaryPrimitives.ReadInt16LittleEndian(
                buildings.Slice(recordOffset + 16, sizeof(short)));
            if (buildings[recordOffset] == 6 || SilentlySkippedTypes.Contains(type) ||
                !pending.Add(id))
                continue;
            AddLinkedBuildings(id, FirstLinkedBuildingOffset, buildings, pending);
            AddLinkedBuildings(id, SecondLinkedBuildingOffset, buildings, pending);
        }
        return pending.Order().ToArray();
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    [OriginalAddress(0x00474700, "FUN_00474700")]
    public LegacyClearLandResult Commit() => BuildResult(preview: false);

    [OriginalAddress(0x00474700, "FUN_00474700")]
    public LegacyClearLandResult Preview() => BuildResult(preview: true);

    [OriginalAddress(0x00474700,"Terrain-only part of the clear-land preview before building removal is confirmed")]
    public LegacyClearLandTerrainPreview PreviewTerrain()
    {
        EnsureActive();
        var original=RequiredChunk("terrain_grid",LegacyCityMapSnapshot.ImageGridLength);
        if(GetBlockReason()!=LegacyClearLandBlockReason.None)return new(original,[]);
        byte[] terrain=original.ToArray();
        var grid=RequiredChunk("building_grid",LegacyCityMapSnapshot.TileCount*2).Span;
        List<int> changed=[];
        foreach(int tile in previewTiles)
        {
            uint flags=BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile*4));
            int buildingId=BinaryPrimitives.ReadInt16LittleEndian(grid[(tile*2)..]);
            if((flags&NonClearableTerrainMask)!=0||(buildingId>0&&buildingId<MaximumBuildings)||
                (flags&(TerrainBuilding|SpecialBuildingBlock))!=0||((flags&TerrainCanal)==0&&(flags&TerrainWater)!=0)||
                (flags&ClearableTerrainMask)==0)continue;
            uint cleared=flags&TerrainPreserveMask;
            if(flags==cleared)continue;
            BinaryPrimitives.WriteUInt32LittleEndian(terrain.AsSpan(tile*4),cleared);changed.Add(tile);
        }
        return new(terrain,changed);
    }

    [OriginalAddress(0x00474700, "FUN_00474700")]
    private LegacyClearLandResult BuildResult(bool preview)
    {
        EnsureActive();
        LegacyClearLandBlockReason blocked = GetBlockReason();
        byte[] terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        byte[] buildingGrid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).ToArray();
        byte[] buildings = RequiredChunk(
            "buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        byte[] images = RequiredChunk(
            "image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        byte[] bitfields = RequiredChunk(
            "bitfields_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        byte[] edge = RequiredChunk(
            "edge_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        byte[] aqueduct = RequiredChunk(
            "aqueduct_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        byte[] sprites = RequiredChunk(
            "sprite_grid", LegacyCityMapSnapshot.TileCount).ToArray();

        if (blocked != LegacyClearLandBlockReason.None)
        {
            if (!preview) Reset();
            return new LegacyClearLandResult(
                terrain, buildingGrid, buildings, images, bitfields, edge, aqueduct, sprites,
                [], [], 0, blocked);
        }

        HashSet<int> changed = [];
        HashSet<int> buildingsToRemove = [];
        int cleared = 0;
        foreach (int tile in previewTiles)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                terrain.AsSpan(tile * sizeof(uint), sizeof(uint)));
            if ((flags & NonClearableTerrainMask) != 0)
                continue;

            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.AsSpan(tile * sizeof(short), sizeof(short)));
            if (id > 0 && id < MaximumBuildings)
            {
                int offset = id * BuildingRecordSize;
                int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
                if (buildings[offset] != 6 && !SilentlySkippedTypes.Contains(type))
                    buildingsToRemove.Add(id);
                continue;
            }

            if ((flags & (TerrainBuilding | SpecialBuildingBlock)) != 0)
                continue;
            if ((flags & TerrainCanal) == 0 && (flags & TerrainWater) != 0)
                continue;
            if ((flags & ClearableTerrainMask) == 0)
                continue;

            BinaryPrimitives.WriteUInt32LittleEndian(
                terrain.AsSpan(tile * sizeof(uint)), flags & TerrainPreserveMask);
            BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)), 0);
            aqueduct[tile] = 0;
            sprites[tile] = 0;
            bitfields[tile] &= preview ? (byte)0xf0 : (byte)0x70;
            edge[tile] |= 0x40;
            changed.Add(tile);
            cleared++;
        }

        HashSet<int> pendingBuildings = [];
        int clearedBuildings = 0;
        foreach (int id in buildingsToRemove)
        {
            if (!pendingBuildings.Add(id))
                continue;
            clearedBuildings++;
            AddLinkedBuildings(id, FirstLinkedBuildingOffset, buildings, pendingBuildings);
            AddLinkedBuildings(id, SecondLinkedBuildingOffset, buildings, pendingBuildings);
        }
        foreach (int id in pendingBuildings)
        {
            int recordOffset = id * BuildingRecordSize;
            buildings[recordOffset] = 6;
            buildings[recordOffset + DemolitionMarkerOffset] = 1;
            for (int tile = 0; tile < LegacyCityMapSnapshot.TileCount; tile++)
            {
                if (BinaryPrimitives.ReadInt16LittleEndian(
                        buildingGrid.AsSpan(tile * sizeof(short), sizeof(short))) == id)
                    changed.Add(tile);
            }
        }
        cleared += clearedBuildings;

        if (!preview) Reset();
        return new LegacyClearLandResult(
            terrain, buildingGrid, buildings, images, bitfields, edge, aqueduct, sprites,
            changed.Order().ToArray(), pendingBuildings.Order().ToArray(), cleared,
            LegacyClearLandBlockReason.None);
    }

    public void Reset()
    {
        chunks = null;
        previewTiles = [];
        width = height = origin = startTile = endTile = 0;
    }

    [OriginalAddress(0x00474700, "Linked demolition records at building offsets 0x38 and 0x3a")]
    private static void AddLinkedBuildings(
        int rootId,
        int linkOffset,
        ReadOnlySpan<byte> buildings,
        HashSet<int> pending)
    {
        int id = rootId;
        for (int count = 0; count < 0x20d; count++)
        {
            id = BinaryPrimitives.ReadInt16LittleEndian(
                buildings.Slice(id * BuildingRecordSize + linkOffset, sizeof(short)));
            if (id <= 0 || id >= MaximumBuildings || !pending.Add(id))
                return;
        }
    }

    private bool HasWaterStructure(int tile)
    {
        ReadOnlySpan<byte> sprites = RequiredChunk(
            "sprite_grid", LegacyCityMapSnapshot.TileCount).Span;
        return sprites[tile] != 0;
    }

    private int[] BuildRectangle()
    {
        TryToLocal(startTile, out int startX, out int startY);
        TryToLocal(endTile, out int endX, out int endY);
        int minX = Math.Min(startX, endX);
        int maxX = Math.Max(startX, endX);
        int minY = Math.Min(startY, endY);
        int maxY = Math.Max(startY, endY);
        List<int> result = new((maxX - minX + 1) * (maxY - minY + 1));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            result.Add(origin + y * LegacyCityMapSnapshot.MapLength + x);
        return result.ToArray();
    }

    private void ReadGeometry(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks)
    {
        if (!logicalChunks.TryGetValue("scenario_info", out ReadOnlyMemory<byte> scenario) ||
            scenario.Length < 60)
            throw new InvalidDataException("Logical chunk 'scenario_info' is missing map geometry.");
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario.Span[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario.Span[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario.Span[52..]);
        int rowPadding = BinaryPrimitives.ReadInt32LittleEndian(scenario.Span[56..]);
        if (width <= 0 || height <= 0 || width > LegacyCityMapSnapshot.MapLength ||
            height > LegacyCityMapSnapshot.MapLength ||
            rowPadding != LegacyCityMapSnapshot.MapLength - width || origin < 0 ||
            origin + (height - 1) * LegacyCityMapSnapshot.MapLength + width >
                LegacyCityMapSnapshot.TileCount)
            throw new InvalidDataException("The scenario contains invalid legacy map geometry.");
    }

    private bool TryToLocal(int tileIndex, out int x, out int y)
    {
        int relative = tileIndex - origin;
        x = relative % LegacyCityMapSnapshot.MapLength;
        y = relative / LegacyCityMapSnapshot.MapLength;
        return relative >= 0 && x >= 0 && x < width && y >= 0 && y < height;
    }

    private ReadOnlyMemory<byte> RequiredChunk(string name, int length)
    {
        EnsureActive();
        if (!chunks!.TryGetValue(name, out ReadOnlyMemory<byte> memory) || memory.Length != length)
            throw new InvalidDataException($"Logical chunk '{name}' has an invalid length.");
        return memory;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Clear-land placement is not active.");
    }
}
