using Pharaoh.Assets.Graphics;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class Minimap
{
    private static readonly Color CameraOutline = Color.FromArgb(231, 227, 90);

    [OriginalAddress(0x0051EFB0, "FUN_0051efb0")]
    public static Rectangle Bounds(int displayWidth) =>
        LayoutManager.MinimapBounds(displayWidth);

    [OriginalAddress(0x0051EFB0, "FUN_0051efb0")]
    public static void Draw(
        Graphics graphics,
        Bitmap? frame,
        int displayWidth,
        int horizontalOffset)
    {
        Rectangle bounds = LayoutManager.MinimapBounds(displayWidth, horizontalOffset);
        if (frame is null)
            graphics.FillRectangle(Brushes.Black, bounds);
        else
            graphics.DrawImageUnscaled(frame, bounds.Location);
    }

    [OriginalAddress(0x0043E810, "FUN_0043e810")]
    [OriginalAddress(0x004CD680, "FUN_004cd680")]
    public static Bitmap Build(
        Sg3AssetCatalog assets,
        LegacyCityMinimapProjection projection,
        IDictionary<int, RgbaImage>? spriteCache = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(projection);
        byte[] pixels = new byte[
            LegacyCityMinimapProjection.PixelWidth * LegacyCityMinimapProjection.PixelHeight * 4];
        for (int alpha = 3; alpha < pixels.Length; alpha += 4)
            pixels[alpha] = byte.MaxValue;
        IDictionary<int, RgbaImage> sprites = spriteCache ?? new Dictionary<int, RgbaImage>();

        foreach (LegacyMinimapDrawCommand command in projection.DrawCommands)
        {
            if (!sprites.TryGetValue(command.ImageIndex, out RgbaImage? sprite))
            {
                sprite = assets.Decode("Pharaoh_General.sg3", command.ImageIndex);
                sprites.Add(command.ImageIndex, sprite);
            }
            CopySprite(sprite, pixels, command.X, command.Y);
        }

        Bitmap result = RgbaBitmap.Create(new RgbaImage(
            LegacyCityMinimapProjection.PixelWidth,
            LegacyCityMinimapProjection.PixelHeight,
            pixels));
        using Graphics graphics = Graphics.FromImage(result);
        using Pen pen = new(CameraOutline, 1);
        LegacyMinimapCameraRectangle camera = projection.CameraRectangle;
        graphics.DrawRectangle(pen, camera.X, camera.Y,
            Math.Max(0, camera.Width - 1), Math.Max(0, camera.Height - 1));
        return result;
    }

    private static void CopySprite(RgbaImage sprite, byte[] destination, int x, int y)
    {
        for (int sourceY = 0; sourceY < sprite.Height; sourceY++)
        for (int sourceX = 0; sourceX < sprite.Width; sourceX++)
        {
            int destinationX = x + sourceX;
            int destinationY = y + sourceY;
            if ((uint)destinationX >= LegacyCityMinimapProjection.PixelWidth ||
                (uint)destinationY >= LegacyCityMinimapProjection.PixelHeight)
                continue;
            int sourceOffset = ((sourceY * sprite.Width) + sourceX) * 4;
            if (sprite.Pixels[sourceOffset + 3] == 0)
                continue;
            int destinationOffset =
                ((destinationY * LegacyCityMinimapProjection.PixelWidth) + destinationX) * 4;
            sprite.Pixels.AsSpan(sourceOffset, 4).CopyTo(destination.AsSpan(destinationOffset, 4));
        }
    }
}
