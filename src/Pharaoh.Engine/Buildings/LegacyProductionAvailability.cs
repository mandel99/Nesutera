using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

/// <summary>Potential production uses domestic supplies and importable inputs, not stored goods or gifts.</summary>
public static class LegacyProductionAvailability
{
    [OriginalAddress(0x00443B90,"Domestic resource flags in active empire-city records")]
    public static bool HasDomestic(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int resource)
    {
        if(resource is <1 or >35||!chunks.TryGetValue("empire_cities",out var data))return false;
        var cities=data.Span;
        for(int index=0;index<Math.Min(61,cities.Length/106);index++)
        {var record=cities.Slice(index*106,106);if(record[0]!=0&&record[2]==0&&record[42+resource]==1)return true;}
        return false;
    }
    [OriginalAddress(0x00443D90,"Resolve production inputs before checking domestic cities and potential imports")]
    [OriginalAddress(0x004439C0,"Potential import routes and effective resource trade limits")]
    [OriginalAddress(0x00443B90,"Domestic resource availability")]
    public static bool CanProduce(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int resource)
    {
        if(resource is <1 or >35)return false;
        bool[] domestic=new bool[36],imports=new bool[36];
        ReadOnlySpan<byte> cities=chunks.TryGetValue("empire_cities",out var c)?c.Span:[];
        ReadOnlySpan<byte> limits=chunks.TryGetValue("trade_routes_limits",out var l)?l.Span:[];
        int cityLength=chunks.TryGetValue("city_data",out var d)?d.Length:0;
        int Short(int offset)=>cityLength>=offset+2?BinaryPrimitives.ReadInt16LittleEndian(chunks["city_data"].Span[offset..]):0;
        int modifier=(Short(37724)>0?1:0)-(Short(37726)>0?2:0)-(Short(37728)>0?1:0);
        for(int index=0;index<Math.Min(61,cities.Length/106);index++)
        {
            var record=cities.Slice(index*106,106);if(record[0]==0)continue;
            for(int item=1;item<36;item++)
            {
                if(record[42+item]!=1)continue;
                if(record[2]==0)domestic[item]=true;
                else if(record[2] is 1 or 3 or 5)
                {
                    int offset=(record[4]*36+item)*4;
                    if(offset+4<=limits.Length&&LegacyFoodAvailability.EffectiveTradeLimit(BinaryPrimitives.ReadInt32LittleEndian(limits[offset..]),modifier)>0)
                        imports[item]=true;
                }
            }
        }
        bool Resolve(int item)
        {
            int input=item switch{15=>14,17=>16,33=>32,19=>18,13=>11,10=>29,28=>20,23=>22,_=>item};
            if(item==12)
            {
                if((domestic[1]||Resolve(9))&&(domestic[11]||Resolve(11)))return true;
                return domestic[12];
            }
            if(item==34)return (domestic[13]&&imports[31])||domestic[34];
            return domestic[input]||imports[input];
        }
        return Resolve(resource);
    }
}
