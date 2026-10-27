using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;
using System.Buffers.Binary;

namespace Pharaoh.Engine.Tutorials;

public readonly record struct LegacyTutorialNotification(
    int EventIndex,
    int MessageId,
    IReadOnlyList<BuildingType> UnlockedBuildings);

/// <summary>The 26-entry tutorial table at DAT_005794A8 and its saved flag array.</summary>
public sealed class LegacyTutorialManager
{
    private sealed record Step(int MissionRank, int MessageId, BuildingType[] Buildings);

    private static readonly Step[] Steps =
    [
        new(0, 252, [BuildingType.Firehouse]),
        new(0, 239, [BuildingType.HuntingLodge, BuildingType.Granary, BuildingType.Bazaar]),
        new(0, 255, [BuildingType.TransportWharf]),
        new(0, 253, [BuildingType.ArchitectPost]),
        new(1, 258, [BuildingType.TempleToOsiris, BuildingType.Temples, BuildingType.Sphinx, BuildingType.None]),
        new(1, 240, [BuildingType.Booth, BuildingType.JugglerSchool]),
        new(2, 250, [BuildingType.Apothecary, BuildingType.Physician]),
        new(2, 259, [BuildingType.ClayPit, BuildingType.RawMaterials, BuildingType.Potter, BuildingType.StorageYard]),
        new(2, 251, []),
        new(2, 256, [BuildingType.Gardens, BuildingType.SmallStatue, BuildingType.MediumStatue,
            BuildingType.LargeStatue, BuildingType.Plaza, BuildingType.Beautification,
            BuildingType.FerryLanding, BuildingType.WaterCrossings, BuildingType.Roadblock]),
        new(3, 310, [BuildingType.TaxCollector, BuildingType.PersonalMansion]),
        new(4, 254, [BuildingType.ScribalSchool, BuildingType.PapyrusMaker, BuildingType.FigFarm]),
        new(4, 260, []),
        new(4, 283, [BuildingType.PyramidMonument, BuildingType.BricklayersGuild]),
        new(4, 282, []),
        new(0, 249, []), new(1, 241, []), new(2, 242, []), new(3, 243, []),
        new(4, 244, []), new(5, 245, []), new(6, 246, []), new(7, 247, []),
        new(8, 248, []), new(9, 284, []), new(10, 285, [])
    ];

    private readonly LegacyMissionSession session;
    private readonly int? missionRank;
    private readonly Memory<byte> flags;
    private readonly Dictionary<int, (short Stock, short Walker)> schoolState = [];

    private LegacyTutorialManager(LegacyMissionSession session, int? missionRank)
    {
        this.session = session;
        this.missionRank = missionRank;
        flags = session.GetWritableLogicalChunk("tutorial_flags_struct");
        ObserveScribalSchools();
    }

    public static LegacyTutorialManager Create(LegacyMissionSession session, int? missionRank) =>
        new(session, missionRank);

    /// <summary>
    /// The mission PAK stores every flag as one. Starting a campaign does not
    /// use those bytes verbatim: Pharaoh marks earlier-rank events complete and
    /// clears events belonging to the current and later tutorial ranks.
    /// Saved games bypass this initialization and retain their serialized state.
    /// </summary>
    [OriginalAddress(0x005405B0, "new-campaign tutorial flag initialization")]
    public static void InitializeNewCampaign(LegacyMissionSession session, int missionRank)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Request.Kind != GameLoadKind.NewCampaign)
            throw new InvalidOperationException("Tutorial defaults apply only to a new campaign mission.");

        Memory<byte> state = session.GetWritableLogicalChunk("tutorial_flags_struct");
        int eventCount = Math.Min(26, state.Length);
        if (missionRank is >= 0 and < 11)
        {
            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
                state.Span[eventIndex] = Steps[eventIndex].MissionRank < missionRank ? (byte)1 : (byte)0;
        }
        else
        {
            state.Span[..eventCount].Fill(1);
        }
        session.ReplaceLogicalChunk("tutorial_flags_struct", state.ToArray());
    }

    [OriginalAddress(0x00417B40, "mission-start tutorial messages 15..25")]
    public LegacyTutorialNotification? BeginMission()
    {
        if (missionRank is not int rank || rank is < 0 or > 10)
            return null;
        return Complete(15 + rank);
    }

    [OriginalAddress(0x00468FF0, "first building fire unlocks tutorial event 0")]
    public LegacyTutorialNotification? NotifyFireStarted() => Complete(0);

    [OriginalAddress(0x00468DA4, "first building collapse unlocks tutorial event 3")]
    public LegacyTutorialNotification? NotifyBuildingCollapsed() => Complete(3);

    [OriginalAddress(0x0042FB30, "disease removes residents from a house")]
    public LegacyTutorialNotification? NotifyDiseaseDeath() => Complete(6);

    [OriginalAddress(0x0044CDC0, "the pottery request becomes active")]
    public LegacyTutorialNotification? NotifyRequestActivated(int resource, int eventKind) =>
        resource == 13 && eventKind == 1 ? Complete(8) : null;

    [OriginalAddress(0x00462180, "scribal school consumes papyrus and dispatches its teacher")]
    public LegacyTutorialNotification? NotifyScribalSchoolInstruction() =>
        IsComplete(11) ? Complete(12) : null;

    [OriginalAddress(0x00409FA0, "first entry into the world map after the scribal-school instruction")]
    public LegacyTutorialNotification? NotifyWorldMapOpened() =>
        IsComplete(12) ? Complete(14) : null;

    /// <summary>
    /// Checks persisted simulation values, not the wording of the current banner.
    /// Fire, collapse, disease, request activation and instruction dispatch also
    /// have explicit event entry points matching their original call sites.
    /// </summary>
    [OriginalAddress(0x00452370, "daily population tutorial check")]
    [OriginalAddress(0x0045D630, "operational granary tutorial checks")]
    [OriginalAddress(0x0045AAE0, "warehouse resource tutorial checks")]
    [OriginalAddress(0x0045FE20, "cumulative gold production tutorial check")]
    [OriginalAddress(0x004E9890, "religion tutorial check")]
    [OriginalAddress(0x004EA2C0, "housing tutorial check during mission evaluation")]
    public IReadOnlyList<LegacyTutorialNotification> Advance(long firstLegacyTick, long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        if (legacyTicks == 0 || missionRank is not (>= 0 and <= 4))
            return [];
        ReadOnlySpan<byte> city = session.LogicalChunks["city_data"].Span;
        List<LegacyTutorialNotification> notifications = [];
        void Add(LegacyTutorialNotification? notification)
        {
            if (notification is { } value) notifications.Add(value);
        }
        bool daily = (firstLegacyTick + legacyTicks) / LegacySimulationClock.DispatcherTicksPerDay >
            firstLegacyTick / LegacySimulationClock.DispatcherTicksPerDay;
        switch (missionRank)
        {
            case 0:
                if (daily && LegacyMissionGoals.ReadInt(city, 18940) > 149) Add(Complete(1));
                if (IsComplete(1) && LegacyMissionGoals.ReadInt(city, 37428) >= 400)
                    Add(Complete(2));
                break;
            case 1:
                if (LegacyMissionGoals.ReadInt(city, 36024) > 499) Add(Complete(4));
                if (IsComplete(4) && unchecked((sbyte)city[36214]) > 39) Add(Complete(5));
                break;
            case 2:
                if (LegacyMissionGoals.ReadInt(city, 37420) > 799) Add(Complete(7));
                if (LegacyMissionGoals.ReadInt(city, 37628) > 199) Add(Complete(9));
                Add(ObserveDiseaseDeaths());
                Add(ObserveRequests());
                break;
            case 3:
                if (LegacyMissionGoals.ReadInt(city, 37636) > 299) Add(Complete(10));
                break;
            case 4:
                if (IsComplete(10) && LegacyMissionGoals.CountQualifyingHousing(session, 9) > 0)
                    Add(Complete(11));
                Add(ObserveScribalSchools());
                if (IsComplete(12) && LegacyMissionGoals.ReadInt(city, 37624) > 799)
                    Add(Complete(13));
                break;
        }
        return notifications;
    }

    [OriginalAddress(0x0042FB30, "persisted disease-death marker on an emptied house")]
    private LegacyTutorialNotification? ObserveDiseaseDeaths()
    {
        if (missionRank != 2 || IsComplete(6)) return null;
        ReadOnlySpan<byte> buildings = session.LogicalChunks["buildings"].Span;
        for (int offset = 264; offset + 264 <= buildings.Length; offset += 264)
            if (buildings[offset] != 0 && buildings[offset + 5] != 0 && buildings[offset + 135] == 1 &&
                BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 28)..]) == 0 &&
                BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 240)..]) == 2)
                return NotifyDiseaseDeath();
        return null;
    }

    [OriginalAddress(0x0044BA90, "124-byte scenario event records")]
    [OriginalAddress(0x0044CDC0, "active pottery request")]
    private LegacyTutorialNotification? ObserveRequests()
    {
        if (missionRank != 2 || IsComplete(8)) return null;
        ReadOnlySpan<byte> events = session.LogicalChunks["scenario_events"].Span;
        int count = Math.Clamp(LegacyMissionGoals.ReadInt(events, 0), 0, 150);
        for (int index = 0; index < count; index++)
        {
            int offset = 4 + index * 124;
            if (offset + 52 > events.Length) break;
            if (events[offset + 2] == 1 && events[offset + 50] != 0 &&
                BinaryPrimitives.ReadInt16LittleEndian(events[(offset + 4)..]) == 13)
                return NotifyRequestActivated(13, 1);
        }
        return null;
    }

    [OriginalAddress(0x00462180, "papyrus consumption and new teacher walker at a scribal school")]
    private LegacyTutorialNotification? ObserveScribalSchools()
    {
        if (missionRank != 4) return null;
        ReadOnlySpan<byte> buildings = session.LogicalChunks["buildings"].Span;
        LegacyTutorialNotification? notification = null;
        for (int offset = 264; offset + 264 <= buildings.Length; offset += 264)
        {
            if (buildings[offset] != 1 || BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]) != 51)
            { schoolState.Remove(offset); continue; }
            short stock = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 60)..]);
            short walker = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 42)..]);
            if (schoolState.TryGetValue(offset, out var previous) && stock < previous.Stock &&
                walker > 0 && walker != previous.Walker)
                notification ??= NotifyScribalSchoolInstruction();
            schoolState[offset] = (stock, walker);
        }
        return notification;
    }

    /// <summary>
    /// Selects the persistent tutorial objective rendered above the city.
    /// The returned value is an item in localized text group 62.
    /// </summary>
    [OriginalAddress(0x004E2530, "FUN_004e2530")]
    public int? CurrentObjectiveTextItem()
    {
        if (missionRank is not int rank || rank is < 0 or > 4)
            return null;
        if (rank == 4 && LegacyMissionGoals.ReadInt(session.LogicalChunks["city_data"].Span, 36012) ==
            LegacyMissionGoals.ReadInt(session.LogicalChunks["scenario_info"].Span, 852))
            return null;
        return rank switch
        {
            0 when IsComplete(0) && CountBuildings(BuildingType.Firehouse) < 1 => 17,
            0 when IsComplete(3) && CountBuildings(BuildingType.ArchitectPost) < 1 => 18,
            0 when IsComplete(2) => 20,
            0 when IsComplete(1) => 19,
            0 => 21,
            1 when IsComplete(5) => 22,
            1 when IsComplete(4) => 23,
            1 => 24,
            2 when IsComplete(6) && CountBuildings(BuildingType.Apothecary) < 1 &&
                CountBuildings(BuildingType.ChariotMaker) < 1 => 25,
            2 when IsComplete(9) => 26,
            2 when IsComplete(7) => 27,
            2 => 28,
            3 when !IsComplete(10) => 33,
            3 => null,
            4 when IsComplete(13) => 32,
            4 when IsComplete(12) => 29,
            4 when IsComplete(11) => 30,
            4 => 31,
            _ => null
        };
    }

    private bool IsComplete(int eventIndex) =>
        (uint)eventIndex < (uint)flags.Length && flags.Span[eventIndex] != 0;

    private int CountBuildings(BuildingType type)
    {
        if (!session.LogicalChunks.TryGetValue("buildings", out ReadOnlyMemory<byte> memory))
            return 0;
        ReadOnlySpan<byte> buildings = memory.Span;
        const int recordSize = 264;
        int count = 0;
        for (int offset = recordSize; offset + recordSize <= buildings.Length; offset += recordSize)
        {
            if (buildings[offset] is 0 or 5)
                continue;
            if ((BuildingType)BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]) == type)
                count++;
        }
        return count;
    }

    [OriginalAddress(0x00417AE0, "sets tutorial flag, rebuilds availability and opens message")]
    private LegacyTutorialNotification? Complete(int eventIndex)
    {
        if ((uint)eventIndex >= Steps.Length || eventIndex >= flags.Length || flags.Span[eventIndex] != 0)
            return null;
        Step step = Steps[eventIndex];
        if (missionRank != step.MissionRank || step.MessageId == 0)
            return null;
        flags.Span[eventIndex] = 1;
        session.ReplaceLogicalChunk("tutorial_flags_struct", flags.ToArray());
        return new LegacyTutorialNotification(eventIndex, step.MessageId, step.Buildings);
    }
}
