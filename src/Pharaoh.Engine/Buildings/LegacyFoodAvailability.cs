using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public static class LegacyFoodAvailability
{
    [OriginalAddress(0x005596D0, "FUN_005596d0")]
    [OriginalAddress(0x00559480, "FUN_00559480")]
    [OriginalAddress(0x00443B90, "FUN_00443b90")]
    [OriginalAddress(0x004439C0, "FUN_004439c0")]
    [OriginalAddress(0x00447790, "FUN_00447790")]
    [OriginalAddress(0x004C5190, "FUN_004c5190")]
    [OriginalAddress(0x004C5240, "FUN_004c5240")]
    public static int[] Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks)
    {
        List<int> foods = [];
        ReadOnlySpan<byte> cities = chunks.TryGetValue("empire_cities", out var c) ? c.Span : [];
        ReadOnlySpan<byte> limits = chunks.TryGetValue("trade_routes_limits", out var t) ? t.Span : [];
        ReadOnlySpan<byte> events = chunks.TryGetValue("scenario_events", out var e) ? e.Span : [];
        ReadOnlySpan<byte> city = chunks.TryGetValue("city_data", out var h) ? h.Span : [];
        int modifier = city.Length >= 37730 ? (BinaryPrimitives.ReadInt16LittleEndian(city[37724..]) > 0 ? 1 : 0) -
            (BinaryPrimitives.ReadInt16LittleEndian(city[37726..]) > 0 ? 2 : 0) -
            (BinaryPrimitives.ReadInt16LittleEndian(city[37728..]) > 0 ? 1 : 0) : 0;
        for (int food = 1; food <= 8; food++)
        {
            bool available = city.Length > 29540 + food && city[29540 + food] != 0;
            for (int index = 0; !available && index < Math.Min(61, cities.Length / 106); index++)
            {
                var record = cities.Slice(index * 106, 106);
                if (record[0] == 0 || record[42 + food] != 1) continue;
                if (record[2] == 0) available = true;
                else if (record[2] is 1 or 3 or 5)
                {
                    int offset = (record[4] * 36 + food) * 4;
                    if (offset + 4 <= limits.Length)
                        available = EffectiveTradeLimit(BinaryPrimitives.ReadInt32LittleEndian(limits[offset..]), modifier) > 0;
                }
            }
            int count = events.Length >= 4 ? Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(events), 0, 150) : 0;
            for (int index = 0; !available && index < count; index++)
            {
                int offset = 4 + index * 124;
                if (offset + 124 > events.Length) break;
                var record = events.Slice(offset, 124);
                if (record[2] == 23)
                    available = BinaryPrimitives.ReadInt16LittleEndian(record[6..]) == food ||
                        BinaryPrimitives.ReadInt16LittleEndian(record[8..]) == food || BinaryPrimitives.ReadInt16LittleEndian(record[10..]) == food;
            }
            if (available) foods.Add(food);
        }
        return foods.ToArray();
    }

    [OriginalAddress(0x004C5240, "FUN_004c5240")]
    public static int EffectiveTradeLimit(int amount, int modifier)
    {
        if (amount == 0) return 0;
        if (modifier > 0) return amount == 25 ? 40 : amount == 15 ? modifier == 1 ? 25 : 40 : amount >= 15 ? amount : modifier == 1 ? 15 : 25;
        if (modifier == -1) return amount == 40 ? 25 : amount == 25 ? 15 : amount == 15 ? 0 : amount;
        if (modifier < -1) return amount == 40 ? 15 : amount is 25 or 15 ? 0 : amount;
        return amount;
    }
}
