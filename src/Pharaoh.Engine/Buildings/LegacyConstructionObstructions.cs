using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public static class LegacyConstructionObstructions
{
    [OriginalAddress(0x0043B630, "FUN_0043b630 walker-grid occupancy")]
    public static bool HasWalker(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks, int tile)
        => chunks.TryGetValue("walker_grid", out var grid) && tile >= 0 && tile < grid.Length / 2 &&
           BinaryPrimitives.ReadUInt16LittleEndian(grid.Span[(tile * 2)..]) != 0;
}
