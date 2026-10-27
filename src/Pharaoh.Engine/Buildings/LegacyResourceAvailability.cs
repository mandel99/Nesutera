using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Loading;

namespace Pharaoh.Engine.Buildings;

/// <summary>Resources offered by domestic cities, import routes, gifts and their production chains.</summary>
public static class LegacyResourceAvailability
{
    [OriginalAddress(0x005596D0,"FUN_005596d0")]
    public static void Refresh(LegacyMissionSession session)
    {
        int[] resources=Read(session.LogicalChunks),foods=LegacyFoodAvailability.Read(session.LogicalChunks);
        Span<byte> city=session.GetWritableLogicalChunk("city_data").Span;
        foreach(int resource in resources)if(city.Length>29549+resource)city[29549+resource]=1;
        foreach(int food in foods)if(city.Length>29540+food)city[29540+food]=1;
    }
    [OriginalAddress(0x005596D0, "FUN_005596d0")]
    [OriginalAddress(0x00559480, "FUN_00559480")]
    [OriginalAddress(0x00443B90, "FUN_00443b90")]
    [OriginalAddress(0x004439C0, "FUN_004439c0")]
    [OriginalAddress(0x00447790, "FUN_00447790")]
    public static int[] Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks)
    {
        bool[] domestic = new bool[36], imports = new bool[36], gifts = new bool[36];
        ReadOnlySpan<byte> city = chunks.TryGetValue("city_data", out var h) ? h.Span : [];
        ReadOnlySpan<byte> scenario = chunks.TryGetValue("scenario_info", out var s) ? s.Span : [];
        ReadOnlySpan<byte> cities = chunks.TryGetValue("empire_cities", out var c) ? c.Span : [];
        ReadOnlySpan<byte> limits = chunks.TryGetValue("trade_routes_limits", out var l) ? l.Span : [];
        ReadOnlySpan<byte> events = chunks.TryGetValue("scenario_events", out var e) ? e.Span : [];
        int modifier = city.Length >= 37730 ? (Short(city,37724)>0?1:0)-(Short(city,37726)>0?2:0)-(Short(city,37728)>0?1:0) : 0;
        for (int index = 0; index < Math.Min(61,cities.Length/106); index++)
        {
            var record = cities.Slice(index*106,106);
            if (record[0] == 0) continue;
            for (int resource=1; resource<36;resource++)
            {
                if (record[42+resource]!=1) continue;
                if (record[2]==0) domestic[resource]=true;
                else if(record[2] is 1 or 3 or 5)
                {
                    int offset=(record[4]*36+resource)*4;
                    if(offset+4<=limits.Length && LegacyFoodAvailability.EffectiveTradeLimit(BinaryPrimitives.ReadInt32LittleEndian(limits[offset..]),modifier)>0) imports[resource]=true;
                }
            }
        }
        int count=events.Length>=4?Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(events),0,150):0;
        for(int index=0;index<count && 4+(index+1)*124<=events.Length;index++)
        {
            var record=events.Slice(4+index*124,124);
            if(record[2]!=23)continue;
            for(int p=6;p<=10;p+=2){int resource=Short(record,p);if(resource is >0 and <36)gifts[resource]=true;}
        }
        bool weaponsEnabled = scenario.Length>1114 && scenario[1114]!=0;
        bool chariotsEnabled = scenario.Length>1116 && scenario[1116]!=0;
        bool Available(int resource, int depth=0)
        {
            if(depth>8)return false;
            if(domestic[resource]||imports[resource]||gifts[resource])return true;
            return resource switch
            {
                9 => domestic[1],
                10 => weaponsEnabled && Available(29,depth+1),
                13 => Available(11,depth+1), 15 => Available(14,depth+1),
                17 => Available(16,depth+1), 19 => Available(18,depth+1),
                23 => Available(22,depth+1), 28 => chariotsEnabled && Available(20,depth+1),
                33 => Available(32,depth+1),
                12 => Available(11,depth+1) && (domestic[1]||Available(9,depth+1)),
                34 => (Available(11,depth+1)||Available(13,depth+1)) && imports[31],
                _ => false
            };
        }
        List<int> result=[];
        for(int resource=1;resource<36;resource++)
            if(Available(resource)||(city.Length>29549+resource&&city[29549+resource]!=0))result.Add(resource);
        return result.ToArray();
    }

    [OriginalAddress(0x005596D0, "FUN_005596d0")]
    private static short Short(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
}
