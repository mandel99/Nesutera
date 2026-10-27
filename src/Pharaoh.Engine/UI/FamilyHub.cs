using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum FamilyHubAction { ResumeHistory, ChooseMission, LoadGame, CustomMissions, ReturnToRegistry }
public sealed record FamilyHubItem(FamilyHubAction Action, UiRect Bounds, int TextIndex);

public static class FamilyHub
{
    public const int TextGroup = 293;
    public const int TitleTextIndex = 5;
    public static IReadOnlyList<FamilyHubItem> Items { get; } = Array.AsReadOnly<FamilyHubItem>(
    [
        new(FamilyHubAction.ResumeHistory,    new UiRect(208, 128, 433, 153), 0),
        new(FamilyHubAction.ChooseMission,    new UiRect(208, 176, 433, 201), 1),
        new(FamilyHubAction.LoadGame,         new UiRect(208, 224, 433, 249), 2),
        new(FamilyHubAction.CustomMissions,   new UiRect(208, 272, 433, 297), 3),
        new(FamilyHubAction.ReturnToRegistry, new UiRect(208, 320, 433, 345), 4)
    ]);

    public static IReadOnlyList<FamilyHubItem> VisibleItems(bool hasSavedGames, bool hasFamilyProgress) =>
        Items.Where(item => hasSavedGames || item.Action != FamilyHubAction.LoadGame)
            .Select(item => item.Action == FamilyHubAction.ResumeHistory && !hasFamilyProgress
                ? item with { TextIndex = 7 }
                : item)
            .ToArray();

    [OriginalAddress(0x004CB5A0, "FUN_004cb5a0")]
    public static FamilyHubItem? HitTest(DisplayLayout display, UiPoint screenPoint)
    {
        UiPoint point = new(screenPoint.X - display.HorizontalOffset, screenPoint.Y - display.VerticalOffset);
        return Items.FirstOrDefault(item => item.Bounds.Contains(point));
    }

    public static FamilyHubItem? HitTest(DisplayLayout display, UiPoint screenPoint, IReadOnlyList<FamilyHubItem> items)
    {
        UiPoint point = new(screenPoint.X - display.HorizontalOffset, screenPoint.Y - display.VerticalOffset);
        return items.FirstOrDefault(item => item.Bounds.Contains(point));
    }
}
