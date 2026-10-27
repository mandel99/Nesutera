using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Pharaoh.Assets.Graphics;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Rendering;

/// <summary>
/// Draws temporary construction and demolition feedback over the persistent map frame.
/// The simulation and placement rules remain owned by <see cref="BuildModeManager"/>.
/// </summary>
internal sealed class BuildPreviewRenderer(
    Sg3AssetCatalog graphicsAssets,
    BuildModeManager buildMode) : IDisposable
{
    private readonly Dictionary<int, Bitmap> previewSprites = [];
    private readonly Dictionary<int, RgbaImage> placementMarkerMasks = [];

    [OriginalAddress(0x00474130, "FUN_00474130")]
    public void DrawHousing(Graphics graphics, CameraManager camera)
    {
        if (!buildMode.HousingPlacement.IsActive || buildMode.HousingPlacement.PreviewTiles.Count == 0)
            return;

        int imageId = LegacySquarePlacementPreview.HeldFootprintImageId;
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        Bitmap sprite = GetPreviewSprite(imageId);
        foreach ((int _, Point anchor) in buildMode.HousingPlacement.PreviewTiles
                     .Select(tile => (Tile: tile, HasAnchor: camera.TryGetTileScreenAnchor(tile, out Point anchor), Anchor: anchor))
                     .Where(item => item.HasAnchor)
                     .OrderBy(item => item.Anchor.Y)
                     .ThenBy(item => item.Anchor.X)
                     .Select(item => (item.Tile, item.Anchor)))
        {
            (int drawX, int drawY) = record.Type == 30
                ? LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(
                    anchor.X, anchor.Y, record.Width, record.Height)
                : (anchor.X, anchor.Y);
            graphics.DrawImageUnscaled(sprite, drawX, drawY);
        }
    }

    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    [OriginalAddress(0x00474130, "FUN_00474130")]
    public void DrawHeldFootprint(Graphics graphics,CameraManager camera,IEnumerable<int> tiles)
    {
        foreach(var entry in tiles.Select(tile=>(Tile:tile,Visible:camera.TryGetTileScreenAnchor(tile,out Point point),Point:point))
            .Where(entry=>entry.Visible).OrderBy(entry=>entry.Point.Y).ThenBy(entry=>entry.Point.X))
            DrawPreviewSprite(graphics,entry.Point,LegacySquarePlacementPreview.HeldFootprintImageId);
    }

    [OriginalAddress(0x004101C0, "FUN_004101c0")]
    [OriginalAddress(0x00410700, "FUN_00410700")]
    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    [OriginalAddress(0x0053F4F0, "FUN_0053f4f0")]
    public void ApplyRoadMarker(
        Bitmap destination,
        CameraManager camera,
        LegacyGridBuilder? builder,
        int tile,
        bool invalid,
        ushort rgb565Mask,
        Rectangle clip)
    {
        if (builder is null)
            return;
        int imageId = invalid
            ? builder.ResolvePlacementFootprintImage()
            : builder.ResolveRoadPreviewImage(tile, new HashSet<int> { tile });
        ApplyMarker(destination, camera, tile, invalid, rgb565Mask, clip, imageId);
    }

    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    public void ApplyHousingMarker(
        Bitmap destination,
        CameraManager camera,
        LegacyGridBuilder? builder,
        int tile,
        bool invalid,
        ushort rgb565Mask,
        Rectangle clip)
    {
        if (builder is null)
            return;
        int imageId = invalid
            ? builder.ResolvePlacementFootprintImage()
            : LegacyHousingPlacement.ForSaleImageId;
        ApplyMarker(destination, camera, tile, invalid, rgb565Mask, clip, imageId);
    }

    public void ApplySafetyServiceMarker(
        Bitmap destination,
        CameraManager camera,
        int tile,
        int imageId,
        bool invalid,
        Rectangle clip) =>
        ApplyMarker(destination, camera, tile, invalid,
            invalid ? (ushort)0xf863 : (ushort)0x1fe3, clip, invalid?LegacySquarePlacementPreview.InvalidFootprintMaskImageId:imageId);

    [OriginalAddress(0x0043B630, "valid building sprite or destination-masked individual footprint cells")]
    [OriginalAddress(0x0053E590, "valid sprite uses the green source-pixel mask")]
    [OriginalAddress(0x0053F4F0, "invalid footprint uses destination-pixel masks per cell")]
    public void ApplySquareBuildingMarker(
        Bitmap destination, CameraManager camera, LegacyGridBuilder? builder,
        LegacySquarePlacementPreview preview, Rectangle clip)
    {
        if (preview.IsValid)
        {
            ApplyMarker(destination,camera,preview.CursorTile,false,0x1fe3,clip,preview.ImageId);
            return;
        }
        if (builder is null) return;
        int footprintImage = builder.ResolvePlacementFootprintImage();
        foreach (var item in preview.Cells
                     .Select(cell => (Cell:cell,Visible:camera.TryGetTileScreenAnchor(cell.Tile,out Point point),Point:point))
                     .Where(item => item.Visible).OrderBy(item => item.Point.Y).ThenBy(item => item.Point.X))
            ApplyMarker(destination,camera,item.Cell.Tile,true,
                item.Cell.IsValid ? (ushort)0x1fe3 : (ushort)0xf863,clip,footprintImage);
    }

    [OriginalAddress(0x00436B60, "DAT_00799a97 selects the 0xf863 building mask")]
    [OriginalAddress(0x004743F0, "Clear-land drag preview")]
    public void ApplyDemolition(
        Bitmap destination,
        CameraManager camera,
        LegacyCityMapSnapshot? map,
        LegacyMissionSession? session,
        IReadOnlyDictionary<int, int> animatedWaterImages,
        IReadOnlyCollection<int> buildingIds,
        Rectangle clip)
    {
        if (buildingIds.Count == 0 || map is null ||
            camera.View is not LegacyCityViewState view || session is null)
            return;
        HashSet<int> marked = buildingIds as HashSet<int> ?? [.. buildingIds];
        ReadOnlySpan<byte> buildingGrid = session.LogicalChunks["building_grid"].Span;
        foreach (LegacyImageDrawCommand command in LegacyCityMapCommandStream.EnumerateBasePass(
                     map, view.Viewport, view.OrientationIndex, view.CameraX, view.CameraY))
        {
            int id = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.Slice(command.TileIndex * sizeof(short), sizeof(short)));
            if (!marked.Contains(id))
                continue;
            int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
            ApplyMarker(destination, camera, command.TileIndex, invalid: true, 0xf863, clip, imageId,
                new Point(command.X, command.Y));
        }
    }

    [OriginalAddress(0x0053E590, "source-pixel tint for a valid preview")]
    [OriginalAddress(0x0053F4F0, "destination-pixel AND mask for footprint feedback")]
    [OriginalAddress(0x0053F250, "combined isometric upper-layer placement")]
    private void ApplyMarker(
        Bitmap destination,
        CameraManager camera,
        int tile,
        bool invalid,
        ushort rgb565Mask,
        Rectangle clip,
        int imageId,
        Point? adjustedAnchor = null)
    {
        Point anchor;
        if (adjustedAnchor is Point commandAnchor)
            anchor = commandAnchor;
        else if (!camera.TryGetTileScreenAnchor(tile, out anchor))
            return;
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        if (!placementMarkerMasks.TryGetValue(imageId, out RgbaImage? marker))
        {
            marker = graphicsAssets.DecodeGlobal(imageId);
            placementMarkerMasks.Add(imageId, marker);
        }

        (int drawX, int drawY) = record.Type == 30
            ? LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(
                anchor.X, anchor.Y, record.Width, record.Height)
            : (anchor.X, anchor.Y);
        Rectangle spriteBounds = new(drawX, drawY, marker.Width, marker.Height);
        Rectangle affected = Rectangle.Intersect(
            Rectangle.Intersect(spriteBounds, clip), new Rectangle(Point.Empty, destination.Size));
        if (affected.IsEmpty)
            return;

        BitmapData data = destination.LockBits(affected, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = checked(affected.Width * sizeof(int));
            int byteCount = checked(rowBytes * affected.Height);
            byte[] destinationPixels = new byte[byteCount];
            for (int y = 0; y < affected.Height; y++)
            {
                IntPtr sourceRow = IntPtr.Add(data.Scan0, checked(y * data.Stride));
                Marshal.Copy(sourceRow, destinationPixels, y * rowBytes, rowBytes);
            }
            ReadOnlySpan<byte> maskPixels = marker.Pixels;
            int sourceLeft = affected.Left - drawX;
            int sourceTop = affected.Top - drawY;

            for (int y = 0; y < affected.Height; y++)
            {
                int destinationRow = y * rowBytes;
                int sourceRow = (sourceTop + y) * marker.Width;
                for (int x = 0; x < affected.Width; x++)
                {
                    int sourceOffset = (sourceRow + sourceLeft + x) * 4;
                    if (maskPixels[sourceOffset + 3] == 0)
                        continue;
                    int destinationOffset = destinationRow + x * 4;
                    byte red = invalid ? destinationPixels[destinationOffset + 2] : maskPixels[sourceOffset];
                    byte green = invalid ? destinationPixels[destinationOffset + 1] : maskPixels[sourceOffset + 1];
                    byte blue = invalid ? destinationPixels[destinationOffset] : maskPixels[sourceOffset + 2];
                    ushort pixel565 = (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
                    ushort masked = (ushort)(pixel565 & rgb565Mask);
                    int red5 = (masked >> 11) & 0x1f;
                    int green6 = (masked >> 5) & 0x3f;
                    int blue5 = masked & 0x1f;
                    destinationPixels[destinationOffset] = (byte)((blue5 << 3) | (blue5 >> 2));
                    destinationPixels[destinationOffset + 1] = (byte)((green6 << 2) | (green6 >> 4));
                    destinationPixels[destinationOffset + 2] = (byte)((red5 << 3) | (red5 >> 2));
                }
            }
            for (int y = 0; y < affected.Height; y++)
            {
                IntPtr destinationRow = IntPtr.Add(data.Scan0, checked(y * data.Stride));
                Marshal.Copy(destinationPixels, y * rowBytes, destinationRow, rowBytes);
            }
        }
        finally
        {
            destination.UnlockBits(data);
        }
    }

    private Rectangle GetPreviewBounds(Point anchor, int imageId)
    {
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        (int drawX, int drawY) = record.Type == 30
            ? LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(
                anchor.X, anchor.Y, record.Width, record.Height)
            : (anchor.X, anchor.Y);
        return new Rectangle(drawX, drawY, record.Width, record.Height);
    }

    private void DrawPreviewSprite(Graphics graphics, Point anchor, int imageId)
    {
        Rectangle bounds = GetPreviewBounds(anchor, imageId);
        graphics.DrawImageUnscaled(GetPreviewSprite(imageId), bounds.X, bounds.Y);
    }

    private Bitmap GetPreviewSprite(int imageId)
    {
        if (previewSprites.TryGetValue(imageId, out Bitmap? sprite))
            return sprite;
        sprite = RgbaBitmap.Create(graphicsAssets.DecodeGlobal(imageId));
        previewSprites.Add(imageId, sprite);
        return sprite;
    }

    private static Rectangle Union(Rectangle first, Rectangle second) =>
        first.IsEmpty ? second : second.IsEmpty ? first : Rectangle.Union(first, second);

    public void Dispose()
    {
        foreach (Bitmap sprite in previewSprites.Values)
            sprite.Dispose();
        previewSprites.Clear();
        placementMarkerMasks.Clear();
    }
}
