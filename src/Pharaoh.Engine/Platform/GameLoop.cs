using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Platform;

public readonly record struct DispatchResult(bool QuitRequested, int ExitCode = 0);

public interface IPlatformEventPump
{
    bool HasPendingEvent();
    DispatchResult DispatchNextEvent();
    void WaitForEvent();
    void PostCloseRequest();
}

public interface IGameFrameHost
{
    bool CanRunIdleFrame { get; }
    bool TryConsumeCloseRequest();
    void RunIdleFrame();
}

public sealed class GameLoop(IPlatformEventPump eventPump, IGameFrameHost frameHost)
{
    [OriginalAddress(0x00413ED0, "FUN_00413ed0")]
    public int Run()
    {
        while (true)
        {
            if (eventPump.HasPendingEvent())
            {
                DispatchResult result = eventPump.DispatchNextEvent();
                if (result.QuitRequested)
                    return result.ExitCode;
            }
            else if (frameHost.CanRunIdleFrame)
            {
                frameHost.RunIdleFrame();
            }
            else
            {
                eventPump.WaitForEvent();
            }

            if (frameHost.TryConsumeCloseRequest())
                eventPump.PostCloseRequest();
        }
    }
}
