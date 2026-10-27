using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum LegacyMenuState : byte
{
    MainMenu = 0x00,
    Gameplay = 0x01,
    TimedTransition = 0x04,
    LoadSave = 0x0D,
    AlternateLoadSave = 0x10,
    CampaignPeriods = 0x17,
    FamilyRegistry = 0x2F,
    FamilyHub = 0x30,
    ContextButtons = 0x33,
    CampaignMap = 0x34,
    CampaignBriefing = 0x35,
    FamilyName = 0x36,
    InterstitialA = 0x37,
    InterstitialB = 0x38,
    InterstitialC = 0x39,
    InterstitialEnd = 0x3A,
    StartupClickThrough = 0x3C,
    IndividualMissions = 0x3D,
    TemporaryOverlay = 0x3E,
    Unclassified3F = 0x3F,
    IntroMovie = 0x40,
    CreditsMovie = 0x41,
    ContextChoice = 0x43,
    LogoMovie = 0x44,
    ProfileTransition = 0x51,
    HighScores = 0x53,
    ModalDialog = 0x54,
    TemporaryMessage = 0x55,
    Unclassified57 = 0x57
}

public enum LegacyMenuEvent
{
    Startup,
    MediaCompleted,
    ClickOrBack,
    MainPlay,
    MainMissionEditor,
    MainHighScores,
    ProfilesInitialized,
    ProfileConfirmed,
    CreateProfile,
    HubResume,
    HubChooseMission,
    HubLoadGame,
    HubCustomMissions,
    HubReturn,
    OpenIndividualMissions,
    OpenCampaigns,
    PlayPeriod,
    PlayMission,
    Confirm,
    TimerElapsed,
    OpenModal,
    CloseModal
}

public readonly record struct LegacyMenuContext(
    int ProfileCount = 0,
    bool FamilyHistoryStarted = false,
    bool ReplayCampaignsAvailable = true,
    bool HasSavedGames = false,
    bool MissionReturnsToMainMenu = false,
    bool PeriodUsesCampaignMap = false,
    LegacyMenuState? TimedTarget = null);

public readonly record struct LegacyMenuTransition(
    LegacyMenuState Source,
    LegacyMenuState Target,
    LegacyMenuEvent Event,
    string Evidence,
    string? Condition = null);

/// <summary>
/// Front-end state machine reconstructed from the verified v17-v19 instruction paths.
/// Gameplay-only states deliberately remain outside this router.
/// </summary>
public sealed class LegacyMenuFlow
{
    public LegacyMenuFlow(LegacyMenuState initial = LegacyMenuState.LogoMovie) => Current = initial;

    public LegacyMenuState Current { get; private set; }
    public LegacyMenuState Previous { get; private set; }

    [OriginalAddress(0x004D8310, "FUN_004d8310")]
    public bool Dispatch(LegacyMenuEvent menuEvent, LegacyMenuContext context = default)
    {
        if (Current == LegacyMenuState.ModalDialog && menuEvent == LegacyMenuEvent.CloseModal)
        {
            RestorePrevious();
            return true;
        }
        LegacyMenuState? target = Resolve(Current, menuEvent, context);
        if (target is null) return false;
        Previous = Current;
        Current = target.Value;
        return true;
    }

    public void RestorePrevious() => (Current, Previous) = (Previous, Current);

    [OriginalAddress(0x004D71C0, "FUN_004d71c0")]
    [OriginalAddress(0x004CB800, "FUN_004cb800")]
    [OriginalAddress(0x004CB860, "FUN_004cb860")]
    [OriginalAddress(0x004CB910, "FUN_004cb910")]
    [OriginalAddress(0x004CB940, "FUN_004cb940")]
    [OriginalAddress(0x004CB970, "FUN_004cb970")]
    public static LegacyMenuState? Resolve(LegacyMenuState source, LegacyMenuEvent menuEvent, LegacyMenuContext context) =>
        (source, menuEvent) switch
        {
            (_, LegacyMenuEvent.Startup) => LegacyMenuState.LogoMovie,
            (LegacyMenuState.LogoMovie, LegacyMenuEvent.MediaCompleted) => LegacyMenuState.IntroMovie,
            (LegacyMenuState.IntroMovie, LegacyMenuEvent.MediaCompleted) => LegacyMenuState.CreditsMovie,
            (LegacyMenuState.CreditsMovie, LegacyMenuEvent.MediaCompleted) => LegacyMenuState.StartupClickThrough,
            (LegacyMenuState.StartupClickThrough, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.MainMenu,

            (LegacyMenuState.MainMenu, LegacyMenuEvent.MainPlay) => LegacyMenuState.FamilyRegistry,
            (LegacyMenuState.MainMenu, LegacyMenuEvent.MainMissionEditor) => LegacyMenuState.IndividualMissions,
            (LegacyMenuState.MainMenu, LegacyMenuEvent.MainHighScores) => LegacyMenuState.HighScores,
            (LegacyMenuState.HighScores, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.MainMenu,

            (LegacyMenuState.FamilyRegistry, LegacyMenuEvent.ProfilesInitialized) when context.ProfileCount == 0 => LegacyMenuState.FamilyName,
            (LegacyMenuState.FamilyRegistry, LegacyMenuEvent.ProfilesInitialized) => LegacyMenuState.FamilyRegistry,
            (LegacyMenuState.FamilyRegistry, LegacyMenuEvent.CreateProfile) => LegacyMenuState.FamilyName,
            (LegacyMenuState.FamilyRegistry, LegacyMenuEvent.ProfileConfirmed) => LegacyMenuState.FamilyHub,
            (LegacyMenuState.FamilyRegistry, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.MainMenu,
            (LegacyMenuState.ProfileTransition, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.FamilyName,

            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubResume) when context.FamilyHistoryStarted => LegacyMenuState.Gameplay,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubResume) => LegacyMenuState.CampaignPeriods,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubChooseMission) when context.ReplayCampaignsAvailable => LegacyMenuState.CampaignPeriods,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubChooseMission) => LegacyMenuState.IndividualMissions,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubLoadGame) when context.HasSavedGames => LegacyMenuState.LoadSave,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubCustomMissions) => LegacyMenuState.IndividualMissions,
            (LegacyMenuState.FamilyHub, LegacyMenuEvent.HubReturn or LegacyMenuEvent.ClickOrBack) => LegacyMenuState.FamilyRegistry,

            (LegacyMenuState.CampaignPeriods, LegacyMenuEvent.OpenIndividualMissions) => LegacyMenuState.IndividualMissions,
            (LegacyMenuState.CampaignPeriods, LegacyMenuEvent.PlayPeriod) when context.PeriodUsesCampaignMap => LegacyMenuState.CampaignMap,
            (LegacyMenuState.CampaignPeriods, LegacyMenuEvent.PlayPeriod) => LegacyMenuState.TimedTransition,
            (LegacyMenuState.CampaignPeriods, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.FamilyHub,
            (LegacyMenuState.IndividualMissions, LegacyMenuEvent.OpenCampaigns) => LegacyMenuState.CampaignPeriods,
            (LegacyMenuState.IndividualMissions, LegacyMenuEvent.PlayMission) => LegacyMenuState.Gameplay,
            (LegacyMenuState.IndividualMissions, LegacyMenuEvent.ClickOrBack) => context.MissionReturnsToMainMenu
                ? LegacyMenuState.MainMenu : LegacyMenuState.FamilyHub,

            (LegacyMenuState.TimedTransition, LegacyMenuEvent.TimerElapsed) => context.TimedTarget,
            (LegacyMenuState.CampaignMap, LegacyMenuEvent.Confirm) => LegacyMenuState.TimedTransition,
            (LegacyMenuState.CampaignBriefing, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.CampaignMap,
            (LegacyMenuState.CampaignBriefing, LegacyMenuEvent.Confirm) => LegacyMenuState.Gameplay,

            (LegacyMenuState.LoadSave, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.FamilyHub,
            (LegacyMenuState.InterstitialA, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.InterstitialB,
            // 0x38 and 0x39 set the quit flag; neither is a verified link to another interstitial.
            (LegacyMenuState.InterstitialEnd, LegacyMenuEvent.ClickOrBack) => LegacyMenuState.MainMenu,
            (_, LegacyMenuEvent.OpenModal) => LegacyMenuState.ModalDialog,
            _ => null
        };

    public static IReadOnlyList<LegacyMenuTransition> VerifiedTransitions { get; } =
    [
        new(LegacyMenuState.LogoMovie, LegacyMenuState.IntroMovie, LegacyMenuEvent.MediaCompleted, "004D84CA -> 00538DC0"),
        new(LegacyMenuState.IntroMovie, LegacyMenuState.CreditsMovie, LegacyMenuEvent.MediaCompleted, "004D8520 -> 00538DC0"),
        new(LegacyMenuState.CreditsMovie, LegacyMenuState.StartupClickThrough, LegacyMenuEvent.MediaCompleted, "004D8560 -> 00538DC0"),
        new(LegacyMenuState.MainMenu, LegacyMenuState.FamilyRegistry, LegacyMenuEvent.MainPlay, "00417FF0 -> 00516BB0"),
        new(LegacyMenuState.MainMenu, LegacyMenuState.IndividualMissions, LegacyMenuEvent.MainMissionEditor, "00417FF0 -> 0041F870"),
        new(LegacyMenuState.MainMenu, LegacyMenuState.HighScores, LegacyMenuEvent.MainHighScores, "00417FF0 -> 004C5920"),
        new(LegacyMenuState.FamilyRegistry, LegacyMenuState.FamilyHub, LegacyMenuEvent.ProfileConfirmed, "00516990 -> 004CB6A0"),
        new(LegacyMenuState.FamilyHub, LegacyMenuState.LoadSave, LegacyMenuEvent.HubLoadGame, "004CB910 -> 00402450", "saved-game flag != 0"),
        new(LegacyMenuState.CampaignPeriods, LegacyMenuState.IndividualMissions, LegacyMenuEvent.OpenIndividualMissions, "0041C310 -> 0041F870"),
        new(LegacyMenuState.IndividualMissions, LegacyMenuState.CampaignPeriods, LegacyMenuEvent.OpenCampaigns, "0041C350"),
        new(LegacyMenuState.HighScores, LegacyMenuState.MainMenu, LegacyMenuEvent.ClickOrBack, "004DD764"),
        new(LegacyMenuState.CampaignPeriods, LegacyMenuState.CampaignMap, LegacyMenuEvent.PlayPeriod, "0041BDDE-0041BDE9", "E38EA8 == 0x7F"),
        new(LegacyMenuState.CampaignPeriods, LegacyMenuState.TimedTransition, LegacyMenuEvent.PlayPeriod, "004181E0 -> 0041AA20", "target 0x35; 1000 ms"),
        new(LegacyMenuState.TimedTransition, LegacyMenuState.CampaignBriefing, LegacyMenuEvent.TimerElapsed, "0041AA20", "configured target 0x35"),
        new(LegacyMenuState.CampaignMap, LegacyMenuState.TimedTransition, LegacyMenuEvent.Confirm, "00418220", "selection remains valid"),
        new(LegacyMenuState.CampaignBriefing, LegacyMenuState.CampaignMap, LegacyMenuEvent.ClickOrBack, "00418350", "signed E38EAA >= 0"),
        new(LegacyMenuState.CampaignBriefing, LegacyMenuState.Gameplay, LegacyMenuEvent.Confirm, "00418301-00418342")
    ];
}
