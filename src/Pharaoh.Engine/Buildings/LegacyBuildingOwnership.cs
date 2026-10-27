using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public static class LegacyBuildingOwnership
{
    [OriginalAddress(0x0046A400, "building +1 city index and +2 faction copied from city record +5")]
    public static void Initialize(Span<byte> building, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks)
    {
        int city = ReadCityIndex(chunks);
        building[1] = checked((byte)city);
        if (chunks.TryGetValue("city_data", out ReadOnlyMemory<byte> data) && data.Length > city * 18904 + 5)
            building[2] = data.Span[city * 18904 + 5];
    }

    [RewriteOnly("Reads the serialized selector corresponding to the native active-city global EAAC28")]
    public static int ReadCityIndex(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks)
    {
        int city = 1;
        if (chunks.TryGetValue("city_data_extra", out ReadOnlyMemory<byte> extra) && extra.Length >= 72)
        {
            int selected = BinaryPrimitives.ReadInt32LittleEndian(extra.Span[68..]);
            if ((uint)selected < 2) city = selected;
        }
        return city;
    }
}
