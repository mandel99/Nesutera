using System.Collections.ObjectModel;
using System.Globalization;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Installation;

public sealed record LegacyFigureModel(
    int Id, string Name, int UnitClass, int HitPoints, int Attack, int Armor,
    int MissileArmor, int MissileAttack, int MissileRange, int MissileRate,
    int Speed, int Frequency, int Value10, int Value11, int Value12, int Value13);

public sealed class LegacyFigureModels
{
    private readonly IReadOnlyDictionary<int, LegacyFigureModel>[] figuresByDifficulty;
    private readonly IReadOnlyList<LegacyFigureModel>[] enemiesByDifficulty;

    private LegacyFigureModels(
        IReadOnlyDictionary<int, LegacyFigureModel>[] figuresByDifficulty,
        IReadOnlyList<LegacyFigureModel>[] enemiesByDifficulty)
    {
        this.figuresByDifficulty = figuresByDifficulty;
        this.enemiesByDifficulty = enemiesByDifficulty;
    }

    [OriginalAddress(0x00540ED0, "FUN_00540ed0")]
    public static LegacyFigureModels Load(string installationRoot)
    {
        IReadOnlyDictionary<int, LegacyFigureModel>[] figures =
            new IReadOnlyDictionary<int, LegacyFigureModel>[5];
        IReadOnlyList<LegacyFigureModel>[] enemies = new IReadOnlyList<LegacyFigureModel>[5];
        for (int difficulty = 0; difficulty < figures.Length; difficulty++)
        {
            string path = Path.Combine(installationRoot,
                LegacyStartupResources.GetDifficultyFiles(difficulty).WalkerModel);
            (figures[difficulty], enemies[difficulty]) = Parse(path);
        }
        return new LegacyFigureModels(figures, enemies);
    }

    public LegacyFigureModel? GetFigure(int difficulty, int id)
    {
        ValidateDifficulty(difficulty);
        return figuresByDifficulty[difficulty].GetValueOrDefault(id);
    }

    public IReadOnlyList<LegacyFigureModel> GetEnemies(int difficulty)
    {
        ValidateDifficulty(difficulty);
        return enemiesByDifficulty[difficulty];
    }

    private void ValidateDifficulty(int difficulty)
    {
        if ((uint)difficulty >= (uint)figuresByDifficulty.Length)
            throw new ArgumentOutOfRangeException(nameof(difficulty));
    }

    [OriginalAddress(0x00540ED0, "FUN_00540ed0")]
    internal static (IReadOnlyDictionary<int, LegacyFigureModel> Figures,
        IReadOnlyList<LegacyFigureModel> Enemies) Parse(string path)
    {
        Dictionary<int, LegacyFigureModel> figures = [];
        List<LegacyFigureModel> enemies = [];
        string section = string.Empty;
        foreach (string source in File.ReadLines(path))
        {
            string line = source.Trim();
            if (line.Equals("ALL FIGURES", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("ALL ENEMIES", StringComparison.OrdinalIgnoreCase))
            {
                section = line;
                continue;
            }
            if (!LegacyBraceTable.TryParse(line, 14, out int id, out string name, out int[] values))
                continue;
            LegacyFigureModel model = new(id, name, values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7], values[8], values[9], values[10],
                values[11], values[12], values[13]);
            if (section.Equals("ALL FIGURES", StringComparison.OrdinalIgnoreCase))
                figures[id] = model;
            else if (section.Equals("ALL ENEMIES", StringComparison.OrdinalIgnoreCase))
                enemies.Add(model);
        }
        if (figures.Count != 110 || enemies.Count != 60)
            throw new InvalidDataException(
                $"Expected 110 figure and 60 enemy rows in '{path}', found {figures.Count} and {enemies.Count}.");
        return (new ReadOnlyDictionary<int, LegacyFigureModel>(figures),
            new ReadOnlyCollection<LegacyFigureModel>(enemies));
    }
}

public sealed class LegacyTaxSentimentModels
{
    private readonly IReadOnlyList<IReadOnlyList<int>>[] tablesByDifficulty;

    private LegacyTaxSentimentModels(IReadOnlyList<IReadOnlyList<int>>[] tablesByDifficulty) =>
        this.tablesByDifficulty = tablesByDifficulty;

    [OriginalAddress(0x00541160, "FUN_00541160")]
    public static LegacyTaxSentimentModels Load(string installationRoot)
    {
        IReadOnlyList<IReadOnlyList<int>>[] tables = new IReadOnlyList<IReadOnlyList<int>>[5];
        for (int difficulty = 0; difficulty < tables.Length; difficulty++)
        {
            string path = Path.Combine(installationRoot,
                LegacyStartupResources.GetDifficultyFiles(difficulty).TaxSentimentModel);
            tables[difficulty] = Parse(path);
        }
        return new LegacyTaxSentimentModels(tables);
    }

    public int GetAdjustment(int difficulty, int taxRate, int coverageBand)
    {
        if ((uint)difficulty >= (uint)tablesByDifficulty.Length)
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        return tablesByDifficulty[difficulty][Math.Clamp(taxRate, 0, 25)]
            [Math.Clamp(coverageBand, 0, 10)];
    }

    [OriginalAddress(0x00541160, "FUN_00541160")]
    internal static IReadOnlyList<IReadOnlyList<int>> Parse(string path)
    {
        List<IReadOnlyList<int>> rows = [];
        foreach (string line in File.ReadLines(path))
            if (LegacyBraceTable.TryParseValues(line, 11, out int[] values))
                rows.Add(Array.AsReadOnly(values));
        if (rows.Count != 26)
            throw new InvalidDataException($"Expected 26 tax rows in '{path}', found {rows.Count}.");
        return new ReadOnlyCollection<IReadOnlyList<int>>(rows);
    }
}

public sealed record LegacyTradeRecommendation(int MinimumPopulation, int? MaximumPopulation,
    IReadOnlyList<int> CommodityAmounts);

public sealed class LegacyTradeRecommendations
{
    private LegacyTradeRecommendations(IReadOnlyList<LegacyTradeRecommendation> bands) => Bands = bands;
    public IReadOnlyList<LegacyTradeRecommendation> Bands { get; }

    [OriginalAddress(0x00541310, "FUN_00541310")]
    public static LegacyTradeRecommendations Load(string installationRoot)
    {
        string path = Path.Combine(installationRoot, "trade_recommends.txt");
        List<LegacyTradeRecommendation> rows = [];
        foreach (string source in File.ReadLines(path))
        {
            if (!LegacyBraceTable.TryParseValues(source, 6, out int[] values)) continue;
            string band = source[..source.IndexOf(',')].Trim();
            int minimum;
            int? maximum;
            if (band.EndsWith('+'))
            {
                minimum = int.Parse(band[..^1], CultureInfo.InvariantCulture);
                maximum = null;
            }
            else
            {
                string[] limits = band.Split('-', StringSplitOptions.TrimEntries);
                minimum = int.Parse(limits[0], CultureInfo.InvariantCulture);
                maximum = int.Parse(limits[1], CultureInfo.InvariantCulture);
            }
            rows.Add(new LegacyTradeRecommendation(minimum, maximum, Array.AsReadOnly(values)));
        }
        if (rows.Count != 4)
            throw new InvalidDataException($"Expected four trade recommendation rows in '{path}'.");
        return new LegacyTradeRecommendations(new ReadOnlyCollection<LegacyTradeRecommendation>(rows));
    }

    public LegacyTradeRecommendation ForPopulation(int population) =>
        Bands.First(row => population >= row.MinimumPopulation &&
            (row.MaximumPopulation is null || population <= row.MaximumPopulation));
}

public sealed class LegacyAutoReasonTable
{
    private LegacyAutoReasonTable(IReadOnlyList<IReadOnlyList<byte>> rows) => Rows = rows;
    public IReadOnlyList<IReadOnlyList<byte>> Rows { get; }

    [OriginalAddress(0x0044EFA0, "FUN_0044efa0")]
    public static LegacyAutoReasonTable Load(string installationRoot)
    {
        string path = Path.Combine(installationRoot, "auto reason phrases.txt");
        List<IReadOnlyList<byte>> rows = [];
        foreach (string line in File.ReadLines(path))
        {
            if (!LegacyBraceTable.TryParseValues(line, 36, out int[] values)) continue;
            rows.Add(Array.AsReadOnly(values.Select(value => checked((byte)value)).ToArray()));
        }
        if (rows.Count != 54)
            throw new InvalidDataException($"Expected 54 reason rows in '{path}', found {rows.Count}.");
        return new LegacyAutoReasonTable(new ReadOnlyCollection<IReadOnlyList<byte>>(rows));
    }

    public byte GetReasonClass(int trigger, int nextEvent) => Rows[trigger][nextEvent];
}

public sealed class LegacyEventMessages
{
    private LegacyEventMessages(IReadOnlyDictionary<string, string> phrases) => Phrases = phrases;
    public IReadOnlyDictionary<string, string> Phrases { get; }

    [OriginalAddress(0x004482F0, "FUN_004482f0")]
    public static LegacyEventMessages Load(string installationRoot)
    {
        string path = Path.Combine(installationRoot, "EVENTMSG.TXT");
        Dictionary<string, string> phrases = new(StringComparer.OrdinalIgnoreCase);
        foreach (string source in File.ReadLines(path))
        {
            string line = source.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            int firstQuote = line.IndexOf('"');
            int lastQuote = line.LastIndexOf('"');
            if (firstQuote <= 0 || lastQuote <= firstQuote) continue;
            string key = line[..firstQuote].Trim();
            phrases[key] = line[(firstQuote + 1)..lastQuote];
        }
        if (phrases.Count == 0)
            throw new InvalidDataException($"No event phrases were found in '{path}'.");
        return new LegacyEventMessages(new ReadOnlyDictionary<string, string>(phrases));
    }

    public string? Get(string key) => Phrases.GetValueOrDefault(key);
}

public sealed record LegacyMusicTrack(
    string FileName, string Mode, int Mission, int PostTuneDelay,
    int MinimumPopulation, int MaximumPopulation, bool Ambient);

public sealed class LegacyMusicCatalog
{
    private LegacyMusicCatalog(IReadOnlyList<LegacyMusicTrack> tracks) => Tracks = tracks;
    public IReadOnlyList<LegacyMusicTrack> Tracks { get; }

    [OriginalAddress(0x00539250, "FUN_00539250")]
    public static LegacyMusicCatalog Load(string installationRoot)
    {
        string path = Path.Combine(installationRoot, "music.txt");
        List<LegacyMusicTrack> tracks = [];
        foreach (string source in File.ReadLines(path))
        {
            string line = source.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase))
            {
                if (tracks.Count > 0) tracks[^1] = tracks[^1] with { Ambient = true };
                continue;
            }
            string[] fields = line.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 6) continue;
            tracks.Add(new LegacyMusicTrack(fields[0], fields[1],
                ParseInt(fields[2]), ParseInt(fields[3]), ParseInt(fields[4]), ParseInt(fields[5]), false));
        }
        if (tracks.Count == 0)
            throw new InvalidDataException($"No music records were found in '{path}'.");
        return new LegacyMusicCatalog(new ReadOnlyCollection<LegacyMusicTrack>(tracks));
    }

    public IReadOnlyList<LegacyMusicTrack> Select(int mission, int population, string mode) =>
        Tracks.Where(track => track.Mission == mission &&
            population >= track.MinimumPopulation && population <= track.MaximumPopulation &&
            track.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
}

/// <summary>All runtime .txt inputs opened by Pharaoh.exe, excluding campaign.txt.</summary>
public sealed record LegacyRuntimeTextData(
    LegacyFigureModels Figures,
    LegacyTaxSentimentModels TaxSentiment,
    LegacyTradeRecommendations TradeRecommendations,
    LegacyAutoReasonTable AutoReasons,
    LegacyEventMessages EventMessages,
    LegacyMusicCatalog Music)
{
    public static LegacyRuntimeTextData Load(string installationRoot) => new(
        LegacyFigureModels.Load(installationRoot),
        LegacyTaxSentimentModels.Load(installationRoot),
        LegacyTradeRecommendations.Load(installationRoot),
        LegacyAutoReasonTable.Load(installationRoot),
        LegacyEventMessages.Load(installationRoot),
        LegacyMusicCatalog.Load(installationRoot));
}

internal static class LegacyBraceTable
{
    public static bool TryParse(string line, int fieldCount, out int id, out string name, out int[] values)
    {
        id = 0;
        name = string.Empty;
        values = [];
        int firstComma = line.IndexOf(',');
        int marker = line.IndexOf("{,", StringComparison.Ordinal);
        if (firstComma <= 0 || marker <= firstComma ||
            !int.TryParse(line.AsSpan(0, firstComma), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out id) ||
            !TryParseValues(line, fieldCount, out values))
            return false;
        name = line[(firstComma + 1)..marker].Trim().TrimEnd(',');
        return true;
    }

    public static bool TryParseValues(string line, int fieldCount, out int[] values)
    {
        values = [];
        int marker = line.IndexOf("{,", StringComparison.Ordinal);
        if (marker < 0) return false;
        int end = line.IndexOf("},", marker + 2, StringComparison.Ordinal);
        if (end < 0) end = line.IndexOf('}', marker + 2);
        if (end < 0) return false;
        string[] fields = line[(marker + 2)..end]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < fieldCount) return false;
        int[] parsed = new int[fieldCount];
        for (int index = 0; index < fieldCount; index++)
            if (!int.TryParse(fields[index], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed[index]))
                return false;
        values = parsed;
        return true;
    }
}
