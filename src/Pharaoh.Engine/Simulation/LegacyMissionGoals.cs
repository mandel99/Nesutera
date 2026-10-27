using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Simulation;

/// <summary>Original scenario goals, independent of tutorial notification flags.</summary>
public static class LegacyMissionGoals
{
    [OriginalAddress(0x004EA280, "counts occupied eligible housing records at or above a level")]
    public static int CountQualifyingHousing(LegacyMissionSession session, int minimumLevel)
    {
        ReadOnlySpan<byte> buildings = session.LogicalChunks["buildings"].Span;
        int count = 0;
        for (int offset = 264; offset + 264 <= buildings.Length && offset < 4000 * 264; offset += 264)
        {
            if (buildings[offset] != 0 && buildings[offset + 5] != 0 &&
                BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 28)..]) != 0 &&
                BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]) - 10 >= minimumLevel)
                count++;
        }
        return count;
    }

    [OriginalAddress(0x004EA2C0, "all enabled goals must pass; no goals do not imply victory")]
    public static bool AreSatisfied(LegacyMissionSession session, int? currentYear = null)
    {
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        ReadOnlySpan<byte> city = session.LogicalChunks["city_data"].Span;
        if (BinaryPrimitives.ReadInt16LittleEndian(scenario[648..]) != 0) return false;
        if (ReadInt(scenario, 888) != 0)
        {
            ReadOnlySpan<byte> time = session.LogicalChunks["game_time"].Span;
            if ((currentYear ?? ReadInt(time, 4)) < ReadInt(time, 0)) return false;
        }
        int goals = 0;
        for (int index = 0; index < 4; index++)
        {
            if (scenario[868 + index] == 0) continue;
            goals++;
            if (ReadInt(city, 36004 + index * 4) < ReadInt(scenario, 844 + index * 4)) return false;
            if (index == 2 && !HasMonumentDeliveries(scenario)) return false;
        }
        if (ReadInt(scenario, 896) != 0)
        {
            goals++;
            if (ReadInt(city, 18940) < ReadInt(scenario, 900)) return false;
        }
        if (ReadInt(scenario, 860) != 0)
        {
            goals++;
            if (CountQualifyingHousing(session, ReadInt(scenario, 864)) < ReadInt(scenario, 860)) return false;
        }
        return goals > 0;
    }

    [OriginalAddress(0x004EA930, "required versus delivered monument resources")]
    private static bool HasMonumentDeliveries(ReadOnlySpan<byte> scenario)
    {
        ReadOnlySpan<int> resources = [0, 7, 9, 12, 14, 16, 17, 18, 19, 22, 23, 24, 25, 27, 29];
        foreach (int resource in resources)
            if (ReadInt(scenario, 1444 + resource * 4) < ReadInt(scenario, 1300 + resource * 4))
                return false;
        return true;
    }

    [RewriteOnly("Bounds-safe decoding of confirmed legacy integer fields")]
    internal static int ReadInt(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
}
