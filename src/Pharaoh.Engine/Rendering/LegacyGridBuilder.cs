using System.Buffers.Binary;
using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Environment;
using Pharaoh.Engine.Housing;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

/// <summary>
/// Reconstructs the runtime image grid for a newly loaded campaign mission.
/// Pharaoh.exe FUN_004d52a0 discards the serialized grid before calling
/// FUN_004dea50; consequently the PAK image_grid is not a renderable snapshot.
/// </summary>
public sealed class LegacyGridBuilder
{
    private const int TerrainGlobalBase = 14252;
    private const int ExpansionCliffRockImageBase = 23776;
    private const uint ClearLandBlockingMask = 0xeefed77f;
    private const uint GrassNeighborBlockingMask = 0xeeffd77f;
    private const byte AlternateTerrainFlag = 0x20;
    private const uint TerrainTree = 0x1;
    private const uint TerrainRock = 0x2;
    private const uint TerrainWater = 0x4;
    private const uint TerrainBuilding = 0x8;
    private const uint TerrainRoad = 0x40;
    private const uint TerrainGroundwaterGrass = 0x80;
    private const uint TerrainCanal = 0x100;
    private const uint TerrainMeadow = 0x800;
    private const uint TerrainRubble = 0x1000;
    private const uint TerrainFountainRange = 0x2000;
    private const uint TerrainFloodplain = 0x10000;
    private const uint TerrainMarsh = 0x40000;
    private const uint TerrainOre = 0x100000;
    private const uint TerrainDune = 0x02000000;
    private const uint TerrainDeepWater = 0x04000000;
    private static readonly int[] DirectNeighborOffsets =
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
    private static readonly LegacyTerrainImageContext DirtRoadContext =
        LegacyTerrainImageContext.DirtRoad();
    private static readonly LegacyTerrainImageContext PavedRoadContext =
        LegacyTerrainImageContext.PavedRoad();

    private readonly uint[] images = new uint[LegacyCityMapSnapshot.TileCount];
    private readonly uint[] terrain = new uint[LegacyCityMapSnapshot.TileCount];
    private readonly byte[] bitfields;
    private readonly byte[] edge;
    private readonly byte[] random;
    private readonly byte[] soilFertility;
    private readonly byte[] vegetationGrowth;
    private readonly byte[] moisture;
    private readonly byte[] desirability;
    private readonly byte[] buildingGrid;
    private readonly byte[] buildingRecords;
    private readonly IReadOnlyList<ushort> terrainGroupStarts;
    private readonly Func<int, int> floodplainGrowthStage;
    private readonly LegacyFloodplainTopology floodplainTopology;
    private readonly int width;
    private readonly int height;
    private readonly int origin;
    private readonly int orientationIndex;
    private IReadOnlyList<int>? localRefreshTiles;

    public int Width => width;
    public int Height => height;
    public int Origin => origin;
    public int Orientation => orientationIndex * 2;

    [RewriteOnly("Retains the current flood growth provider in an isolated terrain preview")]
    public LegacyGridBuilder CreatePreview(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks, Sg3Archive terrainArchive) =>
        new(chunks, terrainArchive, Orientation, floodplainGrowthStage);

    public LegacyGridBuilder(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        Sg3Archive terrainArchive,
        int? orientationOverride = null,
        Func<int, int>? floodplainGrowthStage = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(terrainArchive);

        ReadOnlySpan<byte> scenario = GetChunk(chunks, "scenario_info").Span;
        if (scenario.Length < 60)
            throw new InvalidDataException("The scenario_info chunk is too short for map geometry.");

        width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        int rowPadding = BinaryPrimitives.ReadInt32LittleEndian(scenario[56..]);
        if (width <= 0 || height <= 0 || width > LegacyCityMapSnapshot.MapLength ||
            height > LegacyCityMapSnapshot.MapLength || rowPadding != LegacyCityMapSnapshot.MapLength - width ||
            origin < 0 || origin + ((height - 1) * LegacyCityMapSnapshot.MapLength) + width > images.Length)
        {
            throw new InvalidDataException(
                $"Invalid legacy map geometry {width}x{height}, origin {origin}, padding {rowPadding}.");
        }

        ReadOnlySpan<byte> terrainBytes = GetChunk(chunks, "terrain_grid").Span;
        if (terrainBytes.Length != LegacyCityMapSnapshot.ImageGridLength)
            throw new InvalidDataException("The terrain_grid chunk has an invalid length.");
        for (int i = 0; i < terrain.Length; i++)
            terrain[i] = BinaryPrimitives.ReadUInt32LittleEndian(terrainBytes[(i * sizeof(uint))..]);

        bitfields = GetChunk(chunks, "bitfields_grid").ToArray();
        edge = GetChunk(chunks, "edge_grid").ToArray();
        random = GetChunk(chunks, "random_grid").ToArray();
        soilFertility = chunks.TryGetValue("soil_fertility_grid", out ReadOnlyMemory<byte> fertilityChunk)
            ? fertilityChunk.ToArray()
            : new byte[LegacyCityMapSnapshot.TileCount];
        vegetationGrowth = chunks.TryGetValue("vegetation_growth", out ReadOnlyMemory<byte> growthChunk)
            ? growthChunk.ToArray()
            : new byte[LegacyCityMapSnapshot.TileCount];
        moisture = chunks.TryGetValue("moisture_grid", out ReadOnlyMemory<byte> moistureChunk)
            ? moistureChunk.ToArray()
            : new byte[LegacyCityMapSnapshot.TileCount];
        desirability = chunks.TryGetValue("desirability_grid", out ReadOnlyMemory<byte> desirabilityChunk)
            ? desirabilityChunk.ToArray()
            : new byte[LegacyCityMapSnapshot.TileCount];
        buildingGrid = chunks.TryGetValue("building_grid", out ReadOnlyMemory<byte> buildingGridChunk)
            ? buildingGridChunk.ToArray()
            : new byte[LegacyCityMapSnapshot.TileCount * sizeof(short)];
        buildingRecords = chunks.TryGetValue("buildings", out ReadOnlyMemory<byte> buildingRecordsChunk)
            ? buildingRecordsChunk.ToArray()
            : new byte[4000 * 264];
        if (bitfields.Length != LegacyCityMapSnapshot.TileCount || edge.Length != LegacyCityMapSnapshot.TileCount ||
            random.Length != LegacyCityMapSnapshot.TileCount ||
            soilFertility.Length != LegacyCityMapSnapshot.TileCount ||
            vegetationGrowth.Length != LegacyCityMapSnapshot.TileCount ||
            moisture.Length != LegacyCityMapSnapshot.TileCount ||
            desirability.Length != LegacyCityMapSnapshot.TileCount ||
            buildingGrid.Length != LegacyCityMapSnapshot.TileCount * sizeof(short) ||
            buildingRecords.Length != 4000 * 264)
            throw new InvalidDataException("A byte-sized terrain grid has an invalid length.");
        int serializedOrientation =
            BinaryPrimitives.ReadInt32LittleEndian(GetChunk(chunks, "city_view_orientation").Span);
        if (orientationOverride is int requestedOrientation &&
            requestedOrientation is not (0 or 2 or 4 or 6))
            throw new ArgumentOutOfRangeException(
                nameof(orientationOverride), requestedOrientation,
                "Legacy orientation must be 0, 2, 4, or 6.");
        int orientation = orientationOverride ??
            (serializedOrientation is 0 or 2 or 4 or 6 ? serializedOrientation : 0);
        if (terrainArchive.GroupImageIds.Count != 300)
            throw new InvalidDataException("The terrain SG3 does not contain the 300-entry image-group table.");
        terrainGroupStarts = terrainArchive.GroupImageIds;
        this.floodplainGrowthStage = floodplainGrowthStage ?? (_ => 0);
        floodplainTopology = LegacyFloodplainTopology.Build(
            terrainBytes, random, width, height, origin);
        orientationIndex = orientation / 2;
        RebuildSentinelGroundwaterVegetation(scenario);
    }

    /// <summary>
    /// New-campaign PAKs store 255 in vegetation_growth and rely on the
    /// post-load FUN_00486E70/FUN_00487450 pass. Recreate that pass only for
    /// this sentinel state; saved games retain their live growth values.
    /// </summary>
    [OriginalAddress(0x00486E70, "FUN_00486e70")]
    [OriginalAddress(0x00487450, "FUN_00487450")]
    private void RebuildSentinelGroundwaterVegetation(ReadOnlySpan<byte> scenario)
    {
        bool hasGroundwaterGrass = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                if ((terrain[tile] & TerrainGroundwaterGrass) == 0)
                    continue;
                hasGroundwaterGrass = true;
                if (vegetationGrowth[tile] != byte.MaxValue)
                    return;
            }
        }
        if (!hasGroundwaterGrass)
            return;

        int climate = scenario.Length > 0x3d8 ? scenario[0x3d8] : 1;
        int waterRadius = climate switch { 0 => 46, 2 => 8, _ => 16 };
        int[] waterDistance = BuildTerrainDistanceGrid(TerrainWater, waterRadius);
        int[] groundwaterDistance = BuildTerrainDistanceGrid(TerrainFloodplain | TerrainMarsh, 8);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                uint flags = terrain[tile];
                if ((flags & (TerrainWater | TerrainFloodplain | TerrainMarsh)) != 0)
                {
                    terrain[tile] = flags & ~TerrainGroundwaterGrass;
                    vegetationGrowth[tile] = 0;
                    continue;
                }

                int waterGrowth = waterDistance[tile] <= waterRadius
                    ? WaterDistanceGrowth(waterDistance[tile], waterRadius)
                    : -1;
                // FUN_00486D30 starts at ring one and loops while
                // ring < limit. A limit of eight therefore admits only
                // distances 1..7; including distance eight adds a sparse
                // outer row that ends more abruptly than the water gradient.
                int groundwaterGrowth = groundwaterDistance[tile] < 8
                    ? ScaleDistanceGrowth(Math.Max(groundwaterDistance[tile] - 1, 0), 8)
                    : -1;
                int growth = Math.Max(waterGrowth, groundwaterGrowth);
                if (growth < 0)
                {
                    terrain[tile] = flags & ~TerrainGroundwaterGrass;
                    vegetationGrowth[tile] = 0;
                }
                else
                {
                    terrain[tile] = flags | TerrainGroundwaterGrass;
                    vegetationGrowth[tile] = checked((byte)Math.Max(growth, 1));
                }
            }
        }
        EncodeWetGrassTransitions();
    }

    /// <summary>
    /// Recreates the second half of FUN_00487450/FUN_00486F20. Grass with
    /// intermediate growth next to WATER/MARSH is encoded as 0x80 plus the
    /// orientation byte selected from offset +8 of the matching 16-byte record
    /// at 0x005CBFB8. The record's cycling item is a separate output and must
    /// not be added to that byte. The empty-
    /// land pass interprets that value as terrain group 64. This is the
    /// one-cell wet-grass transition around a shoreline, not mature group 52.
    /// </summary>
    [OriginalAddress(0x00486F20, "FUN_00486f20")]
    private void EncodeWetGrassTransitions()
    {
        LegacyTerrainImageContext context = LegacyTerrainImageContext.DeepWater();
        ForEachTile(tile =>
        {
            if ((terrain[tile] & TerrainGroundwaterGrass) == 0 ||
                vegetationGrowth[tile] is <= 40 or >= 91)
                return;

            Span<byte> neighbors = stackalloc byte[8];
            bool hasWetNeighbor = false;
            for (int direction = 0; direction < DirectNeighborOffsets.Length; direction++)
            {
                int neighbor = tile + DirectNeighborOffsets[direction];
                // The immediate dry floodplain fringe keeps its hard group-31
                // bank sprite. Treat that first land row as the dense side of
                // the group-64 grass context so the following row receives the
                // same oriented dense-to-sparse transition used outside a
                // WATER/MARSH shoreline.
                bool wet = IsWetGrassContextNeighbor(neighbor) ||
                    IsDryFloodplainGrassFringe(neighbor);
                neighbors[direction] = wet ? (byte)1 : (byte)0;
                hasWetNeighbor |= wet;
            }

            if (!hasWetNeighbor ||
                !context.TryResolveSelection(
                    neighbors, orientationIndex,
                    out LegacyTerrainImageContext.Selection selection))
                return;

            vegetationGrowth[tile] = selection.OrientationOffset == 14
                ? (byte)100
                : checked((byte)(0x80 | selection.OrientationOffset));
        });
    }

    /// <summary>
    /// Recreates the terrain-only part shared by FUN_004872C0, FUN_00487300,
    /// and FUN_00487390. Besides a wet neighbour itself, the EXE treats a cell
    /// bracketed by WATER on opposite cardinal sides as wet. This closes the
    /// one-cell diagonal gaps before the 26-record context lookup.
    /// </summary>
    [OriginalAddress(0x004872C0, "FUN_004872c0")]
    [OriginalAddress(0x00487300, "FUN_00487300")]
    [OriginalAddress(0x00487390, "FUN_00487390")]
    private bool IsWetGrassContextNeighbor(int tile)
    {
        if (!IsInsideMission(tile))
            return false;

        uint flags = terrain[tile];
        if ((flags & (TerrainWater | TerrainMarsh)) != 0)
            return true;

        int north = tile - LegacyCityMapSnapshot.MapLength;
        int east = tile + 1;
        int south = tile + LegacyCityMapSnapshot.MapLength;
        int west = tile - 1;
        bool bracketedByWater =
            (IsWater(north) && IsWater(south)) ||
            (IsWater(west) && IsWater(east));
        if (bracketedByWater)
            return true;

        byte growth = vegetationGrowth[tile];
        return growth == 100;
    }

    [RewriteOnly("Extends the shoreline grass blend one land cell beyond the hard floodplain bank")]
    private bool IsDryFloodplainGrassFringe(int tile) =>
        IsInsideMission(tile) &&
        (terrain[tile] & TerrainGroundwaterGrass) != 0 &&
        (terrain[tile] & TerrainFloodplain) == 0 &&
        HasNeighbor(tile, TerrainFloodplain);

    [RewriteOnly("Bounds-safe wrapper around the original terrain WATER-bit tests")]
    private bool IsWater(int tile) =>
        IsInsideMission(tile) && (terrain[tile] & TerrainWater) != 0;

    [OriginalAddress(0x00486D30, "FUN_00486d30")]
    private int[] BuildTerrainDistanceGrid(uint sourceMask, int maximumDistance)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, LegacyCityMapSnapshot.TileCount).ToArray();
        Queue<(int X, int Y)> pending = new();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                uint flags = terrain[tile];
                if ((flags & sourceMask) == 0 || (flags & 0x80000u) != 0)
                    continue;
                distance[tile] = 0;
                pending.Enqueue((x, y));
            }
        }

        while (pending.TryDequeue(out (int X, int Y) cell))
        {
            int tile = TileIndex(cell.X, cell.Y);
            int nextDistance = distance[tile] + 1;
            if (nextDistance > maximumDistance)
                continue;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) ||
                        (uint)(cell.X + dx) >= width || (uint)(cell.Y + dy) >= height)
                        continue;
                    int neighbor = TileIndex(cell.X + dx, cell.Y + dy);
                    if (distance[neighbor] <= nextDistance)
                        continue;
                    distance[neighbor] = nextDistance;
                    pending.Enqueue((cell.X + dx, cell.Y + dy));
                }
            }
        }
        return distance;
    }

    private static int WaterDistanceGrowth(int distance, int radius)
    {
        int adjustedDistance;
        int denominator = radius;
        if (distance < radius - 12)
        {
            adjustedDistance = 0;
        }
        else
        {
            adjustedDistance = distance - (radius - 12) + 1;
            denominator = 12;
        }
        return ScaleDistanceGrowth(Math.Max(adjustedDistance - 1, 0), denominator);
    }

    [OriginalAddress(0x00486EC0, "FUN_00486ec0")]
    private static int ScaleDistanceGrowth(int distance, int denominator)
    {
        // FUN_00486EC0 clamps the numerator against the original search
        // radius, then raises only the divisor to twelve. Raising both values
        // skips the sparse outer grass stages for the eight-cell groundwater
        // search and produces a hard grass-to-sand boundary.
        int numerator = Math.Clamp(denominator - distance, 0, denominator);
        int divisor = Math.Max(denominator, 12);
        return (numerator * 96 / divisor) - 1;
    }

    /// <summary>
    /// Exact clear/empty-land phase of FUN_004767d0: largest footprints are
    /// selected first and their consecutive SG3 frames occupy the footprint.
    /// Other terrain remains zero until its corresponding EXE phase is ported.
    /// </summary>
    public byte[] BuildEmptyLandStage()
    {
        Array.Clear(images);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                if (!IsClear(tile) || images[tile] != 0)
                    continue;

                int group = (bitfields[tile] & AlternateTerrainFlag) != 0 ? 7 : 10;
                int groupBase = GlobalGroupStart(group);
                if (CanPlace(x, y, 4))
                    Place(x, y, 4, groupBase + 42);
                else if (CanPlace(x, y, 3))
                    Place(x, y, 3, groupBase + 24 + (9 * (random[tile] & 1)));
                else if (CanPlace(x, y, 2))
                    Place(x, y, 2, groupBase + 8 + (4 * (random[tile] & 3)));
                else
                    Place(x, y, 1, groupBase + (random[tile] & 7));
            }
        }

        byte[] result = new byte[LegacyCityMapSnapshot.ImageGridLength];
        for (int i = 0; i < images.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * sizeof(uint)), images[i]);
        return result;
    }

    /// <summary>
    /// Rebuilds the terrain-only runtime snapshot used before walkers and
    /// buildings are submitted. The phase order follows Pharaoh.exe
    /// RebuildRuntimeMapImageGrid (FUN_004DEA50).
    /// </summary>
    public LegacyCityMapSnapshot BuildTerrainSnapshot(bool fertilityOverlay = false)
    {
        Array.Clear(images);
        byte[] runtimeEdge = edge.ToArray();
        byte[] runtimeBitfields = bitfields.ToArray();

        // Every terrain phase in the EXE marks a one-tile draw cell. Do not
        // retain multi-tile coordinates from the serialized working grid.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                runtimeEdge[tile] = (byte)((runtimeEdge[tile] & 0x80) | 0x40);
                runtimeBitfields[tile] &= 0xf0;
            }
        }

        BuildRocks(runtimeEdge, runtimeBitfields);
        BuildTrees();
        BuildEmptyLand();
        BuildFloodplain();
        BuildMeadow(runtimeEdge, runtimeBitfields);
        BuildMarsh();
        BuildDunes(runtimeEdge, runtimeBitfields);
        // FUN_004DEA50 refreshes floodplain again after the dune footprint pass.
        BuildFloodplain();
        BuildWater();
        BuildRoads();
        BuildWells(runtimeEdge, runtimeBitfields);
        BuildSquareWorkplaces(runtimeEdge, runtimeBitfields);
        BuildHousing(runtimeEdge, runtimeBitfields);
        BuildDisasters(runtimeEdge,runtimeBitfields);

        if (fertilityOverlay)
            ApplyFertilityOverlay();

        // The scenario rectangle is embedded in the visible 228x228 diamond.
        // Pharaoh_Terrain.sg3 image 201 (terrain group 1) is SPR_SPACE and
        // must replace every visible cell outside the playable rectangle.
        ApplySpaceOutsidePlayableArea();

        return new LegacyCityMapSnapshot(
            SerializeImages(), runtimeEdge, runtimeBitfields, BuildPlayableAreaGrid());
    }

    [OriginalAddress(0x0046D100, "FUN_0046d100")]
    [OriginalAddress(0x0047C2B0, "FUN_0047c2b0")]
    private void BuildDisasters(byte[] runtimeEdge,byte[] runtimeBitfields)
    {
        for(int tile=0;tile<LegacyCityMapSnapshot.TileCount;tile++)
        {
            if((terrain[tile]&0x1000)!=0)images[tile]=(uint)(LegacyBuildingDisasters.RubbleImage+(random[tile]&7));
            else
            {
                int id=BinaryPrimitives.ReadInt16LittleEndian(buildingGrid.AsSpan(tile*2));
                if(id<=0||id>=4000||buildingRecords[id*264]!=1||BinaryPrimitives.ReadInt16LittleEndian(buildingRecords.AsSpan(id*264+16))!=99)continue;
                images[tile]=(uint)LegacyBuildingDisasters.GetBurningImage(random[tile]);
            }
            runtimeEdge[tile]=0x40;runtimeBitfields[tile]&=0xe0;
        }
    }

    /// <summary>
    /// Re-submits the static base image for verified square workplaces and safety services.
    /// The executable rebuilds building images after clearing the runtime image grid;
    /// retaining only the serialized image_grid makes newly placed buildings disappear
    /// on the next terrain rebuild.
    /// </summary>
    [OriginalAddress(0x00470990, "ordinary square workplace and service image submission")]
    [OriginalAddress(0x0047BF50, "square building footprint anchors")]
    private void BuildSquareWorkplaces(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        for (int buildingId = 1; buildingId < 4000; buildingId++)
        {
            int recordOffset = buildingId * 264;
            if (buildingRecords[recordOffset] is not (1 or 3))
                continue;
            BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                buildingRecords.AsSpan(recordOffset + 16, sizeof(short)));
            if (LegacySquareBuildingPlacement.TryGetDefinition(type, out var food))
            {
                int originTile = BinaryPrimitives.ReadInt32LittleEndian(buildingRecords.AsSpan(recordOffset + 12));
                int anchor = LegacySquareBuildingPlacement.DrawAnchor(originTile, food.Size, Orientation);
                foreach (int cell in LegacySquareBuildingPlacement.Cells(originTile, food.Size))
                {
                    if ((uint)cell >= LegacyCityMapSnapshot.TileCount) continue;
                    images[cell] = checked((uint)food.ImageId);
                    runtimeBitfields[cell] = (byte)((runtimeBitfields[cell] & 0xe0) | food.Size - 1);
                    runtimeEdge[cell] = (byte)((cell - originTile) % 228 + (cell - originTile) / 228 * 8);
                    if (cell == anchor) runtimeEdge[cell] |= 0x40;
                }
                continue;
            }
            if (!LegacySafetyServicePlacement.TryGetDefinition(
                    type, out LegacySafetyServiceDefinition definition))
                continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(
                buildingRecords.AsSpan(recordOffset + 12, sizeof(int)));
            if ((uint)tile >= LegacyCityMapSnapshot.TileCount)
                continue;
            images[tile] = checked((uint)definition.ImageId);
            runtimeBitfields[tile] &= 0xf0;
            runtimeEdge[tile] = (byte)((runtimeEdge[tile] & 0x80) | 0x40);
        }
    }

    /// <summary>Rebuilds type-92 wells from building records after image_grid is cleared.</summary>
    [OriginalAddress(0x004555D0, "FUN_004555d0")]
    [OriginalAddress(0x00470990, "type 92 render dispatch")]
    private void BuildWells(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        for (int buildingId = 1; buildingId < 4000; buildingId++)
        {
            int recordOffset = buildingId * 264;
            if (buildingRecords[recordOffset] is not (1 or 3) ||
                (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                    buildingRecords.AsSpan(recordOffset + 16, sizeof(short))) != BuildingType.Well)
                continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(
                buildingRecords.AsSpan(recordOffset + 12, sizeof(int)));
            if ((uint)tile >= LegacyCityMapSnapshot.TileCount)
                continue;
            images[tile] = checked((uint)LegacyWellPlacement.GetImageId(
                buildingRecords[recordOffset + 77]));
            runtimeBitfields[tile] &= 0xf0;
            runtimeEdge[tile] = (byte)((runtimeEdge[tile] & 0x80) | 0x40);
        }
    }

    /// <summary>
    /// Updates the fixed runtime terrain array used by the original terrain
    /// passes. The builder and its derived lookup tables remain alive across
    /// flood steps instead of being reconstructed from every save chunk.
    /// </summary>
    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    public bool RefreshTerrainTiles(ReadOnlySpan<byte> terrainGrid, IEnumerable<int> tiles)
    {
        if (terrainGrid.Length != LegacyCityMapSnapshot.ImageGridLength)
            throw new ArgumentException("The terrain grid has an invalid length.", nameof(terrainGrid));
        bool changed=false;
        foreach (int tile in tiles)
        {
            if ((uint)tile >= LegacyCityMapSnapshot.TileCount)
                continue;
            uint flags=BinaryPrimitives.ReadUInt32LittleEndian(terrainGrid.Slice(tile*4,4));
            changed|=terrain[tile]!=flags;terrain[tile]=flags;
        }
        return changed;
    }

    [OriginalAddress(0x004BD950,"Refresh the local flood context rather than running map initialization")]
    [OriginalAddress(0x00479E70,"Dry floodplain context and growth")]
    [OriginalAddress(0x0047A220,"Water and SPR_FP_COAST context")]
    [OriginalAddress(0x0047B670,"Restored floodplain roads")]
    public LegacyCityMapSnapshot RefreshFloodImages(LegacyCityMapSnapshot previous,IReadOnlyCollection<int> affected)
    {
        int[] cells=affected.Where(t=>IsInsideMission(t)&&(terrain[t]&(TerrainWater|TerrainFloodplain))!=0&&
            (terrain[t]&(TerrainBuilding|TerrainRubble|TerrainCanal))==0).ToArray();
        Dictionary<int,uint> replacements=new(cells.Length);
        // Floodplain and road tables have a single variant; water uses the
        // tile's random byte and deep-water lookup resets its state. These
        // lookups are therefore independent of cells outside this rectangle.
        localRefreshTiles=cells;
        try{BuildFloodplain();BuildWater();BuildRoads();}
        finally{localRefreshTiles=null;}
        foreach(int tile in cells)
            if(IsInsidePlayableArea(tile)&&images[tile]!=previous.GetImageId(tile))replacements[tile]=images[tile];
        return replacements.Count==0?previous:previous.WithImageIds(replacements);
    }

    [RewriteOnly("Limits the incremental fast path to water and floodplain surfaces with unchanged object topology")]
    public bool CanRefreshFloodImages(IReadOnlyCollection<int> affected)
        => affected.All(t=>!IsInsideMission(t)||(terrain[t]&(TerrainWater|TerrainFloodplain))==0||
            (terrain[t]&(TerrainBuilding|TerrainRubble|TerrainCanal))==0);

    [OriginalAddress(0x00474700,"Local clear-land context refresh")]
    [OriginalAddress(0x004767D0,"Retile ground inside the affected rectangle")]
    [RewriteOnly("Retains unrelated terrain and object topology instead of recreating the entire mission snapshot")]
    public LegacyCityMapSnapshot RefreshClearedTerrainImages(LegacyCityMapSnapshot previous,IReadOnlySet<int> affected)
    {
        for(int tile=0;tile<images.Length;tile++)images[tile]=previous.GetImageId(tile);
        int[] cells=affected.Where(IsInsideMission).Order().ToArray();
        foreach(int tile in cells)
            if((terrain[tile]&(TerrainBuilding|TerrainRock|TerrainDune|TerrainMeadow))==0)images[tile]=0;
        localRefreshTiles=cells;
        try{BuildTrees();BuildEmptyLand();BuildMarsh();BuildFloodplain();BuildWater();BuildRoads();}
        finally{localRefreshTiles=null;}
        Dictionary<int,uint> replacements=[];
        foreach(int tile in cells)
            if(IsInsidePlayableArea(tile)&&images[tile]>=6&&images[tile]!=previous.GetImageId(tile))replacements[tile]=images[tile];
        return replacements.Count==0?previous:previous.WithImageIds(replacements);
    }

    /// <summary>
    /// FUN_00474130 creates one type-10 building per selected cell and submits
    /// SPR_B_FORSALE (General #793) while it is empty. Occupied housing
    /// houses use the image groups selected by FUN_0046CEA0; only the anchor
    /// cell draws the large sprite of a 2x2, 3x3, or 4x4 footprint.
    /// </summary>
    [OriginalAddress(0x00474130, "FUN_00474130")]
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    private void BuildHousing(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        for (int buildingId = 1; buildingId < 4000; buildingId++)
        {
            int recordOffset = buildingId * 264;
            BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                buildingRecords.AsSpan(recordOffset + 16, sizeof(short)));
            if (buildingRecords[recordOffset] is not (1 or 3) || !type.IsHousing())
                continue;

            int anchor = BinaryPrimitives.ReadInt32LittleEndian(
                buildingRecords.AsSpan(recordOffset + 12, sizeof(int)));
            int anchorX = BinaryPrimitives.ReadInt16LittleEndian(
                buildingRecords.AsSpan(recordOffset + 6, sizeof(short)));
            int anchorY = BinaryPrimitives.ReadInt16LittleEndian(
                buildingRecords.AsSpan(recordOffset + 8, sizeof(short)));
            int size = Math.Clamp((int)buildingRecords[recordOffset + 3], 1, 6);
            if (anchorX < 0 || anchorY < 0 || anchorX + size > width || anchorY + size > height ||
                anchor != TileIndex(anchorX, anchorY))
                continue;

            short population = BinaryPrimitives.ReadInt16LittleEndian(
                buildingRecords.AsSpan(recordOffset + 28, sizeof(short)));
            uint image = type == BuildingType.Housing && population <= 0
                ? LegacyHousingPlacement.ForSaleImageId
                : checked((uint)HousingManager.GetImageId(
                    (int)type, population, buildingRecords[recordOffset + 4] != 0, random[anchor]));

            int drawX = orientationIndex is 0 or 1 ? 0 : size - 1;
            int drawY = orientationIndex is 1 or 2 ? 0 : size - 1;
            for (int row = 0; row < size; row++)
            for (int column = 0; column < size; column++)
            {
                int tile = TileIndex(anchorX + column, anchorY + row);
                if ((terrain[tile] & TerrainBuilding) == 0 ||
                    BinaryPrimitives.ReadInt16LittleEndian(
                        buildingGrid.AsSpan(tile * sizeof(short), sizeof(short))) != buildingId)
                    continue;

                images[tile] = image;
                runtimeBitfields[tile] = (byte)((runtimeBitfields[tile] & 0xe0) | (size - 1));
                runtimeEdge[tile] = (byte)(column + row * 8);
                if (column == drawX && row == drawY)
                    runtimeEdge[tile] |= 0x40;
            }
        }
    }

    /// <summary>
    /// Render-time replacement performed by Pharaoh.exe FUN_00433760. This
    /// is deliberately not part of the rebuilt runtime image grid: the
    /// fertility view substitutes terrain group 0x22a (Terrain group 57)
    /// only while drawing the overlay.
    /// </summary>
    [OriginalAddress(0x00433760, "FUN_00433760")]
    private void ApplyFertilityOverlay()
    {
        int fertilityBase = GlobalGroupStart(57);
        ForEachTile(tile =>
        {
            uint flags = terrain[tile];
            if ((flags & 0x26341777u) != 0 && (flags & TerrainBuilding) == 0)
                return;
            if ((flags & 0x4000u) != 0)
                return;

            int fertility = unchecked((sbyte)soilFertility[tile]);
            if ((flags & 0x28u) == 0 && fertility == 0)
                return;

            int variant = fertility < -10
                ? 0
                : fertility < 70
                    ? (fertility / 10) + 2
                    : 9;
            images[tile] = checked((uint)(fertilityBase + variant));
        });
    }

    [OriginalAddress(0x00475BF0, "FUN_00475bf0")]
    [OriginalAddress(0x00486600, "FUN_00486600")]
    private void BuildTrees()
    {
        int imageBase = GlobalGroupStart(4);
        ForEachTile(tile =>
        {
            if ((terrain[tile] & TerrainTree) != 0)
            {
                // DAT_00F56B5C is registry entry 0x1f5, terrain group 4.
                // FUN_00486600 stores its moisture rows sixteen frames apart,
                // with the second eight-frame half selected when the first
                // surrounding tree/rock ring is complete. The previous * 8
                // arithmetic accidentally used that context half as the next
                // moisture row and never reached the wettest grass sprites.
                int moistureRow = Math.Min(vegetationGrowth[tile], (byte)100) / 34;
                int contextOffset;
                if (HasCompleteTreeRing(tile, 3))
                    contextOffset = 56;
                else if (HasCompleteTreeRing(tile, 2))
                    contextOffset = 48;
                else
                    contextOffset = (moistureRow * 16) +
                        (HasCompleteTreeRing(tile, 1) ? 8 : 0);
                images[tile] = checked((uint)(imageBase + contextOffset +
                    (random[tile] & 7)));
            }
        });
    }

    /// <summary>
    /// Equivalent of the terrain test performed by FUN_00481490 for the
    /// one-, two-, and three-cell perimeter tables used by FUN_00486600.
    /// A perimeter is complete when every cell on its Chebyshev ring is
    /// outside the active rectangle or carries TREE/ROCK (terrain bits 0..1).
    /// </summary>
    private bool HasCompleteTreeRing(int tile, int radius)
    {
        int centerX = (tile - origin) % LegacyCityMapSnapshot.MapLength;
        int centerY = (tile - origin) / LegacyCityMapSnapshot.MapLength;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if ((uint)x >= width || (uint)y >= height)
                    continue;
                if ((terrain[TileIndex(x, y)] & (TerrainTree | TerrainRock)) == 0)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Port of the meadow image selection in Pharaoh.exe FUN_00477500 and
    /// FUN_00479B70. The moisture grid is the simulation's water-reach value:
    /// it selects one of four increasingly grassy 12-frame rows. Vegetation
    /// growth selects the frame within that row, so dry/distant cells remain
    /// sparse while well-watered cells use the densest grass sprites.
    /// </summary>
    private void BuildMeadow(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        int meadowImageBase = GlobalGroupStart(37);
        int sparseSeedlingImageBase = GlobalGroupStart(66);
        int mediumSeedlingImageBase = GlobalGroupStart(55);
        int mediumMatureImageBase = GlobalGroupStart(54);

        ForEachTile(tile =>
        {
            uint flags = terrain[tile];
            if ((flags & TerrainMeadow) == 0 ||
                (flags & (TerrainCanal | 0x200u | 0x400u | TerrainRubble)) != 0 ||
                (flags & TerrainRoad) != 0 ||
                ((flags & TerrainBuilding) != 0 && (flags & 0x10000000u) == 0) ||
                (flags & 0x20u) != 0)
            {
                return;
            }

            // The EXE evaluates these as double expressions and truncates via
            // __ftol. Integer division has the same toward-zero result for the
            // byte ranges, including the serialized zero sentinel.
            int density = ((moisture[tile] - 1) * 3) / 99;
            int growth = ((Math.Min(vegetationGrowth[tile], (byte)100) - 1) * 12) / 96;

            int imageId;
            if (density == 0 && growth == 0)
                imageId = sparseSeedlingImageBase + (random[tile] & 7);
            else if (density == 2 && growth == 0)
                imageId = mediumSeedlingImageBase + (random[tile] & 7);
            else if (density == 2 && growth == 11)
                imageId = mediumMatureImageBase + (random[tile] & 7);
            else
                imageId = meadowImageBase + (density * 12) + growth;

            images[tile] = checked((uint)imageId);
            runtimeBitfields[tile] &= 0xf0;
            runtimeEdge[tile] |= 0x40;
        });
    }

    /// <summary>
    /// Port of Pharaoh.exe FUN_00475DC0 for ordinary and ore-bearing rocks.
    /// Both families use the same largest-first footprint layout as dunes,
    /// but have distinct terrain masks and SG3 groups.
    /// </summary>
    private void BuildRocks(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        int rockImageBase = GlobalGroupStart(8);
        int oreImageBase = GlobalGroupStart(42);

        ForEachTile(tile =>
        {
            if (IsUpdatableRock(tile))
            {
                images[tile] = 0;
                runtimeBitfields[tile] &= 0xf0;
                runtimeEdge[tile] |= 0x40;
            }
        });

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                if (!IsUpdatableRock(tile) || images[tile] != 0)
                    continue;

                uint flags = terrain[tile];
                // ROCK|0x20000000 uses the separate neighbour-sensitive cliff
                // selector FUN_00475FD0 and must never join square clusters.
                if ((flags & 0x20000002) == 0x20000002)
                {
                    int offset = ResolveCliffRockOffset(tile);
                    images[tile] = checked((uint)(offset < 0
                        ? rockImageBase + (random[tile] & 7)
                        : ExpansionCliffRockImageBase + offset));
                    continue;
                }

                bool isOre = (flags & (TerrainRock | TerrainOre)) == (TerrainRock | TerrainOre);
                uint expectedTerrain = isOre ? TerrainRock | TerrainOre : TerrainRock;
                int imageBase = isOre ? oreImageBase : rockImageBase;
                if (CanPlaceTerrainFootprint(x, y, 3, expectedTerrain))
                {
                    PlaceMultiTileTerrain(
                        x, y, 3, imageBase + 12 + (random[tile] & 1), runtimeEdge, runtimeBitfields);
                }
                else if (CanPlaceTerrainFootprint(x, y, 2, expectedTerrain))
                {
                    PlaceMultiTileTerrain(
                        x, y, 2, imageBase + 8 + (random[tile] & 3), runtimeEdge, runtimeBitfields);
                }
                else
                {
                    images[tile] = checked((uint)(imageBase + (random[tile] & 7)));
                }
            }
        }
    }

    private bool IsUpdatableRock(int tile) =>
        (terrain[tile] & TerrainRock) != 0 &&
        (bitfields[tile] & 0x80) == 0 &&
        (terrain[tile] & 0x600) == 0;

    private int ResolveCliffRockOffset(int tile)
    {
        ReadOnlySpan<int> neighborOffsets =
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
        Span<bool> context = stackalloc bool[8];
        int count = 0;
        for (int direction = 0; direction < context.Length; direction++)
        {
            int neighbor = tile + neighborOffsets[direction];
            bool connected = HasExactGridTerrain(neighbor, 0x20000002);
            bool excluded = HasExactGridTerrain(neighbor, 0x80000);
            context[direction] = connected && !excluded;
            if (context[direction]) count++;
        }

        int variant = random[tile];
        if (count == 8)
            return 48 + (variant % 24);
        if (count == 7)
        {
            if (!context[1]) return OrientedOffset([12, 15, 18, 21]) + (variant % 3);
            if (!context[7]) return OrientedOffset([15, 18, 21, 12]) + (variant % 3);
            if (!context[5]) return OrientedOffset([18, 21, 12, 15]) + (variant % 3);
            if (!context[3]) return OrientedOffset([21, 12, 15, 18]) + (variant % 3);
        }
        else if (count == 6)
        {
            if (!context[7] && !context[3])
                return OrientedOffset([72, 75, 72, 75]) + (variant % 3);
            if (!context[1] && !context[5])
                return OrientedOffset([75, 72, 75, 72]) + (variant % 3);
        }

        if (context[5] && context[7] && context[0] && context[1] && context[2] && count < 8)
            return OrientedOffset([24, 30, 36, 42]) + (variant % 6);
        if (context[4] && context[5] && context[6] && context[7] && context[0] && count < 8)
            return OrientedOffset([30, 36, 42, 24]) + (variant % 6);
        if (context[2] && context[3] && context[4] && context[5] && context[6] && count < 8)
            return OrientedOffset([36, 42, 24, 30]) + (variant % 6);
        if (context[0] && context[1] && context[2] && context[3] && context[4] && count < 8)
            return OrientedOffset([42, 24, 30, 36]) + (variant % 6);
        if (context[0] && context[1] && context[2])
            return OrientedOffset([0, 3, 6, 9]) + (variant % 3);
        if (context[6] && context[7] && context[0])
            return OrientedOffset([3, 6, 9, 0]) + (variant % 3);
        if (context[4] && context[5] && context[6])
            return OrientedOffset([6, 9, 0, 3]) + (variant % 3);
        if (context[2] && context[3] && context[4])
            return OrientedOffset([9, 0, 3, 6]) + (variant % 3);
        return -1;
    }

    private int OrientedOffset(ReadOnlySpan<int> offsets) => offsets[orientationIndex];

    private bool HasExactGridTerrain(int tile, uint mask) =>
        (uint)tile < terrain.Length && (terrain[tile] & mask) == mask;

    /// <summary>
    /// Exact footprint selection and property-grid writes from Pharaoh.exe
    /// FUN_00477210. Dunes are greedily grouped as 3x3, then 2x2, then 1x1.
    /// FUN_0047BF50 stores one shared image id across a footprint and selects
    /// one orientation-dependent draw cell in edge_grid.
    /// </summary>
    private void BuildDunes(byte[] runtimeEdge, byte[] runtimeBitfields)
    {
        int imageBase = GlobalGroupStart(13);

        // The EXE first resets only dune cells that are not plazas/earthquakes
        // (bit 0x80) and do not carry elevation/access-ramp terrain (0x600).
        ForEachTile(tile =>
        {
            if (IsUpdatableDune(tile))
            {
                images[tile] = 0;
                runtimeBitfields[tile] &= 0xf0;
                runtimeEdge[tile] |= 0x40;
            }
        });

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = TileIndex(x, y);
                if (!IsUpdatableDune(tile) || images[tile] != 0)
                    continue;

                if (CanPlaceTerrainFootprint(x, y, 3, TerrainDune))
                {
                    PlaceMultiTileTerrain(
                        x, y, 3, imageBase + 12 + (random[tile] & 1), runtimeEdge, runtimeBitfields);
                }
                else if (CanPlaceTerrainFootprint(x, y, 2, TerrainDune))
                {
                    PlaceMultiTileTerrain(
                        x, y, 2, imageBase + 8 + (random[tile] & 3), runtimeEdge, runtimeBitfields);
                }
                else
                {
                    images[tile] = checked((uint)(imageBase + (random[tile] & 7)));
                }
            }
        }
    }

    private bool IsUpdatableDune(int tile) =>
        (terrain[tile] & TerrainDune) != 0 &&
        (bitfields[tile] & 0x80) == 0 &&
        (terrain[tile] & 0x600) == 0;

    private bool CanPlaceTerrainFootprint(int x, int y, int size, uint expectedTerrain)
    {
        if (x + size > width || y + size > height)
            return false;

        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                int tile = TileIndex(x + dx, y + dy);
                // FUN_0047CAE0 compares this masked value exactly and requires
                // image_grid == 0 (the final argument is one).
                if ((terrain[tile] & ClearLandBlockingMask) != expectedTerrain || images[tile] != 0)
                    return false;
            }
        }
        return true;
    }

    private void PlaceMultiTileTerrain(
        int x,
        int y,
        int size,
        int imageId,
        byte[] runtimeEdge,
        byte[] runtimeBitfields)
    {
        (int anchorX, int anchorY) = orientationIndex switch
        {
            0 => (0, size - 1),
            1 => (0, 0),
            2 => (size - 1, 0),
            _ => (size - 1, size - 1)
        };

        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                int tile = TileIndex(x + dx, y + dy);
                images[tile] = checked((uint)imageId);
                runtimeBitfields[tile] = (byte)((runtimeBitfields[tile] & 0xe0) | (size - 1));
                runtimeEdge[tile] = (byte)(dx + (8 * dy));
                if (dx == anchorX && dy == anchorY)
                    runtimeEdge[tile] |= 0x40;
            }
        }
    }

    [OriginalAddress(0x004767D0, "FUN_004767d0")]
    private void BuildEmptyLand()
    {
        ForEachTile(tile =>
        {
                if (!IsClear(tile) || images[tile] != 0)
                    return;
                int x=(tile-origin)%LegacyCityMapSnapshot.MapLength,y=(tile-origin)/LegacyCityMapSnapshot.MapLength;
                int imageBase = GlobalGroupStart((bitfields[tile] & AlternateTerrainFlag) != 0 ? 7 : 10);
                if (CanPlace(x, y, 4)) Place(x, y, 4, imageBase + 42);
                else if (CanPlace(x, y, 3)) Place(x, y, 3, imageBase + 24 + (9 * (random[tile] & 1)));
                else if (CanPlace(x, y, 2)) Place(x, y, 2, imageBase + 8 + (4 * (random[tile] & 3)));
                else Place(x, y, 1, imageBase + (random[tile] & 7));
        });

        // FUN_004767D0 performs this as the second half of its empty-land
        // pass. Bit 0x80 marks vegetation generated from distance to water by
        // FUN_00487450. A floodplain neighbour selects the special fringe;
        // all other cells use vegetation_growth and their eight neighbours.
        int floodplainBase = GlobalGroupStart(31);
        int sparseGrassBase = GlobalGroupStart(46);
        int blockedGrassBase = GlobalGroupStart(52);
        int interiorGrassBase = GlobalGroupStart(53);
        int grassEdgeBase = GlobalGroupStart(56);
        int animatedGrassBase = GlobalGroupStart(64);
        int animatedGrassFrameCount = checked(
            terrainGroupStarts[65] - terrainGroupStarts[64]);
        if (animatedGrassFrameCount <= 0)
            throw new InvalidDataException("Terrain group 64 has no wet-grass frames.");
        LegacyTerrainImageContext fringeContext = LegacyTerrainImageContext.DeepWater();
        ForEachTile(tile =>
        {
            uint flags = terrain[tile];
            if ((flags & TerrainGroundwaterGrass) == 0 ||
                (flags & 0x22310803u) != 0)
                return;

            if (HasNeighbor(tile, TerrainFloodplain))
            {
                Span<byte> neighbors = stackalloc byte[8];
                FillDirectNeighbors(tile, TerrainFloodplain, neighbors);
                if (fringeContext.TryResolveSelection(
                        neighbors, orientationIndex,
                        out LegacyTerrainImageContext.Selection selection))
                {
                    images[tile] = checked((uint)(floodplainBase + 48 +
                        selection.OrientationOffset));
                }
                return;
            }

            int stage = GrassStage(vegetationGrowth[tile]);
            if (stage < 11)
            {
                images[tile] = checked((uint)(sparseGrassBase + stage +
                    ((random[tile] % 3) * 12)));
                return;
            }

            if (stage >= 128)
            {
                // The context table can return orientation codes 12 and 13,
                // while the installed terrain group 64 contains only twelve
                // images. Keep the selection inside the SG3 group instead of
                // leaking into the unrelated blue group 65.
                int frame = (stage & 0x7f) % animatedGrassFrameCount;
                images[tile] = checked((uint)(animatedGrassBase + frame));
                return;
            }

            int imageBase;
            if ((flags & 0x10000000u) == 0 && HasBlockedGrassNeighbor(tile))
                imageBase = blockedGrassBase;
            else
                imageBase = HasEightMatureGrassNeighbors(tile)
                    ? interiorGrassBase
                    : grassEdgeBase;
            images[tile] = checked((uint)(imageBase + (random[tile] % 12)));
        });
    }

    private static int GrassStage(byte growth) =>
        growth <= 100 ? (growth * 12) / 96 : growth;

    private bool HasBlockedGrassNeighbor(int tile)
    {
        foreach (int offset in DirectNeighborOffsets)
        {
            int neighbor = tile + offset;
            if ((uint)neighbor >= terrain.Length ||
                (terrain[neighbor] & GrassNeighborBlockingMask) != 0)
                return true;
        }
        return false;
    }

    private bool HasEightMatureGrassNeighbors(int tile)
    {
        foreach (int offset in DirectNeighborOffsets)
        {
            int neighbor = tile + offset;
            if ((uint)neighbor >= terrain.Length ||
                GrassStage((byte)Math.Min(vegetationGrowth[neighbor], (byte)100)) != 11)
                return false;

            uint flags = terrain[neighbor];
            if ((flags & 0x10801u) != 0)
                return false;
            if ((flags & 0x10000000u) != 0)
                continue;

            foreach (int nestedOffset in DirectNeighborOffsets)
            {
                int nestedNeighbor = neighbor + nestedOffset;
                if ((uint)nestedNeighbor >= terrain.Length ||
                    (terrain[nestedNeighbor] & GrassNeighborBlockingMask) != 0)
                    return false;
            }
        }
        return true;
    }

    [OriginalAddress(0x004777C0, "FUN_004777c0")]
    [OriginalAddress(0x00479E70, "FUN_00479e70")]
    [OriginalAddress(0x00484D70, "FUN_00484d70")]
    private void BuildFloodplain()
    {
        int imageBase = GlobalGroupStart(31);
        LegacyTerrainImageContext context = LegacyTerrainImageContext.Floodplain();
        LegacyTerrainImageContext waterSouthContext = LegacyTerrainImageContext.FloodplainWaterSouth();
        ForEachTile(tile =>
        {
            uint flags = terrain[tile];
            // FUN_00479E70 tests 0x148 here. WATER is intentionally allowed:
            // the later FUN_0047A220 water pass replaces flooded cells.
            if ((flags & TerrainFloodplain) == 0 ||
                (flags & (TerrainBuilding | TerrainRoad | TerrainCanal)) != 0)
                return;
            Span<byte> neighbors = stackalloc byte[8];
            FillInverseNeighbors(tile, TerrainFloodplain, neighbors);

            // FUN_0047DDD0 adds WATER as bit 1, then FUN_00479E70 clears the
            // four diagonal values before FUN_00484D70 performs its lookup.
            neighbors[1] = 0;
            neighbors[3] = 0;
            neighbors[5] = 0;
            neighbors[7] = 0;
            int waterMask = CardinalMask(tile, TerrainWater);
            ReadOnlySpan<byte> tableByWaterMask = [0, 1, 3, 2, 5, 1, 4, 2, 6, 7, 1, 1, 5, 1, 1, 1];
            LegacyTerrainImageContext selectedContext = tableByWaterMask[waterMask] == 5
                ? waterSouthContext
                : context;
            // FUN_00479E70 explicitly clears the directional result after
            // this lookup. Clear floodplain therefore keeps only the cycling
            // item; the directional fringe belongs to the adjacent dry tile.
            int variant = selectedContext.TryResolveSelection(
                neighbors, orientationIndex, out LegacyTerrainImageContext.Selection selection)
                ? selection.Variant
                : 0;
            int growth = Math.Clamp(floodplainGrowthStage(tile), 0, 5);
            int fertilityBand = Math.Min(soilFertility[tile] / 25, 3);
            int alternate = random[tile] & 1;
            // FUN_00479E70 computes the same value through
            // trunc(fertility * .01 * 8), clears bit zero, restores the
            // random-grid parity and multiplies the result by six. Written
            // this way the four soil-colour bands and their two vegetation
            // variants remain explicit for future building underlays.
            int fertilityVariant = (fertilityBand * 2 + alternate) * 6;
            images[tile] = checked((uint)(imageBase + variant + fertilityVariant + growth));
        });
    }

    [OriginalAddress(0x00475CC0, "FUN_00475cc0")]
    [OriginalAddress(0x0047A220, "FUN_0047a220")]
    private void BuildWater()
    {
        // FUN_0047A220 uses two independent registry entries. Group 5
        // (SPR_WATER) is the shoreline family addressed by non-zero context
        // offsets. A zero offset selects DAT_00F56BD4, terrain group 19
        // (SPR_FP_COAST), whose first frames are the open-water fill. Group 6
        // is SPR_CRACKS and belongs exclusively to the earthquake path.
        int waterShoreBase = GlobalGroupStart(5);
        int waterCenterBase = GlobalGroupStart(19);
        int deepWaterBase = GlobalGroupStart(61);
        LegacyTerrainImageContext waterContext = LegacyTerrainImageContext.Water();
        LegacyTerrainImageContext deepContext = LegacyTerrainImageContext.DeepWater();
        ForEachTile(tile =>
        {
            uint flags = terrain[tile];
            if ((flags & TerrainWater) == 0 || (flags & TerrainBuilding) != 0)
                return;

            Span<byte> waterNeighbors = stackalloc byte[8];
            FillInverseNeighbors(tile, TerrainWater, waterNeighbors);
            LegacyTerrainImageContext.Selection waterSelection = default;
            waterContext.TryResolveRandomSelection(
                waterNeighbors, orientationIndex, random[tile], reverse: false,
                out waterSelection);

            if ((flags & TerrainFloodplain) == 0 && HasNeighbor(tile, TerrainFloodplain))
            {
                images[tile] = checked((uint)ResolveFloodplainWaterImage(tile, waterSelection));
            }
            else if ((flags & TerrainDeepWater) != 0)
            {
                Span<byte> neighbors = stackalloc byte[8];
                FillInverseNeighbors(tile, TerrainDeepWater, neighbors);
                // FUN_00484EE0 resets the table before every deep-water
                // lookup. FUN_0047A220 then addresses six 15-frame rows.
                deepContext.Reset();
                LegacyTerrainImageContext.Selection deepSelection = new(14, 0, 0);
                deepContext.TryResolveRandomSelection(
                    neighbors, orientationIndex, random[tile], reverse: true, out deepSelection);
                images[tile] = checked((uint)(deepWaterBase +
                    (deepSelection.Variant * 15) + deepSelection.OrientationOffset));
            }
            else
            {
                // A flooded floodplain tile with a shoreline-shaped context
                // still uses the open-water registry entry in FUN_0047A220.
                // Other shoreline shapes use group 5; a zero offset uses the
                // leading open-water frames of group 19.
                bool floodedFloodplainCenter =
                    (flags & TerrainFloodplain) != 0 && waterSelection.OrientationOffset >= 8;
                int imageBase = floodedFloodplainCenter || waterSelection.OrientationOffset == 0
                    ? waterCenterBase
                    : waterShoreBase;
                images[tile] = checked((uint)(imageBase + waterSelection.LinearOffset));
            }
        });
    }

    /// <summary>
    /// FUN_0047A220 uses the original row-node +0x18 flags for the bank
    /// junctions. Its ordinary floodplain coast is DAT_00F56BD4 (group 19),
    /// not the unrelated group 33 + 209 family.
    /// </summary>
    [OriginalAddress(0x0047A220, "FUN_0047a220")]
    private int ResolveFloodplainWaterImage(int tile, LegacyTerrainImageContext.Selection water)
    {
        Span<bool> helpers = stackalloc bool[8];
        Span<bool> floodplain = stackalloc bool[8];
        int count = 0;
        for (int direction = 0; direction < 8; direction++)
        {
            int neighbor = tile + DirectNeighborOffsets[direction];
            helpers[direction] = floodplainTopology.IsBankHelper(neighbor);
            floodplain[direction] = HasTerrain(neighbor, TerrainFloodplain);
            if (floodplain[direction]) count++;
        }
        int rotation = orientationIndex * 2;
        int first = water.OrientationOffset switch
        {
            8 => 4, 12 => 6, 16 => 0, 20 => 2,
            24 or 28 => 6, 32 or 36 => 2, _ => -1
        };
        int second = water.OrientationOffset switch
        {
            24 or 36 => 4, 28 or 32 => 0, _ => -1
        };
        int bankDirection = first >= 0 && helpers[(rotation + first) & 7] ? first :
            second >= 0 && helpers[(rotation + second) & 7] ? second : -1;
        if (bankDirection < 0)
            return GlobalGroupStart(19) + water.LinearOffset;

        int thickness = water.OrientationOffset >= 24 ? Math.Min(count, 2) * 2 : 0;
        if (thickness == 2 && (floodplain[0] || floodplain[2] || floodplain[4] || floodplain[6]))
            thickness = 4;
        int image = GlobalGroupStart(31) + (((bankDirection - 4) & 7) + 20) * 3 + thickness;
        if (floodplain[(rotation + bankDirection + 1) & 7] ||
            floodplain[(rotation + bankDirection + 2) & 7])
            image++;
        return image;
    }

    private void ApplySpaceOutsidePlayableArea()
    {
        uint spaceImage = checked((uint)GlobalGroupStart(1));
        ForEachTile(tile =>
        {
            if (!IsInsidePlayableArea(tile))
                images[tile] = spaceImage;
        });
    }

    private void BuildMarsh()
    {
        int imageBase = GlobalGroupStart(11);
        LegacyTerrainImageContext context = LegacyTerrainImageContext.Water();
        ForEachTile(tile =>
        {
            if ((terrain[tile] & TerrainMarsh) == 0 || (terrain[tile] & TerrainBuilding) != 0)
                return;
            Span<byte> neighbors = stackalloc byte[8];
            FillInverseNeighbors(tile, TerrainMarsh, neighbors);
            // FUN_0047AB10 passes random_grid to FUN_00484BE0. Cycling the
            // matched record instead selects different shoreline variants;
            // several have a visibly narrower dark reed footprint.
            int offset = context.TryResolveRandomSelection(
                    neighbors, orientationIndex, random[tile], reverse: false,
                    out LegacyTerrainImageContext.Selection selection)
                ? selection.LinearOffset
                : random[tile] & 7;
            images[tile] = checked((uint)(imageBase + offset));
        });
    }

    [OriginalAddress(0x0047B670, "FUN_0047b670")]
    private void BuildRoads()
    {
        // FUN_0047B670 uses DAT_00F56C94 and the 17 records at 0x005CC268
        // for ordinary roads. DAT_00F56C44/group offset 0 is the paved-road
        // family selected only after desirability (or fountain range) rises.
        int pavedImageBase = GlobalGroupStart(33);
        int dirtImageBase = pavedImageBase + 49;
        int floodplainImageBase = GlobalGroupStart(51);
        ForEachTile(tile =>
        {
            if ((terrain[tile] & TerrainRoad) == 0 ||
                (terrain[tile] & (TerrainWater | TerrainBuilding | TerrainCanal)) != 0)
            {
                return;
            }

            Span<byte> neighbors = stackalloc byte[8];
            FillDirectNeighbors(tile, TerrainRoad, neighbors);
            int desirabilityValue = unchecked((sbyte)desirability[tile]);
            bool floodplain = (terrain[tile] & TerrainFloodplain) != 0;
            bool paved = !floodplain &&
                (desirabilityValue > 4 ||
                 (desirabilityValue > 0 && (terrain[tile] & TerrainFountainRange) != 0));
            LegacyTerrainImageContext context = paved ? PavedRoadContext : DirtRoadContext;
            int offset = context.TryResolve(neighbors, orientationIndex, out int resolved)
                ? resolved
                : 12;
            int transition = !floodplain ? ResolveFloodplainRoadTransition(tile) : -1;
            if (transition >= 0)
                images[tile] = checked((uint)(GlobalGroupStart(31) + transition));
            else
            {
                int imageBase = floodplain
                    ? floodplainImageBase
                    : paved ? pavedImageBase : dirtImageBase;
                images[tile] = checked((uint)(imageBase + offset));
            }
        });
    }

    [OriginalAddress(0x0047B670, "FUN_0047b670")]
    public int ResolveRoadPreviewImage(int tile, IReadOnlySet<int> previewRoads)
    {
        ArgumentNullException.ThrowIfNull(previewRoads);
        if ((uint)tile >= (uint)terrain.Length)
            throw new ArgumentOutOfRangeException(nameof(tile));

        int pavedImageBase = GlobalGroupStart(33);
        int dirtImageBase = pavedImageBase + 49;
        int floodplainImageBase = GlobalGroupStart(51);
        Span<byte> neighbors = stackalloc byte[8];
        for (int index = 0; index < DirectNeighborOffsets.Length; index++)
        {
            int neighbor = tile + DirectNeighborOffsets[index];
            neighbors[index] = (byte)((uint)neighbor < (uint)terrain.Length &&
                (((terrain[neighbor] & TerrainRoad) != 0) || previewRoads.Contains(neighbor))
                    ? 1
                    : 0);
        }

        int desirabilityValue = unchecked((sbyte)desirability[tile]);
        bool floodplain = (terrain[tile] & TerrainFloodplain) != 0;
        bool paved = !floodplain &&
            (desirabilityValue > 4 ||
             (desirabilityValue > 0 && (terrain[tile] & TerrainFountainRange) != 0));
        LegacyTerrainImageContext context = paved
            ? PavedRoadContext
            : DirtRoadContext;
        int offset = context.TryResolve(neighbors, orientationIndex, out int resolved)
            ? resolved
            : 12;
        int transition = !floodplain ? ResolveFloodplainRoadTransition(tile) : -1;
        if (transition >= 0)
            return checked(GlobalGroupStart(31) + transition);
        int imageBase = floodplain
            ? floodplainImageBase
            : paved ? pavedImageBase : dirtImageBase;
        return checked(imageBase + offset);
    }

    /// <summary>
    /// FUN_0051C5F0 converts four directional frames on the adjacent dry
    /// groundwater-grass fringe into matching road transitions. Clear
    /// floodplain roads instead fall back to the SPR_FP_ROAD context family.
    /// </summary>
    [OriginalAddress(0x0051C5F0, "FUN_0051c5f0")]
    private int ResolveFloodplainRoadTransition(int tile)
    {
        if ((terrain[tile] & TerrainGroundwaterGrass) == 0 ||
            !HasNeighbor(tile, TerrainFloodplain))
            return -1;

        Span<byte> neighbors = stackalloc byte[8];
        FillDirectNeighbors(tile, TerrainFloodplain, neighbors);
        if (!LegacyTerrainImageContext.DeepWater().TryResolveSelection(
                neighbors, orientationIndex,
                out LegacyTerrainImageContext.Selection selection))
            return -1;

        return (48 + selection.OrientationOffset) switch
        {
            0x30 => 0x54,
            0x32 => 0x55,
            0x34 => 0x56,
            0x36 => 0x57,
            _ => -1
        };
    }

    public bool IsRoadTile(int tile) =>
        (uint)tile < (uint)terrain.Length && (terrain[tile] & TerrainRoad) != 0;

    /// <summary>
    /// Cells whose visible surface belongs to a mutable gameplay subsystem.
    /// Roads reconnect dynamically; canals change water state; buildings and
    /// farms are submitted after the terrain-only snapshot.
    /// </summary>
    [OriginalAddress(0x00479E70, "FUN_00479e70")]
    [OriginalAddress(0x0047B670, "FUN_0047b670")]
    public bool IsDynamicSurfaceTile(int tile) =>
        (uint)tile < (uint)terrain.Length &&
        (terrain[tile] & (TerrainBuilding | TerrainRoad | TerrainCanal |
            TerrainWater | TerrainFloodplain | TerrainRubble)) != 0;

    public bool IsFloodSurfaceTile(int tile) =>
        (uint)tile < (uint)terrain.Length &&
        (terrain[tile] & (TerrainWater | TerrainFloodplain | TerrainRubble)) != 0;

    [RewriteOnly("Exposes DAT_00F56BE4, terrain group 21, for the original RGB565 placement mask")]
    public int ResolvePlacementFootprintImage() => GlobalGroupStart(21);

    [OriginalAddress(0x00484F40, "FUN_00484f40")]
    private int CardinalMask(int tile, uint flag)
    {
        int mask = 0;
        if (HasTerrain(tile - LegacyCityMapSnapshot.MapLength, flag)) mask |= 1;
        if (HasTerrain(tile + 1, flag)) mask |= 2;
        if (HasTerrain(tile + LegacyCityMapSnapshot.MapLength, flag)) mask |= 4;
        if (HasTerrain(tile - 1, flag)) mask |= 8;
        return mask;
    }

    private bool HasTerrain(int tile, uint flag) =>
        IsInsideMission(tile) && (terrain[tile] & flag) != 0;

    private bool HasNeighbor(int tile, uint flag)
    {
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
        foreach (int offset in offsets)
            if (HasTerrain(tile + offset, flag))
                return true;
        return false;
    }

    [OriginalAddress(0x00484F00, "FUN_00484f00")]
    private void FillInverseNeighbors(int tile, uint flag, Span<byte> neighbors)
    {
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
        for (int direction = 0; direction < offsets.Length; direction++)
            neighbors[direction] = HasTerrain(tile + offsets[direction], flag) ? (byte)0 : (byte)1;
    }

    [OriginalAddress(0x0047DB00, "FUN_0047db00")]
    [OriginalAddress(0x0047DDD0, "FUN_0047ddd0")]
    private void FillDirectNeighbors(int tile, uint flag, Span<byte> neighbors)
    {
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
        for (int direction = 0; direction < offsets.Length; direction++)
            neighbors[direction] = HasTerrain(tile + offsets[direction], flag) ? (byte)1 : (byte)0;
    }

    private bool IsInsideMission(int tile)
    {
        int delta = tile - origin;
        if (delta < 0)
            return false;
        int y = delta / LegacyCityMapSnapshot.MapLength;
        int x = delta % LegacyCityMapSnapshot.MapLength;
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    /// <summary>
    /// Returns whether a grid cell belongs to the playable isometric diamond,
    /// excluding the rectangular scenario padding rendered as SPR_SPACE.
    /// </summary>
    [RewriteOnly("Builder-facing wrapper around FUN_0046F930 shared map geometry")]
    public bool IsInsidePlayableArea(int tile) =>
        LegacyPlayableMapArea.Contains(tile, width, height, origin);

    [RewriteOnly("Caches FUN_0046F930 for camera bounds without relying on SPR_SPACE image ids")]
    private byte[] BuildPlayableAreaGrid()
    {
        byte[] result = new byte[LegacyCityMapSnapshot.TileCount];
        for (int tile = 0; tile < result.Length; tile++)
        {
            if (IsInsidePlayableArea(tile))
                result[tile] = 1;
        }
        return result;
    }

    private void ForEachTile(Action<int> action)
    {
        if(localRefreshTiles is {} selected){foreach(int tile in selected)action(tile);return;}
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                action(TileIndex(x, y));
    }

    private byte[] SerializeImages()
    {
        byte[] result = new byte[LegacyCityMapSnapshot.ImageGridLength];
        for (int i = 0; i < images.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * sizeof(uint)), images[i]);
        return result;
    }

    private bool CanPlace(int x, int y, int size)
    {
        if (x + size > width || y + size > height)
            return false;
        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                int tile = TileIndex(x + dx, y + dy);
                if (!IsClear(tile) || images[tile] != 0)
                    return false;
            }
        }
        return true;
    }

    private void Place(int x, int y, int size, int firstImageId)
    {
        int frame = 0;
        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
                images[TileIndex(x + dx, y + dy)] = checked((uint)(firstImageId + frame++));
        }
    }

    private bool IsClear(int tile) => (terrain[tile] & ClearLandBlockingMask) == 0;
    private int TileIndex(int x, int y) => origin + x + (y * LegacyCityMapSnapshot.MapLength);

    private int GlobalGroupStart(int group)
    {
        int local = terrainGroupStarts[group];
        if (local <= 0)
            throw new InvalidDataException($"Terrain SG3 group {group} has no image start.");
        return TerrainGlobalBase + local;
    }

    private static ReadOnlyMemory<byte> GetChunk(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        string name) =>
        chunks.TryGetValue(name, out ReadOnlyMemory<byte> value)
            ? value
            : throw new InvalidDataException($"The mission has no {name} chunk.");
}
