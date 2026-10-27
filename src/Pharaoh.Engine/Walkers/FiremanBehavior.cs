namespace Pharaoh.Engine.Walkers;

/// <summary>A fireman patrols normally, but leaves the route to extinguish a burning building.</summary>
public sealed class FiremanBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.Fireman;

    internal override void Advance(Walkers walkers, Walker walker)
    {
        if (!walkers.AdvanceFireResponse(walker))
        {
            walkers.AdvanceSafetyPatrol(walker);
        }
    }
}
