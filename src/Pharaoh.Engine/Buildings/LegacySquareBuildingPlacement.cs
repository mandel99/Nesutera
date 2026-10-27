using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacySquareBuildingDefinition(BuildingType Type, int Size, int ImageId, int Employees);

/// <summary>Verified ordinary square buildings sharing the executable's cleared-land placement path.</summary>
public static class LegacySquareBuildingPlacement
{
    [OriginalAddress(0x00470990, "verified ordinary square building image dispatch")]
    [OriginalAddress(0x0046A400, "footprint size table at DAT_005D06DC")]
    public static bool TryGetDefinition(BuildingType type, out LegacySquareBuildingDefinition definition)
    {
        definition = type switch
        {
            BuildingType.HuntingLodge => new(type, 2, 12757, 6),
            BuildingType.Granary => new(type, 4, 13991, 12),
            BuildingType.Bazaar => new(type, 2, 12523, 5),
            BuildingType.SenetHouse => new(type, 4, 12807, 25),
            BuildingType.Conservatory => new(type, 3, 12777, 8),
            BuildingType.DanceSchool => new(type, 4, 12776, 10),
            BuildingType.JugglerSchool => new(type, 2, 12778, 5),
            BuildingType.Apothecary => new(type, 1, 12967, 5),
            BuildingType.Mortuary => new(type, 2, 12993, 8),
            BuildingType.Dentist => new(type, 1, 12980, 2),
            BuildingType.ScribalSchool => new(type, 2, 12912, 10),
            BuildingType.Library => new(type, 3, 12893, 30),
            BuildingType.Brewery => new(type, 2, 12721, 12),
            BuildingType.Weaver => new(type, 2, 12652, 12),
            BuildingType.Bowyer => new(type, 2, 12734, 12),
            BuildingType.Jeweler => new(type, 2, 12621, 12),
            BuildingType.Potter => new(type, 2, 12665, 12),
            BuildingType.CarpenterGuild => new(type, 2, 12562, 8),
            BuildingType.BricklayersGuild => new(type, 2, 12593, 10),
            BuildingType.StonemasonGuild => new(type, 2, 12577, 12),
            BuildingType.Courthouse => new(type, 3, 12880, 10),
            BuildingType.PapyrusMaker => new(type, 2, 12685, 12),
            BuildingType.Brickworks => new(type, 2, 12641, 12),
            BuildingType.ChariotMaker => new(type, 4, 12608, 30),
            BuildingType.Physician => new(type, 2, 13006, 8),
            BuildingType.Zoo => new(type, 6, 23887, 30),
            BuildingType.LampMaker => new(type, 2, 23740, 12),
            _ => default
        };
        return definition.Size != 0;
    }

    [OriginalAddress(0x0046FEF0, "square building footprint validation")]
    public static bool CanPlace(LegacyMissionSession session, LegacySquareBuildingDefinition definition, int tile)
    {
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        int width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        int origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        int relative = tile - origin;
        if (relative < 0 || definition.Size == 0) return false;
        int x = relative % LegacyCityMapSnapshot.MapLength, y = relative / LegacyCityMapSnapshot.MapLength;
        if (x + definition.Size > width || y + definition.Size > height) return false;
        foreach (int cell in Cells(tile, definition.Size))
            if (!CanPlaceCell(session, cell))
                return false;
        return true;
    }

    [OriginalAddress(0x0043B630, "ordinary building cells reject terrain collisions, floodplain and occupancy")]
    [OriginalAddress(0x0046FEF0, "same land eligibility at commit")]
    public static bool CanPlaceCell(LegacyMissionSession session, int tile)
    {
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        int width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        int origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        if ((uint)tile >= LegacyCityMapSnapshot.TileCount ||
            !LegacyPlayableMapArea.IsBuildable(tile,width,height,3,origin) ||
            LegacyTerrainAccess.IsBank(session.LogicalChunks,tile) || LegacyConstructionObstructions.HasWalker(session.LogicalChunks,tile)) return false;
        ReadOnlySpan<byte> terrain = session.LogicalChunks["terrain_grid"].Span;
        ReadOnlySpan<byte> grid = session.LogicalChunks["building_grid"].Span;
        return (BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile*4)..]) & 0xeeffd77f) == 0 &&
            BinaryPrimitives.ReadInt16LittleEndian(grid[(tile*2)..]) == 0;
    }

    [OriginalAddress(0x0046A400, "allocate building and dimensions from DAT_005D06DC")]
    [OriginalAddress(0x00470990, "hunting lodge, granary and bazaar image selection")]
    [OriginalAddress(0x0047BF50, "write square footprint, images and draw anchor")]
    [OriginalAddress(0x0046B100, "granary storage allocation and capacity defaults")]
    public static int Commit(LegacyMissionSession session, LegacySquareBuildingDefinition definition, int tile, int orientation = 0)
    {
        if (!TryGetDefinition(definition.Type,out var verified)||verified!=definition||!CanPlace(session, definition, tile))
            throw new InvalidOperationException("Invalid square-building footprint or unverified definition.");
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        int id = 1;
        while (id < 4000 && buildings[id * 264] != 0) id++;
        if (id == 4000) throw new InvalidOperationException("The legacy building pool is full.");
        int storageId = 0;
        if (definition.Type is BuildingType.Granary or BuildingType.Bazaar)
        {
            ReadOnlySpan<byte> storagePool = session.LogicalChunks["building_storages"].Span;
            storageId = 1;
            while (storageId * 196 + 196 <= storagePool.Length && storagePool[storageId * 196 + 8] != 0) storageId++;
            if (storageId * 196 + 196 > storagePool.Length) throw new InvalidOperationException("The legacy storage pool is full.");
        }
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        int origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        int x = (tile - origin) % LegacyCityMapSnapshot.MapLength, y = (tile - origin) / LegacyCityMapSnapshot.MapLength;
        Span<byte> record = buildings.Slice(id * 264, 264);
        record.Clear();
        LegacyBuildingOwnership.Initialize(record, session.LogicalChunks);
        record[0] = 3; record[3] = checked((byte)definition.Size); record[194] = 0x32;
        BinaryPrimitives.WriteInt16LittleEndian(record[6..], checked((short)x));
        BinaryPrimitives.WriteInt16LittleEndian(record[8..], checked((short)y));
        BinaryPrimitives.WriteInt32LittleEndian(record[12..], tile);
        BinaryPrimitives.WriteInt16LittleEndian(record[16..], (short)definition.Type);
        byte random=session.LogicalChunks["random_grid"].Span[tile];
        record[77] = (byte)(random&0x7f); record[52]=(byte)(random&6);
        InitializeTypeFields(record,definition.Type);
        if (definition.Type == BuildingType.Bazaar)
        {
            Span<byte> storage = session.GetWritableLogicalChunk("building_storages").Span.Slice(storageId * 196, 196);
            storage.Clear(); storage[8] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(storage[4..], checked((short)id));
            record[193] = checked((byte)storageId);
        }
        if (definition.Type == BuildingType.Granary)
        {
            BinaryPrimitives.WriteInt16LittleEndian(record[86..], 3200);
            Span<byte> storages = session.GetWritableLogicalChunk("building_storages").Span;
            Span<byte> storage = storages.Slice(storageId * 196, 196);
            storage.Clear(); storage[8] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(storage[4..], checked((short)id));
            for (int resource = 0; resource < 36; resource++)
            {
                storage[10 + resource] = 1;
                BinaryPrimitives.WriteInt16LittleEndian(storage[(52 + resource * 2)..], 3200);
                BinaryPrimitives.WriteInt16LittleEndian(storage[(124 + resource * 2)..], 3200);
            }
            foreach (int food in LegacyFoodAvailability.Read(session.LogicalChunks)) storage[10 + food] = 0;
            record[193] = checked((byte)storageId);
        }
        // +5 is the housing marker, not a generic physical-building marker.
        Span<byte> terrain = session.GetWritableLogicalChunk("terrain_grid").Span;
        Span<byte> grid = session.GetWritableLogicalChunk("building_grid").Span;
        Span<byte> images = session.GetWritableLogicalChunk("image_grid").Span;
        Span<byte> bits = session.GetWritableLogicalChunk("bitfields_grid").Span;
        Span<byte> edge = session.GetWritableLogicalChunk("edge_grid").Span;
        int size = definition.Size;
        int anchor = DrawAnchor(tile, size, orientation);
        foreach (int cell in Cells(tile, size))
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(cell * 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(terrain[(cell * 4)..], (flags & 0x03052e80) | 8);
            BinaryPrimitives.WriteInt16LittleEndian(grid[(cell * 2)..], checked((short)id));
            BinaryPrimitives.WriteInt32LittleEndian(images[(cell * 4)..], definition.ImageId);
            bits[cell] = (byte)((bits[cell] & 0xe0) | size - 1);
            edge[cell] = (byte)(((cell - tile) % 228) + ((cell - tile) / 228) * 8);
            if (cell == anchor) edge[cell] |= 0x40;
        }
        return id;
    }

    [OriginalAddress(0x0046A400,"Type-specific resource and state defaults in the shared building allocator")]
    private static void InitializeTypeFields(Span<byte> record,BuildingType type)
    {
        (int output,int state,int secondInput)=type switch
        {
            BuildingType.Brewery => (15,9,0),
            BuildingType.Weaver => (17,1,0),
            BuildingType.Bowyer => (10,17,0),
            BuildingType.Jeweler => (19,10,0),
            BuildingType.Potter => (13,5,0),
            BuildingType.PapyrusMaker => (23,11,0),
            BuildingType.Brickworks => (12,5,13),
            BuildingType.ChariotMaker => (28,4,0),
            BuildingType.LampMaker => (34,23,24),
            BuildingType.CarpenterGuild => (0,4,0),
            BuildingType.ScribalSchool or BuildingType.Library => (0,12,0),
            BuildingType.Mortuary => (0,16,0),
            BuildingType.SenetHouse => (0,19,0),
            BuildingType.Zoo => (0,22,13),
            _ => (0,0,0)
        };
        record[67]=(byte)output;
        BinaryPrimitives.WriteInt16LittleEndian(record[18..],(short)state);
        BinaryPrimitives.WriteInt16LittleEndian(record[206..],(short)secondInput);
    }

    [RewriteOnly("Enumerates the cells of a verified square footprint")]
    public static IEnumerable<int> Cells(int tile, int size)
    {
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) yield return tile + y * 228 + x;
    }

    [OriginalAddress(0x0047BF50, "orientation-dependent footprint draw corner")]
    public static int DrawAnchor(int tile, int size, int orientation) => tile +
        (orientation is 0 or 2 ? 0 : size - 1) +
        (orientation is 2 or 4 ? 0 : size - 1) * 228;
}
