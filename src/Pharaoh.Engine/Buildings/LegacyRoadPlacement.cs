using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyRoadPlacementResult(
    ReadOnlyMemory<byte> TerrainGrid,
    IReadOnlyList<int> PlacedTiles);

/// <summary>
/// Reconstructs the type-5 road tool. Pharaoh builds a four-neighbour distance
/// field from the pressed cell and backtracks one shortest route to the current
/// pointer; it does not fill the rectangle between both endpoints.
/// </summary>
public sealed class LegacyRoadPlacement
{
    public const BuildingType RoadType = BuildingType.Road;

    private const uint TerrainRoad = 0x00000040;
    private const uint TerrainGroundwaterGrass = 0x00000080;
    private const uint TerrainCanal = 0x00000100;
    private const uint TerrainFloodplain = 0x00010000;
    private const uint TerrainWater = 0x00000004;
    private const uint RoutingGridBlockingMask = 0xeeffd77f;
    private const uint EndpointBlockingMask = 0x26345237;
    private const uint RouteBlockingMask = 0x12040011;
    private const int SafeBorderWidth = 3;

    private IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? chunks;
    private int width;
    private int height;
    private int origin;
    private int orientationIndex;
    private int startTile;
    private int endTile;
    private int[]? distanceField;
    private int[] previewTiles = [];

    public bool IsActive => chunks is not null;
    public IReadOnlyList<int> PreviewTiles => previewTiles;
    public int PreviewEndpoint => IsActive
        ? endTile
        : LegacyMapOrientationIndex.InvalidTileIndex;
    public int PreviewNewTileCount => previewTiles.Count(tile =>
        (ReadTerrain(tile) & TerrainRoad) == 0);

    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    public static bool CanBeginAt(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks,
        int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(logicalChunks);
        LegacyRoadPlacement probe = new()
        {
            chunks = logicalChunks
        };
        probe.ReadGeometry(logicalChunks);
        return probe.TryToLocal(tileIndex, out _, out _) &&
            (probe.ReadTerrain(tileIndex) & TerrainRoad) == 0 &&
            probe.CanUseEndpoint(tileIndex);
    }

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    [OriginalAddress(0x0051B680, "FUN_0051b680")]
    public bool Begin(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> logicalChunks, int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(logicalChunks);
        chunks = logicalChunks;
        ReadGeometry(logicalChunks);
        if (!TryToLocal(tileIndex, out _, out _) ||
            !CanUseEndpoint(tileIndex))
        {
            Reset();
            return false;
        }
        startTile = endTile = tileIndex;
        distanceField = BuildDistanceField();
        previewTiles = [tileIndex];
        return true;
    }

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x00474F50, "FUN_00474f50")]
    public bool Update(int tileIndex)
    {
        if (!IsActive || !TryToLocal(tileIndex, out _, out _) || tileIndex == endTile)
            return false;
        endTile = tileIndex;
        previewTiles = TraceRoute();
        return true;
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x00474F50, "FUN_00474f50")]
    [OriginalAddress(0x0051C930, "FUN_0051c930")]
    public ReadOnlyMemory<byte> CreatePreviewTerrainGrid()
    {
        EnsureActive();
        byte[] terrain = RequiredChunk("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        ApplyRoadFlags(terrain, previewTiles);
        return terrain;
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    [OriginalAddress(0x00474F50, "FUN_00474f50")]
    [OriginalAddress(0x0051C930, "FUN_0051c930")]
    public LegacyRoadPlacementResult Commit()
    {
        EnsureActive();
        int[] route = previewTiles;
        byte[] terrain = RequiredChunk("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        int[] placed = route.Where(tile => (ReadTerrain(tile) & TerrainRoad) == 0 && !LegacyConstructionObstructions.HasWalker(chunks!,tile)).ToArray();
        ApplyRoadFlags(terrain, placed);
        Reset();
        return new LegacyRoadPlacementResult(terrain, placed);
    }

    public void Reset()
    {
        chunks = null;
        distanceField = null;
        previewTiles = [];
        width = height = origin = orientationIndex = startTile = endTile = 0;
    }

    [OriginalAddress(0x0051BC60, "FUN_0051bc60")]
    private int[] BuildDistanceField()
    {
        EnsureActive();
        int[] distance = new int[LegacyCityMapSnapshot.TileCount];
        Queue<int> open = new();
        distance[startTile] = 1;
        open.Enqueue(startTile);
        int[] offsets = [-LegacyCityMapSnapshot.MapLength, 1,
            LegacyCityMapSnapshot.MapLength, -1];
        while (open.Count > 0)
        {
            int tile = open.Dequeue();
            TryToLocal(tile, out int tileX, out int tileY);
            foreach (int offset in offsets)
            {
                int next = tile + offset;
                if (!TryToLocal(next, out int nextX, out int nextY) ||
                    distance[next] != 0 ||
                    Math.Abs(nextX - tileX) + Math.Abs(nextY - tileY) != 1 ||
                    !CanTraverse(next) ||
                    !CanTraverseFloodplainFringeEdge(tile, next) ||
                    HasVisitedForbiddenFringeNeighbor(next, distance))
                    continue;
                distance[next] = distance[tile] + 1;
                open.Enqueue(next);
            }
        }

        return distance;
    }

    [OriginalAddress(0x0051C930, "FUN_0051c930")]
    private int[] TraceRoute()
    {
        EnsureActive();
        if (!CanUseEndpoint(endTile) || distanceField is null || distanceField[endTile] == 0)
            return [];

        TryToLocal(startTile, out int startX, out int startY);
        List<int> route = [endTile];
        int current = endTile;
        for (int count = 1; current != startTile && count < 400; count++)
        {
            TryToLocal(current, out int currentX, out int currentY);
            int next = LegacyMapOrientationIndex.InvalidTileIndex;
            foreach (int offset in TraceOffsets(currentX, currentY, startX, startY))
            {
                int candidate = current + offset;
                if (TryToLocal(candidate, out int candidateX, out int candidateY) &&
                    Math.Abs(candidateX - currentX) + Math.Abs(candidateY - currentY) == 1 &&
                    distanceField[candidate] > 0 &&
                    distanceField[candidate] < distanceField[current] &&
                    CanTraverseFloodplainFringeEdge(current, candidate))
                {
                    next = candidate;
                    break;
                }
            }
            if (next == LegacyMapOrientationIndex.InvalidTileIndex)
                return [];
            current = next;
            route.Add(current);
        }
        if (current != startTile)
            return [];
        route.Reverse();
        return route.ToArray();
    }

    /// <summary>
    /// DAT_005F815C supplies four cardinal preferences for each relative
    /// direction returned by FUN_0051CBA0. This makes equal-length routes bend
    /// in the same direction as the original instead of depending on queue
    /// predecessor order.
    /// </summary>
    [OriginalAddress(0x0051CBA0, "FUN_0051cba0")]
    private static int[] TraceOffsets(int x, int y, int targetX, int targetY)
    {
        int north = -LegacyCityMapSnapshot.MapLength;
        int east = 1;
        int south = LegacyCityMapSnapshot.MapLength;
        int west = -1;

        if (targetX < x)
            return targetY < y ? [west, north, south, east] :
                targetY == y ? [west, north, south, east] :
                [south, west, east, north];
        if (targetX == x)
            return targetY < y ? [north, east, west, south] :
                targetY > y ? [south, west, east, north] : [];
        return targetY < y ? [north, east, west, south] :
            targetY == y ? [east, south, north, west] :
            [east, south, north, west];
    }

    private bool CanUseEndpoint(int tile) =>
        (ReadTerrain(tile) & EndpointBlockingMask) == 0 &&
        CanTraverse(tile) &&
        CanUseFloodplainFringeEndpoint(tile);

    private bool CanTraverse(int tile)
    {
        if (!LegacyPlayableMapArea.IsBuildable(
                tile, width, height, SafeBorderWidth, origin))
            return false;

        uint terrain = ReadTerrain(tile);
        if ((terrain & TerrainRoad) != 0)
            return true;
        if (LegacyConstructionObstructions.HasWalker(chunks!,tile))
            return false;
        if ((terrain & RouteBlockingMask) != 0)
            return false;
        // FUN_00516F90's derived routing grid rejects ordinary blocked terrain
        // (including water and rubble) but explicitly classifies dry
        // floodplain and canals as traversable special cells.
        if ((terrain & TerrainWater) != 0 ||
            ((terrain & (TerrainFloodplain | TerrainCanal)) == 0 &&
             (terrain & RoutingGridBlockingMask) != 0))
            return false;
        if (IsFloodplainFringe(tile) && ResolveFloodplainFringeOffset(tile) < 0)
            return false;
        if (chunks!.TryGetValue("building_grid", out ReadOnlyMemory<byte> buildings) &&
            buildings.Length == LegacyCityMapSnapshot.TileCount * sizeof(short) &&
            BinaryPrimitives.ReadInt16LittleEndian(
                buildings.Span.Slice(tile * sizeof(short), sizeof(short))) != 0)
            return false;
        return true;
    }

    /// <summary>
    /// FUN_00516F90 assigns -1 to corner fringe frames and -5 only to the four
    /// straight frames. FUN_0051C510 then permits movement across a straight
    /// frame on one axis and rejects movement along the bank.
    /// </summary>
    [OriginalAddress(0x00516F90, "FUN_00516f90")]
    [OriginalAddress(0x0051B680, "FUN_0051b680")]
    [OriginalAddress(0x0051BC60, "FUN_0051bc60")]
    [OriginalAddress(0x0051C510, "FUN_0051c510")]
    private bool CanTraverseFloodplainFringeEdge(int from, int to) =>
        CanLeaveFloodplainFringe(from, to) && CanLeaveFloodplainFringe(to, from);

    private bool CanLeaveFloodplainFringe(int tile, int neighbor)
    {
        int offset = ResolveFloodplainFringeOffset(tile);
        if (offset == int.MinValue)
            return true;
        if (offset < 0)
            return false;

        bool blockVertical = FringeChecksVertical(offset);
        bool verticalMove = Math.Abs(neighbor - tile) == LegacyCityMapSnapshot.MapLength;
        return blockVertical ? !verticalMove : verticalMove;
    }

    [OriginalAddress(0x0051C510, "FUN_0051c510")]
    private bool CanUseFloodplainFringeEndpoint(int tile)
    {
        if ((ReadTerrain(tile) & TerrainRoad) != 0)
            return true;
        int offset = ResolveFloodplainFringeOffset(tile);
        if (offset == int.MinValue)
            return true;
        if (offset < 0)
            return false;

        bool checkVertical = FringeChecksVertical(offset);
        ReadOnlySpan<int> forbiddenNeighbors = checkVertical
            ? [-LegacyCityMapSnapshot.MapLength, LegacyCityMapSnapshot.MapLength]
            : [-1, 1];
        foreach (int neighborOffset in forbiddenNeighbors)
        {
            int neighbor = tile + neighborOffset;
            if (TryToLocal(neighbor, out _, out _) &&
                (ReadTerrain(neighbor) & TerrainRoad) != 0)
                return false;
        }
        return true;
    }

    private bool HasVisitedForbiddenFringeNeighbor(int tile, int[] distance)
    {
        int offset = ResolveFloodplainFringeOffset(tile);
        if (offset < 0)
            return offset != int.MinValue;
        ReadOnlySpan<int> forbiddenNeighbors = FringeChecksVertical(offset)
            ? [-LegacyCityMapSnapshot.MapLength, LegacyCityMapSnapshot.MapLength]
            : [-1, 1];
        foreach (int neighborOffset in forbiddenNeighbors)
        {
            int neighbor = tile + neighborOffset;
            if (TryToLocal(neighbor, out _, out _) && distance[neighbor] > 0)
                return true;
        }
        return false;
    }

    private bool FringeChecksVertical(int offset)
    {
        bool vertical = offset is 0x32 or 0x36;
        return orientationIndex is 1 or 3 ? !vertical : vertical;
    }

    private bool IsFloodplainFringe(int tile) =>
        ResolveFloodplainFringeOffset(tile) != int.MinValue;

    /// <returns>
    /// 0x30/0x32/0x34/0x36 for a straight fringe, -1 for an invalid corner,
    /// or int.MinValue when the tile is not a floodplain fringe.
    /// </returns>
    private int ResolveFloodplainFringeOffset(int tile)
    {
        if (!TryToLocal(tile, out _, out _) ||
            (ReadTerrain(tile) & TerrainGroundwaterGrass) == 0 ||
            (ReadTerrain(tile) & TerrainFloodplain) != 0)
            return int.MinValue;

        ReadOnlySpan<int> offsets =
        [
            -LegacyCityMapSnapshot.MapLength,
            -LegacyCityMapSnapshot.MapLength + 1,
            1,
            LegacyCityMapSnapshot.MapLength + 1,
            LegacyCityMapSnapshot.MapLength,
            LegacyCityMapSnapshot.MapLength - 1,
            -1,
            -LegacyCityMapSnapshot.MapLength - 1
        ];
        Span<byte> neighbors = stackalloc byte[8];
        bool hasFloodplain = false;
        for (int index = 0; index < offsets.Length; index++)
        {
            int neighbor = tile + offsets[index];
            bool match = TryToLocal(neighbor, out _, out _) &&
                (ReadTerrain(neighbor) & TerrainFloodplain) != 0;
            neighbors[index] = match ? (byte)1 : (byte)0;
            hasFloodplain |= match;
        }
        if (!hasFloodplain ||
            !LegacyTerrainImageContext.DeepWater().TryResolveSelection(
                neighbors, orientationIndex,
                out LegacyTerrainImageContext.Selection selection))
            return int.MinValue;

        int imageOffset = 48 + selection.OrientationOffset;
        return imageOffset is 0x30 or 0x32 or 0x34 or 0x36 ? imageOffset : -1;
    }

    private uint ReadTerrain(int tile)
    {
        ReadOnlySpan<byte> terrain = RequiredChunk(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        return BinaryPrimitives.ReadUInt32LittleEndian(
            terrain.Slice(tile * sizeof(uint), sizeof(uint)));
    }

    private static void ApplyRoadFlags(byte[] terrain, IReadOnlyList<int> tiles)
    {
        foreach (int tile in tiles)
        {
            Span<byte> cell = terrain.AsSpan(tile * sizeof(uint), sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(
                cell, BinaryPrimitives.ReadUInt32LittleEndian(cell) | TerrainRoad);
        }
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

        int orientation = logicalChunks.TryGetValue(
                "city_view_orientation", out ReadOnlyMemory<byte> orientationChunk) &&
            orientationChunk.Length >= sizeof(int)
            ? BinaryPrimitives.ReadInt32LittleEndian(orientationChunk.Span)
            : 0;
        orientationIndex = orientation is 0 or 2 or 4 or 6 ? orientation / 2 : 0;
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
            throw new InvalidOperationException("No road drag is active.");
    }
}
