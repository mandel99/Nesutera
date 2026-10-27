using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

public readonly record struct LegacyCartVisual(int ImageId, int X, int Y, bool DrawAfterPusher);

public static class LegacyCartVisuals
{
    private static readonly (int X, int Y)[] Offsets =
        [(13, -7), (18, -1), (12, 7), (0, 11), (-13, 6), (-18, -1), (-13, -7), (0, -12)];

    [OriginalAddress(0x0048B4A0, "FUN_0048b4a0")]
    [OriginalAddress(0x0048B620, "FUN_0048b620")]
    [OriginalAddress(0x00438E50, "FUN_00438e50")]
    public static LegacyCartVisual MeatCart(Sg3Archive main, int direction, int orientation, int cargo)
        => ResourceCart(main, direction, orientation, cargo, 8);

    [OriginalAddress(0x0048B4A0, "FUN_0048b4a0")]
    [OriginalAddress(0x0048B620, "FUN_0048b620")]
    [OriginalAddress(0x00438E50, "FUN_00438e50")]
    public static LegacyCartVisual ResourceCart(Sg3Archive main, int direction, int orientation, int cargo, int resource)
    {
        int screenDirection = (direction - orientation + 8) & 7;
        int loadFrame = cargo <= 200 ? 0 : cargo <= 400 ? 8 : 16;
        int group = cargo > 0 && resource is >= 1 and <= 8 ? 77 + resource : 77;
        int image = main.GroupImageIds[group] + screenDirection + (group != 77 ? loadFrame : 0);
        var offset = Offsets[screenDirection];
        // Native cart coordinates have their own (-2,-10) origin, before
        // subtracting the cargo sprite offsets. They are not body coordinates.
        return new(image, offset.X - 2, offset.Y - 10, ((direction + orientation) & 7) == 3);
    }
}
