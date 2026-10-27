using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Installation;

public sealed record LegacyLoadingCheckpoint(int GraphicIndex, int TextGroup, int TextItem);

public sealed record LegacyGraphicsPack(int Slot, string Name, string? Sg3Path, string? PixelDataPath)
{
    public bool IsComplete => Sg3Path is not null && PixelDataPath is not null;
}

public sealed record LegacyDifficultyFiles(string GameModel, string WalkerModel, string TaxSentimentModel);

/// <summary>Statically verified startup resource metadata from Pharaoh.exe 2.1.</summary>
public static class LegacyStartupResources
{
    // 0x00520B20 draws runtime bank 17 offsets 0..3. Pharaoh_Unloaded bitmap
    // records identify the corresponding contiguous Load1..Load4 images.
    public const string LoadingSpriteArchiveName = "Pharaoh_Unloaded.sg3";
    public const int LoadingSpriteBaseIndex = 509;

    // FUN_00520AF0 draws the final full-screen startup image after Loading Settings.
    // Expansion.sg3 bitmap record 18 names that 640x480 image bag_logo.bmp.
    public const string ExpansionLogoArchiveName = "Expansion.sg3";
    public const int ExpansionLogoImageIndex = 900;

    public static IReadOnlyList<string> ResourcePackNames { get; } =
    [
        "Pharaoh_Unloaded", "SprMain", "Hyksos", "Pharaoh_General", "Pharaoh_Terrain",
        "SPHINX1A", "SPHINX1B", "SPHINX1C", "SPHINX1D", "SPHINX_EXTRA",
        "TEMPLE_NILE", "empire", "SprAmbient", "Pharaoh_Fonts", "Egyptian", "SprMain2",
        "expansion", "PyramidDummy", "CaesareumDummy", "LtHouseDummy", "LibMainDummy",
        "LibMainDummy", "LibMainDummy", "LibExtra", "MausCarryoverDummy", "TombThutDummy",
        "AbuSimbelDummy", "TombTutDummy", "TombSetiDummy", "TombRamDummy"
    ];

    public static IReadOnlyList<LegacyLoadingCheckpoint> LoadingCheckpoints { get; } =
    [
        new(0, 11, 7),
        new(1, 11, 8),
        new(2, 11, 9),
        new(3, 11, 10),
        new(3, 11, 11)
    ];

    // FUN_00414420 has already registered slot zero when checkpoint zero is
    // first presented.  These are the total consumed 30-slot entries after
    // the work hidden by each painted checkpoint: +1, +1, +5, remainder, +0.
    public static IReadOnlyList<int> GraphicsPackSlotsAfterCheckpoint { get; } =
        [2, 3, 8, 30, 30];

    public static IReadOnlyList<string> StartupDataFiles { get; } =
    [
        "Pharaoh_Text.eng", "Pharaoh_MM.eng", "Pharaoh.inf", "Pharaoh.ini",
        "campaign.txt", "EVENTMSG.TXT", "music.txt", "trade_recommends.txt",
        "auto reason phrases.txt"
    ];

    public static IReadOnlyList<string> GetRuntimeDataFiles(int difficulty)
    {
        LegacyDifficultyFiles modelFiles = GetDifficultyFiles(difficulty);
        return StartupDataFiles.Concat(
            [modelFiles.GameModel, modelFiles.WalkerModel, modelFiles.TaxSentimentModel]).ToArray();
    }

    private static readonly string[] DifficultySuffixes =
        ["VeryEasy", "Easy", "Normal", "Hard", "Impossible"];

    public static LegacyDifficultyFiles GetDifficultyFiles(int difficulty)
    {
        if ((uint)difficulty >= DifficultySuffixes.Length)
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        string suffix = DifficultySuffixes[difficulty];
        return new LegacyDifficultyFiles(
            $"Pharaoh_Model_{suffix}.txt",
            $"Figure_model_{suffix}.txt",
            $"Tax_Sentiment_Model_{suffix}.txt");
    }

    public static int ComputePixelBufferBytes(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return Math.Max(checked(width * height * 2), 1_200_000);
    }

    public static IReadOnlyList<string> FindAvailableGraphicsArchives(string installationRoot)
        => InspectGraphicsPacks(installationRoot)
            .Where(pack => pack.Sg3Path is not null)
            .Select(pack => pack.Sg3Path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    [OriginalAddress(0x005394F0, "FUN_005394f0")]
    public static IReadOnlyList<LegacyGraphicsPack> InspectGraphicsPacks(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        string dataDirectory = Path.Combine(Path.GetFullPath(installationRoot), "Data");
        if (!Directory.Exists(dataDirectory)) return [];

        Dictionary<string, string> installed = Directory.EnumerateFiles(dataDirectory)
            .ToDictionary(path => Path.GetFileName(path)!, Path.GetFullPath,
                StringComparer.OrdinalIgnoreCase);
        return ResourcePackNames
            .Select((name, slot) => new LegacyGraphicsPack(
                slot,
                name,
                installed.GetValueOrDefault($"{name}.sg3"),
                installed.GetValueOrDefault($"{name}.555")))
            .ToArray();
    }
}
