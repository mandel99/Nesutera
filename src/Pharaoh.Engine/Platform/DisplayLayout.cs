using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Platform;

public enum LegacyDisplayMode
{
    Size640x480 = 1,
    Size800x600 = 2,
    Size1024x768 = 3
}

public readonly record struct DisplayLayout(
    int Width,
    int Height,
    int BaseViewportWidth,
    int BaseViewportHeight,
    int HorizontalOffset,
    int VerticalOffset)
{
    [OriginalAddress(0x004CF960, "FUN_004cf960")]
    public static DisplayLayout FromLegacyMode(LegacyDisplayMode mode)
    {
        (int width, int height) = mode switch
        {
            LegacyDisplayMode.Size640x480 => (640, 480),
            LegacyDisplayMode.Size800x600 => (800, 600),
            LegacyDisplayMode.Size1024x768 => (1024, 768),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Pharaoh display mode.")
        };

        const int baseWidth = 640;
        const int baseHeight = 480;
        return new DisplayLayout(
            width,
            height,
            baseWidth,
            baseHeight,
            (width - baseWidth) / 2,
            (height - baseHeight) / 2);
    }
}
