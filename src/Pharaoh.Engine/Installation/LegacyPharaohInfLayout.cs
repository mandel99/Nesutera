using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Installation;

public enum LegacyPharaohInfFieldKind
{
    UserSetting,
    CampaignState,
    RuntimeState,
    SystemValue,
    Reserved
}

public readonly record struct LegacyPharaohInfField(
    int Offset,
    int Length,
    string Name,
    LegacyPharaohInfFieldKind Kind,
    string Description);

/// <summary>Complete byte layout of the original 564-byte Pharaoh.inf image.</summary>
[RewriteOnly("Names the verified fields and explicitly bounds every unresolved byte range in DAT_00e38e58")]
public static class LegacyPharaohInfLayout
{
    public static IReadOnlyList<LegacyPharaohInfField> Fields { get; } =
    [
        Field(0x000, 4, "FormatVersion", LegacyPharaohInfFieldKind.SystemValue, "Binary structure version; Pharaoh 2.1 uses 0x10."),
        Field(0x004, 4, "Windowed", LegacyPharaohInfFieldKind.UserSetting, "Zero selects full screen; a nonzero value selects windowed mode."),
        Field(0x008, 1, "InteractionSuppressed", LegacyPharaohInfFieldKind.RuntimeState, "Transient flag that gates several city input paths; reset after loading."),
        Field(0x009, 1, "Reserved009", LegacyPharaohInfFieldKind.Reserved, "Initialized to zero; no read reference is present in Pharaoh 2.1."),
        Field(0x00A, 1, "DataDriveLetter", LegacyPharaohInfFieldKind.SystemValue, "ASCII drive letter used to build fallback CD and data paths."),
        Field(0x00B, 1, "EffectsEnabled", LegacyPharaohInfFieldKind.UserSetting, "Sound-effects enable flag."),
        Field(0x00C, 1, "MusicEnabled", LegacyPharaohInfFieldKind.UserSetting, "Music enable flag."),
        Field(0x00D, 1, "SpeechEnabled", LegacyPharaohInfFieldKind.UserSetting, "Speech enable flag."),
        Field(0x00E, 2, "Reserved00E", LegacyPharaohInfFieldKind.Reserved, "Padding or dormant legacy bytes with no direct references."),
        Field(0x010, 4, "DisplayMode", LegacyPharaohInfFieldKind.UserSetting, "Display mode: 1 is 640x480, 2 is 800x600, and 3 is 1024x768."),
        Field(0x014, 4, "GameSpeed", LegacyPharaohInfFieldKind.UserSetting, "Game speed value used by the speed dialog."),
        Field(0x018, 4, "ScrollSpeed", LegacyPharaohInfFieldKind.UserSetting, "Map scroll speed used by the speed dialog."),
        Field(0x01C, 32, "PlayerName", LegacyPharaohInfFieldKind.CampaignState, "Zero-terminated player or family name reused by campaign and save paths."),
        Field(0x03C, 4, "BuildingDebugField", LegacyPharaohInfFieldKind.RuntimeState, "Internal selector for one of the building-debug information fields."),
        Field(0x040, 12, "Reserved040", LegacyPharaohInfFieldKind.Reserved, "Unreferenced bytes between the debug selector and overseer state."),
        Field(0x04C, 4, "SelectedOverseer", LegacyPharaohInfFieldKind.RuntimeState, "Index of the last selected overseer."),
        Field(0x050, 1, "MissionId", LegacyPharaohInfFieldKind.CampaignState, "Current mission ID loaded from the campaign record."),
        Field(0x051, 1, "CampaignSequence", LegacyPharaohInfFieldKind.CampaignState, "Campaign sequence or chapter index advanced after a completed mission."),
        Field(0x052, 1, "PreviousCampaignRecord", LegacyPharaohInfFieldKind.CampaignState, "Previous campaign-record index; 0xFF means no previous record."),
        Field(0x053, 1, "CurrentCampaignRecord", LegacyPharaohInfFieldKind.CampaignState, "Current campaign-record index returned by campaign lookup."),
        Field(0x054, 4, "MouseHelp", LegacyPharaohInfFieldKind.UserSetting, "Mouse-help mode from 0 through 2; live INF comparison confirmed this field."),
        Field(0x058, 4, "StartingKingdomRating", LegacyPharaohInfFieldKind.CampaignState, "Difficulty-dependent starting kingdom rating: 70, 60, 50, 50, or 40."),
        Field(0x05C, 4, "CampaignCarryOver", LegacyPharaohInfFieldKind.CampaignState, "Value mirrored to and from active-city state; its narrower gameplay meaning remains unresolved."),
        Field(0x060, 4, "ScenarioTransitionCount", LegacyPharaohInfFieldKind.CampaignState, "Counter reset at campaign initialization and incremented during scenario transitions."),
        Field(0x064, 4, "CustomScenario", LegacyPharaohInfFieldKind.CampaignState, "Nonzero for a custom or non-campaign scenario, bypassing campaign progression."),
        Field(0x068, 1, "CitySoundsEnabled", LegacyPharaohInfFieldKind.UserSetting, "City ambient-sounds enable flag."),
        Field(0x069, 1, "WarningsEnabled", LegacyPharaohInfFieldKind.UserSetting, "Warning-message enable flag."),
        Field(0x06A, 1, "PeriodicLastSave", LegacyPharaohInfFieldKind.RuntimeState, "When equal to one, permits the month-boundary last.sav write; no normal menu writer is known."),
        Field(0x06B, 1, "PyramidSpeedup", LegacyPharaohInfFieldKind.UserSetting, "Pyramid construction speed-up flag."),
        Field(0x06C, 4, "EffectsVolume", LegacyPharaohInfFieldKind.UserSetting, "Sound-effects volume from 0 through 100."),
        Field(0x070, 4, "MusicVolume", LegacyPharaohInfFieldKind.UserSetting, "Music volume from 0 through 100."),
        Field(0x074, 4, "SpeechVolume", LegacyPharaohInfFieldKind.UserSetting, "Speech volume from 0 through 100."),
        Field(0x078, 4, "CityVolume", LegacyPharaohInfFieldKind.UserSetting, "City ambient-sounds volume from 0 through 100."),
        Field(0x07C, 4, "DormantLegacyValue07C", LegacyPharaohInfFieldKind.Reserved, "Initialized to 2 but never read by Pharaoh 2.1."),
        Field(0x080, 4, "RamSetting", LegacyPharaohInfFieldKind.SystemValue, "RAM value parsed from Pharaoh.ini and refreshed after the INF load."),
        Field(0x084, 4, "CpuSetting", LegacyPharaohInfFieldKind.SystemValue, "CPU rating parsed from Pharaoh.ini and used for high- or low-quality movie selection."),
        Field(0x088, 4, "DormantLegacyValue088", LegacyPharaohInfFieldKind.Reserved, "Initialized to zero but never read by Pharaoh 2.1."),
        Field(0x08C, 4, "PreviousDisplayMode", LegacyPharaohInfFieldKind.RuntimeState, "Previous display-mode snapshot used to request a mode transition."),
        Field(0x090, 4, "Reserved090", LegacyPharaohInfFieldKind.Reserved, "Unnamed dword with no direct references."),
        Field(0x094, 4, "AutosaveDisabled", LegacyPharaohInfFieldKind.UserSetting, "Inverse autosave flag: zero means autosave is enabled."),
        Field(0x098, 4, "PopupMessageMask", LegacyPharaohInfFieldKind.UserSetting, "Popup-message bit mask; live INF comparison confirmed the twelve category bits."),
        Field(0x09C, 0x188, "Reserved09C", LegacyPharaohInfFieldKind.Reserved, "Opaque block with no named or direct field references; preserve byte for byte."),
        Field(0x224, 4, "VictoryMovieAlternator", LegacyPharaohInfFieldKind.RuntimeState, "Alternates the balcony and senate victory movies."),
        Field(0x228, 4, "Difficulty", LegacyPharaohInfFieldKind.UserSetting, "Difficulty index from 0 through 4."),
        Field(0x22C, 4, "GodsEffectsEnabled", LegacyPharaohInfFieldKind.UserSetting, "Enables or disables gods' gameplay effects."),
        Field(0x230, 1, "ClassicalCityNames", LegacyPharaohInfFieldKind.UserSetting, "Zero selects Egyptian names; a nonzero value selects Classical names."),
        Field(0x231, 1, "CheckCd", LegacyPharaohInfFieldKind.SystemValue, "CheckCD value parsed from Pharaoh.ini after the INF load."),
        Field(0x232, 2, "TailPadding", LegacyPharaohInfFieldKind.Reserved, "Trailing padding at the end of the 0x234-byte image.")
    ];

    [RewriteOnly("Constructs one immutable descriptor for the documented Pharaoh.inf byte layout")]
    private static LegacyPharaohInfField Field(
        int offset,
        int length,
        string name,
        LegacyPharaohInfFieldKind kind,
        string description) => new(offset, length, name, kind, description);
}
