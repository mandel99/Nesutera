using Pharaoh.Assets.Text;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.Managers;

internal enum CityUiLayer
{
    TopBarBackground,
    RightBar,
    ResolutionEdge,
    TopBarForeground,
    BuildMenu
}

internal readonly record struct TopBarStatusLayout(
    int CurrencyX,
    int TreasuryBase,
    int PopulationX,
    int PopulationBase,
    int MonthX,
    int YearBase);

/// <summary>Single source of city UI placement and painter order.</summary>
internal static class LayoutManager
{
    public const int TopBarHeight = 30;
    public const int CustomVisibleRows = 15;

    public static readonly UiRect CampaignBriefingBackBounds = new(24, 424, 55, 455);
    public static readonly UiRect CampaignBriefingPlayBounds = new(580, 424, 608, 455);
    public static readonly UiRect CampaignBriefingCityReturnBounds = new(480, 424, 608, 455);
    public static readonly UiRect CampaignDifficultyUpBounds = new(126, 432, 142, 448);
    public static readonly UiRect CampaignDifficultyDownBounds = new(143, 432, 160, 449);
    public static readonly UiRect CustomListBounds = new(16, 205, 280, 445);
    public static readonly UiRect CustomScrollUpBounds = new(287, 213, 314, 240);
    public static readonly UiRect CustomScrollDownBounds = new(287, 428, 314, 455);
    public static readonly UiRect CustomDifficultyUpBounds = new(368, 392, 385, 409);
    public static readonly UiRect CustomDifficultyDownBounds = new(385, 392, 402, 409);
    public static readonly UiRect DeleteNoBounds = new(272, 184, 304, 216);
    public static readonly UiRect DeleteYesBounds = new(336, 184, 368, 216);
    public static readonly UiRect NoMissionsYesBounds = new(112, 260, 320, 285);
    public static readonly UiRect NoMissionsNoBounds = new(320, 260, 528, 285);
    // FUN_00538940 dialog type 4: two thirds-wide choices at y = center + 28.
    public static readonly UiRect QuitYesBounds = new(224, 268, 320, 293);
    public static readonly UiRect QuitNoBounds = new(320, 268, 416, 293);

    private static readonly Rectangle[] ExpandedSidebarCommands =
    [
        new(9, 281, 34, 48), new(9, 330, 34, 48), new(9, 381, 34, 48),
        new(46, 281, 34, 48), new(46, 330, 34, 48), new(46, 381, 34, 48),
        new(86, 281, 34, 48), new(86, 330, 34, 48), new(86, 381, 34, 48),
        new(125, 281, 34, 48), new(125, 330, 34, 48), new(125, 381, 34, 48),
        new(9, 434, 35, 45), new(46, 434, 38, 45),
        new(86, 434, 28, 45), new(116, 434, 43, 45)
    ];

    private static readonly Rectangle[] CompactSidebarCommands =
    [
        new(9, 51, 27, 35), new(9, 87, 27, 34), new(9, 122, 27, 36),
        new(9, 159, 27, 33), new(9, 193, 27, 34), new(9, 228, 27, 34),
        new(9, 263, 27, 32), new(9, 296, 27, 35), new(9, 332, 27, 34),
        new(9, 368, 27, 35), new(9, 404, 27, 31), new(9, 436, 27, 39)
    ];

    private static readonly int[] BuildMenuVerticalOffsets =
    [
        44, 322, 306, 274, 258, 226, 210, 178,
        162, 130, 114, 82, 66, 34, 18
    ];

    [OriginalAddress(0x0051E990, "FUN_0051e990")]
    [OriginalAddress(0x0051FFD0, "FUN_0051ffd0")]
    public static void VisitCityLayers(bool includeBuildMenu, Action<CityUiLayer> draw)
    {
        draw(CityUiLayer.TopBarBackground);
        draw(CityUiLayer.RightBar);
        draw(CityUiLayer.ResolutionEdge);
        draw(CityUiLayer.TopBarForeground);
        if (includeBuildMenu)
            draw(CityUiLayer.BuildMenu);
    }

    [RewriteOnly("City viewport extended to the window bottom instead of preserving the original 6/9-pixel lower border")]
    public static Rectangle CityMapBounds(int displayWidth, int displayHeight, bool compact = false) =>
        new(0, TopBarHeight, SidebarLeft(displayWidth, compact),
            displayHeight - TopBarHeight);

    [OriginalAddress(0x0051E930, "FUN_0051e930")]
    public static int SidebarLeft(int displayWidth, bool compact) => displayWidth switch
    {
        640 => compact ? 598 : 478,
        800 => compact ? 718 : 598,
        1024 => compact ? 958 : 838,
        _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
    };

    public static Rectangle SidebarBounds(int displayWidth, int displayHeight, bool compact) =>
        new(SidebarLeft(displayWidth, compact), TopBarHeight,
            compact ? 42 : 162, displayHeight - TopBarHeight);

    public static int SidebarCommandCount(bool compact) =>
        (compact ? CompactSidebarCommands : ExpandedSidebarCommands).Length;

    public static Rectangle SidebarCommandBounds(int index, int displayWidth, bool compact, bool hitTest = false)
    {
        Rectangle[] commands = compact ? CompactSidebarCommands : ExpandedSidebarCommands;
        Rectangle bounds = commands[Math.Min(index, commands.Length - 1)];
        if (hitTest && compact)
            bounds.Offset(0, -6);
        bounds.Offset(SidebarLeft(displayWidth, compact), 0);
        return bounds;
    }

    public static Rectangle SidebarToggleBounds(int displayWidth, bool compact, bool hitTest = false)
    {
        Rectangle bounds = compact
            ? new Rectangle(9, hitTest ? 24 : 30, 33, 20)
            : new Rectangle(128, 30, 33, 20);
        bounds.Offset(SidebarLeft(displayWidth, compact), 0);
        return bounds;
    }

    [OriginalAddress(0x0051EFB0, "FUN_0051efb0")]
    public static Rectangle MinimapBounds(int displayWidth, int horizontalOffset = 0) =>
        new(
            SidebarLeft(displayWidth, compact: false) + 13 + horizontalOffset,
            59,
            LegacyCityMinimapProjection.PixelWidth,
            LegacyCityMinimapProjection.PixelHeight);

    public static Rectangle BuildMenuRowBounds(
        int columns,
        int entryCount,
        int row,
        int displayWidth,
        bool compact)
    {
        int width = columns * 16;
        int countIndex = Math.Min(entryCount, BuildMenuVerticalOffsets.Length - 1);
        int left = SidebarLeft(displayWidth, compact) - width - 5;
        int top = BuildMenuVerticalOffsets[countIndex] + 110 + row * 24;
        return new Rectangle(left, top, width, 24);
    }

    public static int BuildMenuCostX(int displayWidth, bool compact) => SidebarLeft(displayWidth, compact) - 82;

    [OriginalAddress(0x00425840, "FUN_00425840")]
    public static Rectangle RotationControlBounds(int displayWidth)
    {
        int left = displayWidth switch
        {
            640 => 475,
            800 => 605,
            1024 => 838,
            _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
        };
        return new Rectangle(left, 0, 36, 21);
    }

    public static TopBarStatusLayout TopBarStatus(int displayWidth) => displayWidth switch
    {
        640 => new(250, 245, 375, 367, 515, 504),
        800 => new(343, 339, 470, 466, 655, 655),
        1024 => new(495, 501, 645, 651, 883, 883),
        _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
    };

    [RewriteOnly("Computes the mutable right edges written by FUN_00425330")]
    public static IReadOnlyList<Rectangle> TopMenuBounds(
        Graphics graphics,
        LegacyBitmapFont font,
        LocalizedTextCatalog text)
    {
        List<Rectangle> bounds = [];
        int x = 10;
        foreach (LegacyCityMenuDefinition menu in LegacyCityTopMenu.Menus)
        {
            string label = text.GetString(menu.TextGroup, 0) ?? string.Empty;
            int width = font.Measure(graphics, label);
            bounds.Add(new Rectangle(x, 6, width, 12));
            x += width + 10;
        }
        return bounds;
    }

    [OriginalAddress(0x00425430, "FUN_00425430")]
    public static Rectangle TopMenuDropdownBounds(
        Graphics graphics,
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        int menuIndex)
    {
        LegacyCityMenuDefinition menu = LegacyCityTopMenu.GetMenu(menuIndex);
        int measured = 1;
        for (int index = 1; text.GetString(menu.TextGroup, index) is string value; index++)
            measured = Math.Max(measured, font.Measure(graphics, value));
        int width = (measured / 16 + 2) * 16;
        int left = TopMenuBounds(graphics, font, text)[menuIndex].Left;
        int height = ((menu.Items.Count * 20 + 20) / 16) * 16;
        return new Rectangle(left, 24, width, height);
    }

    public static Point OptionsOrigin(int displayWidth, int displayHeight) =>
        new((displayWidth - 640) / 2, (displayHeight - 480) / 2);

    [RewriteOnly("Exact hit rectangles from tables 005C2E20, 005C2EB0, 005C3038, 005C3070, 005C8D98, 005C8DF8 and 005C8EE8")]
    public static Rectangle[] OptionsControlBounds(LegacyCitySettingsPage page) => page switch
    {
        LegacyCitySettingsPage.Display => Enumerable.Range(0, 5).Select(i => new Rectangle(128, 136 + i * 24, 224, 20)).ToArray(),
        LegacyCitySettingsPage.Sound =>
        [new(64, 162, 224, 20), new(64, 192, 224, 20), new(64, 222, 224, 20), new(64, 252, 224, 20),
         new(320, 160, 20, 24), new(344, 160, 20, 24), new(320, 190, 20, 24), new(344, 190, 20, 24),
         new(320, 220, 20, 24), new(344, 220, 20, 24), new(320, 250, 20, 24), new(344, 250, 20, 24),
         new(144, 296, 192, 20), new(144, 326, 192, 20)],
        LegacyCitySettingsPage.Speed =>
        [new(240, 140, 20, 24), new(264, 140, 20, 24), new(240, 176, 20, 24), new(264, 176, 20, 24),
         new(144, 232, 192, 20), new(144, 262, 192, 20)],
        LegacyCitySettingsPage.PopupMessages => Enumerable.Range(0, 12)
            .Select(i => new Rectangle(128, 116 + i * 24, 224, 20))
            .Concat([new Rectangle(128, 428, 224, 20), new Rectangle(128, 452, 224, 20)]).ToArray(),
        LegacyCitySettingsPage.Difficulty => [new(288, 166, 20, 24), new(312, 166, 20, 24)],
        _ => []
    };
}
