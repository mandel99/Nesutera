using Pharaoh.Engine.Platform;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public sealed record MissionItem(int Id, string Name);
public sealed record CampaignPeriod(int Id, int TextIndex, UiRect Bounds);

public enum MissionSelectionMode
{
    Campaigns,
    Individual
}

public sealed class MissionSelection
{
    // Both original states 0x17 (campaigns) and 0x3D (individual missions)
    // are drawn over this same full-screen Pharaoh_Unloaded image.
    public const int ExploreHistoryBackgroundGraphicIndex = 658;
    public const int VisibleRows = 13;
    public static readonly UiRect ListBounds = new(16, 249, 280, 457);
    public static readonly UiRect IndividualTabBounds = new(32, 208, 192, 233);
    public static readonly UiRect CampaignTabBounds = new(195, 208, 288, 233);
    public static readonly UiRect ResultsBounds = new(354, 410, 604, 433);
    public static readonly UiRect ScrollUpBounds = new(282, 247, 316, 281);
    public static readonly UiRect ScrollDownBounds = new(282, 435, 316, 469);
    public static readonly UiRect ExitBounds = new(335, 440, 362, 467);
    public static readonly UiRect PlayBounds = new(600, 440, 627, 467);
    public static readonly IReadOnlyList<CampaignPeriod> CampaignPeriods =
    [
        new(0, 0, new UiRect(20, 291, 164, 316)),
        new(1, 1, new UiRect(20, 321, 164, 346)),
        new(2, 2, new UiRect(172, 261, 316, 286)),
        new(3, 3, new UiRect(172, 291, 316, 316)),
        new(4, 4, new UiRect(172, 321, 316, 346)),
        new(5, 5, new UiRect(20, 391, 164, 416)),
        new(6, 6, new UiRect(20, 421, 164, 446)),
        new(7, 7, new UiRect(172, 391, 316, 416)),
        new(8, 8, new UiRect(172, 421, 316, 446))
    ];

    public MissionSelectionMode Mode { get; private set; } = MissionSelectionMode.Campaigns;
    public int SelectedCampaign { get; private set; }
    public int SelectedIndex { get; private set; }
    public int FirstVisibleIndex { get; private set; }

    public void SetMode(MissionSelectionMode mode) => Mode = mode;

    public int? HitCampaign(UiPoint point)
    {
        CampaignPeriod? period = CampaignPeriods.FirstOrDefault(period => period.Bounds.Contains(point));
        return period?.Id;
    }

    public void SelectCampaign(int index) => SelectedCampaign = Math.Clamp(index, 0, CampaignPeriods.Count - 1);

    [OriginalAddress(0x0041F810, "FUN_0041f810")]
    public static IReadOnlyList<MissionItem> LoadCampaign(string installationRoot)
    {
        string path = Path.Combine(installationRoot, "campaign.txt");
        if (!File.Exists(path)) return [];
        List<MissionItem> result = [];
        bool names = false;
        foreach (string sourceLine in File.ReadLines(path))
        {
            string line = sourceLine.Trim();
            if (line.Equals("[MISSION_NAMES]", StringComparison.OrdinalIgnoreCase)) { names = true; continue; }
            if (!names || line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[')) break;
            result.Add(new MissionItem(result.Count, line));
        }
        return result;
    }

    [OriginalAddress(0x0041F220, "FUN_0041f220")]
    public int? HitRow(UiPoint point, int count) => HitRow(point, count, ListBounds, VisibleRows);

    public int? HitRow(UiPoint point, int count, UiRect bounds, int visibleRows)
    {
        if (!bounds.Contains(point)) return null;
        int row = (point.Y - bounds.Top) / 16;
        int index = FirstVisibleIndex + row;
        return row < visibleRows && index < count ? index : null;
    }

    [OriginalAddress(0x0041F670, "FUN_0041f670")]
    public void Select(int index, int count) => Select(index, count, VisibleRows);

    public void Select(int index, int count, int visibleRows)
    {
        if (count == 0) { SelectedIndex = FirstVisibleIndex = 0; return; }
        SelectedIndex = Math.Clamp(index, 0, count - 1);
        if (SelectedIndex < FirstVisibleIndex) FirstVisibleIndex = SelectedIndex;
        if (SelectedIndex >= FirstVisibleIndex + visibleRows) FirstVisibleIndex = SelectedIndex - visibleRows + 1;
    }

    public void Move(int delta, int count) => Select(SelectedIndex + delta, count);

    public void Move(int delta, int count, int visibleRows) => Select(SelectedIndex + delta, count, visibleRows);

    public void Scroll(int delta, int count) => Scroll(delta, count, VisibleRows);

    public void Scroll(int delta, int count, int visibleRows)
    {
        int maximum = Math.Max(0, count - visibleRows);
        FirstVisibleIndex = Math.Clamp(FirstVisibleIndex + delta, 0, maximum);
    }

    public void SetScrollPosition(int firstVisibleIndex, int count, int visibleRows = VisibleRows)
    {
        int maximum = Math.Max(0, count - visibleRows);
        FirstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, maximum);
    }
}

public static class CustomMissionCatalog
{
    [OriginalAddress(0x0041F810, "FUN_0041f810")]
    public static IReadOnlyList<MissionItem> Load(string installationRoot)
    {
        string directory = Path.Combine(installationRoot, "Maps");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.map", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase)
            .Select((path, index) => new MissionItem(index, Path.GetFileNameWithoutExtension(path)))
            .ToArray();
    }
}
