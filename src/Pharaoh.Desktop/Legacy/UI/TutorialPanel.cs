using System.Drawing.Drawing2D;
using System.Text;
using Pharaoh.Assets.Text;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class TutorialPanel
{
    private const int Width = 640;
    private const int Height = 480;
    private const int LineHeight = 16;
    // FUN_004C5E60 derives these values from a 40x30 message box. With no
    // header graphic, the content panel starts at y + 48, text at y + 54,
    // and DAT_00D3B13C exposes 22 scrollable text rows. The previous values
    // were one complete 16-pixel row too low/tall.
    private const int ContentPanelTop = 48;
    private const int ContentTextTop = 54;
    private const int ContentPanelRows = 23;
    private const int VisibleLines = 22;
    private const int ScrollPanelRows = 21;
    private const int NextButtonTop = 390;
    private const int ScrollTrackRange = 291;
    // FUN_004C8070 receives 560 pixels. The managed bitmap-font measurement
    // needs an eight-pixel compensation to reproduce its word wrapping.
    private const int ScrollContentWidth = 552;

    private sealed record LayoutItem(string? Text, int Graphic, int X, int Line, int HeightLines);

    [OriginalAddress(0x004C5E60, "message panel renderer")]
    [OriginalAddress(0x004C8070, "@P/@L/@G rich-message layout")]
    public static void Draw(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont titleFont,
        LegacyBitmapFont bodyFont,
        MessageEntry message,
        int scrollLine,
        int displayWidth,
        int displayHeight,
        Bitmap scrollUp,
        Bitmap scrollUpHover,
        Bitmap scrollDown,
        Bitmap scrollDownHover,
        Bitmap scrollThumb,
        Bitmap close,
        Bitmap closeHover,
        int scrollHover,
        Func<int, Bitmap?> resolveGraphic)
    {
        int left = (displayWidth - Width) / 2;
        int top = (displayHeight - Height) / 2;
        skin.DrawOuterPanel(graphics, left, top, 40, 30);
        titleFont.DrawCentered(graphics, message.Title,
            new Rectangle(left + 32, top + 20, Width - 64, 24));

        IReadOnlyList<LayoutItem> items = Layout(message.Text, bodyFont, ScrollContentWidth, resolveGraphic);
        int totalLines = items.Count == 0 ? 0 : items.Max(item => item.Line + item.HeightLines);
        int selectedLine = Math.Clamp(scrollLine, 0, Math.Max(0, totalLines - VisibleLines));
        int maximumScroll = Math.Max(0, totalLines - VisibleLines);
        if (maximumScroll > 0)
        {
            skin.DrawInnerPanel(graphics, left + 594, top + 74, 2, ScrollPanelRows);
            skin.DrawInnerPanel(graphics, left + 16, top + ContentPanelTop, 36, ContentPanelRows);
        }
        else
        {
            skin.DrawInnerPanel(graphics, left + 16, top + ContentPanelTop, 38, ContentPanelRows);
        }

        Rectangle clip = new(
            left + 24, top + ContentTextTop, ScrollContentWidth, VisibleLines * LineHeight);
        GraphicsState state = graphics.Save();
        graphics.SetClip(clip);
        foreach (LayoutItem item in items)
        {
            int y = clip.Top + (item.Line - selectedLine) * LineHeight;
            if (item.Graphic > 0)
            {
                Bitmap? image = resolveGraphic(item.Graphic);
                if (image is not null && y < clip.Bottom && y + image.Height > clip.Top)
                    graphics.DrawImageUnscaled(image, clip.Left + (ScrollContentWidth - image.Width) / 2, y + 8);
            }
            else if (item.Text is not null && y >= clip.Top - LineHeight && y < clip.Bottom)
            {
                bodyFont.DrawAt(graphics, item.Text, clip.Left + item.X, y);
            }
        }
        graphics.Restore(state);

        if (maximumScroll > 0)
        {
            Rectangle previous = PreviousBounds(displayWidth, displayHeight);
            Rectangle next = NextBounds(displayWidth, displayHeight);
            graphics.DrawImageUnscaled(scrollHover == -1 ? scrollUpHover : scrollUp, previous.Location);
            graphics.DrawImageUnscaled(scrollHover == 1 ? scrollDownHover : scrollDown, next.Location);
            graphics.DrawImageUnscaled(scrollThumb,
                ThumbBounds(selectedLine, maximumScroll, displayWidth, displayHeight, scrollThumb.Size).Location);
        }
        graphics.DrawImageUnscaled(
            scrollHover == 2 ? closeHover : close,
            CloseBounds(displayWidth, displayHeight).Location);
    }

    public static int HitTestScrollButton(int x, int y, int displayWidth, int displayHeight)
    {
        if (PreviousBounds(displayWidth, displayHeight).Contains(x, y)) return -1;
        if (NextBounds(displayWidth, displayHeight).Contains(x, y)) return 1;
        return 0;
    }

    [OriginalAddress(0x004C7340, "message close-button submission")]
    public static bool IsCloseButton(int x, int y, int displayWidth, int displayHeight) =>
        CloseBounds(displayWidth, displayHeight).Contains(x, y);

    [OriginalAddress(0x004C72B0, "message scroll-thumb percentage and position")]
    public static Rectangle ThumbBounds(
        int scrollLine,
        int maximumScroll,
        int displayWidth,
        int displayHeight,
        Size thumbSize)
    {
        Rectangle previous = PreviousBounds(displayWidth, displayHeight);
        int clampedLine = Math.Clamp(scrollLine, 0, maximumScroll);
        int percentage = maximumScroll == 0 ? 0 : clampedLine * 100 / maximumScroll;
        int offset = ScrollTrackRange * percentage / 100;
        return new Rectangle(previous.Left + 7, previous.Bottom + offset,
            thumbSize.Width, thumbSize.Height);
    }

    [OriginalAddress(0x004C7DB0, "message scroll-track pointer to first visible row")]
    public static int ScrollLineFromThumbTop(
        int thumbTop,
        int maximumScroll,
        int displayWidth,
        int displayHeight)
    {
        int trackTop = PreviousBounds(displayWidth, displayHeight).Bottom;
        int offset = Math.Clamp(thumbTop - trackTop, 0, ScrollTrackRange);
        int percentage = offset * 100 / ScrollTrackRange;
        return maximumScroll * percentage / 100;
    }

    public static bool IsScrollTrack(int x, int y, int displayWidth, int displayHeight)
    {
        Rectangle previous = PreviousBounds(displayWidth, displayHeight);
        return new Rectangle(previous.Left + 2, previous.Bottom, 41, ScrollTrackRange + 1)
            .Contains(x, y);
    }

    public static int MaximumScroll(
        MessageEntry message,
        LegacyBitmapFont bodyFont,
        Func<int, Bitmap?> resolveGraphic)
    {
        IReadOnlyList<LayoutItem> items = Layout(message.Text, bodyFont, ScrollContentWidth, resolveGraphic);
        int totalLines = items.Count == 0 ? 0 : items.Max(item => item.Line + item.HeightLines);
        return Math.Max(0, totalLines - VisibleLines);
    }

    private static Rectangle PreviousBounds(int displayWidth, int displayHeight) =>
        new((displayWidth - Width) / 2 + Width - 49,
            (displayHeight - Height) / 2 + ContentPanelTop, 26, 26);

    private static Rectangle NextBounds(int displayWidth, int displayHeight) =>
        new((displayWidth - Width) / 2 + Width - 49,
            (displayHeight - Height) / 2 + NextButtonTop, 26, 26);

    // FUN_004C7340 submits DAT_005C7DD0 at box width - 0x26 and
    // box height - 0x24. The descriptor itself is a 24x24 button.
    private static Rectangle CloseBounds(int displayWidth, int displayHeight) =>
        new((displayWidth - Width) / 2 + Width - 38,
            (displayHeight - Height) / 2 + Height - 36, 24, 24);

    private static IReadOnlyList<LayoutItem> Layout(
        string source,
        LegacyBitmapFont font,
        int width,
        Func<int, Bitmap?> resolveGraphic)
    {
        List<LayoutItem> result = [];
        StringBuilder lineText = new();
        int line = 0;
        int indent = 0;

        void FlushLine(bool force)
        {
            if (lineText.Length > 0)
            {
                result.Add(new LayoutItem(lineText.ToString(), 0, indent, line, 1));
                lineText.Clear();
                line++;
                indent = 0;
            }
            else if (force)
            {
                line++;
                indent = 0;
            }
        }

        void AppendText(string text)
        {
            foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = lineText.Length == 0 ? word : $"{lineText} {word}";
                if (lineText.Length > 0 && font.Measure(candidate) >= width - indent)
                {
                    FlushLine(false);
                    candidate = word;
                }
                lineText.Clear().Append(candidate);
            }
        }

        int plainStart = 0;
        for (int index = 0; index <= source.Length; index++)
        {
            bool command = index + 1 < source.Length && source[index] == '@' &&
                source[index + 1] is 'P' or 'L' or 'G';
            if (index != source.Length && !command) continue;
            if (index > plainStart) AppendText(source[plainStart..index]);
            if (index == source.Length) break;

            char kind = source[index + 1];
            index += 1;
            if (kind == 'P')
            {
                FlushLine(false);
                indent = 48;
            }
            else if (kind == 'L')
            {
                FlushLine(true);
            }
            else
            {
                FlushLine(false);
                int value = 0;
                while (index + 1 < source.Length && char.IsDigit(source[index + 1]))
                    value = checked(value * 10 + source[++index] - '0');
                Bitmap? image = resolveGraphic(value);
                // FUN_004C8070 reserves ceil(height / 16) + 2 rows for @G.
                // The former +4 inserted two spurious blank rows after every
                // tutorial graphic and pushed the following section into the
                // bottom frame even when the scroll position was zero.
                int imageLines = image is null ? 1 :
                    (image.Height + LineHeight - 1) / LineHeight + 2;
                result.Add(new LayoutItem(null, value, 0, line, imageLines));
                line += imageLines;
            }
            plainStart = index + 1;
        }
        FlushLine(false);
        return result;
    }
}
