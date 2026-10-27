using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

[RewriteOnly("Typed behavior registration delegates to native distribution handlers")]
public sealed class DistributionBehavior(WalkerType type) : WalkerBehavior
{
    public override WalkerType Type => type;
    internal override void Advance(Walkers walkers, Walker walker) => walkers.AdvanceDistributionWalker(walker);
}

public abstract class WalkerBehavior
{
    public abstract WalkerType Type { get; }
    internal abstract void Advance(Walkers walkers, Walker walker);
}

/// <summary>
/// Type 73's hunting actions, reconstructed from the indirect EXE handler.
/// </summary>
public sealed class HunterBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.OstrichHunter;
    [OriginalAddress(0x004A2430, "type-73 dispatcher")]
    internal override void Advance(Walkers walkers, Walker walker) => walkers.AdvanceHunter(walker);
}

public sealed class OstrichBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.Ostrich;
    [OriginalAddress(0x0049BD40, "type-69 dispatcher")]
    internal override void Advance(Walkers walkers, Walker walker) => walkers.AdvanceOstrich(walker);
}

public sealed class HunterArrowBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.HunterArrow;
    [OriginalAddress(0x004A2E90, "type-74 dispatcher")]
    internal override void Advance(Walkers walkers, Walker walker) => walkers.AdvanceHunterArrow(walker);
}

public sealed class MeatCartBehavior : WalkerBehavior
{
    public override WalkerType Type => WalkerType.CartPusher;
    [OriginalAddress(0x0048B620, "type-4 producer meat cart")]
    internal override void Advance(Walkers walkers, Walker walker) => walkers.AdvanceMeatCart(walker);
}
