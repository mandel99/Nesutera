using Pharaoh.Assets.Text;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Loading;
using System.Buffers.Binary;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class MissionVictoryPanel
{
    [OriginalAddress(0x004194C0, "victory report and localized speech")]
    public static void DrawReport(Graphics graphics, LegacyUiSkin skin, LegacyBitmapFont heading,
        LegacyBitmapFont body, LocalizedTextCatalog text, int width, int height,
        LegacyMissionSession session, int? victoryTextIndex, string cityName)
    {
        int x = (width - 640) / 2 + 48;
        int y = (height - 480) / 2 + 16;
        skin.DrawOuterPanel(graphics, x, y, 34, 27);
        heading.DrawCentered(graphics, text.GetString(62, 0) ?? "Victory!", new Rectangle(x, y + 16, 544, 24));
        skin.DrawInnerPanel(graphics, x + 16, y + 56, 32, 7);
        if (victoryTextIndex is int index)
            body.DrawWrapped(graphics, (text.GetString(147, index) ?? string.Empty)
                .Replace("[city_name]", cityName, StringComparison.OrdinalIgnoreCase),
                new Rectangle(x + 32, y + 64, 496, 96));
        ReadOnlySpan<byte> city = session.LogicalChunks["city_data"].Span;
        ReadOnlySpan<int> offsets = [36004, 36008, 36016, 18916, 18940];
        ReadOnlySpan<int> labels = [0, 1, 3, 5, 4];
        for (int row = 0; row < offsets.Length; row++)
            body.DrawAt(graphics, $"{text.GetString(148, labels[row])} {BinaryPrimitives.ReadInt32LittleEndian(city[offsets[row]..])}",
                x + 40, y + 180 + row * 20);
        body.DrawCentered(graphics, text.GetString(13, 1) ?? string.Empty, new Rectangle(x + 16, y + 396, 512, 24));
    }

    [OriginalAddress(0x004191F0, "state 0x43 victory choice panel")]
    [OriginalAddress(0x00419DB0, "one or three victory choice buttons")]
    public static void Draw(Graphics graphics, LegacyUiSkin skin, LegacyBitmapFont heading,
        LegacyBitmapFont body, LocalizedTextCatalog text, int width, int height,
        bool allowContinuedGoverning, int hover, Action<Graphics, UiRect, bool> drawButton)
    {
        int x = (width - 640) / 2 + 48;
        int y = (height - 480) / 2 + 128;
        skin.DrawOuterPanel(graphics, x, y, 34, 15);
        heading.DrawCentered(graphics, text.GetString(62, 0) ?? "Victory!", new Rectangle(x, y + 16, 544, 24));
        body.DrawCentered(graphics, text.GetString(62, 2) ?? string.Empty, new Rectangle(x, y + 47, 544, 24));
        for (int index = 0; index < (allowContinuedGoverning ? 3 : 1); index++)
        {
            UiRect bounds = ButtonBounds(index, width, height);
            drawButton(graphics, bounds, hover == index);
            body.DrawCentered(graphics, text.GetString(62, 3 + index) ?? string.Empty,
                new Rectangle(bounds.Left, bounds.Top + 6, bounds.Width, 16));
        }
    }

    [OriginalAddress(0x00419DB0, "DAT_005C65D0 victory button rectangles")]
    public static UiRect ButtonBounds(int index, int width, int height) =>
        new((width - 640) / 2 + 80, (height - 480) / 2 + 240 + index * 32,
            (width - 640) / 2 + 560, (height - 480) / 2 + 260 + index * 32);

    [OriginalAddress(0x00419DB0, "DAT_005C65D0 input control count")]
    public static int HitTest(int x, int y, int width, int height, bool allowContinuedGoverning)
    {
        for (int index = 0; index < (allowContinuedGoverning ? 3 : 1); index++)
            if (ButtonBounds(index, width, height).Contains(new UiPoint(x, y))) return index;
        return -1;
    }
}
