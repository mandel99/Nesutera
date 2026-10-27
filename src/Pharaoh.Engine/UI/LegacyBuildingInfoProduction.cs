using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public static class LegacyBuildingInfoProduction
{
    [OriginalAddress(0x005085B0, "FUN_005085b0")]
    [OriginalAddress(0x00508A60, "FUN_00508a60")]
    [OriginalAddress(0x005091C0, "FUN_005091c0")]
    [OriginalAddress(0x00509570, "FUN_00509570")]
    [OriginalAddress(0x00509920, "FUN_00509920")]
    [OriginalAddress(0x0050A060, "FUN_0050a060")]
    [OriginalAddress(0x0050A400, "FUN_0050a400")]
    [OriginalAddress(0x0050A870, "FUN_0050a870")]
    [OriginalAddress(0x0050ACE0, "FUN_0050ace0")]
    [OriginalAddress(0x0050B160, "FUN_0050b160")]
    [OriginalAddress(0x0050B6D0, "FUN_0050b6d0")]
    [OriginalAddress(0x0050BB50, "FUN_0050bb50")]
    [OriginalAddress(0x0050BFD0, "FUN_0050bfd0")]
    [OriginalAddress(0x0050C540, "FUN_0050c540")]
    [OriginalAddress(0x0050C9B0, "FUN_0050c9b0")]
    [OriginalAddress(0x0050CE20, "FUN_0050ce20")]
    [OriginalAddress(0x0050D290, "FUN_0050d290")]
    public static (int Maximum,int Input) Get(int type)=>type switch
    {
        106=>(200,0),
        109=>(200,0),
        110=>(400,14),
        111=>(400,16),
        112=>(400,29),
        113=>(400,18),
        114=>(400,11),
        161=>(200,0),
        162=>(200,0),
        177=>(400,20),
        194=>(400,0),
        203=>(400,22),
        204=>(400,11),
        205=>(400,20),
        217=>(200,0),
        232=>(400,31),
        233=>(400,32),
        _=>default
    };

    [OriginalAddress(0x004FE9A0, "FUN_004fe9a0")]
    [OriginalAddress(0x004FED40, "FUN_004fed40")]
    [OriginalAddress(0x004FEEA0, "FUN_004feea0")]
    [OriginalAddress(0x004FF200, "FUN_004ff200")]
    public static bool UsesServiceStaffState(int type)=>type is 46 or 49 or 53 or 206;
}
