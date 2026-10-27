using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Walkers;
using System.Buffers.Binary;

namespace Pharaoh.Engine.UI;

public static class LegacyWalkerInformation
{
    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    public static Walker ReadInspectionRecord(int id,ReadOnlySpan<byte> record)=>new(id,(WalkerType)record[10],BinaryPrimitives.ReadInt32LittleEndian(record[32..]))
    {
        State=(WalkerState)record[14],Resource=record[11],ActionState=BinaryPrimitives.ReadInt16LittleEndian(record[56..]),
        HomeBuildingId=BinaryPrimitives.ReadInt16LittleEndian(record[90..]),Direction=record[17],
        Cargo=BinaryPrimitives.ReadInt16LittleEndian(record[116..]),NativeRecord=record.ToArray()
    };
    // Read-only REA capture of DAT_006012EC in Pharaoh.exe 2.1.
    private static readonly int[] SpeechGroups=[-1,202,203,204,222,213,-1,245,243,222,242,-1,-1,-1,-1,226,227,228,229,225,224,225,-1,205,248,211,220,-3,-1,235,236,238,239,241,240,212,-1,-1,222,219,207,-1,252,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,221,-1,-1,-1,-1,-1,-1,-2,-1,214,-1,250,249,216,217,218,-1,-1,-1,212,-1,237,244,246,215,-4,-1,-1,-1,-1,212,-1,206,-1,-1,-1,-1,-1,-1,309,-1,-1,247,-1];

    [OriginalAddress(0x0053D280,"FUN_0053d280")]
    public static int SpeechGroup(int type,int subType,int wildlife,int deity)
    {
        int group=(uint)type<SpeechGroups.Length?SpeechGroups[type]:-1;
        if(group==-4)group=(uint)subType<SpeechGroups.Length?SpeechGroups[subType]:-1;
        if(group==-2)return wildlife switch{0=>210,1=>208,2=>209,_=>-1};
        if(group==-3)return deity is >=0 and <=4?230+deity:-1;
        return group>0?group:-1;
    }

    [OriginalAddress(0x004FA8C0,"FUN_004fa8c0")]
    public static int Portrait(int type,int subType,int wildlife,int deity)
    {
        if(type==91)type=subType;
        if(type==27)return deity switch{1=>628,2=>627,3=>629,4=>626,_=>514+type};
        if(type==73)return wildlife switch{0=>625,2=>624,_=>514+type};
        if(type==30)return 543;
        return type switch{24=>23881,102=>23882,103=>23883,104=>23880,105=>23878,106=>23884,107=>23885,108=>23879,109=>23886,_=>514+type};
    }

    [OriginalAddress(0x0053C100,"FUN_0053c100")]
    public static int SpeechItem(int type,int action,int savedItem,int counter,int migrationReason)
    {
        return type switch
        {
            1=>(counter+1)%3,3=>(counter+1)%2,23=>(counter+1)%4,24=>(counter+1)%2,
            2=>migrationReason switch{1=>1,3=>2,2=>0,_=>3},
            8=>10,10=>action==13?(counter+1)%13:savedItem,
            39=>action==8?10:action==9?11:savedItem,
            73=>action==12?1:0,
            _=>savedItem
        };
    }
}
