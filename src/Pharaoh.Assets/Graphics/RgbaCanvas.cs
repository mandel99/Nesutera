using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Assets.Graphics;

public sealed class RgbaCanvas
{
    public RgbaCanvas(int width, int height)
    {
        Surface = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
    }

    public RgbaImage Surface { get; }

    public void Clear(byte red, byte green, byte blue, byte alpha = 255)
    {
        Span<byte> pixels = Surface.Pixels;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = alpha;
        }
    }

    public void Blit(RgbaImage source, int destinationX, int destinationY) =>
        BlitScaled(source, destinationX, destinationY, source.Width, source.Height);

    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    public void BlitScaled(RgbaImage source, int destinationX, int destinationY, int destinationWidth, int destinationHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (destinationWidth <= 0 || destinationHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(destinationWidth));

        int startX = Math.Max(0, destinationX);
        int startY = Math.Max(0, destinationY);
        int endX = Math.Min(Surface.Width, destinationX + destinationWidth);
        int endY = Math.Min(Surface.Height, destinationY + destinationHeight);
        for (int y = startY; y < endY; y++)
        {
            int sourceY = ((y - destinationY) * source.Height) / destinationHeight;
            for (int x = startX; x < endX; x++)
            {
                int sourceX = ((x - destinationX) * source.Width) / destinationWidth;
                int sourceOffset = ((sourceY * source.Width) + sourceX) * 4;
                int destinationOffset = ((y * Surface.Width) + x) * 4;
                Blend(source.Pixels, sourceOffset, Surface.Pixels, destinationOffset);
            }
        }
    }

    private static void Blend(byte[] source, int sourceOffset, byte[] destination, int destinationOffset)
    {
        int sourceAlpha = source[sourceOffset + 3];
        if (sourceAlpha == 0)
            return;
        if (sourceAlpha == 255)
        {
            source.AsSpan(sourceOffset, 4).CopyTo(destination.AsSpan(destinationOffset, 4));
            return;
        }

        int destinationAlpha = destination[destinationOffset + 3];
        int inverseAlpha = 255 - sourceAlpha;
        int outputAlpha = sourceAlpha + ((destinationAlpha * inverseAlpha + 127) / 255);
        for (int channel = 0; channel < 3; channel++)
        {
            int premultiplied = (source[sourceOffset + channel] * sourceAlpha * 255) +
                (destination[destinationOffset + channel] * destinationAlpha * inverseAlpha);
            destination[destinationOffset + channel] = outputAlpha == 0
                ? (byte)0
                : (byte)((premultiplied + ((outputAlpha * 255) / 2)) / (outputAlpha * 255));
        }
        destination[destinationOffset + 3] = (byte)outputAlpha;
    }
}
