using Pharaoh.Assets.Text;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.UI;

/// <summary>Single city-UI composition entry point; individual regions own their rendering.</summary>
internal sealed class UiManager
{
    public void DrawCity(
        Graphics graphics,
        LegacyUiSkin skin,
        LegacyBitmapFont smallFont,
        LegacyBitmapFont menuFont,
        LegacyBitmapFont normalFont,
        LegacyBitmapFont hoverFont,
        LegacyBitmapFont yellowFont,
        LocalizedTextCatalog text,
        Bitmap? minimapFrame,
        LegacyCityHudState state,
        LegacyBuildMenuState? buildMenuState,
        bool undoAvailable,
        LegacyBuildingModelCosts buildingCosts,
        int difficulty,
        int selectedCommand,
        int? hoveredCommand,
        int? openBuildMenuCommand,
        int? hoveredBuildMenuRow,
        LegacyCityOptions options,
        int? openMenu,
        int? hoveredMenuItem,
        int? hoveredRotationSegment,
        bool compactSidebar,
        bool sidebarToggleHovered,
        int? expandedSidebarAnimationOffset,
        int displayWidth,
        int displayHeight)
    {
        bool includeBuildMenu = expandedSidebarAnimationOffset is null &&
            buildMenuState is not null && openBuildMenuCommand is not null;
        LayoutManager.VisitCityLayers(includeBuildMenu, layer =>
        {
            switch (layer)
            {
                case CityUiLayer.TopBarBackground:
                    TopBar.DrawBackground(graphics, skin, displayWidth);
                    break;
                case CityUiLayer.RightBar:
                    RightBar.Draw(graphics, skin, smallFont, normalFont, text, minimapFrame, buildMenuState,
                        undoAvailable,
                        selectedCommand, hoveredCommand, compactSidebar, sidebarToggleHovered,
                        expandedSidebarAnimationOffset, displayWidth, displayHeight);
                    break;
                case CityUiLayer.ResolutionEdge:
                    skin.DrawCityResolutionEdge(graphics, displayWidth);
                    break;
                case CityUiLayer.TopBarForeground:
                    TopBar.DrawForeground(graphics, skin, menuFont, hoverFont, yellowFont, text, state, options,
                        openMenu, hoveredMenuItem, hoveredRotationSegment, displayWidth);
                    break;
                case CityUiLayer.BuildMenu:
                    BuildMenu.Draw(graphics, skin, menuFont, yellowFont, text, buildMenuState!,
                        openBuildMenuCommand!.Value, hoveredBuildMenuRow, buildingCosts, difficulty,
                        displayWidth, compactSidebar);
                    break;
            }
        });
    }
}
