using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum LegacyBuildMenuPermission
{
    Disabled,
    Enabled,
    RequiresRuntimeState
}

public sealed record LegacyBuildMenuEntry(
    BuildingType Type,
    LegacyBuildMenuPermission Permission)
{
    // Compatibility bridge for callers which still serialize the original id.
    public int BuildingType => (int)Type;
}

public sealed record LegacyBuildMenuCategory(
    int SidebarCommand,
    int OriginalCategory,
    IReadOnlyList<LegacyBuildMenuEntry> Entries);

/// <summary>
/// Scenario-owned build permissions and the original category-to-building table.
/// Runtime resource, trade, deity, and tutorial predicates are deliberately kept
/// distinct from the serialized scenario flags.
/// </summary>
public sealed class LegacyBuildMenuState
{
    public const int PermissionOffset = 1036;
    public const int PermissionCount = 44;
    public const int AllowedMonumentsOffset = 1288;
    public const int AllowedMonumentsCount = 3;

    private static readonly int[] SidebarCategories =
    [
        1, 3, 2, 4, 10, 12, 8, 6, 7, 5, 9, 11
    ];

    // DAT_005ce3bc contains 38 rows of 30 int32 building types. These are the
    // twelve rows selected by the expanded and compact sidebar button tables.
    private static readonly BuildingType[][] CategoryBuildingTypes =
    [
        [BuildingType.Housing],
        [BuildingType.Road],
        [BuildingType.ClearLand],
        [BuildingType.Farm, BuildingType.WaterLift, BuildingType.IrrigationDitch,
            BuildingType.ReedGatherer, BuildingType.HuntingLodge,
            BuildingType.FishingWharf, BuildingType.MudGatehouse],
        [BuildingType.RawMaterials, BuildingType.Jeweler, BuildingType.Potter,
            BuildingType.Brewery, BuildingType.Weaver, BuildingType.PapyrusMaker,
            BuildingType.Brickworks, BuildingType.Shipwright, BuildingType.LampMaker,
            BuildingType.MediumRoyalBurialTomb, BuildingType.ConstructionGuilds],
        [BuildingType.Granary, BuildingType.Bazaar, BuildingType.StorageYard, BuildingType.Dock],
        [BuildingType.Booth, BuildingType.Bandstand, BuildingType.Pavilion,
            BuildingType.SenetHouse, BuildingType.JugglerSchool, BuildingType.Conservatory,
            BuildingType.DanceSchool, BuildingType.Zoo],
        [BuildingType.TempleToOsiris, BuildingType.Temples, BuildingType.TempleComplex,
            BuildingType.Monuments, BuildingType.Sphinx],
        [BuildingType.ScribalSchool, BuildingType.Library],
        [BuildingType.Well, BuildingType.TransportWharf, BuildingType.Dentist,
            BuildingType.Apothecary, BuildingType.Physician, BuildingType.Mortuary],
        [BuildingType.Firehouse, BuildingType.ArchitectPost, BuildingType.PoliceStation,
            BuildingType.TaxCollector, BuildingType.Courthouse, BuildingType.TownPalace,
            BuildingType.CityPalace, BuildingType.SecondBazaar, BuildingType.PersonalMansion,
            BuildingType.FamilyMansion, BuildingType.DynastyMansion, BuildingType.Roadblock,
            BuildingType.WaterCrossings, BuildingType.Beautification],
        [BuildingType.Fortifications, BuildingType.Recruiter, BuildingType.Fort,
            BuildingType.Academy, BuildingType.Bowyer, BuildingType.ChariotMaker,
            BuildingType.Pyramid, BuildingType.WarshipWharf]
    ];

    // DAT_005794A8 contains the tutorial unlock records. FUN_00404350 only
    // applies a record to the campaign rank stored in DAT_00E38EA8.
    private static readonly (int MissionRank, BuildingType[] BuildingTypes)[] TutorialBuildingGroups =
    [
        (0, [BuildingType.Firehouse]),
        (0, [BuildingType.HuntingLodge, BuildingType.Granary, BuildingType.Bazaar]),
        (0, [BuildingType.TransportWharf]),
        (0, [BuildingType.ArchitectPost]),
        (1, [BuildingType.TempleToOsiris, BuildingType.Temples, BuildingType.Sphinx,
            BuildingType.None]),
        (1, [BuildingType.Booth, BuildingType.JugglerSchool]),
        (2, [BuildingType.Apothecary, BuildingType.Physician]),
        (2, [BuildingType.ClayPit, BuildingType.RawMaterials, BuildingType.Potter,
            BuildingType.StorageYard]),
        (2, []),
        (2, [BuildingType.Gardens, BuildingType.SmallStatue, BuildingType.MediumStatue,
            BuildingType.LargeStatue, BuildingType.Plaza, BuildingType.Beautification,
            BuildingType.FerryLanding, BuildingType.WaterCrossings, BuildingType.Roadblock]),
        (3, [BuildingType.TaxCollector, BuildingType.PersonalMansion]),
        (4, [BuildingType.ScribalSchool, BuildingType.PapyrusMaker, BuildingType.FigFarm]),
        (4, []),
        (4, [BuildingType.PyramidMonument, BuildingType.BricklayersGuild])
    ];

    private readonly bool[] scenarioPermissions;
    private readonly ushort[] allowedMonuments;
    private readonly int? campaignMissionRank;
    private readonly bool[] tutorialUnlocks;
    private readonly byte[] attention = new byte[12];
    private readonly IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks;
    private bool guildSubmenu;

    private LegacyBuildMenuState(
        bool[] scenarioPermissions,
        ushort[] allowedMonuments,
        int? campaignMissionRank,
        byte[] savedTutorialFlags,
        IReadOnlyDictionary<string,ReadOnlyMemory<byte>> chunks)
    {
        this.chunks=chunks;
        this.scenarioPermissions = scenarioPermissions;
        this.allowedMonuments = allowedMonuments;
        this.campaignMissionRank = campaignMissionRank;
        tutorialUnlocks = TutorialBuildingGroups
            .Select((group, index) => campaignMissionRank is null || group.MissionRank < campaignMissionRank ||
                index < savedTutorialFlags.Length && savedTutorialFlags[index] != 0)
            .ToArray();
        Categories = BuildCategories();
    }

    private IReadOnlyList<LegacyBuildMenuCategory> BuildCategories() =>
        Enumerable.Range(0, SidebarCategories.Length)
            .Select(command => new LegacyBuildMenuCategory(
                command,
                SidebarCategories[command],
                CategoryBuildingTypes[command]
                    .Select(type => new LegacyBuildMenuEntry(type, ResolvePermission(type)))
                    .ToArray()))
            .ToArray();

    public IReadOnlyList<LegacyBuildMenuCategory> Categories { get; private set; }

    public IReadOnlyList<bool> ScenarioPermissions => scenarioPermissions;

    public IReadOnlyList<ushort> AllowedMonuments => allowedMonuments;

    [OriginalAddress(0x00404350, "FUN_00404350")]
    public static LegacyBuildMenuState FromLogicalChunks(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks,
        int? campaignMissionRank = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (!chunks.TryGetValue("scenario_info", out ReadOnlyMemory<byte> memory) ||
            memory.Length < AllowedMonumentsOffset + AllowedMonumentsCount * sizeof(ushort))
        {
            throw new InvalidDataException(
                "Logical chunk 'scenario_info' is missing or too short for build permissions.");
        }

        ReadOnlySpan<byte> scenario = memory.Span;
        bool[] permissions = new bool[PermissionCount];
        for (int index = 0; index < permissions.Length; index++)
        {
            permissions[index] = BinaryPrimitives.ReadInt16LittleEndian(
                scenario[(PermissionOffset + index * sizeof(short))..]) != 0;
        }

        ushort[] monuments = new ushort[AllowedMonumentsCount];
        for (int index = 0; index < monuments.Length; index++)
        {
            monuments[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                scenario[(AllowedMonumentsOffset + index * sizeof(ushort))..]);
        }

        ReadOnlySpan<byte> tutorialFlags = chunks.TryGetValue(
            "tutorial_flags_struct", out ReadOnlyMemory<byte> tutorialMemory)
            ? tutorialMemory.Span
            : [];
        return new LegacyBuildMenuState(permissions, monuments, campaignMissionRank, tutorialFlags.ToArray(),chunks);
    }

    public LegacyBuildMenuCategory GetCategory(int sidebarCommand)
    {
        if ((uint)sidebarCommand >= (uint)Categories.Count)
            throw new ArgumentOutOfRangeException(nameof(sidebarCommand));
        return Categories[sidebarCommand];
    }

    [OriginalAddress(0x00403F10, "FUN_00403f10")]
    public IReadOnlyList<LegacyBuildMenuEntry> GetVerifiedAvailableEntries(int sidebarCommand) =>
        (guildSubmenu&&sidebarCommand==4
            ? new[]{BuildingType.CarpenterGuild,BuildingType.StonemasonGuild,BuildingType.BricklayersGuild}
                .Select(type=>new LegacyBuildMenuEntry(type,ResolvePermission(type)))
            : GetCategory(sidebarCommand).Entries)
            .Where(entry => entry.Permission == LegacyBuildMenuPermission.Enabled)
            .ToArray();

    [OriginalAddress(0x00404350,"Re-evaluate resource-dependent build permissions before opening the menu")]
    public void RefreshRuntimePermissions(){guildSubmenu=false;Categories=BuildCategories();}

    [OriginalAddress(0x00405200,"Type4 selects native category22; type176 selects the distinct fortifications category32")]
    public bool OpenSubmenu(BuildingType type)
    {
        guildSubmenu=type==BuildingType.ConstructionGuilds;
        return guildSubmenu;
    }

    public bool IsCommandEnabled(int sidebarCommand) =>
        GetCategory(sidebarCommand).Entries.Any(entry=>entry.Permission==LegacyBuildMenuPermission.Enabled);

    /// <summary>
    /// Resolves the image following the neutral city-sidebar category image.
    /// The executable keys this permutation by the original category id, not
    /// by the visual button position.
    /// </summary>
    [OriginalAddress(0x0051EDD0, "FUN_0051edd0")]
    public static int ResolveCategoryIconOffset(int originalCategory) => originalCategory switch
    {
        1 => 1,
        3 => 5,
        2 => 9,
        12 => 2,
        10 => 6,
        4 => 10,
        8 => 3,
        6 => 7,
        7 => 11,
        5 => 4,
        9 => 8,
        11 => 12,
        _ => 0
    };

    [OriginalAddress(0x00417AE0, "FUN_00417ae0")]
    [OriginalAddress(0x00417A70, "snapshot available entry counts before tutorial unlock")]
    [OriginalAddress(0x00417AA0, "flash categories whose entry count increased")]
    public void UnlockTutorialGroup(int group)
    {
        if ((uint)group >= (uint)tutorialUnlocks.Length)
            throw new ArgumentOutOfRangeException(nameof(group));
        int[] previousCounts = Categories.Select(category => category.Entries.Count(
            entry => entry.Permission == LegacyBuildMenuPermission.Enabled)).ToArray();
        tutorialUnlocks[group] = true;
        Categories = BuildCategories();
        for (int command = 0; command < Categories.Count; command++)
            if (GetCategory(command).Entries.Count(entry=>entry.Permission==LegacyBuildMenuPermission.Enabled) > previousCounts[command])
                attention[command] = 1;
    }

    public bool HasAttention => attention.Any(value => value != 0);
    public bool NeedsAttention(int command) => (uint)command < attention.Length && attention[command] != 0;

    [OriginalAddress(0x00425990, "clicking a control clears its attention byte")]
    public void AcknowledgeCommand(int command)
    {
        if ((uint)command < attention.Length) attention[command] = 0;
    }

    [OriginalAddress(0x00425730, "packed attention mode and sixteen-draw countdown")]
    public bool AdvanceAttentionFrame(int command)
    {
        if (!NeedsAttention(command)) return false;
        int value = attention[command];
        int countdown = value >> 2;
        int mode = value & 3;
        if (countdown == 0)
        {
            mode = mode != 2 ? 2 : 1;
            countdown = 16;
        }
        attention[command] = unchecked((byte)(((countdown - 1) << 2) | mode));
        return mode == 2;
    }

    [OriginalAddress(0x00404350, "FUN_00404350")]
    private LegacyBuildMenuPermission ResolvePermission(BuildingType buildingType)
    {
        LegacyBuildMenuPermission permission = ResolveScenarioPermission(buildingType);
        if (campaignMissionRank is not int rank)
            return permission;

        // FUN_00404350 first enables the police station with the
        // other core buildings, then explicitly disables it in campaign rank
        // zero. Unlike firehouses and architects' posts it is not subsequently
        // associated with one of DAT_005794A8's tutorial unlock records.
        if (rank == 0 && buildingType == BuildingType.PoliceStation)
            return LegacyBuildMenuPermission.Disabled;

        for (int group = 0; group < TutorialBuildingGroups.Length; group++)
        {
            (int missionRank, BuildingType[] buildingTypes) = TutorialBuildingGroups[group];
            if (missionRank == rank && buildingTypes.Contains(buildingType))
            {
                return tutorialUnlocks[group]
                    ? LegacyBuildMenuPermission.Enabled
                    : LegacyBuildMenuPermission.Disabled;
            }
        }
        return permission;
    }

    /// <summary>
    /// Applies only predicates which FUN_00404350 reads directly from the 44
    /// verified scenario permission values. Entries involving live city state remain
    /// unresolved until their subsystem state is reconstructed.
    /// </summary>
    [OriginalAddress(0x00404350, "FUN_00404350")]
    [OriginalAddress(0x00403EF0,"Campaign rank4 construction guild tutorial gate")]
    private LegacyBuildMenuPermission ResolveScenarioPermission(BuildingType buildingType) => buildingType switch
    {
        BuildingType.Road or BuildingType.ClearLand or BuildingType.Housing or
            BuildingType.PoliceStation or BuildingType.ArchitectPost or BuildingType.Well or
            BuildingType.Firehouse => LegacyBuildMenuPermission.Enabled,

        BuildingType.WaterLift => Direct(1),
        BuildingType.IrrigationDitch => Direct(0),
        BuildingType.MudGatehouse => Direct(4),
        BuildingType.Bazaar => Direct(6),
        BuildingType.StorageYard => Direct(7),
        BuildingType.Dock => Direct(8),
        BuildingType.Booth or BuildingType.JugglerSchool => Direct(9),
        BuildingType.Bandstand or BuildingType.Conservatory => Direct(10),
        BuildingType.Pavilion or BuildingType.DanceSchool => Direct(11),
        BuildingType.SenetHouse => Direct(12),
        BuildingType.Sphinx => Direct(13),
        BuildingType.ScribalSchool => Direct(14),
        BuildingType.Granary => Direct(5),
        BuildingType.Library => Direct(15),
        BuildingType.TransportWharf => Direct(16),
        BuildingType.Dentist => Direct(17),
        BuildingType.Apothecary => Direct(18),
        BuildingType.Physician => Direct(19),
        BuildingType.Mortuary => Direct(20),
        BuildingType.TaxCollector => Direct(21),
        BuildingType.Courthouse => Direct(22),
        BuildingType.TownPalace => Direct(23),
        BuildingType.PersonalMansion or BuildingType.FamilyMansion or
            BuildingType.DynastyMansion => Direct(24),
        BuildingType.Roadblock => Direct(25),
        BuildingType.WaterCrossings => Any(26, 27),
        BuildingType.Beautification => Any(28, 29, 30),
        BuildingType.Fortifications => Any(31, 32, 33),
        BuildingType.Recruiter => Direct(34),
        BuildingType.Fort => Any(35, 36, 37),
        BuildingType.Academy => Direct(38),
        BuildingType.Bowyer => Direct(39),
        BuildingType.ChariotMaker => Direct(40),
        BuildingType.Pyramid => Direct(41),
        BuildingType.WarshipWharf => Direct(42),
        BuildingType.Zoo => Direct(43),
        BuildingType.Shipwright => Any(3, 41, 42),
        BuildingType.Brewery => Production(15),
        BuildingType.Weaver => Production(16),
        BuildingType.Potter => Production(11),
        BuildingType.PapyrusMaker => Production(22),
        BuildingType.Jeweler => Production(18),
        BuildingType.LampMaker => LegacyProductionAvailability.CanProduce(chunks,31)?Production(13):LegacyBuildMenuPermission.Disabled,
        BuildingType.Brickworks => campaignMissionRank is >=0 and <7?LegacyBuildMenuPermission.Disabled:Production(12),
        BuildingType.CarpenterGuild => Production(20),
        BuildingType.StonemasonGuild => Domestic(24),
        BuildingType.BricklayersGuild => Production(12),
        BuildingType.ConstructionGuilds => campaignMissionRank==4&&!tutorialUnlocks[13]?LegacyBuildMenuPermission.Disabled:
            LegacyProductionAvailability.CanProduce(chunks,20)||LegacyProductionAvailability.CanProduce(chunks,12)||LegacyProductionAvailability.HasDomestic(chunks,24)
                ?LegacyBuildMenuPermission.Enabled:LegacyBuildMenuPermission.Disabled,

        // The remaining entries depend on resource production/imports,
        // monument state, tutorial gates, deity state, or existing buildings.
        _ => LegacyBuildMenuPermission.RequiresRuntimeState
    };

    [OriginalAddress(0x00404350,"Resource-dependent construction menu entries")]
    [OriginalAddress(0x00443D90,"Potential manufacturing input availability")]
    private LegacyBuildMenuPermission Production(int resource)=>LegacyProductionAvailability.CanProduce(chunks,resource)
        ?LegacyBuildMenuPermission.Enabled:LegacyBuildMenuPermission.Disabled;

    [OriginalAddress(0x00404350,"Domestic resource predicate for the stonemasons menu entry")]
    private LegacyBuildMenuPermission Domestic(int resource)=>LegacyProductionAvailability.HasDomestic(chunks,resource)
        ?LegacyBuildMenuPermission.Enabled:LegacyBuildMenuPermission.Disabled;

    private LegacyBuildMenuPermission Direct(int index) =>
        scenarioPermissions[index]
            ? LegacyBuildMenuPermission.Enabled
            : LegacyBuildMenuPermission.Disabled;

    private LegacyBuildMenuPermission Any(params int[] indices) =>
        indices.Any(index => scenarioPermissions[index])
            ? LegacyBuildMenuPermission.Enabled
            : LegacyBuildMenuPermission.Disabled;
}
