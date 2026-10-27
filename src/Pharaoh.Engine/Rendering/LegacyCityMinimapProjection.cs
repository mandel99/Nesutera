using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

/// <summary>
/// One original radar sprite for every sampled map tile. The sprites are the
/// two-by-one-pixel entries stored under radar.bmp in Pharaoh_General.sg3.
/// </summary>
public sealed class LegacyCityMinimapProjection
{
    public const int TileColumns = 73;
    public const int TileRows = 111;
    public const int PixelWidth = TileColumns * 2;
    public const int PixelHeight = TileRows;

    public const int BlackImage = 2022;
    private const int ClearLandBase = 1760;
    private const int WaterBase = 1768;
    private const int VegetationBase = 1772;
    private const int RockBase = 1778;
    private const int FertileLandBase = 1782;
    private const int StructureImage = 1786;
    private const int BuildingImageBase = 1787;
    private const int BuildingCategoryBase = 1946;
    private const int BuildingType90Image = 2011;
    private const int CanalImage = 2012;
    private const int DuneBase = 2014;
    private const int SpecialTerrainBase = 1946;

    private const uint TerrainTree = 0x00000001;
    private const uint TerrainRock = 0x00000002;
    private const uint TerrainWater = 0x00000004;
    private const uint TerrainBuilding = 0x00000008;
    private const uint TerrainMarshLike = 0x00000010;
    private const uint TerrainSpecial50 = 0x00000020;
    private const uint TerrainRoad = 0x00000040;
    private const uint TerrainCanal = 0x00000100;
    private const uint TerrainRockLike = 0x00000200;
    private const uint TerrainMeadow = 0x00000800;
    private const uint TerrainSpecial60 = 0x00004000;
    private const uint TerrainFloodplain = 0x00010000;
    private const uint TerrainOutsideMap = 0x00080000;
    private const uint TerrainDune = 0x02000000;
    private const uint TerrainStructureLike = 0x40000000;
    private const uint TerrainSpecial55 = 0x10000000;

    private readonly int[] localImageIndices;
    private readonly int[] tileIndices;
    private readonly LegacyMinimapDrawCommand[] drawCommands;

    [RewriteOnly("Stores the sampled executable radar images and their corresponding map tiles")]
    private LegacyCityMinimapProjection(
        int sourceColumn,
        int sourceRow,
        int[] localImageIndices,
        int[] tileIndices,
        LegacyMinimapDrawCommand[] drawCommands,
        LegacyMinimapCameraRectangle cameraRectangle)
    {
        SourceColumn = sourceColumn;
        SourceRow = sourceRow;
        this.localImageIndices = localImageIndices;
        this.tileIndices = tileIndices;
        this.drawCommands = drawCommands;
        CameraRectangle = cameraRectangle;
    }

    public int SourceColumn { get; }
    public int SourceRow { get; }
    public LegacyMinimapCameraRectangle CameraRectangle { get; }
    public IReadOnlyList<LegacyMinimapDrawCommand> DrawCommands => drawCommands;

    [OriginalAddress(0x0043E810, "FUN_0043e810")]
    public int GetLocalImageIndex(int column, int row)
    {
        if ((uint)column >= TileColumns || (uint)row >= TileRows)
            throw new ArgumentOutOfRangeException(nameof(column));
        return localImageIndices[(row * TileColumns) + column];
    }

    /// <summary>
    /// Converts one minimap pixel to its sampled map tile. Each radar sprite is
    /// two pixels wide; odd rows begin one pixel to the left, as in Pharaoh.exe.
    /// Unlike the executable's scanning hit test, this lookup is constant-time.
    /// </summary>
    [OriginalAddress(0x0043EF10, "FUN_0043ef10")]
    public bool TryGetTileIndexAtPixel(int x, int y, out int tileIndex)
    {
        tileIndex = LegacyMapOrientationIndex.InvalidTileIndex;
        if ((uint)x >= PixelWidth || (uint)y >= PixelHeight)
            return false;

        int column = (x + (y & 1)) / 2;
        if ((uint)column >= TileColumns)
            return false;

        tileIndex = tileIndices[(y * TileColumns) + column];
        return tileIndex != LegacyMapOrientationIndex.InvalidTileIndex;
    }

    /// <summary>
    /// Reconstructs the normal gameplay branch of Pharaoh.exe FUN_0043E810.
    /// The four padded source rows/columns used by the executable for tall
    /// objects are intentionally omitted here; the terrain radar sprites are
    /// exactly two pixels wide and one pixel high.
    /// </summary>
    [OriginalAddress(0x0043E670, "FUN_0043e670")]
    [OriginalAddress(0x0043E810, "FUN_0043e810")]
    public static LegacyCityMinimapProjection Build(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        LegacyCityViewState view)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(view);

        ReadOnlySpan<byte> terrainBytes = RequiredChunk(chunks, "terrain_grid").Span;
        ReadOnlySpan<byte> random = RequiredChunk(chunks, "random_grid").Span;
        if (terrainBytes.Length != LegacyCityMapSnapshot.ImageGridLength ||
            random.Length != LegacyCityMapSnapshot.TileCount)
        {
            throw new InvalidDataException("The legacy minimap grids have invalid lengths.");
        }

        chunks.TryGetValue("building_grid", out ReadOnlyMemory<byte> buildingGridMemory);
        chunks.TryGetValue("buildings", out ReadOnlyMemory<byte> buildingsMemory);
        chunks.TryGetValue("bitfields_grid", out ReadOnlyMemory<byte> bitfieldsMemory);
        chunks.TryGetValue("edge_grid", out ReadOnlyMemory<byte> edgeMemory);
        bool hasBuildingData = !buildingGridMemory.IsEmpty &&
            !buildingsMemory.IsEmpty && !bitfieldsMemory.IsEmpty && !edgeMemory.IsEmpty;
        ReadOnlySpan<byte> buildingGrid = buildingGridMemory.Span;
        ReadOnlySpan<byte> buildings = buildingsMemory.Span;
        ReadOnlySpan<byte> bitfields = bitfieldsMemory.Span;
        ReadOnlySpan<byte> edge = edgeMemory.Span;
        if (hasBuildingData &&
            (buildingGrid.Length != LegacyCityMapSnapshot.TileCount * sizeof(short) ||
             buildings.Length < 4000 * 264 ||
             bitfields.Length != LegacyCityMapSnapshot.TileCount ||
             edge.Length != LegacyCityMapSnapshot.TileCount))
        {
            throw new InvalidDataException("The legacy minimap building grids have invalid lengths.");
        }

        LegacyMapCameraBounds mapBounds = view.CameraBounds;
        int sourceColumn = SelectSourceColumn(
            mapBounds.MinimumX, mapBounds.MaximumX, view.CameraX, view.Viewport.VisibleColumns);
        int sourceRow = SelectSourceRow(
            mapBounds.MinimumY, mapBounds.MaximumY, view.CameraY, view.Viewport.VisibleRows);

        int[] images = new int[TileColumns * TileRows];
        int[] tiles = new int[TileColumns * TileRows];
        Array.Fill(images, BlackImage);
        Array.Fill(tiles, LegacyMapOrientationIndex.InvalidTileIndex);
        List<LegacyMinimapDrawCommand> commands = new(TileColumns * TileRows);
        for (int row = 0; row < TileRows; row++)
        {
            int lookupRow = sourceRow + row;
            int maximumLookupRow = (mapBounds.MaximumY - 1) & ~1;
            for (int column = 0; column < TileColumns; column++)
            {
                int lookupColumn = sourceColumn + column;
                if (lookupColumn < mapBounds.MinimumX || lookupColumn > mapBounds.MaximumX ||
                    lookupRow < mapBounds.MinimumY || lookupRow > maximumLookupRow ||
                    !view.OrientationIndex.TryGetTileIndex(
                        lookupColumn, lookupRow, out int tileIndex))
                {
                    continue;
                }

                uint terrain = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    terrainBytes.Slice(tileIndex * sizeof(uint), sizeof(uint)));
                if ((terrain & TerrainOutsideMap) != 0)
                    continue;
                int projectionIndex = (row * TileColumns) + column;
                int destinationX = -(row & 1) + column * 2;
                int buildingId = hasBuildingData && (terrain & TerrainBuilding) != 0
                    ? System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(
                        buildingGrid.Slice(tileIndex * sizeof(short), sizeof(short)))
                    : 0;
                if (buildingId != 0 && (edge[tileIndex] & 0x40) == 0)
                {
                    // FUN_0043E810 deliberately leaves covered footprint cells
                    // untouched; their anchor's larger radar sprite covers them.
                    images[projectionIndex] = BlackImage;
                }
                else if (buildingId != 0 &&
                    TryAppendBuildingCommands(tileIndex, destinationX, row, terrain,
                        buildingId, buildings, bitfields, commands, out int buildingImage))
                {
                    images[projectionIndex] = buildingImage;
                }
                else
                {
                    int terrainImage = SelectTerrainImage(terrain, random[tileIndex]);
                    images[projectionIndex] = terrainImage;
                    commands.Add(new LegacyMinimapDrawCommand(terrainImage, destinationX, row));
                }
                tiles[projectionIndex] = tileIndex;
            }
        }

        int cameraX = Math.Max(0, (view.CameraX - sourceColumn) * 2);
        int cameraY = view.CameraY - sourceRow;
        LegacyMinimapCameraRectangle camera = new(
            cameraX,
            cameraY,
            view.Viewport.VisibleColumns * 2,
            view.Viewport.VisibleRows);
        return new LegacyCityMinimapProjection(
            sourceColumn, sourceRow, images, tiles, commands.ToArray(), camera);
    }

    /// <summary>
    /// Reconstructs the normal building branch of FUN_00439980. The low nibble
    /// of bitfields_grid selects the radar footprint sprite; value five is the
    /// executable's four-part 3x3 composition.
    /// </summary>
    [OriginalAddress(0x00439980, "FUN_00439980")]
    private static bool TryAppendBuildingCommands(
        int tileIndex,
        int x,
        int y,
        uint terrain,
        int buildingId,
        ReadOnlySpan<byte> buildings,
        ReadOnlySpan<byte> bitfields,
        List<LegacyMinimapDrawCommand> commands,
        out int displayedImage)
    {
        displayedImage = 0;
        if ((terrain & TerrainBuilding) == 0)
            return false;
        if ((uint)buildingId >= 4000 || buildingId == 0)
            return false;

        int recordOffset = buildingId * 264;
        if (buildings[recordOffset] == 0)
            return false;

        int image = BuildingImageBase;
        if (buildings[recordOffset + 5] == 0)
        {
            BuildingType type = (BuildingType)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(
                buildings.Slice(recordOffset + 16, sizeof(short)));
            if (type == BuildingType.WaterLiftCostModel)
                image = BuildingType90Image;
            else
            {
                int category = GetBuildingPaletteCategory((int)type);
                if (category < 0)
                    return false;
                image = BuildingCategoryBase + category * 5;
            }
        }

        int footprint = bitfields[tileIndex] & 0x0f;
        if (footprint <= 4)
        {
            displayedImage = image + footprint;
            commands.Add(new LegacyMinimapDrawCommand(displayedImage, x, y - footprint));
            return true;
        }
        if (footprint != 5)
            return false;

        displayedImage = image + 2;
        commands.Add(new LegacyMinimapDrawCommand(displayedImage, x + 3, y - 5));
        commands.Add(new LegacyMinimapDrawCommand(displayedImage, x + 3, y + 1));
        commands.Add(new LegacyMinimapDrawCommand(displayedImage, x, y - 2));
        commands.Add(new LegacyMinimapDrawCommand(displayedImage, x + 6, y - 2));
        return true;
    }

    [OriginalAddress(0x0043E770, "FUN_0043e770")]
    public static int GetBuildingPaletteCategory(int type)
    {
        int tableValue = type switch
        {
            >= 0 and <= 4 or 9 or 37 or 48 or 50 or 52 or 56 or 80 or 88 or 89 or 91 or 93 or
                >= 96 and <= 99 or >= 116 and <= 135 or 138 or 139 or >= 163 and <= 166 or
                >= 174 and <= 176 or >= 197 and <= 198 or 214 or 223 => 0,
            5 or 82 or 83 or 137 => 1,
            7 or 8 or 70 or 71 or 76 or 90 or >= 102 and <= 105 or 115 or >= 190 and <= 194 or
                196 or 199 => 2,
            >= 72 and <= 75 or >= 100 and <= 101 or >= 106 and <= 111 or >= 113 and <= 114 or
                161 or 162 or >= 177 and <= 179 or 195 or 203 or 204 or 216 or 217 or 221 or
                224 or >= 231 and <= 233 => 3,
            >= 30 and <= 36 or 226 => 4,
            >= 60 and <= 69 or >= 140 and <= 160 or 209 or 211 or 212 => 5,
            51 or 53 => 6,
            46 or 47 or 49 or 180 or 206 => 7,
            55 or 81 or 136 or 167 => 8,
            >= 77 and <= 79 or >= 84 and <= 87 or 184 or >= 187 and <= 189 or 210 => 9,
            40 or 44 or 45 or 54 or >= 57 and <= 59 or 94 or 95 or 112 or 172 or 173 or
                181 or 182 or 185 or 186 or >= 200 and <= 202 or 205 => 10,
            38 or 39 or >= 41 and <= 43 or 92 => 11,
            183 or 207 or 208 or 213 or 215 or >= 218 and <= 220 or 222 or 225 or
                >= 227 and <= 230 or >= 234 and <= 236 => 12,
            6 or >= 168 and <= 171 => 13,
            >= 10 and <= 29 => 14,
            _ => 0
        };
        return tableValue - 1;
    }

    [OriginalAddress(0x0043E810, "FUN_0043e810")]
    private static int SelectTerrainImage(uint terrain, byte random)
    {
        if ((terrain & TerrainSpecial55) != 0)
            return SpecialTerrainBase + 55;
        if ((terrain & TerrainSpecial50) != 0)
            return SpecialTerrainBase + 50;
        if ((terrain & TerrainWater) != 0)
            return WaterBase + (random & 3);
        if ((terrain & (TerrainMarshLike | TerrainTree)) != 0)
            return VegetationBase + (random & 3);
        if ((terrain & TerrainRock) != 0)
            return RockBase + (random & 3);
        if ((terrain & TerrainDune) != 0)
            return DuneBase + (random & 3);
        if ((terrain & TerrainRockLike) != 0)
            return RockBase + (random & 3);
        if ((terrain & (TerrainStructureLike | TerrainRoad)) != 0)
            return StructureImage;
        if ((terrain & TerrainCanal) != 0)
            return CanalImage;
        if ((terrain & TerrainSpecial60) != 0)
            return SpecialTerrainBase + 60;
        if ((terrain & (TerrainMeadow | TerrainFloodplain)) != 0)
            return FertileLandBase + (random & 3);
        if ((terrain & 0x00040000) != 0)
            return VegetationBase + (random & 4);
        return ClearLandBase + (random & 7);
    }

    [OriginalAddress(0x0043E670, "FUN_0043e670")]
    private static int SelectSourceColumn(int minimum, int maximum, int camera, int visibleColumns)
    {
        if (maximum - minimum < TileColumns)
            return (LegacyMapOrientationIndex.ColumnCount - TileColumns) / 2;
        return Math.Clamp(camera + (visibleColumns / 2) - (TileColumns / 2),
            minimum, maximum - TileColumns);
    }

    [OriginalAddress(0x0043E670, "FUN_0043e670")]
    private static int SelectSourceRow(int minimum, int maximum, int camera, int visibleRows)
    {
        if (maximum - minimum < TileRows)
            return ((LegacyMapOrientationIndex.RowCount - TileRows) / 2) & ~1;
        int selected = (camera + (visibleRows / 2) - (TileRows / 2)) & ~1;
        return Math.Clamp(selected, minimum & ~1, (maximum - TileRows) & ~1);
    }

    [RewriteOnly("Validates named chunks supplied by the reconstructed mission loader")]
    private static ReadOnlyMemory<byte> RequiredChunk(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        string name) =>
        chunks.TryGetValue(name, out ReadOnlyMemory<byte> value)
            ? value
            : throw new InvalidDataException($"The mission has no {name} chunk.");
}

[RewriteOnly("Immutable value used to pass the original radar camera outline to the desktop renderer")]
public readonly record struct LegacyMinimapCameraRectangle(int X, int Y, int Width, int Height);

[RewriteOnly("Carries an original radar sprite and its destination through the renderer boundary")]
public readonly record struct LegacyMinimapDrawCommand(int ImageIndex, int X, int Y);
