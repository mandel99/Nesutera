using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Pharaoh.Assets.Graphics;

namespace Pharaoh.Desktop;

internal static class RgbaBitmap
{
    public static Bitmap Create(RgbaImage image)
    {
        Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData? data = null;
        try
        {
            data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] bgra = new byte[image.Pixels.Length];
            for (int offset = 0; offset < bgra.Length; offset += 4)
            {
                bgra[offset] = image.Pixels[offset + 2];
                bgra[offset + 1] = image.Pixels[offset + 1];
                bgra[offset + 2] = image.Pixels[offset];
                bgra[offset + 3] = image.Pixels[offset + 3];
            }
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            bitmap.UnlockBits(data);
            data = null;
            return bitmap;
        }
        catch
        {
            if (data is not null) bitmap.UnlockBits(data);
            bitmap.Dispose();
            throw;
        }
    }
}
