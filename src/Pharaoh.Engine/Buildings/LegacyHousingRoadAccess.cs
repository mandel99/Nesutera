using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

/// <summary>
/// Resolves the road cell used as a building's entrance and exit. Pharaoh stores
/// the selected cell in building_record.road_access; walkers must not choose a
/// fresh side of the footprint every time they leave or return.
/// </summary>
public static class LegacyBuildingRoadAccess
{
    private const uint TerrainRoad = 0x40;
    private const uint TerrainWater = 0x04;
    private const byte UnrankedNetwork = 0xff;

    [OriginalAddress(0x0047FE60, "FUN_0047fe60")]
    [OriginalAddress(0x00482650, "FUN_00482650")]
    public static bool HasNearbyRoad(
        ReadOnlySpan<byte> terrain,
        int width,
        int height,
        int origin,
        int anchorTile,
        int footprintSize,
        int radius = 2)
    {
        for (int distance = 1; distance <= radius; distance++)
        {
            if (TryFindAtDistance(
                    terrain, default, width, height, origin, anchorTile,
                    footprintSize, distance, out _))
                return true;
        }
        return false;
    }

    [OriginalAddress(0x0047FE60, "FUN_0047fe60 returns the selected road-access coordinates")]
    public static bool TryFindNearbyRoad(
        ReadOnlySpan<byte> terrain,
        int width,
        int height,
        int origin,
        int anchorTile,
        int footprintSize,
        out int roadTile,
        int radius = 2)
        => TryFindNearbyRoad(
            terrain, BuildRoadNetworkPriorities(terrain, width, height, origin),
            width, height, origin, anchorTile, footprintSize, out roadTile, radius);

    public static bool TryFindNearbyRoad(
        ReadOnlySpan<byte> terrain,
        ReadOnlySpan<byte> roadNetworkPriorities,
        int width,
        int height,
        int origin,
        int anchorTile,
        int footprintSize,
        out int roadTile,
        int radius = 2)
    {
        roadTile = -1;
        for (int distance = 1; distance <= radius; distance++)
        {
            if (TryFindAtDistance(
                    terrain, roadNetworkPriorities, width, height, origin,
                    anchorTile, footprintSize, distance, out roadTile))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reconstructs FUN_00468860's five preferred road networks. The executable
    /// ranks connected road components by size, and FUN_0047FEB0 prefers a cell
    /// belonging to the largest reachable network when several entrances are
    /// equally close to a building.
    /// </summary>
    [OriginalAddress(0x00468860, "rebuild and rank connected road networks")]
    public static byte[] BuildRoadNetworkPriorities(
        ReadOnlySpan<byte> terrain, int width, int height, int origin)
    {
        byte[] result = Enumerable.Repeat(UnrankedNetwork, LegacyCityMapSnapshot.TileCount).ToArray();
        bool[] visited = new bool[LegacyCityMapSnapshot.TileCount];
        List<List<int>> components = [];
        Queue<int> open = new();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int start = origin + y * LegacyCityMapSnapshot.MapLength + x;
            if (visited[start] || !IsUsableRoad(terrain, start))
                continue;
            List<int> component = [];
            visited[start] = true;
            open.Enqueue(start);
            while (open.TryDequeue(out int tile))
            {
                component.Add(tile);
                int localX = (tile - origin) % LegacyCityMapSnapshot.MapLength;
                int localY = (tile - origin) / LegacyCityMapSnapshot.MapLength;
                EnqueueRoad(terrain, width, height, origin, localX - 1, localY, visited, open);
                EnqueueRoad(terrain, width, height, origin, localX + 1, localY, visited, open);
                EnqueueRoad(terrain, width, height, origin, localX, localY - 1, visited, open);
                EnqueueRoad(terrain, width, height, origin, localX, localY + 1, visited, open);
            }
            components.Add(component);
        }

        foreach ((List<int> component, int rank) in components
                     .OrderByDescending(component => component.Count)
                     .ThenBy(component => component[0])
                     .Take(5)
                     .Select((component, rank) => (component, rank)))
        {
            foreach (int tile in component)
                result[tile] = checked((byte)rank);
        }
        return result;
    }

    [OriginalAddress(0x0047FEB0, "select building road-access cell at one search radius")]
    private static bool TryFindAtDistance(
        ReadOnlySpan<byte> terrain,
        ReadOnlySpan<byte> roadNetworkPriorities,
        int width,
        int height,
        int origin,
        int anchorTile,
        int footprintSize,
        int distance,
        out int roadTile)
    {
        roadTile = -1;
        int relative = anchorTile - origin;
        if (relative < 0)
            return false;
        int anchorX = relative % LegacyCityMapSnapshot.MapLength;
        int anchorY = relative / LegacyCityMapSnapshot.MapLength;
        if ((uint)anchorX >= (uint)width || (uint)anchorY >= (uint)height)
            return false;

        int size = Math.Max(1, footprintSize);
        int minX = Math.Max(0, anchorX - distance);
        int minY = Math.Max(0, anchorY - distance);
        int maxX = Math.Min(width - 1, anchorX + size - 1 + distance);
        int maxY = Math.Min(height - 1, anchorY + size - 1 + distance);
        int bestRank = 12;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int tile = origin + y * LegacyCityMapSnapshot.MapLength + x;
            if (!IsUsableRoad(terrain, tile))
                continue;
            int rank = roadNetworkPriorities.Length == LegacyCityMapSnapshot.TileCount &&
                       roadNetworkPriorities[tile] < 5
                ? roadNetworkPriorities[tile]
                : 11;
            if (rank >= bestRank)
                continue;
            bestRank = rank;
            roadTile = tile;
        }
        return roadTile >= 0;
    }

    private static bool IsUsableRoad(ReadOnlySpan<byte> terrain, int tile)
    {
        int offset = tile * sizeof(uint);
        if ((uint)(offset + sizeof(uint)) > (uint)terrain.Length)
            return false;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.Slice(offset, sizeof(uint)));
        return (flags & (TerrainRoad | TerrainWater)) == TerrainRoad;
    }

    private static void EnqueueRoad(
        ReadOnlySpan<byte> terrain,
        int width,
        int height,
        int origin,
        int x,
        int y,
        bool[] visited,
        Queue<int> open)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return;
        int tile = origin + y * LegacyCityMapSnapshot.MapLength + x;
        if (visited[tile] || !IsUsableRoad(terrain, tile))
            return;
        visited[tile] = true;
        open.Enqueue(tile);
    }
}
