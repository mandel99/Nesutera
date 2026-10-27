using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Simulation;

/// <summary>A time-domain projection of the original CPU-driven simulation loop.</summary>
public readonly record struct LegacySimulationAdvance(
    TimeSpan ElapsedSimulationTime,
    TimeSpan TotalSimulationTime,
    long FirstLegacyTick,
    long LegacyTicks,
    long TotalLegacyTicks)
{
    [OriginalAddress(0x004CDC70, "FUN_004cdc70")]
    public long CountCadenceHits(int period)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        return TotalLegacyTicks / period - FirstLegacyTick / period;
    }
}

public readonly record struct LegacyCalendarTime(int MonthIndex, int Year);

/// <summary>
/// Converts Pharaoh's timer-gated ticks into deterministic simulation time. The
/// original speed table is retained, while catch-up work is derived from elapsed
/// monotonic time rather than host CPU throughput.
/// </summary>
public sealed class LegacySimulationClock
{
    public const int LegacyBurstIterationLimit = 10_000;
    public const int NormalGameSpeed = 50;
    public const int NormalTickMilliseconds = 112;
    public const int DispatcherTicksPerDay = 51;
    public const int DaysPerMonth = 16;
    public const int MonthsPerYear = 12;
    public const int LegacyTicksPerMonth = DispatcherTicksPerDay * DaysPerMonth;
    public const int LegacyTicksPerYear = LegacyTicksPerMonth * MonthsPerYear;
    public const long TimeSpanTicksPerLegacyTick =
        TimeSpan.TicksPerMillisecond * NormalTickMilliseconds;

    private static readonly int[] DelayTableMilliseconds =
        [0, 20, 35, 55, 80, 110, 160, 240, 350, 500];

    private long totalSimulationTimeTicks;
    private long fractionalNumerator;
    private int fractionalDenominator = NormalTickMilliseconds;

    public TimeSpan SimulationTime => TimeSpan.FromTicks(totalSimulationTimeTicks);
    public long TotalLegacyTicks => totalSimulationTimeTicks / TimeSpanTicksPerLegacyTick;

    [OriginalAddress(0x00451C50, "FUN_00451c50")]
    [OriginalAddress(0x00452370, "FUN_00452370")]
    public LegacyCalendarTime GetCalendar(int startYear, int startMonth = 0)
    {
        if ((uint)startMonth >= MonthsPerYear)
            throw new ArgumentOutOfRangeException(nameof(startMonth));
        long elapsedMonths = TotalLegacyTicks / LegacyTicksPerMonth;
        long absoluteMonth = checked((long)startMonth + elapsedMonths);
        int month = (int)(absoluteMonth % MonthsPerYear);
        int year = checked(startYear + (int)(absoluteMonth / MonthsPerYear));
        return new LegacyCalendarTime(month, year);
    }

    [OriginalAddress(0x004DDCB0, "FUN_004ddcb0")]
    public static int GetBatchIntervalMilliseconds(int gameSpeed)
    {
        if (gameSpeed is < 10 or > 100)
            throw new ArgumentOutOfRangeException(nameof(gameSpeed));
        int index = (100 - gameSpeed) / 10;
        return DelayTableMilliseconds[index] + 2;
    }

    [OriginalAddress(0x004D6B60, "FUN_004d6b60")]
    [OriginalAddress(0x004DDCB0, "FUN_004ddcb0")]
    public LegacySimulationAdvance Advance(
        TimeSpan elapsedRealTime,
        int gameSpeed,
        bool paused = false,
        bool enabled = true)
    {
        if (elapsedRealTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsedRealTime));

        long firstLegacyTick = TotalLegacyTicks;
        if (!enabled || paused || elapsedRealTime == TimeSpan.Zero)
            return Snapshot(firstLegacyTick, 0);

        int interval = GetBatchIntervalMilliseconds(gameSpeed);
        if (interval != fractionalDenominator)
        {
            fractionalNumerator = (long)((Int128)fractionalNumerator * interval / fractionalDenominator);
            fractionalDenominator = interval;
        }

        Int128 numerator = (Int128)elapsedRealTime.Ticks * NormalTickMilliseconds + fractionalNumerator;
        long elapsedSimulationTicks = checked((long)(numerator / interval));
        fractionalNumerator = (long)(numerator % interval);
        totalSimulationTimeTicks = checked(totalSimulationTimeTicks + elapsedSimulationTicks);
        return Snapshot(firstLegacyTick, elapsedSimulationTicks);
    }

    public void Reset(long initialLegacyTicks = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialLegacyTicks);
        totalSimulationTimeTicks = checked(initialLegacyTicks * TimeSpanTicksPerLegacyTick);
        fractionalNumerator = 0;
        fractionalDenominator = NormalTickMilliseconds;
    }

    private LegacySimulationAdvance Snapshot(long firstLegacyTick, long elapsedSimulationTicks) => new(
        TimeSpan.FromTicks(elapsedSimulationTicks),
        SimulationTime,
        firstLegacyTick,
        TotalLegacyTicks - firstLegacyTick,
        TotalLegacyTicks);
}
