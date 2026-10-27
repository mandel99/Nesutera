using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

/// <summary>
/// Immutable, byte-exact view of the 228 by 228 image grid consumed by
/// Pharaoh.exe FUN_00430530 and FUN_00430ef0.
/// </summary>
public sealed class LegacyCityMapSnapshot
{
    public const int MapLength = 228;
    public const int TileCount = MapLength * MapLength;
    public const int ImageGridLength = TileCount * sizeof(uint);

    private readonly ReadOnlyMemory<byte> imageGrid;
    private readonly ReadOnlyMemory<byte> edgeGrid;
    private readonly ReadOnlyMemory<byte> bitfieldsGrid;
    private readonly ReadOnlyMemory<byte> playableAreaGrid;

    public LegacyCityMapSnapshot(ReadOnlyMemory<byte> imageGrid) : this(imageGrid, default, default, default)
    {
    }

    public LegacyCityMapSnapshot(
        ReadOnlyMemory<byte> imageGrid,
        ReadOnlyMemory<byte> edgeGrid,
        ReadOnlyMemory<byte> bitfieldsGrid) : this(imageGrid, edgeGrid, bitfieldsGrid, default)
    {
    }

    public LegacyCityMapSnapshot(
        ReadOnlyMemory<byte> imageGrid,
        ReadOnlyMemory<byte> edgeGrid,
        ReadOnlyMemory<byte> bitfieldsGrid,
        ReadOnlyMemory<byte> playableAreaGrid)
    {
        if (imageGrid.Length != ImageGridLength)
        {
            throw new ArgumentException(
                $"The legacy image grid must contain exactly {ImageGridLength} bytes.",
                nameof(imageGrid));
        }
        if (!edgeGrid.IsEmpty && edgeGrid.Length != TileCount)
            throw new ArgumentException($"The legacy edge grid must contain exactly {TileCount} bytes.", nameof(edgeGrid));
        if (!bitfieldsGrid.IsEmpty && bitfieldsGrid.Length != TileCount)
            throw new ArgumentException($"The legacy bitfields grid must contain exactly {TileCount} bytes.", nameof(bitfieldsGrid));
        if (!playableAreaGrid.IsEmpty && playableAreaGrid.Length != TileCount)
            throw new ArgumentException($"The legacy playable-area grid must contain exactly {TileCount} bytes.", nameof(playableAreaGrid));

        this.imageGrid = imageGrid.ToArray();
        this.edgeGrid = edgeGrid.ToArray();
        this.bitfieldsGrid = bitfieldsGrid.ToArray();
        this.playableAreaGrid = playableAreaGrid.ToArray();
    }

    public byte GetEdgeFlags(int tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return edgeGrid.IsEmpty ? (byte)0x40 : edgeGrid.Span[tileIndex];
    }

    public byte GetDrawAnchor(int tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return bitfieldsGrid.IsEmpty ? (byte)0 : (byte)(bitfieldsGrid.Span[tileIndex] & 0x0f);
    }

    public int GetMultiTileSize(int tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return bitfieldsGrid.IsEmpty ? 1 : (bitfieldsGrid.Span[tileIndex] & 0x0f) + 1;
    }

    private static void ValidateTileIndex(int tileIndex)
    {
        if ((uint)tileIndex >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
    }

    public uint GetImageId(int tileIndex)
    {
        if ((uint)tileIndex >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));

        return BinaryPrimitives.ReadUInt32LittleEndian(
            imageGrid.Span.Slice(tileIndex * sizeof(uint), sizeof(uint)));
    }

    /// <summary>
    /// Applies an incremental image-grid update while retaining the original
    /// edge, draw-anchor and playable-area grids.
    /// </summary>
    [RewriteOnly("Preserves the executable's incremental road-image updates in an immutable snapshot")]
    public LegacyCityMapSnapshot WithImageIds(IReadOnlyDictionary<int, uint> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        byte[] updated = imageGrid.ToArray();
        foreach ((int tile, uint imageId) in replacements)
        {
            ValidateTileIndex(tile);
            BinaryPrimitives.WriteUInt32LittleEndian(
                updated.AsSpan(tile * sizeof(uint), sizeof(uint)), imageId);
        }
        return new LegacyCityMapSnapshot(updated, this);
    }

    [RewriteOnly("A fresh image buffer can share immutable topology buffers with its source snapshot")]
    private LegacyCityMapSnapshot(byte[] ownedImages,LegacyCityMapSnapshot topology)
    {
        imageGrid=ownedImages;edgeGrid=topology.edgeGrid;
        bitfieldsGrid=topology.bitfieldsGrid;playableAreaGrid=topology.playableAreaGrid;
    }

    [RewriteOnly("Reads the cached per-tile result of original playable-area predicate FUN_0046F930")]
    public bool IsInsidePlayableArea(int tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return playableAreaGrid.IsEmpty
            ? GetImageId(tileIndex) >= 6
            : playableAreaGrid.Span[tileIndex] != 0;
    }
}

/// <summary>
/// Port of the screen-row lookup built by Pharaoh.exe FUN_00430530. Invalid
/// EXE sentinel values are normalized to -1; valid entries retain the exact
/// original 228 by 228 tile index.
/// </summary>
public sealed class LegacyMapOrientationIndex
{
    public const int ColumnCount = 231;
    public const int RowCount = 457;
    public const int InvalidTileIndex = -1;

    private readonly int[] tileIndices;
    private readonly bool[] playableCells;

    private LegacyMapOrientationIndex(int orientation, int[] tileIndices, bool[] playableCells)
    {
        Orientation = orientation;
        this.tileIndices = tileIndices;
        this.playableCells = playableCells;
    }

    public int Orientation { get; }

    [OriginalAddress(0x00430480, "FUN_00430480")]
    [OriginalAddress(0x00430530, "FUN_00430530")]
    public static LegacyMapOrientationIndex Build(
        LegacyCityMapSnapshot map,
        int orientation)
    {
        ArgumentNullException.ThrowIfNull(map);
        ValidateOrientation(orientation);

        int[] lookup = new int[ColumnCount * RowCount];
        Array.Fill(lookup, InvalidTileIndex);
        bool[] playable = new bool[lookup.Length];

        Transform transform = Transform.ForOrientation(orientation);
        int screenRowStart = transform.ScreenRowStart;
        int doubledColumnStart = transform.DoubledColumnStart;

        for (int sourceY = 0; sourceY < LegacyCityMapSnapshot.MapLength; sourceY++)
        {
            int screenRow = screenRowStart;
            int doubledColumn = doubledColumnStart;
            int sourceRowOffset = sourceY * LegacyCityMapSnapshot.MapLength;

            for (int sourceX = 0; sourceX < LegacyCityMapSnapshot.MapLength; sourceX++)
            {
                int screenColumn = doubledColumn / 2;
                int tileIndex = sourceRowOffset + sourceX;
                if (screenRow >= 0 && screenRow < RowCount &&
                    screenColumn >= 0 && screenColumn < ColumnCount &&
                    map.GetImageId(tileIndex) >= 6)
                {
                    int lookupIndex = (screenRow * ColumnCount) + screenColumn;
                    lookup[lookupIndex] = tileIndex;
                    playable[lookupIndex] = map.IsInsidePlayableArea(tileIndex);
                }

                screenRow += transform.ScreenRowColumnStep;
                doubledColumn += transform.DoubledColumnColumnStep;
            }

            screenRowStart += transform.ScreenRowRowStep;
            doubledColumnStart += transform.DoubledColumnRowStep;
        }

        return new LegacyMapOrientationIndex(orientation, lookup, playable);
    }

    public bool TryGetTileIndex(int screenColumn, int screenRow, out int tileIndex)
    {
        if (screenColumn < 0 || screenColumn >= ColumnCount ||
            screenRow < 0 || screenRow >= RowCount)
        {
            tileIndex = InvalidTileIndex;
            return false;
        }

        tileIndex = tileIndices[(screenRow * ColumnCount) + screenColumn];
        return tileIndex != InvalidTileIndex;
    }

    [RewriteOnly("Combines the original orientation lookup with cached FUN_0046F930 results")]
    public bool TryGetPlayableTileIndex(int screenColumn, int screenRow, out int tileIndex)
    {
        if (!TryGetTileIndex(screenColumn, screenRow, out tileIndex))
            return false;

        return playableCells[(screenRow * ColumnCount) + screenColumn];
    }

    [OriginalAddress(0x004304C0, "FUN_004304c0")]
    [RewriteOnly("Constant-time inverse of the verified orientation transform, retaining lookup holes")]
    public bool TryFindTilePosition(int tileIndex, out int screenColumn, out int screenRow)
    {
        if ((uint)tileIndex >= LegacyCityMapSnapshot.TileCount)
        {
            screenColumn = screenRow = InvalidTileIndex;
            return false;
        }

        Transform transform = Transform.ForOrientation(Orientation);
        int sourceX = tileIndex % LegacyCityMapSnapshot.MapLength;
        int sourceY = tileIndex / LegacyCityMapSnapshot.MapLength;
        screenRow = transform.ScreenRowStart + sourceY * transform.ScreenRowRowStep +
            sourceX * transform.ScreenRowColumnStep;
        screenColumn = (transform.DoubledColumnStart + sourceY * transform.DoubledColumnRowStep +
            sourceX * transform.DoubledColumnColumnStep) / 2;
        if (TryGetTileIndex(screenColumn, screenRow, out int mapped) && mapped == tileIndex)
            return true;

        screenColumn = screenRow = InvalidTileIndex;
        return false;
    }

    private static void ValidateOrientation(int orientation)
    {
        if (orientation is not (0 or 2 or 4 or 6))
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientation),
                "Legacy city orientation must be 0, 2, 4, or 6.");
        }
    }

    private readonly record struct Transform(
        int ScreenRowStart,
        int ScreenRowRowStep,
        int ScreenRowColumnStep,
        int DoubledColumnStart,
        int DoubledColumnRowStep,
        int DoubledColumnColumnStep)
    {
        public static Transform ForOrientation(int orientation) => orientation switch
        {
            0 => new(1, 1, 1, 230, -1, 1),
            2 => new(230, 1, -1, 1, 1, 1),
            4 => new(455, -1, -1, 230, 1, -1),
            6 => new(230, -1, 1, 455, -1, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };
    }
}

public readonly record struct LegacyMapDrawCell(
    int TileIndex,
    uint GlobalImageId,
    int ScreenX,
    int ScreenY,
    int LookupColumn,
    int LookupRow);

/// <summary>
/// One global-image submission made by the base city-map pass. X and Y are
/// the exact draw-anchor arguments passed to Pharaoh.exe FUN_0053E590; bitmap
/// top-left conversion remains a concern of the image renderer.
/// </summary>
public readonly record struct LegacyImageDrawCommand(
    int TileIndex,
    int ImageId,
    int X,
    int Y);

/// <summary>
/// Builds the ordered global-image command stream emitted by the terrain part
/// of Pharaoh.exe FUN_00430EF0. Keeping this boundary separate from raster
/// composition allows direct comparison with an original-executable trace.
/// </summary>
public static class LegacyCityMapCommandStream
{
    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    public static (int X, int Y) AdjustMultiTileDrawAnchor(
        int drawCellX,
        int drawCellY,
        int sizeMinusOne)
    {
        if (sizeMinusOne is < 1 or > 5)
            return (drawCellX, drawCellY);

        return (
            drawCellX + sizeMinusOne * (LegacyIsometricViewport.HalfTileWidth + 1),
            drawCellY - sizeMinusOne * LegacyIsometricViewport.HalfTileHeight);
    }

    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    public static IEnumerable<LegacyImageDrawCommand> EnumerateBasePass(
        LegacyCityMapSnapshot map,
        LegacyIsometricViewport viewport,
        LegacyMapOrientationIndex orientationIndex,
        int cameraX,
        int cameraY)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(orientationIndex);

        foreach (LegacyMapDrawCell cell in viewport.EnumerateBaseTiles(
                     map, orientationIndex, cameraX, cameraY))
        {
            if ((map.GetEdgeFlags(cell.TileIndex) & 0x40) == 0)
                continue;

            (int drawX, int drawY) = AdjustMultiTileDrawAnchor(
                cell.ScreenX, cell.ScreenY, map.GetDrawAnchor(cell.TileIndex));

            yield return new LegacyImageDrawCommand(
                cell.TileIndex,
                checked((int)cell.GlobalImageId),
                drawX,
                drawY);
        }
    }
}

/// <summary>
/// Camera limits reconstructed from Pharaoh.exe FUN_004ddf90 and the normal
/// (non-editor) branch of FUN_004de080. The executable derives these limits
/// from the oriented lookup table rather than from its rectangular allocation.
/// </summary>
public readonly record struct LegacyMapCameraBounds(
    int MinimumX,
    int MinimumY,
    int MaximumX,
    int MaximumY)
{
    [OriginalAddress(0x004DDF90, "FUN_004ddf90")]
    public static LegacyMapCameraBounds Calculate(LegacyMapOrientationIndex orientationIndex)
    {
        ArgumentNullException.ThrowIfNull(orientationIndex);

        const int middleRow = 228;
        const int middleColumn = 115;

        int left = FindColumn(orientationIndex, middleRow, 0, 1);
        int right = FindColumn(orientationIndex, middleRow, 230, -1);
        int top = FindRow(orientationIndex, middleColumn, 0, 2);
        int bottom = FindRow(orientationIndex, middleColumn, 454, -2);

        return new LegacyMapCameraBounds(
            left + 1,
            (top + 1) & ~1,
            right - 1,
            (bottom - 1) & ~1);
    }

    [OriginalAddress(0x004DE080, "FUN_004de080")]
    public (int X, int Y) Clamp(int cameraX, int cameraY, LegacyIsometricViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        cameraX = Math.Clamp(cameraX, MinimumX, MaximumX - viewport.VisibleColumns);
        cameraY = Math.Clamp(cameraY, MinimumY, MaximumY - viewport.VisibleRows);
        cameraY &= ~1;
        return (cameraX, cameraY);
    }

    private static int FindColumn(
        LegacyMapOrientationIndex orientationIndex,
        int row,
        int start,
        int step)
    {
        for (int column = start; column >= 0 && column < LegacyMapOrientationIndex.ColumnCount; column += step)
        {
            if (orientationIndex.TryGetPlayableTileIndex(column, row, out _))
                return column;
        }

        throw new InvalidDataException("The oriented map has no valid tile on its middle row.");
    }

    private static int FindRow(
        LegacyMapOrientationIndex orientationIndex,
        int column,
        int start,
        int step)
    {
        for (int row = start; row >= 0 && row < LegacyMapOrientationIndex.RowCount; row += step)
        {
            if (orientationIndex.TryGetPlayableTileIndex(column, row, out _))
                return row;
        }

        throw new InvalidDataException("The oriented map has no valid tile on its middle column.");
    }
}

/// <summary>
/// Converts the draw-tile anchor used by Pharaoh.exe into the upper-left
/// corner of a complete SG3 type-30 bitmap. The executable draws the diamond
/// footprint and the compressed upper part in separate passes; the rewrite
/// currently composites both parts into one bitmap, so it must apply both
/// pass offsets before drawing it.
/// </summary>
public static class LegacyIsometricSpritePlacement
{
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    public static (int X, int Y) GetCombinedBitmapTopLeft(
        int anchorX,
        int anchorY,
        int width,
        int height)
    {
        if (width < LegacyIsometricViewport.TileSpriteWidth ||
            (width - LegacyIsometricViewport.TileSpriteWidth) % LegacyIsometricViewport.HorizontalStride != 0)
        {
            return (anchorX, anchorY);
        }

        int footprintSize = 1 +
            ((width - LegacyIsometricViewport.TileSpriteWidth) / LegacyIsometricViewport.HorizontalStride);
        // FUN_0053F250 submits the upper layer relative to the anchor already
        // corrected by FUN_00430EF0. The rewrite combines base and upper
        // layers into one bitmap, so it must restore the bitmap's top-left.
        int x = anchorX -
            ((footprintSize - 1) * (LegacyIsometricViewport.HalfTileWidth + 1));
        int y = anchorY - height +
            (footprintSize * LegacyIsometricViewport.TileHeight);
        return (x, y);
    }

    /// <summary>
    /// Anchor used by FUN_00436B60's default building-animation branch.
    /// The offsets belong to the static type-30 building image, not to the
    /// smaller animation frame which follows it in the global image table.
    /// </summary>
    [OriginalAddress(0x00436B60, "default one-cell building animation placement")]
    public static (int X, int Y) GetBuildingAnimationTopLeft(
        int tileAnchorX,
        int tileAnchorY,
        int baseHeight,
        int baseSpriteOffsetX,
        int baseSpriteOffsetY) =>
        (
            tileAnchorX + baseSpriteOffsetX,
            tileAnchorY + baseSpriteOffsetY - baseHeight + LegacyIsometricViewport.TileHeight
        );
}

/// <summary>
/// The base-tile traversal proven in Pharaoh.exe FUN_004303a0 and
/// FUN_00430ef0. The 58-pixel sprite base and 60-pixel traversal stride are
/// intentionally separate.
/// </summary>
public sealed class LegacyIsometricViewport
{
    public const int TileSpriteWidth = 58;
    public const int TileHeight = 30;
    public const int HalfTileWidth = 29;
    public const int HalfTileHeight = 15;
    public const int HorizontalStride = TileSpriteWidth + 2;
    public const int OriginX = 0;
    public const int OriginY = 30;

    private LegacyIsometricViewport(int displayWidth, int visibleColumns, int visibleRows)
    {
        DisplayWidth = displayWidth;
        VisibleColumns = visibleColumns;
        VisibleRows = visibleRows;
    }

    public int DisplayWidth { get; }
    public int VisibleColumns { get; }
    public int VisibleRows { get; }

    [RewriteOnly("Compatibility adapter retaining the previously exposed compact-width traversal")]
    public static LegacyIsometricViewport ForDisplayWidth(int displayWidth) =>
        ForDisplayWidth(displayWidth, compactSidebar: true);

    [OriginalAddress(0x004303A0, "FUN_004303a0")]
    [OriginalAddress(0x004DDEC0, "FUN_004ddec0")]
    [OriginalAddress(0x004DDF20, "FUN_004ddf20")]
    public static LegacyIsometricViewport ForDisplayWidth(int displayWidth, bool compactSidebar) => displayWidth switch
    {
        640 => new(displayWidth, compactSidebar ? 10 : 8, 31),
        800 => new(displayWidth, compactSidebar ? 12 : 10, 39),
        1024 => new(displayWidth, compactSidebar ? 16 : 14, 50),
        _ => throw new ArgumentOutOfRangeException(
            nameof(displayWidth),
            "Pharaoh.exe defines this viewport setup for widths 640, 800, and 1024.")
    };

    public IEnumerable<LegacyMapDrawCell> EnumerateBaseTiles(
        LegacyCityMapSnapshot map,
        LegacyMapOrientationIndex orientationIndex,
        int cameraX,
        int cameraY)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(orientationIndex);

        int lookupRow = cameraY - 8;
        int screenY = OriginY - (9 * HalfTileHeight);
        bool shiftedRow = false;
        int rowCount = VisibleRows + 14;
        int columnCount = VisibleColumns + 7;

        for (int row = 0; row < rowCount; row++)
        {
            int screenX = OriginX + ((-2 - TileSpriteWidth) * 4);
            if (shiftedRow)
                screenX -= HalfTileWidth + 1;
            shiftedRow = !shiftedRow;

            int lookupColumn = cameraX - 4;
            for (int column = 0; column < columnCount; column++)
            {
                if (orientationIndex.TryGetTileIndex(lookupColumn, lookupRow, out int tileIndex))
                {
                    yield return new LegacyMapDrawCell(
                        tileIndex,
                        map.GetImageId(tileIndex),
                        screenX,
                        screenY,
                        lookupColumn,
                        lookupRow);
                }

                lookupColumn++;
                screenX += HorizontalStride;
            }

            lookupRow++;
            screenY += HalfTileHeight;
        }
    }

    [OriginalAddress(0x00430790, "FUN_00430790")]
    [OriginalAddress(0x00430960, "FUN_00430960")]
    public bool TryHitTest(
        LegacyMapOrientationIndex orientationIndex,
        int cameraX,
        int cameraY,
        int screenX,
        int screenY,
        out int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(orientationIndex);

        int relativeX = screenX - OriginX;
        int relativeY = screenY - OriginY;
        if (relativeX < 0 || relativeX >= (TileSpriteWidth + 2) * VisibleColumns ||
            relativeY < 0 || relativeY >= HalfTileHeight * VisibleRows)
        {
            tileIndex = LegacyMapOrientationIndex.InvalidTileIndex;
            return false;
        }

        int row = relativeY / HalfTileHeight;
        int doubledColumn = relativeX / (HalfTileWidth + 1);
        int rowParity = row & 1;
        int diamondParity = (row + doubledColumn) & 1;
        int withinRow = relativeY % HalfTileHeight;
        int withinHalfColumn = (relativeX % (HalfTileWidth + 1)) / 2;
        int column = doubledColumn / 2;

        if (diamondParity == 0)
        {
            if (withinHalfColumn < withinRow)
                row++;
            else if ((doubledColumn & 1) != 0 && rowParity != 0)
                column++;
        }
        else if (withinRow + withinHalfColumn >= HalfTileHeight - 1)
        {
            row++;
            if ((doubledColumn & 1) != 0 && rowParity == 0)
                column++;
        }

        return orientationIndex.TryGetTileIndex(cameraX + column, cameraY + row, out tileIndex);
    }

    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    public bool TryGetTileScreenAnchor(
        LegacyMapOrientationIndex orientationIndex,
        int cameraX,
        int cameraY,
        int tileIndex,
        out int screenX,
        out int screenY)
    {
        ArgumentNullException.ThrowIfNull(orientationIndex);
        if (!orientationIndex.TryFindTilePosition(tileIndex, out int column, out int row))
        {
            screenX = screenY = 0;
            return false;
        }

        int relativeRow = row - cameraY;
        screenX = (column - cameraX) * HorizontalStride -
            ((relativeRow & 1) * (HalfTileWidth + 1));
        // EnumerateBaseTiles starts eight lookup rows above cameraY and nine
        // half-rows above OriginY. At cameraY this resolves to OriginY - 15,
        // the top of the diamond whose hit-test centre is at OriginY.
        screenY = OriginY - HalfTileHeight + relativeRow * HalfTileHeight;
        return true;
    }
}

public enum LegacyCameraDirection
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest
}

/// <summary>
/// Reconstructs the continuously sampled camera input used by Pharaoh.exe.
/// Keyboard and screen-edge flags are combined before selecting one of the
/// eight movement directions.
/// </summary>
public static class LegacyCityCameraInput
{
    [OriginalAddress(0x00425840, "FUN_00425840")]
    public static bool? ResolveRotationClockwise(int segment) => segment switch
    {
        1 => false,
        2 => true,
        _ => null
    };

    [RewriteOnly("Projects foreground-window screen coordinates onto the four executable edge flags")]
    public static (bool North, bool East, bool South, bool West) ResolveEdgeFlags(
        int pointerX,
        int pointerY,
        int left,
        int top,
        int right,
        int bottom,
        int inset = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inset);
        // Cursor.Position is sampled in screen coordinates and can remain far
        // beyond the presentation rectangle while this window still owns the
        // foreground. Do not reinterpret an out-of-window pointer as one or
        // two permanently held edge-scroll directions.
        if (pointerX < left || pointerX >= right || pointerY < top || pointerY >= bottom)
            return default;
        return (
            pointerY <= top + inset,
            pointerX >= right - 1 - inset,
            pointerY >= bottom - 1 - inset,
            pointerX <= left + inset);
    }

    [OriginalAddress(0x004CF660, "FUN_004cf660")]
    public static LegacyCameraDirection? ResolveDirection(
        bool north,
        bool east,
        bool south,
        bool west)
    {
        int horizontal = (east ? 1 : 0) - (west ? 1 : 0);
        int vertical = (south ? 1 : 0) - (north ? 1 : 0);
        return (horizontal, vertical) switch
        {
            (0, -1) => LegacyCameraDirection.North,
            (1, -1) => LegacyCameraDirection.NorthEast,
            (1, 0) => LegacyCameraDirection.East,
            (1, 1) => LegacyCameraDirection.SouthEast,
            (0, 1) => LegacyCameraDirection.South,
            (-1, 1) => LegacyCameraDirection.SouthWest,
            (-1, 0) => LegacyCameraDirection.West,
            (-1, -1) => LegacyCameraDirection.NorthWest,
            _ => null
        };
    }

    [OriginalAddress(0x004DDD60, "FUN_004ddd60")]
    public static int? GetRepeatIntervalMilliseconds(int scrollSpeed)
    {
        scrollSpeed = Math.Clamp(scrollSpeed, 0, 100);
        int setting = (100 - scrollSpeed) / 10;
        return setting < 10 ? (setting * 12) + 2 : null;
    }
}

/// <summary>Mutable camera/orientation state over an immutable legacy city map.</summary>
public sealed class LegacyCityViewState
{
    private readonly LegacyCityMapSnapshot map;

    public LegacyCityViewState(
        LegacyCityMapSnapshot map,
        LegacyIsometricViewport viewport,
        int orientation,
        int cameraX,
        int cameraY)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        SetOrientation(orientation);
        (CameraX, CameraY) = CameraBounds.Clamp(cameraX, cameraY, Viewport);
    }

    public LegacyIsometricViewport Viewport { get; }
    public int Orientation { get; private set; }
    public LegacyMapOrientationIndex OrientationIndex { get; private set; } = null!;
    public LegacyMapCameraBounds CameraBounds { get; private set; }
    public int CameraX { get; private set; }
    public int CameraY { get; private set; }

    [OriginalAddress(0x004DDDE0, "FUN_004ddde0")]
    public bool Move(LegacyCameraDirection direction, int step = 4)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));

        int x = CameraX;
        int y = CameraY;
        switch (direction)
        {
            case LegacyCameraDirection.North: y -= step; break;
            case LegacyCameraDirection.NorthEast: x++; y -= step; break;
            case LegacyCameraDirection.East: x++; break;
            case LegacyCameraDirection.SouthEast: x++; y += step; break;
            case LegacyCameraDirection.South: y += step; break;
            case LegacyCameraDirection.SouthWest: x--; y += step; break;
            case LegacyCameraDirection.West: x--; break;
            case LegacyCameraDirection.NorthWest: x--; y -= step; break;
            default: throw new ArgumentOutOfRangeException(nameof(direction));
        }

        (x, y) = CameraBounds.Clamp(x, y, Viewport);
        bool changed = x != CameraX || y != CameraY;
        (CameraX, CameraY) = (x, y);
        return changed;
    }

    [RewriteOnly("Composes the verified orientation lookup, tile centering, and camera clamping functions")]
    public bool Rotate(bool clockwise)
    {
        int previous = Orientation;
        int centerColumn = CameraX + Viewport.VisibleColumns / 2;
        int centerRow = CameraY + Viewport.VisibleRows / 2;
        OrientationIndex.TryGetTileIndex(centerColumn, centerRow, out int centerTile);

        int next = (Orientation + (clockwise ? 2 : 6)) & 6;
        SetOrientation(next);
        if (centerTile != LegacyMapOrientationIndex.InvalidTileIndex)
            CenterOnTile(centerTile);
        else
            (CameraX, CameraY) = CameraBounds.Clamp(CameraX, CameraY, Viewport);
        return next != previous;
    }

    [RewriteOnly("Implements the centre segment of the original three-part map-rotation control")]
    public bool OrientNorth()
    {
        if (Orientation == 0)
            return false;

        int centerColumn = CameraX + Viewport.VisibleColumns / 2;
        int centerRow = CameraY + Viewport.VisibleRows / 2;
        OrientationIndex.TryGetTileIndex(centerColumn, centerRow, out int centerTile);
        SetOrientation(0);
        if (centerTile != LegacyMapOrientationIndex.InvalidTileIndex)
            CenterOnTile(centerTile);
        else
            (CameraX, CameraY) = CameraBounds.Clamp(CameraX, CameraY, Viewport);
        return true;
    }

    [OriginalAddress(0x00430CD0, "FUN_00430cd0")]
    public bool CenterOnTile(int tileIndex)
    {
        if (!OrientationIndex.TryFindTilePosition(tileIndex, out int column, out int row))
            return false;
        (CameraX, CameraY) = CameraBounds.Clamp(
            column - Viewport.VisibleColumns / 2,
            row - Viewport.VisibleRows / 2,
            Viewport);
        return true;
    }

    public bool TryHitTest(int screenX, int screenY, out int tileIndex) =>
        Viewport.TryHitTest(OrientationIndex, CameraX, CameraY, screenX, screenY, out tileIndex);

    private void SetOrientation(int orientation)
    {
        OrientationIndex = LegacyMapOrientationIndex.Build(map, orientation);
        CameraBounds = LegacyMapCameraBounds.Calculate(OrientationIndex);
        Orientation = orientation;
    }
}
