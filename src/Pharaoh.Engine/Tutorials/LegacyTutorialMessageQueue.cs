using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Tutorials;

/// <summary>Presentation ordering for several tutorial events in one dispatcher pass.</summary>
[RewriteOnly("Managed adapter for the original queued message presentation")]
public sealed class LegacyTutorialMessageQueue
{
    private readonly Queue<LegacyTutorialNotification> pending = new();
    public LegacyTutorialNotification? Current { get; private set; }

    public void Enqueue(LegacyTutorialNotification notification)
    {
        if (Current is null) Current = notification;
        else pending.Enqueue(notification);
    }

    public void Dismiss() => Current = pending.TryDequeue(out LegacyTutorialNotification next) ? next : null;

    public void Clear()
    {
        pending.Clear();
        Current = null;
    }
}
