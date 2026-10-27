using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Environment;

/// <summary>The original flood row nodes, including non-floodable bank helpers.</summary>
public sealed class LegacyFloodplainTopology
{
    private readonly int[] tileRows = Enumerable.Repeat(-1, LegacyCityMapSnapshot.TileCount).ToArray();
    private readonly bool[] floodable = new bool[LegacyCityMapSnapshot.TileCount];
    private readonly List<int>[] rows = Enumerable.Range(0, 30).Select(_ => new List<int>()).ToArray();

    public int Width { get; private set; }
    public int GetRow(int tile) => (uint)tile < tileRows.Length ? tileRows[tile] : -1;
    public bool IsFloodable(int tile) => (uint)tile < floodable.Length && floodable[tile];
    public bool IsBankHelper(int tile) => GetRow(tile) >= 0 && !IsFloodable(tile);
    public IReadOnlyList<int> GetNodes(int row) => rows[row];

    [OriginalAddress(0x004BE160, "FUN_004be160")]
    [OriginalAddress(0x004BD740, "FUN_004bd740")]
    public static LegacyFloodplainTopology Build(
        ReadOnlySpan<byte> terrainBytes, ReadOnlySpan<byte> random, int width, int height, int origin)
    {
        LegacyFloodplainTopology result = new();
        uint[] terrain = new uint[LegacyCityMapSnapshot.TileCount];
        for (int tile = 0; tile < terrain.Length; tile++)
            terrain[tile] = BinaryPrimitives.ReadUInt32LittleEndian(terrainBytes[(tile * sizeof(uint))..]);
        byte[] levels = new byte[terrain.Length];
        int[] offsets = [-228, -227, 1, 229, 228, 227, -1, -229];
        // This is the EXE's ordered scan, not a breadth-first distance field.
        // Newly assigned level L cannot propagate within the same scan L.
        for (int scanLevel = 30; ; scanLevel--)
        {
            bool added = false;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int tile = origin + y * 228 + x;
                if (levels[tile] != 0)
                    continue;
                uint flags = terrain[tile];
                int floodplainNeighbors = 0;
                int riverNeighbors = 0;
                foreach (int offset in offsets)
                {
                    int neighbor = tile + offset;
                    if ((uint)neighbor >= terrain.Length)
                        continue;
                    uint adjacent = terrain[neighbor];
                    if ((adjacent & 0x10000) != 0)
                        floodplainNeighbors++;
                    if ((adjacent & 4) != 0 && (adjacent & 0x90000) == 0)
                        riverNeighbors++;
                }
                if ((flags & 0x10000) == 0 && floodplainNeighbors == 0)
                    continue;
                if (riverNeighbors + floodplainNeighbors == 8 && (flags & 0x10004) == 4)
                    continue;
                int level = 30;
                if (riverNeighbors == 0)
                {
                    level = 0;
                    foreach (int offset in offsets)
                    {
                        int neighbor = tile + offset;
                        if ((uint)neighbor >= terrain.Length || (terrain[neighbor] & 0x10000) == 0)
                            continue;
                        int adjacentLevel = levels[neighbor];
                        if (adjacentLevel > level && (adjacentLevel == 1 || adjacentLevel != scanLevel))
                            level = Math.Max(1, adjacentLevel - 1);
                    }
                    if (level == 0)
                        continue;
                }
                levels[tile] = checked((byte)level);
                int row = level - 1;
                result.tileRows[tile] = row;
                result.floodable[tile] = (flags & 0x10000) != 0;
                // FUN_004BD740 prepends equal head keys but appends equal
                // tail keys; middle equal keys are inserted before the match.
                List<int> nodes = result.rows[row];
                int index = 0;
                if (nodes.Count != 0 && random[tile] > random[nodes[0]])
                {
                    if (random[tile] >= random[nodes[^1]])
                        index = nodes.Count;
                    else
                        while (random[nodes[index]] < random[tile])
                            index++;
                }
                nodes.Insert(index, tile);
                added = true;
            }
            if (!added)
                break;
        }
        for (int row = 29; row >= 0 && result.rows[row].Count != 0; row--)
            result.Width++;
        return result;
    }
}
