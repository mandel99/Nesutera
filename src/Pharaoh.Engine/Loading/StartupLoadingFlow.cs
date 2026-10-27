using Pharaoh.Engine.Installation;

namespace Pharaoh.Engine.Loading;

public sealed record StartupLoadingFrame(
    int Index,
    LegacyLoadingCheckpoint? Checkpoint,
    bool IsExpansionLogo);

public sealed class StartupLoadingFlow
{
    private readonly IReadOnlyList<LegacyLoadingCheckpoint> checkpoints;
    private int index;

    public StartupLoadingFlow(IReadOnlyList<LegacyLoadingCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        if (checkpoints.Count == 0) throw new ArgumentException("At least one loading checkpoint is required.", nameof(checkpoints));
        this.checkpoints = checkpoints;
    }

    public int Index => index;
    public int FrameCount => checkpoints.Count + 1;
    public bool IsComplete => index >= FrameCount;
    public StartupLoadingFrame Current => !IsComplete
        ? index < checkpoints.Count
            ? new StartupLoadingFrame(index, checkpoints[index], IsExpansionLogo: false)
            : new StartupLoadingFrame(index, Checkpoint: null, IsExpansionLogo: true)
        : throw new InvalidOperationException("The startup loading flow is complete.");

    public void Reset() => index = 0;

    public void ShowExpansionLogo() => index = checkpoints.Count;

    public bool Advance()
    {
        if (IsComplete) return false;
        index++;
        return !IsComplete;
    }
}
