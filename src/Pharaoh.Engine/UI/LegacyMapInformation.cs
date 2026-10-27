using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public readonly record struct LegacyInfoBounds(int X,int Y,int Width,int Height);

/// <summary>Native map inspection selection, window placement and persistent storage commands.</summary>
public static class LegacyMapInformation
{
    public static readonly int[] WalkerNeighborOffsets=[0,-228,228,1,-1,-229,-227,227,229];

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    public static int BuildingAt(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int tile)
    {
        if((uint)tile>=51984||!chunks.TryGetValue("building_grid",out var grid)||grid.Length<(tile+1)*2||!chunks.TryGetValue("buildings",out var pool))return 0;
        int id=Short(grid.Span,tile*2);
        if(id<=0||pool.Length<(id+1)*264||pool.Span[id*264]==0)return 0;
        // Secondary footprint records point back to their owning building.
        HashSet<int> visited=[];
        while(visited.Add(id))
        {
            var b=pool.Span.Slice(id*264,264);int type=Short(b,16);
            if(type is not (54 or 73 or 210 or 218 or 219 or 220))break;
            int parent=Short(b,56);
            if(parent<=0||parent==id||pool.Length<(parent+1)*264)break;
            id=parent;
        }
        return id;
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    [OriginalAddress(0x00480180,"FUN_00480180")]
    public static bool HasRoad(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int buildingId)
    {
        var b=chunks["buildings"].Span.Slice(buildingId*264,264);var scenario=chunks["scenario_info"].Span;
        return LegacyBuildingRoadAccess.HasNearbyRoad(chunks["terrain_grid"].Span,BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]),BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]),BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]),BinaryPrimitives.ReadInt32LittleEndian(b[12..]),Math.Max(1,(int)b[3]),1);
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    [OriginalAddress(0x004F8960,"FUN_004f8960")]
    public static LegacyInfoBounds Bounds(int displayWidth,int displayHeight,int rows,int clickY,bool compact)
    {
        int x=displayHeight switch{600=>68,768=>188,_=>8};
        if(compact)x+=60;
        int y=displayHeight switch{600 when clickY<313=>(37-rows)*16,768 when clickY<397=>760-rows*16,_=>32};
        return new(x,y,464,rows*16);
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    public static int TerrainCategory(uint terrain,bool floodplainGrass,bool rubble)
    {
        if(rubble)return (terrain&4)!=0?11:10;
        if(floodplainGrass){if((terrain&2)!=0)return 5;if((terrain&0x2000000)!=0)return 22;if((terrain&0x40)!=0)return 13;}
        if((terrain&1)!=0)return 1;
        if((terrain&2)!=0)return (terrain&0x100000)!=0?16:(terrain&0x20000000)!=0?26:17;
        if((terrain&4)!=0&&(terrain&0x10008)==0)return 3;
        if((terrain&0x10)!=0)return 4;
        if((terrain&0x20)!=0)return 12;
        if((terrain&0x40)!=0&&(terrain&8)==0)return 6;
        if((terrain&0x100)!=0)return 7;
        if((terrain&0x1000)!=0)return 8;
        if((terrain&0x40000)!=0)return 21;
        if((terrain&0x2000000)!=0)return 22;
        if((terrain&0xc04000)==0x4000)return 9;
        if((terrain&0xc04000)==0x404000)return 23;
        if((terrain&0xc04000)==0x804000)return 24;
        if((terrain&0x10004)==0x10000&&(terrain&8)==0)return 19;
        if((terrain&0x800)!=0&&(terrain&8)==0)return 25;
        if((terrain&0x10000)!=0&&(terrain&8)==0)return 20;
        return 10;
    }

    [OriginalAddress(0x00406300,"FUN_00406300")]
    public static void CycleOrder(LegacyMissionSession session,int buildingId,int resource)
    {
        if(resource is <1 or >35)return;
        Span<byte> b=session.GetWritableLogicalChunk("buildings").Span.Slice(buildingId*264,264);
        Span<byte> storage=session.GetWritableLogicalChunk("building_storages").Span;
        int p=b[193]*196+10+resource;if(b[193]==0||p>=storage.Length)return;
        bool bazaar=Short(b,16) is 70 or 190;
        storage[p]=storage[p] switch{0=>(byte)(bazaar?1:2),1=>0,2=>3,3=>1,_=>storage[p]};
    }

    [OriginalAddress(0x00406620,"FUN_00406620")]
    public static void Refuse(LegacyMissionSession session,int buildingId,int resource)
    {
        var b=session.LogicalChunks["buildings"].Span.Slice(buildingId*264,264);
        Span<byte> storage=session.GetWritableLogicalChunk("building_storages").Span;
        int p=b[193]*196+10+resource;if(b[193]>0&&resource is >0 and <36&&p<storage.Length)storage[p]=1;
    }

    [OriginalAddress(0x00406460,"FUN_00406460")]
    public static void ChangeLimit(LegacyMissionSession session,int buildingId,int resource,int amount)
    {
        if(resource is <1 or >35)return;
        var b=session.LogicalChunks["buildings"].Span.Slice(buildingId*264,264);
        Span<byte> storage=session.GetWritableLogicalChunk("building_storages").Span;
        int p=b[193]*196;if(b[193]==0||p+196>storage.Length)return;
        int mode=storage[p+10+resource];if(mode is not (0 or 2))return;
        int offset=p+(mode==0?52:124)+resource*2;
        BinaryPrimitives.WriteInt16LittleEndian(storage[offset..],(short)Math.Clamp(Short(storage,offset)+amount,800,3200));
    }

    [OriginalAddress(0x00459240,"FUN_00459240")]
    public static bool IsProductionStopped(LegacyMissionSession session,int resource)
    {
        var city=session.LogicalChunks["city_data"].Span;int offset=29326+resource*2;
        return resource is >=0 and <36&&offset+2<=city.Length&&Short(city,offset)!=0;
    }

    [OriginalAddress(0x0045D510,"FUN_0045d510")]
    public static int Stock(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks,int buildingId,int resource)
    {
        var pool=chunks["buildings"].Span;var b=pool.Slice(buildingId*264,264);int type=Short(b,16);
        if(type is 71 or 191)return resource is >=1 and <=8?Short(b,86+resource*2):0;
        if(type is 70 or 190){int index=LegacyDistributionRules.BazaarIndex(resource);return index>=0?Short(b,86+index*2):0;}
        if(type is not (72 or 193))return b[67]==resource?Math.Max(0,(int)Short(b,60)):0;
        int result=0,id=buildingId;HashSet<int> seen=[];
        for(int index=0;index<8;index++)
        {
            id=Short(pool.Slice(id*264,264),58);
            if(id<=0||pool.Length<(id+1)*264||!seen.Add(id))break;
            var bay=pool.Slice(id*264,264);if(Short(bay,18)==resource)result+=Math.Max(0,(int)Short(bay,60));
        }
        return result;
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    private static short Short(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
}
