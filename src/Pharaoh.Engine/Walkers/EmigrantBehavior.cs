using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

/// <summary>Walker type 2: leaves its source house and walks to the city exit.</summary>
public sealed class EmigrantBehavior : WalkerBehavior
{
    public const int LeavingHouse = 8;
    public const int WalkingToRoad = 9;
    public const int LeavingCity = 10;

    public override WalkerType Type => WalkerType.Emigrant;

    [OriginalAddress(0x0048A260, "Type-2 indirect walker dispatcher")]
    internal override void Advance(Walkers walkers, Walker walker)
    {
        switch (walker.ActionState)
        {
            case LeavingHouse:
                walker.AnimationFrame = 0;
                walker.WaitTicks++;
                if (walker.WaitTicks >= 5)
                {
                    if (!walkers.TryPrepareEmigrantRoadExit(walker))
                        walkers.Destroy(walker, clearBuildingSlot: false);
                    else
                        walker.ActionState = WalkingToRoad;
                }
                break;

            case WalkingToRoad:
                if (walkers.AdvanceResidentApproach(walker))
                {
                    walker.ActionState = LeavingCity;
                    walker.DestinationTile = walkers.ExitTile;
                    walker.route.Clear();
                    walker.RoutePosition = 0;
                }
                break;

            case LeavingCity:
                if (walker.route.Count == 0 && !walkers.TryBuildExitRoute(walker))
                {
                    walkers.Destroy(walker, clearBuildingSlot: false);
                    return;
                }
                if (walkers.AdvanceAlongRoute(walker))
                    walkers.DepartCity(walker);
                break;

            default:
                walker.ActionState = LeavingHouse;
                break;
        }
    }
}
