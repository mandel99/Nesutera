using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

/// <summary>Leaves a new workplace, follows roads and establishes access to local labor.</summary>
public sealed class LaborSeekerBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.Recruiter;

    [OriginalAddress(0x0048BFF0, "native type-five workplace recruiter")]
    internal override void Advance(Walkers walkers, Walker walker)
    {
        walkers.AdvanceRecruiter(walker);
    }
}
