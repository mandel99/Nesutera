using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Simulation;

/// <summary>Persisted victory presentation and optional continued governing.</summary>
public sealed class LegacyMissionProgress
{
    private readonly LegacyMissionSession session;
    private bool presentationPending;
    private readonly int startYear;
    private readonly int startMonth;

    public LegacyMissionProgress(LegacyMissionSession session)
    {
        this.session = session;
        ReadOnlySpan<byte> time = session.LogicalChunks["game_time"].Span;
        startYear = LegacyMissionGoals.ReadInt(time, 4);
        startMonth = LegacyMissionGoals.ReadInt(time, 8);
    }

    public bool PresentationPending => presentationPending;
    public bool ShowChoice { get; private set; }

    [OriginalAddress(0x004EA2C0, "checks mission goals each dispatcher tick and records first victory")]
    [OriginalAddress(0x004EA740, "continued-governing countdown advances monthly")]
    public bool Advance(long firstLegacyTick, long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        if (legacyTicks == 0 || presentationPending) return presentationPending;
        Memory<byte> city = session.GetWritableLogicalChunk("city_data");
        bool continued = LegacyMissionGoals.ReadInt(city.Span, 36520) != 0;
        if (continued)
        {
            long months = (firstLegacyTick + legacyTicks) / LegacySimulationClock.LegacyTicksPerMonth -
                firstLegacyTick / LegacySimulationClock.LegacyTicksPerMonth;
            int remaining = LegacyMissionGoals.ReadInt(city.Span, 36524);
            remaining = (int)Math.Max(int.MinValue, remaining - months);
            BinaryPrimitives.WriteInt32LittleEndian(city.Span[36524..], remaining);
            if (remaining > 0) return false;
        }
        else if (!LegacyMissionGoals.AreSatisfied(session,
            checked(startYear + (int)((startMonth + (firstLegacyTick + legacyTicks) /
                LegacySimulationClock.LegacyTicksPerMonth) / 12)))) return false;
        ShowChoice = LegacyMissionGoals.ReadInt(city.Span, 37104) == 0;
        BinaryPrimitives.WriteInt32LittleEndian(city.Span[37104..], 1);
        presentationPending = true;
        return true;
    }

    [OriginalAddress(0x004EA770, "continue governing for 24 or 60 additional months")]
    public void ContinueGoverning(int months)
    {
        if (!presentationPending || months is not (24 or 60))
            throw new InvalidOperationException("Continued governing requires a victory and 24 or 60 months.");
        Memory<byte> city = session.GetWritableLogicalChunk("city_data");
        BinaryPrimitives.WriteInt32LittleEndian(city.Span[36520..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(city.Span[36524..],
            checked(LegacyMissionGoals.ReadInt(city.Span, 36524) + months));
        BinaryPrimitives.WriteInt32LittleEndian(city.Span[36528..], months);
        presentationPending = false;
    }
}
