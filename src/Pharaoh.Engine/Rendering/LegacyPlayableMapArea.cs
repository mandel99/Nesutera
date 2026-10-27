using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

/// <summary>
/// Shared geometry predicates for Pharaoh's isometric map diamond.
/// The serialized scenario rectangle includes corner padding that is visible
/// to the renderer but is not playable or buildable.
/// </summary>
public static class LegacyPlayableMapArea
{
    [OriginalAddress(0x0046F930, "FUN_0046f930")]
    public static bool Contains(int tile, int width, int height, int? origin = null)
    {
        if ((uint)tile >= LegacyCityMapSnapshot.TileCount ||
            width <= 0 || height <= 0 ||
            width > LegacyCityMapSnapshot.MapLength ||
            height > LegacyCityMapSnapshot.MapLength)
            return false;

        GetBounds(width, height, origin,
            out int minimumX, out int minimumY,
            out int maximumX, out int maximumY,
            out int middleX, out int middleY);
        int x = tile % LegacyCityMapSnapshot.MapLength;
        int y = tile / LegacyCityMapSnapshot.MapLength;
        if (middleX < x)
        {
            return y <= middleY
                ? x - y <= maximumX - middleY
                : x + y <= maximumX + 1 + middleY;
        }

        return y <= middleY
            ? minimumX + middleY <= x + y
            : middleX - maximumY <= x - y;
    }

    /// <summary>
    /// Returns true for cells inside the playable diamond but within the
    /// requested inset from one of its four diagonal edges. Placement callers
    /// reject this band; roads pass an inset of three.
    /// </summary>
    [OriginalAddress(0x0046F9A0, "FUN_0046f9a0")]
    public static bool IsInSafeBorder(
        int tile, int width, int height, int inset, int? origin = null)
    {
        if (inset < 0)
            throw new ArgumentOutOfRangeException(nameof(inset));
        if (!Contains(tile, width, height, origin))
            return false;

        GetBounds(width, height, origin,
            out int minimumX, out _,
            out int maximumX, out int maximumY,
            out int middleX, out int middleY);
        int x = tile % LegacyCityMapSnapshot.MapLength;
        int y = tile / LegacyCityMapSnapshot.MapLength;
        if (middleX < x)
        {
            return y <= middleY
                ? maximumX - middleY < x - y + inset
                : maximumX + 1 + middleY < x + y + inset;
        }

        return y <= middleY
            ? x - inset + y < minimumX + middleY
            : x - y - inset < middleX - maximumY;
    }

    public static bool IsBuildable(
        int tile, int width, int height, int safeBorder, int? origin = null) =>
        Contains(tile, width, height, origin) &&
        !IsInSafeBorder(tile, width, height, safeBorder, origin);

    private static void GetBounds(
        int width,
        int height,
        int? origin,
        out int minimumX,
        out int minimumY,
        out int maximumX,
        out int maximumY,
        out int middleX,
        out int middleY)
    {
        minimumX = origin is int tileOrigin
            ? tileOrigin % LegacyCityMapSnapshot.MapLength
            : (LegacyCityMapSnapshot.MapLength - width) / 2;
        minimumY = origin is int tileOriginY
            ? tileOriginY / LegacyCityMapSnapshot.MapLength
            : (LegacyCityMapSnapshot.MapLength - height) / 2;
        maximumX = minimumX - 1 + width;
        maximumY = minimumY - 1 + height;
        middleX = (maximumX + minimumX) / 2;
        middleY = (maximumY + minimumY) / 2;
    }
}
