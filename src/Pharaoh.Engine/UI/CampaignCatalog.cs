using System.Collections.ObjectModel;

namespace Pharaoh.Engine.UI;

public sealed record CampaignMission(
    int MissionId,
    int BriefingMessageId,
    int VictoryTextIndex,
    int PathId,
    IReadOnlyList<int> MergePathIds);

public sealed record CampaignChoice(int PathId, int X, int Y, int TextIndex);

public sealed record CampaignChoiceScreen(
    int GraphicId,
    int TitleTextIndex,
    int BeforeMissionIndex,
    IReadOnlyList<CampaignChoice> Choices);

public sealed record CampaignChapter(
    string Key,
    IReadOnlyList<CampaignMission> Missions,
    IReadOnlyList<CampaignChoiceScreen> ChoiceScreens)
{
    public CampaignChoiceScreen? InitialChoiceScreen => ChoiceScreens.FirstOrDefault(screen => screen.BeforeMissionIndex == 0);

    public CampaignMission? FirstMissionForPath(int pathId, int fromMissionIndex = 0) =>
        Missions.Skip(fromMissionIndex).FirstOrDefault(mission => mission.PathId == 0 || mission.PathId == pathId);
}

/// <summary>Original campaign.txt structure used by the 0x34/0x35 front-end states.</summary>
public sealed class CampaignCatalog
{
    private CampaignCatalog(IReadOnlyList<string> missionNames, IReadOnlyList<CampaignChapter> chapters)
    {
        MissionNames = missionNames;
        Chapters = chapters;
    }

    public IReadOnlyList<string> MissionNames { get; }
    public IReadOnlyList<CampaignChapter> Chapters { get; }

    public static CampaignCatalog Load(string installationRoot) =>
        Parse(File.ReadLines(Path.Combine(installationRoot, "campaign.txt")));

    public static CampaignCatalog Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<string> missionNames = [];
        List<ChapterBuilder> chapters = [];
        ChapterBuilder? chapter = null;
        ChoiceScreenBuilder? choiceScreen = null;
        bool readingNames = false;

        foreach (string source in lines)
        {
            string line = source.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                readingNames = section.Equals("MISSION_NAMES", StringComparison.OrdinalIgnoreCase);
                chapter = null;
                choiceScreen = null;
                if (!readingNames)
                {
                    chapter = new ChapterBuilder(section);
                    chapters.Add(chapter);
                }
                continue;
            }

            if (readingNames)
            {
                missionNames.Add(line);
                continue;
            }
            if (chapter is null) continue;

            int separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string command = line[..separator].Trim();
            int[] values = ParseValues(line[(separator + 1)..]);
            if (command.Equals("mission", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length < 3)
                    throw new InvalidDataException($"Invalid campaign mission line '{source}'.");
                chapter.Missions.Add(new CampaignMission(
                    values[0], values[1], values[2], values.Length > 3 ? values[3] : 0,
                    Array.AsReadOnly(values.Length > 4 ? values[4..] : [])));
                choiceScreen = null;
            }
            else if (command.Equals("choicescreen", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length != 2)
                    throw new InvalidDataException($"Invalid campaign choicescreen line '{source}'.");
                choiceScreen = new ChoiceScreenBuilder(values[0], values[1], chapter.Missions.Count);
                chapter.ChoiceScreens.Add(choiceScreen);
            }
            else if (command.Equals("choice", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length != 4 || choiceScreen is null)
                    throw new InvalidDataException($"Invalid campaign choice line '{source}'.");
                choiceScreen.Choices.Add(new CampaignChoice(values[0], values[1], values[2], values[3]));
            }
        }

        IReadOnlyList<CampaignChapter> result = chapters.Select(item => item.Build()).ToArray();
        return new CampaignCatalog(
            new ReadOnlyCollection<string>(missionNames),
            new ReadOnlyCollection<CampaignChapter>(result.ToList()));
    }

    private static int[] ParseValues(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        int[] result = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], out result[index]))
                throw new InvalidDataException($"Invalid campaign number '{parts[index]}'.");
        return result;
    }

    private sealed class ChapterBuilder(string key)
    {
        public string Key { get; } = key;
        public List<CampaignMission> Missions { get; } = [];
        public List<ChoiceScreenBuilder> ChoiceScreens { get; } = [];

        public CampaignChapter Build() => new(
            Key,
            new ReadOnlyCollection<CampaignMission>(Missions),
            new ReadOnlyCollection<CampaignChoiceScreen>(ChoiceScreens.Select(screen => screen.Build()).ToList()));
    }

    private sealed class ChoiceScreenBuilder(int graphicId, int titleTextIndex, int beforeMissionIndex)
    {
        public int GraphicId { get; } = graphicId;
        public int TitleTextIndex { get; } = titleTextIndex;
        public int BeforeMissionIndex { get; } = beforeMissionIndex;
        public List<CampaignChoice> Choices { get; } = [];

        public CampaignChoiceScreen Build() => new(
            GraphicId, TitleTextIndex, BeforeMissionIndex, new ReadOnlyCollection<CampaignChoice>(Choices));
    }
}
