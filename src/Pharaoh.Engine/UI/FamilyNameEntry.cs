using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;
using System.Text;

namespace Pharaoh.Engine.UI;

public enum FamilyNameEntryAction
{
    EgyptianName,
    Continue,
    ConfirmEgyptianName
}

public static class FamilyNameEntry
{
    public const int TitleTextGroup = 31;
    public const int TitleTextIndex = 0;
    public const int CommonTextGroup = 13;
    public const int ContinueTextIndex = 5;
    public const int EgyptianNameTextIndex = 8;
    public const int EgyptianNamesTextGroup = 197;
    public const int MaleHeadingIndex = 0;
    public const int FemaleHeadingIndex = 21;
    public const int PresetNamesPerColumn = 20;

    public static UiRect EgyptianNamesPanel => new(110, 30, 526, 478);

    public static UiRect EgyptianNamesOkButton => new(435, 444, 510, 462);

    public static UiRect ContinueButton(DisplayLayout display) =>
        new UiRect(464, 249, 491, 276).Offset(display.HorizontalOffset, display.VerticalOffset);

    public static UiRect EgyptianNameButton(DisplayLayout display) =>
        new UiRect(352, 246, 377, 271).Offset(display.HorizontalOffset, display.VerticalOffset);

    public static UiRect EgyptianNameBounds(int textIndex)
    {
        if (textIndex is >= 1 and <= 20)
            return new UiRect(115, 44 + textIndex * 20, 211, 62 + textIndex * 20);
        if (textIndex is >= 22 and <= 41)
            return new UiRect(340, 44 + (textIndex - 21) * 20, 436, 62 + (textIndex - 21) * 20);
        throw new ArgumentOutOfRangeException(nameof(textIndex));
    }

    public static FamilyNameEntryAction? HitNameEntry(UiPoint point)
    {
        if (EgyptianNameButton(DisplayLayout.FromLegacyMode(LegacyDisplayMode.Size640x480)).Contains(point))
            return FamilyNameEntryAction.EgyptianName;
        if (ContinueButton(DisplayLayout.FromLegacyMode(LegacyDisplayMode.Size640x480)).Contains(point))
            return FamilyNameEntryAction.Continue;
        return null;
    }

    public static FamilyNameEntryAction? HitEgyptianNamesAction(UiPoint point) =>
        EgyptianNamesOkButton.Contains(point) ? FamilyNameEntryAction.ConfirmEgyptianName : null;

    public static int? HitEgyptianName(UiPoint point)
    {
        for (int index = 1; index <= 41; index++)
        {
            if (index == FemaleHeadingIndex) continue;
            if (EgyptianNameBounds(index).Contains(point)) return index;
        }
        return null;
    }

    [OriginalAddress(0x00516C80, "FUN_00516c80")]
    public static bool FitsLegacyBuffer(string name, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(encoding);
        return name.Length > 0 && encoding.GetByteCount(name) <= FamilySelection.MaximumNameBytes;
    }
}
