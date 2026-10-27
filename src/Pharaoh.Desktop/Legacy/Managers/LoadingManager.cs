using Pharaoh.Engine.Installation;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Desktop loading boundary for campaign, save and custom-map sessions.</summary>
internal sealed class LoadingManager
{
    private readonly GameLoadCoordinator<LegacyMissionSession> coordinator =
        new(new LegacyMissionLoader());

    public GameLoadStatus Status => coordinator.Status;
    public LegacyMissionSession? Session => coordinator.Session;
    public Exception? Error => coordinator.Error;
    public GameLoadProgress? Progress => coordinator.Progress;

    [OriginalAddress(0x005405B0, "FUN_005405b0")]
    public Task<GameLoadStatus> LoadCampaignAsync(
        string installationRoot,
        int difficulty,
        int missionId,
        CancellationToken cancellationToken = default)
    {
        GameLoadRequest request = new(
            GameLoadKind.NewCampaign,
            Path.Combine(installationRoot, "mission1.pak"),
            difficulty,
            missionId);
        return coordinator.LoadAsync(request, cancellationToken);
    }

    [RewriteOnly("Desktop loading boundary for the verified saved-game loader")]
    public Task<GameLoadStatus> LoadSavedGameAsync(
        string path,
        int difficulty,
        CancellationToken cancellationToken = default) =>
        coordinator.LoadAsync(
            new GameLoadRequest(GameLoadKind.SavedGame, path, difficulty),
            cancellationToken);

    public void Cancel() => coordinator.Cancel();
}
