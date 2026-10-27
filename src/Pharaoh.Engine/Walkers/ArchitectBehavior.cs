namespace Pharaoh.Engine.Walkers;

/// <summary>An architect roams roads and prevents structural collapse as he passes buildings.</summary>
public sealed class ArchitectBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.Architect;

    internal override void Advance(Walkers walkers, Walker walker)
    {
        walkers.AdvanceSafetyPatrol(walker);
    }
}
