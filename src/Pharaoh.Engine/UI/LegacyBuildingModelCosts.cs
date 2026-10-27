using System.Globalization;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

/// <summary>
/// Building costs loaded from the same difficulty-specific model tables as
/// Pharaoh.exe. The first value inside each BUILDINGS record is its cost.
/// </summary>
public sealed class LegacyBuildingModelCosts
{
    private readonly IReadOnlyDictionary<int, LegacyBuildingModel>[] modelsByDifficulty;

    private LegacyBuildingModelCosts(
        IReadOnlyDictionary<int, LegacyBuildingModel>[] modelsByDifficulty) =>
        this.modelsByDifficulty = modelsByDifficulty;

    [OriginalAddress(0x00540C90, "FUN_00540c90")]
    public static LegacyBuildingModelCosts Load(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        IReadOnlyDictionary<int, LegacyBuildingModel>[] models =
            new IReadOnlyDictionary<int, LegacyBuildingModel>[5];
        for (int difficulty = 0; difficulty < models.Length; difficulty++)
        {
            string fileName = LegacyStartupResources.GetDifficultyFiles(difficulty).GameModel;
            models[difficulty] = ParseFile(Path.Combine(installationRoot, fileName));
        }
        return new LegacyBuildingModelCosts(models);
    }

    public int GetCost(int difficulty, int buildingType)
    {
        return GetModel(difficulty, buildingType)?.Cost ?? 0;
    }

    public int GetCost(int difficulty, BuildingType type) => GetCost(difficulty, (int)type);

    public (int Fire, int Damage) GetRiskIncrements(int difficulty, BuildingType type)
    {
        LegacyBuildingModel? model = GetModel(difficulty, (int)type);
        return model is null ? default : (model.FireRiskIncrement, model.DamageRiskIncrement);
    }

    public LegacyBuildingModel? GetModel(int difficulty, int buildingType)
    {
        if ((uint)difficulty >= (uint)modelsByDifficulty.Length)
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        return modelsByDifficulty[difficulty].GetValueOrDefault(buildingType);
    }

    public LegacyBuildingModel? GetModel(int difficulty, BuildingType type) =>
        GetModel(difficulty, (int)type);

    [OriginalAddress(0x00540C90, "FUN_00540c90")]
    internal static IReadOnlyDictionary<int, LegacyBuildingModel> ParseFile(string path)
    {
        Dictionary<int, LegacyBuildingModel> models = [];
        foreach (string line in File.ReadLines(path))
        {
            int firstComma = line.IndexOf(',');
            int values = line.IndexOf(",{,", StringComparison.Ordinal);
            if (firstComma <= 0 || values < firstComma)
                continue;

            int costStart = values + 3;
            int valuesEnd = line.IndexOf("},", costStart, StringComparison.Ordinal);
            if (valuesEnd <= costStart ||
                !int.TryParse(line.AsSpan(0, firstComma), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int buildingType))
            {
                continue;
            }
            int[] fields = line.AsSpan(costStart, valuesEnd - costStart).ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int parsed) ? parsed : int.MinValue)
                .ToArray();
            if (fields.Length < 10 || fields.Take(10).Any(value => value == int.MinValue))
                continue;
            models[buildingType] = new LegacyBuildingModel(
                buildingType, fields[0], fields[1], fields[2], fields[3], fields[4],
                fields[5], fields[6], fields[7], fields[8], fields[9]);
        }
        if (models.Count != 237)
            throw new InvalidDataException(
                $"Expected 237 building model rows in '{path}', found {models.Count}.");
        return models;
    }
}

/// <summary>The ten integers loaded per BUILDINGS row by Pharaoh.exe.</summary>
public sealed record LegacyBuildingModel(
    int Type,
    int Cost,
    int InitialDesirability,
    int DesirabilityStep,
    int DesirabilityStepSize,
    int MaximumDesirabilityRange,
    int Employees,
    int FireRiskIncrement,
    int DamageRiskIncrement,
    int Reserved8,
    int Reserved9);
