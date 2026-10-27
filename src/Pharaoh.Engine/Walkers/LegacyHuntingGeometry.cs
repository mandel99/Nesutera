using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public static class LegacyHuntingGeometry
{
    [OriginalAddress(0x0051CD80, "FUN_0051cd80")]
    public static int Facing(int dx,int dy)
    {
        if(dx==0)return dy<0?0:4;
        if(dy==0)return dx<0?6:2;
        if(Math.Abs(dx)*100/Math.Abs(dy)>399)return dx<0?6:2;
        if(Math.Abs(dy)*100/Math.Abs(dx)>399)return dy<0?0:4;
        return dx<0?(dy<0?7:5):(dy<0?1:3);
    }

    [OriginalAddress(0x0051CAE0, "FUN_0051cae0")]
    public static int ArrowDirection(int dx,int dy)
    {
        double length=Math.Sqrt((double)dx*dx+(double)dy*dy);
        if(length==0)return 0;
        double angle=Math.Asin(-dy/length);
        if(dx<=0)angle=3.1429-angle;
        angle-=5.500075;
        while(angle<0)angle+=6.2858;
        return (int)((6.2858-angle)*5.090839670368132)&31;
    }
}
