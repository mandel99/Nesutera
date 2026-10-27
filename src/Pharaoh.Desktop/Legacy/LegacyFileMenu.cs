using Pharaoh.Engine.ReverseEngineering;

using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy;

public enum LegacyFileMenuMode
{
    SaveCity,
    LoadCity,
    DeleteCity,
    SaveMap,
    LoadMap
}

public enum LegacyFileMenuAction { Proceed, Cancel, ScrollUp, ScrollDown }

public readonly record struct LegacyFileMenuHit(LegacyFileMenuAction? Action, int? FileIndex);

/// <summary>State and hit testing shared by the original states 0x0D and 0x10.</summary>
public sealed class LegacyFileMenu
{
    public const int VisibleRows = 12;
    public static readonly UiRect InputBounds = new(160, 88, 448, 112);
    public static readonly UiRect ScrollTrackBounds = new(464, 145, 496, 300);

    private static readonly UiRect[] Rows = Enumerable.Range(0, VisibleRows)
        .Select(row => new UiRect(160, 128 + row * 16, 448, 144 + row * 16)).ToArray();

    public LegacyFileMenuMode Mode { get; set; } = LegacyFileMenuMode.LoadCity;
    public int FirstVisibleIndex { get; private set; }
    public int? SelectedIndex { get; private set; }
    public string Input { get; private set; } = string.Empty;

    public int TitleTextIndex => Mode switch
    {
        LegacyFileMenuMode.SaveCity => 0,
        LegacyFileMenuMode.LoadCity => 1,
        LegacyFileMenuMode.DeleteCity => 6,
        LegacyFileMenuMode.SaveMap => 3,
        _ => 4
    };

    public void Reset(LegacyFileMenuMode mode)
    {
        Mode = mode;
        FirstVisibleIndex = 0;
        SelectedIndex = null;
        Input = string.Empty;
    }

    [OriginalAddress(0x00402A80, "FUN_00402a80")]
    public void Select(int index, IReadOnlyList<string> files)
    {
        if ((uint)index >= (uint)files.Count) return;
        SelectedIndex = index;
        Input = Path.GetFileNameWithoutExtension(files[index]);
    }

    public bool Scroll(int delta, int count)
    {
        int previous = FirstVisibleIndex;
        FirstVisibleIndex = Math.Clamp(FirstVisibleIndex + Math.Sign(delta), 0, Math.Max(0, count - VisibleRows));
        return previous != FirstVisibleIndex;
    }

    public LegacyFileMenuHit? HitTest(UiPoint point, int fileCount)
    {
        for (int row = 0; row < VisibleRows; row++)
        {
            int index = FirstVisibleIndex + row;
            if (index < fileCount && Rows[row].Contains(point)) return new(null, index);
        }

        bool map = Mode is LegacyFileMenuMode.SaveMap or LegacyFileMenuMode.LoadMap;
        UiRect cancel = map ? new(392, 335, 426, 369) : new(344, 335, 378, 369);
        UiRect proceed = map ? new(344, 335, 378, 369) : new(392, 335, 426, 369);
        if (cancel.Contains(point)) return new(LegacyFileMenuAction.Cancel, null);
        if (proceed.Contains(point)) return new(LegacyFileMenuAction.Proceed, null);
        if (new UiRect(464, 120, 498, 154).Contains(point)) return new(LegacyFileMenuAction.ScrollUp, null);
        if (new UiRect(464, 300, 498, 334).Contains(point)) return new(LegacyFileMenuAction.ScrollDown, null);
        return null;
    }
}
