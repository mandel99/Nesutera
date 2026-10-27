using System.Drawing.Drawing2D;
using Pharaoh.Assets.Text;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class RightBar
{
    private const int ExpandedWidth = 162;
    private const int UpperHeight = 450;
    private const int CategoryCount = 12;
    private const int ExpandedCommandImageBase = 2440;
    private const int CompactCommandImageBase = 2530;
    [OriginalAddress(0x0051E990, "FUN_0051e990")]
    [OriginalAddress(0x0051FFD0, "FUN_0051ffd0")]
    public static void Draw(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont smallFont,
        LegacyBitmapFont normalFont,
        LocalizedTextCatalog text,
        Bitmap? minimapFrame,
        LegacyBuildMenuState? buildMenuState,
        bool undoAvailable,
        int selectedCommand,
        int? hoveredCommand,
        bool compact,
        bool toggleHovered,
        int? animationOffset,
        int displayWidth,
        int displayHeight)
    {
        if (animationOffset is int slideOffset)
        {
            skin.DrawCitySidebar(graphics, displayWidth, compact: true);
            DrawButtons(graphics, skin, buildMenuState, undoAvailable, selectedCommand, null,
                compact: true, toggleHovered: false, displayWidth);
            int left = LayoutManager.SidebarLeft(displayWidth, compact: false) + slideOffset;
            graphics.FillRectangle(Brushes.Black,
                new Rectangle(left, LayoutManager.TopBarHeight, ExpandedWidth, UpperHeight));
            skin.DrawCitySidebar(graphics, displayWidth, compact: false, slideOffset);
        }
        else
        {
            GraphicsState saved = graphics.Save();
            graphics.SetClip(LayoutManager.SidebarBounds(displayWidth, displayHeight, compact));
            skin.DrawCitySidebar(graphics, displayWidth, compact);
            graphics.Restore(saved);
        }

        if (animationOffset is int animatedOffset)
        {
            DrawExpandedContents(graphics, skin, normalFont, text, minimapFrame,
                buildMenuState, undoAvailable, selectedCommand, null, false, displayWidth, animatedOffset);
        }
        else if (!compact)
        {
            DrawExpandedContents(graphics, skin, normalFont, text, minimapFrame,
                buildMenuState, undoAvailable, selectedCommand, hoveredCommand, toggleHovered, displayWidth, 0);
            DrawNotifications(graphics, smallFont, displayWidth);
        }
        else
        {
            DrawButtons(graphics, skin, buildMenuState, undoAvailable, selectedCommand, hoveredCommand,
                compact: true, toggleHovered, displayWidth);
        }
    }

    [OriginalAddress(0x0051ED30, "FUN_0051ed30")]
    [OriginalAddress(0x0051EDD0, "FUN_0051edd0")]
    [OriginalAddress(0x0051FD60, "FUN_0051fd60")]
    private static void DrawExpandedContents(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        Bitmap? minimapFrame,
        LegacyBuildMenuState? buildMenuState,
        bool undoAvailable,
        int selectedCommand,
        int? hoveredCommand,
        bool toggleHovered,
        int displayWidth,
        int horizontalOffset)
    {
        font.DrawCentered(graphics, text.GetString(6, 4) ?? "Overlays",
            new Rectangle(LayoutManager.SidebarLeft(displayWidth, false) + horizontalOffset + 11, 33, 117, 19));
        Minimap.Draw(graphics, minimapFrame, displayWidth, horizontalOffset);
        DrawButtons(graphics, skin, buildMenuState, undoAvailable, selectedCommand, hoveredCommand,
            compact: false, toggleHovered, displayWidth, horizontalOffset);
    }

    [OriginalAddress(0x0051EC90, "FUN_0051ec90")]
    private static void DrawButtons(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBuildMenuState? buildMenuState,
        bool undoAvailable,
        int selectedCommand,
        int? hoveredCommand,
        bool compact,
        bool toggleHovered,
        int displayWidth,
        int horizontalOffset = 0)
    {
        int sidebarLeft = LayoutManager.SidebarLeft(displayWidth, compact) + horizontalOffset;
        Rectangle toggle = LayoutManager.SidebarToggleBounds(displayWidth, compact);
        toggle.Offset(horizontalOffset, 0);
        skin.DrawCitySidebarControl(graphics, compact ? 2522 : 2519, toggle, false, toggleHovered);

        int imageBase = compact ? CompactCommandImageBase : ExpandedCommandImageBase;
        for (int index = 0; index < LayoutManager.SidebarCommandCount(compact); index++)
        {
            Rectangle bounds = LayoutManager.SidebarCommandBounds(index, displayWidth, compact);
            bounds.Offset(horizontalOffset, 0);
            bool enabled = index == CategoryCount
                ? undoAvailable
                : index > CategoryCount || (buildMenuState?.IsCommandEnabled(index) ?? false);
            bool flashing = !compact && enabled && (buildMenuState?.NeedsAttention(index) ?? false);
            bool attention = flashing && (buildMenuState?.AdvanceAttentionFrame(index) ?? false);
            skin.DrawCitySidebarControl(graphics, imageBase + index * 4, bounds,
                enabled && index == selectedCommand,
                flashing ? attention : enabled && index == hoveredCommand,
                enabled);
        }

        if (!compact)
        {
            int activeCategory = selectedCommand >= 0 &&
                selectedCommand < (buildMenuState?.Categories.Count ?? 0)
                    ? buildMenuState!.GetCategory(selectedCommand).OriginalCategory
                    : 0;
            skin.DrawCityCategoryIcon(graphics, sidebarLeft + 11, 211, activeCategory);
            skin.DrawCitySidebarControl(graphics, 2504,
                Offset(new Rectangle(16, 173, 60, 36), sidebarLeft), false, false);
            skin.DrawCitySidebarControl(graphics, 2508,
                Offset(new Rectangle(90, 173, 62, 36), sidebarLeft), false, false);
        }
    }

    [OriginalAddress(0x0051F680, "FUN_0051f680")]
    private static void DrawNotifications(Graphics graphics, LegacyBitmapFont font, int displayWidth) =>
        font.DrawCentered(graphics, "0", new Rectangle(LayoutManager.SidebarLeft(displayWidth, false) + 36, 448, 24, 18));

    [OriginalAddress(0x00425990, "FUN_00425990")]
    public static int? HitTestCommand(
        int x,
        int y,
        int displayWidth,
        bool compact,
        LegacyBuildMenuState? buildMenuState,
        bool undoAvailable)
    {
        for (int index = 0; index < LayoutManager.SidebarCommandCount(compact); index++)
        {
            bool enabled = index == CategoryCount
                ? undoAvailable
                : index > CategoryCount || (buildMenuState?.IsCommandEnabled(index) ?? false);
            if (enabled &&
                LayoutManager.SidebarCommandBounds(index, displayWidth, compact, hitTest: true).Contains(x, y))
            {
                return index;
            }
        }
        return null;
    }

    [OriginalAddress(0x004D8310, "FUN_004d8310")]
    public static bool HitTestToggle(int x, int y, int displayWidth, bool compact) =>
        LayoutManager.SidebarToggleBounds(displayWidth, compact, hitTest: true).Contains(x, y);

    private static Rectangle Offset(Rectangle relative, int left)
    {
        relative.Offset(left, 0);
        return relative;
    }
}
