using System.Buffers.Binary;

namespace Pharaoh.Assets.Graphics;

public sealed record Sg3IsometricLayers(RgbaImage Base, RgbaImage Overlay);

public static class Sg3ImageDecoder
{
    public const ushort TransparentColor = 0xF81F;
    private static readonly HashSet<ushort> PlainTypes = [0, 1, 10, 12, 13];
    private static readonly HashSet<ushort> SpriteTypes = [256, 257, 276];

    public static RgbaImage Decode(string sg3Path, Sg3Archive archive, int imageIndex)
        => Decode(sg3Path, archive, imageIndex, File.ReadAllBytes);

    public static RgbaImage Decode(
        string sg3Path,
        Sg3Archive archive,
        int imageIndex,
        Func<string, byte[]> readSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sg3Path);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(readSource);
        if ((uint)imageIndex >= (uint)archive.Images.Count)
            throw new ArgumentOutOfRangeException(nameof(imageIndex));

        Sg3ImageRecord requested = archive.Images[imageIndex];
        bool mirror = requested.MirroredImageIndex is int;
        Sg3ImageRecord record = mirror ? archive.Images[requested.MirroredImageIndex!.Value] : requested;
        if (record.Width <= 0 || record.Height <= 0)
            throw new InvalidDataException($"SG3 image {imageIndex} has invalid dimensions.");

        Sg3BitmapRecord bitmap = ResolveBitmap(archive, record);
        string sourcePath = Resolve555Path(sg3Path, bitmap, record);
        byte[] source = readSource(sourcePath);
        int start = checked((int)record.Offset - (record.DataOffsetAdjustment != 0 ? 1 : 0));
        int length = checked((int)record.Length);
        if (start < 0 || (long)start + length + record.AlphaLength > source.Length)
            throw new InvalidDataException($"SG3 image {imageIndex} points outside {Path.GetFileName(sourcePath)}.");
        ReadOnlySpan<byte> blob = source.AsSpan(start, length);

        RgbaImage image;
        // Terrain group35 is an animation-only type30 anchor with SpriteCount8
        // and no base pixels. Its29-pixel height does not describe a ground tile.
        if (length == 0 && record.UncompressedLength == 0)
            image = Empty(record.Width,record.Height);
        else if (PlainTypes.Contains(record.Type) || blob.Length == checked(record.Width * record.Height * 2))
            image = DecodeRaw555(blob, record.Width, record.Height, transparentKey: true);
        else if (SpriteTypes.Contains(record.Type))
            image = DecodeTransparentStream(blob, record.Width, record.Height);
        else if (record.Type == 30)
            image = CompositeIsometric(DecodeIsometricLayerData(record, blob));
        else
            throw new NotSupportedException($"Unsupported SG3 image type {record.Type}.");

        if (record.AlphaLength > 0)
            ApplyAlphaStream(image, source.AsSpan(start + length, checked((int)record.AlphaLength)));
        if (mirror)
            FlipHorizontally(image);
        return image;
    }

    public static Sg3IsometricLayers DecodeIsometricLayers(
        string sg3Path,
        Sg3Archive archive,
        int imageIndex,
        Func<string, byte[]> readSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sg3Path);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(readSource);
        if ((uint)imageIndex >= (uint)archive.Images.Count)
            throw new ArgumentOutOfRangeException(nameof(imageIndex));

        Sg3ImageRecord requested = archive.Images[imageIndex];
        bool mirror = requested.MirroredImageIndex is int;
        Sg3ImageRecord record = mirror ? archive.Images[requested.MirroredImageIndex!.Value] : requested;
        if (record.Type != 30 || record.Width <= 0 || record.Height <= 0)
            throw new InvalidDataException($"SG3 image {imageIndex} is not a valid isometric image.");

        Sg3BitmapRecord bitmap = ResolveBitmap(archive, record);
        string sourcePath = Resolve555Path(sg3Path, bitmap, record);
        byte[] source = readSource(sourcePath);
        int start = checked((int)record.Offset - (record.DataOffsetAdjustment != 0 ? 1 : 0));
        int length = checked((int)record.Length);
        if (start < 0 || (long)start + length + record.AlphaLength > source.Length)
            throw new InvalidDataException($"SG3 image {imageIndex} points outside {Path.GetFileName(sourcePath)}.");

        Sg3IsometricLayers layers = DecodeIsometricLayerData(record, source.AsSpan(start, length));
        if (record.AlphaLength > 0)
        {
            ReadOnlySpan<byte> alpha = source.AsSpan(start + length, checked((int)record.AlphaLength));
            ApplyAlphaStream(layers.Base, alpha);
            ApplyAlphaStream(layers.Overlay, alpha);
        }
        if (mirror)
        {
            FlipHorizontally(layers.Base);
            FlipHorizontally(layers.Overlay);
        }
        return layers;
    }

    public static RgbaImage DecodeRaw555(ReadOnlySpan<byte> data, int width, int height, bool transparentKey)
    {
        if (data.Length != checked(width * height * 2))
            throw new InvalidDataException("RAW555 byte count does not match its dimensions.");
        RgbaImage image = Empty(width, height);
        for (int pixel = 0; pixel < width * height; pixel++)
            Write555(image.Pixels, pixel * 4, BinaryPrimitives.ReadUInt16LittleEndian(data[(pixel * 2)..]), transparentKey);
        return image;
    }

    public static RgbaImage DecodeTransparentStream(ReadOnlySpan<byte> data, int width, int height)
    {
        RgbaImage image = Empty(width, height);
        int pixel = 0;
        int input = 0;
        int pixelCount = checked(width * height);
        while (input < data.Length && pixel < pixelCount)
        {
            byte command = data[input++];
            if (command == 0xFF)
            {
                if (input >= data.Length)
                    throw new InvalidDataException("Truncated SG3 transparent skip command.");
                pixel = checked(pixel + data[input++]);
                continue;
            }

            int bytes = checked(command * 2);
            if (input + bytes > data.Length || pixel + command > pixelCount)
                throw new InvalidDataException("SG3 transparent run exceeds its buffer.");
            for (int index = 0; index < command; index++, pixel++, input += 2)
                Write555(image.Pixels, pixel * 4, BinaryPrimitives.ReadUInt16LittleEndian(data[input..]), true);
        }
        return image;
    }

    public static void ApplyAlphaStream(RgbaImage image, ReadOnlySpan<byte> data)
    {
        int pixel = 0;
        int input = 0;
        int pixelCount = checked(image.Width * image.Height);
        while (input < data.Length && pixel < pixelCount)
        {
            byte command = data[input++];
            if (command == 0xFF)
            {
                if (input >= data.Length)
                    throw new InvalidDataException("Truncated SG3 alpha skip command.");
                pixel = checked(pixel + data[input++]);
                continue;
            }
            if (input + command > data.Length || pixel + command > pixelCount)
                throw new InvalidDataException("SG3 alpha run exceeds its buffer.");
            for (int index = 0; index < command; index++, pixel++)
            {
                int alpha5 = data[input++] & 0x1F;
                image.Pixels[(pixel * 4) + 3] = (byte)((alpha5 << 3) | (alpha5 >> 2));
            }
        }
    }

    private static Sg3IsometricLayers DecodeIsometricLayerData(Sg3ImageRecord record, ReadOnlySpan<byte> blob)
    {
        if(blob.Length==0&&record.UncompressedLength==0)
            return new(Empty(record.Width,record.Height),Empty(record.Width,record.Height));
        int logicalHeight = (record.Width + 2) / 2;
        int size = record.IsometricSize;
        (int tileWidth, int tileHeight, int tileBytes) = logicalHeight == 40 * size
            ? (78, 40, 3200)
            : (58, 30, 1800);
        if (size <= 0)
        {
            if (logicalHeight % 30 == 0) size = logicalHeight / 30;
            else if (logicalHeight % 40 == 0) { size = logicalHeight / 40; tileWidth = 78; tileHeight = 40; tileBytes = 3200; }
            else throw new InvalidDataException("Cannot infer SG3 isometric tile size.");
        }

        int baseLength = checked((int)record.UncompressedLength);
        if (baseLength < 0 || baseLength > blob.Length)
            throw new InvalidDataException("Invalid SG3 isometric base length.");
        RgbaImage image = Empty(record.Width, record.Height);
        int baseIndex = 0;
        int yOffset = record.Height - logicalHeight;
        for (int row = 0; row < (size * 2) - 1; row++)
        {
            int xOffset = (row < size ? size - row - 1 : row - size + 1) * tileHeight;
            int tileCount = row < size ? row + 1 : (2 * size) - row - 1;
            for (int tile = 0; tile < tileCount; tile++)
            {
                if (baseIndex + tileBytes > baseLength)
                    throw new InvalidDataException("Truncated SG3 isometric tile data.");
                BlitIsoTile(image, blob.Slice(baseIndex, tileBytes), xOffset, yOffset, tileWidth, tileHeight);
                baseIndex += tileBytes;
                xOffset += tileWidth + 2;
            }
            yOffset += tileHeight / 2;
        }

        RgbaImage overlay = baseLength < blob.Length
            ? DecodeTransparentStream(blob[baseLength..], record.Width, record.Height)
            : Empty(record.Width, record.Height);
        return new Sg3IsometricLayers(image, overlay);
    }

    private static RgbaImage CompositeIsometric(Sg3IsometricLayers layers)
    {
        for (int pixel = 0; pixel < layers.Base.Width * layers.Base.Height; pixel++)
        {
            int offset = pixel * 4;
            if (layers.Overlay.Pixels[offset + 3] != 0)
                layers.Overlay.Pixels.AsSpan(offset, 4).CopyTo(layers.Base.Pixels.AsSpan(offset, 4));
        }
        return layers.Base;
    }

    private static void BlitIsoTile(RgbaImage image, ReadOnlySpan<byte> tile, int offsetX, int offsetY, int width, int height)
    {
        int input = 0;
        for (int y = 0; y < height; y++)
        {
            int start = y < height / 2 ? height - (2 * (y + 1)) : (2 * y) - height;
            int end = width - start;
            for (int x = start; x < end; x++, input += 2)
            {
                if (input + 2 > tile.Length || (uint)(offsetX + x) >= (uint)image.Width || (uint)(offsetY + y) >= (uint)image.Height)
                    throw new InvalidDataException("Invalid SG3 isometric tile geometry.");
                Write555(image.Pixels, (((offsetY + y) * image.Width) + offsetX + x) * 4,
                    BinaryPrimitives.ReadUInt16LittleEndian(tile[input..]), true);
            }
        }
    }

    private static Sg3BitmapRecord ResolveBitmap(Sg3Archive archive, Sg3ImageRecord record) =>
        archive.Bitmaps.FirstOrDefault(bitmap => bitmap.StartIndex > 0 && record.Index >= bitmap.StartIndex &&
            (record.Index <= bitmap.EndIndex || record.Index < bitmap.StartIndex + bitmap.ImageCount))
        ?? ((uint)record.BitmapId < (uint)archive.Bitmaps.Count ? archive.Bitmaps[record.BitmapId] : archive.Bitmaps[0]);

    private static string Resolve555Path(string sg3Path, Sg3BitmapRecord bitmap, Sg3ImageRecord record)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(sg3Path))!;
        string defaultName = Path.ChangeExtension(Path.GetFileName(sg3Path), ".555");
        string bitmapName = Path.ChangeExtension(Path.GetFileName(bitmap.FileName), ".555");
        string primary = record.DataOffsetAdjustment != 0 && !bitmap.FileName.Equals("Default", StringComparison.OrdinalIgnoreCase)
            ? bitmapName : defaultName;
        foreach (string name in new[] { primary, primary == bitmapName ? defaultName : bitmapName })
        {
            string direct = Path.Combine(directory, name);
            if (File.Exists(direct)) return direct;
            string nested = Path.Combine(directory, "555", name);
            if (File.Exists(nested)) return nested;
        }
        throw new FileNotFoundException($"No .555 source found for SG3 bitmap '{bitmap.FileName}'.");
    }

    private static RgbaImage Empty(int width, int height) => new(width, height, new byte[checked(width * height * 4)]);

    private static void Write555(byte[] output, int offset, ushort color, bool transparentKey)
    {
        if (transparentKey && color == TransparentColor)
            return;
        int r = (color >> 10) & 0x1F;
        int g = (color >> 5) & 0x1F;
        int b = color & 0x1F;
        output[offset] = (byte)((r << 3) | (r >> 2));
        output[offset + 1] = (byte)((g << 3) | (g >> 2));
        output[offset + 2] = (byte)((b << 3) | (b >> 2));
        output[offset + 3] = 255;
    }

    private static void FlipHorizontally(RgbaImage image)
    {
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width / 2; x++)
        {
            int left = ((y * image.Width) + x) * 4;
            int right = ((y * image.Width) + image.Width - x - 1) * 4;
            for (int channel = 0; channel < 4; channel++)
                (image.Pixels[left + channel], image.Pixels[right + channel]) = (image.Pixels[right + channel], image.Pixels[left + channel]);
        }
    }
}
