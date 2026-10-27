using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

/// <summary>
/// The measured 47-stage sidebar transition stored at 005F8260. The original
/// reads the same table forward while collapsing and backward while expanding.
/// </summary>
public static class LegacyCitySidebarAnimation
{
    public const int FinalStep = 47;

    private static readonly int[] ExpandedPanelOffsets =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 21, 24, 27,
        30, 33, 37, 41, 45, 49, 54, 59, 64, 70, 76, 83, 91, 99, 106,
        113, 119, 125, 130, 135, 139, 143, 146, 149, 152, 154, 156, 158,
        160, 162, 165
    ];

    [OriginalAddress(0x0051FD60, "FUN_0051fd60")]
    public static int AdvanceStep(int currentStep, int benchmarkDurationMilliseconds)
    {
        int increment = benchmarkDurationMilliseconds switch
        {
            < 25 => 1,
            < 61 => 2,
            < 97 => 3,
            _ => 4
        };
        return Math.Min(FinalStep, currentStep + increment);
    }

    [OriginalAddress(0x0051FD60, "FUN_0051fd60")]
    public static int ExpandedPanelOffset(int step, bool startedCompact)
    {
        if (step is <= 0 or >= FinalStep)
            throw new ArgumentOutOfRangeException(nameof(step), "An animated sidebar step must be from 1 to 46.");
        return startedCompact
            ? ExpandedPanelOffsets[FinalStep - step]
            : ExpandedPanelOffsets[step];
    }
}
