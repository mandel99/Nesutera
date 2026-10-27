using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyPlacementCell(int Tile, bool IsValid);

/// <summary>The cursor is the rear corner on screen; storage uses the canonical map origin.</summary>
public sealed record LegacySquarePlacementPreview(
    int CursorTile, int OriginTile, int ImageId, IReadOnlyList<LegacyPlacementCell> Cells)
{
    // 00474130 sets bit0x10; 00430EF0 substitutes DAT_00F56BDC.
    public const int HeldFootprintImageId=14834;
    public const int InvalidFootprintMaskImageId=14877;
    public bool IsValid => Cells.Count > 0 && Cells.All(cell => cell.IsValid);

    [OriginalAddress(0x0043B630, "orientation-dependent canonical origin for the square under the cursor")]
    public static int GetOriginTile(int cursorTile, int size, int orientation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 6);
        int last = size - 1;
        return cursorTile - (orientation is 2 or 4 ? last : 0)
            - (orientation is 4 or 6 ? last * LegacyCityMapSnapshot.MapLength : 0);
    }

    [OriginalAddress(0x0043B630, "whole-footprint validation followed by independent green/red cell feedback")]
    public static LegacySquarePlacementPreview Create(
        LegacyMissionSession session, LegacySquareBuildingDefinition definition, int cursorTile, int orientation)
    {
        int origin = GetOriginTile(cursorTile, definition.Size, orientation);
        LegacyPlacementCell[] cells = LegacySquareBuildingPlacement.Cells(origin, definition.Size)
            .Select(tile => new LegacyPlacementCell(tile, LegacySquareBuildingPlacement.CanPlaceCell(session,tile)))
            .ToArray();
        return new(cursorTile, origin, definition.ImageId, cells);
    }
}
