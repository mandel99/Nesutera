using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

/// <summary>Values shown by the legacy city chrome for a newly entered mission.</summary>
public sealed record LegacyCityHudState(
    int Population,
    int Treasury,
    int MonthIndex,
    int Year,
    string CityName)
{
    [RewriteOnly("Projects confirmed scenario_info and city_data fields into the city HUD model")]
    public static LegacyCityHudState FromLogicalChunks(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        string cityName)
    {
        ReadOnlySpan<byte> scenario = RequiredChunk(chunks, "scenario_info", 36);
        ReadOnlySpan<byte> city = RequiredChunk(chunks, "city_data", 18_944);
        int startYear = BinaryPrimitives.ReadInt16LittleEndian(scenario);
        int initialFunds = BinaryPrimitives.ReadInt32LittleEndian(scenario[32..]);
        int population = BinaryPrimitives.ReadInt32LittleEndian(city[18_940..]);
        return new LegacyCityHudState(population, initialFunds, 0, startYear, cityName);
    }

    [RewriteOnly("Validated logical-chunk accessor for the typed HUD projection")]
    private static ReadOnlySpan<byte> RequiredChunk(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        string name,
        int minimumLength)
    {
        if (!chunks.TryGetValue(name, out ReadOnlyMemory<byte> memory) || memory.Length < minimumLength)
            throw new InvalidDataException($"Logical chunk '{name}' is missing or shorter than {minimumLength} bytes.");
        return memory.Span;
    }
}
