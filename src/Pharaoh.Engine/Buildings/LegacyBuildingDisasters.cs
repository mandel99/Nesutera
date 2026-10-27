using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Population;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

/// <summary>Destruction owns the footprint, population and ruin records, not just a risk notification.</summary>
public static class LegacyBuildingDisasters
{
    public const int BurningRuinType = 99;
    public const int RubbleImage = 14957;
    public const int BurningImage = 14913;

    [OriginalAddress(0x00475B50, "FUN_00475b50")]
    public static int Root(ReadOnlySpan<byte> buildings, int id)
    {
        for (int n = 0; n < 525 && id > 0 && id < 4000; n++)
        {
            int previous = BinaryPrimitives.ReadInt16LittleEndian(buildings[(id * 264 + 56)..]);
            if (previous <= 0) return id;
            id = previous;
        }
        return 0;
    }

    [OriginalAddress(0x0046D100, "FUN_0046d100")]
    [OriginalAddress(0x004695D0, "FUN_004695d0")]
    [OriginalAddress(0x0047C2B0, "FUN_0047c2b0")]
    public static IReadOnlyList<int> Destroy(LegacyMissionSession session, int root, bool fire)
    {
        HashSet<int> changed = []; HashSet<int> linked = [];
        ReadOnlySpan<byte> original = session.LogicalChunks["buildings"].Span;
        linked.Add(root);
        foreach (int field in new[] { 56, 58 })
        {
            int id = root;
            for (int n = 0; n < 525 && id > 0 && id < 4000; n++)
            { id = BinaryPrimitives.ReadInt16LittleEndian(original[(id * 264 + field)..]); if (id <= 0 || id >= 4000 || !linked.Add(id)) break; }
        }
        foreach (int id in linked.Order()) DestroyOne(session, id, fire, changed);
        return changed.Order().ToArray();
    }

    [OriginalAddress(0x0046D100, "FUN_0046d100")]
    [OriginalAddress(0x0047C2B0, "FUN_0047c2b0")]
    private static void DestroyOne(LegacyMissionSession s, int id, bool fire, HashSet<int> changed)
    {
        if (id <= 0 || id >= 4000) return;
        Span<byte> buildings = s.GetWritableLogicalChunk("buildings").Span;
        Span<byte> b = buildings.Slice(id * 264, 264);
        if (b[0] != 1 || BinaryPrimitives.ReadInt16LittleEndian(b[16..]) == BurningRuinType) return;
        int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]), size = Math.Clamp((int)b[3], 1, 6);
        int population = b[5] != 0 ? Math.Max(0, (int)BinaryPrimitives.ReadInt16LittleEndian(b[28..])) : 0;
        if (fire) { RemovePopulation(s, population); ReleaseStorage(s, b); }
        Span<byte> grid = s.GetWritableLogicalChunk("building_grid").Span, terrain = s.GetWritableLogicalChunk("terrain_grid").Span;
        Span<byte> images = s.GetWritableLogicalChunk("image_grid").Span, bits = s.GetWritableLogicalChunk("bitfields_grid").Span, edge = s.GetWritableLogicalChunk("edge_grid").Span;
        List<int> cells = [];
        foreach (int cell in LegacySquareBuildingPlacement.Cells(tile, size))
        {
            if ((uint)cell >= LegacyCityMapSnapshot.TileCount || BinaryPrimitives.ReadInt16LittleEndian(grid[(cell * 2)..]) != id) continue;
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(cell * 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(terrain[(cell * 4)..], (flags & 4) != 0 ? 4 : (flags & 0x03052e80) | 0x1000);
            BinaryPrimitives.WriteInt16LittleEndian(grid[(cell * 2)..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(images[(cell * 4)..], RubbleImage + (s.LogicalChunks["random_grid"].Span[cell] & 7));
            bits[cell] &= 0xe0; edge[cell] = 0x40;
            s.GetWritableLogicalChunk("aqueduct_grid").Span[cell] = 0; s.GetWritableLogicalChunk("sprite_grid").Span[cell] = 0;
            cells.Add(cell); changed.Add(cell);
        }
        b[0] = 4;
        if (!fire) return;
        byte city = b[1], faction = b[2];
        int sourceX = BinaryPrimitives.ReadInt16LittleEndian(b[6..]), sourceY = BinaryPrimitives.ReadInt16LittleEndian(b[8..]);
        int sourceTile = tile;
        byte sourceRandom = b[77];
        b[5] = 0; BinaryPrimitives.WriteInt16LittleEndian(b[28..], 0);
        b[67] = 0; BinaryPrimitives.WriteInt16LittleEndian(b[32..], 0);
        // Ruins must not inherit live rewrite service/recruitment reservations.
        // Preserve unrelated bytes, including the unclassified byte69.
        b.Slice(42, 8).Clear();
        foreach (int cell in cells)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(cell * 4)..]); if ((flags & 4) != 0) continue;
            int ruin = cell == sourceTile ? id : FindFree(buildings);
            if (ruin == 0) continue;
            Span<byte> r = buildings.Slice(ruin * 264, 264);
            if (ruin != id) r.Clear();
            r[0] = 1; r[1] = city; r[2] = faction; r[3] = 1; r[5] = 0;
            BinaryPrimitives.WriteInt16LittleEndian(r[6..], (short)(sourceX + (cell - sourceTile) % 228));
            BinaryPrimitives.WriteInt16LittleEndian(r[8..], (short)(sourceY + (cell - sourceTile) / 228));
            BinaryPrimitives.WriteInt32LittleEndian(r[12..], cell); BinaryPrimitives.WriteInt16LittleEndian(r[16..], 99);
            r.Slice(42, 8).Clear(); r.Slice(70, 8).Clear();
            r[77] = ruin == id ? sourceRandom : s.LogicalChunks["random_grid"].Span[cell]; r[76] = 1; r[189] = 0;
            BinaryPrimitives.WriteInt16LittleEndian(r[74..], (short)((r[77] & 7) + 1));
            BinaryPrimitives.WriteInt16LittleEndian(grid[(cell * 2)..], (short)ruin);
            BinaryPrimitives.WriteUInt32LittleEndian(terrain[(cell * 4)..], (flags & 0x03052e80) | 8);
            BinaryPrimitives.WriteInt32LittleEndian(images[(cell * 4)..], GetBurningImage(s.LogicalChunks["random_grid"].Span[cell]));
        }
    }

    [OriginalAddress(0x00469730, "FUN_00469730")]
    public static void FinishBurning(LegacyMissionSession s, int id, HashSet<int> changed)
    {
        Span<byte> b = s.GetWritableLogicalChunk("buildings").Span.Slice(id * 264, 264);
        int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]); if ((uint)tile >= LegacyCityMapSnapshot.TileCount) return;
        b[0] = 4;
        Span<byte> terrain = s.GetWritableLogicalChunk("terrain_grid").Span;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * 4)..]);
        BinaryPrimitives.WriteUInt32LittleEndian(terrain[(tile * 4)..], (flags & 0x03052e80) | 0x1000);
        BinaryPrimitives.WriteInt16LittleEndian(s.GetWritableLogicalChunk("building_grid").Span[(tile * 2)..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(s.GetWritableLogicalChunk("image_grid").Span[(tile * 4)..], RubbleImage + (s.LogicalChunks["random_grid"].Span[tile] & 7));
        changed.Add(tile);
    }

    [OriginalAddress(0x004525F0, "FUN_004525f0")]
    public static void Cleanup(LegacyMissionSession s)
    {
        Span<byte> buildings = s.GetWritableLogicalChunk("buildings").Span;
        for (int id = 1; id < 4000; id++)
        {
            Span<byte> b = buildings.Slice(id * 264, 264); if (b[0] != 4) continue;
            if (b[5] != 0) RemovePopulation(s, Math.Max(0, (int)BinaryPrimitives.ReadInt16LittleEndian(b[28..])));
            ReleaseStorage(s, b); b.Clear();
        }
    }

    [OriginalAddress(0x0046D100, "FUN_0046d100")]
    public static int GetBurningImage(byte random) => BurningImage + (random & 3) * 9;

    [OriginalAddress(0x0046AC50, "FUN_0046ac50")]
    private static void ReleaseStorage(LegacyMissionSession s, Span<byte> building)
    {
        int id = building[193]; if (id == 0) return;
        Span<byte> pool = s.GetWritableLogicalChunk("building_storages").Span;
        if (id * 196 + 196 <= pool.Length) pool[id * 196 + 8] = 0;
        building[193] = 0;
    }

    [OriginalAddress(0x004E9170, "FUN_004e9170")]
    private static void RemovePopulation(LegacyMissionSession s, int count)
    {
        if (count == 0) return;
        LegacyPopulationAges.ApplyDelta(s, -count);
        Span<byte> city = s.GetWritableLogicalChunk("city_data").Span;
        int population = BinaryPrimitives.ReadInt32LittleEndian(city[18940..]);
        BinaryPrimitives.WriteInt32LittleEndian(city[18940..], Math.Max(0, population - count));
        int deaths = BinaryPrimitives.ReadInt32LittleEndian(city[28860..]);
        // The persistent disaster-death accumulator is city record EA3F54, logical28860.
        BinaryPrimitives.WriteInt32LittleEndian(city[28860..], checked(deaths + count));
    }

    [RewriteOnly("Bound the original fixed building-pool allocation without overwriting unrelated slots")]
    private static int FindFree(ReadOnlySpan<byte> buildings)
    { for (int id = 1; id < 4000; id++) if (buildings[id * 264] == 0) return id; return 0; }
}
