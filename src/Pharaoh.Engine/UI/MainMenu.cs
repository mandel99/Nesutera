using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum MainMenuAction
{
    Play = 0,
    OpenWebsite = 1,
    MapEditor = 2,
    FamilyRanking = 3,
    Quit = 4
}

public sealed record MainMenuItem(
    MainMenuAction Action,
    UiRect Bounds,
    int TextGroup,
    int TextIndex,
    string? TextOverride = null);

public static class MainMenu
{
    public const int TextGroup = 30;

    // Change this value to customize the website button shown on the main menu.
    public const string WebsiteButtonText = "Open Nesutera Github";

    // Pharaoh.exe 2.1 tables at 0x0057BD88 (rectangles) and 0x0057BE14 (text indices).
    public static IReadOnlyList<MainMenuItem> Items { get; } = Array.AsReadOnly<MainMenuItem>(
    [
        new(MainMenuAction.Play,           new UiRect(192, 100, 448, 125), TextGroup, 0),
        new(MainMenuAction.OpenWebsite,    new UiRect(192, 140, 448, 165), TextGroup, 2, WebsiteButtonText),
        new(MainMenuAction.MapEditor,      new UiRect(192, 180, 448, 205), TextGroup, 3),
        new(MainMenuAction.FamilyRanking,  new UiRect(192, 220, 448, 245), TextGroup, 5),
        new(MainMenuAction.Quit,           new UiRect(192, 260, 448, 285), TextGroup, 4)
    ]);

    [OriginalAddress(0x004180C0, "FUN_004180c0")]
    public static IEnumerable<(MainMenuItem Item, UiRect ScreenBounds)> Layout(DisplayLayout display)
    {
        foreach (MainMenuItem item in Items)
            yield return (item, item.Bounds.Offset(display.HorizontalOffset, display.VerticalOffset));
    }

    [OriginalAddress(0x004D6740, "FUN_004d6740")]
    public static MainMenuItem? HitTest(DisplayLayout display, UiPoint screenPoint)
    {
        foreach ((MainMenuItem item, UiRect bounds) in Layout(display))
        {
            if (bounds.Contains(screenPoint))
                return item;
        }

        return null;
    }
}
