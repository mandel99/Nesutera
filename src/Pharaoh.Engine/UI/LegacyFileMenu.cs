using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum LegacyFileMenuMode
{
    SaveCity,
    LoadCity,
    DeleteCity,
    SaveMap,
    LoadMap
}

/// <summary>Selection and scroll state shared by the original states 0x0D and 0x10.</summary>
public sealed class LegacyFileMenu
{
    public const int VisibleRows = 12;

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

    [OriginalAddress(0x00402980, "FUN_00402980")]
    [OriginalAddress(0x005325F0, "FUN_005325f0")]
    public bool SetScrollPosition(int firstVisibleIndex, int count)
    {
        int previous = FirstVisibleIndex;
        FirstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, count - VisibleRows));
        return previous != FirstVisibleIndex;
    }

}
