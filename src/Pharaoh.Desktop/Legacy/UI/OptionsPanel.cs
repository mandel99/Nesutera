using Pharaoh.Assets.Text;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.UI;

/// <summary>Owns the original city-options panel rendering and pointer geometry.</summary>
internal sealed class OptionsPanel(
    LegacyUiSkin skin,
    LegacyBitmapFont normalFont,
    LegacyBitmapFont selectedFont,
    LocalizedTextCatalog text,
    Bitmap upIcon,
    Bitmap upHoverIcon,
    Bitmap downIcon,
    Bitmap downHoverIcon)
{
    [RewriteOnly("Dispatches the five original option-state renderers")]
    public void Draw(
        Graphics graphics,
        LegacyCitySettingsPage page,
        LegacyCityOptions options,
        int? hoveredControl,
        int displayWidth,
        int displayHeight)
    {
        Point origin = LayoutManager.OptionsOrigin(displayWidth, displayHeight);
        System.Drawing.Drawing2D.GraphicsState saved = graphics.Save();
        graphics.TranslateTransform(origin.X, origin.Y);
        switch (page)
        {
            case LegacyCitySettingsPage.Display: DrawDisplay(graphics, options, hoveredControl); break;
            case LegacyCitySettingsPage.Sound: DrawSound(graphics, options, hoveredControl); break;
            case LegacyCitySettingsPage.Speed: DrawSpeed(graphics, options, hoveredControl); break;
            case LegacyCitySettingsPage.PopupMessages: DrawPopup(graphics, options, hoveredControl); break;
            case LegacyCitySettingsPage.Difficulty: DrawDifficulty(graphics, options, hoveredControl); break;
        }
        graphics.Restore(saved);
    }

    [OriginalAddress(0x00531190, "FUN_00531190")]
    private void DrawDisplay(Graphics graphics, LegacyCityOptions options, int? hoveredControl)
    {
        skin.DrawOuterPanel(graphics, 96, 80, 18, 12);
        normalFont.DrawCentered(graphics, text.GetString(42, 0) ?? "Display options", new Rectangle(128, 94, 224, 20));
        string mode = text.GetString(42, options.FullScreen ? 1 : 2) ??
            (options.FullScreen ? "Full screen" : "Windowed screen");
        string[] labels = [mode, text.GetString(42, 3) ?? "640 x 480 resolution",
            text.GetString(42, 4) ?? "800 x 600 resolution", text.GetString(42, 5) ?? "1024 x 768 resolution",
            text.GetString(42, 6) ?? "Cancel"];
        for (int index = 0; index < labels.Length; index++)
            DrawOptionButton(graphics, new Rectangle(128, 136 + index * 24, 224, 20), labels[index], index, hoveredControl);
    }

    [OriginalAddress(0x00531EF0, "FUN_00531ef0")]
    private void DrawSound(Graphics graphics, LegacyCityOptions options, int? hoveredControl)
    {
        skin.DrawOuterPanel(graphics, 48, 80, 24, 18);
        normalFont.DrawCentered(graphics, text.GetString(46, 0) ?? "Sound options", new Rectangle(96, 92, 288, 20));
        normalFont.DrawAt(graphics, text.GetString(46, 10) ?? "Current audio state", 112, 142);
        normalFont.DrawAt(graphics, text.GetString(46, 11) ?? "Volume", 336, 142);
        bool[] enabled = [options.MusicEnabled, options.SpeechEnabled, options.EffectsEnabled, options.CitySoundsEnabled];
        int[] values = [options.MusicVolume, options.SpeechVolume, options.EffectsVolume, options.CityVolume];
        int[] offText = [1, 3, 5, 7];
        for (int row = 0; row < 4; row++)
        {
            int y = 162 + row * 30;
            DrawOptionButton(graphics, new Rectangle(64, y, 224, 20),
                text.GetString(46, offText[row] + (enabled[row] ? 1 : 0)) ?? string.Empty, row, hoveredControl);
            normalFont.DrawAt(graphics, values[row].ToString(), 370, y + 4);
            DrawArrow(graphics, 320, y - 2, increase: false, 4 + row * 2, hoveredControl);
            DrawArrow(graphics, 344, y - 2, increase: true, 5 + row * 2, hoveredControl);
        }
        DrawOptionButton(graphics, new Rectangle(144, 296, 192, 20), text.GetString(46, 12) ?? "OK", 12, hoveredControl);
        DrawOptionButton(graphics, new Rectangle(144, 326, 192, 20), text.GetString(46, 9) ?? "Cancel", 13, hoveredControl);
    }

    [OriginalAddress(0x00531CC0, "FUN_00531cc0")]
    private void DrawSpeed(Graphics graphics, LegacyCityOptions options, int? hoveredControl)
    {
        skin.DrawOuterPanel(graphics, 96, 80, 18, 14);
        normalFont.DrawCentered(graphics, text.GetString(45, 0) ?? "Speed options", new Rectangle(128, 92, 224, 20));
        normalFont.DrawAt(graphics, text.GetString(45, 2) ?? "Game speed", 144, 146);
        normalFont.DrawAt(graphics, options.GameSpeed.ToString(), 296, 146);
        normalFont.DrawAt(graphics, text.GetString(45, 3) ?? "Scroll speed", 144, 182);
        normalFont.DrawAt(graphics, options.ScrollSpeed.ToString(), 296, 182);
        DrawArrow(graphics, 240, 140, increase: false, 0, hoveredControl);
        DrawArrow(graphics, 264, 140, increase: true, 1, hoveredControl);
        DrawArrow(graphics, 240, 176, increase: false, 2, hoveredControl);
        DrawArrow(graphics, 264, 176, increase: true, 3, hoveredControl);
        DrawOptionButton(graphics, new Rectangle(144, 232, 192, 20), text.GetString(45, 4) ?? "OK", 4, hoveredControl);
        DrawOptionButton(graphics, new Rectangle(144, 262, 192, 20), text.GetString(45, 1) ?? "Cancel", 5, hoveredControl);
    }

    [OriginalAddress(0x00531480, "FUN_00531480")]
    private void DrawPopup(Graphics graphics, LegacyCityOptions options, int? hoveredControl)
    {
        skin.DrawOuterPanel(graphics, 32, 38, 26, 28);
        normalFont.DrawCentered(graphics, text.GetString(310, 0) ?? "Popup Messages", new Rectangle(48, 60, 384, 20));
        normalFont.DrawCentered(graphics, text.GetString(310, 1) ?? string.Empty, new Rectangle(48, 95, 384, 20));
        for (int row = 0; row < 12; row++)
        {
            Rectangle bounds = new(128, 116 + row * 24, 224, 20);
            skin.DrawButton(graphics, bounds, hoveredControl == row);
            LegacyBitmapFont font = (options.PopupMessageMask & (1 << (row + 1))) != 0 ? selectedFont : normalFont;
            font.DrawCentered(graphics, text.GetString(310, row + 2) ?? string.Empty, bounds);
        }
        DrawOptionButton(graphics, new Rectangle(128, 428, 224, 20), text.GetString(310, 14) ?? "Ok", 12, hoveredControl);
        DrawOptionButton(graphics, new Rectangle(128, 452, 224, 20), text.GetString(310, 15) ?? "Cancel", 13, hoveredControl);
    }

    [OriginalAddress(0x00531BE0, "FUN_00531be0")]
    private void DrawDifficulty(Graphics graphics, LegacyCityOptions options, int? hoveredControl)
    {
        skin.DrawOuterPanel(graphics, 48, 80, 24, 12);
        normalFont.DrawCentered(graphics, text.GetString(153, 0) ?? "Difficulty settings", new Rectangle(48, 94, 384, 20));
        normalFont.DrawCentered(graphics, text.GetString(153, options.Difficulty + 1) ?? DifficultyName(options.Difficulty),
            new Rectangle(80, 174, 224, 20));
        DrawArrow(graphics, 288, 166, increase: false, 0, hoveredControl);
        DrawArrow(graphics, 312, 166, increase: true, 1, hoveredControl);
        normalFont.DrawCentered(graphics, text.GetString(153, 8) ?? "Right click to continue", new Rectangle(48, 246, 384, 20));
    }

    [RewriteOnly("Draws one original table-driven option button with its verified bounds")]
    private void DrawOptionButton(Graphics graphics, Rectangle bounds, string label, int control, int? hoveredControl)
    {
        skin.DrawButton(graphics, bounds, hoveredControl == control);
        normalFont.DrawCentered(graphics, label, bounds);
    }

    [OriginalAddress(0x004256A0, "FUN_004256a0")]
    private void DrawArrow(Graphics graphics, int x, int y, bool increase, int control, int? hoveredControl)
    {
        bool hover = hoveredControl == control;
        Bitmap icon = increase ? hover ? upHoverIcon : upIcon : hover ? downHoverIcon : downIcon;
        graphics.DrawImageUnscaled(icon, x, y);
    }

    [RewriteOnly("Pointer adapter for the reconstructed option-state controls")]
    public int? HitTest(LegacyCitySettingsPage page, UiPoint point, int displayWidth, int displayHeight)
    {
        Point origin = LayoutManager.OptionsOrigin(displayWidth, displayHeight);
        point = new UiPoint(point.X - origin.X, point.Y - origin.Y);
        Rectangle[] controls = LayoutManager.OptionsControlBounds(page);
        for (int index = 0; index < controls.Length; index++)
            if (controls[index].Contains(point.X, point.Y)) return index;
        return null;
    }

    [RewriteOnly("Presents the five difficulty values used by the original game")]
    private static string DifficultyName(int difficulty) => difficulty switch
    {
        0 => "Very Easy", 1 => "Easy", 2 => "Normal", 3 => "Hard", _ => "Very Hard"
    };
}
