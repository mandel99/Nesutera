using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using Pharaoh.Assets.Graphics;
using Pharaoh.Assets.Save;
using Pharaoh.Assets.Text;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Environment;
using Pharaoh.Engine.Housing;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Platform;
using Pharaoh.Engine.Profiles;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Walkers;
using Pharaoh.Engine.Tutorials;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Desktop.Legacy.UI;
using static Pharaoh.Desktop.Legacy.Managers.LayoutManager;

namespace Pharaoh.Desktop.Legacy;

internal enum MissionControl
{
    IndividualTab,
    CampaignTab,
    ScrollUp,
    ScrollDown,
    Exit,
    Play,
    Results,
    DifficultyUp,
    DifficultyDown
}

internal sealed partial class PharaohWindow : DesktopWindow
{
    private static readonly DisplayLayout BaseDisplay = DisplayLayout.FromLegacyMode(LegacyDisplayMode.Size640x480);
    private readonly string installationRoot;
    private readonly Encoding legacyEncoding;
    private readonly LocalizedTextCatalog text;
    private readonly MainMenuManager mainMenu = new(BaseDisplay);
    private readonly FamilySelection familySelection = new();
    private readonly MissionSelection missionSelection = new();
    private readonly MissionSelection customMissionSelection = new();
    private CampaignCatalog campaigns = null!;
    private MissionPakArchive missionArchive = null!;
    private MessageEngArchive messages = null!;
    private readonly LegacyFileMenu fileMenu = new();
    private List<FamilyProfile> profiles = null!;
    private IReadOnlyList<MissionItem> missions = null!;
    private IReadOnlyList<MissionItem> customMissions = null!;
    private readonly Sg3AssetCatalog graphicsAssets;
    private readonly StartupAssetLoadingManager startupAssetLoadingManager;
    private readonly BuildModeManager buildModeManager = new();
    private readonly MapManager mapManager;
    private readonly LoadingManager loadingManager = new();
    private readonly OptionManager optionManager;
    private readonly FileManager fileManager;
    private readonly UiManager uiManager = new();
    private OptionsPanel optionsPanel = null!;
    private readonly LoadingPanel loadingPanel;
    private readonly Dictionary<string, CustomMissionMetadata?> customMissionMetadata =
        new(StringComparer.OrdinalIgnoreCase);
    private Bitmap titleBackground = null!;
    private Bitmap menuBackground = null!;
    private readonly Bitmap[] loadingBackgrounds;
    private Bitmap expansionLogo = null!;
    private Bitmap familyBackground = null!;
    private Bitmap familyHubBackground = null!;
    private Bitmap missionBackground = null!;
    private Bitmap customMissionBackground = null!;
    private Bitmap briefingBackground = null!;
    private Bitmap campaignMapBackground = null!;
    private Bitmap[] campaignChoiceScreens = null!;
    private Bitmap[] missionThumbnails = null!;
    private Bitmap[] campaignThumbnails = null!;
    private Bitmap missionExitIcon = null!;
    private Bitmap missionExitHoverIcon = null!;
    private Bitmap missionPlayIcon = null!;
    private Bitmap missionPlayHoverIcon = null!;
    private Bitmap fileProceedIcon = null!;
    private Bitmap fileProceedHoverIcon = null!;
    private Bitmap fileUpIcon = null!;
    private Bitmap fileUpHoverIcon = null!;
    private Bitmap fileDownIcon = null!;
    private Bitmap fileDownHoverIcon = null!;
    private Bitmap tutorialCloseIcon = null!;
    private Bitmap tutorialCloseHoverIcon = null!;
    private Bitmap tutorialObjectiveCap = null!;
    private Bitmap[] menuButtonTiles = null!;
    private Bitmap scrollThumb = null!;
    private Bitmap difficultyUpIcon = null!;
    private Bitmap difficultyUpHoverIcon = null!;
    private Bitmap difficultyDownIcon = null!;
    private Bitmap difficultyDownHoverIcon = null!;
    private Bitmap[] objectiveTiles = null!;
    private readonly Dictionary<int, Bitmap> tutorialGraphics = [];
    private readonly Bitmap standardFrame = new(640, 480);
    private readonly Bitmap missionFrame = new(1024, 768);
    private Bitmap? cityPresentationFrame;
    private bool preparedCityPresentationReady;
    private LegacyBitmapFont menuFont = null!;
    private LegacyBitmapFont smallFont = null!;
    private LegacyBitmapFont normalFont = null!;
    private LegacyBitmapFont selectedFont = null!;
    private LegacyBitmapFont hoverFont = null!;
    private LegacyBitmapFont objectiveFont = null!;
    private LegacyBitmapFont headingFont = null!;
    private LegacyBitmapFont campaignTitleFont = null!;
    private LegacyUiSkin uiSkin = null!;
    private AudioManager audioManager = null!;
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 15 };
    private DateTime waterAnimationDeadline = DateTime.MinValue;
    private readonly HashSet<int> pendingFloodTerrainTiles = [];
    private readonly VideoManager videoManager = new();
    private readonly LegacyCursorManager cursorManager = new();
    private readonly ScreenManager screenManager = new();
    private DesktopScreen screen
    {
        get => screenManager.Current;
        set => screenManager.Current = value;
    }
    private string notice = string.Empty;
    private string cityStatusMessage = string.Empty;
    private DateTime cityStatusMessageDeadline = DateTime.MinValue;
    private DesktopScreen noticeReturnScreen
    {
        get => screenManager.NoticeReturn;
        set => screenManager.NoticeReturn = value;
    }
    private string familyNameInput = string.Empty;
    private bool nameCaretVisible = true;
    private int? egyptianNameHover;
    private int? selectedEgyptianName;
    private FamilyNameEntryAction? familyNameEntryHover;
    private bool egyptianNamesOkHover;
    private bool? deleteConfirmationHover;
    private bool? noMissionsConfirmationHover;
    private bool? quitConfirmationHover;
    private DesktopScreen quitConfirmationReturnScreen
    {
        get => screenManager.QuitConfirmationReturn;
        set => screenManager.QuitConfirmationReturn = value;
    }
    private FamilySelectionHit? familyHover;
    private FamilyHubItem? familyHubHover;
    private int? missionHover;
    private MissionControl? missionControlHover;
    private LegacyFileMenuHit? fileHover;
    private CampaignChoice? campaignChoiceHover;
    private CampaignChapter? activeCampaign;
    private CampaignChoiceScreen? activeChoiceScreen;
    private CampaignMission? activeMission;
    private LegacyMissionSession? activeMissionSession;
    private CitySimulation? citySimulation;
    private LegacyTutorialManager? tutorialManager;
    private readonly LegacyTutorialMessageQueue tutorialMessages = new();
    private LegacyMissionProgress? missionProgress;
    private int missionVictoryHover = -1;
    private bool missionVictoryReportShown;
    private MessageEntry? activeTutorialMessage;
    private int tutorialMessageScrollLine;
    private int tutorialScrollHover;
    private bool draggingTutorialScroll;
    private int tutorialScrollDragOffsetY;
    private int preparedMissionId = -1;
    private readonly TimeManager timeManager = new();
    private readonly InputManager inputManager = new();
    private readonly CameraManager cameraManager;
    private int? cityHoverTile;
    private LegacyDebugOverlayMode cityDebugOverlay;
    private int? cityCommandHover;
    private bool compactCitySidebar;
    private bool citySidebarToggleHover;
    private bool citySidebarAnimating;
    private bool citySidebarAnimationStartedCompact;
    private int citySidebarAnimationStep;
    private int selectedCityCommand;
    private LegacyCityHudState? cityHudState;
    private LegacyBuildMenuState? cityBuildMenuState;
    private int? openBuildMenuCommand;
    private int? cityBuildMenuHover;
    private BuildingType? selectedBuildingType;
    private Point? pendingRoadPointerDown;
    private Point? pendingHousingPointerDown;
    private Point? pendingClearLandPointerDown;
    private LegacyCityOptions cityOptions => optionManager.Options;
    private LegacyBuildingModelCosts buildingCosts = null!;
    private LegacyHousingModels housingModels = null!;
    private LegacyRuntimeTextData runtimeTextData = null!;
    private int? openCityMenu;
    private int? cityMenuItemHover;
    private int? cityRotationHover;
    private LegacyCitySettingsPage citySettingsPage;
    private int presentationLogicalWidth = 640;
    private int presentationLogicalHeight = 480;
    private int? citySettingsControlHover;
    private DesktopScreen? briefingReturnScreen
    {
        get => screenManager.BriefingReturn;
        set => screenManager.BriefingReturn = value;
    }
    private bool cityObjectivesMode;
    private bool familyHistoryMode = true;
    private bool showMissionObjectives;
    private bool draggingMissionScroll;
    private bool draggingCustomMissionScroll;
    private bool draggingFamilyScroll;
    private int missionScrollDragOffsetY;
    private int familyScrollDragOffsetY;
    private int availableFamilyPeriod;
    private int campaignDifficulty = 2;
    private int loadingPhase;
    private int paintedLoadingPhase = -1;
    private bool startupAssetsLoaded;
    private bool startupWorkInProgress;
    private readonly bool startAtPeriodVideo;
    private readonly bool skipStartupIntro;
    private readonly DesktopScreen postLoadingScreen;
    private bool suppressTitleMouseRelease;
    private IReadOnlyList<string> saveFiles = Array.Empty<string>();
    private DesktopScreen fileMenuReturnScreen
    {
        get => screenManager.FileMenuReturn;
        set => screenManager.FileMenuReturn = value;
    }
    private Rectangle presentationBounds;

    public PharaohWindow(bool skipIntro = false, bool startAtTitle = false, bool startAtFamily = false,
        bool startAtNameEntry = false, bool startAtFamilyHub = false, bool startAtMissions = false, bool startAtHighScores = false,
        bool startAtLoad = false, bool startAtCampaignMap = false, bool startAtCampaignBriefing = false,
        bool startAtEgyptianNames = false, bool startAtDeleteConfirmation = false, bool startAtPeriodVideo = false,
        bool startAtIndividualMissions = false, bool startAtCustomMissions = false,
        bool startAtNoMissionsConfirmation = false, bool showLoading = true)
    {
        cameraManager = new CameraManager(timeManager);
        this.installationRoot = Path.GetFullPath(AppContext.BaseDirectory);
        optionManager = new OptionManager(this.installationRoot);
        fileManager = new FileManager(this.installationRoot);
        graphicsAssets = new Sg3AssetCatalog(this.installationRoot);
        startupAssetLoadingManager = new StartupAssetLoadingManager(graphicsAssets);
        mapManager = new MapManager(graphicsAssets, buildModeManager);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        legacyEncoding = Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        text = LocalizedTextCatalog.Load(Path.Combine(this.installationRoot, "Pharaoh_Text.eng"), legacyEncoding);
        // FUN_00414420 registers slot zero before it can draw Load1.bmp.
        startupAssetLoadingManager.RegisterNextPack();
        loadingBackgrounds = Enumerable.Range(LegacyStartupResources.LoadingSpriteBaseIndex, 4)
            .Select(index => LoadImage(this.installationRoot, LegacyStartupResources.LoadingSpriteArchiveName, index)).ToArray();
        loadingPanel = new LoadingPanel(loadingBackgrounds, text);
        campaignDifficulty = Math.Clamp(cityOptions.Difficulty, 0, 4);
        screen = startAtTitle ? DesktopScreen.Title : startAtFamily ? DesktopScreen.Families :
            startAtNameEntry ? DesktopScreen.NameEntry : startAtFamilyHub ? DesktopScreen.FamilyHub :
            startAtEgyptianNames ? DesktopScreen.EgyptianNames : startAtDeleteConfirmation ? DesktopScreen.DeleteFamilyConfirmation :
            startAtIndividualMissions ? DesktopScreen.Maps : startAtCustomMissions ? DesktopScreen.CustomMissions :
            startAtNoMissionsConfirmation ? DesktopScreen.NoMissionsConfirmation :
            startAtMissions ? DesktopScreen.Periods : startAtHighScores ? DesktopScreen.HighScores :
            startAtLoad ? DesktopScreen.SaveBrowser : DesktopScreen.MainMenu;
        if (startAtCampaignMap) screen = DesktopScreen.CampaignMap;
        else if (startAtCampaignBriefing) screen = DesktopScreen.CampaignBriefing;
        if (startAtIndividualMissions) missionSelection.SetMode(MissionSelectionMode.Individual);
        if (startAtEgyptianNames)
        {
            selectedEgyptianName = 11;
            egyptianNameHover = 12;
        }
        skipStartupIntro = skipIntro;
        postLoadingScreen = screen;
        this.startAtPeriodVideo = startAtPeriodVideo;
        if (showLoading)
        {
            loadingPhase = 0;
            timeManager.LoadingDeadline = DateTime.MaxValue;
            screen = DesktopScreen.Loading;
        }
        else
        {
            startupAssetLoadingManager.RegisterRemainingPacks();
            LoadExpansionLogo();
            LoadPostGraphicsAssets();
            BeginPostLoading();
        }

        Text = "Nesutera";
        ClientSize = new Size(1024, 768);
        MinimumSize = new Size(656, 519);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        cursorManager.Apply(this, LegacyCursorKind.Default);
        animationTimer.Tick += (_, _) => UpdateAnimation();
        animationTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach(Bitmap infoImage in mapInformationImages.Values)infoImage.Dispose();
            animationTimer.Dispose();
            videoManager.Dispose();
            titleBackground?.Dispose();
            menuBackground?.Dispose();
            foreach (Bitmap background in loadingBackgrounds) background.Dispose();
            expansionLogo?.Dispose();
            familyBackground?.Dispose();
            familyHubBackground?.Dispose();
            missionBackground?.Dispose();
            customMissionBackground?.Dispose();
            briefingBackground?.Dispose();
            campaignMapBackground?.Dispose();
            foreach (Bitmap choiceScreen in campaignChoiceScreens ?? []) choiceScreen.Dispose();
            foreach (Bitmap thumbnail in missionThumbnails ?? []) thumbnail.Dispose();
            foreach (Bitmap thumbnail in campaignThumbnails ?? []) thumbnail.Dispose();
            missionExitIcon?.Dispose();
            missionExitHoverIcon?.Dispose();
            missionPlayIcon?.Dispose();
            missionPlayHoverIcon?.Dispose();
            fileProceedIcon?.Dispose();
            fileProceedHoverIcon?.Dispose();
            fileUpIcon?.Dispose();
            fileUpHoverIcon?.Dispose();
            fileDownIcon?.Dispose();
            fileDownHoverIcon?.Dispose();
            tutorialCloseIcon?.Dispose();
            tutorialCloseHoverIcon?.Dispose();
            tutorialObjectiveCap?.Dispose();
            foreach (Bitmap tile in menuButtonTiles ?? []) tile.Dispose();
            scrollThumb?.Dispose();
            difficultyUpIcon?.Dispose();
            difficultyUpHoverIcon?.Dispose();
            difficultyDownIcon?.Dispose();
            difficultyDownHoverIcon?.Dispose();
            foreach (Bitmap tile in objectiveTiles ?? []) tile.Dispose();
            foreach (Bitmap graphic in tutorialGraphics.Values) graphic.Dispose();
            standardFrame.Dispose();
            missionFrame.Dispose();
            cityPresentationFrame?.Dispose();
            menuFont?.Dispose();
            smallFont?.Dispose();
            normalFont?.Dispose();
            selectedFont?.Dispose();
            hoverFont?.Dispose();
            objectiveFont?.Dispose();
            headingFont?.Dispose();
            campaignTitleFont?.Dispose();
            uiSkin?.Dispose();
            audioManager?.Dispose();
            mapManager.Dispose();
            cursorManager.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (screen == DesktopScreen.Loading)
        {
            loadingPhase = 0;
            paintedLoadingPhase = -1;
            timeManager.LoadingDeadline = DateTime.MaxValue;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (screen is DesktopScreen.Intro or DesktopScreen.PeriodVideo)
        {
            DrawVideoToWindow(e.Graphics);
            return;
        }

        DesktopScreen presentationScreen = screen == DesktopScreen.QuitConfirmation ? quitConfirmationReturnScreen : screen;
        bool missionScreen = presentationScreen is DesktopScreen.Periods or DesktopScreen.Maps or DesktopScreen.CustomMissions or DesktopScreen.MissionEditor or
            DesktopScreen.TimedTransition or DesktopScreen.CampaignMap or DesktopScreen.CampaignBriefing;
        bool cityScreen = presentationScreen == DesktopScreen.City;
        Bitmap frame = cityScreen ? EnsureCityPresentationFrame() : missionScreen ? missionFrame : standardFrame;
        presentationLogicalWidth = cityScreen ? frame.Width : 640;
        presentationLogicalHeight = cityScreen ? frame.Height : 480;
        if (cityScreen && !preparedCityPresentationReady)
            PrepareCityPresentationFrame();
        if (!(cityScreen && preparedCityPresentationReady))
        {
            using Graphics graphics = Graphics.FromImage(frame);
            RenderFrame(graphics, missionScreen);
        }

        e.Graphics.Clear(Color.Black);
        Rectangle canvasBounds = Fit(ClientRectangle, frame.Width, frame.Height);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.DrawImage(frame, canvasBounds, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel);

        if (missionScreen)
        {
            int left = canvasBounds.Left + (192 * canvasBounds.Width / 1024);
            int top = canvasBounds.Top + (144 * canvasBounds.Height / 768);
            int right = canvasBounds.Left + (832 * canvasBounds.Width / 1024);
            int bottom = canvasBounds.Top + (624 * canvasBounds.Height / 768);
            presentationBounds = Rectangle.FromLTRB(left, top, right, bottom);
        }
        else
            presentationBounds = canvasBounds;
        if (cityScreen)
            preparedCityPresentationReady = false;
    }

    [RewriteOnly("Owns the native-size city back buffer selected by the original display modes")]
    private Bitmap EnsureCityPresentationFrame()
    {
        (int width, int height) = CityDisplaySize();
        if (cityPresentationFrame is null || cityPresentationFrame.Width != width || cityPresentationFrame.Height != height)
        {
            cityPresentationFrame?.Dispose();
            cityPresentationFrame = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }
        return cityPresentationFrame;
    }

    [RewriteOnly("Validates persisted dimensions against Pharaoh.exe's three display modes")]
    private (int Width, int Height) CityDisplaySize() => cityOptions.DisplayWidth switch
    {
        640 => (640, 480),
        800 => (800, 600),
        1024 => (1024, 768),
        _ => (1024, 768)
    };

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Opaque, fully composed frames avoid background erasure and resize flicker.
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        // WM_SIZE may invalidate only the newly exposed strips. Repaint the complete
        // presentation so a live resize cannot mix two differently scaled frames.
        Invalidate(ClientRectangle);
        if (Visible && IsHandleCreated && !Disposing)
            Update();
    }

    private void RenderFrame(Graphics graphics, bool missionScreen)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.Clear(Color.Black);

        if (screen == DesktopScreen.Loading)
        {
            DrawLoading(graphics);
            return;
        }

        GraphicsState state = graphics.Save();
        if (missionScreen)
        {
            DesktopScreen presentationScreen = screen == DesktopScreen.QuitConfirmation ? quitConfirmationReturnScreen : screen;
            Bitmap fullBackground = presentationScreen switch
            {
                DesktopScreen.CustomMissions => customMissionBackground,
                DesktopScreen.CampaignMap => campaignMapBackground,
                DesktopScreen.TimedTransition or DesktopScreen.CampaignBriefing => briefingBackground,
                _ => missionBackground
            };
            graphics.DrawImageUnscaled(fullBackground, 0, 0);
            graphics.TranslateTransform(192, 144);
        }
        else
            DrawBackground(graphics);

        DrawScreenContent(graphics);
        graphics.Restore(state);
    }

    private void DrawScreenContent(Graphics graphics)
    {
        switch (screen)
        {
            case DesktopScreen.Title: DrawTitleScreen(graphics); break;
            case DesktopScreen.MainMenu: DrawMainMenu(graphics); break;
            case DesktopScreen.Families: DrawFamilies(graphics); break;
            case DesktopScreen.NameEntry: DrawNameEntry(graphics); break;
            case DesktopScreen.EgyptianNames: DrawEgyptianNames(graphics); break;
            case DesktopScreen.DeleteFamilyConfirmation: DrawDeleteFamilyConfirmation(graphics); break;
            case DesktopScreen.NoMissionsConfirmation: DrawNoMissionsConfirmation(graphics); break;
            case DesktopScreen.QuitConfirmation: DrawQuitConfirmation(graphics); break;
            case DesktopScreen.FamilyHub: DrawFamilyHub(graphics); break;
            case DesktopScreen.Periods:
            case DesktopScreen.Maps:
            case DesktopScreen.CustomMissions:
            case DesktopScreen.MissionEditor: DrawMissions(graphics); break;
            case DesktopScreen.CampaignMap: DrawCampaignMap(graphics); break;
            case DesktopScreen.CampaignBriefing: DrawCampaignBriefing(graphics); break;
            case DesktopScreen.HighScores: DrawHighScores(graphics); break;
            case DesktopScreen.SaveBrowser: DrawFileMenu(graphics); break;
            case DesktopScreen.Notice: DrawNotice(graphics); break;
            case DesktopScreen.City: DrawCity(graphics); break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        inputManager.PointerMoved(e.Location);
        if (screen == DesktopScreen.City && IsInformationVisible)
        {
            mapInformationHover=TryToGame(e.Location,out UiPoint informationPoint)?MapInformationHit(informationPoint):-1;
            Invalidate();return;
        }
        if (IsMissionVictoryVisible)
        {
            missionVictoryHover = TryToGame(e.Location, out UiPoint victoryPoint)
                ? MissionVictoryPanel.HitTest(victoryPoint.X, victoryPoint.Y,
                    presentationLogicalWidth, presentationLogicalHeight, AllowContinuedGoverning) : -1;
            Invalidate();
            return;
        }
        if (screen == DesktopScreen.City && activeTutorialMessage is not null)
        {
            if (!TryToGame(e.Location, out UiPoint tutorialPoint))
            {
                if (tutorialScrollHover != 0) { tutorialScrollHover = 0; Invalidate(); }
                return;
            }

            int maximumScroll = TutorialPanel.MaximumScroll(
                activeTutorialMessage, selectedFont, ResolveTutorialGraphic);
            if (draggingTutorialScroll)
            {
                tutorialMessageScrollLine = TutorialPanel.ScrollLineFromThumbTop(
                    tutorialPoint.Y - tutorialScrollDragOffsetY, maximumScroll,
                    presentationLogicalWidth, presentationLogicalHeight);
                Invalidate();
                return;
            }

            int hover = TutorialPanel.IsCloseButton(
                tutorialPoint.X, tutorialPoint.Y,
                presentationLogicalWidth, presentationLogicalHeight)
                ? 2
                : maximumScroll > 0
                    ? TutorialPanel.HitTestScrollButton(tutorialPoint.X, tutorialPoint.Y,
                        presentationLogicalWidth, presentationLogicalHeight)
                    : 0;
            if (hover != tutorialScrollHover)
            {
                tutorialScrollHover = hover;
                Invalidate();
            }
            return;
        }
        if(buildModeManager.IsBuildingPlacementActive)
        {UpdateHeldBuildingPlacement(e.Location);return;}
        if (buildModeManager.IsRoadPlacementActive)
        {
            UpdateRoadPlacement(e.Location);
            return;
        }
        if (buildModeManager.IsHousingPlacementActive)
        {
            UpdateHousingPlacement(e.Location);
            return;
        }
        if (buildModeManager.IsClearLandPlacementActive)
        {
            UpdateClearLandPlacement(e.Location);
            return;
        }
        if (pendingRoadPointerDown is Point roadStart &&
            (e.Button & MouseButtons.Left) != 0 && TryBeginRoadPlacement(roadStart))
        {
            UpdateRoadPlacement(e.Location);
            return;
        }
        if (pendingHousingPointerDown is Point housingStart &&
            (e.Button & MouseButtons.Left) != 0 && TryBeginHousingPlacement(housingStart))
        {
            UpdateHousingPlacement(e.Location);
            return;
        }
        if (pendingClearLandPointerDown is Point clearStart &&
            (e.Button & MouseButtons.Left) != 0 && TryBeginClearLandPlacement(clearStart))
        {
            UpdateClearLandPlacement(e.Location);
            return;
        }
        if (draggingFamilyScroll && TryToGame(e.Location, out UiPoint familyDragPoint))
        {
            int range = Math.Max(0, profiles.Count - FamilySelection.VisibleRows);
            int thumbTop = Math.Clamp(familyDragPoint.Y - familyScrollDragOffsetY,
                FamilySelection.ScrollTrackTop, FamilySelection.ScrollTrackTop + FamilySelection.ScrollTrackRange);
            int first = range == 0 ? 0 : (int)Math.Round(
                (thumbTop - FamilySelection.ScrollTrackTop) * range / (double)FamilySelection.ScrollTrackRange);
            familySelection.SetScrollPosition(first, profiles.Count);
            Invalidate();
            return;
        }
        if (draggingMissionScroll && TryToGame(e.Location, out UiPoint dragPoint))
        {
            int range = Math.Max(0, missions.Count - MissionSelection.VisibleRows);
            int thumbTop = Math.Clamp(dragPoint.Y - missionScrollDragOffsetY, 277, 409);
            int first = range == 0 ? 0 : (int)Math.Round((thumbTop - 277) * range / 132d);
            missionSelection.SetScrollPosition(first, missions.Count);
            Invalidate();
            return;
        }
        if (draggingCustomMissionScroll && TryToGame(e.Location, out UiPoint customDragPoint))
        {
            int range = Math.Max(0, customMissions.Count - CustomVisibleRows);
            int thumbTop = Math.Clamp(customDragPoint.Y - missionScrollDragOffsetY, 241, 401);
            int first = range == 0 ? 0 : (int)Math.Round((thumbTop - 241) * range / 160d);
            customMissionSelection.SetScrollPosition(first, customMissions.Count, CustomVisibleRows);
            Invalidate();
            return;
        }
        if (!presentationBounds.Contains(e.Location))
        {
            ClearPointerHover();
            return;
        }
        if (screen == DesktopScreen.MainMenu && TryToGame(e.Location, out UiPoint point) && mainMenu.MovePointer(point))
            Invalidate();
        else if (screen == DesktopScreen.Families && TryToGame(e.Location, out point))
        {
            FamilySelectionHit? hit = familySelection.HitTest(BaseDisplay, point, profiles.Count);
            if (hit != familyHover) { familyHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.NameEntry && TryToGame(e.Location, out point))
        {
            FamilyNameEntryAction? hit = FamilyNameEntry.HitNameEntry(point);
            if (hit != familyNameEntryHover) { familyNameEntryHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.FamilyHub && TryToGame(e.Location, out point))
        {
            FamilyHubItem? hit = FamilyHub.HitTest(BaseDisplay, point, VisibleFamilyHubItems());
            if (hit != familyHubHover) { familyHubHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.EgyptianNames && TryToGame(e.Location, out point))
        {
            int? hit = FamilyNameEntry.HitEgyptianName(point);
            bool ok = FamilyNameEntry.HitEgyptianNamesAction(point) == FamilyNameEntryAction.ConfirmEgyptianName;
            if (hit != egyptianNameHover || ok != egyptianNamesOkHover)
            {
                egyptianNameHover = hit;
                egyptianNamesOkHover = ok;
                Invalidate();
            }
        }
        else if (screen == DesktopScreen.DeleteFamilyConfirmation && TryToGame(e.Location, out point))
        {
            bool? hit = DeleteYesBounds.Contains(point) ? true : DeleteNoBounds.Contains(point) ? false : null;
            if (hit != deleteConfirmationHover) { deleteConfirmationHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.NoMissionsConfirmation && TryToGame(e.Location, out point))
        {
            bool? hit = NoMissionsYesBounds.Contains(point) ? true : NoMissionsNoBounds.Contains(point) ? false : null;
            if (hit != noMissionsConfirmationHover) { noMissionsConfirmationHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.QuitConfirmation && TryToGame(e.Location, out point))
        {
            bool? hit = QuitYesBounds.Contains(point) ? true : QuitNoBounds.Contains(point) ? false : null;
            if (hit != quitConfirmationHover) { quitConfirmationHover = hit; Invalidate(); }
        }
        else if (screen is DesktopScreen.Periods or DesktopScreen.Maps or DesktopScreen.CustomMissions or DesktopScreen.MissionEditor &&
            TryToGame(e.Location, out point))
        {
            bool customScreen = screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor;
            MissionSelection selection = customScreen ? customMissionSelection : missionSelection;
            int count = customScreen ? customMissions.Count : missions.Count;
            int? hit = !customScreen && selection.Mode == MissionSelectionMode.Campaigns
                ? missionSelection.HitCampaign(point)
                : customScreen
                    ? selection.HitRow(point, count, CustomListBounds, CustomVisibleRows)
                    : selection.HitRow(point, count);
            MissionControl? control = HitMissionControl(point, customScreen,
                screen == DesktopScreen.Maps && showMissionObjectives);
            if (familyHistoryMode && screen == DesktopScreen.Periods &&
                (control != MissionControl.Play || missionSelection.SelectedCampaign != availableFamilyPeriod))
                control = null;
            if (hit != missionHover || control != missionControlHover)
            {
                missionHover = hit;
                missionControlHover = control;
                Invalidate();
            }
        }
        else if (screen == DesktopScreen.SaveBrowser && TryToGame(e.Location, out point))
        {
            LegacyFileMenuHit? hit = fileMenu.HitTest(point, saveFiles.Count);
            if (hit != fileHover) { fileHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.CampaignMap && TryToGame(e.Location, out point))
        {
            CampaignChoice? hit = HitCampaignChoice(point);
            if (hit != campaignChoiceHover) { campaignChoiceHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.CampaignBriefing && TryToGame(e.Location, out point))
        {
            MissionControl? hit = CampaignDifficultyUpBounds.Contains(point) ? MissionControl.DifficultyUp :
                CampaignDifficultyDownBounds.Contains(point) ? MissionControl.DifficultyDown :
                !cityObjectivesMode && CampaignBriefingBackBounds.Contains(point) ? MissionControl.Exit :
                (cityObjectivesMode ? CampaignBriefingCityReturnBounds : CampaignBriefingPlayBounds).Contains(point)
                    ? MissionControl.Play
                    : null;
            if (hit != missionControlHover) { missionControlHover = hit; Invalidate(); }
        }
        else if (screen == DesktopScreen.City && TryToGame(e.Location, out point))
        {
            if (citySidebarAnimating)
            {
                ClearCityPointerHover();
                return;
            }
            if (citySettingsPage != LegacyCitySettingsPage.None)
            {
                int? settingsControl = optionsPanel.HitTest(citySettingsPage, point,
                    presentationLogicalWidth, presentationLogicalHeight);
                if (settingsControl != citySettingsControlHover)
                {
                    citySettingsControlHover = settingsControl;
                    Invalidate();
                }
                return;
            }

            int? topMenu = HitTestCityTopMenu(point);
            bool menuChanged = false;
            if (openCityMenu is not null && topMenu is not null && topMenu != openCityMenu)
            {
                openCityMenu = topMenu;
                cityMenuItemHover = null;
                menuChanged = true;
            }
            int? menuItem = openCityMenu is int menuIndex
                ? HitTestCityDropdown(menuIndex, point)
                : null;
            int? rotation = openCityMenu is null
                ? TopBar.HitTestRotationControl(
                    point.X, point.Y, presentationLogicalWidth)
                : null;
            bool toggle = RightBar.HitTestToggle(
                point.X, point.Y, presentationLogicalWidth, compactCitySidebar);
            int? command = toggle || rotation is not null ? null : RightBar.HitTestCommand(
                point.X, point.Y, presentationLogicalWidth, compactCitySidebar, cityBuildMenuState,
                citySimulation?.Construction.UndoAvailable == true);
            int? buildMenuRow = openBuildMenuCommand is int buildCommand &&
                cityBuildMenuState is not null
                    ? BuildMenu.HitTest(
                        point.X, point.Y, menuFont, text, cityBuildMenuState,
                        buildCommand, presentationLogicalWidth, compactCitySidebar)
                    : null;
            int? hit = topMenu is null && menuItem is null && command is null && buildMenuRow is null &&
                rotation is null && !toggle && LayoutManager.CityMapBounds(
                    presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar).Contains(point.X, point.Y) &&
                cameraManager.TryHitTest(point.X, point.Y, out int tile)
                ? tile
                : null;
            if (menuChanged || hit != cityHoverTile || command != cityCommandHover ||
                menuItem != cityMenuItemHover || rotation != cityRotationHover ||
                toggle != citySidebarToggleHover || buildMenuRow != cityBuildMenuHover)
            {
                cityHoverTile = hit;
                cityCommandHover = command;
                cityMenuItemHover = menuItem;
                cityRotationHover = rotation;
                citySidebarToggleHover = toggle;
                cityBuildMenuHover = buildMenuRow;
                Invalidate();
            }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        inputManager.PointerLeft();
        ClearPointerHover();
    }

    protected override void OnActivated(EventArgs e)
    {
        inputManager.SetWindowActive(true);
        base.OnActivated(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        CancelRoadPlacement();
        draggingTutorialScroll = false;
        inputManager.SetWindowActive(false);
        timeManager.ResetCameraRepeat();
        base.OnDeactivate(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (screen == DesktopScreen.City && IsInformationVisible) return;
        if (IsMissionVictoryVisible) return;
        if (screen == DesktopScreen.City && activeTutorialMessage is not null)
        {
            if (e.Button == MouseButtons.Left && TryToGame(e.Location, out UiPoint tutorialPoint))
            {
                int maximumScroll = TutorialPanel.MaximumScroll(
                    activeTutorialMessage, selectedFont, ResolveTutorialGraphic);
                Rectangle thumbBounds = TutorialPanel.ThumbBounds(
                    tutorialMessageScrollLine, maximumScroll,
                    presentationLogicalWidth, presentationLogicalHeight, scrollThumb.Size);
                if (maximumScroll > 0 && thumbBounds.Contains(tutorialPoint.X, tutorialPoint.Y))
                {
                    draggingTutorialScroll = true;
                    tutorialScrollDragOffsetY = tutorialPoint.Y - thumbBounds.Top;
                    Capture = true;
                }
                else if (maximumScroll > 0 && TutorialPanel.IsScrollTrack(
                    tutorialPoint.X, tutorialPoint.Y, presentationLogicalWidth, presentationLogicalHeight))
                {
                    tutorialMessageScrollLine = TutorialPanel.ScrollLineFromThumbTop(
                        tutorialPoint.Y - scrollThumb.Height / 2, maximumScroll,
                        presentationLogicalWidth, presentationLogicalHeight);
                    Invalidate();
                }
            }
            return;
        }
        if (screen == DesktopScreen.Intro && e.Button == MouseButtons.Left)
        {
            suppressTitleMouseRelease = true;
            timeManager.TitleMouseBlockedUntil = DateTime.UtcNow.AddMilliseconds(SystemInformation.DoubleClickTime + 50);
            FinishIntro();
            return;
        }
        if (screen == DesktopScreen.PeriodVideo && e.Button == MouseButtons.Left)
        {
            FinishPeriodVideo();
            return;
        }
        if (screen == DesktopScreen.Families && e.Button == MouseButtons.Left &&
            TryToGame(e.Location, out UiPoint point) && FamilyScrollThumbBounds().Contains(point))
        {
            draggingFamilyScroll = true;
            familyScrollDragOffsetY = point.Y - FamilyScrollThumbBounds().Top;
            Capture = true;
            return;
        }
        if (screen == DesktopScreen.Maps && e.Button == MouseButtons.Left &&
            TryToGame(e.Location, out point) && MissionScrollThumbBounds().Contains(point))
        {
            draggingMissionScroll = true;
            missionScrollDragOffsetY = point.Y - MissionScrollThumbBounds().Top;
            Capture = true;
            return;
        }
        if (screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor && e.Button == MouseButtons.Left &&
            TryToGame(e.Location, out point) && CustomMissionScrollThumbBounds().Contains(point))
        {
            draggingCustomMissionScroll = true;
            missionScrollDragOffsetY = point.Y - CustomMissionScrollThumbBounds().Top;
            Capture = true;
            return;
        }
        if (e.Button == MouseButtons.Left && IsRoadPlacementMapPoint(e.Location))
        {
            pendingRoadPointerDown = e.Location;
            Capture = true;
            TryBeginRoadPlacement(e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left && IsHousingPlacementMapPoint(e.Location))
        {
            pendingHousingPointerDown = e.Location;
            Capture = true;
            TryBeginHousingPlacement(e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left && IsClearLandPlacementMapPoint(e.Location))
        {
            pendingClearLandPointerDown = e.Location;
            Capture = true;
            TryBeginClearLandPlacement(e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left && TryBeginBuildingPlacement(e.Location))return;
        if (startupAssetsLoaded && e.Button == MouseButtons.Left && screen != DesktopScreen.Intro)
            audioManager.PlayButton();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (screen == DesktopScreen.City && IsInformationVisible)
        { ActivateMapInformation(e); return; }
        base.OnMouseUp(e);
        if (IsMissionVictoryVisible)
        {
            if (IsMissionVictoryReportVisible)
            {
                if (e.Button == MouseButtons.Right) CompleteCampaignMission();
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left && TryToGame(e.Location, out UiPoint victoryPoint))
            {
                int choice = MissionVictoryPanel.HitTest(victoryPoint.X, victoryPoint.Y,
                    presentationLogicalWidth, presentationLogicalHeight, AllowContinuedGoverning);
                if (choice == 0) CompleteCampaignMission();
                else if (choice > 0) missionProgress!.ContinueGoverning(choice == 1 ? 24 : 60);
            }
            Invalidate();
            return;
        }
        if (screen == DesktopScreen.City && activeTutorialMessage is not null)
        {
            if (draggingTutorialScroll)
            {
                draggingTutorialScroll = false;
                Capture = false;
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                DismissTutorialNotification();
            }
            else if (e.Button == MouseButtons.Left && TryToGame(e.Location, out UiPoint tutorialPoint))
            {
                if (TutorialPanel.IsCloseButton(
                    tutorialPoint.X, tutorialPoint.Y,
                    presentationLogicalWidth, presentationLogicalHeight))
                {
                    DismissTutorialNotification();
                }
                else
                {
                    int delta = TutorialPanel.HitTestScrollButton(
                        tutorialPoint.X, tutorialPoint.Y, presentationLogicalWidth, presentationLogicalHeight);
                    tutorialMessageScrollLine = Math.Clamp(tutorialMessageScrollLine + delta, 0,
                        TutorialPanel.MaximumScroll(activeTutorialMessage, selectedFont, ResolveTutorialGraphic));
                }
            }
            Invalidate();
            return;
        }
        if(e.Button==MouseButtons.Left&&buildModeManager.IsBuildingPlacementActive)
        {ReleaseHeldBuildingPlacement(e.Location);return;}
        if (e.Button == MouseButtons.Left &&
            (buildModeManager.IsRoadPlacementActive || pendingRoadPointerDown is not null))
        {
            if (!buildModeManager.IsRoadPlacementActive && pendingRoadPointerDown is Point roadStart)
                TryBeginRoadPlacement(roadStart);
            pendingRoadPointerDown = null;
            if (buildModeManager.IsRoadPlacementActive)
                CommitRoadPlacement(e.Location);
            else
                Capture = false;
            return;
        }
        if (e.Button == MouseButtons.Left &&
            (buildModeManager.IsHousingPlacementActive || pendingHousingPointerDown is not null))
        {
            if (!buildModeManager.IsHousingPlacementActive && pendingHousingPointerDown is Point housingStart)
                TryBeginHousingPlacement(housingStart);
            pendingHousingPointerDown = null;
            if (buildModeManager.IsHousingPlacementActive)
                CommitHousingPlacement(e.Location);
            else
                Capture = false;
            return;
        }
        if (e.Button == MouseButtons.Left &&
            (buildModeManager.IsClearLandPlacementActive || pendingClearLandPointerDown is not null))
        {
            if (!buildModeManager.IsClearLandPlacementActive && pendingClearLandPointerDown is Point clearStart)
                TryBeginClearLandPlacement(clearStart);
            pendingClearLandPointerDown = null;
            if (buildModeManager.IsClearLandPlacementActive)
                CommitClearLandPlacement(e.Location);
            else
                Capture = false;
            return;
        }
        if (buildModeManager.IsRoadPlacementActive)
        {
            CancelRoadPlacement();
            return;
        }
        if (draggingFamilyScroll || draggingMissionScroll || draggingCustomMissionScroll)
        {
            draggingFamilyScroll = false;
            draggingMissionScroll = false;
            draggingCustomMissionScroll = false;
            Capture = false;
            return;
        }
        if (suppressTitleMouseRelease)
        {
            suppressTitleMouseRelease = false;
            return;
        }
        if (screen is DesktopScreen.Intro or DesktopScreen.PeriodVideo) return;
        if (screen == DesktopScreen.QuitConfirmation)
        {
            if (e.Button == MouseButtons.Left && TryToGame(e.Location, out UiPoint quitPoint))
            {
                if (QuitYesBounds.Contains(quitPoint)) Close();
                else if (QuitNoBounds.Contains(quitPoint)) screen = quitConfirmationReturnScreen;
                Invalidate();
            }
            return;
        }
        if (screen == DesktopScreen.Title)
        {
            // Ignore the complete double-click window after an intro skip. Without
            // this guard the second half of a double-click also dismissed Cleopatra.
            if (DateTime.UtcNow < timeManager.TitleMouseBlockedUntil) return;
            if (e.Button is MouseButtons.Left or MouseButtons.Right)
            {
                screen = DesktopScreen.MainMenu;
                audioManager.StartSetupMusic();
                Invalidate();
            }
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            if (screen == DesktopScreen.CampaignBriefing && cityObjectivesMode)
                return;
            if (screen == DesktopScreen.City && citySettingsPage != LegacyCitySettingsPage.None)
                CloseCitySettings();
            else if (screen == DesktopScreen.City && openCityMenu is not null)
                CloseCityMenu();
            else if (screen == DesktopScreen.City && openBuildMenuCommand is not null)
            {
                openBuildMenuCommand = null;
                cityBuildMenuHover = null;
            }
            else if (screen == DesktopScreen.City && selectedBuildingType is not null)
            {
                selectedBuildingType = null;
                buildModeManager.CancelAll();
            }
            else if (screen == DesktopScreen.City && TryToGame(e.Location,out UiPoint informationPoint))
                OpenMapInformation(informationPoint);
            else if (screen == DesktopScreen.HighScores) screen = DesktopScreen.MainMenu;
            else if (screen == DesktopScreen.Periods) screen = DesktopScreen.FamilyHub;
            else if (screen == DesktopScreen.Maps) screen = DesktopScreen.FamilyHub;
            else if (screen == DesktopScreen.CustomMissions) screen = DesktopScreen.FamilyHub;
            else if (screen == DesktopScreen.MissionEditor) screen = DesktopScreen.MainMenu;
            else if (screen == DesktopScreen.FamilyHub) OpenFamilyRegistry();
            else if (screen == DesktopScreen.NameEntry)
            {
                RefreshFamilyProfiles();
                screen = profiles.Count > 0 ? DesktopScreen.Families : DesktopScreen.MainMenu;
            }
            else if (screen == DesktopScreen.EgyptianNames) screen = DesktopScreen.NameEntry;
            else if (screen == DesktopScreen.DeleteFamilyConfirmation) OpenFamilyRegistry();
            else if (screen == DesktopScreen.NoMissionsConfirmation) screen = DesktopScreen.FamilyHub;
            else if (screen == DesktopScreen.SaveBrowser) screen = fileMenuReturnScreen;
            else if (screen == DesktopScreen.Families) screen = DesktopScreen.MainMenu;
            else if (screen == DesktopScreen.CampaignMap) screen = DesktopScreen.Periods;
            else if (screen == DesktopScreen.CampaignBriefing)
                screen = briefingReturnScreen ?? DesktopScreen.Periods;
            else return;
            Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left || !TryToGame(e.Location, out UiPoint point))
            return;
        if (screen == DesktopScreen.MainMenu)
            ActivateMainMenu(point);
        else if (screen == DesktopScreen.City)
        {
            if (citySidebarAnimating)
                return;
            if (citySettingsPage != LegacyCitySettingsPage.None)
            {
                if (optionsPanel.HitTest(citySettingsPage, point,
                    presentationLogicalWidth, presentationLogicalHeight) is int settingsControl)
                    ActivateCitySettingsControl(settingsControl);
                Invalidate();
                return;
            }

            if (HitTestCityTopMenu(point) is int topMenu)
            {
                OpenOrCloseCityMenu(topMenu);
                Invalidate();
                return;
            }

            if (openCityMenu is int menuIndex && HitTestCityDropdown(menuIndex, point) is int menuItem)
            {
                ActivateCityMenuItem(menuIndex, menuItem);
                Invalidate();
                return;
            }

            CloseCityMenu();
            if (openBuildMenuCommand is int buildCommand && cityBuildMenuState is not null &&
                BuildMenu.HitTest(
                    point.X, point.Y, menuFont, text, cityBuildMenuState,
                    buildCommand, presentationLogicalWidth, compactCitySidebar) is int buildRow)
            {
                BuildingType chosen=cityBuildMenuState.GetVerifiedAvailableEntries(buildCommand)[buildRow].Type;
                if(cityBuildMenuState.OpenSubmenu(chosen))selectedBuildingType=null;
                else{selectedBuildingType=chosen;openBuildMenuCommand=null;}
                cityBuildMenuHover = null;
                cityHoverTile = null;
                Invalidate();
                return;
            }
            if (TopBar.HitTestRotationControl(
                    point.X, point.Y, presentationLogicalWidth) is int rotationSegment)
            {
                ActivateCityRotationControl(rotationSegment);
                return;
            }
            if (TryCenterCityFromMinimap(point))
                return;
            if (RightBar.HitTestToggle(
                    point.X, point.Y, presentationLogicalWidth, compactCitySidebar))
            {
                ToggleCitySidebar();
                return;
            }

            int? command = RightBar.HitTestCommand(
                point.X, point.Y, presentationLogicalWidth, compactCitySidebar, cityBuildMenuState,
                citySimulation?.Construction.UndoAvailable == true);
            if (command is not null)
            {
                ActivateCitySidebarCommand(command.Value);
                Invalidate();
            }
        }
        else if (screen == DesktopScreen.Families)
            ActivateFamilyScreen(point);
        else if (screen == DesktopScreen.NameEntry)
        {
            FamilyNameEntryAction? action = FamilyNameEntry.HitNameEntry(point);
            if (action == FamilyNameEntryAction.EgyptianName)
            {
                egyptianNameHover = null;
                egyptianNamesOkHover = false;
                screen = DesktopScreen.EgyptianNames;
            }
            else if (action == FamilyNameEntryAction.Continue)
            {
                if (familyNameInput.Length > 0) CreateFamily();
                else OpenFamilyRegistry();
            }
        }
        else if (screen == DesktopScreen.EgyptianNames)
        {
            if (FamilyNameEntry.HitEgyptianName(point) is int nameIndex)
            {
                selectedEgyptianName = nameIndex;
                familyNameInput = text.GetString(FamilyNameEntry.EgyptianNamesTextGroup, nameIndex) ?? familyNameInput;
                ResetNameCaret();
            }
            else if (FamilyNameEntry.HitEgyptianNamesAction(point) == FamilyNameEntryAction.ConfirmEgyptianName)
            {
                familyNameEntryHover = null;
                screen = DesktopScreen.NameEntry;
            }
            Invalidate();
        }
        else if (screen == DesktopScreen.DeleteFamilyConfirmation)
        {
            if (DeleteYesBounds.Contains(point)) DeleteSelectedFamily();
            else if (DeleteNoBounds.Contains(point)) OpenFamilyRegistry();
            Invalidate();
        }
        else if (screen == DesktopScreen.NoMissionsConfirmation)
        {
            if (NoMissionsYesBounds.Contains(point)) OpenMissionReplay();
            else if (NoMissionsNoBounds.Contains(point)) screen = DesktopScreen.FamilyHub;
            Invalidate();
        }
        else if (screen == DesktopScreen.FamilyHub)
        {
            FamilyHubItem? item = FamilyHub.HitTest(BaseDisplay, point, VisibleFamilyHubItems());
            if (item?.Action == FamilyHubAction.ReturnToRegistry) OpenFamilyRegistry();
            else if (item?.Action == FamilyHubAction.ResumeHistory)
            {
                ResumeFamilyHistory();
            }
            else if (item?.Action == FamilyHubAction.ChooseMission)
            {
                if (SelectedFamilyProgress().CompletedMissionCount == 0)
                {
                    noMissionsConfirmationHover = null;
                    screen = DesktopScreen.NoMissionsConfirmation;
                }
                else OpenMissionReplay();
            }
            else if (item?.Action == FamilyHubAction.CustomMissions)
            {
                familyHistoryMode = false;
                customMissionSelection.SetMode(MissionSelectionMode.Individual);
                customMissionSelection.Select(0, customMissions.Count, CustomVisibleRows);
                screen = DesktopScreen.CustomMissions;
            }
            else if (item?.Action == FamilyHubAction.LoadGame)
                OpenLoadBrowser();
            else if (item is not null)
            {
                notice = $"The next state for '{text.GetString(FamilyHub.TextGroup, item.TextIndex)}' is mapped; its renderer is not implemented yet.";
                noticeReturnScreen = DesktopScreen.FamilyHub;
                screen = DesktopScreen.Notice;
            }
            Invalidate();
        }
        else if (screen is DesktopScreen.Periods or DesktopScreen.Maps or DesktopScreen.CustomMissions or DesktopScreen.MissionEditor)
        {
            if (screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor)
            {
                ActivateCustomMissionScreen(point, screen == DesktopScreen.MissionEditor);
                Invalidate();
                return;
            }
            if (!familyHistoryMode && screen == DesktopScreen.Periods && MissionSelection.IndividualTabBounds.Contains(point))
            {
                missionSelection.SetMode(MissionSelectionMode.Individual);
                showMissionObjectives = false;
                screen = DesktopScreen.Maps;
            }
            else if (screen == DesktopScreen.Maps && MissionSelection.CampaignTabBounds.Contains(point))
            {
                missionSelection.SetMode(MissionSelectionMode.Campaigns);
                screen = DesktopScreen.Periods;
            }
            else if (screen == DesktopScreen.Maps && MissionSelection.ScrollUpBounds.Contains(point))
                missionSelection.Scroll(-1, missions.Count);
            else if (screen == DesktopScreen.Maps && MissionSelection.ScrollDownBounds.Contains(point))
                missionSelection.Scroll(1, missions.Count);
            else if (screen == DesktopScreen.Maps && showMissionObjectives && CustomDifficultyUpBounds.Contains(point))
                campaignDifficulty = Math.Min(4, campaignDifficulty + 1);
            else if (screen == DesktopScreen.Maps && showMissionObjectives && CustomDifficultyDownBounds.Contains(point))
                campaignDifficulty = Math.Max(0, campaignDifficulty - 1);
            else if (missionSelection.Mode == MissionSelectionMode.Campaigns && missionSelection.HitCampaign(point) is int campaign)
                missionSelection.SelectCampaign(campaign);
            else if (missionSelection.Mode == MissionSelectionMode.Individual && missionSelection.HitRow(point, missions.Count) is int index)
            {
                missionSelection.Select(index, missions.Count);
            }
            else if (screen == DesktopScreen.Maps && MissionSelection.ResultsBounds.Contains(point))
                showMissionObjectives = !showMissionObjectives;
            else if (!familyHistoryMode && MissionSelection.ExitBounds.Contains(point)) screen = DesktopScreen.FamilyHub;
            else if (MissionSelection.PlayBounds.Contains(point) && missions.Count > 0)
            {
                if (missionSelection.Mode == MissionSelectionMode.Campaigns)
                {
                    if (!familyHistoryMode || missionSelection.SelectedCampaign == availableFamilyPeriod)
                        BeginCampaign();
                }
                else
                {
                    notice = $"Mission '{missions[missionSelection.SelectedIndex].Name}' is selected; its map is not connected to gameplay yet.";
                    noticeReturnScreen = screen;
                    screen = DesktopScreen.Notice;
                }
            }
            Invalidate();
        }
        else if (screen == DesktopScreen.CampaignMap)
        {
            CampaignChoice? choice = HitCampaignChoice(point);
            if (choice is not null && activeCampaign is not null)
            {
                activeMission = activeCampaign.FirstMissionForPath(choice.PathId, activeChoiceScreen?.BeforeMissionIndex ?? 0);
                briefingReturnScreen = DesktopScreen.CampaignMap;
                if (activeMission is not null) BeginBriefingTransition();
            }
            Invalidate();
        }
        else if (screen == DesktopScreen.CampaignBriefing)
        {
            if (!cityObjectivesMode && CampaignBriefingBackBounds.Contains(point) && briefingReturnScreen is not null)
                screen = briefingReturnScreen.Value;
            else if ((cityObjectivesMode ? CampaignBriefingCityReturnBounds : CampaignBriefingPlayBounds).Contains(point))
                ConfirmCampaignMission();
            else if (CampaignDifficultyUpBounds.Contains(point))
                campaignDifficulty = Math.Min(4, campaignDifficulty + 1);
            else if (CampaignDifficultyDownBounds.Contains(point))
                campaignDifficulty = Math.Max(0, campaignDifficulty - 1);
            Invalidate();
        }
        else if (screen == DesktopScreen.HighScores)
        {
            screen = DesktopScreen.MainMenu;
            Invalidate();
        }
        else if (screen == DesktopScreen.SaveBrowser)
        {
            ActivateFileMenu(point);
            Invalidate();
        }
        else if (screen == DesktopScreen.Notice)
        {
            screen = noticeReturnScreen;
            Invalidate();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            draggingFamilyScroll = false;
            draggingMissionScroll = false;
            draggingCustomMissionScroll = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (screen == DesktopScreen.City && IsInformationVisible)
        { if(e.KeyCode is Keys.Escape or Keys.Enter)CloseMapInformation();e.Handled=true;return; }
        if (IsMissionVictoryVisible)
        {
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.City && activeTutorialMessage is not null)
        {
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.Intro)
        {
            FinishIntro();
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.PeriodVideo)
        {
            FinishPeriodVideo();
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.Title) return;
        if (screen == DesktopScreen.CampaignBriefing && cityObjectivesMode)
        {
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.City && InputManager.IsCameraMovementKey(e.KeyCode))
        {
            if (inputManager.KeyDown(e.KeyCode))
                UpdateContinuousCityCamera();
            e.Handled = true;
            return;
        }
        if (screen == DesktopScreen.City && HandleCityKey(e.KeyCode))
        {
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            if (screen != DesktopScreen.QuitConfirmation)
                ShowQuitConfirmation();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.Families && e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
        {
            int rows = e.KeyCode switch
            {
                Keys.Up => -1,
                Keys.Down => 1,
                Keys.PageUp => -FamilySelection.VisibleRows,
                _ => FamilySelection.VisibleRows
            };
            familySelection.ScrollBy(rows, profiles.Count);
            Invalidate();
            e.Handled = true;
        }
        else if (screen is DesktopScreen.Periods or DesktopScreen.Maps && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
        {
            if (missionSelection.Mode == MissionSelectionMode.Campaigns)
                missionSelection.SelectCampaign(missionSelection.SelectedCampaign + (e.KeyCode == Keys.Up ? -1 : 1));
            else
            {
                missionSelection.Move(e.KeyCode == Keys.Up ? -1 : 1, missions.Count);
                showMissionObjectives = false;
            }
            Invalidate();
            e.Handled = true;
        }
        else if (screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
        {
            customMissionSelection.Move(e.KeyCode == Keys.Up ? -1 : 1, customMissions.Count, CustomVisibleRows);
            Invalidate();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.SaveBrowser && (e.KeyCode is Keys.Up or Keys.Down))
        {
            fileMenu.Scroll(e.KeyCode == Keys.Up ? -1 : 1, saveFiles.Count);
            Invalidate();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.SaveBrowser && e.KeyCode == Keys.Enter)
        {
            ProceedFileMenu();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.CampaignMap && e.KeyCode == Keys.Enter && campaignChoiceHover is not null && activeCampaign is not null)
        {
            activeMission = activeCampaign.FirstMissionForPath(campaignChoiceHover.PathId, activeChoiceScreen?.BeforeMissionIndex ?? 0);
            briefingReturnScreen = DesktopScreen.CampaignMap;
            if (activeMission is not null) BeginBriefingTransition();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.CampaignBriefing &&
                 !cityObjectivesMode && e.KeyCode == Keys.Enter)
        {
            ConfirmCampaignMission();
            e.Handled = true;
        }
        else if (screen == DesktopScreen.NameEntry && e.KeyCode == Keys.Back && familyNameInput.Length > 0)
        {
            familyNameInput = familyNameInput[..^1];
            Invalidate();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (inputManager.KeyUp(e.KeyCode))
        {
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (screen == DesktopScreen.NameEntry && !char.IsControl(e.KeyChar))
        {
            string candidate = familyNameInput + e.KeyChar;
            try
            {
                if (FamilyNameEntry.FitsLegacyBuffer(candidate, legacyEncoding))
                {
                    familyNameInput = candidate;
                    ResetNameCaret();
                    Invalidate();
                }
            }
            catch (EncoderFallbackException) { }
            e.Handled = true;
        }
        base.OnKeyPress(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if(screen==DesktopScreen.City && IsInformationVisible)
        {
            if(mapInformationHelpMessage is not null)
                mapInformationHelpScroll=Math.Clamp(mapInformationHelpScroll+(e.Delta>0?-3:3),0,helpMaximumScroll);
            Invalidate();return;
        }
        if (IsMissionVictoryVisible) return;
        int delta = e.Delta > 0 ? -1 : 1;
        if (screen == DesktopScreen.Families)
        {
            familySelection.ScrollBy(delta, profiles.Count);
            Invalidate();
        }
        else if (screen == DesktopScreen.Maps)
        {
            missionSelection.Scroll(delta, missions.Count);
            Invalidate();
        }
        else if (screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor)
        {
            customMissionSelection.Scroll(delta, customMissions.Count, CustomVisibleRows);
            Invalidate();
        }
    }

    private void ActivateMainMenu(UiPoint point)
    {
        MainMenuCommand? command = mainMenu.Activate(point);
        if (command is null) return;
        switch (command.Value.Kind)
        {
            case MainMenuCommandKind.StartGame:
                OpenFamilyRegistry();
                // Present the already composed registry immediately on the same input turn.
                Update();
                break;
            case MainMenuCommandKind.OpenWebsite:
                Process.Start(new ProcessStartInfo(command.Value.Argument!) { UseShellExecute = true });
                break;
            case MainMenuCommandKind.StartMapEditor:
                customMissionSelection.SetMode(MissionSelectionMode.Individual);
                customMissionSelection.Select(0, customMissions.Count, CustomVisibleRows);
                screen = DesktopScreen.MissionEditor;
                break;
            case MainMenuCommandKind.ShowFamilyRanking:
                screen = DesktopScreen.HighScores;
                break;
            case MainMenuCommandKind.Quit:
                ShowQuitConfirmation();
                return;
        }
        Invalidate();
    }

    [OriginalAddress(0x00516BB0, "FUN_00516bb0")]
    private void OpenFamilyRegistry()
    {
        RefreshFamilyProfiles();
        familyHover = null;
        screen = profiles.Count == 0 ? DesktopScreen.NameEntry : DesktopScreen.Families;
        if (screen == DesktopScreen.NameEntry) ResetNameCaret();
        Invalidate();
    }

    private void RefreshFamilyProfiles()
    {
        string? selectedPath = familySelection.SelectedIndex is int selected && (uint)selected < (uint)profiles.Count
            ? profiles[selected].DataPath
            : null;
        IReadOnlyList<FamilyProfile> refreshed = FamilyProfileCatalog.Load(installationRoot);
        profiles.Clear();
        profiles.AddRange(refreshed);
        int restored = selectedPath is null
            ? 0
            : profiles.FindIndex(profile => profile.DataPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        familySelection.RestoreSelection(restored < 0 ? 0 : restored, profiles.Count);
    }

    private void ActivateFamilyScreen(UiPoint point)
    {
        FamilySelectionHit? hit = familySelection.HitTest(BaseDisplay, point, profiles.Count);
        if (hit?.FamilyIndex is int index)
            familySelection.Select(index, profiles.Count);
        else if (hit?.Action is FamilySelectionAction action)
        {
            if (action is FamilySelectionAction.ScrollUp or FamilySelectionAction.ScrollDown)
                familySelection.Scroll(action, profiles.Count);
            else if (action == FamilySelectionAction.Create)
            {
                familyNameInput = string.Empty;
                familyNameEntryHover = null;
                ResetNameCaret();
                screen = DesktopScreen.NameEntry;
            }
            else if (action == FamilySelectionAction.MainMenu)
                screen = DesktopScreen.MainMenu;
            else if (action == FamilySelectionAction.Confirm && familySelection.SelectedIndex is not null)
                screen = DesktopScreen.FamilyHub;
            else if (action == FamilySelectionAction.Delete && familySelection.SelectedIndex is not null)
            {
                deleteConfirmationHover = null;
                screen = DesktopScreen.DeleteFamilyConfirmation;
            }
            else
            {
                notice = "Campaign save loading will follow the static-map milestone.";
                noticeReturnScreen = DesktopScreen.Families;
                screen = DesktopScreen.Notice;
            }
        }
        Invalidate();
    }

    private void OpenLoadBrowser()
    {
        fileMenuReturnScreen = DesktopScreen.FamilyHub;
        fileMenu.Reset(LegacyFileMenuMode.LoadCity);
        RefreshSaveFiles();
        screen = DesktopScreen.SaveBrowser;
    }

    private void RefreshSaveFiles()
    {
        if (familySelection.SelectedIndex is not int selected || (uint)selected >= (uint)profiles.Count)
        {
            saveFiles = Array.Empty<string>();
            return;
        }
        saveFiles = fileManager.EnumerateFamilySaves(profiles[selected].Name);
    }

    private void ActivateFileMenu(UiPoint point)
    {
        LegacyFileMenuHit? hit = fileMenu.HitTest(point, saveFiles.Count);
        if (hit?.FileIndex is int index) fileMenu.Select(index, saveFiles);
        else if (hit?.Action == LegacyFileMenuAction.Cancel) screen = fileMenuReturnScreen;
        else if (hit?.Action == LegacyFileMenuAction.ScrollUp) fileMenu.Scroll(-1, saveFiles.Count);
        else if (hit?.Action == LegacyFileMenuAction.ScrollDown) fileMenu.Scroll(1, saveFiles.Count);
        else if (hit?.Action == LegacyFileMenuAction.Proceed) ProceedFileMenu();
    }

    private void ProceedFileMenu()
    {
        if (fileMenu.Mode == LegacyFileMenuMode.SaveCity)
        {
            SaveCurrentCity();
            return;
        }
        if (fileMenu.SelectedIndex is not int index || (uint)index >= (uint)saveFiles.Count)
        {
            notice = text.GetString(43, 2) ?? "File does not exist";
        }
        else
        {
            try
            {
                if (fileMenu.Mode == LegacyFileMenuMode.DeleteCity)
                {
                    string deletedName = Path.GetFileName(saveFiles[index]);
                    fileManager.Delete(saveFiles[index]);
                    RefreshSaveFiles();
                    fileMenu.Reset(LegacyFileMenuMode.DeleteCity);
                    notice = $"Deleted {deletedName}.";
                }
                else
                {
                    LoadSelectedCity(saveFiles[index], DesktopScreen.SaveBrowser);
                    return;
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                notice = $"{Path.GetFileName(saveFiles[index])}\n{error.Message}";
            }
        }
        noticeReturnScreen = DesktopScreen.SaveBrowser;
        screen = DesktopScreen.Notice;
        Invalidate();
    }

    [OriginalAddress(0x00402540, "FUN_00402540")]
    private void SaveCurrentCity()
    {
        if (activeMissionSession is null || familySelection.SelectedIndex is not int selected ||
            (uint)selected >= (uint)profiles.Count)
        {
            notice = "A family and a loaded city are required before the game can be saved.";
            noticeReturnScreen = DesktopScreen.SaveBrowser;
            screen = DesktopScreen.Notice;
            Invalidate();
            return;
        }

        string requestedName = fileMenu.SelectedIndex is int fileIndex && (uint)fileIndex < (uint)saveFiles.Count
            ? Path.GetFileNameWithoutExtension(saveFiles[fileIndex])
            : cityHudState?.CityName ?? "city";
        try
        {
            string path = fileManager.SaveCity(
                activeMissionSession, profiles[selected].Name, requestedName);
            RefreshSaveFiles();
            fileMenu.Reset(LegacyFileMenuMode.SaveCity);
            int savedIndex = saveFiles.ToList().FindIndex(
                candidate => candidate.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (savedIndex >= 0)
                fileMenu.Select(savedIndex, saveFiles);
            notice = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            notice = $"The city could not be saved: {error.Message}";
        }
        noticeReturnScreen = DesktopScreen.SaveBrowser;
        screen = DesktopScreen.Notice;
        Invalidate();
    }

    [RewriteOnly("Desktop adapter over the verified saved-game path in LegacyMissionLoader")]
    private async void LoadSelectedCity(string path, DesktopScreen failureReturnScreen)
    {
        GameLoadStatus status = await loadingManager.LoadSavedGameAsync(
            path, cityOptions.Difficulty);
        if (status == GameLoadStatus.Completed)
        {
            LegacyMissionSession session = loadingManager.Session!;
            cityObjectivesMode = false;
            int? savedRank = session.LogicalChunks.TryGetValue("scenario_is_custom", out ReadOnlyMemory<byte> custom) &&
                BinaryPrimitives.ReadInt32LittleEndian(custom.Span) != 0 ? null : session.Document.CampaignMissionRank;
            activeCampaign = campaigns.Chapters.FirstOrDefault(chapter => chapter.Missions.Any(mission => mission.MissionId == savedRank));
            activeMission = activeCampaign?.Missions.FirstOrDefault(mission => mission.MissionId == savedRank);
            activeMissionSession = session;
            citySimulation = CitySimulation.Create(
                session, housingModels.GetLevels(session.Request.Difficulty), buildingCosts,
                session.Request.Difficulty, runtimeTextData.Figures);
            InitializeMissionProgress(session, activeMission?.MissionId);
            BuildCityMapFrame(session);
            cityHudState = LegacyCityHudState.FromLogicalChunks(
                session.LogicalChunks, Path.GetFileNameWithoutExtension(path));
            cityBuildMenuState = LegacyBuildMenuState.FromLogicalChunks(session.LogicalChunks, savedRank);
            session.EnterCity();
            ShowTutorialNotification(tutorialManager?.BeginMission());
            timeManager.ResetSimulation(cityHudState.Year, cityHudState.MonthIndex);
            selectedCityCommand = 0;
            cityCommandHover = null;
            openBuildMenuCommand = null;
            cityBuildMenuHover = null;
            selectedBuildingType = null;
            screen = DesktopScreen.City;
        }
        else
        {
            notice = $"Could not load {Path.GetFileName(path)}: {loadingManager.Error?.Message}";
            noticeReturnScreen = failureReturnScreen;
            screen = DesktopScreen.Notice;
        }
        Invalidate();
    }

    [OriginalAddress(0x00520AB0, "FUN_00520ab0")]
    private void DrawBackground(Graphics graphics)
    {
        DesktopScreen presentationScreen = screen == DesktopScreen.QuitConfirmation ? quitConfirmationReturnScreen : screen;
        if (presentationScreen is DesktopScreen.Periods or DesktopScreen.Maps)
        {
            graphics.DrawImageUnscaled(missionBackground, -192, -144);
            return;
        }
        Bitmap background = presentationScreen switch
        {
            DesktopScreen.Title => titleBackground,
            DesktopScreen.Families or DesktopScreen.DeleteFamilyConfirmation => familyBackground,
            DesktopScreen.FamilyHub or DesktopScreen.NoMissionsConfirmation => familyHubBackground,
            DesktopScreen.City when mapManager.Frame is not null => mapManager.Frame,
            _ => menuBackground
        };
        graphics.DrawImageUnscaled(background, 0, 0);
    }

    [OriginalAddress(0x004180C0, "FUN_004180c0")]
    private void DrawMainMenu(Graphics graphics)
    {
        foreach ((MainMenuItem item, UiRect bounds) in MainMenu.Layout(BaseDisplay))
            DrawMenuButton(graphics, bounds, item.TextOverride ??
                text.GetString(item.TextGroup, item.TextIndex) ?? $"{item.TextGroup}:{item.TextIndex}",
                mainMenu.HoveredItem?.Action == item.Action);
    }

    [OriginalAddress(0x00520E00, "FUN_00520e00")]
    private void DrawTitleScreen(Graphics graphics)
    {
        string prompt = text.GetString(13, 7) ?? "Click to continue";
        // FUN_00520E00 passes RGB565 0xffff to FUN_004D6250 for this prompt.
        int left = 320 - smallFont.Measure(graphics, prompt) / 2;
        smallFont.DrawAtSolidColor(graphics, prompt, left, 462, Color.White);
    }

    [RewriteOnly("Keeps loading timing at the window lifecycle boundary after LoadingPanel composition")]
    private void DrawLoading(Graphics graphics)
    {
        loadingPanel.Draw(graphics, loadingPhase);
        if (paintedLoadingPhase != loadingPhase)
            paintedLoadingPhase = loadingPhase;
    }

    private void DrawVideoToWindow(Graphics graphics)
    {
        graphics.Clear(Color.Black);
        Bitmap? frame = videoManager.CurrentFrame;
        if (frame is null)
        {
            presentationBounds = ClientRectangle;
            return;
        }

        Rectangle videoBounds = Fit(ClientRectangle, frame.Width, frame.Height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(frame, videoBounds, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel);
        presentationBounds = videoBounds;
    }

    [OriginalAddress(0x004180C0,"Main menu labels use the descriptor top plus6 before the ordinary glyph correction")]
    private void DrawMenuButton(Graphics graphics, UiRect bounds, string label, bool selected)
    {
        DrawSegmentedButton(graphics, bounds, selected);
        menuFont.DrawAt(graphics,label,bounds.Left+Math.Max(0,(bounds.Width-menuFont.Measure(label))/2),bounds.Top+6);
    }

    // Pharaoh.exe FUN_00536710: a horizontal row of 16 px left/middle/right
    // segments from the runtime UI graphics bank (Pharaoh_General #222-227).
    [OriginalAddress(0x00536710, "FUN_00536710")]
    private void DrawSegmentedButton(Graphics graphics, UiRect bounds, bool pressed)
    {
        int segments = Math.Max(2, bounds.Width / 16);
        int x = bounds.Left + (bounds.Width - segments * 16) / 2;
        int first = pressed ? 3 : 0;
        for (int segment = 0; segment < segments; segment++)
        {
            int tile = segment == 0 ? first : segment == segments - 1 ? first + 2 : first + 1;
            graphics.DrawImageUnscaled(menuButtonTiles[tile], x + segment * 16, bounds.Top);
        }
    }

    private void UpdateAnimation()
    {
        if (screen == DesktopScreen.City && activeTutorialMessage is null &&
            cityBuildMenuState?.HasAttention == true)
            Invalidate();
        cursorManager.Apply(this, ResolveLegacyCursor());
        LegacySimulationAdvance simulationAdvance = timeManager.UpdateSimulation(cityOptions.GameSpeed,
            screen == DesktopScreen.City && activeMissionSession is not null &&
            citySettingsPage == LegacyCitySettingsPage.None && activeTutorialMessage is null && !IsInformationVisible &&
            missionProgress?.PresentationPending != true);
        if (simulationAdvance.LegacyTicks > 0 && cityHudState is not null)
        {
            CitySimulationAdvance cityAdvance = citySimulation?.Advance(
                simulationAdvance.FirstLegacyTick, simulationAdvance.LegacyTicks)
                ?? new CitySimulationAdvance(
                    new LegacyConstructionAdvance([], false),
                    new LegacyFloodAdvance(LegacyFloodState.Farmable, -1, [], [], []),
                    new LegacyWellAdvance([]),
                    new LegacyHousingAdvance(0, 0, 0, []),
                    new WalkerAdvance(0, 0, 0, 0, [], []),
                    [],
                    new LegacyBuildingRiskAdvance(false, false, []));
            LegacyConstructionAdvance constructionAdvance = cityAdvance.Construction;
            if (constructionAdvance.TerrainChanged && activeMissionSession is not null)
            {
                mapManager.InvalidateTerrainTiles(constructionAdvance.ChangedTiles);
                BuildCityMapFrame(activeMissionSession, resetView: false);
                Invalidate();
            }
            else if (constructionAdvance.UndoAvailabilityChanged)
                Invalidate();

            LegacyFloodAdvance floodAdvance = cityAdvance.Flood;
            LegacyWellAdvance wellAdvance = cityAdvance.Wells;
            LegacyHousingAdvance housingAdvance = cityAdvance.Housing;
            WalkerAdvance walkerAdvance = cityAdvance.Walkers;
            LegacyBuildingRiskAdvance riskAdvance = cityAdvance.BuildingRisks;
            IReadOnlyList<int> housingTerrainTiles = housingAdvance.RequiresTerrainRebuild ? housingAdvance.ChangedTiles : [];
            if ((floodAdvance.TerrainChanged || wellAdvance.TerrainChanged || housingAdvance.RequiresTerrainRebuild ||
                 walkerAdvance.PopulationDelta < 0 || cityAdvance.SafetyServiceChangedTiles.Count != 0 ||
                 riskAdvance.ChangedTiles.Count != 0) &&
                activeMissionSession is not null)
            {
                mapManager.InvalidateFloodTerrainTiles(floodAdvance.FloodedTiles, radius: 2);
                mapManager.InvalidateFloodTerrainTiles(floodAdvance.RecededTiles, radius: 3);
                mapManager.InvalidateTerrainTiles(
                    floodAdvance.ChangedTiles
                        .Except(floodAdvance.FloodedTiles)
                        .Except(floodAdvance.RecededTiles)
                        .Concat(wellAdvance.ChangedTiles)
                        .Concat(housingTerrainTiles)
                        .Concat(cityAdvance.SafetyServiceChangedTiles)
                        .Concat(riskAdvance.ChangedTiles)
                        .Concat(walkerAdvance.PopulationDelta < 0 ? walkerAdvance.ChangedTiles : []).Distinct());
                HashSet<int> changedTerrain = [.. floodAdvance.ChangedTiles
                    .Except(floodAdvance.FloodedTiles)
                    .Except(floodAdvance.RecededTiles)
                    .Concat(wellAdvance.ChangedTiles)
                    .Concat(housingTerrainTiles)
                    .Concat(cityAdvance.SafetyServiceChangedTiles)
                    .Concat(riskAdvance.ChangedTiles)
                    .Concat(walkerAdvance.PopulationDelta < 0 ? walkerAdvance.ChangedTiles : [])];
                foreach (int tile in floodAdvance.FloodedTiles)
                    changedTerrain.UnionWith(
                        FloodManager.EnumerateTerrainRefreshTiles(tile, radius: 2));
                foreach (int tile in floodAdvance.RecededTiles)
                    changedTerrain.UnionWith(
                        FloodManager.EnumerateTerrainRefreshTiles(tile, radius: 3));
                if (!wellAdvance.TerrainChanged && !housingAdvance.RequiresTerrainRebuild && walkerAdvance.PopulationDelta >= 0 &&
                    cityAdvance.SafetyServiceChangedTiles.Count == 0 && riskAdvance.ChangedTiles.Count == 0)
                    pendingFloodTerrainTiles.UnionWith(changedTerrain);
                else
                {
                    pendingFloodTerrainTiles.Clear();
                    BuildCityMapFrame(activeMissionSession, resetView: false);
                    Invalidate();
                }
            }
            IReadOnlyList<int> houseImages = !housingAdvance.RequiresTerrainRebuild && housingAdvance.ChangedTiles.Count != 0
                ? walkerAdvance.HousingImageChangedTiles.Concat(housingAdvance.ChangedTiles).Distinct().ToArray()
                : walkerAdvance.HousingImageChangedTiles;
            if (houseImages.Count != 0 && activeMissionSession is not null)
            {
                mapManager.RefreshHousingImages(
                    activeMissionSession, cameraManager, houseImages);
                Invalidate();
            }
            int currentPopulation=activeMissionSession is {} populationSession
                ? BinaryPrimitives.ReadInt32LittleEndian(populationSession.LogicalChunks["city_data"].Span[18940..]):cityHudState.Population;
            if(currentPopulation!=cityHudState.Population)
            {cityHudState=cityHudState with {Population=currentPopulation};Invalidate();}
            if (walkerAdvance.Changed)
                Invalidate();
            if (riskAdvance.FireStarted)
                ShowTutorialNotification(tutorialManager?.NotifyFireStarted());
            if (riskAdvance.BuildingCollapsed)
                ShowTutorialNotification(tutorialManager?.NotifyBuildingCollapsed());
            if (tutorialManager is not null)
                foreach (LegacyTutorialNotification notification in tutorialManager.Advance(
                    simulationAdvance.FirstLegacyTick, simulationAdvance.LegacyTicks))
                    ShowTutorialNotification(notification);
            if (missionProgress?.Advance(simulationAdvance.FirstLegacyTick, simulationAdvance.LegacyTicks) == true)
            {
                openCityMenu = null;
                openBuildMenuCommand = null;
                buildModeManager.CancelAll();
                Invalidate();
            }
            LegacyCalendarTime calendar = timeManager.Calendar;
            if (calendar.MonthIndex != cityHudState.MonthIndex || calendar.Year != cityHudState.Year)
            {
                cityHudState = cityHudState with { MonthIndex = calendar.MonthIndex, Year = calendar.Year };
                Invalidate();
            }
        }

        if (screen == DesktopScreen.Loading && paintedLoadingPhase == loadingPhase && !startupWorkInProgress)
        {
            startupWorkInProgress = true;
            try
            {
                if (loadingPhase < LegacyStartupResources.LoadingCheckpoints.Count)
                {
                    startupAssetLoadingManager.CompleteCheckpointWork(loadingPhase);
                    if (loadingPhase == LegacyStartupResources.LoadingCheckpoints.Count - 1)
                        LoadExpansionLogo();
                    loadingPhase++;
                    paintedLoadingPhase = -1;
                    Invalidate();
                }
                else
                {
                    LoadPostGraphicsAssets();
                    BeginPostLoading();
                }
            }
            finally
            {
                startupWorkInProgress = false;
            }
        }
        else if ((screen is DesktopScreen.Intro or DesktopScreen.PeriodVideo) && videoManager.IsPlaying)
        {
            bool changed = videoManager.Update();
            if (videoManager.Finished || videoManager.Failure is not null)
            {
                if (screen == DesktopScreen.Intro) FinishIntro();
                else FinishPeriodVideo();
            }
            else if (changed) Invalidate();
        }
        else if (screen == DesktopScreen.TimedTransition && DateTime.UtcNow >= timeManager.TransitionDeadline)
        {
            screen = DesktopScreen.CampaignBriefing;
            Invalidate();
        }
        else if (screen == DesktopScreen.NameEntry && DateTime.UtcNow >= timeManager.NameCaretDeadline)
        {
            nameCaretVisible = !nameCaretVisible;
            timeManager.NameCaretDeadline = DateTime.UtcNow.AddMilliseconds(500);
            Invalidate();
        }
        else if (screen == DesktopScreen.City && citySidebarAnimating)
        {
            AdvanceCitySidebarAnimation();
        }
        else if (screen == DesktopScreen.City)
        {
            UpdateContinuousCityCamera();
        }

        if (screen == DesktopScreen.City && DateTime.UtcNow >= waterAnimationDeadline)
        {
            waterAnimationDeadline = DateTime.UtcNow.AddMilliseconds(
                LegacyWaterAnimation.PulseMilliseconds);
            bool changed = false;
            if (pendingFloodTerrainTiles.Count > 0 && activeMissionSession is not null)
            {
                mapManager.RefreshTerrain(
                    activeMissionSession, cameraManager, pendingFloodTerrainTiles);
                pendingFloodTerrainTiles.Clear();
                changed = true;
            }
            changed |= mapManager.AdvanceWaterAnimation(cameraManager,includeBuildings:false);
            if (changed)Invalidate();
        }
        if(screen==DesktopScreen.City&&activeMissionSession is not null&&!citySidebarAnimating&&
            mapManager.AdvanceBuildingAnimations(cameraManager,Environment.TickCount64))Invalidate();
    }

    [OriginalAddress(0x004D6B60, "FUN_004d6b60")]
    private LegacyCursorKind ResolveLegacyCursor()
    {
        // The currently reconstructed city actions reach the executable's
        // default and clear-land states. The complete 0..18 cursor table is
        // retained by LegacyCursorManager for the remaining action systems.
        if (screen == DesktopScreen.City && selectedBuildingType == BuildingType.ClearLand)
            return LegacyCursorKind.ClearLand;
        return LegacyCursorKind.Default;
    }

    [OriginalAddress(0x00520AF0, "FUN_00520af0")]
    private void LoadExpansionLogo()
    {
        if (expansionLogo is not null) return;
        expansionLogo = LoadImage(installationRoot, LegacyStartupResources.ExpansionLogoArchiveName,
            LegacyStartupResources.ExpansionLogoImageIndex);
        loadingPanel.SetExpansionLogo(expansionLogo);
    }

    /// <summary>
    /// Loads the data and presentation caches that Pharaoh.exe initializes
    /// while the final Expansion.sg3 logo is on screen, after FUN_00414420 has finished
    /// building the graphics-pack registry and before the intro Bink starts.
    /// </summary>
    [OriginalAddress(0x00413ED0, "FUN_00413ed0")]
    private void LoadPostGraphicsAssets()
    {
        if (startupAssetsLoaded) return;

        buildingCosts = LegacyBuildingModelCosts.Load(installationRoot);
        housingModels = LegacyHousingModels.Load(installationRoot);
        runtimeTextData = LegacyRuntimeTextData.Load(installationRoot);
        campaigns = CampaignCatalog.Load(installationRoot);
        messages = MessageEngArchive.Load(Path.Combine(installationRoot, "Pharaoh_MM.eng"), legacyEncoding);
        missionArchive = MissionPakArchive.Load(Path.Combine(installationRoot, "mission1.pak"));
        profiles = FamilyProfileCatalog.Load(installationRoot).ToList();
        missions = MissionSelection.LoadCampaign(installationRoot);
        customMissions = CustomMissionCatalog.Load(installationRoot);

        titleBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 425);
        menuBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 201);
        familyBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 654);
        familyHubBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 656);
        missionBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3",
            MissionSelection.ExploreHistoryBackgroundGraphicIndex);
        customMissionBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 657);
        briefingBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 507);
        campaignMapBackground = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 492);
        campaignChoiceScreens = Enumerable.Range(493, 9)
            .Select(index => LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", index)).ToArray();
        missionThumbnails = missions.Select(mission => mission.Id < 19
            ? LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 635 + mission.Id)
            : LoadImage(installationRoot, "Expansion.sg3", 865 + mission.Id - 19)).ToArray();
        campaignThumbnails =
        [
            LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 635),
            LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 636),
            LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 637),
            LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 638),
            LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 639),
            LoadImage(installationRoot, "Expansion.sg3", 869),
            LoadImage(installationRoot, "Expansion.sg3", 867),
            LoadImage(installationRoot, "Expansion.sg3", 870),
            LoadImage(installationRoot, "Expansion.sg3", 872)
        ];
        missionExitIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 391);
        missionExitHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 392);
        missionPlayIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 327);
        missionPlayHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 328);
        fileProceedIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 387);
        fileProceedHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 388);
        fileUpIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 395);
        fileUpHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 396);
        fileDownIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 399);
        fileDownHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 400);
        // The EXE values are offsets in one-based global group 403, not local
        // SG3 image indices. FUN_0053DFF0 maps it to General group 134, whose
        // local start is 695: close +4/+5 is 699/700 and objective +16 is 711.
        tutorialCloseIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 699);
        tutorialCloseHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 700);
        tutorialObjectiveCap = LoadImage(installationRoot, "Pharaoh_General.sg3", 711);
        menuButtonTiles = Enumerable.Range(222, 6)
            .Select(index => LoadImage(installationRoot, "Pharaoh_General.sg3", index)).ToArray();
        scrollThumb = LoadImage(installationRoot, "Pharaoh_General.sg3", 261);
        difficultyUpIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 743);
        difficultyUpHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 744);
        difficultyDownIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 746);
        difficultyDownHoverIcon = LoadImage(installationRoot, "Pharaoh_General.sg3", 747);
        objectiveTiles = Enumerable.Range(265, 3)
            .Select(index => LoadImage(installationRoot, "Pharaoh_General.sg3", index)).ToArray();

        menuFont = new LegacyBitmapFont(installationRoot, 335, graphicsAssets);
        smallFont = new LegacyBitmapFont(installationRoot, 201, graphicsAssets);
        selectedFont = new LegacyBitmapFont(installationRoot, 469, graphicsAssets);
        hoverFont = new LegacyBitmapFont(installationRoot, 737, graphicsAssets);
        objectiveFont = new LegacyBitmapFont(installationRoot, 603, graphicsAssets);
        headingFont = new LegacyBitmapFont(installationRoot, 871, graphicsAssets);
        campaignTitleFont = new LegacyBitmapFont(installationRoot, 1005, graphicsAssets);
        normalFont = new LegacyBitmapFont(installationRoot, 1273, graphicsAssets);
        uiSkin = new LegacyUiSkin(installationRoot, graphicsAssets);
        optionsPanel = new OptionsPanel(uiSkin, normalFont, selectedFont, text,
            difficultyUpIcon, difficultyUpHoverIcon, difficultyDownIcon, difficultyDownHoverIcon);
        audioManager = new AudioManager(installationRoot);
        audioManager.SetVolumes(cityOptions.MusicEnabled ? cityOptions.MusicVolume : 0,
            cityOptions.EffectsEnabled ? cityOptions.EffectsVolume : 0);

        if (postLoadingScreen == DesktopScreen.CampaignMap)
        {
            activeCampaign = campaigns.Chapters.FirstOrDefault(chapter => chapter.InitialChoiceScreen is not null);
            activeChoiceScreen = activeCampaign?.InitialChoiceScreen;
            campaignChoiceHover = activeChoiceScreen?.Choices.FirstOrDefault();
        }
        else if (postLoadingScreen == DesktopScreen.CampaignBriefing)
        {
            activeCampaign = campaigns.Chapters.FirstOrDefault();
            activeMission = activeCampaign?.Missions.FirstOrDefault();
        }
        if (postLoadingScreen == DesktopScreen.SaveBrowser) RefreshSaveFiles();
        if (postLoadingScreen is DesktopScreen.Families or DesktopScreen.FamilyHub or
            DesktopScreen.DeleteFamilyConfirmation or DesktopScreen.NoMissionsConfirmation && profiles.Count > 0)
            familySelection.SelectFirstIfAvailable(profiles.Count);

        PrewarmFamilyRegistry();
        startupAssetsLoaded = true;
    }

    private void BeginPostLoading()
    {
        if (!startupAssetsLoaded)
            throw new InvalidOperationException("Startup assets must be loaded before leaving the loading screen.");
        screen = postLoadingScreen;
        if (startAtPeriodVideo)
        {
            familyHistoryMode = true;
            missionSelection.SelectCampaign(0);
            BeginCampaign();
            return;
        }
        if (!skipStartupIntro && postLoadingScreen == DesktopScreen.MainMenu)
        {
            string introPath = Path.Combine(installationRoot, "BINKS", "High", "Intro_big.bik");
            if (File.Exists(introPath))
            {
                videoManager.Play(introPath);
                screen = DesktopScreen.Intro;
            }
        }
        if (screen == DesktopScreen.MainMenu) audioManager.StartSetupMusic();
        Invalidate();
    }

    private void FinishIntro()
    {
        videoManager.Stop();
        screen = DesktopScreen.Title;
        Invalidate();
    }

    private void BeginCampaign()
    {
        int chapterIndex = missionSelection.SelectedCampaign;
        if ((uint)chapterIndex >= (uint)campaigns.Chapters.Count) return;
        activeCampaign = campaigns.Chapters[chapterIndex];
        briefingReturnScreen = null;
        activeChoiceScreen = activeCampaign.InitialChoiceScreen;
        campaignChoiceHover = activeChoiceScreen?.Choices.FirstOrDefault();
        activeMission = activeChoiceScreen is null ? activeCampaign.Missions.FirstOrDefault() : null;
        string? movieName = chapterIndex switch
        {
            0 => "pre_dynastic_big.bik",
            1 => "Archaic_big.bik",
            2 => "old_kingdom_big.bik",
            3 => "middle_kingdom_big.bik",
            4 => "new_king_big.bik",
            _ => null
        };
        string? moviePath = movieName is null ? null : Path.Combine(installationRoot, "BINKS", "High", movieName);
        if (moviePath is not null && File.Exists(moviePath))
        {
            audioManager.StopSetupMusic();
            videoManager.Play(moviePath);
            screen = DesktopScreen.PeriodVideo;
            return;
        }
        ContinueCampaignAfterPeriodVideo();
    }

    private void FinishPeriodVideo()
    {
        videoManager.Stop();
        ContinueCampaignAfterPeriodVideo();
        Invalidate();
    }

    private void ContinueCampaignAfterPeriodVideo()
    {
        if (activeChoiceScreen is not null)
        {
            screen = DesktopScreen.CampaignMap;
            return;
        }
        if (activeMission is not null) BeginBriefingTransition();
        else screen = DesktopScreen.Periods;
    }

    [OriginalAddress(0x004181E0, "FUN_004181e0")]
    [OriginalAddress(0x0041AA20, "FUN_0041aa20")]
    private void BeginBriefingTransition()
    {
        cityObjectivesMode = false;
        activeMissionSession = null;
        citySimulation = null;
        preparedMissionId = -1;
        preparedCityPresentationReady = false;
        timeManager.TransitionDeadline = DateTime.UtcNow.AddMilliseconds(1000);
        screen = DesktopScreen.TimedTransition;
        PrepareCampaignMissionBriefing();
    }

    [OriginalAddress(0x0041A180, "FUN_0041a180")]
    [OriginalAddress(0x005405B0, "FUN_005405b0")]
    private async void PrepareCampaignMissionBriefing()
    {
        CampaignMission? mission = activeMission;
        if (mission is null || preparedMissionId == mission.MissionId ||
            loadingManager.Status == GameLoadStatus.Loading)
            return;

        preparedMissionId = mission.MissionId;
        GameLoadStatus status = await loadingManager.LoadCampaignAsync(
            installationRoot, campaignDifficulty, mission.MissionId);
        if (activeMission?.MissionId != mission.MissionId)
            return;

        if (status == GameLoadStatus.Completed)
        {
            LegacyMissionSession session = loadingManager.Session!;
            string name = (uint)mission.MissionId < (uint)campaigns.MissionNames.Count
                ? campaigns.MissionNames[mission.MissionId]
                : $"Mission {mission.MissionId}";
            // Pharaoh.exe FUN_0041A180 invokes FUN_005405B0 while the briefing is
            // visible. Build every expensive city resource here; FUN_00418220
            // only changes the published gameplay state when the user confirms.
            compactCitySidebar = false;
            citySidebarAnimating = false;
            citySidebarAnimationStartedCompact = false;
            citySidebarAnimationStep = 0;
            activeMissionSession = session;
            citySimulation = CitySimulation.Create(
                session, housingModels.GetLevels(session.Request.Difficulty), buildingCosts,
                session.Request.Difficulty, runtimeTextData.Figures);
            InitializeMissionProgress(session, mission.MissionId);
            BuildCityMapFrame(session);
            cityHudState = LegacyCityHudState.FromLogicalChunks(session.LogicalChunks, name);
            cityBuildMenuState = LegacyBuildMenuState.FromLogicalChunks(
                session.LogicalChunks, mission.MissionId);
            selectedCityCommand = 0;
            cityCommandHover = null;
            openBuildMenuCommand = null;
            cityBuildMenuHover = null;
            selectedBuildingType = null;
            PrepareCityPresentationFrame();
        }
        else if (status == GameLoadStatus.Failed)
        {
            notice = $"Mission loading failed: {loadingManager.Error?.Message}";
            noticeReturnScreen = DesktopScreen.CampaignBriefing;
            screen = DesktopScreen.Notice;
        }
        Invalidate();
    }

    [OriginalAddress(0x00418220, "FUN_00418220")]
    private void ConfirmCampaignMission()
    {
        if (activeMission is null) return;
        if (cityObjectivesMode)
        {
            cityObjectivesMode = false;
            briefingReturnScreen = null;
            missionControlHover = null;
            screen = DesktopScreen.City;
            Invalidate();
            return;
        }
        string name = (uint)activeMission.MissionId < (uint)campaigns.MissionNames.Count
            ? campaigns.MissionNames[activeMission.MissionId]
            : $"Mission {activeMission.MissionId}";
        if (activeMissionSession is null || preparedMissionId != activeMission.MissionId)
        {
            PrepareCampaignMissionBriefing();
            notice = loadingManager.Status == GameLoadStatus.Failed
                ? $"Mission '{name}' could not be prepared: {loadingManager.Error?.Message}"
                : $"Mission '{name}' is still loading from mission1.pak.";
            noticeReturnScreen = DesktopScreen.CampaignBriefing;
            screen = DesktopScreen.Notice;
            return;
        }

        if (activeMissionSession.FlowState == LegacyMissionFlowState.SerializedStateReady)
        {
            activeMissionSession.EnterCity();
            timeManager.ResetSimulation(cityHudState?.Year ?? 0, cityHudState?.MonthIndex ?? 0);
            ShowTutorialNotification(tutorialManager?.BeginMission());
        }
        cityObjectivesMode = false;
        audioManager.StopSetupMusic();
        screen = DesktopScreen.City;
        Invalidate();
    }

    [RewriteOnly("Window adapter for the verified city chrome renderer")]
    private void DrawCity(Graphics graphics)
    {
        if (cityHudState is null)
            return;
        DrawHousingPlacementPreview(graphics);
        DrawRoadPlacementPrice(graphics);
        DrawHousingPlacementPrice(graphics);
        DrawClearLandPlacementPrice(graphics);
        DrawSingleCellBuildingPlacementPrice(graphics);
        uiManager.DrawCity(graphics, uiSkin, smallFont, menuFont, normalFont, hoverFont, objectiveFont, text,
            mapManager.MinimapFrame, cityHudState, cityBuildMenuState,
            citySimulation?.Construction.UndoAvailable == true, buildingCosts, campaignDifficulty,
            selectedCityCommand, cityCommandHover, openBuildMenuCommand, cityBuildMenuHover,
            cityOptions, openCityMenu, cityMenuItemHover, cityRotationHover,
            compactCitySidebar, citySidebarToggleHover,
            citySidebarAnimating
                ? LegacyCitySidebarAnimation.ExpandedPanelOffset(
                    citySidebarAnimationStep, citySidebarAnimationStartedCompact)
                : null,
            presentationLogicalWidth, presentationLogicalHeight);
        DrawCityStatusMessage(graphics);
        DrawMapInformation(graphics);
        if (citySettingsPage != LegacyCitySettingsPage.None)
            optionsPanel.Draw(graphics, citySettingsPage, cityOptions, citySettingsControlHover,
                presentationLogicalWidth, presentationLogicalHeight);
        DrawMissionVictory(graphics);
    }

    [RewriteOnly("Raster overlay adapter for the original placement tint and pointer-local cost rendering")]
    private bool TryGetRoadPlacementEndpoint(out bool dragging, out int endpoint)
    {
        dragging = false;
        endpoint = -1;
        if (selectedBuildingType != LegacyRoadPlacement.RoadType ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return false;

        dragging = buildModeManager.IsRoadPlacementActive;
        endpoint = dragging ? buildModeManager.RoadPreviewEndpoint : cityHoverTile ?? -1;
        return endpoint >= 0;
    }

    [RewriteOnly("Housing uses the same cursor endpoint but an independent rectangular placement state")]
    private bool TryGetHousingPlacementEndpoint(out bool dragging, out int endpoint)
    {
        dragging = false;
        endpoint = -1;
        if (selectedBuildingType != LegacyHousingPlacement.HousingType ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return false;

        dragging = buildModeManager.IsHousingPlacementActive;
        endpoint = dragging ? buildModeManager.HousingPreviewEndpoint : cityHoverTile ?? -1;
        return endpoint >= 0;
    }

    [RewriteOnly("Clear land uses Pharaoh's rectangular drag endpoint and type-9 cost")]
    private bool TryGetClearLandPlacementEndpoint(out bool dragging, out int endpoint)
    {
        dragging = false;
        endpoint = -1;
        if (selectedBuildingType != LegacyClearLandPlacement.ClearLandType ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return false;

        dragging = buildModeManager.IsClearLandPlacementActive;
        endpoint = dragging ? buildModeManager.ClearLandPreviewEndpoint : cityHoverTile ?? -1;
        return endpoint >= 0;
    }

    [RewriteOnly("Draws the independently validated cells in Pharaoh's housing rectangle")]
    private void DrawHousingPlacementPreview(Graphics graphics)
    {
        if (!TryGetHousingPlacementEndpoint(out bool dragging, out _) || !dragging)
            return;
        GraphicsState saved = graphics.Save();
        graphics.SetClip(LayoutManager.CityMapBounds(
            presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
        mapManager.DrawHousingPreview(graphics, cameraManager);
        graphics.Restore(saved);
    }

    [RewriteOnly("Draws the original pointer-local road price after the RGB565 endpoint mask")]
    private void DrawRoadPlacementPrice(Graphics graphics)
    {
        if (!TryGetRoadPlacementEndpoint(out bool dragging, out int endpoint) || !dragging)
            return;
        GraphicsState saved = graphics.Save();
        graphics.SetClip(LayoutManager.CityMapBounds(
            presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
        if (cameraManager.TryGetTileScreenAnchor(endpoint, out Point priceAnchor))
        {
            int unitCost = activeMissionSession is null
                ? 0
                : buildingCosts.GetCost(
                    activeMissionSession.Request.Difficulty, LegacyRoadPlacement.RoadType);
            int price = checked(buildModeManager.RoadPreviewNewTileCount * unitCost);
            string label = price.ToString(CultureInfo.InvariantCulture);
            Rectangle mapBounds = LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar);
            int labelWidth = objectiveFont.Measure(graphics, label);
            int labelX = priceAnchor.X + LegacyIsometricViewport.HorizontalStride;
            if (labelX + labelWidth > mapBounds.Right)
                labelX = priceAnchor.X - labelWidth - 4;
            labelX = Math.Clamp(labelX, mapBounds.Left, mapBounds.Right - labelWidth);
            int labelY = Math.Clamp(
                priceAnchor.Y,
                mapBounds.Top, mapBounds.Bottom - LegacyIsometricViewport.HalfTileHeight);
            objectiveFont.DrawAt(graphics, label, labelX, labelY);
        }
        graphics.Restore(saved);
    }

    [RewriteOnly("Draws the pointer-local total for all valid housing cells in the drag rectangle")]
    private void DrawHousingPlacementPrice(Graphics graphics)
    {
        if (!TryGetHousingPlacementEndpoint(out bool dragging, out int endpoint) || !dragging)
            return;
        DrawPlacementPrice(
            graphics, endpoint, buildModeManager.HousingPreviewTileCount,
            LegacyHousingPlacement.HousingType);
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    [OriginalAddress(0x00474700, "FUN_00474700")]
    private void DrawClearLandPlacementPrice(Graphics graphics)
    {
        if (!TryGetClearLandPlacementEndpoint(out bool dragging, out int endpoint) || !dragging)
            return;
        DrawPlacementPrice(
            graphics, endpoint, buildModeManager.ClearLandPreviewItemCount,
            LegacyClearLandPlacement.ClearLandType);
    }

    private void DrawSingleCellBuildingPlacementPrice(Graphics graphics)
    {
        if (!buildModeManager.IsBuildingPlacementActive || selectedBuildingType is not BuildingType type || cityHoverTile is not int tile ||
            (type != BuildingType.Well && !LegacySafetyServicePlacement.TryGetDefinition(type, out _) &&
             !LegacySquareBuildingPlacement.TryGetDefinition(type, out _)) ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return;
        DrawPlacementPrice(graphics, tile, 1, type);
    }

    private void DrawPlacementPrice(
        Graphics graphics,
        int endpoint,
        int count,
        BuildingType buildingType)
    {
        GraphicsState saved = graphics.Save();
        graphics.SetClip(LayoutManager.CityMapBounds(
            presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
        if (cameraManager.TryGetTileScreenAnchor(endpoint, out Point priceAnchor))
        {
            int unitCost = activeMissionSession is null
                ? 0
                : buildingCosts.GetCost(activeMissionSession.Request.Difficulty, buildingType);
            string label = checked(count * unitCost).ToString(CultureInfo.InvariantCulture);
            Rectangle mapBounds = LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar);
            int labelWidth = objectiveFont.Measure(graphics, label);
            int labelX = Math.Clamp(
                priceAnchor.X + LegacyIsometricViewport.HorizontalStride,
                mapBounds.Left, mapBounds.Right - labelWidth);
            int labelY = Math.Clamp(
                priceAnchor.Y, mapBounds.Top,
                mapBounds.Bottom - LegacyIsometricViewport.HalfTileHeight);
            objectiveFont.DrawAt(graphics, label, labelX, labelY);
        }
        graphics.Restore(saved);
    }

    [RewriteOnly("Applies Pharaoh's destination-pixel road marker after map composition and before UI chrome")]
    private void ApplyRoadPlacementMarker(Bitmap frame)
    {
        if (!TryGetRoadPlacementEndpoint(out bool dragging, out int endpoint))
            return;
        bool invalid = dragging || activeMissionSession is null ||
            !BuildModeManager.CanBeginRoadPlacement(activeMissionSession, endpoint);
        mapManager.ApplyRoadPlacementMarker(
            frame,
            cameraManager,
            endpoint,
            invalid,
            invalid ? (ushort)0xf863 : (ushort)0x1fe3,
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
    }

    [RewriteOnly("Applies the original valid/invalid housing cursor masks")]
    private void ApplyHousingPlacementMarker(Bitmap frame)
    {
        if (!TryGetHousingPlacementEndpoint(out bool dragging, out int endpoint))
            return;
        bool invalid = activeMissionSession is null ||
            !BuildModeManager.CanBeginHousingPlacement(activeMissionSession, endpoint);
        mapManager.ApplyHousingPlacementMarker(
            frame, cameraManager, endpoint, invalid,
            invalid ? (ushort)0xf863 : (ushort)0x1fe3,
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
    }

    [OriginalAddress(0x0043B630, "building preview at cursor anchor; invalid footprints use per-cell markers")]
    private void ApplySafetyServicePlacementMarker(Bitmap frame)
    {
        if (selectedBuildingType is BuildingType foodType && activeMissionSession is not null &&
            cityHoverTile is int foodTile && LegacySquareBuildingPlacement.TryGetDefinition(foodType, out var food) &&
            citySettingsPage == LegacyCitySettingsPage.None && openCityMenu is null && openBuildMenuCommand is null)
        {
            LegacySquarePlacementPreview preview = LegacySquarePlacementPreview.Create(
                activeMissionSession,food,foodTile,cameraManager.View?.Orientation ?? 0);
            if(buildModeManager.IsBuildingPlacementActive && preview.IsValid)return;
            mapManager.ApplySquareBuildingPlacementMarker(frame, cameraManager, preview,
                LayoutManager.CityMapBounds(presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
            return;
        }
        if (selectedBuildingType is not BuildingType type || activeMissionSession is null ||
            cityHoverTile is not int tile ||
            !LegacySafetyServicePlacement.TryGetDefinition(type, out LegacySafetyServiceDefinition definition) ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return;
        if(buildModeManager.IsBuildingPlacementActive && BuildModeManager.CanPlaceSafetyService(activeMissionSession,tile))return;
        mapManager.ApplySafetyServicePlacementMarker(
            frame, cameraManager, tile, definition.ImageId,
            !BuildModeManager.CanPlaceSafetyService(activeMissionSession, tile),
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
    }

    private void ApplyWellPlacementMarker(Bitmap frame)
    {
        if (selectedBuildingType != BuildingType.Well || activeMissionSession is null ||
            cityHoverTile is not int tile ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null)
            return;
        if(buildModeManager.IsBuildingPlacementActive && BuildModeManager.CanPlaceWell(activeMissionSession,tile))return;
        int imageId = LegacyWellPlacement.GetImageId(
            activeMissionSession.LogicalChunks["random_grid"].Span[tile]);
        mapManager.ApplySafetyServicePlacementMarker(
            frame, cameraManager, tile, imageId,
            !BuildModeManager.CanPlaceWell(activeMissionSession, tile),
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
    }

    [OriginalAddress(0x00436B60, "Pending and previewed demolished buildings use RGB565 mask 0xf863")]
    private void ApplyClearLandDemolitionPreview(Bitmap frame)
    {
        if (!TryGetClearLandPlacementEndpoint(out bool dragging, out _) || !dragging)
            return;
        mapManager.ApplyClearLandDemolitionPreview(
            frame,
            cameraManager,
            buildModeManager.ClearLandPreviewBuildingIds,
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
    }

    [OriginalAddress(0x0041A180, "FUN_0041a180")]
    [OriginalAddress(0x005405B0, "FUN_005405b0")]
    private void PrepareCityPresentationFrame()
    {
        Bitmap frame = EnsureCityPresentationFrame();
        int previousWidth = presentationLogicalWidth;
        int previousHeight = presentationLogicalHeight;
        presentationLogicalWidth = frame.Width;
        presentationLogicalHeight = frame.Height;
        using (Graphics graphics = Graphics.FromImage(frame))
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.Clear(Color.Black);
            if (citySimulation is not null)
            {
                GraphicsState mapClip = graphics.Save();
                graphics.SetClip(LayoutManager.CityMapBounds(
                    presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
                mapManager.DrawMapAndWalkers(
                    graphics, cameraManager, citySimulation.Walkers);
                graphics.Restore(mapClip);
            }
            else if (mapManager.Frame is not null)
                graphics.DrawImageUnscaled(mapManager.Frame, 0, 0);
        }
        ApplyClearLandDemolitionPreview(frame);
        using (Graphics graphics = Graphics.FromImage(frame))
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            DrawHousingPlacementPreview(graphics);
            DrawHeldBuildingFootprint(graphics);
        }
        ApplyRoadPlacementMarker(frame);
        ApplyHousingPlacementMarker(frame);
        ApplyWellPlacementMarker(frame);
        ApplySafetyServicePlacementMarker(frame);
        using (Graphics graphics = Graphics.FromImage(frame))
        {
            if (cityHudState is not null)
            {
                DrawRoadPlacementPrice(graphics);
                DrawHousingPlacementPrice(graphics);
                DrawClearLandPlacementPrice(graphics);
                DrawSingleCellBuildingPlacementPrice(graphics);
                uiManager.DrawCity(graphics, uiSkin, smallFont, menuFont, normalFont, hoverFont, objectiveFont, text,
                    mapManager.MinimapFrame, cityHudState, cityBuildMenuState,
                    citySimulation?.Construction.UndoAvailable == true, buildingCosts, campaignDifficulty,
                    selectedCityCommand, cityCommandHover, openBuildMenuCommand, cityBuildMenuHover,
                    cityOptions, openCityMenu, cityMenuItemHover, cityRotationHover,
                    compactCitySidebar, citySidebarToggleHover,
                    citySidebarAnimating
                        ? LegacyCitySidebarAnimation.ExpandedPanelOffset(
                            citySidebarAnimationStep, citySidebarAnimationStartedCompact)
                        : null,
                    presentationLogicalWidth, presentationLogicalHeight);
                DrawCityStatusMessage(graphics);
                DrawMapInformation(graphics);
                if (citySettingsPage != LegacyCitySettingsPage.None)
                    optionsPanel.Draw(graphics, citySettingsPage, cityOptions, citySettingsControlHover,
                        presentationLogicalWidth, presentationLogicalHeight);
                if (citySimulation is not null && activeMissionSession is not null)
                {
                    LegacyCalendarTime calendar = timeManager.Calendar;
                    LegacyDebugOverlay.Draw(
                        graphics, smallFont, cityDebugOverlay, activeMissionSession, citySimulation,
                        cityHudState, cityHoverTile, timeManager.SimulationClock.TotalLegacyTicks,
                        calendar.MonthIndex, calendar.Year, cityOptions.GameSpeed,
                        timeManager.IsSimulationPaused,
                        LayoutManager.CityMapBounds(
                            presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar));
                }
                if (activeTutorialMessage is not null)
                {
                    TutorialPanel.Draw(graphics, uiSkin, headingFont, selectedFont,
                        activeTutorialMessage, tutorialMessageScrollLine,
                        presentationLogicalWidth, presentationLogicalHeight,
                        fileUpIcon, fileUpHoverIcon, fileDownIcon, fileDownHoverIcon,
                        scrollThumb, tutorialCloseIcon, tutorialCloseHoverIcon,
                        tutorialScrollHover, ResolveTutorialGraphic);
                }
                DrawMissionVictory(graphics);
            }
        }
        presentationLogicalWidth = previousWidth;
        presentationLogicalHeight = previousHeight;
        preparedCityPresentationReady = true;
    }

    [OriginalAddress(0x00403D70, "FUN_00403d70")]
    private void ActivateCitySidebarCommand(int command)
    {
        openBuildMenuCommand = null;
        cityBuildMenuHover = null;
        cityBuildMenuState?.RefreshRuntimePermissions();

        if (command == 12)
        {
            UndoLastConstruction();
            return;
        }

        if (command == 15)
        {
            OpenCityObjectives();
            return;
        }

        if (cityBuildMenuState is null || command >= cityBuildMenuState.Categories.Count)
        {
            selectedCityCommand = command;
            return;
        }
        if (!cityBuildMenuState.IsCommandEnabled(command))
            return;

        cityBuildMenuState.AcknowledgeCommand(command);
        selectedCityCommand = command;
        LegacyBuildMenuCategory category = cityBuildMenuState.GetCategory(command);
        if (category.OriginalCategory is 1 or 2 or 3)
        {
            selectedBuildingType = cityBuildMenuState
                .GetVerifiedAvailableEntries(command)[0].Type;
            return;
        }

        openBuildMenuCommand = command;
    }

    [OriginalAddress(0x00406010, "Expanded sidebar Undo command")]
    [OriginalAddress(0x00485230, "Undo construction callback")]
    private void UndoLastConstruction()
    {
        if (activeMissionSession is null || cityHudState is null || citySimulation is null)
            return;
        LegacyConstructionUndoResult result = citySimulation.Construction.Undo();
        if (!result.Restored)
            return;
        cityHudState = cityHudState with
        {
            Treasury = checked(cityHudState.Treasury + result.Refund)
        };
        mapManager.InvalidateTerrainTiles(result.ChangedTiles);
        citySimulation.RebuildAll();
        BuildCityMapFrame(activeMissionSession, resetView: false);
        Invalidate();
    }

    [OriginalAddress(0x00418390, "FUN_00418390")]
    private void OpenCityObjectives()
    {
        if (activeMission is null || activeMissionSession is null)
            return;
        cityObjectivesMode = true;
        briefingReturnScreen = DesktopScreen.City;
        missionControlHover = null;
        screen = DesktopScreen.CampaignBriefing;
    }

    [OriginalAddress(0x0051FD60, "FUN_0051fd60")]
    private void ToggleCitySidebar()
    {
        if (citySidebarAnimating)
            return;

        citySidebarAnimationStartedCompact = compactCitySidebar;
        citySidebarAnimationStep = 1;
        citySidebarAnimating = true;
        citySidebarToggleHover = false;
        cityCommandHover = null;
        cityHoverTile = null;
        if (!citySidebarAnimationStartedCompact && activeMissionSession is not null)
            BuildCityMapFrame(activeMissionSession, resetView: false, compactSidebarOverride: true);
        Invalidate();
    }

    [OriginalAddress(0x00425840, "FUN_00425840")]
    private void ActivateCityRotationControl(int segment)
    {
        if (cameraManager.View is null || activeMissionSession is null)
            return;

        bool changed = cameraManager.ActivateRotation(segment);
        if (!changed)
            return;

        // Rotation can select orientation-specific road/building variants in
        // LegacyGridBuilder, so it still rebuilds the logical render snapshot.
        BuildCityMapFrame(activeMissionSession, resetView: false);
        cityHoverTile = null;
        Invalidate();
    }

    [OriginalAddress(0x0051FD60, "FUN_0051fd60")]
    private void AdvanceCitySidebarAnimation()
    {
        citySidebarAnimationStep = LegacyCitySidebarAnimation.AdvanceStep(
            citySidebarAnimationStep, benchmarkDurationMilliseconds: 0);
        if (citySidebarAnimationStep < LegacyCitySidebarAnimation.FinalStep)
        {
            Invalidate();
            return;
        }

        compactCitySidebar = !citySidebarAnimationStartedCompact;
        citySidebarAnimating = false;
        if (!compactCitySidebar && activeMissionSession is not null)
            BuildCityMapFrame(activeMissionSession, resetView: false, compactSidebarOverride: false);
        Invalidate();
    }

    [RewriteOnly("Applies an OptionManager result to the desktop-owned display and audio boundaries")]
    private void ActivateCitySettingsControl(int control)
    {
        OptionChange change = optionManager.Activate(citySettingsPage, control);
        if ((change & OptionChange.ApplyDisplay) != 0)
            ApplyDisplayOptions();
        if ((change & OptionChange.RefreshAudio) != 0)
            audioManager.SetVolumes(cityOptions.MusicEnabled ? cityOptions.MusicVolume : 0,
                cityOptions.EffectsEnabled ? cityOptions.EffectsVolume : 0);
        if ((change & OptionChange.RefreshDifficulty) != 0)
        {
            campaignDifficulty = cityOptions.Difficulty;
            citySimulation?.BuildingRisks?.SetDifficulty(campaignDifficulty);
        }
        if ((change & OptionChange.ClosePanel) != 0)
            CloseCitySettings();
    }

    [RewriteOnly("Closes the managed representation of an original option state")]
    private void CloseCitySettings()
    {
        citySettingsPage = LegacyCitySettingsPage.None;
        citySettingsControlHover = null;
    }

    [OriginalAddress(0x00426100, "FUN_00426100")]
    private int? HitTestCityTopMenu(UiPoint point)
    {
        using Graphics graphics = Graphics.FromImage(standardFrame);
        return TopBar.HitTestMenu(graphics, menuFont, text, point.X, point.Y);
    }

    [OriginalAddress(0x00426150, "FUN_00426150")]
    private int? HitTestCityDropdown(int menuIndex, UiPoint point)
    {
        using Graphics graphics = Graphics.FromImage(standardFrame);
        return TopBar.HitTestDropdown(
            graphics, menuFont, text, menuIndex, point.X, point.Y);
    }

    [OriginalAddress(0x00425F20, "FUN_00425f20")]
    private void ActivateCityMenuItem(int menuIndex, int itemIndex)
    {
        LegacyCityMenuDefinition menu = LegacyCityTopMenu.GetMenu(menuIndex);
        if ((uint)itemIndex >= (uint)menu.Items.Count) return;
        LegacyCityMenuItem item = menu.Items[itemIndex];
        CloseCityMenu();
        switch (item.Action)
        {
            case LegacyCityMenuAction.NewGame:
                audioManager.StartSetupMusic();
                OpenFamilyRegistry();
                break;
            case LegacyCityMenuAction.ReplayMission:
                ReplayActiveMission();
                break;
            case LegacyCityMenuAction.LoadGame:
                OpenCityFileBrowser(LegacyFileMenuMode.LoadCity);
                break;
            case LegacyCityMenuAction.SaveGame:
                OpenCityFileBrowser(LegacyFileMenuMode.SaveCity);
                break;
            case LegacyCityMenuAction.DeleteGame:
                OpenCityFileBrowser(LegacyFileMenuMode.DeleteCity);
                break;
            case LegacyCityMenuAction.ExitGame:
                ShowQuitConfirmation();
                break;
            case LegacyCityMenuAction.DisplaySettings:
                OpenCitySettings(LegacyCitySettingsPage.Display);
                break;
            case LegacyCityMenuAction.SoundSettings:
                OpenCitySettings(LegacyCitySettingsPage.Sound);
                break;
            case LegacyCityMenuAction.SpeedSettings:
                OpenCitySettings(LegacyCitySettingsPage.Speed);
                break;
            case LegacyCityMenuAction.PopupMessages:
                OpenCitySettings(LegacyCitySettingsPage.PopupMessages);
                break;
            case LegacyCityMenuAction.Difficulty:
                OpenCitySettings(LegacyCitySettingsPage.Difficulty);
                break;
            case LegacyCityMenuAction.ToggleCityNames:
                cityOptions.EgyptianCityNames = !cityOptions.EgyptianCityNames;
                optionManager.Save();
                break;
            case LegacyCityMenuAction.ToggleAutosave:
                cityOptions.Autosave = !cityOptions.Autosave;
                optionManager.Save();
                break;
            case LegacyCityMenuAction.TogglePyramidSpeedup:
                cityOptions.PyramidSpeedup = !cityOptions.PyramidSpeedup;
                optionManager.Save();
                break;
            case LegacyCityMenuAction.CycleMouseHelp:
                cityOptions.MouseHelp = (cityOptions.MouseHelp + 1) % 3;
                optionManager.Save();
                break;
            case LegacyCityMenuAction.ToggleWarnings:
                cityOptions.Warnings = !cityOptions.Warnings;
                optionManager.Save();
                break;
            case LegacyCityMenuAction.Help:
                OpenHelp(10);
                break;
            case LegacyCityMenuAction.About:
                ShowCityNotice("Nesutera — compatible rewrite of Pharaoh (Cleopatra expansion).");
                break;
            case LegacyCityMenuAction.OpenOverseer:
                ShowCityNotice($"{text.GetString(4, item.TextIndex)} is not available in the current city milestone.");
                break;
        }
    }

    [OriginalAddress(0x004183D0, "FUN_004183d0")]
    [OriginalAddress(0x00418510, "Replay mission menu branch")]
    private async void ReplayActiveMission()
    {
        if (activeMissionSession is null)
            return;

        CancelRoadPlacement();
        LegacyMissionSession currentSession = activeMissionSession;
        LegacyMissionSession replay;
        string? replaySavePath = LegacyReplayMission.ResolveStartSavePath(currentSession.Request);
        if (replaySavePath is not null)
        {
            if (!File.Exists(replaySavePath))
            {
                notice = $"The mission cannot be replayed because {LegacyReplayMission.StartSaveFileName} is missing.";
                noticeReturnScreen = DesktopScreen.City;
                screen = DesktopScreen.Notice;
                Invalidate();
                return;
            }

            GameLoadStatus status = await loadingManager.LoadSavedGameAsync(
                replaySavePath, currentSession.Request.Difficulty);
            if (!ReferenceEquals(activeMissionSession, currentSession))
                return;
            if (status != GameLoadStatus.Completed)
            {
                notice = $"The mission could not be restarted: {loadingManager.Error?.Message}";
                noticeReturnScreen = DesktopScreen.City;
                screen = DesktopScreen.Notice;
                Invalidate();
                return;
            }
            replay = loadingManager.Session!;
        }
        else
        {
            replay = currentSession.CreateReplaySession();
        }
        string cityName = cityHudState?.CityName ?? "City";
        int? campaignMissionRank = activeMission?.MissionId;
        if (campaignMissionRank is null && replay.Request.Kind == GameLoadKind.NewCampaign)
            campaignMissionRank = replay.Request.MissionIndex;

        cityObjectivesMode = false;
        preparedCityPresentationReady = false;
        compactCitySidebar = false;
        citySidebarAnimating = false;
        citySidebarAnimationStartedCompact = false;
        citySidebarAnimationStep = 0;
        activeMissionSession = replay;
        citySimulation = CitySimulation.Create(
            replay, housingModels.GetLevels(replay.Request.Difficulty), buildingCosts,
            replay.Request.Difficulty, runtimeTextData.Figures);
        InitializeMissionProgress(replay, campaignMissionRank);
        BuildCityMapFrame(replay);
        cityHudState = LegacyCityHudState.FromLogicalChunks(replay.LogicalChunks, cityName);
        cityBuildMenuState = LegacyBuildMenuState.FromLogicalChunks(
            replay.LogicalChunks, campaignMissionRank);
        replay.EnterCity();
        ShowTutorialNotification(tutorialManager?.BeginMission());
        timeManager.ResetSimulation(cityHudState.Year, cityHudState.MonthIndex);
        selectedCityCommand = 0;
        cityCommandHover = null;
        cityHoverTile = null;
        openBuildMenuCommand = null;
        cityBuildMenuHover = null;
        selectedBuildingType = null;
        Invalidate();
    }

    [RewriteOnly("Opens the managed representation of an original option state")]
    private void OpenCitySettings(LegacyCitySettingsPage page)
    {
        citySettingsPage = page;
        citySettingsControlHover = null;
    }

    [OriginalAddress(0x00426030, "FUN_00426030")]
    private void OpenOrCloseCityMenu(int menuIndex)
    {
        openBuildMenuCommand = null;
        cityBuildMenuHover = null;
        if (openCityMenu == menuIndex) CloseCityMenu();
        else
        {
            openCityMenu = menuIndex;
            cityMenuItemHover = null;
        }
    }

    [OriginalAddress(0x00426090, "FUN_00426090")]
    [OriginalAddress(0x004260D0, "FUN_004260d0")]
    private void CloseCityMenu()
    {
        openCityMenu = null;
        cityMenuItemHover = null;
    }

    [RewriteOnly("Routes city file commands through the existing original-style file browser")]
    private void OpenCityFileBrowser(LegacyFileMenuMode mode)
    {
        fileMenuReturnScreen = DesktopScreen.City;
        fileMenu.Reset(mode);
        RefreshSaveFiles();
        screen = DesktopScreen.SaveBrowser;
    }

    [RewriteOnly("Shows an informational modal and returns it to gameplay")]
    private void ShowCityNotice(string message)
    {
        notice = message;
        noticeReturnScreen = DesktopScreen.City;
        screen = DesktopScreen.Notice;
    }

    [OriginalAddress(0x004E2970, "FUN_004e2970")]
    private void ShowCityStatusMessage(string message)
    {
        cityStatusMessage = message;
        cityStatusMessageDeadline = DateTime.UtcNow.AddSeconds(15);
    }

    [OriginalAddress(0x00417AE0, "tutorial flag, build-menu refresh and instructional message")]
    private void ShowTutorialNotification(LegacyTutorialNotification? notification)
    {
        if (notification is not LegacyTutorialNotification value)
            return;
        if (value.EventIndex < 14)
            cityBuildMenuState?.UnlockTutorialGroup(value.EventIndex);
        bool alreadyOpen = activeTutorialMessage is not null;
        tutorialMessages.Enqueue(value);
        if (alreadyOpen) return;
        activeTutorialMessage = messages.GetEntry(tutorialMessages.Current!.Value.MessageId);
        tutorialMessageScrollLine = 0;
        tutorialScrollHover = 0;
        draggingTutorialScroll = false;
        openBuildMenuCommand = null;
        cityBuildMenuHover = null;
        selectedBuildingType = null;
        buildModeManager.CancelAll();
        Invalidate();
    }

    [RewriteOnly("Resets per-session presentation state without resetting serialized tutorial flags")]
    private void InitializeMissionProgress(LegacyMissionSession session, int? rank)
    {
        tutorialMessages.Clear();
        activeTutorialMessage = null;
        tutorialManager = LegacyTutorialManager.Create(session, rank);
        missionProgress = new LegacyMissionProgress(session);
        missionVictoryHover = -1;
        missionVictoryReportShown = false;
    }

    [RewriteOnly("Displays queued tutorial instructions in order while simulation remains paused")]
    private void DismissTutorialNotification()
    {
        tutorialMessages.Dismiss();
        activeTutorialMessage = tutorialMessages.Current is { } next ? messages.GetEntry(next.MessageId) : null;
        tutorialMessageScrollLine = 0;
        tutorialScrollHover = 0;
        draggingTutorialScroll = false;
    }

    private bool IsMissionVictoryVisible => screen == DesktopScreen.City &&
        activeTutorialMessage is null && missionProgress?.PresentationPending == true;

    private bool AllowContinuedGoverning => activeMission is null || activeMission.MissionId > 1;
    private bool IsMissionVictoryReportVisible => missionVictoryReportShown || missionProgress?.ShowChoice == false;

    [OriginalAddress(0x004191F0, "victory choice panel")]
    [OriginalAddress(0x00419DB0, "victory choice controls")]
    private void DrawMissionVictory(Graphics graphics)
    {
        if (IsMissionVictoryVisible)
        {
            if (IsMissionVictoryReportVisible && activeMissionSession is not null)
                MissionVictoryPanel.DrawReport(graphics, uiSkin, headingFont, selectedFont, text,
                    presentationLogicalWidth, presentationLogicalHeight,
                    activeMissionSession, activeMission?.VictoryTextIndex, cityHudState?.CityName ?? string.Empty);
            else
                MissionVictoryPanel.Draw(graphics, uiSkin, headingFont, selectedFont, text,
                    presentationLogicalWidth, presentationLogicalHeight, AllowContinuedGoverning,
                    missionVictoryHover, DrawSegmentedButton);
        }
    }

    [OriginalAddress(0x004194C0, "victory report precedes campaign continuation")]
    [RewriteOnly("Publishes the next parsed campaign mission through the existing prepared briefing flow")]
    private void CompleteCampaignMission()
    {
        if (!IsMissionVictoryReportVisible)
        {
            missionVictoryReportShown = true;
            return;
        }
        if (activeMission is null)
        {
            screen = DesktopScreen.MainMenu;
            return;
        }
        if (familyHistoryMode && familySelection.SelectedIndex is int selected && (uint)selected < (uint)profiles.Count)
        {
            try { FamilyCampaignProgress.MarkCompleted(profiles[selected].DataPath, activeMission.MissionId); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ShowCityNotice($"Campaign progress could not be saved: {error.Message}");
                return;
            }
        }
        activeCampaign ??= campaigns.Chapters.FirstOrDefault(chapter => chapter.Missions.Contains(activeMission));
        if (activeCampaign is null) { screen = DesktopScreen.Periods; return; }
        int current = activeCampaign.Missions.ToList().IndexOf(activeMission);
        int path = activeMission.PathId;
        for (int index = current + 1; index < activeCampaign.Missions.Count; index++)
        {
            CampaignChoiceScreen? choice = activeCampaign.ChoiceScreens.FirstOrDefault(item => item.BeforeMissionIndex == index);
            if (choice is not null)
            {
                activeChoiceScreen = choice;
                campaignChoiceHover = choice.Choices.FirstOrDefault();
                screen = DesktopScreen.CampaignMap;
                return;
            }
            CampaignMission next = activeCampaign.Missions[index];
            if (next.PathId != 0 && next.PathId != path && !next.MergePathIds.Contains(path)) continue;
            activeMission = next;
            activeChoiceScreen = null;
            BeginBriefingTransition();
            return;
        }
        int chapterIndex = campaigns.Chapters.ToList().IndexOf(activeCampaign) + 1;
        if (chapterIndex < campaigns.Chapters.Count)
        {
            missionSelection.SelectCampaign(chapterIndex);
            BeginCampaign();
        }
        else screen = DesktopScreen.FamilyHub;
    }

    [OriginalAddress(0x004E2720, "FUN_004e2720")]
    [OriginalAddress(0x00537000, "FUN_00537000")]
    [OriginalAddress(0x0051F790, "FUN_0051f790 pause panel")]
    private void DrawCityStatusMessage(Graphics graphics)
    {
        string? objective = activeTutorialMessage is null &&
            tutorialManager?.CurrentObjectiveTextItem() is int textItem
            ? text.GetString(62, textItem)
            : null;
        bool hasObjective = !string.IsNullOrWhiteSpace(objective);
        bool showPause=timeManager.IsSimulationPaused && !IsInformationVisible;
        if (hasObjective)
            DrawCityStatusBanner(graphics, objective!, 460, showPause?100:30, drawObjectiveCaps: true);

        if(showPause)
        {
            int left=((presentationLogicalWidth-180)-448)/2;
            uiSkin.DrawOuterPanel(graphics,left,40,28,3);
            menuFont.DrawCenteredAt(graphics,text.GetString(13,2)??string.Empty,left+224,58);
            return;
        }

        if (!string.IsNullOrWhiteSpace(cityStatusMessage) &&
            DateTime.UtcNow < cityStatusMessageDeadline)
        {
            int measured = smallFont.Measure(cityStatusMessage);
            int width = measured < 101 ? 200 : measured < 201 ? 300 : measured <= 300 ? 400 : 460;
            DrawCityStatusBanner(
                graphics, cityStatusMessage, width, hasObjective ? 100 : 30,
                drawObjectiveCaps: false);
        }
    }

    [OriginalAddress(0x004E2720, "FUN_004e2720 persistent objective and queued status row")]
    private void DrawCityStatusBanner(
        Graphics graphics,
        string message,
        int width,
        int y,
        bool drawObjectiveCaps)
    {
        int centerX = (presentationLogicalWidth - 180) / 2;
        int left = centerX - width / 2 + 1;
        int columns = (width + 15) >> 4;
        uiSkin.DrawBuildMenuRow(graphics, left, y, columns, hovered: true);
        if (drawObjectiveCaps)
        {
            // FUN_004E2720 submits the same group-403 +16 decoration at the
            // two fixed positions flanking its 29-segment objective row.
            graphics.DrawImageUnscaled(tutorialObjectiveCap, left + 1, y + 2);
            graphics.DrawImageUnscaled(tutorialObjectiveCap, left + 429, y + 2);
        }
        selectedFont.DrawCentered(graphics, message, new Rectangle(left, y + 4, width, 16));
    }

    [RewriteOnly("Applies the selected managed window mode to the WinForms host")]
    private void ApplyDisplayOptions()
    {
        (cityOptions.DisplayWidth, cityOptions.DisplayHeight) = CityDisplaySize();
        cityPresentationFrame?.Dispose();
        cityPresentationFrame = null;
        if (activeMissionSession is not null)
            BuildCityMapFrame(activeMissionSession, resetView: false);
        if (cityOptions.FullScreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(cityOptions.DisplayWidth, cityOptions.DisplayHeight);
        }
    }

    [OriginalAddress(0x00430D30, "FUN_00430d30")]
    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    private void BuildCityMapFrame(
        LegacyMissionSession session,
        bool fullMap = false,
        bool resetView = true,
        bool? compactSidebarOverride = null)
    {
        bool compactSidebar = compactSidebarOverride ?? compactCitySidebar;
        (int frameWidth, int frameHeight) = CityDisplaySize();
        mapManager.Build(
            session, cameraManager, frameWidth, frameHeight,
            compactSidebar, fullMap, resetView, citySimulation?.Flood);
        pendingFloodTerrainTiles.Clear();
    }

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    private bool TryBeginRoadPlacement(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType != LegacyRoadPlacement.RoadType || cityHudState.Treasury < -4999 ||
            citySidebarAnimating || citySettingsPage != LegacyCitySettingsPage.None ||
            openCityMenu is not null || openBuildMenuCommand is not null ||
            !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.BeginRoadPlacement(activeMissionSession, tile))
        {
            return false;
        }

        citySimulation?.Construction.BeginAction();
        Capture = true;
        cityHoverTile = tile;
        Invalidate();
        return true;
    }

    private bool IsRoadPlacementMapPoint(Point location)
    {
        return screen == DesktopScreen.City && activeMissionSession is not null &&
            selectedBuildingType == LegacyRoadPlacement.RoadType &&
            !citySidebarAnimating && citySettingsPage == LegacyCitySettingsPage.None &&
            openCityMenu is null && openBuildMenuCommand is null &&
            TryToGame(location, out UiPoint point) &&
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y);
    }

    private bool IsHousingPlacementMapPoint(Point location)
    {
        return screen == DesktopScreen.City && activeMissionSession is not null &&
            selectedBuildingType == LegacyHousingPlacement.HousingType &&
            !citySidebarAnimating && citySettingsPage == LegacyCitySettingsPage.None &&
            openCityMenu is null && openBuildMenuCommand is null &&
            TryToGame(location, out UiPoint point) &&
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y);
    }

    private bool IsClearLandPlacementMapPoint(Point location)
    {
        return screen == DesktopScreen.City && activeMissionSession is not null &&
            selectedBuildingType == LegacyClearLandPlacement.ClearLandType &&
            !citySidebarAnimating && citySettingsPage == LegacyCitySettingsPage.None &&
            openCityMenu is null && openBuildMenuCommand is null &&
            TryToGame(location, out UiPoint point) &&
            LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y);
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    [OriginalAddress(0x004E32E0, "well requires groundwater")]
    private bool TryPlaceWell(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType != BuildingType.Well || cityHudState.Treasury < -4999 ||
            citySidebarAnimating || citySettingsPage != LegacyCitySettingsPage.None ||
            openCityMenu is not null || openBuildMenuCommand is not null ||
            !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !BuildModeManager.CanPlaceWell(activeMissionSession, tile))
            return false;

        int cost = buildingCosts.GetCost(
            activeMissionSession.Request.Difficulty, BuildingType.Well);
        citySimulation?.Construction.BeginAction();
        LegacyWellPlacementResult result =
            buildModeManager.CommitWell(activeMissionSession, tile);
        citySimulation?.RebuildHousing();
        cityHudState = cityHudState with { Treasury = checked(cityHudState.Treasury - cost) };
        citySimulation?.Construction.CommitImmediate(cost);
        mapManager.InvalidateTerrainTiles([result.Tile]);
        BuildCityMapFrame(activeMissionSession, resetView: false);
        cityHoverTile = tile;
        audioManager.PlayButton();
        Invalidate();
        return true;
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    [OriginalAddress(0x0043B630, "same orientation-dependent square origin as the preview")]
    private bool TryPlaceFoodBuilding(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType is not BuildingType type ||
            !LegacySquareBuildingPlacement.TryGetDefinition(type, out var definition) ||
            cityHudState.Treasury < -4999 || citySidebarAnimating ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null || !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar).Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int cursorTile)) return false;
        int tile = LegacySquarePlacementPreview.GetOriginTile(cursorTile,definition.Size,cameraManager.View?.Orientation ?? 0);
        if (!LegacySquareBuildingPlacement.CanPlace(activeMissionSession, definition, tile)) return false;
        int cost = buildingCosts.GetCost(activeMissionSession.Request.Difficulty, type);
        citySimulation?.Construction.BeginAction();
        LegacySquareBuildingPlacement.Commit(activeMissionSession, definition, tile, cameraManager.View?.Orientation ?? 0);
        cityHudState = cityHudState with { Treasury = checked(cityHudState.Treasury - cost) };
        citySimulation?.Construction.CommitImmediate(cost);
        mapManager.InvalidateTerrainTiles(LegacySquareBuildingPlacement.Cells(tile, definition.Size));
        BuildCityMapFrame(activeMissionSession, resetView: false);
        cityHoverTile = cursorTile; audioManager.PlayButton(); Invalidate(); return true;
    }

    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    private bool TryPlaceSafetyService(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType is not BuildingType type ||
            !LegacySafetyServicePlacement.TryGetDefinition(type, out LegacySafetyServiceDefinition definition) ||
            cityHudState.Treasury < -4999 || citySidebarAnimating ||
            citySettingsPage != LegacyCitySettingsPage.None || openCityMenu is not null ||
            openBuildMenuCommand is not null || !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !BuildModeManager.CanPlaceSafetyService(activeMissionSession, tile))
            return false;

        int cost = buildingCosts.GetCost(activeMissionSession.Request.Difficulty, type);
        citySimulation?.Construction.BeginAction();
        LegacySafetyServicePlacementResult result =
            buildModeManager.CommitSafetyService(activeMissionSession, definition, tile);
        cityHudState = cityHudState with { Treasury = checked(cityHudState.Treasury - cost) };
        citySimulation?.Construction.CommitImmediate(cost);
        mapManager.InvalidateTerrainTiles([result.Tile]);
        BuildCityMapFrame(activeMissionSession, resetView: false);
        cityHoverTile = tile;
        audioManager.PlayButton();
        Invalidate();
        return true;
    }

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    private bool TryBeginHousingPlacement(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType != LegacyHousingPlacement.HousingType || cityHudState.Treasury < -4999 ||
            citySidebarAnimating || citySettingsPage != LegacyCitySettingsPage.None ||
            openCityMenu is not null || openBuildMenuCommand is not null ||
            !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.BeginHousingPlacement(activeMissionSession, tile))
            return false;

        citySimulation?.Construction.BeginAction();
        Capture = true;
        cityHoverTile = tile;
        Invalidate();
        return true;
    }

    [OriginalAddress(0x00405C20, "FUN_00405c20")]
    private bool TryBeginClearLandPlacement(Point location)
    {
        if (screen != DesktopScreen.City || activeMissionSession is null || cityHudState is null ||
            selectedBuildingType != LegacyClearLandPlacement.ClearLandType || cityHudState.Treasury < -4999 ||
            citySidebarAnimating || citySettingsPage != LegacyCitySettingsPage.None ||
            openCityMenu is not null || openBuildMenuCommand is not null ||
            !TryToGame(location, out UiPoint point) ||
            !LayoutManager.CityMapBounds(
                presentationLogicalWidth, presentationLogicalHeight, compactCitySidebar)
                .Contains(point.X, point.Y) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.BeginClearLandPlacement(activeMissionSession, tile))
            return false;

        citySimulation?.Construction.BeginAction();
        Capture = true;
        cityHoverTile = tile;
        Invalidate();
        return true;
    }

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    private void UpdateClearLandPlacement(Point location)
    {
        if (activeMissionSession is null || !TryToGame(location, out UiPoint point) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.UpdateClearLandPlacement(tile))
            return;
        cityHoverTile = tile;
        Invalidate();
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    [OriginalAddress(0x004743F0, "FUN_004743f0")]
    [OriginalAddress(0x00474700, "FUN_00474700")]
    private void CommitClearLandPlacement(Point location)
    {
        if (activeMissionSession is null || cityHudState is null)
        {
            CancelRoadPlacement();
            return;
        }

        UpdateClearLandPlacement(location);
        LegacyClearLandResult result = buildModeManager.CommitClearLandPlacement(activeMissionSession);
        Capture = false;
        if (result.BlockReason != LegacyClearLandBlockReason.None)
        {
            citySimulation?.Construction.CancelAction();
            ShowCityStatusMessage(ClearLandBlockedMessage(result.BlockReason));
            Invalidate();
            return;
        }

        int unitCost = buildingCosts.GetCost(
            activeMissionSession.Request.Difficulty, LegacyClearLandPlacement.ClearLandType);
        int totalCost = checked(result.ClearedItemCount * unitCost);
        DemolitionDisplacement displacement =
            citySimulation?.Walkers.DisplaceDemolishedResidents(result.PendingBuildingIds)
            ?? new DemolitionDisplacement(0, 0, []);
        cityHudState = cityHudState with
        {
            Treasury = checked(cityHudState.Treasury - totalCost),
            Population = checked(cityHudState.Population - displacement.DisplacedPeople)
        };
        citySimulation?.Construction.CommitDemolition(totalCost, result.PendingBuildingIds);
        citySimulation?.RebuildHousing();
        mapManager.InvalidateTerrainTiles(result.ChangedTiles.Concat(displacement.ChangedTiles));
        BuildCityMapFrame(activeMissionSession, resetView: false);
        Invalidate();
    }

    private static string ClearLandBlockedMessage(LegacyClearLandBlockReason reason) => reason switch
    {
        LegacyClearLandBlockReason.Monument => "Monuments cannot be demolished",
        LegacyClearLandBlockReason.MilitaryStructure => "This military structure cannot be demolished",
        LegacyClearLandBlockReason.WaterStructure => "This water structure cannot be demolished",
        _ => "This structure cannot be demolished"
    };

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    [OriginalAddress(0x0046FEF0, "FUN_0046fef0")]
    private void UpdateHousingPlacement(Point location)
    {
        if (activeMissionSession is null || !TryToGame(location, out UiPoint point) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.UpdateHousingPlacement(tile))
            return;

        cityHoverTile = tile;
        Invalidate();
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    [OriginalAddress(0x00474130, "FUN_00474130")]
    private void CommitHousingPlacement(Point location)
    {
        if (activeMissionSession is null || cityHudState is null)
        {
            CancelRoadPlacement();
            return;
        }

        UpdateHousingPlacement(location);
        LegacyHousingPlacementResult result =
            buildModeManager.CommitHousingPlacement(activeMissionSession);
        int unitCost = buildingCosts.GetCost(
            activeMissionSession.Request.Difficulty, LegacyHousingPlacement.HousingType);
        int totalCost = checked(result.PlacedTiles.Count * unitCost);
        cityHudState = cityHudState with
        {
            Treasury = checked(cityHudState.Treasury - totalCost)
        };
        citySimulation?.Construction.CommitImmediate(totalCost);
        citySimulation?.RebuildHousing();
        if (result.HasPlotsWithoutRoadAccess)
        {
            string? message = text.GetString(19, 60);
            ShowCityStatusMessage(string.IsNullOrWhiteSpace(message)
                ? "Some plots are too far from a road"
                : message);
        }
        Capture = false;
        mapManager.InvalidateTerrainTiles(result.PlacedTiles);
        BuildCityMapFrame(activeMissionSession, resetView: false);
        Invalidate();
    }

    [OriginalAddress(0x00405D90, "FUN_00405d90")]
    private void UpdateRoadPlacement(Point location)
    {
        if (activeMissionSession is null || !TryToGame(location, out UiPoint point) ||
            !cameraManager.TryHitTest(point.X, point.Y, out int tile) ||
            !buildModeManager.UpdateRoadPlacement(tile))
        {
            return;
        }

        cityHoverTile = tile;
        Invalidate();
    }

    [OriginalAddress(0x00405E90, "FUN_00405e90")]
    [OriginalAddress(0x004728A0, "FUN_004728a0")]
    private void CommitRoadPlacement(Point location)
    {
        if (activeMissionSession is null || cityHudState is null)
        {
            CancelRoadPlacement();
            return;
        }

        UpdateRoadPlacement(location);
        LegacyRoadPlacementResult result = buildModeManager.CommitRoadPlacement(activeMissionSession);
        int unitCost = buildingCosts.GetCost(
            activeMissionSession.Request.Difficulty, LegacyRoadPlacement.RoadType);
        int totalCost = checked(result.PlacedTiles.Count * unitCost);
        cityHudState = cityHudState with
        {
            Treasury = checked(cityHudState.Treasury - totalCost)
        };
        citySimulation?.Construction.CommitImmediate(totalCost);
        Capture = false;
        BuildCityMapFrame(activeMissionSession, resetView: false);
        Invalidate();
    }

    private void CancelRoadPlacement()
    {
        pendingRoadPointerDown = null;
        pendingHousingPointerDown = null;
        pendingClearLandPointerDown = null;
        bool buildingActive=buildModeManager.IsBuildingPlacementActive;
        buildModeManager.CancelBuildingPlacement();
        bool roadActive = buildModeManager.IsRoadPlacementActive;
        bool housingActive = buildModeManager.IsHousingPlacementActive;
        bool clearLandActive = buildModeManager.IsClearLandPlacementActive;
        if (!roadActive && !housingActive && !clearLandActive && !buildingActive)
        {
            citySimulation?.Construction.CancelAction();
            Capture = false;
            return;
        }
        if (roadActive)
            buildModeManager.CancelRoadPlacement();
        if (housingActive)
            buildModeManager.CancelHousingPlacement();
        if (clearLandActive)
            buildModeManager.CancelClearLandPlacement();
        citySimulation?.Construction.CancelAction();
        Capture = false;
        Invalidate();
    }

    /// <summary>
    /// Reconstructs the original minimap click path without its full-grid scan:
    /// one bounded lookup identifies the sampled tile, then CenterOnTile applies
    /// the verified camera clamp from FUN_00430CD0.
    /// </summary>
    [OriginalAddress(0x004061E0, "FUN_004061e0")]
    [OriginalAddress(0x0043EF10, "FUN_0043ef10")]
    private bool TryCenterCityFromMinimap(UiPoint point)
    {
        if (compactCitySidebar || cameraManager.View is null || activeMissionSession is null ||
            mapManager.MinimapProjection is null)
        {
            return false;
        }

        Rectangle bounds = Minimap.Bounds(presentationLogicalWidth);
        if (!bounds.Contains(point.X, point.Y))
            return false;

        int x = point.X - bounds.Left;
        int y = point.Y - bounds.Top;
        if (cameraManager.CenterOnMinimapPixel(mapManager.MinimapProjection, x, y))
        {
            cityHoverTile = null;
            mapManager.RefreshViewport(cameraManager);
        }

        // The black/outside portion still belongs to the minimap and must not
        // activate a command button beneath it.
        Invalidate();
        return true;
    }

    [RewriteOnly("Handles city Escape semantics before the global quit action")]
    private bool HandleCityKey(Keys key)
    {
        LegacyDebugOverlayMode requestedDebugOverlay = key switch
        {
            Keys.F1 => LegacyDebugOverlayMode.Walker,
            Keys.F2 => LegacyDebugOverlayMode.Network,
            Keys.F3 => LegacyDebugOverlayMode.Evolution,
            Keys.F4 => LegacyDebugOverlayMode.Building,
            _ => LegacyDebugOverlayMode.None
        };
        if (requestedDebugOverlay != LegacyDebugOverlayMode.None)
        {
            cityDebugOverlay = cityDebugOverlay == requestedDebugOverlay
                ? LegacyDebugOverlayMode.None
                : requestedDebugOverlay;
            Invalidate();
            return true;
        }
        if (InputManager.ResolveTimeAction(key) is CityTimeAction timeAction)
        {
            switch (timeAction)
            {
                case CityTimeAction.TogglePause:
                    timeManager.ToggleSimulationPause();
                    break;
                case CityTimeAction.DecreaseSpeed:
                    optionManager.AdjustGameSpeed(-10);
                    break;
                case CityTimeAction.IncreaseSpeed:
                    optionManager.AdjustGameSpeed(10);
                    break;
            }
            Invalidate();
            return true;
        }
        if (key == Keys.Escape && citySettingsPage != LegacyCitySettingsPage.None)
        {
            CloseCitySettings();
            Invalidate();
            return true;
        }
        if (key == Keys.Escape && openCityMenu is not null)
        {
            CloseCityMenu();
            Invalidate();
            return true;
        }
        return false;
    }

    [OriginalAddress(0x004CF660, "FUN_004cf660")]
    [OriginalAddress(0x004DDD60, "FUN_004ddd60")]
    [OriginalAddress(0x004DDDE0, "FUN_004ddde0")]
    private void UpdateContinuousCityCamera()
    {
        bool inputEnabled = screen == DesktopScreen.City && activeMissionSession is not null &&
            !citySidebarAnimating && citySettingsPage == LegacyCitySettingsPage.None &&
            openCityMenu is null && activeTutorialMessage is null && !IsInformationVisible && inputManager.WindowActive;
        Rectangle screenBounds = presentationBounds.Width > 0 && presentationBounds.Height > 0
            ? RectangleToScreen(presentationBounds)
            : Rectangle.Empty;
        bool moved = cameraManager.UpdateContinuous(
            inputEnabled, cityOptions.ScrollSpeed, inputManager, screenBounds, out _);
        if (!moved)
            return;

        mapManager.RefreshViewport(cameraManager);
        cityHoverTile = null;
        Invalidate();
        // Keyboard auto-repeat can keep the Win32 message queue populated and
        // postpone WM_PAINT until the key is released. Publish every completed
        // camera step synchronously so held-key scrolling remains visibly
        // continuous, as it is in the original renderer.
        Update();
    }

    private CampaignChoice? HitCampaignChoice(UiPoint point)
    {
        if (activeChoiceScreen is null || point.Y >= 400) return null;
        return activeChoiceScreen.Choices.FirstOrDefault(choice =>
            point.X >= choice.X - 18 && point.X < choice.X + 18 &&
            point.Y >= choice.Y - 18 && point.Y < choice.Y + 18);
    }

    [OriginalAddress(0x00516630, "FUN_00516630")]
    [OriginalAddress(0x00516720, "FUN_00516720")]
    private void DrawFamilies(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 128, 40, 24, 23);
        uiSkin.DrawInnerPanel(graphics, 144, 120, 20, 13);
        uiSkin.DrawInnerPanel(graphics, 467, 146, 2, 10);
        headingFont.DrawCentered(graphics, text.GetString(292, 3) ?? "Family list", new Rectangle(128, 58, 384, 40));
        int first = familySelection.FirstVisibleIndex;
        for (int row = 0; row < FamilySelection.VisibleRows && first + row < profiles.Count; row++)
        {
            UiRect bounds = new(160, 128 + row * 16, 448, 144 + row * 16);
            bool selected = familySelection.SelectedIndex == first + row || familyHover?.FamilyIndex == first + row;
            (selected ? selectedFont : normalFont).DrawAt(graphics, profiles[first + row].Name,
                bounds.Left, bounds.Top);
        }
        foreach ((FamilySelectionAction action, int index) in FamilySelection.ActionTextIndices)
        {
            UiRect bounds = action switch
            {
                FamilySelectionAction.Create => new(142, 332, 267, 357),
                FamilySelectionAction.Delete => new(276, 332, 406, 357),
                FamilySelectionAction.Confirm => new(418, 332, 498, 357),
                _ => new(196, 362, 446, 387)
            };
            bool selected = familyHover?.Action == action;
            uiSkin.DrawButton(graphics, new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height), selected);
            menuFont.DrawCentered(graphics, text.GetString(FamilySelection.TextGroup, index) ?? action.ToString(),
                new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        }
        if (profiles.Count > FamilySelection.VisibleRows)
        {
            int range = profiles.Count - FamilySelection.VisibleRows;
            int offset = range == 0 ? 0 : familySelection.FirstVisibleIndex * FamilySelection.ScrollTrackRange / range;
            graphics.DrawImageUnscaled(scrollThumb, 472, FamilySelection.ScrollTrackTop + offset);
        }
        graphics.DrawImageUnscaled(familyHover?.Action == FamilySelectionAction.ScrollUp ? fileUpHoverIcon : fileUpIcon,
            464, 120);
        graphics.DrawImageUnscaled(familyHover?.Action == FamilySelectionAction.ScrollDown ? fileDownHoverIcon : fileDownIcon,
            464, 300);
    }

    private UiRect FamilyScrollThumbBounds()
    {
        if (profiles.Count <= FamilySelection.VisibleRows) return new UiRect(0, 0, 0, 0);
        int range = profiles.Count - FamilySelection.VisibleRows;
        int offset = familySelection.FirstVisibleIndex * FamilySelection.ScrollTrackRange / range;
        return new UiRect(472, FamilySelection.ScrollTrackTop + offset,
            472 + scrollThumb.Width, FamilySelection.ScrollTrackTop + offset + scrollThumb.Height);
    }

    private void PrewarmFamilyRegistry()
    {
        // Family panels and bitmap glyphs are decoded lazily. Doing this once before
        // the first input keeps Main menu -> Family Registry free of a first-paint stall.
        using Graphics graphics = Graphics.FromImage(standardFrame);
        graphics.Clear(Color.Black);
        graphics.DrawImageUnscaled(familyBackground, 0, 0);
        DrawFamilies(graphics);
    }

    private void DrawNameEntry(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 128, 160, 24, 8);
        headingFont.DrawCentered(graphics,
            text.GetString(FamilyNameEntry.TitleTextGroup, FamilyNameEntry.TitleTextIndex) ?? "Family name",
            new Rectangle(128, 172, 384, 32));
        uiSkin.DrawInnerPanel(graphics, 160, 208, 20, 2);
        selectedFont.DrawAt(graphics, familyNameInput, 176, 216);
        if (nameCaretVisible)
        {
            int caretX = 176 + selectedFont.Measure(graphics, familyNameInput);
            using Pen caret = new(Color.White);
            graphics.DrawLine(caret, caretX, 230, caretX + 4, 230);
        }
        menuFont.DrawCentered(graphics, text.GetString(13, 8) ?? "Egyptian name", new Rectangle(145, 256, 180, 17));
        uiSkin.DrawButton(graphics, new Rectangle(352, 246, 25, 25),
            familyNameEntryHover == FamilyNameEntryAction.EgyptianName);
        menuFont.DrawAt(graphics, text.GetString(13, 5) ?? "Continue", 395, 255);
        graphics.DrawImageUnscaled(familyNameEntryHover == FamilyNameEntryAction.Continue
            ? missionPlayHoverIcon : missionPlayIcon, 464, 249);
    }

    [OriginalAddress(0x00520D00, "FUN_00520d00")]
    private void DrawEgyptianNames(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 110, 30, 26, 28);
        menuFont.DrawCentered(graphics, text.GetString(FamilyNameEntry.EgyptianNamesTextGroup, 0) ?? "Male Names",
            new Rectangle(115, 44, 96, 18));
        menuFont.DrawCentered(graphics, text.GetString(FamilyNameEntry.EgyptianNamesTextGroup, 21) ?? "Female Names",
            new Rectangle(340, 44, 96, 18));
        for (int index = 1; index <= 41; index++)
        {
            if (index == FamilyNameEntry.FemaleHeadingIndex) continue;
            UiRect bounds = FamilyNameEntry.EgyptianNameBounds(index);
            string name = text.GetString(FamilyNameEntry.EgyptianNamesTextGroup, index) ?? string.Empty;
            LegacyBitmapFont font = selectedEgyptianName == index ? objectiveFont :
                egyptianNameHover == index ? hoverFont : normalFont;
            font.DrawAt(graphics, name, bounds.Left, bounds.Top);
        }
        UiRect ok = FamilyNameEntry.EgyptianNamesOkButton;
        uiSkin.DrawButton(graphics, new Rectangle(ok.Left, ok.Top, ok.Width, ok.Height), egyptianNamesOkHover);
        menuFont.DrawCentered(graphics, text.GetString(18, 3) ?? "OK",
            new Rectangle(ok.Left, ok.Top, ok.Width, ok.Height));
    }

    private void DrawDeleteFamilyConfirmation(Graphics graphics)
    {
        DrawFamilies(graphics);
        uiSkin.DrawOuterPanel(graphics, 80, 80, 30, 10);
        headingFont.DrawCentered(graphics, text.GetString(5, 90) ?? "Delete Dynasty?", new Rectangle(96, 92, 448, 32));
        normalFont.DrawWrapped(graphics,
            text.GetString(5, 91) ?? "This deletes the selected dynasty and all of its saved games. Do you really want to do that?",
            new Rectangle(112, 136, 416, 48));
        graphics.DrawImageUnscaled(deleteConfirmationHover == false ? missionExitHoverIcon : missionExitIcon,
            DeleteNoBounds.Left, DeleteNoBounds.Top);
        graphics.DrawImageUnscaled(deleteConfirmationHover == true ? fileProceedHoverIcon : fileProceedIcon,
            DeleteYesBounds.Left, DeleteYesBounds.Top);
    }

    private void DrawNoMissionsConfirmation(Graphics graphics)
    {
        DrawFamilyHub(graphics);
        uiSkin.DrawOuterPanel(graphics, 8, 192, 39, 7);
        headingFont.DrawCentered(graphics, text.GetString(5, 141) ?? "No Missions Won By Family",
            new Rectangle(24, 198, 592, 30));
        normalFont.DrawAt(graphics,
            text.GetString(5, 142) ?? "Please 'Begin Family History' if you are new to Pharaoh. Continue to choose mission?", 16, 232);
        foreach ((bool answer, UiRect bounds, int textIndex) in new[]
        {
            (true, NoMissionsYesBounds, 1),
            (false, NoMissionsNoBounds, 0)
        })
        {
            Rectangle rectangle = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            uiSkin.DrawButton(graphics, rectangle, noMissionsConfirmationHover == answer);
            menuFont.DrawCentered(graphics, text.GetString(18, textIndex) ?? (answer ? "Yes" : "No"), rectangle);
        }
    }

    [OriginalAddress(0x00538940, "FUN_00538940")]
    private void DrawQuitConfirmation(Graphics graphics)
    {
        DesktopScreen modalScreen = screen;
        try
        {
            screen = quitConfirmationReturnScreen;
            DrawScreenContent(graphics);
        }
        finally
        {
            screen = modalScreen;
        }

        // The executable derives this 18x7 panel from the wider of text group 5:0/1.
        uiSkin.DrawOuterPanel(graphics, 184, 190, 18, 7);
        uiSkin.DrawInnerPanel(graphics, 192, 195, 17, 2);
        headingFont.DrawCentered(graphics, text.GetString(5, 1) ?? "Leave the Kingdom?",
            new Rectangle(200, 200, 272, 30));
        normalFont.DrawCentered(graphics, text.GetString(5, 0) ?? "Quit", new Rectangle(189, 238, 282, 20));

        foreach ((bool answer, UiRect bounds, int textIndex) in new[]
        {
            (true, QuitYesBounds, 1),
            (false, QuitNoBounds, 0)
        })
        {
            Rectangle rectangle = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            uiSkin.DrawButton(graphics, rectangle, quitConfirmationHover == answer);
            menuFont.DrawCentered(graphics, text.GetString(18, textIndex) ?? (answer ? "Yes" : "No"), rectangle);
        }
    }

    private void ShowQuitConfirmation()
    {
        quitConfirmationReturnScreen = screen;
        quitConfirmationHover = null;
        screen = DesktopScreen.QuitConfirmation;
        Invalidate();
    }

    private void DeleteSelectedFamily()
    {
        if (familySelection.SelectedIndex is not int selected || (uint)selected >= (uint)profiles.Count)
        {
            OpenFamilyRegistry();
            return;
        }

        FamilyProfile profile = profiles[selected];
        try
        {
            FamilyProfileStore.Delete(installationRoot, profile);
            profiles.RemoveAt(selected);
            familySelection.RemoveSelected(profiles.Count);
            OpenFamilyRegistry();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            notice = error.Message;
            noticeReturnScreen = DesktopScreen.Families;
            screen = DesktopScreen.Notice;
        }
    }

    private void CreateFamily()
    {
        try
        {
            string? templatePath = profiles.FirstOrDefault(profile => File.Exists(profile.DataPath))?.DataPath;
            FamilyProfile created = FamilyProfileStore.Create(installationRoot, familyNameInput, templatePath);
            profiles.Add(created);
            profiles.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
            familySelection.Select(profiles.FindIndex(profile => profile == created), profiles.Count);
            familyNameInput = string.Empty;
            selectedEgyptianName = null;
            screen = DesktopScreen.FamilyHub;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            notice = error is IOException && File.Exists(Path.Combine(installationRoot, "Save", familyNameInput + ".dat"))
                ? $"{text.GetString(5, 92) ?? "Dynasty Exists"}\n{text.GetString(5, 93) ?? "Choose another name."}"
                : error.Message;
            noticeReturnScreen = DesktopScreen.NameEntry;
            screen = DesktopScreen.Notice;
        }
    }

    private void ResetNameCaret()
    {
        nameCaretVisible = true;
        timeManager.NameCaretDeadline = DateTime.UtcNow.AddMilliseconds(500);
    }

    [OriginalAddress(0x004CB4D0, "FUN_004cb4d0")]
    private void DrawFamilyHub(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 128, 40, 24, 21);
        string familyName = familySelection.SelectedIndex is int selected && selected < profiles.Count ? profiles[selected].Name : string.Empty;
        string title = (text.GetString(FamilyHub.TextGroup, FamilyHub.TitleTextIndex) ?? "[player_name] family")
            .Replace("[player_name]", familyName, StringComparison.OrdinalIgnoreCase);
        headingFont.DrawCentered(graphics, title, new Rectangle(140, 60, 368, 45));
        foreach (FamilyHubItem item in VisibleFamilyHubItems())
        {
            Rectangle bounds = new(item.Bounds.Left, item.Bounds.Top, item.Bounds.Width, item.Bounds.Height);
            uiSkin.DrawButton(graphics, bounds, familyHubHover == item);
            menuFont.DrawCentered(graphics, text.GetString(FamilyHub.TextGroup, item.TextIndex) ?? item.Action.ToString(), bounds);
        }
    }

    [OriginalAddress(0x0041BE10, "FUN_0041be10")]
    [OriginalAddress(0x0041E5C0, "FUN_0041e5c0")]
    private void DrawMissions(Graphics graphics)
    {
        if (screen is DesktopScreen.CustomMissions or DesktopScreen.MissionEditor)
        {
            DrawCustomMissions(graphics, screen == DesktopScreen.MissionEditor);
            return;
        }

        // Explore History is one visual screen in the original. States 0x17 and
        // 0x3D switch only the tab contents and both retain this shared heading
        // and Pharaoh_Unloaded #658 background.
        headingFont.DrawCenteredAt(graphics,
            familyHistoryMode ? SelectedFamilyTitle() : text.GetString(293, 6) ?? "Explore History", 320, 17);
        if (missionSelection.Mode == MissionSelectionMode.Campaigns)
        {
            DrawCampaigns(graphics);
            return;
        }

        int selected = missions.Count == 0 ? 0 : missionSelection.SelectedIndex;
        int chapter = FindCampaignChapterIndex(selected);
        if ((uint)chapter < (uint)campaignThumbnails.Length)
            graphics.DrawImageUnscaled(campaignThumbnails[chapter], 78, 56);

        Rectangle individualTab = new(32, 208, 160, 25);
        Rectangle campaignTab = new(195, 208, 93, 25);
        uiSkin.DrawButton(graphics, individualTab, true);
        uiSkin.DrawButton(graphics, campaignTab, missionControlHover == MissionControl.CampaignTab);
        menuFont.DrawCentered(graphics, text.GetString(294, 38) ?? "Individual Missions", individualTab);
        menuFont.DrawCentered(graphics, text.GetString(294, 39) ?? "Campaigns", campaignTab);

        // The original state 0x3D composes this inset dynamically; it is not
        // baked into the shared #658 background used by the campaigns tab.
        uiSkin.DrawInnerPanel(graphics, 15, 237, 16, 14);

        int first = missionSelection.FirstVisibleIndex;
        for (int row = 0; row < MissionSelection.VisibleRows && first + row < missions.Count; row++)
        {
            int index = first + row;
            LegacyBitmapFont font = index == selected || index == missionHover ? selectedFont : normalFont;
            font.DrawAt(graphics, missions[index].Name, 24, 249 + row * 16);
        }

        string selectedName = missions.Count == 0 ? "No missions found" : missions[selected].Name;
        CampaignMission? selectedMission = FindCampaignMission(selected);
        MessageEntry? selectedMessage = selectedMission is null ? null : messages.GetEntry(selectedMission.BriefingMessageId);
        normalFont.DrawCentered(graphics, selectedName, new Rectangle(345, 60, 260, 20));
        string missionTitle = (uint)selected < (uint)missionArchive.Metadata.Count
            ? missionArchive.Metadata[selected].Title
            : selectedMessage?.Caption ?? string.Empty;
        selectedFont.DrawCentered(graphics, missionTitle, new Rectangle(345, 82, 260, 20));
        if ((uint)selected < (uint)missionArchive.StartYears.Count)
            normalFont.DrawCentered(graphics, FormatScenarioYear(missionArchive.StartYears[selected]), new Rectangle(345, 103, 260, 20));
        bool won = SelectedFamilyProgress().CompletedMissionIds.Contains(selected);
        if (showMissionObjectives)
        {
            DrawIndividualMissionObjectives(graphics, selected);
            DrawCustomDifficultySelector(graphics);
        }
        else
            normalFont.DrawWrapped(graphics, won
                    ? selectedMessage?.Text ?? string.Empty
                    : text.GetString(305, 0) ?? "Your family has never faced this challenge.",
                new Rectangle(345, 125, 260, 260));

        DrawMissionScroll(graphics, missionSelection, missions.Count);

        Rectangle resultsBounds = new(354, 410, 250, 23);
        uiSkin.DrawButton(graphics, resultsBounds, missionControlHover == MissionControl.Results);
        int toggleText = showMissionObjectives ? 220 : 221;
        normalFont.DrawCentered(graphics, text.GetString(44, toggleText) ??
            (showMissionObjectives ? "Show Prior Results" : "Show Mission Objectives"), resultsBounds);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.Exit ? missionExitHoverIcon : missionExitIcon, 335, 440);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.Play ? missionPlayHoverIcon : missionPlayIcon, 600, 440);
        menuFont.DrawAt(graphics, text.GetString(44, 217) ?? "Exit", 380, 446);
        menuFont.DrawAt(graphics, text.GetString(44, 215) ?? "Play this mission", 460, 446);
    }

    private void DrawCustomMissions(Graphics graphics, bool editorMode)
    {
        int selected = customMissions.Count == 0 ? 0 : customMissionSelection.SelectedIndex;
        string selectedName = customMissions.Count == 0 ? "No custom missions found" : customMissions[selected].Name;
        CustomMissionMetadata? metadata = GetCustomMissionMetadata(selectedName);
        if (customMissions.Count > 0)
        {
            int thumbnail = metadata is not null && (uint)metadata.ThumbnailMissionId < (uint)missionThumbnails.Length
                ? metadata.ThumbnailMissionId
                : FindMissionThumbnail(selectedName);
            if ((uint)thumbnail < (uint)missionThumbnails.Length)
                graphics.DrawImageUnscaled(missionThumbnails[thumbnail], 78, 36);
        }

        // FUN_0041e5c0 composes the list well and scrollbar track over the
        // decorative custom-mission backdrop.
        uiSkin.DrawInnerPanel(graphics, 15, 200, 16, 16);

        int first = customMissionSelection.FirstVisibleIndex;
        for (int row = 0; row < CustomVisibleRows && first + row < customMissions.Count; row++)
        {
            int index = first + row;
            LegacyBitmapFont font = index == selected || index == missionHover ? selectedFont : normalFont;
            font.DrawAt(graphics, customMissions[index].Name, 24, 215 + row * 16);
        }

        normalFont.DrawCentered(graphics, selectedName, new Rectangle(345, 60, 265, 20));
        selectedFont.DrawCentered(graphics, metadata?.Title ?? string.Empty, new Rectangle(345, 88, 265, 20));
        if (metadata is not null)
        {
            normalFont.DrawCentered(graphics, FormatScenarioYear(metadata.StartYear), new Rectangle(345, 108, 265, 20));
            normalFont.DrawCentered(graphics, text.GetString(44, 77 + Math.Clamp(metadata.Climate, 0, 2)) ?? "Normal climate",
                new Rectangle(345, 123, 265, 20));
            int mapSize = Math.Clamp((metadata.MapWidth - 50) / 30, 0, 5);
            normalFont.DrawCentered(graphics, text.GetString(44, 121 + mapSize) ?? "Landscape",
                new Rectangle(345, 140, 265, 20));
            int military = metadata.InvasionCount switch { <= 0 => 0, <= 2 => 1, <= 4 => 2, <= 10 => 3, _ => 4 };
            normalFont.DrawCentered(graphics, text.GetString(44, 112 + military) ?? "Military activity",
                new Rectangle(345, 157, 265, 20));
            normalFont.DrawCentered(graphics, text.GetString(32, 29) ?? "Very hard mission",
                new Rectangle(345, 174, 265, 20));
        }
        DrawCustomMissionObjectives(graphics, metadata);
        DrawCustomMissionScroll(graphics, customMissionSelection, customMissions.Count);
        if (!editorMode) DrawCustomDifficultySelector(graphics);

        graphics.DrawImageUnscaled(missionControlHover == MissionControl.Exit ? missionExitHoverIcon : missionExitIcon, 335, 440);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.Play ? missionPlayHoverIcon : missionPlayIcon, 600, 440);
        menuFont.DrawAt(graphics, text.GetString(44, editorMode ? 214 : 136) ??
            (editorMode ? "Edit this map" : "Go to city"), editorMode ? 490 : 510, 446);
    }

    private void DrawCustomMissionScroll(Graphics graphics, MissionSelection selection, int count)
    {
        uiSkin.DrawInnerPanel(graphics, 286, 234, 2, 12);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.ScrollUp ? fileUpHoverIcon : fileUpIcon,
            CustomScrollUpBounds.Left, CustomScrollUpBounds.Top);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.ScrollDown ? fileDownHoverIcon : fileDownIcon,
            CustomScrollDownBounds.Left, CustomScrollDownBounds.Top);
        int range = Math.Max(0, count - CustomVisibleRows);
        int offset = range == 0 ? 0 : selection.FirstVisibleIndex * 160 / range;
        graphics.DrawImageUnscaled(scrollThumb, 289, 241 + offset);
    }

    private UiRect CustomMissionScrollThumbBounds()
    {
        int range = Math.Max(0, customMissions.Count - CustomVisibleRows);
        int offset = range == 0 ? 0 : customMissionSelection.FirstVisibleIndex * 160 / range;
        return new UiRect(289, 241 + offset, 289 + scrollThumb.Width, 241 + offset + scrollThumb.Height);
    }

    private void DrawMissionScroll(Graphics graphics, MissionSelection selection, int count)
    {
        uiSkin.DrawInnerPanel(graphics, 286, 274, 2, 10);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.ScrollUp ? fileUpHoverIcon : fileUpIcon,
            MissionSelection.ScrollUpBounds.Left, MissionSelection.ScrollUpBounds.Top);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.ScrollDown ? fileDownHoverIcon : fileDownIcon,
            MissionSelection.ScrollDownBounds.Left, MissionSelection.ScrollDownBounds.Top);
        int range = Math.Max(0, count - MissionSelection.VisibleRows);
        int offset = range == 0 ? 0 : selection.FirstVisibleIndex * 132 / range;
        graphics.DrawImageUnscaled(scrollThumb, 289, 277 + offset);
    }

    private UiRect MissionScrollThumbBounds()
    {
        int range = Math.Max(0, missions.Count - MissionSelection.VisibleRows);
        int offset = range == 0 ? 0 : missionSelection.FirstVisibleIndex * 132 / range;
        return new UiRect(289, 277 + offset, 289 + scrollThumb.Width, 277 + offset + scrollThumb.Height);
    }

    private void DrawIndividualMissionObjectives(Graphics graphics, int missionId)
    {
        if ((uint)missionId >= (uint)missionArchive.Entries.Count) return;
        CampaignMissionObjectives objectives = missionArchive[missionId];
        CampaignMissionMetadata metadata = missionArchive.Metadata[missionId];
        normalFont.DrawCentered(graphics, text.GetString(44, 77 + Math.Clamp(metadata.Climate, 0, 2)) ?? "Normal climate",
            new Rectangle(345, 125, 260, 20));
        int mapSize = Math.Clamp((metadata.MapWidth - 50) / 30, 0, 5);
        normalFont.DrawCentered(graphics, text.GetString(44, 121 + mapSize) ?? "Landscape",
            new Rectangle(345, 145, 260, 20));
        int military = metadata.InvasionCount switch { <= 0 => 0, <= 2 => 1, <= 4 => 2, <= 10 => 3, _ => 4 };
        normalFont.DrawCentered(graphics, text.GetString(44, 112 + military) ?? "Military activity",
            new Rectangle(345, 165, 260, 20));
        normalFont.DrawCentered(graphics, text.GetString(32, metadata.DifficultyTextIndex) ?? "Trivial mission",
            new Rectangle(345, 185, 260, 20));
        selectedFont.DrawCentered(graphics, text.GetString(44, 127) ?? "Win conditions",
            new Rectangle(345, 217, 260, 20));

        void DrawCondition(string value, int y) =>
            normalFont.DrawCentered(graphics, value, new Rectangle(345, y, 260, 20));
        if (objectives.CultureEnabled)
            DrawCondition($"{objectives.CultureRating} {text.GetString(44, 129) ?? "Culture"}", 245);
        if (objectives.ProsperityEnabled)
            DrawCondition($"{objectives.ProsperityRating} {text.GetString(44, 130) ?? "Prosperity"}", 261);
        if (objectives.KingdomEnabled)
            DrawCondition($"{objectives.KingdomRating} {text.GetString(44, 132) ?? "Kingdom"}", 277);
        if (objectives.PopulationEnabled)
            DrawCondition($"{objectives.PopulationTarget} {text.GetString(44, 133) ?? "People"}", 293);
        if (objectives.RequiredHouseCount > 0)
            DrawCondition($"{objectives.RequiredHouseCount} {text.GetString(29, objectives.HouseLevel + 20) ?? "Residences"}", 309);
        if (objectives.SurvivalEnabled)
            DrawCondition($"{objectives.SurvivalYears} {text.GetString(44, 135) ?? "Survival years"}", 325);
        for (int index = 0; index < metadata.MonumentIds.Count; index++)
            DrawCondition(text.GetString(198, metadata.MonumentIds[index]) ?? string.Empty, 341 + index * 16);
    }

    private int FindCampaignChapterIndex(int missionId)
    {
        for (int index = 0; index < campaigns.Chapters.Count; index++)
            if (campaigns.Chapters[index].Missions.Any(mission => mission.MissionId == missionId)) return index;
        return 0;
    }

    private int FindMissionThumbnail(string name)
    {
        if (name.StartsWith("Alexandria3", StringComparison.CurrentCultureIgnoreCase)) return 17;
        if (name.StartsWith("Alexandria2", StringComparison.CurrentCultureIgnoreCase)) return 16;
        if (name.StartsWith("Alexandria", StringComparison.CurrentCultureIgnoreCase)) return 15;
        for (int index = 0; index < missions.Count; index++)
            if (missions[index].Name.StartsWith(name, StringComparison.CurrentCultureIgnoreCase) ||
                name.StartsWith(missions[index].Name, StringComparison.CurrentCultureIgnoreCase))
                return index;
        return 0;
    }

    private CampaignMission? FindCampaignMission(int missionId) =>
        campaigns.Chapters.SelectMany(chapter => chapter.Missions)
            .FirstOrDefault(mission => mission.MissionId == missionId);

    private string LoadCustomMissionDescription(string name)
    {
        string path = Path.Combine(installationRoot, "Maps", name + ".txt");
        if (!File.Exists(path)) return string.Empty;
        try { return File.ReadAllText(path, legacyEncoding); }
        catch (IOException) { return string.Empty; }
    }

    private CustomMissionMetadata? GetCustomMissionMetadata(string name)
    {
        if (customMissionMetadata.TryGetValue(name, out CustomMissionMetadata? metadata)) return metadata;
        string path = Path.Combine(installationRoot, "Maps", name + ".map");
        try { metadata = CustomMissionMetadata.Load(path, legacyEncoding); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            metadata = null;
        }
        customMissionMetadata[name] = metadata;
        return metadata;
    }

    private void DrawCustomMissionObjectives(Graphics graphics, CustomMissionMetadata? metadata)
    {
        if (metadata is null) return;
        CampaignMissionObjectives objectives = metadata.Objectives;
        selectedFont.DrawCentered(graphics, text.GetString(44, 127) ?? "Win Conditions", new Rectangle(345, 211, 265, 20));
        List<string> labels = [];
        if (objectives.CultureEnabled) labels.Add($"{objectives.CultureRating} Culture");
        if (objectives.ProsperityEnabled) labels.Add($"{objectives.ProsperityRating} Prosperity");
        if (objectives.KingdomEnabled) labels.Add($"{objectives.KingdomRating} Kingdom");
        if (objectives.PopulationEnabled) labels.Add($"{objectives.PopulationTarget} People");
        if (objectives.RequiredHouseCount > 0)
            labels.Add($"{objectives.RequiredHouseCount} {text.GetString(29, objectives.HouseLevel + 20) ?? "Residences"}");
        if (objectives.SurvivalEnabled)
            labels.Add($"{objectives.SurvivalYears} {text.GetString(44, 135) ?? "Survival years"}");
        for (int index = 0; index < labels.Count; index++)
            normalFont.DrawAt(graphics, labels[index], 416, 241 + index * 17);

        if (metadata.MonumentIds.Count == 0)
            normalFont.DrawCentered(graphics, text.GetString(198, 0) ?? "None", new Rectangle(345, 340, 265, 18));
        else
            for (int index = 0; index < Math.Min(3, metadata.MonumentIds.Count); index++)
                normalFont.DrawCentered(graphics, text.GetString(198, metadata.MonumentIds[index]) ?? string.Empty,
                    new Rectangle(345, 340 + index * 17, 265, 18));
    }

    private static string FormatScenarioYear(int year) => year < 0 ? $"{-year} BC" : $"{year} AD";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0051:Remove unused private members")]
    private void DrawCampaigns(Graphics graphics)
    {
        int selected = missionSelection.SelectedCampaign;
        graphics.DrawImageUnscaled(campaignThumbnails[selected], 78, 56);

        if (!familyHistoryMode)
        {
            Rectangle individualTab = new(32, 208, 160, 25);
            Rectangle campaignTab = new(195, 208, 93, 25);
            uiSkin.DrawButton(graphics, individualTab, missionControlHover == MissionControl.IndividualTab);
            uiSkin.DrawButton(graphics, campaignTab, true);
            menuFont.DrawCentered(graphics, text.GetString(294, 38) ?? "Individual Missions", individualTab);
            menuFont.DrawCentered(graphics, text.GetString(294, 39) ?? "Campaigns", campaignTab);
        }

        menuFont.DrawAt(graphics, text.GetString(294, 41) ?? "Pharaoh", 30, 266);
        menuFont.DrawAt(graphics, text.GetString(294, 42) ?? "Cleopatra", 30, 366);
        foreach (CampaignPeriod period in MissionSelection.CampaignPeriods)
        {
            bool currentPeriod = period.Id == availableFamilyPeriod;
            Rectangle bounds = new(period.Bounds.Left, period.Bounds.Top, period.Bounds.Width, period.Bounds.Height);
            DrawSegmentedButton(graphics, period.Bounds, currentPeriod && missionHover == period.Id);
            (currentPeriod ? menuFont : objectiveFont).DrawCentered(graphics,
                text.GetString(27, period.TextIndex) ?? $"Campaign {period.Id + 1}", bounds);
        }

        campaignTitleFont.DrawCentered(graphics, text.GetString(294, selected * 4) ?? string.Empty,
            new Rectangle(339, 60, 283, 23));
        normalFont.DrawWrapped(graphics, text.GetString(294, selected * 4 + 2) ?? string.Empty,
            new Rectangle(347, 116, 269, 300));

        if (!familyHistoryMode)
        {
            graphics.DrawImageUnscaled(missionControlHover == MissionControl.Exit ? missionExitHoverIcon : missionExitIcon, 335, 440);
            menuFont.DrawAt(graphics, text.GetString(44, 217) ?? "Exit", 380, 445);
        }
        if (!familyHistoryMode || selected == availableFamilyPeriod)
        {
            graphics.DrawImageUnscaled(missionControlHover == MissionControl.Play ? missionPlayHoverIcon : missionPlayIcon, 600, 440);
            menuFont.DrawAt(graphics, text.GetString(294, familyHistoryMode ? 36 : 37) ?? (familyHistoryMode ? "Begin" : "Play"), 550, 445);
        }
    }

    private string SelectedFamilyTitle()
    {
        if (familySelection.SelectedIndex is not int selected || (uint)selected >= (uint)profiles.Count)
            return text.GetString(293, 6) ?? "Explore History";
        string familyName = profiles[selected].Name;
        return (text.GetString(FamilyHub.TextGroup, FamilyHub.TitleTextIndex) ?? "[player_name] family")
            .Replace("[player_name]", familyName, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<FamilyHubItem> VisibleFamilyHubItems()
    {
        if (familySelection.SelectedIndex is not int selected || (uint)selected >= (uint)profiles.Count)
            return FamilyHub.VisibleItems(false, false);
        FamilyProfile profile = profiles[selected];
        bool hasSavedGames = fileManager.FindMostRecentFamilySave(profile.Name) is not null;
        FamilyCampaignProgress progress = FamilyCampaignProgress.Load(profile.DataPath);
        return FamilyHub.VisibleItems(hasSavedGames, hasSavedGames || progress.CompletedMissionCount > 0);
    }

    private FamilyCampaignProgress SelectedFamilyProgress()
    {
        if (familySelection.SelectedIndex is int selected && (uint)selected < (uint)profiles.Count)
            return FamilyCampaignProgress.Load(profiles[selected].DataPath);
        return new FamilyCampaignProgress(0, 0, new HashSet<int>());
    }

    [OriginalAddress(0x0041F870, "FUN_0041f870")]
    private void OpenMissionReplay()
    {
        familyHistoryMode = false;
        availableFamilyPeriod = SelectedFamilyProgress().NextPeriod;
        missionSelection.SetMode(MissionSelectionMode.Campaigns);
        missionSelection.SelectCampaign(0);
        screen = DesktopScreen.Periods;
    }

    private void OpenFamilyHistory()
    {
        familyHistoryMode = true;
        availableFamilyPeriod = 0;
        if (familySelection.SelectedIndex is int selected && (uint)selected < (uint)profiles.Count)
            availableFamilyPeriod = FamilyCampaignProgress.Load(profiles[selected].DataPath).NextPeriod;
        missionSelection.SetMode(MissionSelectionMode.Campaigns);
        missionSelection.SelectCampaign(availableFamilyPeriod);
        screen = DesktopScreen.Periods;
    }

    [OriginalAddress(0x004CB800, "FUN_004cb800")]
    private void ResumeFamilyHistory()
    {
        if (familySelection.SelectedIndex is not int selected || (uint)selected >= (uint)profiles.Count)
        {
            OpenFamilyHistory();
            return;
        }

        string? savePath = fileManager.FindMostRecentFamilySave(profiles[selected].Name);
        if (savePath is null)
        {
            OpenFamilyHistory();
            return;
        }

        LoadSelectedCity(savePath, DesktopScreen.FamilyHub);
    }

    [OriginalAddress(0x0041AAA0, "FUN_0041aaa0")]
    private void DrawCampaignMap(Graphics graphics)
    {
        if (activeChoiceScreen is null) return;
        if ((uint)activeChoiceScreen.GraphicId < (uint)campaignChoiceScreens.Length)
            graphics.DrawImageUnscaled(campaignChoiceScreens[activeChoiceScreen.GraphicId], 0, 0);

        string title = text.GetString(144, activeChoiceScreen.TitleTextIndex) ?? activeCampaign?.Key ?? string.Empty;
        menuFont.DrawAt(graphics, title, 20, 410);
        CampaignChoice? selected = campaignChoiceHover ?? activeChoiceScreen.Choices.FirstOrDefault();
        if (selected is not null)
        {
            string description = text.GetString(144, selected.TextIndex) ?? string.Empty;
            normalFont.DrawWrapped(graphics, description, new Rectangle(20, 438, 560, 36));
        }
    }

    [OriginalAddress(0x0041A180, "FUN_0041a180")]
    [OriginalAddress(0x00418F70, "FUN_00418f70")]
    private void DrawCampaignBriefing(Graphics graphics)
    {
        if (activeMission is null) return;
        PrepareCampaignMissionBriefing();
        MessageEntry? message = messages.GetEntry(activeMission.BriefingMessageId);
        if (message is null) return;

        uiSkin.DrawOuterPanel(graphics, 16, 16, 38, 28);
        headingFont.DrawAt(graphics, message.Title, 32, 30);
        menuFont.DrawAt(graphics, message.Caption, 32, 58);

        uiSkin.DrawInnerPanel(graphics, 32, 80, 36, 6);
        selectedFont.DrawAt(graphics, text.GetString(62, 10) ?? "Objectives", 48, 84);
        DrawMissionObjectives(graphics);

        uiSkin.DrawInnerPanel(graphics, 32, 184, 33, 15);
        selectedFont.DrawBriefingParagraphs(graphics, message.Text, new Rectangle(48, 196, 544, 224));

        if (!cityObjectivesMode && briefingReturnScreen is not null)
        {
            graphics.DrawImageUnscaled(missionControlHover == MissionControl.Exit ? missionExitHoverIcon : missionExitIcon,
                CampaignBriefingBackBounds.Left, CampaignBriefingBackBounds.Top);
            menuFont.DrawAt(graphics, text.GetString(44, 217) ?? "Back", 62, 430);
        }
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.Play ? missionPlayHoverIcon : missionPlayIcon,
            CampaignBriefingPlayBounds.Left, CampaignBriefingPlayBounds.Top);
        menuFont.DrawAt(graphics, text.GetString(62, 7) ?? "To the city", 492, 433);
        DrawDifficultySelector(graphics);
    }

    private void DrawMissionObjectives(Graphics graphics)
    {
        if (activeMission is null || (uint)activeMission.MissionId >= (uint)missionArchive.Entries.Count) return;
        CampaignMissionObjectives objectives = missionArchive[activeMission.MissionId];
        List<string> labels = [];
        if (objectives.PopulationEnabled)
            labels.Add($"{text.GetString(62, 11) ?? "Population of"} {objectives.PopulationTarget}");
        if (objectives.RequiredHouseCount != 0)
            labels.Add($"{objectives.RequiredHouseCount} {text.GetString(29, objectives.HouseLevel + 20) ?? "Residences"}");

        int[] ratings =
        [
            objectives.CultureRating,
            objectives.ProsperityRating,
            objectives.MonumentRating,
            objectives.KingdomRating
        ];
        bool[] ratingEnabled =
        [
            objectives.CultureEnabled,
            objectives.ProsperityEnabled,
            objectives.MonumentEnabled,
            objectives.KingdomEnabled
        ];
        for (int index = 0; index < ratings.Length; index++)
        {
            if (ratingEnabled[index])
                labels.Add($"{text.GetString(62, 12 + index) ?? "Rating"} {ratings[index]}");
        }

        Point[] positions =
        [
            new(48, 106), new(304, 106),
            new(48, 128), new(304, 128),
            new(48, 150), new(304, 150)
        ];
        for (int index = 0; index < Math.Min(labels.Count, positions.Length); index++)
        {
            Rectangle bounds = new(positions[index].X, positions[index].Y, 240, 20);
            DrawObjectiveBox(graphics, bounds);
            objectiveFont.DrawAt(graphics, labels[index], bounds.X + 8, bounds.Y - 1);
        }

        if (activeMission.MissionId == 0)
        {
            DrawObjectiveBox(graphics, new Rectangle(48, 150, 544, 20));
            objectiveFont.DrawAt(graphics,
                text.GetString(62, 21) ?? "Create an area of housing, and watch immigrants arrive", 56, 149);
        }
    }

    private void DrawObjectiveBox(Graphics graphics, Rectangle bounds)
    {
        int segments = Math.Max(2, bounds.Width / 16);
        for (int segment = 0; segment < segments; segment++)
        {
            Bitmap tile = objectiveTiles[segment == 0 ? 0 : segment == segments - 1 ? 2 : 1];
            graphics.DrawImageUnscaled(tile, bounds.Left + segment * 16, bounds.Top);
        }
    }

    private void DrawDifficultySelector(Graphics graphics)
    {
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.DifficultyUp ? difficultyUpHoverIcon : difficultyUpIcon, 126, 432);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.DifficultyDown ? difficultyDownHoverIcon : difficultyDownIcon, 143, 432);
        string label = text.GetString(44, 216) ?? "Difficulty Level";
        menuFont.DrawAt(graphics, label, 166, 433);
        menuFont.DrawAt(graphics, text.GetString(153, campaignDifficulty + 1) ?? "Normal",
            170 + menuFont.Measure(graphics, label), 433);
    }

    private void DrawCustomDifficultySelector(Graphics graphics)
    {
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.DifficultyUp ? difficultyUpHoverIcon : difficultyUpIcon,
            CustomDifficultyUpBounds.Left, CustomDifficultyUpBounds.Top);
        graphics.DrawImageUnscaled(missionControlHover == MissionControl.DifficultyDown ? difficultyDownHoverIcon : difficultyDownIcon,
            CustomDifficultyDownBounds.Left, CustomDifficultyDownBounds.Top);
        string label = text.GetString(44, 216) ?? "Difficulty Level";
        normalFont.DrawAt(graphics, label, 408, 393);
        normalFont.DrawAt(graphics, text.GetString(153, campaignDifficulty + 1) ?? "Normal",
            412 + normalFont.Measure(graphics, label), 393);
    }

    [OriginalAddress(0x004C5660, "FUN_004c5660")]
    private void DrawHighScores(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 0, 0, 40, 30);
        uiSkin.DrawInnerPanel(graphics, 32, 96, 34, 22);
        headingFont.DrawCenteredAt(graphics, text.GetString(296, 0) ?? "Eternal Houses of Egypt", 320, 32);
        menuFont.DrawCenteredAt(graphics, text.GetString(296, 1) ?? "Mission", 128, 80);
        menuFont.DrawCenteredAt(graphics, text.GetString(296, 2) ?? "Best Family", 272, 80);
        menuFont.DrawCenteredAt(graphics, text.GetString(296, 3) ?? "Difficulty Level", 416, 80);
        menuFont.DrawCenteredAt(graphics, text.GetString(296, 4) ?? "Score", 544, 80);
        normalFont.DrawAt(graphics, text.GetString(296, 6) ?? "There are no high scores.", 48, 104);
        menuFont.DrawCenteredAt(graphics, text.GetString(13, 1) ?? "Right-click to continue", 320, 452);
    }

    [OriginalAddress(0x00532340, "FUN_00532340")]
    private void DrawFileMenu(Graphics graphics)
    {
        uiSkin.DrawOuterPanel(graphics, 128, 40, 24, 21);
        uiSkin.DrawInnerPanel(graphics, 144, 80, 20, 2);
        uiSkin.DrawInnerPanel(graphics, 144, 120, 20, 13);
        uiSkin.DrawInnerPanel(graphics, 467, 144, 2, 10);
        headingFont.DrawCentered(graphics, text.GetString(43, fileMenu.TitleTextIndex) ?? "Loading saved game",
            new Rectangle(160, 50, 304, 30));
        selectedFont.DrawAt(graphics, fileMenu.Input, 160, 90);

        int first = fileMenu.FirstVisibleIndex;
        for (int row = 0; row < LegacyFileMenu.VisibleRows && first + row < saveFiles.Count; row++)
        {
            int index = first + row;
            bool active = fileMenu.SelectedIndex == index || fileHover?.FileIndex == index;
            string name = Path.GetFileNameWithoutExtension(saveFiles[index]);
            (active ? selectedFont : normalFont).DrawAt(graphics, name, 160, 130 + row * 16);
        }

        int range = Math.Max(0, saveFiles.Count - LegacyFileMenu.VisibleRows);
        int offset = range == 0 ? 0 : fileMenu.FirstVisibleIndex * 130 / range;
        graphics.DrawImageUnscaled(scrollThumb, 472, 145 + offset);
        graphics.DrawImageUnscaled(missionExitIcon, 344, 335);
        graphics.DrawImageUnscaled(fileProceedIcon, 392, 335);
        graphics.DrawImageUnscaled(fileUpIcon, 464, 120);
        graphics.DrawImageUnscaled(fileDownIcon, 464, 300);
        menuFont.DrawAt(graphics, text.GetString(43, 5) ?? "Proceed?", 224, 342);
    }

    private void DrawNotice(Graphics graphics)
    {
        DrawPanel(graphics, new UiRect(96, 160, 544, 300));
        using Font font = new("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush brush = new(Color.FromArgb(255, 236, 210, 145));
        using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(notice, font, brush, new RectangleF(116, 180, 408, 100), format);
    }

    private static void DrawTitle(Graphics graphics, string title, float size)
    {
        using Font font = new("Georgia", size, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush shadow = new(Color.FromArgb(190, 0, 0, 0));
        using SolidBrush gold = new(Color.FromArgb(255, 226, 181, 71));
        using StringFormat format = new() { Alignment = StringAlignment.Center };
        graphics.DrawString(title, font, shadow, new PointF(323, 28), format);
        graphics.DrawString(title, font, gold, new PointF(320, 25), format);
    }

    private static void DrawPanel(Graphics graphics, UiRect bounds)
    {
        using SolidBrush fill = new(Color.FromArgb(225, 61, 38, 20));
        using Pen light = new(Color.FromArgb(255, 220, 181, 100), 2);
        using Pen dark = new(Color.FromArgb(255, 56, 28, 10), 2);
        graphics.FillRectangle(fill, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        graphics.DrawLine(light, bounds.Left, bounds.Bottom, bounds.Left, bounds.Top);
        graphics.DrawLine(light, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
        graphics.DrawLine(dark, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom);
        graphics.DrawLine(dark, bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
    }

    private static void DrawButton(Graphics graphics, UiRect bounds, string label, bool selected)
    {
        DrawPanel(graphics, bounds);
        using SolidBrush overlay = new(selected ? Color.FromArgb(90, 255, 211, 86) : Color.FromArgb(30, 0, 0, 0));
        graphics.FillRectangle(overlay, bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, bounds.Height - 4);
        using Font font = new("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush textBrush = new(selected ? Color.White : Color.FromArgb(255, 239, 214, 153));
        using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, textBrush, new RectangleF(bounds.Left, bounds.Top, bounds.Width, bounds.Height), format);
    }

    private static void DrawListRow(Graphics graphics, UiRect bounds, string label, bool selected)
    {
        using SolidBrush background = new(selected ? Color.FromArgb(180, 151, 93, 32) : Color.FromArgb(130, 32, 18, 8));
        using SolidBrush textBrush = new(Color.FromArgb(255, 238, 216, 166));
        using Font font = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.FillRectangle(background, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        graphics.DrawString(label, font, textBrush, bounds.Left + 5, bounds.Top - 1);
    }

    private static MissionControl? HitMissionControl(UiPoint point, bool custom, bool individualObjectives = false)
    {
        if (!custom && MissionSelection.IndividualTabBounds.Contains(point)) return MissionControl.IndividualTab;
        if (!custom && MissionSelection.CampaignTabBounds.Contains(point)) return MissionControl.CampaignTab;
        if ((custom ? CustomScrollUpBounds : MissionSelection.ScrollUpBounds).Contains(point)) return MissionControl.ScrollUp;
        if ((custom ? CustomScrollDownBounds : MissionSelection.ScrollDownBounds).Contains(point)) return MissionControl.ScrollDown;
        if (MissionSelection.ExitBounds.Contains(point)) return MissionControl.Exit;
        if (MissionSelection.PlayBounds.Contains(point)) return MissionControl.Play;
        if (!custom && MissionSelection.ResultsBounds.Contains(point)) return MissionControl.Results;
        if ((custom || individualObjectives) && CustomDifficultyUpBounds.Contains(point)) return MissionControl.DifficultyUp;
        if ((custom || individualObjectives) && CustomDifficultyDownBounds.Contains(point)) return MissionControl.DifficultyDown;
        return null;
    }

    private void ClearPointerHover()
    {
        bool changed = mainMenu.MovePointer(new UiPoint(-1, -1)) || familyHover is not null || familyHubHover is not null ||
            familyNameEntryHover is not null || egyptianNameHover is not null || egyptianNamesOkHover ||
            deleteConfirmationHover is not null || noMissionsConfirmationHover is not null ||
            missionHover is not null || missionControlHover is not null || fileHover is not null || campaignChoiceHover is not null ||
            cityHoverTile is not null || cityCommandHover is not null || cityBuildMenuHover is not null || citySidebarToggleHover ||
            cityMenuItemHover is not null || cityRotationHover is not null ||
            citySettingsControlHover is not null || tutorialScrollHover != 0;
        familyHover = null;
        familyHubHover = null;
        familyNameEntryHover = null;
        egyptianNameHover = null;
        egyptianNamesOkHover = false;
        deleteConfirmationHover = null;
        noMissionsConfirmationHover = null;
        missionHover = null;
        missionControlHover = null;
        fileHover = null;
        campaignChoiceHover = null;
        cityHoverTile = null;
        cityCommandHover = null;
        cityBuildMenuHover = null;
        citySidebarToggleHover = false;
        cityMenuItemHover = null;
        cityRotationHover = null;
        citySettingsControlHover = null;
        tutorialScrollHover = 0;
        if (changed) Invalidate();
    }

    [RewriteOnly("Clears city-only pointer state while the original screen-state-2 sidebar transition owns input")]
    private void ClearCityPointerHover()
    {
        bool changed = cityHoverTile is not null || cityCommandHover is not null || cityBuildMenuHover is not null ||
            citySidebarToggleHover || cityMenuItemHover is not null || cityRotationHover is not null;
        cityHoverTile = null;
        cityCommandHover = null;
        cityBuildMenuHover = null;
        citySidebarToggleHover = false;
        cityMenuItemHover = null;
        cityRotationHover = null;
        if (changed)
            Invalidate();
    }

    private void ActivateCustomMissionScreen(UiPoint point, bool editorMode)
    {
        if (!editorMode && CustomDifficultyUpBounds.Contains(point))
            campaignDifficulty = Math.Min(4, campaignDifficulty + 1);
        else if (!editorMode && CustomDifficultyDownBounds.Contains(point))
            campaignDifficulty = Math.Max(0, campaignDifficulty - 1);
        else if (CustomScrollUpBounds.Contains(point))
            customMissionSelection.Scroll(-1, customMissions.Count, CustomVisibleRows);
        else if (CustomScrollDownBounds.Contains(point))
            customMissionSelection.Scroll(1, customMissions.Count, CustomVisibleRows);
        else if (customMissionSelection.HitRow(point, customMissions.Count, CustomListBounds, CustomVisibleRows) is int index)
            customMissionSelection.Select(index, customMissions.Count, CustomVisibleRows);
        else if (MissionSelection.ExitBounds.Contains(point))
            screen = editorMode ? DesktopScreen.MainMenu : DesktopScreen.FamilyHub;
        else if (MissionSelection.PlayBounds.Contains(point) && customMissions.Count > 0)
        {
            notice = editorMode
                ? $"Map „{customMissions[customMissionSelection.SelectedIndex].Name}“ is selected; the city editor runtime is not reconstructed yet."
                : $"Custom mission „{customMissions[customMissionSelection.SelectedIndex].Name}“ is selected; city loading follows the game-state reconstruction.";
            noticeReturnScreen = editorMode ? DesktopScreen.MissionEditor : DesktopScreen.CustomMissions;
            screen = DesktopScreen.Notice;
        }
    }

    private bool TryToGame(Point clientPoint, out UiPoint point)
    {
        point = default;
        if (!presentationBounds.Contains(clientPoint)) return false;
        point = new UiPoint(
            (clientPoint.X - presentationBounds.X) * presentationLogicalWidth / presentationBounds.Width,
            (clientPoint.Y - presentationBounds.Y) * presentationLogicalHeight / presentationBounds.Height);
        return true;
    }

    private static Rectangle Fit(Rectangle available, int width, int height)
    {
        float scale = Math.Min(available.Width / (float)width, available.Height / (float)height);
        int resultWidth = Math.Max(1, (int)(width * scale));
        int resultHeight = Math.Max(1, (int)(height * scale));
        return new Rectangle((available.Width - resultWidth) / 2, (available.Height - resultHeight) / 2, resultWidth, resultHeight);
    }

    private Bitmap LoadImage(string root, string archiveName, int index)
        => RgbaBitmap.Create(graphicsAssets.Decode(archiveName, index));

    [OriginalAddress(0x004C820A, "@G message graphic resolves relative to the tutorial image bank")]
    private Bitmap? ResolveTutorialGraphic(int graphic)
    {
        if (graphic is < 50 or > 78)
            return null;
        if (!tutorialGraphics.TryGetValue(graphic, out Bitmap? bitmap))
        {
            // Pharaoh_Unloaded records 345..373 are Tutorial1..Tutorial4;
            // @G50 therefore resolves to local image 345.
            bitmap = LoadImage(installationRoot, "Pharaoh_Unloaded.sg3", 295 + graphic);
            tutorialGraphics.Add(graphic, bitmap);
        }
        return bitmap;
    }
}
