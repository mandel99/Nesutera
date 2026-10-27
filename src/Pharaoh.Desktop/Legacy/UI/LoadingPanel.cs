using Pharaoh.Assets.Text;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.UI;

/// <summary>Owns presentation of the original startup checkpoints and expansion logo.</summary>
internal sealed class LoadingPanel(
    IReadOnlyList<Bitmap> backgrounds,
    LocalizedTextCatalog text)
{
    private Bitmap? expansionLogo;

    public void SetExpansionLogo(Bitmap logo) => expansionLogo = logo;

    [RewriteOnly("Selects the original startup checkpoint or final expansion-logo renderer")]
    public void Draw(Graphics graphics, int phase)
    {
        if (phase == LegacyStartupResources.LoadingCheckpoints.Count)
        {
            DrawExpansionLogo(graphics);
            return;
        }

        DrawCheckpoint(graphics, LegacyStartupResources.LoadingCheckpoints[phase]);
    }

    [OriginalAddress(0x00520B20, "FUN_00520b20")]
    private void DrawCheckpoint(Graphics graphics, LegacyLoadingCheckpoint checkpoint)
    {
        graphics.DrawImageUnscaled(backgrounds[checkpoint.GraphicIndex], 0, 0);
        string label = text.GetString(checkpoint.TextGroup, checkpoint.TextItem) ?? "Loading ...";
        Size textSize = TextRenderer.MeasureText(label, SystemFonts.DefaultFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int textX = (640 - textSize.Width) / 2;
        int textY = ((480 - textSize.Height) / 2) * 3 / 2;
        graphics.FillRectangle(Brushes.Black, textX - 26, textY - 11, textSize.Width + 52, textSize.Height + 22);
        graphics.FillRectangle(Brushes.White, textX - 25, textY - 10, textSize.Width + 50, textSize.Height + 20);
        TextRenderer.DrawText(graphics, label, SystemFonts.DefaultFont, new Point(textX, textY), Color.Black, Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    [OriginalAddress(0x00520AF0, "FUN_00520af0")]
    private void DrawExpansionLogo(Graphics graphics)
    {
        if (expansionLogo is null)
            throw new InvalidOperationException("The expansion logo has not been loaded yet.");
        graphics.DrawImageUnscaled(expansionLogo, 0, 0);
    }
}
