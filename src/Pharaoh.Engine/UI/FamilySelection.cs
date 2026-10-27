using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum FamilySelectionAction
{
    Create,
    Delete,
    Confirm,
    MainMenu,
    ScrollUp,
    ScrollDown
}

public readonly record struct FamilySelectionHit(FamilySelectionAction? Action, int? FamilyIndex);

public sealed class FamilySelection
{
    public const int VisibleRows = 12;
    public const int ScrollTrackTop = 145;
    public const int ScrollTrackRange = 130;
    public const int TextGroup = 292;
    public const int MaximumNameBytes = 31;

    private static readonly UiRect[] Rows = Enumerable.Range(0, VisibleRows)
        .Select(index => new UiRect(160, 128 + (index * 16), 448, 144 + (index * 16)))
        .ToArray();

    private static readonly IReadOnlyDictionary<FamilySelectionAction, UiRect> Actions =
        new Dictionary<FamilySelectionAction, UiRect>
        {
            [FamilySelectionAction.Create] = new(142, 332, 267, 357),
            [FamilySelectionAction.Delete] = new(276, 332, 406, 357),
            [FamilySelectionAction.Confirm] = new(418, 332, 498, 357),
            [FamilySelectionAction.MainMenu] = new(196, 362, 446, 387),
            [FamilySelectionAction.ScrollUp] = new(464, 120, 498, 154),
            [FamilySelectionAction.ScrollDown] = new(464, 300, 498, 334)
        };

    public static IReadOnlyDictionary<FamilySelectionAction, int> ActionTextIndices { get; } =
        new Dictionary<FamilySelectionAction, int>
        {
            [FamilySelectionAction.Create] = 0,
            [FamilySelectionAction.Delete] = 1,
            [FamilySelectionAction.Confirm] = 2,
            [FamilySelectionAction.MainMenu] = 4
        };

    public int FirstVisibleIndex { get; private set; }
    public int? SelectedIndex { get; private set; }

    [OriginalAddress(0x00516890, "FUN_00516890")]
    public bool Scroll(FamilySelectionAction direction, int familyCount)
    {
        int previous = FirstVisibleIndex;
        if (direction == FamilySelectionAction.ScrollUp && FirstVisibleIndex > 0)
            FirstVisibleIndex--;
        else if (direction == FamilySelectionAction.ScrollDown && FirstVisibleIndex + VisibleRows < familyCount)
            FirstVisibleIndex++;
        return previous != FirstVisibleIndex;
    }

    public bool ScrollBy(int rows, int familyCount)
    {
        int previous = FirstVisibleIndex;
        FirstVisibleIndex = Math.Clamp(FirstVisibleIndex + rows, 0, Math.Max(0, familyCount - VisibleRows));
        return previous != FirstVisibleIndex;
    }

    public bool SetScrollPosition(int firstVisibleIndex, int familyCount)
    {
        int previous = FirstVisibleIndex;
        FirstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, familyCount - VisibleRows));
        return previous != FirstVisibleIndex;
    }

    public void RestoreSelection(int? familyIndex, int familyCount)
    {
        if (familyCount <= 0)
        {
            SelectedIndex = null;
            FirstVisibleIndex = 0;
            return;
        }

        SelectedIndex = Math.Clamp(familyIndex ?? 0, 0, familyCount - 1);
        FirstVisibleIndex = Math.Clamp(FirstVisibleIndex, 0, Math.Max(0, familyCount - VisibleRows));
        if (SelectedIndex < FirstVisibleIndex)
            FirstVisibleIndex = SelectedIndex.Value;
        else if (SelectedIndex >= FirstVisibleIndex + VisibleRows)
            FirstVisibleIndex = SelectedIndex.Value - VisibleRows + 1;
    }

    [OriginalAddress(0x00516AB0, "FUN_00516ab0")]
    public void Select(int familyIndex, int familyCount)
    {
        if ((uint)familyIndex >= (uint)familyCount)
            throw new ArgumentOutOfRangeException(nameof(familyIndex));
        SelectedIndex = familyIndex;
    }

    public void SelectFirstIfAvailable(int familyCount)
    {
        if (familyCount <= 0)
        {
            SelectedIndex = null;
            FirstVisibleIndex = 0;
            return;
        }

        if (SelectedIndex is null || SelectedIndex >= familyCount)
            SelectedIndex = 0;
    }

    public void RemoveSelected(int remainingFamilyCount)
    {
        if (remainingFamilyCount <= 0)
        {
            SelectedIndex = null;
            FirstVisibleIndex = 0;
            return;
        }

        SelectedIndex = Math.Min(SelectedIndex ?? 0, remainingFamilyCount - 1);
        FirstVisibleIndex = Math.Min(FirstVisibleIndex, Math.Max(0, remainingFamilyCount - VisibleRows));
    }

    public FamilySelectionHit? HitTest(DisplayLayout display, UiPoint screenPoint, int familyCount)
    {
        UiPoint point = new(screenPoint.X - display.HorizontalOffset, screenPoint.Y - display.VerticalOffset);
        for (int row = 0; row < Rows.Length; row++)
        {
            int familyIndex = FirstVisibleIndex + row;
            if (familyIndex < familyCount && Rows[row].Contains(point))
                return new FamilySelectionHit(null, familyIndex);
        }

        foreach ((FamilySelectionAction action, UiRect bounds) in Actions)
        {
            if (bounds.Contains(point))
                return new FamilySelectionHit(action, null);
        }
        return null;
    }
}
