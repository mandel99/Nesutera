using System.Drawing.Drawing2D;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;
using Pharaoh.Desktop.Legacy.Managers;

namespace Pharaoh.Desktop.Legacy;

internal sealed partial class PharaohWindow
{
    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    private bool TryBeginBuildingPlacement(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || selectedBuildingType is not BuildingType type ||
            (type != BuildingType.Well && !LegacySafetyServicePlacement.TryGetDefinition(type, out _) && !LegacySquareBuildingPlacement.TryGetDefinition(type, out _)) ||
            citySidebarAnimating || citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null || openBuildMenuCommand is not null ||
            !TryToGame(location, out UiPoint point) || !LayoutManager.CityMapBounds(presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar).Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile)) return false;
        buildModeManager.BeginBuildingPlacement(type); cityHoverTile = tile; Capture = true; Invalidate(); return true;
    }

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    private void UpdateHeldBuildingPlacement(Point location)
    {
        cityHoverTile = TryToGame(location, out UiPoint point) && LayoutManager.CityMapBounds(presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar).Contains(point.X, point.Y) &&
            cameraManager.TryHitTest(point.X, point.Y, out int tile) ? tile : null;
        Invalidate();
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    private void ReleaseHeldBuildingPlacement(Point location)
    {
        UpdateHeldBuildingPlacement(location);
        BuildingType? held = buildModeManager.HeldBuildingType;
        buildModeManager.CancelBuildingPlacement(); Capture = false;
        if (cityHoverTile is not null && selectedBuildingType == held)
        {
            bool placed = TryPlaceWell(location) || TryPlaceSafetyService(location) || TryPlaceFoodBuilding(location);
            if (!placed) ShowCityStatusMessage("Must build on cleared land");
        }
        Invalidate();
    }

    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    private void DrawHeldBuildingFootprint(Graphics graphics)
    {
        if (!buildModeManager.IsBuildingPlacementActive || activeMissionSession is null || cityHoverTile is not int tile || selectedBuildingType is not BuildingType type) return;
        IEnumerable<int> tiles = [];
        if (LegacySquareBuildingPlacement.TryGetDefinition(type, out var definition))
            tiles = LegacySquarePlacementPreview.Create(activeMissionSession, definition, tile, cameraManager.View?.Orientation ?? 0).Cells.Where(c => c.IsValid).Select(c => c.Tile);
        else if (type == BuildingType.Well ? BuildModeManager.CanPlaceWell(activeMissionSession, tile) : BuildModeManager.CanPlaceSafetyService(activeMissionSession, tile)) tiles = [tile];
        GraphicsState state = graphics.Save(); graphics.SetClip(LayoutManager.CityMapBounds(presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
        mapManager.DrawHeldBuildingFootprint(graphics, cameraManager, tiles); graphics.Restore(state);
    }
}
