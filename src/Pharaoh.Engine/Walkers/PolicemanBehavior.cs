namespace Pharaoh.Engine.Walkers;

/// <summary>A constable patrols until a thief or dangerous animal enters his search radius.</summary>
public sealed class PolicemanBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.Policeman;

    internal override void Advance(Walkers walkers, Walker walker)
    {
        if (!walkers.AdvancePolicemanCombat(walker))
            walkers.AdvanceSafetyPatrol(walker);
    }
}
