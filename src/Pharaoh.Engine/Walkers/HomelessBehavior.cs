using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

/// <summary>
/// Walker type 3 (SprMain "Vagrant"): displaced residents first seek another
/// house with room and only leave through the Kingdom exit while none exists.
/// </summary>
public sealed class HomelessBehavior : WalkerBehavior
{
    public const int SeekingHousing = 8;
    public const int WalkingToHouse = 9;
    public const int EnteringHouse = 10;
    public const int LeavingCity = 11;
    public const int InitialSearchDelay = 51;
    public const int LeavingSearchPeriod = 30;

    public override WalkerType Type => WalkerType.Homeless;

    [OriginalAddress(0x0048A720, "Type-3 walker dispatcher")]
    [OriginalAddress(0x00459020, "FUN_00459020")]
    internal override void Advance(Walkers walkers, Walker walker)
    {
        walker.TerrainUsage = WalkerTerrainUsage.PreferRoads;
        switch (walker.ActionState)
        {
            case SeekingHousing:
                walker.AnimationFrame = 0;
                if (++walker.WaitTicks <= InitialSearchDelay)
                    return;
                walker.WaitTicks = 0;
                if (walkers.TryReserveHomeForHomeless(walker, out bool missingRoad))
                {
                    walker.ActionState = WalkingToHouse;
                    return;
                }
                if (missingRoad)
                {
                    walkers.Destroy(walker, clearBuildingSlot: false);
                    return;
                }
                walker.ActionState = LeavingCity;
                walker.route.Clear();
                walker.RoutePosition = 0;
                walker.DestinationTile = walkers.ExitTile;
                break;

            case WalkingToHouse:
                if (!walkers.IsReservedHomeValid(walker))
                {
                    walkers.ReleaseHomelessReservation(walker);
                    walker.ActionState = SeekingHousing;
                    walker.WaitTicks = InitialSearchDelay;
                    walker.route.Clear();
                    walker.RoutePosition = 0;
                    walker.ProgressOnTile = 0;
                    return;
                }
                if (walker.route.Count == 0 && !walkers.TryBuildHomelessRoute(walker))
                {
                    walkers.ReleaseHomelessReservation(walker);
                    walkers.Destroy(walker, clearBuildingSlot: false);
                    return;
                }
                if (walkers.AdvanceAlongRoute(walker))
                {
                    walkers.PrepareHomelessHouseEntry(walker);
                    walker.ActionState = EnteringHouse;
                }
                break;

            case EnteringHouse:
                if (!walkers.IsReservedHomeValid(walker))
                {
                    walkers.ReleaseHomelessReservation(walker);
                    walker.ActionState = SeekingHousing;
                    walker.WaitTicks = InitialSearchDelay;
                    walker.route.Clear();
                    walker.RoutePosition = 0;
                    walker.ProgressOnTile = 0;
                    return;
                }
                if (walkers.PrepareResidentHouseEntry(walker) && walkers.AdvanceResidentApproach(walker))
                    walkers.EnterReservedHome(walker);
                break;

            case LeavingCity:
                bool hasExitRoute = walker.route.Count != 0 || walkers.TryBuildExitRoute(walker);
                if (hasExitRoute && walkers.AdvanceAlongRoute(walker))
                {
                    walkers.DepartCity(walker);
                    return;
                }
                if (++walker.WaitTicks > LeavingSearchPeriod)
                {
                    walker.WaitTicks = 0;
                    if (walkers.TryReserveHomeForHomeless(walker, out _))
                    {
                        walker.ActionState = WalkingToHouse;
                        return;
                    }
                }
                break;
        }
    }
}
