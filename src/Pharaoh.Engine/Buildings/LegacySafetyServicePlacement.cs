using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacySafetyServiceDefinition(
    BuildingType Type,
    int ImageId,
    int RequiredWorkers,
    WalkerType ServiceWalkerType);

public readonly record struct LegacySafetyServicePlacementResult(
    ReadOnlyMemory<byte> TerrainGrid,
    ReadOnlyMemory<byte> BuildingGrid,
    ReadOnlyMemory<byte> Buildings,
    ReadOnlyMemory<byte> ImageGrid,
    ReadOnlyMemory<byte> BitfieldsGrid,
    ReadOnlyMemory<byte> EdgeGrid,
    int BuildingId,
    int Tile);

/// <summary>The executable's shared one-cell placement path for city safety services.</summary>
public static class LegacySafetyServicePlacement
{
    public static readonly LegacySafetyServiceDefinition Firehouse =
        new(BuildingType.Firehouse, 12941, 6, WalkerType.Fireman);
    public static readonly LegacySafetyServiceDefinition ArchitectPost =
        new(BuildingType.ArchitectPost, 12954, 5, WalkerType.Architect);
    public static readonly LegacySafetyServiceDefinition PoliceStation =
        new(BuildingType.PoliceStation, 12928, 6, WalkerType.Policeman);

    private const uint TerrainBuilding = 0x00000008;
    private const uint PlacementBlockingMask = 0xeefed77f;
    private const uint BuildingTerrainPreserveMask = 0x03052e80;
    private const int RecordSize = 264;
    private const int MaximumBuildings = 4000;

    public static bool TryGetDefinition(BuildingType type, out LegacySafetyServiceDefinition definition)
    {
        definition = type switch
        {
            BuildingType.Firehouse => Firehouse,
            BuildingType.ArchitectPost => ArchitectPost,
            BuildingType.PoliceStation => PoliceStation,
            _ => default
        };
        return definition.Type != BuildingType.None;
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x0043B630,"non-floodable bank-object exclusion")]
    public static bool CanPlace(LegacyMissionSession session, int tile)
    {
        ReadGeometry(session, out int width, out int height, out int origin);
        if (!LegacyPlayableMapArea.IsBuildable(tile, width, height, 3, origin)||LegacyTerrainAccess.IsBank(session.LogicalChunks,tile)||LegacyConstructionObstructions.HasWalker(session.LogicalChunks,tile))
            return false;
        ReadOnlySpan<byte> terrain = session.LogicalChunks["terrain_grid"].Span;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * sizeof(uint))..]);
        if ((flags & (PlacementBlockingMask|0x10000)) != 0)
            return false;
        return BinaryPrimitives.ReadInt16LittleEndian(
            session.LogicalChunks["building_grid"].Span[(tile * sizeof(short))..]) == 0;
    }

    [OriginalAddress(0x0046A400, "FUN_0046a400")]
    [OriginalAddress(0x00470990, "FUN_00470990")]
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    public static LegacySafetyServicePlacementResult Commit(
        LegacyMissionSession session,
        LegacySafetyServiceDefinition definition,
        int tile)
    {
        if (!CanPlace(session, tile))
            throw new InvalidOperationException("The selected tile cannot contain this building.");
        ReadGeometry(session, out int width, out int height, out int origin);
        byte[] terrain = session.LogicalChunks["terrain_grid"].ToArray();
        byte[] buildingGrid = session.LogicalChunks["building_grid"].ToArray();
        byte[] buildings = session.LogicalChunks["buildings"].ToArray();
        byte[] images = session.LogicalChunks["image_grid"].ToArray();
        byte[] bitfields = session.LogicalChunks["bitfields_grid"].ToArray();
        byte[] edge = session.LogicalChunks["edge_grid"].ToArray();

        int id = Enumerable.Range(1, MaximumBuildings - 1)
            .FirstOrDefault(candidate => buildings[candidate * RecordSize] == 0);
        if (id == 0)
            throw new InvalidOperationException("The legacy building pool is full.");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            terrain.AsSpan(tile * sizeof(uint)), (flags & BuildingTerrainPreserveMask) | TerrainBuilding);
        BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(tile * sizeof(short)), checked((short)id));
        BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)), definition.ImageId);
        bitfields[tile] &= 0xe0;
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
        BinaryPrimitives.WriteInt16LittleEndian(record[16..], (short)definition.Type);
        record[66] = 6;
        if (LegacyBuildingRoadAccess.TryFindNearbyRoad(
                terrain, width, height, origin, tile, 1, out int roadTile))
        {
            int roadX = roadTile % LegacyCityMapSnapshot.MapLength -
                origin % LegacyCityMapSnapshot.MapLength;
            int roadY = roadTile / LegacyCityMapSnapshot.MapLength -
                origin / LegacyCityMapSnapshot.MapLength;
            BinaryPrimitives.WriteInt16LittleEndian(record[38..], checked((short)roadX));
            BinaryPrimitives.WriteInt16LittleEndian(record[40..], checked((short)roadY));
            record[68] = 1;
        }
        record[77] = session.LogicalChunks["random_grid"].Span[tile];
        record[194] = 0x32;

        return new(terrain, buildingGrid, buildings, images, bitfields, edge, id, tile);
    }

    private static void ReadGeometry(
        LegacyMissionSession session, out int width, out int height, out int origin)
    {
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
    }
}
