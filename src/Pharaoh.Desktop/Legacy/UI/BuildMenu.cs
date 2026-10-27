using System.Runtime.CompilerServices;
using Pharaoh.Assets.Text;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.UI;

internal static class BuildMenu
{
    private static readonly ConditionalWeakTable<LocalizedTextCatalog, Metrics> MetricsCache = new();

    [OriginalAddress(0x00520000, "FUN_00520000")]
    public static void Draw(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont font,
        LegacyBitmapFont yellowFont,
        LocalizedTextCatalog text,
        LegacyBuildMenuState state,
        int sidebarCommand,
        int? hoveredRow,
        LegacyBuildingModelCosts buildingCosts,
        int difficulty,
        int displayWidth,
        bool compact)
    {
        IReadOnlyList<LegacyBuildMenuEntry> entries =
            state.GetVerifiedAvailableEntries(sidebarCommand);
        for (int row = 0; row < entries.Count; row++)
        {
            Rectangle bounds = RowBounds(font, text, entries, row, displayWidth, compact);
            bool hovered = hoveredRow == row;
            skin.DrawBuildMenuRow(graphics, bounds.Left, bounds.Top, bounds.Width / 16, hovered);
            BuildingType buildingType = entries[row].Type;
            int buildingTypeId = (int)buildingType;
            string label = text.GetString(28, buildingTypeId) ?? $"Building {buildingTypeId}";
            int cost = buildingType == BuildingType.Fort
                ? 0
                : buildingCosts.GetCost(difficulty,
                    buildingType == BuildingType.WaterLift
                        ? BuildingType.WaterLiftCostModel
                        : buildingType);
            LegacyBitmapFont activeFont = hovered ? yellowFont : font;
            activeFont.DrawAt(graphics, label, bounds.Left + 8, bounds.Top + 3);
            if (cost != 0)
            {
                activeFont.DrawAt(graphics, $"{cost}Db",
                    LayoutManager.BuildMenuCostX(displayWidth, compact), bounds.Top + 4);
            }
        }
    }

    [OriginalAddress(0x00403E00, "FUN_00403e00")]
    public static int? HitTest(
        int x,
        int y,
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        LegacyBuildMenuState state,
        int sidebarCommand,
        int displayWidth,
        bool compact)
    {
        IReadOnlyList<LegacyBuildMenuEntry> entries =
            state.GetVerifiedAvailableEntries(sidebarCommand);
        for (int row = 0; row < entries.Count; row++)
            if (RowBounds(font, text, entries, row, displayWidth, compact).Contains(x, y))
                return row;
        return null;
    }

    private static Rectangle RowBounds(
        LegacyBitmapFont font,
        LocalizedTextCatalog text,
        IReadOnlyList<LegacyBuildMenuEntry> entries,
        int row,
        int displayWidth,
        bool compact)
    {
        Metrics metrics = MetricsCache.GetValue(text, _ =>
        {
            int maximumWidth = Enumerable.Range(0, 300)
                .Select(index => text.GetString(28, index))
                .Where(value => value is not null)
                .Max(value => font.Measure(value!));
            return new Metrics((maximumWidth + 15) / 16 + 7);
        });
        return LayoutManager.BuildMenuRowBounds(
            metrics.Columns, entries.Count, row, displayWidth, compact);
    }

    private sealed record Metrics(int Columns);
}
