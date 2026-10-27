using System.Text;
using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop;

/// <summary>Original single-byte Pharaoh font measurement, wrapping and rendering.</summary>
internal sealed class LegacyBitmapFont : IDisposable
{
    // Tabulka na adrese 005E049C: index = bajt - 0x20, hodnota = glyph id.
    private static readonly byte[] GlyphMap = Convert.FromHexString(
        "003F40000041004A434442464E454F4D3E35363738393A3B3C3D48490047004B" +
        "001B1C1D1E1F202122232425262728292A2B2C2D2E2F30313233340063000050" +
        "000102030405060708090A0B0C0D0E0F101112131415161718191A0000000000" +
        "6561565451530167815557596E5D691B6A676D605D5F6463197B6B006F000000" +
        "527F5E62666C010F800000000000000082000000000000000000000000000080" +
        "7270717169836D65746A737377757676006C7A7879797B00847E7C7D6B330068" +
        "5352545151856765575658555B5A5C5900665F5E60605D008663626461190019");

    private readonly string archivePath;
    private readonly Sg3Archive archive;
    private readonly Sg3AssetCatalog? assets;
    private readonly int baseImage;
    private readonly Encoding legacyEncoding;
    private readonly int measuredSpaceWidth;
    private readonly int drawnSpaceWidth;
    private readonly int measuredGlyphSpacing;
    private readonly int drawnGlyphSpacing;
    private readonly int lineAdvance;
    private readonly Dictionary<int, Bitmap> glyphs = [];
    private readonly Dictionary<(int ImageIndex, int Argb), Bitmap> solidGlyphs = [];

    public LegacyBitmapFont(string installationRoot, int baseImage, Sg3AssetCatalog? assets = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        legacyEncoding = Encoding.GetEncoding(1250, new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback);
        archivePath = Path.Combine(installationRoot, "Data", "Pharaoh_Fonts.sg3");
        this.assets = assets;
        archive = assets?.LoadArchive("Pharaoh_Fonts.sg3") ?? Sg3Archive.Load(archivePath);
        this.baseImage = baseImage;

        // The executable treats the small font and the two large heading fonts specially.
        // Measuring and drawing large headings intentionally use different advances.
        if (baseImage == 201)
        {
            measuredSpaceWidth = drawnSpaceWidth = 4;
            measuredGlyphSpacing = drawnGlyphSpacing = 1;
            lineAdvance = 14;
        }
        else if (baseImage is 871 or 1005)
        {
            measuredSpaceWidth = 10;
            drawnSpaceWidth = 8;
            measuredGlyphSpacing = 1;
            drawnGlyphSpacing = 0;
            lineAdvance = 28;
        }
        else
        {
            measuredSpaceWidth = drawnSpaceWidth = 6;
            measuredGlyphSpacing = drawnGlyphSpacing = 0;
            lineAdvance = 16;
        }
    }

    [OriginalAddress(0x004CBF30, "FUN_004cbf30")]
    public int Measure(Graphics graphics, string text) => Measure(text);

    [OriginalAddress(0x004CBF30, "FUN_004cbf30")]
    public int Measure(string text)
    {
        int width = 0;
        foreach (byte value in legacyEncoding.GetBytes(text))
        {
            if (value == 0x20) width += measuredSpaceWidth;
            else if (TryGetImage(value, out int imageIndex))
                width += archive.Images[imageIndex].Width + measuredGlyphSpacing;
        }
        return width;
    }

    [OriginalAddress(0x004CC720, "Horizontal alignment of a centered label")]
    [RewriteOnly("Rectangle alignment already computes the glyph-canvas origin; it must not subtract the baseline correction again")]
    public void DrawCentered(Graphics graphics, string text, Rectangle bounds)
    {
        int x = bounds.Left + (bounds.Width - Measure(graphics, text)) / 2;
        int y = bounds.Top + (bounds.Height - LineHeight) / 2;
        DrawRunes(graphics, text, ref x, y+3);
    }

    public void DrawCenteredAt(Graphics graphics, string text, int centerX, int top)
    {
        int x = centerX - Measure(graphics, text) / 2;
        DrawRunes(graphics, text, ref x, top);
    }

    [OriginalAddress(0x004CC770, "FUN_004cc770")]
    [OriginalAddress(0x004CCA60, "FUN_004cca60")]
    public void DrawAt(Graphics graphics, string text, int left, int top)
    {
        int x = left;
        DrawRunes(graphics, text, ref x, top);
    }

    [OriginalAddress(0x004C8680, "Help glyphs use an unshifted line origin")]
    public void DrawHelpAt(Graphics graphics,string text,int left,int top)
        => DrawAt(graphics,text,left,top+3);

    [OriginalAddress(0x00410B10, "FUN_00410b10")]
    [OriginalAddress(0x00410DE0, "FUN_00410de0")]
    [OriginalAddress(0x00410E80, "FUN_00410e80")]
    public void DrawAtSolidColor(Graphics graphics, string text, int left, int top, Color color)
    {
        int x = left;
        DrawRunes(graphics, text, ref x, top, color);
    }

    /// <summary>Draws the @P-delimited paragraphs used by Pharaoh_MM.eng briefings.</summary>
    public void DrawBriefingParagraphs(Graphics graphics, string text, Rectangle bounds)
    {
        string[] paragraphs = text.Split("@P", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int y = bounds.Top + lineAdvance;
        for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
        {
            if (paragraphIndex > 0) y += 5;
            string[] words = paragraphs[paragraphIndex]
                .Replace("@L", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int wordIndex = 0;
            bool firstLine = true;
            while (wordIndex < words.Length && y + LineHeight <= bounds.Bottom)
            {
                int left = bounds.Left + (firstLine ? 48 : 0);
                int limit = bounds.Right - left;
                StringBuilder line = new();
                while (wordIndex < words.Length)
                {
                    string candidate = line.Length == 0 ? words[wordIndex] : $"{line} {words[wordIndex]}";
                    if (line.Length > 0 && MeasureForWrap(candidate) >= limit) break;
                    line.Clear().Append(candidate);
                    wordIndex++;
                }
                DrawAt(graphics, line.ToString(), left, y);
                y += lineAdvance;
                firstLine = false;
            }
        }
    }

    private int MeasureForWrap(string text)
    {
        int width = 0;
        foreach (byte value in legacyEncoding.GetBytes(text))
        {
            if (value == 0x20) width += 4;
            else width += MeasureWrapGlyphAdvance(value);
        }
        return width;
    }

    [OriginalAddress(0x004CD010, "FUN_004cd010")]
    public void DrawWrapped(Graphics graphics, string text, Rectangle bounds)
    {
        byte[] source = legacyEncoding.GetBytes(text);
        int sourceOffset = 0;
        int y = bounds.Top;
        for (int lineNumber = 0; lineNumber < 100 && sourceOffset < source.Length; lineNumber++)
        {
            List<byte> line = new(200);
            int lineWidth = 0;
            while (lineWidth < bounds.Width && sourceOffset < source.Length)
            {
                WrapToken token = MeasureNextWrapToken(source.AsSpan(sourceOffset));
                lineWidth += token.Width;
                // The executable uses `limit <= accumulatedWidth`; an exactly full
                // token is deferred to the next line together with its leading space.
                if (lineWidth >= bounds.Width) break;

                int copyLength = Math.Min(token.ByteCount, 200 - line.Count);
                for (int index = 0; index < copyLength; index++)
                    line.Add(source[sourceOffset + index]);
                sourceOffset += token.ByteCount;
                if (token.ByteCount == 0) break;
            }

            if (line.Count == 0) break; // Matches valid game strings while avoiding the original oversized-word stall.
            DrawAt(graphics, legacyEncoding.GetString([.. line]), bounds.Left, y);
            y += lineAdvance;
            if (y + LineHeight > bounds.Bottom) return;
        }
    }

    [OriginalAddress(0x004CD120, "FUN_004cd120")]
    private WrapToken MeasureNextWrapToken(ReadOnlySpan<byte> source)
    {
        int width = 0;
        int byteCount = 0;
        bool sawPrintable = false;
        while (byteCount < source.Length && byteCount < 1999)
        {
            byte value = source[byteCount];
            if (value == 0x20)
            {
                if (sawPrintable) break;
                width += 4;
            }
            else if (value == 0x24)
            {
                if (sawPrintable) break;
            }
            else if (value > 0x1F)
            {
                width += MeasureWrapGlyphAdvance(value);
                sawPrintable = true;
            }
            byteCount++;
        }
        return new WrapToken(width, byteCount);
    }

    [OriginalAddress(0x004CC020, "FUN_004cc020")]
    private int MeasureWrapGlyphAdvance(byte value) =>
        TryGetImage(value, out int imageIndex) ? archive.Images[imageIndex].Width + 1 : 0;

    [OriginalAddress(0x004CD240, "FUN_004cd240")]
    [OriginalAddress(0x004CCA60, "FUN_004cca60")]
    private void DrawRunes(Graphics graphics, string text, ref int x, int y, Color? solidColor = null)
    {
        foreach (byte value in legacyEncoding.GetBytes(text))
        {
            if (value == 0x20)
            {
                x += drawnSpaceWidth;
                continue;
            }
            if (!TryGetImage(value, out int imageIndex)) continue;
            Sg3ImageRecord record = archive.Images[imageIndex];
            Bitmap glyph = solidColor is Color color
                ? GetSolidGlyph(imageIndex, color)
                : GetGlyph(imageIndex);
            int referenceHeight = baseImage switch { 201 => 9, 871 => 23, 1005 => 24, _ => 11 };
            int accent = value >= 0x80 && value != 0xe7
                ? Math.Max(0, record.Height - referenceHeight) : 0;
            graphics.DrawImageUnscaled(glyph, x, y - 3 - accent);
            x += record.Width + drawnGlyphSpacing;
        }
    }

    internal int LineHeight => archive.Images[baseImage].Height;

    private bool TryGetImage(byte value, out int imageIndex)
    {
        imageIndex = 0;
        if (value < 0x20 || value - 0x20 >= GlyphMap.Length) return false;
        byte glyph = GlyphMap[value - 0x20];
        if (glyph == 0 && value != 'a') return false;
        imageIndex = baseImage + glyph - 1;
        return imageIndex >= 0 && imageIndex < archive.Images.Count;
    }

    private Bitmap GetGlyph(int imageIndex)
    {
        if (!glyphs.TryGetValue(imageIndex, out Bitmap? bitmap))
        {
            bitmap = RgbaBitmap.Create(assets?.Decode("Pharaoh_Fonts.sg3", imageIndex) ??
                Sg3ImageDecoder.Decode(archivePath, archive, imageIndex));
            glyphs.Add(imageIndex, bitmap);
        }
        return bitmap;
    }

    [RewriteOnly("Caches the RGB565 solid-color mask produced by the original type-0x14 sprite blitters")]
    private Bitmap GetSolidGlyph(int imageIndex, Color color)
    {
        (int ImageIndex, int Argb) key = (imageIndex, color.ToArgb());
        if (solidGlyphs.TryGetValue(key, out Bitmap? tinted)) return tinted;

        Bitmap source = GetGlyph(imageIndex);
        tinted = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            byte alpha = source.GetPixel(x, y).A;
            if (alpha != 0) tinted.SetPixel(x, y, Color.FromArgb(alpha, color));
        }
        solidGlyphs.Add(key, tinted);
        return tinted;
    }

    public void Dispose()
    {
        foreach (Bitmap bitmap in solidGlyphs.Values) bitmap.Dispose();
        solidGlyphs.Clear();
        foreach (Bitmap bitmap in glyphs.Values) bitmap.Dispose();
        glyphs.Clear();
    }

    private readonly record struct WrapToken(int Width, int ByteCount);
}
