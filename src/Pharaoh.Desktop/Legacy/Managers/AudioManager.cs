using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Owns the current WinMM menu-music and UI-effect playback adapters.</summary>
internal sealed class AudioManager : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndNodefault = 0x0002;
    private const uint SndFilename = 0x00020000;
    private readonly string buttonPath;
    private readonly string setupMusicPath;
    private readonly string musicAlias = "PharaohMenu" + Guid.NewGuid().ToString("N");
    private bool musicOpen;
    private int musicVolume = 100;
    private int effectsVolume = 100;

    public AudioManager(string installationRoot)
    {
        buttonPath = ResolveFilePath(installationRoot, "Audio", "Wavs", "button.wav");
        setupMusicPath = ResolveFilePath(installationRoot, "Audio", "Music", "setup.mp3");
    }

    public void PlayButton()
    {
        if (effectsVolume > 0 && File.Exists(buttonPath))
            PlaySound(buttonPath, IntPtr.Zero, SndAsync | SndNodefault | SndFilename);
    }

    [RewriteOnly("Applies original sound-option percentages to the available WinMM adapters")]
    public void SetVolumes(int music, int effects)
    {
        musicVolume = Math.Clamp(music, 0, 100);
        effectsVolume = Math.Clamp(effects, 0, 100);
        if (musicOpen)
            Mci($"setaudio {musicAlias} volume to {musicVolume * 10}");
    }

    public bool StartSetupMusic()
    {
        if (!File.Exists(setupMusicPath))
            return false;
        if (!musicOpen)
        {
            int openResult = Mci($"open \"{setupMusicPath}\" type mpegvideo alias {musicAlias}");
            if (openResult != 0)
            {
                TraceMciFailure("open Setup.mp3", openResult);
                return false;
            }
            musicOpen = true;
        }

        Mci($"setaudio {musicAlias} volume to {musicVolume * 10}");
        Mci($"seek {musicAlias} to start");
        int playResult = Mci($"play {musicAlias} from 0 repeat");
        if (playResult == 0)
            return true;

        TraceMciFailure("play Setup.mp3", playResult);
        Mci($"close {musicAlias}");
        musicOpen = false;
        return false;
    }

    public void StopSetupMusic()
    {
        if (!musicOpen)
            return;
        Mci($"stop {musicAlias}");
        Mci($"close {musicAlias}");
        musicOpen = false;
    }

    public void Dispose() => StopSetupMusic();

    private static string ResolveFilePath(params string[] parts)
    {
        if (parts.Length == 0)
            return string.Empty;
        string combined = Path.Combine(parts);
        string? directory = Path.GetDirectoryName(combined);
        string filename = Path.GetFileName(combined);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || string.IsNullOrEmpty(filename))
            return combined;

        try
        {
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(
                    filename, StringComparison.OrdinalIgnoreCase)) ?? combined;
        }
        catch (IOException)
        {
            return combined;
        }
        catch (UnauthorizedAccessException)
        {
            return combined;
        }
    }

    private static int Mci(string command) => mciSendString(command, null, 0, IntPtr.Zero);

    private static void TraceMciFailure(string operation, int errorCode)
    {
        StringBuilder message = new(256);
        mciGetErrorString(errorCode, message, message.Capacity);
        Debug.WriteLine($"{operation} failed ({errorCode}): {message}");
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(
        string command, StringBuilder? result, int resultLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(
        int errorCode, StringBuilder errorText, int errorTextSize);
}
