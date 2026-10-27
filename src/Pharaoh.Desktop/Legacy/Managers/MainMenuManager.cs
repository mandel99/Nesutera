using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy;

public enum MainMenuCommandKind
{
    StartGame,
    OpenWebsite,
    StartMapEditor,
    ShowFamilyRanking,
    Quit
}

public readonly record struct MainMenuCommand(MainMenuCommandKind Kind, string? Argument = null);

/// <summary>Owns pointer state and command dispatch for the main menu.</summary>
public sealed class MainMenuManager(DisplayLayout display)
{
    public MainMenuItem? HoveredItem { get; private set; }

    public bool MovePointer(UiPoint screenPoint)
    {
        MainMenuItem? previous = HoveredItem;
        HoveredItem = MainMenu.HitTest(display, screenPoint);
        return previous?.Action != HoveredItem?.Action;
    }

    [OriginalAddress(0x00417FF0, "FUN_00417ff0")]
    public MainMenuCommand? Activate(UiPoint screenPoint)
    {
        MainMenuItem? item = MainMenu.HitTest(display, screenPoint);
        if (item is null)
            return null;

        return item.Action switch
        {
            MainMenuAction.Play => new(MainMenuCommandKind.StartGame),
            MainMenuAction.OpenWebsite => new(MainMenuCommandKind.OpenWebsite, "https://github.com/mandel99/Nesutera"),
            MainMenuAction.MapEditor => new(MainMenuCommandKind.StartMapEditor),
            MainMenuAction.FamilyRanking => new(MainMenuCommandKind.ShowFamilyRanking),
            MainMenuAction.Quit => new(MainMenuCommandKind.Quit),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
