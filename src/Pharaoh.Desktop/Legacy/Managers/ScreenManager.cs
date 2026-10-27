namespace Pharaoh.Desktop.Legacy.Managers;

internal enum DesktopScreen
{
    Loading,
    Intro,
    PeriodVideo,
    Title,
    MainMenu,
    Families,
    NameEntry,
    EgyptianNames,
    DeleteFamilyConfirmation,
    NoMissionsConfirmation,
    QuitConfirmation,
    FamilyHub,
    Periods,
    Maps,
    CustomMissions,
    MissionEditor,
    TimedTransition,
    CampaignMap,
    CampaignBriefing,
    HighScores,
    SaveBrowser,
    Notice,
    City
}

/// <summary>Owns navigation state shared by the legacy desktop screens.</summary>
internal sealed class ScreenManager
{
    public DesktopScreen Current { get; set; }
    public DesktopScreen NoticeReturn { get; set; } = DesktopScreen.MainMenu;
    public DesktopScreen QuitConfirmationReturn { get; set; } = DesktopScreen.MainMenu;
    public DesktopScreen? BriefingReturn { get; set; }
    public DesktopScreen FileMenuReturn { get; set; } = DesktopScreen.FamilyHub;
}
