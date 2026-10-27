using System.Globalization;
using Pharaoh.Assets.Text;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class TopBar
{
    public static void DrawBackground(Graphics graphics, LegacyUiSkin skin, int displayWidth)
    {
        System.Drawing.Drawing2D.GraphicsState saved = graphics.Save();
        graphics.SetClip(new Rectangle(0, 0, displayWidth, LayoutManager.TopBarHeight));
        skin.DrawCityTopBar(graphics, displayWidth);
        graphics.Restore(saved);
    }

    [OriginalAddress(0x0051F0A0, "FUN_0051f0a0")]
    public static void DrawForeground(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont menuFont,
        LegacyBitmapFont negativeTreasuryFont,
        LegacyBitmapFont yellowFont,
        LocalizedTextCatalog text,
        LegacyCityHudState state,
        LegacyCityOptions options,
        int? openMenu,
        int? hoveredMenuItem,
        int? hoveredRotationSegment,
        int displayWidth)
    {
        DrawStatus(graphics, menuFont, negativeTreasuryFont, text, state, displayWidth);
        skin.DrawCityRotationControl(graphics, LayoutManager.RotationControlBounds(displayWidth), hoveredRotationSegment);
        DrawMenus(graphics, skin, menuFont, yellowFont, text, options, openMenu, hoveredMenuItem);
    }

    private static void DrawStatus(
        Graphics graphics,
        LegacyBitmapFont menuFont,
        LegacyBitmapFont negativeTreasuryFont,
        LocalizedTextCatalog text,
        LegacyCityHudState state,
        int displayWidth)
    {
        string currency = text.GetString(6, 0) ?? "Db";
        string population = text.GetString(6, 1) ?? "Pop";
        string month = text.GetString(25, Math.Clamp(state.MonthIndex, 0, 11)) ?? "January";
        TopBarStatusLayout layout = LayoutManager.TopBarStatus(displayWidth);
        LegacyBitmapFont treasuryFont = state.Treasury < 0 ? negativeTreasuryFont : menuFont;
        treasuryFont.DrawAt(graphics, currency, layout.CurrencyX, 5);
        treasuryFont.DrawAt(graphics, $" {state.Treasury.ToString(CultureInfo.InvariantCulture)}",
            layout.TreasuryBase + treasuryFont.Measure(graphics, currency), 5);
        menuFont.DrawAt(graphics, population, layout.PopulationX, 5);
        menuFont.DrawAt(graphics, $" {state.Population.ToString(CultureInfo.InvariantCulture)}",
            layout.PopulationBase + menuFont.Measure(graphics, population), 5);
        menuFont.DrawAt(graphics, month, layout.MonthX, 5);
        DrawYear(graphics, menuFont, text, state.Year,
            layout.YearBase + menuFont.Measure(graphics, month), displayWidth);
    }

    [OriginalAddress(0x00425330, "FUN_00425330")]
    private static void DrawMenus(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont font,
        LegacyBitmapFont yellowFont,
        LocalizedTextCatalog text,
        LegacyCityOptions options,
        int? openMenu,
        int? hoveredMenuItem)
    {
        IReadOnlyList<Rectangle> labels = LayoutManager.TopMenuBounds(graphics, font, text);
        for (int index = 0; index < labels.Count; index++)
        {
            string label = text.GetString(LegacyCityTopMenu.GetMenu(index).TextGroup, 0) ?? string.Empty;
            font.DrawAt(graphics, label, labels[index].Left, 6);
        }
        if (openMenu is not int menuIndex || (uint)menuIndex >= (uint)labels.Count)
            return;

        LegacyCityMenuDefinition menu = LegacyCityTopMenu.GetMenu(menuIndex);
        Rectangle popup = LayoutManager.TopMenuDropdownBounds(graphics, font, text, menuIndex);
        skin.DrawMenuPanel(graphics, popup.Left, popup.Top, popup.Width / 16, popup.Height / 16);
        for (int itemIndex = 0; itemIndex < menu.Items.Count; itemIndex++)
        {
            LegacyCityMenuItem item = menu.Items[itemIndex];
            string label = text.GetString(menu.TextGroup,
                LegacyCityTopMenu.ResolveTextIndex(item, options)) ?? string.Empty;
            (hoveredMenuItem == itemIndex ? yellowFont : font).DrawAt(
                graphics, label, popup.Left + 8, popup.Top + 12 + itemIndex * 20);
        }
    }

    [OriginalAddress(0x00426100, "FUN_00426100")]
    public static int? HitTestMenu(
        Graphics graphics, LegacyBitmapFont font, LocalizedTextCatalog text, int x, int y)
    {
        IReadOnlyList<Rectangle> bounds = LayoutManager.TopMenuBounds(graphics, font, text);
        for (int index = 0; index < bounds.Count; index++)
            if (bounds[index].Contains(x, y))
                return index;
        return null;
    }

    [OriginalAddress(0x00426150, "FUN_00426150")]
    public static int? HitTestDropdown(
        Graphics graphics,
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        int menuIndex,
        int x,
        int y)
    {
        Rectangle popup = LayoutManager.TopMenuDropdownBounds(graphics, font, text, menuIndex);
        int relativeY = y - popup.Top - 12;
        if (x < popup.Left || x >= popup.Right || relativeY < 0)
            return null;
        int item = relativeY / 20;
        return item < LegacyCityTopMenu.GetMenu(menuIndex).Items.Count && relativeY % 20 < 15
            ? item
            : null;
    }

    [OriginalAddress(0x00425840, "FUN_00425840")]
    public static int? HitTestRotationControl(int x, int y, int displayWidth)
    {
        Rectangle bounds = LayoutManager.RotationControlBounds(displayWidth);
        if (!bounds.Contains(x, y))
            return null;
        int relativeX = x - bounds.Left;
        return relativeX < bounds.Width / 3 ? 1 :
            relativeX >= bounds.Width * 2 / 3 ? 2 : 0;
    }

    private static void DrawYear(
        Graphics graphics,
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        int year,
        int left,
        int displayWidth)
    {
        string bc = text.GetString(20, 0) ?? "BC";
        string ad = text.GetString(20, 1) ?? "AD";
        if (year < 0)
        {
            string value = $" {(-year).ToString(CultureInfo.InvariantCulture)} ";
            font.DrawAt(graphics, value, left, 5);
            int eraLeft = left + font.Measure(graphics, value);
            if (displayWidth == 640)
                eraLeft -= 11;
            font.DrawAt(graphics, bc, eraLeft, 5);
            return;
        }
        font.DrawAt(graphics, ad, left, 5);
        font.DrawAt(graphics, $" {year.ToString(CultureInfo.InvariantCulture)} ",
            left + font.Measure(graphics, ad), 5);
    }
}
