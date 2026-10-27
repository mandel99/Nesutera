using System.Collections.ObjectModel;
using System.Globalization;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public sealed record LegacyHousingLevel(
    int Type,
    int DevolveDesirability,
    int EvolveDesirability,
    int Entertainment,
    int Water,
    int Religion,
    int Education,
    int Market,
    int Dentist,
    int Physician,
    int Health,
    int FoodTypes,
    int Pottery,
    int Linen,
    int Jewelry,
    int Beer,
    int CrimeRiskIncrement,
    int CrimeRiskBase,
    int Prosperity,
    int PopulationCapacity,
    int TaxRateMultiplier,
    int MalariaRiskIncrement,
    int DiseaseRiskIncrement);

/// <summary>
/// The 20 difficulty-specific house rows parsed by Pharaoh.exe FUN_00540C90.
/// Values are deliberately read from the installed game's model files rather
/// than duplicated in code, so custom model tuning remains effective.
/// </summary>
public sealed class LegacyHousingModels
{
    private readonly IReadOnlyList<LegacyHousingLevel>[] levelsByDifficulty;

    private LegacyHousingModels(IReadOnlyList<LegacyHousingLevel>[] levelsByDifficulty) =>
        this.levelsByDifficulty = levelsByDifficulty;

    [OriginalAddress(0x00540C90, "FUN_00540c90")]
    public static LegacyHousingModels Load(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        IReadOnlyList<LegacyHousingLevel>[] levels = new IReadOnlyList<LegacyHousingLevel>[5];
        for (int difficulty = 0; difficulty < levels.Length; difficulty++)
        {
            string fileName = LegacyStartupResources.GetDifficultyFiles(difficulty).GameModel;
            levels[difficulty] = ParseFile(Path.Combine(installationRoot, fileName));
        }
        return new LegacyHousingModels(levels);
    }

    public IReadOnlyList<LegacyHousingLevel> GetLevels(int difficulty)
    {
        if ((uint)difficulty >= (uint)levelsByDifficulty.Length)
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        return levelsByDifficulty[difficulty];
    }

    [OriginalAddress(0x00540C90, "FUN_00540c90")]
    internal static IReadOnlyList<LegacyHousingLevel> ParseFile(string path)
    {
        List<LegacyHousingLevel> result = [];
        bool houses = false;
        foreach (string line in File.ReadLines(path))
        {
            if (!houses)
            {
                houses = line.Trim().Equals("ALL HOUSES", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!line.StartsWith("House ", StringComparison.OrdinalIgnoreCase))
            {
                if (result.Count > 0)
                    break;
                continue;
            }

            int marker = line.IndexOf("{,", StringComparison.Ordinal);
            if (marker < 0)
                continue;
            string[] fields = line[(marker + 2)..].Split(',');
            if (fields.Length < 22)
                throw new InvalidDataException($"Housing model row is incomplete in '{path}'.");

            int[] values = new int[22];
            for (int index = 0; index < values.Length; index++)
            {
                if (!decimal.TryParse(fields[index].Trim(), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out decimal value))
                    throw new InvalidDataException($"Housing model value {index} is invalid in '{path}'.");
                values[index] = decimal.ToInt32(decimal.Truncate(value));
            }
            int type = (int)BuildingType.Housing + result.Count;
            result.Add(new LegacyHousingLevel(
                type, values[0], values[1], values[2], values[3], values[4], values[5],
                values[6], values[7], values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15], values[16], values[17],
                values[18], values[19], values[20], values[21]));
        }

        if (result.Count != 20)
            throw new InvalidDataException($"Expected 20 housing rows in '{path}', found {result.Count}.");
        return new ReadOnlyCollection<LegacyHousingLevel>(result);
    }
}
