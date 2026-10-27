namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Owns the lifetime and frame progression of the currently playing Bink video.</summary>
internal sealed class VideoManager : IDisposable
{
    private BinkIntroPlayer? player;

    public bool IsPlaying => player is not null;
    public Bitmap? CurrentFrame => player?.Current;
    public bool Finished => player?.Finished ?? false;
    public Exception? Failure => player?.Failure;

    public void Play(string path)
    {
        Stop();
        player = new BinkIntroPlayer(path);
    }

    public bool Update() => player?.Update() ?? false;

    public void Stop()
    {
        player?.Dispose();
        player = null;
    }

    public void Dispose() => Stop();
}
