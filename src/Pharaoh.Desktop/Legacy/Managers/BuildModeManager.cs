using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Loading;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>
/// Owns the mutable construction-tool interaction state. Validation and map
/// mutation remain in the verified engine placement implementations.
/// </summary>
internal sealed class BuildModeManager
{
    internal LegacyRoadPlacement RoadPlacement { get; } = new();
    internal LegacyHousingPlacement HousingPlacement { get; } = new();
    internal LegacyClearLandPlacement ClearLandPlacement { get; } = new();

    public BuildingType? HeldBuildingType { get; private set; }
    public bool IsBuildingPlacementActive => HeldBuildingType is not null;
    public void BeginBuildingPlacement(BuildingType type) => HeldBuildingType=type;
    public void CancelBuildingPlacement() => HeldBuildingType=null;

    public bool IsRoadPlacementActive => RoadPlacement.IsActive;
    public int RoadPreviewTileCount => RoadPlacement.PreviewTiles.Count;
    public IReadOnlyList<int> RoadPreviewTiles => RoadPlacement.PreviewTiles;
    public int RoadPreviewEndpoint => RoadPlacement.PreviewEndpoint;
    public int RoadPreviewNewTileCount => RoadPlacement.PreviewNewTileCount;

    public bool IsHousingPlacementActive => HousingPlacement.IsActive;
    public int HousingPreviewTileCount => HousingPlacement.PreviewTiles.Count;
    public IReadOnlyList<int> HousingPreviewTiles => HousingPlacement.PreviewTiles;
    public int HousingPreviewEndpoint => HousingPlacement.PreviewEndpoint;

    public bool IsClearLandPlacementActive => ClearLandPlacement.IsActive;
    public int ClearLandPreviewItemCount => IsClearLandPlacementActive
        ? ClearLandPlacement.GetPreviewItemCount()
        : 0;
    public int ClearLandPreviewEndpoint => ClearLandPlacement.PreviewEndpoint;
    public IReadOnlyList<int> ClearLandPreviewBuildingIds => IsClearLandPlacementActive
        ? ClearLandPlacement.GetPreviewBuildingIds()
        : [];

    public bool BeginRoadPlacement(LegacyMissionSession session, int tileIndex) =>
        RoadPlacement.Begin(session.LogicalChunks, tileIndex);

    public static bool CanBeginRoadPlacement(LegacyMissionSession session, int tileIndex) =>
        LegacyRoadPlacement.CanBeginAt(session.LogicalChunks, tileIndex);

    public bool UpdateRoadPlacement(int tileIndex) => RoadPlacement.Update(tileIndex);

    public LegacyRoadPlacementResult CommitRoadPlacement(LegacyMissionSession session)
    {
        LegacyRoadPlacementResult result = RoadPlacement.Commit();
        session.ReplaceLogicalChunk("terrain_grid", result.TerrainGrid);
        return result;
    }

    public void CancelRoadPlacement() => RoadPlacement.Reset();

    public bool BeginHousingPlacement(LegacyMissionSession session, int tileIndex) =>
        HousingPlacement.Begin(session.LogicalChunks, tileIndex);

    public static bool CanBeginHousingPlacement(LegacyMissionSession session, int tileIndex) =>
        LegacyHousingPlacement.CanBeginAt(session.LogicalChunks, tileIndex);

    public bool UpdateHousingPlacement(int tileIndex) => HousingPlacement.Update(tileIndex);

    public LegacyHousingPlacementResult CommitHousingPlacement(LegacyMissionSession session)
    {
        LegacyHousingPlacementResult result = HousingPlacement.Commit();
        session.ReplaceLogicalChunk("terrain_grid", result.TerrainGrid);
        session.ReplaceLogicalChunk("building_grid", result.BuildingGrid);
        session.ReplaceLogicalChunk("buildings", result.Buildings);
        session.ReplaceLogicalChunk("image_grid", result.ImageGrid);
        session.ReplaceLogicalChunk("bitfields_grid", result.BitfieldsGrid);
        session.ReplaceLogicalChunk("edge_grid", result.EdgeGrid);
        return result;
    }

    public void CancelHousingPlacement() => HousingPlacement.Reset();

    public bool BeginClearLandPlacement(LegacyMissionSession session, int tileIndex) =>
        ClearLandPlacement.Begin(session.LogicalChunks, tileIndex);

    public bool UpdateClearLandPlacement(int tileIndex) => ClearLandPlacement.Update(tileIndex);

    public LegacyClearLandBlockReason GetClearLandBlockReason() =>
        ClearLandPlacement.GetBlockReason();

    public LegacyClearLandResult CommitClearLandPlacement(LegacyMissionSession session)
    {
        LegacyClearLandResult result = ClearLandPlacement.Commit();
        if (result.BlockReason != LegacyClearLandBlockReason.None)
            return result;
        session.ReplaceLogicalChunk("terrain_grid", result.TerrainGrid);
        session.ReplaceLogicalChunk("building_grid", result.BuildingGrid);
        session.ReplaceLogicalChunk("buildings", result.Buildings);
        session.ReplaceLogicalChunk("image_grid", result.ImageGrid);
        session.ReplaceLogicalChunk("bitfields_grid", result.BitfieldsGrid);
        session.ReplaceLogicalChunk("edge_grid", result.EdgeGrid);
        session.ReplaceLogicalChunk("aqueduct_grid", result.AqueductGrid);
        session.ReplaceLogicalChunk("sprite_grid", result.SpriteGrid);
        return result;
    }

    public void CancelClearLandPlacement() => ClearLandPlacement.Reset();

    public static bool CanPlaceSafetyService(LegacyMissionSession session, int tileIndex) =>
        LegacySafetyServicePlacement.CanPlace(session, tileIndex);

    public LegacySafetyServicePlacementResult CommitSafetyService(
        LegacyMissionSession session,
        LegacySafetyServiceDefinition definition,
        int tileIndex)
    {
        LegacySafetyServicePlacementResult result =
            LegacySafetyServicePlacement.Commit(session, definition, tileIndex);
        session.ReplaceLogicalChunk("terrain_grid", result.TerrainGrid);
        session.ReplaceLogicalChunk("building_grid", result.BuildingGrid);
        session.ReplaceLogicalChunk("buildings", result.Buildings);
        session.ReplaceLogicalChunk("image_grid", result.ImageGrid);
        session.ReplaceLogicalChunk("bitfields_grid", result.BitfieldsGrid);
        session.ReplaceLogicalChunk("edge_grid", result.EdgeGrid);
        return result;
    }

    public static bool CanPlaceWell(LegacyMissionSession session, int tileIndex) =>
        LegacyWellPlacement.CanPlace(session, tileIndex);

    public LegacyWellPlacementResult CommitWell(
        LegacyMissionSession session,
        int tileIndex)
    {
        LegacyWellPlacementResult result = LegacyWellPlacement.Commit(session, tileIndex);
        session.ReplaceLogicalChunk("terrain_grid", result.TerrainGrid);
        session.ReplaceLogicalChunk("building_grid", result.BuildingGrid);
        session.ReplaceLogicalChunk("buildings", result.Buildings);
        session.ReplaceLogicalChunk("image_grid", result.ImageGrid);
        session.ReplaceLogicalChunk("bitfields_grid", result.BitfieldsGrid);
        session.ReplaceLogicalChunk("edge_grid", result.EdgeGrid);
        return result;
    }

    public void CancelAll()
    {
        HeldBuildingType=null;
        RoadPlacement.Reset();
        HousingPlacement.Reset();
        ClearLandPlacement.Reset();
    }
}
