using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyWellPlacementResult(
    ReadOnlyMemory<byte> TerrainGrid,
    ReadOnlyMemory<byte> BuildingGrid,
    ReadOnlyMemory<byte> Buildings,
    ReadOnlyMemory<byte> ImageGrid,
    ReadOnlyMemory<byte> BitfieldsGrid,
    ReadOnlyMemory<byte> EdgeGrid,
    int BuildingId,
    int Tile);

/// <summary>Placement and persisted building state for Pharaoh's one-cell well.</summary>
public static class LegacyWellPlacement
{
    public const int BaseImageId = 13047;
    public const int AlternateImageId = 13049;
    public const uint GroundwaterTerrain = 0x00000080;

    private const uint TerrainBuilding = 0x00000008;
    private const uint PlacementBlockingMask = 0xeefed77f;
    private const uint BuildingTerrainPreserveMask = 0x03052e80;
    private const int RecordSize = 264;
    private const int MaximumBuildings = 4000;

    /// <summary>
    /// FUN_004555D0 selects base or base+2 from the signed random-map byte.
    /// Each base image owns its following animation overlay.
    /// </summary>
    [OriginalAddress(0x004555D0, "well sprite selection")]
    public static int GetImageId(byte random) =>
        unchecked((sbyte)random) < 30 ? BaseImageId : AlternateImageId;

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004E32E0, "groundwater placement warning")]
    [OriginalAddress(0x0043B630,"non-floodable bank-object exclusion")]
    public static bool CanPlace(LegacyMissionSession session, int tile)
    {
        ReadGeometry(session, out int width, out int height, out int origin);
        if (!LegacyPlayableMapArea.IsBuildable(tile, width, height, 3, origin)||LegacyTerrainAccess.IsBank(session.LogicalChunks,tile)||LegacyConstructionObstructions.HasWalker(session.LogicalChunks,tile))
            return false;
        ReadOnlySpan<byte> terrain = session.LogicalChunks["terrain_grid"].Span;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * sizeof(uint))..]);
        if ((flags & GroundwaterTerrain) == 0 || (flags & (PlacementBlockingMask|0x10000)) != 0)
            return false;
        return BinaryPrimitives.ReadInt16LittleEndian(
            session.LogicalChunks["building_grid"].Span[(tile * sizeof(short))..]) == 0;
    }

    [OriginalAddress(0x0046A400, "building allocation")]
    [OriginalAddress(0x00470990, "type 92 render dispatch")]
    [OriginalAddress(0x0047BF50, "one-cell building submission")]
    public static LegacyWellPlacementResult Commit(LegacyMissionSession session, int tile)
    {
        if (!CanPlace(session, tile))
            throw new InvalidOperationException("The selected tile cannot contain a well.");

        ReadGeometry(session, out int width, out int height, out int origin);
        byte[] terrain = session.LogicalChunks["terrain_grid"].ToArray();
        byte[] buildingGrid = session.LogicalChunks["building_grid"].ToArray();
        byte[] buildings = session.LogicalChunks["buildings"].ToArray();
        byte[] images = session.LogicalChunks["image_grid"].ToArray();
        byte[] bitfields = session.LogicalChunks["bitfields_grid"].ToArray();
        byte[] edge = session.LogicalChunks["edge_grid"].ToArray();
        ReadOnlySpan<byte> random = session.LogicalChunks["random_grid"].Span;

        int id = Enumerable.Range(1, MaximumBuildings - 1)
            .FirstOrDefault(candidate => buildings[candidate * RecordSize] == 0);
        if (id == 0)
            throw new InvalidOperationException("The legacy building pool is full.");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            terrain.AsSpan(tile * sizeof(uint)), (flags & BuildingTerrainPreserveMask) | TerrainBuilding);
        BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(tile * sizeof(short)), checked((short)id));
        BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)), GetImageId(random[tile]));
        bitfields[tile] &= 0xf0;
        edge[tile] = (byte)((edge[tile] & 0x80) | 0x40);

        int x = tile % LegacyCityMapSnapshot.MapLength - origin % LegacyCityMapSnapshot.MapLength;
        int y = tile / LegacyCityMapSnapshot.MapLength - origin / LegacyCityMapSnapshot.MapLength;
        Span<byte> record = buildings.AsSpan(id * RecordSize, RecordSize);
        record.Clear();
        LegacyBuildingOwnership.Initialize(record, session.LogicalChunks);
        record[0] = 3;
        record[3] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(record[6..], checked((short)x));
        BinaryPrimitives.WriteInt16LittleEndian(record[8..], checked((short)y));
        BinaryPrimitives.WriteInt32LittleEndian(record[12..], tile);
        BinaryPrimitives.WriteInt16LittleEndian(record[16..], (short)BuildingType.Well);
        record[66] = 6;
        record[77] = random[tile];
        record[194] = 0x32;

        return new(terrain, buildingGrid, buildings, images, bitfields, edge, id, tile);
    }

    internal static void ReadGeometry(
        LegacyMissionSession session, out int width, out int height, out int origin)
    {
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
    }
}
