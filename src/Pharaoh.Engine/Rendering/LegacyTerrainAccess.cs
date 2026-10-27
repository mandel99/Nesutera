using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Pharaoh.Engine.Environment;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Engine.Rendering;

/// <summary>Terrain flags alone do not describe native bank objects or walking permissions.</summary>
public static class LegacyTerrainAccess
{
    private sealed record Banks(LegacyFloodplainTopology Topology);
    private static readonly ConditionalWeakTable<object,Banks> BankCache=new();

    [RewriteOnly("Invalidate derived bank membership when a mission publishes new terrain geometry")]
    internal static void Invalidate(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks) => BankCache.Remove(chunks);

    [OriginalAddress(0x004BE160,"non-floodable row nodes correspond to native bank objects at00D05720")]
    public static bool IsBank(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int tile)
    {
        if((uint)tile>=LegacyCityMapSnapshot.TileCount)return false;
        Banks banks=BankCache.GetValue(chunks,_=>
        {
            ReadOnlySpan<byte> scenario=chunks["scenario_info"].Span;
            // Ordering keys do not affect bank membership; isolated placement fixtures may omit them.
            ReadOnlyMemory<byte> random=chunks.TryGetValue("random_grid",out var supplied)?supplied:new byte[LegacyCityMapSnapshot.TileCount];
            return new(LegacyFloodplainTopology.Build(chunks["terrain_grid"].Span,random.Span,
                BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]),BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]),
                BinaryPrimitives.ReadInt32LittleEndian(scenario[52..])));
        });
        return banks.Topology.IsBankHelper(tile);
    }

    [OriginalAddress(0x00516F90,"derive native land-routing classes; bank corners and straight fringes are negative")]
    public static int GetLandClass(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int tile)
    {
        if((uint)tile>=LegacyCityMapSnapshot.TileCount)return -1;
        uint flags=BinaryPrimitives.ReadUInt32LittleEndian(chunks["terrain_grid"].Span[(tile*4)..]);
        if((flags&0x80000)!=0||(flags&0x10004)==0x10004)return -1;
        if((flags&0x40000040)!=0)return 0;
        if((flags&0x400)!=0)return 2;
        if((flags&0x10000000)!=0)return 5;
        if((flags&0x8008)!=0)
        {
            int id=BinaryPrimitives.ReadInt16LittleEndian(chunks["building_grid"].Span[(tile*2)..]);
            if(id<=0||id>=4000)return -1;
            int type=BinaryPrimitives.ReadInt16LittleEndian(chunks["buildings"].Span[(id*264+16)..]);
            return type switch{138 or 215 or 136=>0,54=>6,209=>4,_=>-1};
        }
        if((flags&0x100)!=0)return 4;
        if(IsBank(chunks,tile))return -1;
        if((flags&0x10000)!=0||(flags&0x02040011)!=0)return 4;
        if((flags&0x1000)!=0)return 2;
        return (flags&0xeeffd77f)==0?4:-1;
    }

    [OriginalAddress(0x00517F20,"free land mode accepts routing classes below5")]
    [OriginalAddress(0x005181C0,"animal mode rejects negative bank classes and classes1/3")]
    [OriginalAddress(0x00519840,"road mode accepts nonnegative classes below3")]
    public static bool CanWalk(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int tile,WalkerTerrainUsage usage)
    {
        int kind=GetLandClass(chunks,tile);
        return usage switch
        {
            WalkerTerrainUsage.Roads=>kind>=0&&kind<3,
            WalkerTerrainUsage.Animal=>kind>=0&&kind is not(1 or 3),
            _=>kind>=0&&kind<5
        };
    }
}
