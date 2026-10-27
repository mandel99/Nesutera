using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed class ImmigrantBehavior : WalkerBehavior
{
    // Kept only so saves produced by the first rewrite implementation remain loadable.
    public const int Created = 0;
    public const int Recalculate = 8;
    public const int Arriving = 9;
    public const int EnteringHouse = 10;
    public const int MaximumReroutes = 20;

    public override WalkerType Type => WalkerType.Immigrant;

    [OriginalAddress(0x00458D00, "FUN_00458d00")]
    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    [OriginalAddress(0x00489CA0, "FUN_00489ca0")]
    internal override void Advance(Walkers walkers, Walker walker)
    {
        if (!walkers.IsValidHouse(walker.HomeBuildingId))
        {
            walkers.Destroy(walker);
            return;
        }

        switch (walker.ActionState)
        {
            case Created:
            case Recalculate:
                walker.AnimationFrame = 0;
                if (--walker.WaitTicks <= 0)
                    walker.ActionState = Arriving;
                break;

            case Arriving:
                if (walker.route.Count == 0 && !walkers.TryBuildRoute(walker))
                {
                    walker.RerouteCounter++;
                    if (walker.RerouteCounter > MaximumReroutes)
                    {
                        walkers.Destroy(walker);
                        return;
                    }
                    walker.WaitTicks = 20;
                    walker.ActionState = Recalculate;
                    break;
                }
                if (walkers.AdvanceAlongRoute(walker))
                {
                    walkers.PrepareResidentHouseEntry(walker);
                    walker.ActionState = EnteringHouse;
                }
                break;

            case EnteringHouse:
                if (walkers.PrepareResidentHouseEntry(walker) && walkers.AdvanceResidentApproach(walker))
                    walkers.EnterHouse(walker);
                break;
        }
    }
}
