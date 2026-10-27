using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using BinkInspector;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop;

internal sealed class BinkIntroPlayer : IDisposable
{
    private const int QueueLimit = 12;
    private readonly ConcurrentQueue<VideoFrame> frames = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch clock = new();
    private readonly Task decodeTask;
    private readonly Task<string?> audioTask;
    private Task? disposeCleanupTask;
    private readonly string audioAlias = "PharaohIntro" + Guid.NewGuid().ToString("N");
    private readonly double framesPerSecond;
    private int queuedCount;
    private bool completed;
    private int disposed;
    private bool audioStarted;
    private string? audioPath;
    private Bitmap? current;

    [OriginalAddress(0x00413580, "FUN_00413580")]
    [OriginalAddress(0x00538DC0, "FUN_00538dc0")]
    [OriginalAddress(0x00538E30, "FUN_00538e30")]
    public BinkIntroPlayer(string path)
    {
        BinkFile file = BinkFile.Load(path);
        Width = checked((int)file.Width);
        Height = checked((int)file.Height);
        framesPerSecond = file.FpsDenominator == 0 ? 15 : file.FpsNumerator / (double)file.FpsDenominator;
        FrameCount = file.FrameIndex.Count;
        decodeTask = Task.Run(() => Decode(file, cancellation.Token));
        audioTask = Task.Run(() => BuildAudioWorker(file, cancellation.Token));
        clock.Start();
    }

    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; }
    public Bitmap? Current => Volatile.Read(ref current);
    public Exception? Failure { get; private set; }
    public bool Finished { get; private set; }

    [OriginalAddress(0x00538FA0, "FUN_00538fa0")]
    public bool Update()
    {
        StartAudioWhenReady();
        int target = Math.Min(FrameCount - 1, (int)(clock.Elapsed.TotalSeconds * framesPerSecond));
        VideoFrame? next = null;
        while (frames.TryPeek(out VideoFrame? candidate) && candidate.Index <= target)
        {
            frames.TryDequeue(out next);
            Interlocked.Decrement(ref queuedCount);
        }

        if (next is not null)
        {
            Bitmap? previous = current;
            current = next.Bitmap;
            previous?.Dispose();
        }

        if ((completed || Failure is not null) && Volatile.Read(ref queuedCount) == 0)
            Finished = true;
        return next is not null;
    }

    [OriginalAddress(0x00539170, "FUN_00539170")]
    [OriginalAddress(0x00539220, "FUN_00539220")]
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        Mci($"stop {audioAlias}");
        Mci($"close {audioAlias}");
        Interlocked.Exchange(ref current, null)?.Dispose();

        // A click may arrive while a worker is converting a large frame or
        // decoding an audio packet. Do not dispose its cancellation source or
        // race a final queue insertion after an arbitrary timeout. The video
        // is detached immediately; worker-owned resources are reclaimed after
        // both cancelled tasks have actually stopped.
        disposeCleanupTask = CompleteDisposeAsync();
    }

    private async Task CompleteDisposeAsync()
    {
        string? generatedAudioPath = null;
        try { await decodeTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* Failure was already captured by Decode. */ }
        try { generatedAudioPath = await audioTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* Skipping a video must not surface background audio failure. */ }

        while (frames.TryDequeue(out VideoFrame? frame))
            frame.Bitmap.Dispose();
        cancellation.Dispose();

        string? path = audioPath ?? generatedAudioPath;
        if (path is not null)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Decode(BinkFile file, CancellationToken token)
    {
        try
        {
            using BinkSequentialPacketReader reader = new(file);
            BinkVideoDecoder decoder = new(file);
            for (int index = 0; index < FrameCount; index++)
            {
                token.ThrowIfCancellationRequested();
                while (Volatile.Read(ref queuedCount) >= QueueLimit)
                {
                    token.ThrowIfCancellationRequested();
                    Thread.Sleep(5);
                }
                BinkDecodedVideoFrame decoded = decoder.Decode(reader.ReadFramePacket(index));
                frames.Enqueue(new VideoFrame(index, ToBitmap(decoded.Yuv, decoded.Width, decoded.Height)));
                Interlocked.Increment(ref queuedCount);
            }
            completed = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Failure = exception; }
    }

    private static Bitmap ToBitmap(byte[] yuv, int width, int height)
    {
        int chromaWidth = (width + 1) >> 1;
        int yPlaneSize = width * height;
        int uvPlaneSize = chromaWidth * ((height + 1) >> 1);
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[data.Stride * height];
            for (int y = 0; y < height; y++)
            {
                int yRow = y * width;
                int uvRow = (y >> 1) * chromaWidth;
                int pixelRow = y * data.Stride;
                for (int x = 0; x < width; x++)
                {
                    int uv = uvRow + (x >> 1);
                    int c = Math.Max(0, yuv[yRow + x] - 16);
                    int d = yuv[yPlaneSize + uv] - 128;
                    int e = yuv[yPlaneSize + uvPlaneSize + uv] - 128;
                    int offset = pixelRow + x * 4;
                    pixels[offset] = Clamp((298 * c + 516 * d + 128) >> 8);
                    pixels[offset + 1] = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
                    pixels[offset + 2] = Clamp((298 * c + 409 * e + 128) >> 8);
                    pixels[offset + 3] = 255;
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private void StartAudioWhenReady()
    {
        if (audioStarted || !audioTask.IsCompletedSuccessfully) return;
        audioPath = audioTask.Result;
        audioStarted = true;
        if (audioPath is null) return;
        Mci($"open \"{audioPath}\" type waveaudio alias {audioAlias}");
        Mci($"set {audioAlias} time format milliseconds");
        Mci($"play {audioAlias} from {(long)clock.Elapsed.TotalMilliseconds}");
    }

    private static string? BuildAudio(BinkFile file, CancellationToken token)
    {
        if (file.AudioTracks.Count == 0) return null;
        AudioTrackInfo track = file.AudioTracks[0];
        BinkRdfAudioDecoder decoder = new(track);
        List<float> samples = new(track.MaxDecodedSize > 0 ? checked((int)track.MaxDecodedSize) : 16384);
        using BinkSequentialPacketReader reader = new(file);
        for (int index = 0; index < file.FrameIndex.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            foreach (AudioPacket packet in reader.ReadFramePacket(index).AudioPackets)
                if (packet.Payload.Length > 0) samples.AddRange(decoder.DecodePacket(packet.Payload));
        }
        if (samples.Count == 0) return null;

        int channels = track.IsStereo ? 2 : 1;
        string path = Path.Combine(Path.GetTempPath(), $"pharaoh-intro-{Guid.NewGuid():N}.wav");
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, false);
        int dataLength = checked(samples.Count * 2);
        int blockAlign = channels * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write((int)track.SampleRate);
        writer.Write(track.SampleRate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (float sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        return path;
    }

    private static string? BuildAudioWorker(BinkFile file, CancellationToken token)
    {
        try
        {
            return BuildAudio(file, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Skipping or closing a video deliberately cancels background
            // audio extraction. Complete the worker normally so the expected
            // cancellation never escapes as an unhandled task exception (or
            // a first-chance exception in the debugger).
            return null;
        }
    }

    private static void Mci(string command) => mciSendString(command, null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? result, int resultLength, IntPtr callback);

    private sealed record VideoFrame(int Index, Bitmap Bitmap);
}
