using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyHousingPlacementResult(
    ReadOnlyMemory<byte> TerrainGrid,
    ReadOnlyMemory<byte> BuildingGrid,
    ReadOnlyMemory<byte> Buildings,
    ReadOnlyMemory<byte> ImageGrid,
    ReadOnlyMemory<byte> BitfieldsGrid,
    ReadOnlyMemory<byte> EdgeGrid,
    IReadOnlyList<int> PlacedTiles,
    bool HasPlotsWithoutRoadAccess);

/// <summary>
/// Reconstructs the type-10 vacant-housing tool. Unlike roads, Pharaoh fills
/// the axis-aligned rectangle between the pressed and current cells and skips
/// invalid cells independently.
/// </summary>
public sealed class LegacyHousingPlacement
{
    public const BuildingType HousingType = BuildingType.Housing;
    public const int ForSaleImageId = 12499;

    private const uint TerrainBuilding = 0x00000008;
    private const uint TerrainFloodplain = 0x00010000;
    private const uint PlacementBlockingMask = 0xeefed77f;
    private const uint BuildingTerrainPreserveMask = 0x03052e80;
    private const int BuildingRecordSize = 264;
    private const int MaximumBuildings = 4000;

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

    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    [OriginalAddress(0x00474130, "FUN_00474130")]
    public static bool CanBeginAt(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks,
        int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(logicalChunks);
        LegacyHousingPlacement probe = new() { chunks = logicalChunks };
        probe.ReadGeometry(logicalChunks);
        return probe.CanPlace(tileIndex);
    }

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    public bool Begin(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks,
        int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(logicalChunks);
        chunks = logicalChunks;
        ReadGeometry(logicalChunks);
        if (!CanPlace(tileIndex))
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
    [OriginalAddress(0x00474130, "FUN_00474130")]
    public bool Update(int tileIndex)
    {
        if (!IsActive || !TryToLocal(tileIndex, out _, out _) || tileIndex == endTile)
            return false;

        endTile = tileIndex;
        previewTiles = BuildValidRectangle();
        return true;
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    [OriginalAddress(0x00474130, "FUN_00474130")]
    [OriginalAddress(0x0046A400, "FUN_0046a400")]
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    public LegacyHousingPlacementResult Commit()
    {
        EnsureActive();
        byte[] terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        byte[] buildingGrid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).ToArray();
        byte[] buildings = RequiredChunk(
            "buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        byte[] imageGrid = RequiredChunk(
            "image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        byte[] bitfields = RequiredChunk(
            "bitfields_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        byte[] edge = RequiredChunk(
            "edge_grid", LegacyCityMapSnapshot.TileCount).ToArray();

        List<int> placed = [];
        bool hasPlotsWithoutRoadAccess = false;
        int nextId = 1;
        foreach (int tile in previewTiles)
        {
            if (!CanPlace(tile)) continue;
            while (nextId < MaximumBuildings && buildings[nextId * BuildingRecordSize] != 0)
                nextId++;
            if (nextId >= MaximumBuildings)
                break;

            hasPlotsWithoutRoadAccess |= !LegacyBuildingRoadAccess.HasNearbyRoad(
                terrain, width, height, origin, tile, footprintSize: 1);

            Span<byte> terrainCell = terrain.AsSpan(tile * sizeof(uint), sizeof(uint));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrainCell);
            BinaryPrimitives.WriteUInt32LittleEndian(
                terrainCell, (flags & BuildingTerrainPreserveMask) | TerrainBuilding);
            BinaryPrimitives.WriteInt16LittleEndian(
                buildingGrid.AsSpan(tile * sizeof(short), sizeof(short)), checked((short)nextId));
            BinaryPrimitives.WriteInt32LittleEndian(
                imageGrid.AsSpan(tile * sizeof(int), sizeof(int)), ForSaleImageId);
            bitfields[tile] &= 0xe0;
            edge[tile] = (byte)((edge[tile] & 0x80) | 0x40);

            WriteBuildingRecord(buildings, nextId, tile);
            placed.Add(tile);
            nextId++;
        }

        Reset();
        return new LegacyHousingPlacementResult(
            terrain, buildingGrid, buildings, imageGrid, bitfields, edge, placed,
            hasPlotsWithoutRoadAccess);
    }

    public void Reset()
    {
        chunks = null;
        previewTiles = [];
        width = height = origin = startTile = endTile = 0;
    }

    private int[] BuildValidRectangle()
    {
        TryToLocal(startTile, out int startX, out int startY);
        TryToLocal(endTile, out int endX, out int endY);
        int minX = Math.Clamp(Math.Min(startX, endX), 0, width - 1);
        int maxX = Math.Clamp(Math.Max(startX, endX), 0, width - 1);
        int minY = Math.Clamp(Math.Min(startY, endY), 0, height - 1);
        int maxY = Math.Clamp(Math.Max(startY, endY), 0, height - 1);
        List<int> valid = [];
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int tile = origin + y * LegacyCityMapSnapshot.MapLength + x;
            if (CanPlace(tile))
                valid.Add(tile);
        }
        return valid.ToArray();
    }

    [OriginalAddress(0x0043B630,"ordinary housing cells also reject non-floodable bank objects")]
    [OriginalAddress(0x00474130,"per-cell housing placement eligibility")]
    private bool CanPlace(int tile)
    {
        if (!TryToLocal(tile, out _, out _) || LegacyConstructionObstructions.HasWalker(chunks!,tile))
            return false;
        uint terrain = ReadTerrain(tile);
        if ((terrain & TerrainFloodplain) != 0 || (terrain & PlacementBlockingMask) != 0||LegacyTerrainAccess.IsBank(chunks!,tile))
            return false;
        ReadOnlySpan<byte> grid = RequiredChunk(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).Span;
        return BinaryPrimitives.ReadInt16LittleEndian(
            grid.Slice(tile * sizeof(short), sizeof(short))) == 0;
    }

    private void WriteBuildingRecord(byte[] buildings, int id, int tile)
    {
        TryToLocal(tile, out int x, out int y);
        Span<byte> record = buildings.AsSpan(id * BuildingRecordSize, BuildingRecordSize);
        record.Clear();
        LegacyBuildingOwnership.Initialize(record, chunks!);
        record[0] = 3;
        record[3] = 1;
        record[5] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(record[6..], checked((short)x));
        BinaryPrimitives.WriteInt16LittleEndian(record[8..], checked((short)y));
        BinaryPrimitives.WriteInt32LittleEndian(record[12..], tile);
        BinaryPrimitives.WriteInt16LittleEndian(record[16..], (short)HousingType);
        BinaryPrimitives.WriteInt16LittleEndian(record[18..], 0);
        record[194] = 0x32;
    }

    private uint ReadTerrain(int tile)
    {
        ReadOnlySpan<byte> terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        return BinaryPrimitives.ReadUInt32LittleEndian(
            terrain.Slice(tile * sizeof(uint), sizeof(uint)));
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
            throw new InvalidOperationException("Housing placement is not active.");
    }
}
