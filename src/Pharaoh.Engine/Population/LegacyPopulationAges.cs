using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Population;

/// <summary>Native age buckets and the non-serialized one-hundred-draw random history.</summary>
internal static class LegacyPopulationAges
{
    private sealed class History { public readonly int[] Values = new int[100]; public int Next; }
    private static readonly ConditionalWeakTable<LegacyMissionSession, History> Histories = new();
    public const int BucketsOffset = 28572;

    [OriginalAddress(0x004BB3F0, "cache the previous seven-bit draw before advancing the LFSRs")]
    public static void RecordPreviousRandom(LegacyMissionSession session, uint value)
    {
        History history = Histories.GetOrCreateValue(session);
        history.Values[history.Next] = (int)(value & 127);
        history.Next = (history.Next + 1) % 100;
    }

    [OriginalAddress(0x004E9400, "incoming ages from cached random values; alternating child/adult adjustment")]
    [OriginalAddress(0x004E9350, "remove residents from populated age buckets with bounded fallback")]
    public static void ApplyDelta(LegacyMissionSession session, int delta)
    {
        if (delta == 0) return;
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        History history = Histories.GetOrCreateValue(session);
        int cursor = history.Next;
        if (delta > 0)
        {
            for (int i = 0; i < delta; i++, cursor = (cursor + 1) % 100)
            {
                int age = history.Values[cursor] & 63;
                if (age >= 51) age -= 30;
                else if (age < 10 && (i & 1) != 0) age += 20;
                Span<byte> bucket = city[(BucketsOffset + age * 2)..];
                int count = BinaryPrimitives.ReadInt16LittleEndian(bucket);
                BinaryPrimitives.WriteInt16LittleEndian(bucket, checked((short)Math.Min(short.MaxValue, count + 1)));
            }
            return;
        }
        int remaining = -delta, empty = 0;
        while (remaining > 0 && empty < 100)
        {
            int age = history.Values[cursor] & 63;
            Span<byte> bucket = city[(BucketsOffset + age * 2)..];
            short count = BinaryPrimitives.ReadInt16LittleEndian(bucket);
            if (count > 0) { BinaryPrimitives.WriteInt16LittleEndian(bucket, (short)(count - 1)); remaining--; empty = 0; }
            else empty++;
            cursor = (cursor + 1) % 100;
        }
        cursor = 10; empty = 0;
        while (remaining > 0 && empty < 100)
        {
            Span<byte> bucket = city[(BucketsOffset + cursor * 2)..];
            short count = BinaryPrimitives.ReadInt16LittleEndian(bucket);
            if (count > 0) { BinaryPrimitives.WriteInt16LittleEndian(bucket, (short)(count - 1)); remaining--; empty = 0; }
            else empty++;
            cursor = (cursor + 1) % 100;
        }
    }
}
