using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Engine.Installation;

/// <summary>Reads and patches the original 564-byte Pharaoh.inf settings structure.</summary>
public static class LegacyGameSettingsStore
{
    public const int FileSize = 0x234;
    private const int FormatVersion = 0x10;

    private const int VersionOffset = 0x000;
    private const int WindowedOffset = 0x004;
    private const int DataDriveLetterOffset = 0x00A;
    private const int MusicEnabledOffset = 0x00C;
    private const int EffectsEnabledOffset = 0x00B;
    private const int SpeechEnabledOffset = 0x00D;
    private const int DisplayModeOffset = 0x010;
    private const int GameSpeedOffset = 0x014;
    private const int ScrollSpeedOffset = 0x018;
    private const int MouseHelpOffset = 0x054;
    private const int StartingKingdomRatingOffset = 0x058;
    private const int CitySoundsEnabledOffset = 0x068;
    private const int WarningsOffset = 0x069;
    private const int PyramidSpeedupOffset = 0x06B;
    private const int EffectsVolumeOffset = 0x06C;
    private const int MusicVolumeOffset = 0x070;
    private const int SpeechVolumeOffset = 0x074;
    private const int CityVolumeOffset = 0x078;
    private const int DormantLegacyValueOffset = 0x07C;
    private const int RamSettingOffset = 0x080;
    private const int CpuSettingOffset = 0x084;
    private const int PreviousDisplayModeOffset = 0x08C;
    private const int AutosaveDisabledOffset = 0x094;
    private const int PopupMessageMaskOffset = 0x098;
    private const int VictoryMovieAlternatorOffset = 0x224;
    private const int DifficultyOffset = 0x228;
    private const int GodsEffectsOffset = 0x22C;
    private const int ClassicalCityNamesOffset = 0x230;
    private const int CheckCdOffset = 0x231;

    [OriginalAddress(0x004147F0, "FUN_004147f0")]
    [OriginalAddress(0x004CEB10, "FUN_004ceb10")]
    public static LegacyCityOptions Load(string installationRoot)
    {
        string path = SettingsPath(installationRoot);
        if (!File.Exists(path))
            return CreateDefaults();

        byte[] data = File.ReadAllBytes(path);
        Validate(data, path);
        int displayMode = ReadInt32(data, DisplayModeOffset);
        (int width, int height) = displayMode switch
        {
            1 => (640, 480),
            2 => (800, 600),
            _ => (1024, 768)
        };

        return new LegacyCityOptions
        {
            DisplayWidth = width,
            DisplayHeight = height,
            FullScreen = ReadInt32(data, WindowedOffset) == 0,
            MusicEnabled = data[MusicEnabledOffset] != 0,
            SpeechEnabled = data[SpeechEnabledOffset] != 0,
            EffectsEnabled = data[EffectsEnabledOffset] != 0,
            CitySoundsEnabled = data[CitySoundsEnabledOffset] != 0,
            MusicVolume = ReadPercentage(data, MusicVolumeOffset),
            SpeechVolume = ReadPercentage(data, SpeechVolumeOffset),
            EffectsVolume = ReadPercentage(data, EffectsVolumeOffset),
            CityVolume = ReadPercentage(data, CityVolumeOffset),
            GameSpeed = Math.Clamp(ReadInt32(data, GameSpeedOffset), 10, 100),
            ScrollSpeed = Math.Clamp(ReadInt32(data, ScrollSpeedOffset), 0, 100),
            Difficulty = Math.Clamp(ReadInt32(data, DifficultyOffset), 0, 4),
            PopupMessageMask = ReadInt32(data, PopupMessageMaskOffset) & 0x1FFE,
            GodsEffects = ReadInt32(data, GodsEffectsOffset) != 0,
            EgyptianCityNames = data[ClassicalCityNamesOffset] == 0,
            Autosave = ReadInt32(data, AutosaveDisabledOffset) == 0,
            PyramidSpeedup = data[PyramidSpeedupOffset] != 0,
            MouseHelp = Math.Clamp(ReadInt32(data, MouseHelpOffset), 0, 2),
            Warnings = data[WarningsOffset] != 0
        };
    }

    [OriginalAddress(0x00414750, "FUN_00414750")]
    [OriginalAddress(0x004CED90, "FUN_004ced90")]
    public static void Save(string installationRoot, LegacyCityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string path = SettingsPath(installationRoot);
        byte[] data = File.Exists(path) ? File.ReadAllBytes(path) : CreateDefaultImage();
        Validate(data, path);

        WriteInt32(data, WindowedOffset, options.FullScreen ? 0 : 1);
        data[MusicEnabledOffset] = BoolByte(options.MusicEnabled);
        data[SpeechEnabledOffset] = BoolByte(options.SpeechEnabled);
        data[EffectsEnabledOffset] = BoolByte(options.EffectsEnabled);
        data[CitySoundsEnabledOffset] = BoolByte(options.CitySoundsEnabled);
        WriteInt32(data, DisplayModeOffset, options.DisplayWidth switch { 640 => 1, 800 => 2, _ => 3 });
        WriteInt32(data, GameSpeedOffset, Math.Clamp(options.GameSpeed, 10, 100));
        WriteInt32(data, ScrollSpeedOffset, Math.Clamp(options.ScrollSpeed, 0, 100));
        WriteInt32(data, MouseHelpOffset, Math.Clamp(options.MouseHelp, 0, 2));
        data[WarningsOffset] = BoolByte(options.Warnings);
        data[PyramidSpeedupOffset] = BoolByte(options.PyramidSpeedup);
        WriteInt32(data, EffectsVolumeOffset, Math.Clamp(options.EffectsVolume, 0, 100));
        WriteInt32(data, MusicVolumeOffset, Math.Clamp(options.MusicVolume, 0, 100));
        WriteInt32(data, SpeechVolumeOffset, Math.Clamp(options.SpeechVolume, 0, 100));
        WriteInt32(data, CityVolumeOffset, Math.Clamp(options.CityVolume, 0, 100));
        WriteInt32(data, AutosaveDisabledOffset, options.Autosave ? 0 : 1);
        WriteInt32(data, PopupMessageMaskOffset, options.PopupMessageMask & 0x1FFE);
        WriteInt32(data, DifficultyOffset, Math.Clamp(options.Difficulty, 0, 4));
        WriteInt32(data, GodsEffectsOffset, options.GodsEffects ? 1 : 0);
        data[ClassicalCityNamesOffset] = BoolByte(!options.EgyptianCityNames);

        SaveAtomically(path, data);
    }

    [OriginalAddress(0x004147F0, "FUN_004147f0")]
    public static LegacyCityOptions CreateDefaults() => new()
    {
        DisplayWidth = 640,
        DisplayHeight = 480,
        FullScreen = false,
        MusicEnabled = true,
        SpeechEnabled = true,
        EffectsEnabled = true,
        CitySoundsEnabled = true,
        MusicVolume = 80,
        SpeechVolume = 100,
        EffectsVolume = 100,
        CityVolume = 100,
        GameSpeed = 90,
        ScrollSpeed = 70,
        Difficulty = 2,
        PopupMessageMask = 0,
        GodsEffects = true,
        EgyptianCityNames = true,
        Autosave = true,
        PyramidSpeedup = false,
        MouseHelp = 2,
        Warnings = true
    };

    [RewriteOnly("Builds a complete original-size settings image when Pharaoh.inf is absent")]
    private static byte[] CreateDefaultImage()
    {
        byte[] data = new byte[FileSize];
        WriteInt32(data, VersionOffset, FormatVersion);
        LegacyCityOptions defaults = CreateDefaults();
        WriteInt32(data, WindowedOffset, 1);
        data[DataDriveLetterOffset] = (byte)'D';
        data[EffectsEnabledOffset] = 1;
        data[MusicEnabledOffset] = 1;
        data[SpeechEnabledOffset] = 1;
        data[CitySoundsEnabledOffset] = 1;
        data[WarningsOffset] = 1;
        WriteInt32(data, GodsEffectsOffset, 1);
        WriteInt32(data, DifficultyOffset, defaults.Difficulty);
        WriteInt32(data, MouseHelpOffset, defaults.MouseHelp);
        WriteInt32(data, StartingKingdomRatingOffset, 50);
        WriteInt32(data, GameSpeedOffset, defaults.GameSpeed);
        WriteInt32(data, ScrollSpeedOffset, defaults.ScrollSpeed);
        WriteInt32(data, EffectsVolumeOffset, defaults.EffectsVolume);
        WriteInt32(data, MusicVolumeOffset, defaults.MusicVolume);
        WriteInt32(data, SpeechVolumeOffset, defaults.SpeechVolume);
        WriteInt32(data, CityVolumeOffset, defaults.CityVolume);
        WriteInt32(data, DisplayModeOffset, 1);
        WriteInt32(data, DormantLegacyValueOffset, 2);
        WriteInt32(data, RamSettingOffset, 15);
        WriteInt32(data, CpuSettingOffset, 0x42);
        WriteInt32(data, PreviousDisplayModeOffset, 1);
        WriteInt32(data, VictoryMovieAlternatorOffset, 0);
        data[CheckCdOffset] = 1;
        return data;
    }

    [RewriteOnly("Rejects truncated or foreign files before any in-place-compatible patch")]
    private static void Validate(byte[] data, string path)
    {
        if (data.Length != FileSize)
            throw new InvalidDataException($"{path} must be exactly {FileSize} bytes, but is {data.Length} bytes.");
        if (ReadInt32(data, VersionOffset) != FormatVersion)
            throw new InvalidDataException($"{path} has an unsupported Pharaoh.inf format version.");
    }

    [RewriteOnly("Publishes a complete settings image without exposing a partially written Pharaoh.inf")]
    private static void SaveAtomically(string path, byte[] data)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(data);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    [RewriteOnly("Resolves the original settings file beside Pharaoh.exe")]
    private static string SettingsPath(string installationRoot) =>
        Path.Combine(Path.GetFullPath(installationRoot), "Pharaoh.inf");

    [RewriteOnly("Reads one little-endian integer field from the original raw structure")]
    private static int ReadInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));

    [RewriteOnly("Clamps one original volume field to the UI-supported percentage range")]
    private static int ReadPercentage(byte[] data, int offset) =>
        Math.Clamp(ReadInt32(data, offset), 0, 100);

    [RewriteOnly("Writes one little-endian integer without changing adjacent structure bytes")]
    private static void WriteInt32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);

    [RewriteOnly("Encodes original byte-sized option flags")]
    private static byte BoolByte(bool value) => value ? (byte)1 : (byte)0;
}
