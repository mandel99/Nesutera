using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum LegacyCityMenuAction
{
    NewGame,
    ReplayMission,
    LoadGame,
    SaveGame,
    DeleteGame,
    ExitGame,
    DisplaySettings,
    SoundSettings,
    SpeedSettings,
    PopupMessages,
    ToggleCityNames,
    Difficulty,
    ToggleAutosave,
    TogglePyramidSpeedup,
    Help,
    CycleMouseHelp,
    ToggleWarnings,
    About,
    OpenOverseer
}

public enum LegacyCitySettingsPage
{
    None,
    Display,
    Sound,
    Speed,
    PopupMessages,
    Difficulty
}

public readonly record struct LegacyCityMenuItem(
    LegacyCityMenuAction Action,
    int TextIndex,
    uint CallbackAddress,
    int Argument = 0);

public readonly record struct LegacyCityMenuDefinition(int TextGroup, IReadOnlyList<LegacyCityMenuItem> Items);

/// <summary>Exact top-menu order and callback table projected from Pharaoh.exe 2.1.</summary>
[RewriteOnly("Typed projection of the original menu tables at 005C8F30 through 005C91F0")]
public static class LegacyCityTopMenu
{
    public static IReadOnlyList<LegacyCityMenuDefinition> Menus { get; } =
    [
        new(1,
        [
            new(LegacyCityMenuAction.NewGame, 1, 0x004023A0),
            new(LegacyCityMenuAction.ReplayMission, 2, 0x00418510),
            new(LegacyCityMenuAction.LoadGame, 3, 0x00402450),
            new(LegacyCityMenuAction.SaveGame, 4, 0x00402540),
            new(LegacyCityMenuAction.DeleteGame, 6, 0x00402630),
            new(LegacyCityMenuAction.ExitGame, 5, 0x00402B90)
        ]),
        new(2,
        [
            new(LegacyCityMenuAction.DisplaySettings, 1, 0x00402BC0),
            new(LegacyCityMenuAction.SoundSettings, 2, 0x00402EE0),
            new(LegacyCityMenuAction.SpeedSettings, 3, 0x004032C0),
            new(LegacyCityMenuAction.PopupMessages, 11, 0x00402BE0),
            new(LegacyCityMenuAction.ToggleCityNames, 7, 0x0040C7D0),
            new(LegacyCityMenuAction.Difficulty, 6, 0x00402E30),
            new(LegacyCityMenuAction.ToggleAutosave, 9, 0x00403500),
            new(LegacyCityMenuAction.TogglePyramidSpeedup, 4, 0x00403540)
        ]),
        new(3,
        [
            new(LegacyCityMenuAction.Help, 1, 0x004033F0),
            new(LegacyCityMenuAction.CycleMouseHelp, 2, 0x00403410),
            new(LegacyCityMenuAction.ToggleWarnings, 5, 0x00403490),
            new(LegacyCityMenuAction.About, 7, 0x00403400)
        ]),
        new(4, Enumerable.Range(1, 13)
            .Select((textIndex, index) => new LegacyCityMenuItem(
                LegacyCityMenuAction.OpenOverseer,
                textIndex,
                0x0040A0E0,
                index >= 2 ? index + 2 : index + 1))
            .ToArray())
    ];

    [OriginalAddress(0x00425330, "FUN_00425330")]
    public static LegacyCityMenuDefinition GetMenu(int index) => Menus[index];

    [RewriteOnly("Resolves original self-modifying menu text indices from runtime option state")]
    public static int ResolveTextIndex(LegacyCityMenuItem item, LegacyCityOptions options) => item.Action switch
    {
        LegacyCityMenuAction.ToggleCityNames => options.EgyptianCityNames ? 7 : 8,
        LegacyCityMenuAction.ToggleAutosave => options.Autosave ? 9 : 10,
        LegacyCityMenuAction.TogglePyramidSpeedup => options.PyramidSpeedup ? 5 : 4,
        LegacyCityMenuAction.CycleMouseHelp => options.MouseHelp switch { 0 => 2, 1 => 3, _ => 4 },
        LegacyCityMenuAction.ToggleWarnings => options.Warnings ? 6 : 5,
        _ => item.TextIndex
    };
}

/// <summary>Runtime values controlled by the original Options and Help menus.</summary>
[RewriteOnly("Managed storage for option globals used by the reconstructed UI")]
public sealed class LegacyCityOptions
{
    public int DisplayWidth { get; set; } = 1024;
    public int DisplayHeight { get; set; } = 768;
    public bool FullScreen { get; set; }
    public bool MusicEnabled { get; set; } = true;
    public bool SpeechEnabled { get; set; } = true;
    public bool EffectsEnabled { get; set; } = true;
    public bool CitySoundsEnabled { get; set; } = true;
    public int MusicVolume { get; set; } = 100;
    public int SpeechVolume { get; set; } = 100;
    public int EffectsVolume { get; set; } = 100;
    public int CityVolume { get; set; } = 100;
    public int GameSpeed { get; set; } = 50;
    public int ScrollSpeed { get; set; } = 50;
    public int Difficulty { get; set; } = 2;
    public int PopupMessageMask { get; set; } = 0x1FFE;
    public bool GodsEffects { get; set; } = true;
    public bool EgyptianCityNames { get; set; } = true;
    public bool Autosave { get; set; } = true;
    public bool PyramidSpeedup { get; set; }
    public int MouseHelp { get; set; } = 2;
    public bool Warnings { get; set; } = true;
}
