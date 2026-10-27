namespace Pharaoh.Engine.Loading;

public enum GameLoadKind
{
    NewCampaign,
    CustomMission,
    SavedGame
}

public enum GameLoadStage
{
    Validating,
    ReadingFiles,
    Decompressing,
    ParsingMap,
    LoadingScenario,
    PreparingGraphics,
    PreparingAudio,
    CreatingSession,
    Completed
}

public sealed record GameLoadRequest(
    GameLoadKind Kind,
    string Path,
    int Difficulty,
    int MissionIndex = -1);
public sealed record GameLoadProgress(GameLoadStage Stage, float Fraction, string Description);

public interface IGameLoader<TSession>
{
    Task<TSession> LoadAsync(
        GameLoadRequest request,
        IProgress<GameLoadProgress> progress,
        CancellationToken cancellationToken);
}

public enum GameLoadStatus
{
    Idle,
    Loading,
    Completed,
    Cancelled,
    Failed
}

public sealed class GameLoadCoordinator<TSession>
{
    private readonly IGameLoader<TSession> loader;
    private CancellationTokenSource? cancellation;

    public GameLoadCoordinator(IGameLoader<TSession> loader) =>
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));

    public GameLoadStatus Status { get; private set; }
    public GameLoadProgress? Progress { get; private set; }
    public TSession? Session { get; private set; }
    public Exception? Error { get; private set; }
    public event Action? Changed;

    public async Task<GameLoadStatus> LoadAsync(GameLoadRequest request, CancellationToken cancellationToken = default)
    {
        if (Status == GameLoadStatus.Loading) throw new InvalidOperationException("A game is already loading.");
        cancellation?.Dispose();
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Status = GameLoadStatus.Loading;
        Progress = null;
        Session = default;
        Error = null;
        Changed?.Invoke();

        InlineProgress<GameLoadProgress> progress = new(value =>
        {
            Progress = value with { Fraction = Math.Clamp(value.Fraction, 0, 1) };
            Changed?.Invoke();
        });

        try
        {
            Session = await loader.LoadAsync(request, progress, cancellation.Token).ConfigureAwait(false);
            Status = GameLoadStatus.Completed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = GameLoadStatus.Cancelled;
        }
        catch (Exception exception)
        {
            Error = exception;
            Status = GameLoadStatus.Failed;
        }
        Changed?.Invoke();
        return Status;
    }

    public void Cancel() => cancellation?.Cancel();

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
