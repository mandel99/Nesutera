using Pharaoh.Assets.Graphics;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Desktop.Legacy.UI;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop;

internal sealed class LegacyUiSkin : IDisposable
{
    private const int OuterBase = 502;
    private const int InnerBase = 646;
    private const int ButtonBase = 715;
    private const int BuildMenuRowBase = 265;
    private const int CityChromeBase = 2578;
    private const int CityRotationBase = 2512;
    private readonly string path;
    private readonly Sg3Archive archive;
    private readonly Sg3AssetCatalog? assets;
    private readonly Dictionary<int, Bitmap> images = [];

    public LegacyUiSkin(string installationRoot, Sg3AssetCatalog? assets = null)
    {
        path = Path.Combine(installationRoot, "Data", "Pharaoh_General.sg3");
        this.assets = assets;
        archive = assets?.LoadArchive("Pharaoh_General.sg3") ?? Sg3Archive.Load(path);
    }

    [OriginalAddress(0x00536820, "FUN_00536820")]
    public void DrawOuterPanel(Graphics graphics, int x, int y, int columns, int rows) =>
        DrawPatternPanel(graphics, x, y, columns, rows, OuterBase, 10, 12, 120, 132, 143);

    [OriginalAddress(0x00536A30, "FUN_00536a30")]
    public void DrawInnerPanel(Graphics graphics, int x, int y, int columns, int rows) =>
        DrawPatternPanel(graphics, x, y, columns, rows, InnerBase, 5, 7, 35, 42, 48);

    [OriginalAddress(0x00536C30, "FUN_00536c30")]
    public void DrawButton(Graphics graphics, Rectangle bounds, bool selected)
    {
        int columns = (bounds.Width + 15) / 16;
        int rows = (bounds.Height + 15) / 16;
        int excessX = columns * 16 - bounds.Width;
        int excessY = rows * 16 - bounds.Height;
        int imageBase = ButtonBase + (selected ? 8 : 0);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int image;
            int drawX = bounds.Left + column * 16;
            int drawY = bounds.Top + row * 16;
            if (row == 0) image = imageBase + (column == 0 ? 0 : column == columns - 1 ? 2 : 1);
            else if (row == rows - 1)
            {
                image = imageBase + (column == 0 ? 6 : column == columns - 1 ? 4 : 5);
                drawY -= excessY;
            }
            else image = imageBase + (column == 0 ? 7 : column == columns - 1 ? 3 : 1);
            if (column == columns - 1) drawX -= excessX;
            graphics.DrawImageUnscaled(Get(image), drawX, drawY);
        }
    }

    [OriginalAddress(0x00536670, "FUN_00536670")]
    public void DrawCityTopBar(Graphics graphics, int displayWidth)
    {
        int x = displayWidth switch
        {
            640 => -360,
            800 => -240,
            1024 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
        };
        graphics.DrawImageUnscaled(Get(CityChromeBase + 8), x, 0);
    }

    [OriginalAddress(0x00537100, "FUN_00537100")]
    [OriginalAddress(0x00537120, "FUN_00537120")]
    public void DrawCityResolutionEdge(Graphics graphics, int displayWidth)
    {
        (int image, int x)? edge = displayWidth switch
        {
            800 => (CityChromeBase + 3, 760),
            1024 => (CityChromeBase + 2, 1000),
            640 => null,
            _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
        };
        if (edge is { } value)
            graphics.DrawImageUnscaled(Get(value.image), value.x, 0);
    }

    [OriginalAddress(0x0051E990, "FUN_0051e990")]
    public void DrawCitySidebar(
        Graphics graphics, int displayWidth, bool compact, int horizontalOffset = 0)
    {
        int left = LayoutManager.SidebarLeft(displayWidth, compact) + horizontalOffset;
        graphics.DrawImageUnscaled(Get(CityChromeBase + (compact ? 1 : 0)), left, 30);

        int? lowerImage = displayWidth switch
        {
            1024 => CityChromeBase + (compact ? 5 : 4),
            800 => CityChromeBase + (compact ? 7 : 6),
            640 => null,
            _ => throw new ArgumentOutOfRangeException(nameof(displayWidth))
        };
        if (lowerImage is int image)
            graphics.DrawImageUnscaled(Get(image), left, 480);
    }

    [OriginalAddress(0x00425730, "FUN_00425730")]
    public void DrawCitySidebarControl(
        Graphics graphics, int image, Rectangle bounds, bool selected, bool hovered, bool enabled = true)
    {
        int stateOffset = !enabled ? 3 : hovered ? 1 : selected ? 2 : 0;
        graphics.DrawImageUnscaled(Get(image + stateOffset), bounds.Left, bounds.Top);
    }

    [OriginalAddress(0x00537000, "FUN_00537000")]
    public void DrawBuildMenuRow(
        Graphics graphics, int x, int y, int columns, bool hovered)
    {
        int imageBase = BuildMenuRowBase + (hovered ? 0 : 3);
        for (int column = 0; column < columns; column++)
        {
            int image = imageBase + (column == 0 ? 0 : column == columns - 1 ? 2 : 1);
            graphics.DrawImageUnscaled(Get(image), x + column * 16, y);
        }
    }

    [OriginalAddress(0x00425900, "FUN_00425900")]
    public void DrawCityRotationControl(Graphics graphics, Rectangle bounds, int? hoveredSegment)
    {
        int image = hoveredSegment is int segment
            ? CityRotationBase + 1 + segment
            : CityRotationBase;
        graphics.DrawImageUnscaled(Get(image), bounds.Left, bounds.Top);
    }

    [OriginalAddress(0x0051EDD0, "FUN_0051edd0")]
    public void DrawCityCategoryIcon(Graphics graphics, int x, int y, int originalCategory) =>
        graphics.DrawImageUnscaled(
            Get(CityChromeBase + 9 + LegacyBuildMenuState.ResolveCategoryIconOffset(originalCategory)),
            x,
            y);

    [OriginalAddress(0x00536990, "FUN_00536990")]
    public void DrawMenuPanel(Graphics graphics, int x, int y, int columns, int rows)
    {
        int verticalPattern = 0;
        for (int row = 0; row < rows; row++)
        {
            int horizontalPattern = 0;
            for (int column = 0; column < columns; column++)
            {
                graphics.DrawImageUnscaled(
                    Get(OuterBase + 13 + horizontalPattern + verticalPattern),
                    x + column * 16,
                    y + row * 16);
                if (++horizontalPattern >= 10) horizontalPattern = 0;
            }
            verticalPattern += 12;
            if (verticalPattern >= 120) verticalPattern = 0;
        }
    }

    private void DrawPatternPanel(Graphics graphics, int x, int y, int columns, int rows, int imageBase,
        int horizontalCycle, int verticalStep, int verticalCycle, int bottomOffset, int bottomRightOffset)
    {
        int verticalPattern = 0;
        for (int row = 0; row < rows; row++)
        {
            int horizontalPattern = 0;
            for (int column = 0; column < columns; column++)
            {
                int image = row == 0
                    ? imageBase + (column == 0 ? 0 : column == columns - 1 ? horizontalCycle + 1 : 1 + horizontalPattern)
                    : row == rows - 1
                        ? imageBase + (column == 0 ? bottomOffset : column == columns - 1 ? bottomRightOffset : bottomOffset + 1 + horizontalPattern)
                        : imageBase + (column == 0 ? horizontalCycle + 2 + verticalPattern :
                            column == columns - 1 ? horizontalCycle + 2 + verticalPattern + horizontalCycle + 1 :
                            horizontalCycle + 3 + verticalPattern + horizontalPattern);
                graphics.DrawImageUnscaled(Get(image), x + column * 16, y + row * 16);
                if (++horizontalPattern >= horizontalCycle) horizontalPattern = 0;
            }
            verticalPattern += verticalStep;
            if (verticalPattern >= verticalCycle) verticalPattern = 0;
        }
    }

    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    public void DrawGeneralImage(Graphics graphics,int index,int x,int y)=>graphics.DrawImageUnscaled(Get(index),x,y);

    private Bitmap Get(int index)
    {
        if (!images.TryGetValue(index, out Bitmap? bitmap))
        {
            bitmap = RgbaBitmap.Create(assets?.Decode("Pharaoh_General.sg3", index) ??
                Sg3ImageDecoder.Decode(path, archive, index));
            images.Add(index, bitmap);
        }
        return bitmap;
    }

    public void Dispose()
    {
        foreach (Bitmap bitmap in images.Values) bitmap.Dispose();
        images.Clear();
    }
}
