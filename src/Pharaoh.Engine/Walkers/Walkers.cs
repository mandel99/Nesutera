using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Housing;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Population;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Installation;

namespace Pharaoh.Engine.Walkers;

public readonly record struct WalkerAdvance(
    int Spawned,
    int Arrived,
    int Removed,
    int PopulationDelta,
    IReadOnlyList<int> ChangedTiles,
    IReadOnlyList<int> HousingImageChangedTiles)
{
    public bool Changed => Spawned != 0 || Arrived != 0 || Removed != 0 || ChangedTiles.Count != 0;
}

public readonly record struct DemolitionDisplacement(
    int SpawnedWalkers,
    int DisplacedPeople,
    IReadOnlyList<int> ChangedTiles);

/// <summary>
/// Owns Pharaoh's fixed walker pool and dispatches each walker to its type-specific behavior.
/// The first active behavior is the original immigrant state machine.
/// </summary>
public sealed partial class Walkers
{
    public const int RecordSize = 388;
    public const int MaximumWalkers = 2000;
    public const int MovementUnitsPerTile = 15;
    public const int MigrationDispatcherPeriod = PopulationManager.MigrationDispatcherPeriod;
    // Architect/fireman handlers at 0048C6A0/0048DED0 store 0x280 movement
    // pulses as their roaming limit, including partial movement at the end.
    private const int SafetyPatrolMovementLimit = 0x280;

    public const int SafetyWaitingAction = 8;
    public const int SafetyLeavingAction = 9;
    public const int SafetyPatrollingAction = 10;
    public const int SafetyReturningAction = 11;
    public const int FireRespondingAction = 12;
    public const int FireFightingAction = 13;

    private const int MaximumRoutes = 1000;
    private const int RouteCapacity = 500;
    // city_data is serialized from 0xEA0000 - 0x3168. These are the active-city
    // fields used by FUN_004E8F80/FUN_00458540.
    private const int CityPopulationOffset = 18_940;
    private const int DisplacedPopulationOffset = 28_880;

    private const int BuildingRecordSize = HousingManager.RecordSize;
    private const int MaximumBuildings = HousingManager.MaximumBuildings;
    private const uint TerrainTree = 0x00000001;
    private const uint TerrainRock = 0x00000002;
    private const uint TerrainWater = 0x00000004;
    private const uint TerrainBuilding = 0x00000008;
    private const uint TerrainMarsh = 0x00040000;
    private const uint TerrainOutsideMap = 0x00080000;
    private const uint TerrainDune = 0x02000000;
    private const uint TerrainDeepWater = 0x04000000;

    private static readonly (int Dx, int Dy)[] Directions =
    [
        (0, -1), (1, -1), (1, 0), (1, 1),
        (0, 1), (-1, 1), (-1, 0), (-1, -1)
    ];
    private static readonly int[] CardinalDirectionIndices = [0, 2, 4, 6];

    private readonly LegacyMissionSession session;
    private readonly IReadOnlyList<LegacyHousingLevel>? housingLevels;
    private readonly LegacyBuildingModelCosts? buildingModels;
    private readonly LegacyFigureModels? figureModels;
    private readonly int difficulty;
    private readonly Dictionary<WalkerType, WalkerBehavior> behaviors;
    private readonly PopulationManager populationManager;
    private readonly List<Walker> active = [];
    private readonly int width;
    private readonly int height;
    private readonly int origin;
    private int arrivedThisAdvance;
    private int removedThisAdvance;
    private int populationDeltaThisAdvance;
    private readonly HashSet<int> changedTiles = [];
    private readonly HashSet<int> housingImageChangedTiles = [];
    private readonly HashSet<int> serializedMigrantIds = [];
    private readonly HashSet<int> activeIdsScratch = [];
    private readonly List<int> retiredIdsScratch = [];

    private Walkers(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel>? housingLevels,
        LegacyBuildingModelCosts? buildingModels,
        LegacyFigureModels? figureModels,
        int difficulty)
    {
        this.session = session;
        this.housingLevels = housingLevels;
        this.buildingModels = buildingModels;
        this.figureModels = figureModels;
        this.difficulty = difficulty;
        populationManager = new PopulationManager(session);
        behaviors = new Dictionary<WalkerType, WalkerBehavior>
        {
            [WalkerType.Immigrant] = new ImmigrantBehavior(),
            [WalkerType.Emigrant] = new EmigrantBehavior(),
            [WalkerType.Homeless] = new HomelessBehavior(),
            [WalkerType.Architect] = new ArchitectBehavior(),
            [WalkerType.Fireman] = new FiremanBehavior(),
            [WalkerType.Policeman] = new PolicemanBehavior(),
            [WalkerType.OstrichHunter] = new HunterBehavior(),
            [WalkerType.Ostrich] = new OstrichBehavior(),
            [WalkerType.HunterArrow] = new HunterArrowBehavior(),
            [WalkerType.Recruiter] = new LaborSeekerBehavior(),
            [WalkerType.CartPusher] = new MeatCartBehavior(),
            [WalkerType.StorageTransporter] = new DistributionBehavior(WalkerType.StorageTransporter),
            [WalkerType.BazaarBuyer] = new DistributionBehavior(WalkerType.BazaarBuyer),
            [WalkerType.BazaarSeller] = new DistributionBehavior(WalkerType.BazaarSeller),
            [WalkerType.BazaarCarrier] = new DistributionBehavior(WalkerType.BazaarCarrier)
        };
        ReadOnlySpan<byte> scenario = Required("scenario_info", 912).Span;
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        distributionFoods = LegacyFoodAvailability.Read(session.LogicalChunks);
        LoadExistingMigrants();
    }

    public IReadOnlyList<Walker> Active => active;

    public static Walkers Create(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel>? housingLevels = null,
        LegacyBuildingModelCosts? buildingModels = null,
        LegacyFigureModels? figureModels = null,
        int difficulty = 2)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (housingLevels is not null && housingLevels.Count != 20)
            throw new ArgumentException("The housing model must contain exactly 20 levels.", nameof(housingLevels));
        return new Walkers(session, housingLevels, buildingModels, figureModels, difficulty);
    }

    internal int ExitTile => ReadExitTile();

    [OriginalAddress(0x00458B60, "FUN_00458b60")]
    [OriginalAddress(0x00458D00, "FUN_00458d00")]
    public WalkerAdvance Advance(long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        int spawned = 0;
        arrivedThisAdvance = 0;
        removedThisAdvance = 0;
        populationDeltaThisAdvance = 0;
        changedTiles.Clear();
        housingImageChangedTiles.Clear();
        for (long tick = 0; tick < legacyTicks; tick++)
        {
            AdvanceLabor();
            AdvanceHuntingProduction();
            AdvanceDistributionBuildings();
            spawned += populationManager.AdvanceMigration(
                SpawnImmigrantsForVacantHousing, SpawnEmigrants);

            for (int index = 0; index < active.Count; index++)
            {
                Walker walker = active[index];
                if (walker.State != WalkerState.Alive ||
                    !behaviors.TryGetValue(walker.Type, out WalkerBehavior? behavior))
                    continue;
                behavior.Advance(this, walker);
            }
            active.RemoveAll(walker => walker.State == WalkerState.Dead);
        }
        PublishWalkerRecords();
        return new WalkerAdvance(spawned, arrivedThisAdvance, removedThisAdvance,
            populationDeltaThisAdvance,
            changedTiles.Order().ToArray(),
            housingImageChangedTiles.Order().ToArray());
    }

    [OriginalAddress(0x004583F0, "active housing list")]
    [OriginalAddress(0x00458440, "capacity and disease eligibility")]
    [OriginalAddress(0x00458950, "FUN_00458950 housing selection passes")]
    [OriginalAddress(0x00458D00, "FUN_00458d00")]
    public int SpawnImmigrantsForVacantHousing(int maximumPeople)
    {
        if (maximumPeople <= 0)
            return 0;
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        int remaining = maximumPeople;
        int spawned = 0;
        // The entry at 00458950 first clears dead reservations, then selects
        // homes with >=8 vacancies (groups of at most four), followed by all
        // remaining homes with vacancies. Its earlier empty-home loop has no
        // reachable path for a positive migration batch in the installed EXE.
        for (int buildingId = 1; buildingId < MaximumBuildings; buildingId++)
        {
            int offset = buildingId * BuildingRecordSize;
            if (!((BuildingType)BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..])).IsHousing()) continue;
            int reserved = BinaryPrimitives.ReadUInt16LittleEndian(buildings[(offset + 46)..]);
            if (reserved > 0 && !IsReservationAlive(reserved))
                BinaryPrimitives.WriteUInt16LittleEndian(buildings[(offset + 46)..], 0);
        }
        for (int pass = 0; pass < 2 && remaining > 0; pass++)
        for (int buildingId = 1; buildingId < MaximumBuildings && remaining > 0; buildingId++)
        {
            int offset = buildingId * BuildingRecordSize;
            BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                buildings[(offset + 16)..]);
            // Queued state-3 parcels remain supported at the rewrite's current
            // construction boundary; the EXE activates them before building
            // its state-1 housing list. Ruins and demolition states are excluded.
            if (buildings[offset] is not (1 or 3) || !type.IsHousing() ||
                buildings[offset + 5] == 0 || BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 32)..]) <= 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(buildings[(offset + 46)..]) != 0)
                continue;
            int available = GetAvailableHousing(buildings, offset, (int)type);
            if (available < (pass == 0 ? 8 : 1))
                continue;

            int id = AllocateId();
            if (id == 0)
                break;
            int entryTile = ReadEntryTile();
            int people = Math.Min(pass == 0 ? 4 : available, remaining);
            Walker walker = new(id, WalkerType.Immigrant, entryTile)
            {
                HomeBuildingId = buildingId,
                DestinationBuildingId = buildingId,
                DestinationTile = BinaryPrimitives.ReadInt32LittleEndian(buildings[(offset + 12)..]),
                ActionState = ImmigrantBehavior.Recalculate,
                WaitTicks = 10 + (buildings[offset + 77] & 0x7f),
                Direction = 0,
                MigrantPeople = people,
                TerrainUsage = WalkerTerrainUsage.Any
            };
            active.Add(walker);
            BinaryPrimitives.WriteUInt16LittleEndian(buildings[(offset + 46)..], checked((ushort)id));
            changedTiles.Add(entryTile);
            remaining -= people;
            spawned++;
        }
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        int admitted = maximumPeople - remaining;
        BinaryPrimitives.WriteInt32LittleEndian(city[28920..],
            checked(BinaryPrimitives.ReadInt32LittleEndian(city[28920..]) + admitted));
        BinaryPrimitives.WriteInt32LittleEndian(city[28948..],
            checked(BinaryPrimitives.ReadInt32LittleEndian(city[28948..]) + admitted));
        if (admitted == 0 && remaining > 0)
            BinaryPrimitives.WriteInt32LittleEndian(city[28928..],
                checked(BinaryPrimitives.ReadInt32LittleEndian(city[28928..]) + remaining));
        return spawned;
    }

    [OriginalAddress(0x00458950, "clear reservations whose walker is no longer alive")]
    private bool IsReservationAlive(int id) =>
        id > 0 && id < MaximumWalkers &&
        (active.Any(walker => walker.Id == id && walker.State == WalkerState.Alive) ||
         Required("walkers", MaximumWalkers * RecordSize).Span[id * RecordSize + 14] == 1);

    [OriginalAddress(0x00458B60, "FUN_00458b60")]
    [OriginalAddress(0x00458D80, "FUN_00458d80")]
    [OriginalAddress(0x004583F0, "active physical house list")]
    public int SpawnEmigrants(int maximumPeople)
    {
        if (maximumPeople <= 0)
            return 0;
        byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        int remaining = maximumPeople;
        int spawned = 0;
        // FUN_00458b60 performs ten passes over the house list, lowest level first.
        for (int level = 0; level < 10 && remaining > 0; level++)
        {
            for (int buildingId = 1; buildingId < MaximumBuildings && remaining > 0; buildingId++)
            {
                int offset = buildingId * BuildingRecordSize;
                BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                    buildings.AsSpan(offset + 16));
                int houseLevel = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 18));
                int population = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 28));
                if (buildings[offset] != 1 || buildings[offset + 5] == 0 || !type.IsHousing() ||
                    houseLevel != level || population <= 0)
                    continue;

                int people = Math.Min(4, Math.Min(population, remaining));
                int id = AllocateId();
                if (id == 0)
                {
                    session.ReplaceLogicalChunk("buildings", buildings);
                    return spawned;
                }
                int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
                Walker walker = new(id, WalkerType.Emigrant, tile)
                {
                    SourceTile = tile,
                    DestinationTile = ReadExitTile(),
                    ActionState = EmigrantBehavior.LeavingHouse,
                    WaitTicks = 0,
                    Direction = 0,
                    MigrantPeople = people,
                    TerrainUsage = WalkerTerrainUsage.Any
                };
                active.Add(walker);
                ApplyEmigrationToHouse(buildings, offset, tile, population, people);
                UpdateCityPopulation(-people);
                Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
                BinaryPrimitives.WriteInt32LittleEndian(city[28924..],
                    checked(BinaryPrimitives.ReadInt32LittleEndian(city[28924..]) + people));
                populationDeltaThisAdvance -= people;
                changedTiles.Add(tile);
                remaining -= people;
                spawned++;
            }
        }
        session.ReplaceLogicalChunk("buildings", buildings);
        return spawned;
    }

    [OriginalAddress(0x00462180, "one primary service walker per operational workplace")]
    [OriginalAddress(0x00493100, "allocate a walker with its home-building ownership")]
    [RewriteOnly("All implemented workplace walkers materialize on the selected road entrance, as explicitly requested")]
    public bool SpawnSafetyWalker(int buildingId, WalkerType type)
    {
        if ((uint)buildingId >= MaximumBuildings || buildingId == 0 ||
            active.Any(w => w.State == WalkerState.Alive && w.HomeBuildingId == buildingId && w.Type == type))
            return false;
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).Span;
        int offset = buildingId * BuildingRecordSize;
        if (buildings[offset] != 1)
            return false;
        if (!TryGetBuildingRoadAccess(buildings, offset, out int tile)) return false;
        bool nativeSafety = type is WalkerType.Architect or WalkerType.Fireman;
        int id = AllocateId();
        if (id == 0)
            return false;
        Walker walker = new(id, type, tile)
        {
            HomeBuildingId = buildingId,
            SourceTile = tile,
            DestinationTile = tile,
            ActionState = nativeSafety ? 8 : SafetyPatrollingAction,
            TerrainUsage = WalkerTerrainUsage.Roads
        };
        active.Add(walker);
        if (type is WalkerType.Architect or WalkerType.Fireman or WalkerType.Policeman)
            WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span,offset+42,id);
        changedTiles.Add(tile);
        return true;
    }

    internal void AdvanceSafetyPatrol(Walker walker) => AdvanceServicePatrol(walker);

    [OriginalAddress(0x0048C6A0, "FUN_0048c6a0")]
    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    internal bool ValidateSafetyHome(Walker walker)
    {
        if(walker.HomeBuildingId>0 && walker.HomeBuildingId<MaximumBuildings)
        {
            ReadOnlySpan<byte> b=Required("buildings",MaximumBuildings*BuildingRecordSize).Span.Slice(walker.HomeBuildingId*BuildingRecordSize,BuildingRecordSize);
            if(b[0]==1 && ReadLaborShort(b,42)==walker.Id)return true;
        }
        RetireSafetyWalker(walker);return false;
    }

    [OriginalAddress(0x0048C6A0, "FUN_0048c6a0")]
    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    private void AdvanceServicePatrol(Walker walker)
    {
        bool safety=walker.Type is WalkerType.Architect or WalkerType.Fireman;
        if(safety && !ValidateSafetyHome(walker))return;
        if(walker.ActionState==8)
        {
            if(!TryGetBuildingRoadAccess(Required("buildings",MaximumBuildings*BuildingRecordSize).Span,walker.HomeBuildingId*BuildingRecordSize,out int road))
            {RetireSafetyWalker(walker);return;}
            walker.DestinationTile=road;ClearHuntingRoute(walker);
            changedTiles.Add(walker.Tile); walker.PreviousTile = walker.Tile = road;
            walker.ProgressOnTile = 0; walker.ActionState = SafetyPatrollingAction;
            walker.RoamTicks = 0; InitializeServiceRoam(walker); changedTiles.Add(road); return;
        }
        if(walker.ActionState==9)
        {
            // Older saves can retain the native cross-country entrance state.
            // Finish it at the road endpoint without traversing the building.
            changedTiles.Add(walker.Tile); walker.PreviousTile = walker.Tile = walker.DestinationTile;
            walker.ProgressOnTile = 0; walker.ActionState = SafetyPatrollingAction;
            walker.RoamTicks = 0; InitializeServiceRoam(walker); changedTiles.Add(walker.Tile);
            return;
        }
        if(walker.ActionState==SafetyReturningAction)
        {
            if(walker.route.Count==0 || walker.RoutePosition>=walker.route.Count)
                if(!TryBuildSafetyReturn(walker)||walker.route.Count==0){RetireSafetyWalker(walker);return;}
            ServiceBeforeStep(walker);
            if(AdvanceAlongRoute(walker))RetireSafetyWalker(walker);
            return;
        }
        walker.ActionState=SafetyPatrollingAction;walker.TerrainUsage=WalkerTerrainUsage.Roads;
        if(++walker.RoamTicks>=SafetyPatrolMovementLimit){BeginSafetyReturn(walker);return;}
        if(walker.route.Count==0||walker.RoutePosition>=walker.route.Count)
        {
            ClearHuntingRoute(walker);
            if(!TryBuildNextSafetyPatrolStep(walker)){BeginSafetyReturn(walker);return;}
        }
        ReadOnlySpan<byte> terrain=Required("terrain_grid",LegacyCityMapSnapshot.ImageGridLength).Span;
        int next=walker.route[walker.RoutePosition];
        if(!IsRoad(next,terrain)||!IsPassable(next,terrain,false)){BeginSafetyReturn(walker);return;}
        ServiceBeforeStep(walker);
        if(AdvanceAlongRoute(walker)){walker.RerouteCounter++;ClearHuntingRoute(walker);}
    }

    [OriginalAddress(0x004BAA10, "FUN_004baa10")]
    private void ServiceBeforeStep(Walker walker)
    {
        if(walker.ProgressOnTile!=MovementUnitsPerTile-1)return;
        if(walker.Type==WalkerType.Architect)ApplyArchitectInspection(walker);
        if(walker.Type==WalkerType.Fireman)ApplyFirePrevention(walker);
    }

    [OriginalAddress(0x0048C6A0, "FUN_0048c6a0")]
    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    private void BeginSafetyReturn(Walker walker)
    {
        ReleaseFireReservation(walker);
        walker.ActionState=SafetyReturningAction;walker.DestinationBuildingId=0;walker.TargetWalkerId=0;
        ClearHuntingRoute(walker);
        if(!TryBuildSafetyReturn(walker))RetireSafetyWalker(walker);
    }

    [OriginalAddress(0x0048C6A0, "FUN_0048c6a0")]
    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    private void RetireSafetyWalker(Walker walker)
    {
        ReleaseFireReservation(walker);
        if(walker.HomeBuildingId>0&&walker.HomeBuildingId<MaximumBuildings)
        {
            Span<byte> b=session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId*BuildingRecordSize,BuildingRecordSize);
            if(ReadLaborShort(b,42)==walker.Id)WriteLaborShort(b,42,0);
        }
        Destroy(walker,false);
    }

    [OriginalAddress(0x00482C10, "FUN_00482c10")]
    internal void ApplyArchitectInspection(Walker walker)=>ApplyAdjacentBuildingRisk(walker,70,buildingModels?.GetModel(difficulty,81)?.Reserved9??50);

    [OriginalAddress(0x00483000, "FUN_00483000")]
    internal void ApplyFirePrevention(Walker walker)=>ApplyAdjacentBuildingRisk(walker,72,buildingModels?.GetModel(difficulty,167)?.Reserved9??100);

    [OriginalAddress(0x00482C10, "FUN_00482c10")]
    [OriginalAddress(0x00483000, "FUN_00483000")]
    private void ApplyAdjacentBuildingRisk(Walker walker,int riskOffset,int amount)
    {
        ReadOnlySpan<byte> grid=Required("building_grid",LegacyCityMapSnapshot.TileCount*2).Span;
        Span<byte> buildings=session.GetWritableLogicalChunk("buildings").Span;
        int wx=walker.Tile%228,wy=walker.Tile/228;
        for(int y=wy-2;y<=wy+2;y++)for(int x=wx-2;x<=wx+2;x++)
        {
            if((uint)x>=228||(uint)y>=228)continue;
            int tile=y*228+x,id=ReadLaborShort(grid,tile*2);
            if(id<=0||id>=MaximumBuildings)continue;
            id=LegacyBuildingDisasters.Root(buildings,id);if(id==0)continue;
            Span<byte> b=buildings.Slice(id*BuildingRecordSize,BuildingRecordSize);
            int risk=ReadLaborShort(b,riskOffset);if(b[0]!=1||risk<=0)continue;
            WriteLaborShort(b,riskOffset,Math.Max(0,risk-amount));changedTiles.Add(tile);
        }
    }

    [OriginalAddress(0x0048E5E0, "FUN_0048e5e0")]
    [OriginalAddress(0x00469A50, "FUN_00469a50")]
    private bool AcquireFire(Walker walker)
    {
        if(walker.ActionState is not (SafetyPatrollingAction or SafetyReturningAction))return false;
        ReadOnlySpan<byte> buildings=Required("buildings",MaximumBuildings*BuildingRecordSize).Span;
        int burning=BinaryPrimitives.ReadInt32LittleEndian(Required("building_burning_list_info",8).Span);
        if(burning<1)return false;
        walker.FireSearchTicks=unchecked((byte)(walker.FireSearchTicks+1));
        if(walker.FireSearchTicks<20)return false;
        ReadOnlySpan<byte> list=Required("building_list_burning",1000).Span;
        List<(int Id,int Distance)> candidates=[];
        int count=Math.Clamp(burning,0,499);
        BinaryPrimitives.WriteInt32LittleEndian(session.GetWritableLogicalChunk("building_burning_list_info").Span[4..],count);
        for(int index=0;index<count;index++)
        {
            int id=ReadLaborShort(list,index*2);
            if(!IsFireTarget(buildings,id,out int tile))continue;
            int distance=ChebyshevDistance(walker.Tile,tile),reservation=ReadLaborShort(buildings,id*BuildingRecordSize+48);
            // The native caller's distance guard rejects the reserved-target fallback.
            if(distance>25||reservation!=0)continue;
            candidates.Add((id,distance));
        }
        foreach(var candidate in candidates.OrderBy(c=>c.Distance).ThenBy(c=>c.Id))
        {
            IsFireTarget(buildings,candidate.Id,out int tile);
            List<int> route=[];
            if(candidate.Distance>1&&!TryRouteBeside(walker.Tile,tile,out route))continue;
            ReleaseFireReservation(walker);
            WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span,candidate.Id*BuildingRecordSize+48,walker.Id);
            walker.FireSearchTicks=0;walker.DestinationBuildingId=candidate.Id;walker.ActionState=FireRespondingAction;walker.TerrainUsage=WalkerTerrainUsage.Any;
            ClearHuntingRoute(walker);walker.route=route;walker.DestinationTile=route.Count>0?route[^1]:walker.Tile;
            return true;
        }
        return false;
    }

    [OriginalAddress(0x00469A50, "FUN_00469a50")]
    private static bool IsFireTarget(ReadOnlySpan<byte> buildings,int id,out int tile)
    {
        tile=0;if(id<=0||id>=MaximumBuildings)return false;
        ReadOnlySpan<byte> b=buildings.Slice(id*BuildingRecordSize,BuildingRecordSize);
        tile=BinaryPrimitives.ReadInt32LittleEndian(b[12..]);
        return b[0]==1&&ReadLaborShort(b,16)==99&&b[189]==0&&(uint)tile<LegacyCityMapSnapshot.TileCount;
    }

    [OriginalAddress(0x0048E5E0, "FUN_0048e5e0")]
    private void ReleaseFireReservation(Walker walker)
    {
        if(walker.Type!=WalkerType.Fireman||walker.DestinationBuildingId<=0||walker.DestinationBuildingId>=MaximumBuildings)return;
        Span<byte> b=session.GetWritableLogicalChunk("buildings").Span.Slice(walker.DestinationBuildingId*BuildingRecordSize,BuildingRecordSize);
        if(ReadLaborShort(b,16)==99&&ReadLaborShort(b,48)==walker.Id)WriteLaborShort(b,48,0);
    }

    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    internal bool AdvanceFireResponse(Walker walker)
    {
        if(!ValidateSafetyHome(walker))return true;
        if(walker.ActionState is not (FireRespondingAction or FireFightingAction))
            if(!AcquireFire(walker))return false;
        ReadOnlySpan<byte> buildings=Required("buildings",MaximumBuildings*BuildingRecordSize).Span;
        bool valid=IsFireTarget(buildings,walker.DestinationBuildingId,out int targetTile);
        if(walker.ActionState==FireFightingAction)
        {
            if(valid&&ChebyshevDistance(walker.Tile,targetTile)<2)
            {
                WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span,walker.DestinationBuildingId*BuildingRecordSize+74,32);
                int direction=DirectionBetween(walker.Tile,targetTile);if(direction>=0)walker.Direction=direction;
                changedTiles.Add(targetTile);
            }
            else walker.WaitTicks=Math.Min(1,walker.WaitTicks);
            if(--walker.WaitTicks<=0&&walker.FireFightingFrame>=36)
            {walker.FireSearchTicks=20;BeginSafetyReturn(walker);}
            if(walker.ActionState==FireFightingAction) { walker.FireFightingFrame++;walker.AnimationFrame=walker.FireFightingFrame>=36?0:walker.FireFightingFrame; }
            return true;
        }
        if(!valid){BeginSafetyReturn(walker);return true;}
        if(ChebyshevDistance(walker.Tile,targetTile)<2)
        {walker.ActionState=FireFightingAction;walker.WaitTicks=50;walker.FireFightingFrame=1;walker.AnimationFrame=1;ClearHuntingRoute(walker);return true;}
        if(walker.route.Count==0||walker.RoutePosition>=walker.route.Count)
        {
            if(!TryRouteBeside(walker.Tile,targetTile,out List<int> route)) {BeginSafetyReturn(walker);return true;}
            walker.route=route;walker.RoutePosition=0;
        }
        AdvanceAlongRoute(walker);return true;
    }

    internal bool AdvancePolicemanCombat(Walker walker)
    {
        byte[] records = Required("walkers", MaximumWalkers * RecordSize).ToArray();
        int targetId = walker.TargetWalkerId;
        if (!TryGetHostileWalker(records, targetId, out WalkerType targetType, out int targetTile))
        {
            targetId = FindNearestHostileWalker(records, walker.Tile, out targetType, out targetTile);
            if (targetId == 0)
            {
                walker.TargetWalkerId = 0;
                walker.ActionState = 0;
                return false;
            }
            walker.TargetWalkerId = targetId;
            walker.WaitTicks = 0;
            walker.route.Clear();
            walker.RoutePosition = 0;
        }

        walker.ActionState = 1;
        if (ChebyshevDistance(walker.Tile, targetTile) <= 1)
        {
            if (walker.WaitTicks > 0)
            {
                walker.WaitTicks--;
                return true;
            }
            int offset = targetId * RecordSize;
            int damage = BinaryPrimitives.ReadUInt16LittleEndian(records.AsSpan(offset + 30));
            damage = Math.Min(ushort.MaxValue, damage + 25);
            BinaryPrimitives.WriteUInt16LittleEndian(
                records.AsSpan(offset + 30), checked((ushort)damage));
            if (damage >= 100)
            {
                int targetRouteId = BinaryPrimitives.ReadInt16LittleEndian(records.AsSpan(offset + 58));
                records.AsSpan(offset, RecordSize).Clear();
                ReleaseSerializedRoute(targetRouteId);
                walker.TargetWalkerId = 0;
                walker.ActionState = 0;
            }
            if (targetType == WalkerType.Crocodile || targetType == WalkerType.Hyena)
            {
                walker.Damage = Math.Min(ushort.MaxValue, walker.Damage + 10);
                if (walker.Damage >= 100)
                    Destroy(walker, clearBuildingSlot: false);
            }
            session.ReplaceLogicalChunk("walkers", records);
            changedTiles.Add(targetTile);
            walker.WaitTicks = 7;
            walker.AnimationFrame = (walker.AnimationFrame + 1) % 12;
            return true;
        }

        if (walker.route.Count == 0 || walker.RoutePosition >= walker.route.Count)
        {
            if (!TryRouteBeside(walker.Tile, targetTile, out List<int> route))
            {
                walker.TargetWalkerId = 0;
                return false;
            }
            walker.route = route;
            walker.RoutePosition = 0;
        }
        AdvanceAlongRoute(walker);
        return true;
    }

    private int FindNearestHostileWalker(
        byte[] records, int fromTile, out WalkerType targetType, out int targetTile)
    {
        int bestId = 0;
        int bestDistance = 13;
        targetType = default;
        targetTile = 0;
        for (int id = 1; id < MaximumWalkers; id++)
        {
            if (!TryGetHostileWalker(records, id, out WalkerType type, out int tile))
                continue;
            int distance = ChebyshevDistance(fromTile, tile);
            if (distance >= bestDistance)
                continue;
            bestId = id;
            bestDistance = distance;
            targetType = type;
            targetTile = tile;
        }
        return bestId;
    }

    private bool TryGetHostileWalker(
        byte[] records, int id, out WalkerType type, out int tile)
    {
        type = default;
        tile = 0;
        if (id <= 0 || id >= MaximumWalkers)
            return false;
        ReadOnlySpan<byte> record = records.AsSpan(id * RecordSize, RecordSize);
        type = (WalkerType)record[10];
        if (record[14] == 0 ||
            (type != WalkerType.Thief && type != WalkerType.Crocodile && type != WalkerType.Hyena))
            return false;
        return TryReadCurrentTile(record, out tile);
    }

    private bool TryRouteBeside(int start, int target, out List<int> route)
    {
        route = [];
        List<int> goals = [];
        ReadOnlySpan<byte> terrain =
            Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        if (TryLocal(target, out int tx, out int ty))
        {
            for (int y = ty - 1; y <= ty + 1; y++)
            for (int x = tx - 1; x <= tx + 1; x++)
                if ((x != tx || y != ty) && TryTile(x, y, out int tile) &&
                    IsPassable(tile, terrain, goal: false))
                    goals.Add(tile);
        }
        return goals.Count > 0 && TryFindRoute(start, goals, out route);
    }

    private void ReleaseSerializedRoute(int routeId)
    {
        if (routeId <= 0 || routeId >= MaximumRoutes)
            return;
        byte[] owners = Required("route_walkers", MaximumRoutes * sizeof(short)).ToArray();
        byte[] paths = Required("route_paths", MaximumRoutes * RouteCapacity).ToArray();
        ReleaseRoute(routeId, owners, paths);
        session.ReplaceLogicalChunk("route_walkers", owners);
        session.ReplaceLogicalChunk("route_paths", paths);
    }

    private static int ChebyshevDistance(int first, int second)
    {
        int dx = Math.Abs(first % LegacyCityMapSnapshot.MapLength -
                          second % LegacyCityMapSnapshot.MapLength);
        int dy = Math.Abs(first / LegacyCityMapSnapshot.MapLength -
                          second / LegacyCityMapSnapshot.MapLength);
        return Math.Max(dx, dy);
    }

    [OriginalAddress(0x004B1DF0, "FUN_004b1df0")]
    [OriginalAddress(0x004B1EF0, "FUN_004b1ef0")]
    [OriginalAddress(0x0047E340, "FUN_0047e340")]
    private bool TryBuildNextSafetyPatrolStep(Walker walker)
    {
        ReadOnlySpan<byte> terrain = Required(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        if (!TryLocal(walker.Tile, out int x, out int y) || !IsRoad(walker.Tile, terrain))
            return false;
        if (!walker.RoamInitialized) InitializeServiceRoam(walker);
        if (!walker.RoamFree)
        {
            if (walker.Tile != walker.DestinationTile && TryFindRoute(walker.Tile, [walker.DestinationTile], out List<int> approach, roadsOnly: true, allowDiagonal: false))
            { walker.route = approach; walker.RoutePosition = 0; return true; }
            walker.RoamFree = true; walker.RoamTurnCountdown = 100;
        }
        Span<int> candidates = stackalloc int[8];
        candidates.Clear();
        int count = 0;
        foreach (int direction in CardinalDirectionIndices)
        {
            (int dx, int dy) = Directions[direction];
            if (!TryTile(x + dx, y + dy, out int next) ||
                !IsRoad(next, terrain) || !IsPassable(next, terrain, goal: false))
                continue;
            candidates[direction] = next;
            count++;
        }
        if (count == 0)
            return false;

        if (count > 2)
        {
            int least = 7;
            foreach (int direction in CardinalDirectionIndices)
                if (candidates[direction] != 0) least = Math.Min(least, GetRoamingVisit(walker.Type, candidates[direction]));
            foreach (int direction in CardinalDirectionIndices)
                if (candidates[direction] != 0 && GetRoamingVisit(walker.Type, candidates[direction]) > least)
                { candidates[direction] = 0; count--; }
        }
        int selectedDirection = SelectRoamingDirection(walker, candidates, count);
        if (selectedDirection < 0) return false;
        int selected = candidates[selectedDirection];
        walker.route = [selected];
        walker.RoutePosition = 0;
        return true;
    }

    private bool TryBuildSafetyReturn(Walker walker)
    {
        ReadOnlySpan<byte> buildings = Required(
            "buildings", MaximumBuildings * BuildingRecordSize).Span;
        int offset = walker.HomeBuildingId * BuildingRecordSize;
        if ((uint)offset >= buildings.Length || buildings[offset] == 0 ||
            !TryGetBuildingRoadAccess(buildings, offset, out int accessTile))
            return false;
        walker.DestinationTile = accessTile;
        if (walker.Tile == accessTile)
            return true;

        ReadOnlySpan<byte> terrain = Required(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        if (IsRoad(walker.Tile, terrain) &&
            TryFindRoute(walker.Tile, [accessTile], out List<int> roadRoute,
                roadsOnly: true, allowDiagonal: false))
        {
            walker.route = roadRoute;
            walker.RoutePosition = 0;
            walker.TerrainUsage = WalkerTerrainUsage.Roads;
            return true;
        }

        // Emergency responders may finish beside a burning building off-road.
        // Rejoin the nearest connected road, then remain on roads to the post.
        if (!TryLocal(walker.Tile, out int wx, out int wy))
            return false;
        for (int radius = 1; radius <= 4; radius++)
        {
            List<int> roadCandidates = [];
            for (int y = wy - radius; y <= wy + radius; y++)
            for (int x = wx - radius; x <= wx + radius; x++)
            {
                if (Math.Max(Math.Abs(x - wx), Math.Abs(y - wy)) != radius ||
                    !TryTile(x, y, out int candidate) || !IsRoad(candidate, terrain))
                    continue;
                roadCandidates.Add(candidate);
            }
            foreach (int road in roadCandidates.OrderBy(tile => OctileDistance(walker.Tile, tile)))
            {
                if (!TryFindRoute(walker.Tile, [road], out List<int> approach) ||
                    !TryFindRoute(road, [accessTile], out List<int> home,
                        roadsOnly: true, allowDiagonal: false))
                    continue;
                approach.AddRange(home);
                walker.route = approach;
                walker.RoutePosition = 0;
                walker.TerrainUsage = WalkerTerrainUsage.PreferRoads;
                return true;
            }
        }
        return false;
    }

    private bool TryGetBuildingRoadAccess(
        ReadOnlySpan<byte> buildings, int offset, out int accessTile)
    {
        accessTile = -1;
        ReadOnlySpan<byte> terrain = Required(
            "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        int roadX = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 38)..]);
        int roadY = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 40)..]);
        if (TryTile(roadX, roadY, out int stored) && IsRoad(stored, terrain))
        {
            accessTile = stored;
            return true;
        }

        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings[(offset + 12)..]);
        int size = Math.Max(1, (int)buildings[offset + 3]);
        return LegacyBuildingRoadAccess.TryFindNearbyRoad(
            terrain, width, height, origin, anchor, size, out accessTile);
    }

    private bool TryBuildSafetyPatrol(Walker walker)
    {
        ReadOnlySpan<byte> terrain = Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        if (!TryLocal(walker.Tile, out _, out _))
            return false;

        // Regular service walkers leave their one-cell building directly for
        // the road and stay on the road network for the whole patrol. One
        // bounded breadth-first pass finds a distant reachable road without
        // running A* separately for every candidate tile.
        const int maximumPatrolDistance = 64;
        Queue<int> open = new();
        Dictionary<int, int> cameFrom = [];
        Dictionary<int, int> distances = new() { [walker.Tile] = 0 };
        open.Enqueue(walker.Tile);
        int destination = walker.Tile;
        int destinationDistance = 0;
        int patrolSeed = walker.Id + walker.RerouteCounter;
        while (open.TryDequeue(out int current))
        {
            int distance = distances[current];
            if (distance > destinationDistance ||
                (distance == destinationDistance &&
                 Math.Abs(current + patrolSeed) % 31 < Math.Abs(destination + patrolSeed) % 31))
            {
                destination = current;
                destinationDistance = distance;
            }
            if (distance >= maximumPatrolDistance || !TryLocal(current, out int x, out int y))
                continue;
            foreach (int direction in CardinalDirectionIndices)
            {
                (int dx, int dy) = Directions[direction];
                if (!TryTile(x + dx, y + dy, out int next) ||
                    distances.ContainsKey(next) || !IsRoad(next, terrain) ||
                    !IsPassable(next, terrain, goal: false))
                    continue;
                distances[next] = distance + 1;
                cameFrom[next] = current;
                open.Enqueue(next);
            }
        }
        if (destination == walker.Tile)
            return false;

        List<int> route = [];
        for (int current = destination; current != walker.Tile; current = cameFrom[current])
            route.Add(current);
        route.Reverse();
        walker.route = route;
        walker.RoutePosition = 0;
        walker.RerouteCounter++;
        walker.WaitTicks = 0;
        return true;
    }

    /// <summary>
    /// Mirrors FUN_00474700 -> FUN_00459020. Each occupied building selected
    /// for demolition produces one type-3 vagrant carrying its whole population.
    /// This happens when demolition is committed, before delayed footprint cleanup.
    /// </summary>
    [OriginalAddress(0x00474700, "FUN_00474700")]
    [OriginalAddress(0x00459020, "FUN_00459020")]
    public DemolitionDisplacement DisplaceDemolishedResidents(
        IReadOnlyList<int> pendingBuildingIds)
    {
        ArgumentNullException.ThrowIfNull(pendingBuildingIds);
        byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        int spawned = 0;
        int displaced = 0;
        HashSet<int> affected = [];

        foreach (int buildingId in pendingBuildingIds.Distinct())
        {
            if (buildingId <= 0 || buildingId >= MaximumBuildings)
                continue;
            int offset = buildingId * BuildingRecordSize;
            int population = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 28));
            // FUN_00474700 checks the occupied/physical record byte at +5 and
            // the population field, after changing the building to state 6.
            if (buildings[offset] != 6 || buildings[offset + 5] == 0 || population <= 0)
                continue;

            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
            if (!TryLocal(tile, out _, out _))
                continue;

            int id = AllocateId();
            if (id != 0)
            {
                active.Add(new Walker(id, WalkerType.Homeless, tile)
                {
                    SourceTile = tile,
                    DestinationTile = ReadExitTile(),
                    ActionState = HomelessBehavior.SeekingHousing,
                    WaitTicks = 0,
                    Direction = 0,
                    MigrantPeople = unchecked((byte)population),
                    TerrainUsage = WalkerTerrainUsage.PreferRoads
                });
                spawned++;
            }

            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 28), 0);
            UpdateCityPopulation(-population);
            TransferDisplacedPopulation(population);
            displaced += population;
            affected.Add(tile);
        }

        session.ReplaceLogicalChunk("buildings", buildings);
        if (spawned != 0)
            PublishWalkerRecords();
        return new DemolitionDisplacement(spawned, displaced, affected.Order().ToArray());
    }

    internal bool IsValidHouse(int buildingId)
    {
        if ((uint)buildingId >= MaximumBuildings || buildingId == 0)
            return false;
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).Span;
        int offset = buildingId * BuildingRecordSize;
        BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
            buildings[(offset + 16)..]);
        return buildings[offset] is 1 or 3 && type.IsHousing();
    }

    [OriginalAddress(0x00458C30, "FUN_00458c30")]
    [OriginalAddress(0x0048A720, "vagrant home search and road access")]
    internal bool TryReserveHomeForHomeless(Walker walker, out bool missingRoad)
    {
        missingRoad = false;
        byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        int bestId = 0;
        int bestDistance = 1000;
        if (!TryLocal(walker.Tile, out int walkerX, out int walkerY))
            return false;

        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * BuildingRecordSize;
            BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
                buildings.AsSpan(offset + 16));
            if (buildings[offset] != 1 || buildings[offset + 5] == 0 || !type.IsHousing() ||
                BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 32)) <= 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(buildings.AsSpan(offset + 46)) != 0 ||
                GetAvailableHousing(buildings, offset, (int)type) <= 0)
                continue;
            int x = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 6));
            int y = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 8));
            int distance = Math.Max(Math.Abs(x - walkerX), Math.Abs(y - walkerY));
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestId = id;
        }
        if (bestId == 0)
            return false;

        int bestOffset = bestId * BuildingRecordSize;
        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(bestOffset + 12));
        if (!LegacyBuildingRoadAccess.TryFindNearbyRoad(
                Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span,
                width, height, origin, anchor, Math.Max(1, (int)buildings[bestOffset + 3]),
                out int roadTile, radius: 2))
        {
            missingRoad = true;
            return false;
        }
        BinaryPrimitives.WriteUInt16LittleEndian(
            buildings.AsSpan(bestOffset + 46), checked((ushort)walker.Id));
        session.ReplaceLogicalChunk("buildings", buildings);
        walker.HomeBuildingId = bestId;
        walker.DestinationBuildingId = bestId;
        walker.DestinationTile = roadTile;
        walker.route.Clear();
        walker.RoutePosition = 0;
        walker.ProgressOnTile = 0;
        return true;
    }

    internal bool IsReservedHomeValid(Walker walker)
    {
        if (walker.HomeBuildingId <= 0 || walker.HomeBuildingId >= MaximumBuildings)
            return false;
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).Span;
        int offset = walker.HomeBuildingId * BuildingRecordSize;
        BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(
            buildings[(offset + 16)..]);
        return buildings[offset] == 1 && type.IsHousing() &&
               BinaryPrimitives.ReadUInt16LittleEndian(buildings[(offset + 46)..]) == walker.Id &&
               GetAvailableHousing(buildings, offset, (int)type) > 0;
    }

    internal void ReleaseHomelessReservation(Walker walker)
    {
        if (walker.HomeBuildingId <= 0 || walker.HomeBuildingId >= MaximumBuildings)
            return;
        byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        int offset = walker.HomeBuildingId * BuildingRecordSize;
        if (BinaryPrimitives.ReadUInt16LittleEndian(buildings.AsSpan(offset + 46)) == walker.Id)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buildings.AsSpan(offset + 46), 0);
            session.ReplaceLogicalChunk("buildings", buildings);
        }
        walker.HomeBuildingId = 0;
        walker.DestinationBuildingId = 0;
    }

    [OriginalAddress(0x0048AB13, "Type-3 enter-house state")]
    [OriginalAddress(0x0046CEA0, "reset an empty receiving house to type 10")]
    [OriginalAddress(0x004E91E0, "transfer displaced residents into city population")]
    internal void EnterReservedHome(Walker walker)
    {
        if (!IsReservedHomeValid(walker))
        {
            ReleaseHomelessReservation(walker);
            Destroy(walker, clearBuildingSlot: false);
            return;
        }
        byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
        int offset = walker.HomeBuildingId * BuildingRecordSize;
        int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
        int population = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 28));
        int available = GetAvailableHousing(buildings, offset, type);
        int admitted = Math.Min(walker.MigrantPeople, available);
        if (admitted > 0)
        {
            if (population == 0)
            {
                type = 10;
                BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 16), 10);
                BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 18), 0);
            }
            int newPopulation = checked(population + admitted);
            BinaryPrimitives.WriteInt16LittleEndian(
                buildings.AsSpan(offset + 28), checked((short)newPopulation));
            BinaryPrimitives.WriteInt16LittleEndian(
                buildings.AsSpan(offset + 30),
                checked((short)(available - admitted)));
            UpdateCityPopulation(admitted);
            TransferDisplacedPopulation(-admitted);
            populationDeltaThisAdvance += admitted;
            arrivedThisAdvance += admitted;

            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
            byte[] images = Required("image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)),
                HousingManager.GetImageId(
                    type, newPopulation, buildings[offset + 4] != 0, ReadRandom(tile)));
            session.ReplaceLogicalChunk("image_grid", images);
            changedTiles.Add(tile);
            housingImageChangedTiles.Add(tile);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(buildings.AsSpan(offset + 46), 0);
        session.ReplaceLogicalChunk("buildings", buildings);
        Destroy(walker, clearBuildingSlot: false);
    }

    [OriginalAddress(0x00458440, "capacity excludes diseased houses")]
    private int GetAvailableHousing(
        ReadOnlySpan<byte> buildings,
        int offset,
        int type,
        int? population = null)
    {
        if (buildings[offset + 135] != 0)
            return 0;
        int occupied = population ?? BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 28)..]);
        if (housingLevels is null)
            return Math.Max(0, (int)BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 30)..]));
        int capacity = housingLevels[type - 10].PopulationCapacity;
        if (buildings[offset + 4] != 0)
            capacity *= 4;
        return Math.Max(0, capacity - occupied);
    }

    [OriginalAddress(0x00489CA0,"immigrant selects the house road entrance before travelling in free land mode")]
    [OriginalAddress(0x0047FE60,"radius-two road entrance selection")]
    internal bool TryBuildRoute(Walker walker)
    {
        if (!TryGetHouse(walker.HomeBuildingId, out int houseTile, out int houseSize))
            return false;
        ReadOnlySpan<byte> buildings=Required("buildings",MaximumBuildings*BuildingRecordSize).Span;
        if(!TryGetBuildingRoadAccess(buildings,walker.HomeBuildingId*BuildingRecordSize,out int entrance))
            return false;
        if (!TryFindRoute(walker.Tile,[entrance],out List<int> route,usage:WalkerTerrainUsage.Any))
            return false;
        walker.route = route;
        walker.RoutePosition = 0;
        walker.DestinationTile = entrance;
        return true;
    }

    [OriginalAddress(0x0048A720, "route the vagrant to the selected road entrance")]
    internal bool TryBuildHomelessRoute(Walker walker)
    {
        if (!TryFindRoute(walker.Tile, [walker.DestinationTile], out List<int> route,usage:walker.TerrainUsage))
            return false;
        walker.route = route;
        walker.RoutePosition = 0;
        return true;
    }

    [OriginalAddress(0x0048A720, "begin the final off-road house approach")]
    internal void PrepareHomelessHouseEntry(Walker walker)
    {
        PrepareResidentHouseEntry(walker);
    }

    [OriginalAddress(0x0048A260, "find a road within five cells of the source house")]
    [OriginalAddress(0x0047FE60, "FUN_0047fe60")]
    internal bool TryPrepareEmigrantRoadExit(Walker walker)
    {
        if (IsRoad(walker.Tile, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span))
        { BeginResidentApproach(walker, walker.Tile); return true; }
        if (!LegacyBuildingRoadAccess.TryFindNearbyRoad(
                Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span,
                width, height, origin, walker.Tile, 1, out int road, radius: 5))
            return false;
        walker.DestinationTile = road;
        walker.ProgressOnTile = 0;
        return true;
    }

    internal bool TryBuildExitRoute(Walker walker)
    {
        int exit = ReadExitTile();
        if (!TryFindRoute(walker.Tile, [exit], out List<int> route,usage:walker.TerrainUsage))
            return false;
        walker.route = route;
        walker.RoutePosition = 0;
        walker.DestinationTile = exit;
        return true;
    }

    internal bool AdvanceAlongRoute(Walker walker, bool advanceAnimation = true)
    {
        if (walker.RoutePosition >= walker.route.Count)
            return true;
        int pending=walker.route[walker.RoutePosition];
        if(!CanTakeRouteStep(walker.Tile,pending,walker.TerrainUsage))
        {
            walker.route.Clear();walker.RoutePosition=0;walker.ProgressOnTile=0;
            return false;
        }
        if (walker.ProgressOnTile == 0)
        {
            walker.PreviousDirection = walker.Direction;
            walker.Direction = DirectionBetween(walker.Tile, walker.route[walker.RoutePosition]);
        }
        walker.ProgressOnTile++;
        if (advanceAnimation) walker.AnimationFrame = (walker.AnimationFrame + 1) % 12;
        if (walker.ProgressOnTile < MovementUnitsPerTile)
            return false;
        RecordRoamingVisit(walker);
        int next = walker.route[walker.RoutePosition++];
        walker.PreviousTile = walker.Tile;
        walker.Tile = next;
        walker.ProgressOnTile = 0;
        changedTiles.Add(walker.PreviousTile);
        changedTiles.Add(next);
        if (walker.RoutePosition < walker.route.Count)
        {
            walker.PreviousDirection = walker.Direction;
            walker.Direction = DirectionBetween(walker.Tile, walker.route[walker.RoutePosition]);
        }
        return walker.RoutePosition >= walker.route.Count;
    }

    [OriginalAddress(0x0046CEA0, "updates the occupied house image")]
    [OriginalAddress(0x004E90F0, "updates city population")]
    internal void EnterHouse(Walker walker)
    {
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        int offset = walker.HomeBuildingId * BuildingRecordSize;
        int population = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 28)..]);
        int added = Math.Max(0, walker.MigrantPeople);
        buildings[offset] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(buildings[(offset + 28)..],
            checked((short)Math.Min(short.MaxValue, population + added)));
        BinaryPrimitives.WriteUInt16LittleEndian(buildings[(offset + 46)..], 0);
        int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings[(offset + 12)..]);

        Span<byte> images = session.GetWritableLogicalChunk("image_grid").Span;
        BinaryPrimitives.WriteInt32LittleEndian(images[(tile * sizeof(int))..],
            HousingManager.GetImageId(
                BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]),
                population + added, buildings[offset + 4] != 0, ReadRandom(tile)));
        UpdateCityPopulation(added);
        populationDeltaThisAdvance += added;
        arrivedThisAdvance += added;
        changedTiles.Add(tile);
        housingImageChangedTiles.Add(tile);
        Destroy(walker, clearBuildingSlot: false);
    }

    internal void DepartCity(Walker walker) => Destroy(walker, clearBuildingSlot: false);

    internal void Destroy(Walker walker, bool clearBuildingSlot = true)
    {
        if (walker.State == WalkerState.Dead)
            return;
        if (walker.Type == WalkerType.Homeless)
            ReleaseHomelessReservation(walker);
        walker.State = WalkerState.Dead;
        if (clearBuildingSlot && IsValidHouse(walker.HomeBuildingId))
        {
            byte[] buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).ToArray();
            int offset = walker.HomeBuildingId * BuildingRecordSize;
            int slot = walker.Type == WalkerType.Immigrant ? 46 : 42;
            if (BinaryPrimitives.ReadUInt16LittleEndian(buildings.AsSpan(offset + slot)) == walker.Id)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buildings.AsSpan(offset + slot), 0);
                session.ReplaceLogicalChunk("buildings", buildings);
            }
        }
        changedTiles.Add(walker.Tile);
        removedThisAdvance++;
    }

    private IEnumerable<int> EnumerateHouseApproachTiles(int houseTile, int size)
    {
        byte[] terrain = Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        if (!TryLocal(houseTile, out int hx, out int hy))
            yield break;
        const int radius = 1;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = hy - radius; y <= hy + size - 1 + radius; y++)
            for (int x = hx - radius; x <= hx + size - 1 + radius; x++)
            {
                bool border = x == hx - radius || x == hx + size - 1 + radius ||
                              y == hy - radius || y == hy + size - 1 + radius;
                if (!border || !TryTile(x, y, out int tile) || !IsPassable(tile, terrain, false))
                    continue;
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * 4)..]);
                bool road = (flags & 0x40) != 0;
                if ((pass == 0 && road) || (pass == 1 && !road))
                    yield return tile;
            }
        }
    }

    private int FindNearestHouseFootprintTile(int from, int houseTile, int size)
    {
        if (!TryLocal(from, out int fromX, out int fromY) ||
            !TryLocal(houseTile, out int houseX, out int houseY))
            return houseTile;
        int x = Math.Clamp(fromX, houseX, houseX + size - 1);
        int y = Math.Clamp(fromY, houseY, houseY + size - 1);
        return TryTile(x, y, out int tile) ? tile : houseTile;
    }

    [OriginalAddress(0x004B2D20,"select routing permissions and road-first mode3 fallback")]
    [OriginalAddress(0x00517F20,"free land connectivity")]
    [OriginalAddress(0x005181C0,"animal connectivity")]
    [RewriteOnly("Bounded A* adapter retains the existing diagonal costs and corner guards")]
    private bool TryFindRoute(
        int start,
        IReadOnlyCollection<int> goals,
        out List<int> route,
        bool roadsOnly = false,
        bool allowDiagonal = true,
        WalkerTerrainUsage usage = WalkerTerrainUsage.Any)
    {
        if(usage==WalkerTerrainUsage.PreferRoads&&!roadsOnly&&
            TryFindRoute(start,goals,out route,roadsOnly:true,allowDiagonal:false,usage:WalkerTerrainUsage.Roads))return true;
        route = [];
        HashSet<int> goalSet = [.. goals.Where(tile=>TryLocal(tile,out _,out _)&&LegacyTerrainAccess.CanWalk(session.LogicalChunks,tile,usage))];
        if(goalSet.Count==0)return false;
        if (goalSet.Contains(start))
            return true;
        ReadOnlySpan<byte> terrain = Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span;
        PriorityQueue<int, int> open = new();
        Dictionary<int, int> cameFrom = [];
        Dictionary<int, int> costs = new() { [start] = 0 };
        open.Enqueue(start, 0);
        while (open.TryDequeue(out int current, out _))
        {
            if (goalSet.Contains(current))
            {
                while (current != start)
                {
                    route.Add(current);
                    current = cameFrom[current];
                }
                route.Reverse();
                return true;
            }
            if (!TryLocal(current, out int cx, out int cy))
                continue;
            for (int direction = 0; direction < Directions.Length; direction++)
            {
                (int dx, int dy) = Directions[direction];
                if (!allowDiagonal && dx != 0 && dy != 0)
                    continue;
                if (!TryTile(cx + dx, cy + dy, out int next) ||
                    !LegacyTerrainAccess.CanWalk(session.LogicalChunks,next,usage) ||
                    (roadsOnly && !IsRoad(next, terrain)))
                    continue;
                if (dx != 0 && dy != 0 &&
                    (!TryTile(cx + dx, cy, out int sideA) || !TryTile(cx, cy + dy, out int sideB) ||
                     !LegacyTerrainAccess.CanWalk(session.LogicalChunks,sideA,usage) || !LegacyTerrainAccess.CanWalk(session.LogicalChunks,sideB,usage) ||
                     (roadsOnly && (!IsRoad(sideA, terrain) || !IsRoad(sideB, terrain)))))
                    continue;
                int nextCost = costs[current] + (dx == 0 || dy == 0 ? 10 : 14);
                if (costs.TryGetValue(next, out int oldCost) && oldCost <= nextCost)
                    continue;
                costs[next] = nextCost;
                cameFrom[next] = current;
                int heuristic = goalSet.Min(goal => OctileDistance(next, goal));
                open.Enqueue(next, nextCost + heuristic);
            }
        }
        return false;
    }

    [OriginalAddress(0x00516F90,"shared native land-routing classification; destinations do not bypass blocked terrain")]
    private bool IsPassable(int tile,ReadOnlySpan<byte> terrain,bool goal) =>
        LegacyTerrainAccess.CanWalk(session.LogicalChunks,tile,WalkerTerrainUsage.Any);

    [OriginalAddress(0x004B3530,"revalidate movement against live terrain before entering a cell")]
    [RewriteOnly("Also enforce bounded diagonal corner guards on restored and dynamically blocked routes")]
    private bool CanTakeRouteStep(int from,int to,WalkerTerrainUsage usage)
    {
        if(!TryLocal(from,out int x,out int y)||!TryLocal(to,out int tx,out int ty)||
            !LegacyTerrainAccess.CanWalk(session.LogicalChunks,to,usage))return false;
        int dx=tx-x,dy=ty-y;
        if(Math.Abs(dx)>1||Math.Abs(dy)>1)return false;
        return dx==0||dy==0||
            (TryTile(x+dx,y,out int a)&&TryTile(x,y+dy,out int b)&&
             LegacyTerrainAccess.CanWalk(session.LogicalChunks,a,usage)&&LegacyTerrainAccess.CanWalk(session.LogicalChunks,b,usage));
    }

    private static bool IsRoad(int tile, ReadOnlySpan<byte> terrain) =>
        (BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * sizeof(uint))..]) & 0x40) != 0;

    private static int OctileDistance(int first, int second)
    {
        int ax = first % LegacyCityMapSnapshot.MapLength;
        int ay = first / LegacyCityMapSnapshot.MapLength;
        int bx = second % LegacyCityMapSnapshot.MapLength;
        int by = second / LegacyCityMapSnapshot.MapLength;
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }

    private static int DirectionBetween(int from, int to)
    {
        int dx = to % LegacyCityMapSnapshot.MapLength - from % LegacyCityMapSnapshot.MapLength;
        int dy = to / LegacyCityMapSnapshot.MapLength - from / LegacyCityMapSnapshot.MapLength;
        for (int index = 0; index < Directions.Length; index++)
            if (Directions[index] == (dx, dy))
                return index;
        return 0;
    }

    private int AllocateId()
    {
        HashSet<int> used = active.Select(walker => walker.Id).ToHashSet();
        ReadOnlySpan<byte> records = Required("walkers", MaximumWalkers * RecordSize).Span;
        for (int id = 1; id < MaximumWalkers; id++)
            if (!used.Contains(id) && records[id * RecordSize + 14] == 0)
                return id;
        return 0;
    }

    [RewriteOnly("Restores supported native walker records and schedules pending deletion for cleanup")]
    private void LoadExistingMigrants()
    {
        ReadOnlySpan<byte> records = Required("walkers", MaximumWalkers * RecordSize).Span;
        for (int id = 1; id < MaximumWalkers; id++)
        {
            ReadOnlySpan<byte> record = records.Slice(id * RecordSize, RecordSize);
            WalkerType type = (WalkerType)record[10];
            if ((type != WalkerType.Immigrant && type != WalkerType.Emigrant &&
                 type != WalkerType.Homeless &&
                 type != WalkerType.Architect && type != WalkerType.Fireman &&
                 type != WalkerType.Policeman && type != WalkerType.Ostrich &&
                 type != WalkerType.OstrichHunter && type != WalkerType.HunterArrow &&
                 type != WalkerType.Recruiter && type != WalkerType.CartPusher && !IsDistributionType(type)))
                continue;
            if (type == WalkerType.CartPusher && record[11] != 8) continue;
            if (record[14] != 1)
            {
                if (record[14] == 2)
                {
                    serializedMigrantIds.Add(id);
                    int home = BinaryPrimitives.ReadInt16LittleEndian(record[92..]);
                    if ((type == WalkerType.Immigrant || type == WalkerType.Homeless) &&
                        home > 0 && home < MaximumBuildings)
                    {
                        Span<byte> house = session.GetWritableLogicalChunk("buildings").Span
                            .Slice(home * BuildingRecordSize, BuildingRecordSize);
                        if (BinaryPrimitives.ReadUInt16LittleEndian(house[46..]) == id)
                            BinaryPrimitives.WriteUInt16LittleEndian(house[46..], 0);
                    }
                }
                continue;
            }
            if (!TryReadCurrentTile(record, out int tile))
                continue;
            Walker walker = new(id, type, tile)
            {
                NativeRecord = record.ToArray(),
                State = WalkerState.Alive,
                PreviousTile = ReadCoordinateTile(record, 24, tile),
                DestinationTile = ReadCoordinateTile(record, 36, tile),
                SourceTile = ReadCoordinateTile(record, 44, tile),
                ProgressOnTile = Math.Min((int)record[57], MovementUnitsPerTile - 1),
                Direction = unchecked((sbyte)record[17]),
                PreviousDirection = unchecked((sbyte)record[18]),
                Damage = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]),
                AnimationFrame = record[124] % 12,
                WaitTicks = BinaryPrimitives.ReadInt16LittleEndian(record[54..]),
                ActionState = record[56],
                HomeBuildingId = BinaryPrimitives.ReadInt16LittleEndian(record[92..]),
                DestinationBuildingId = BinaryPrimitives.ReadUInt16LittleEndian(record[94..]),
                TargetWalkerId = BinaryPrimitives.ReadInt16LittleEndian(record[129..]),
                // FUN_00459020 stores the carried population as a char at +0x64.
                MigrantPeople = record[100],
                RerouteCounter = record[151],
                TerrainUsage = Enum.IsDefined((WalkerTerrainUsage)record[114])
                    ? (WalkerTerrainUsage)record[114]
                    : WalkerTerrainUsage.Any
            };
            RestoreHuntingFields(record, walker);
            RestoreWorkplaceWalkerFields(record,walker);
            RestoreDistributionFields(record, walker);
            RestoreResidentApproach(record, walker);
            RestoreSerializedRoute(record, walker);
            if (IsResidentApproachState(walker)) { walker.route.Clear(); walker.RoutePosition = 0; }
            active.Add(walker);
            serializedMigrantIds.Add(id);
            if (type == WalkerType.Immigrant && IsValidHouse(walker.HomeBuildingId))
            {
                // Migrate the slot used by earlier rewrite saves, only when
                // it demonstrably belongs to this restored immigrant.
                Span<byte> house = session.GetWritableLogicalChunk("buildings").Span
                    .Slice(walker.HomeBuildingId * BuildingRecordSize, BuildingRecordSize);
                if (BinaryPrimitives.ReadUInt16LittleEndian(house[46..]) == 0 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(house[42..]) == id)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(house[46..], checked((ushort)id));
                    BinaryPrimitives.WriteUInt16LittleEndian(house[42..], 0);
                }
            }
        }
    }

    [OriginalAddress(0x004B2D20, "FUN_004b2d20")]
    [OriginalAddress(0x004B33D0, "FUN_004b33d0")]
    private void RestoreSerializedRoute(ReadOnlySpan<byte> record, Walker walker)
    {
        int routeId = BinaryPrimitives.ReadInt16LittleEndian(record[58..]);
        int routePosition = BinaryPrimitives.ReadInt16LittleEndian(record[60..]);
        int routeLength = BinaryPrimitives.ReadInt16LittleEndian(record[62..]);
        if (routeId <= 0 || routeId >= MaximumRoutes ||
            routePosition < 0 || routeLength < routePosition || routeLength > RouteCapacity ||
            !session.LogicalChunks.TryGetValue("route_paths", out ReadOnlyMemory<byte> routePaths) ||
            routePaths.Length < MaximumRoutes * RouteCapacity)
            return;

        walker.SerializedRouteId = routeId;

        // FUN_004b27e0 selects a direction through FUN_004b33d0 and then
        // increments +0x3c. The saved route position therefore points after
        // the direction currently being traversed; +0x11 retains that active
        // direction and +0x39 retains its sub-tile phase.
        List<int> restored = new(routeLength - routePosition + 1);
        int current = walker.Tile;
        if (routePosition > 0 && (uint)walker.Direction < Directions.Length)
        {
            (int dx, int dy) = Directions[walker.Direction];
            if (!TryLocal(current, out int x, out int y) ||
                !TryTile(x + dx, y + dy, out current))
                return;
            restored.Add(current);
        }
        ReadOnlySpan<byte> directions = routePaths.Span.Slice(
            routeId * RouteCapacity + routePosition, routeLength - routePosition);
        foreach (byte direction in directions)
        {
            if (direction >= Directions.Length || !TryLocal(current, out int x, out int y))
                return;
            (int dx, int dy) = Directions[direction];
            if (!TryTile(x + dx, y + dy, out current))
                return;
            restored.Add(current);
        }

        walker.route = restored;
        walker.RoutePosition = 0;
    }

    [OriginalAddress(0x004B4800, "native field initialization including city ownership at +15")]
    private void PublishWalkerRecords()
    {
        Span<byte> records = session.GetWritableLogicalChunk("walkers").Span;
        Span<byte> routeWalkers = session.GetWritableLogicalChunk("route_walkers").Span;
        Span<byte> routePaths = session.GetWritableLogicalChunk("route_paths").Span;
        Span<byte> grid = session.GetWritableLogicalChunk("walker_grid").Span;
        grid.Clear();

        activeIdsScratch.Clear();
        foreach (Walker walker in active)
            activeIdsScratch.Add(walker.Id);
        retiredIdsScratch.Clear();
        foreach (int id in serializedMigrantIds)
            if (!activeIdsScratch.Contains(id))
                retiredIdsScratch.Add(id);
        foreach (int retiredId in retiredIdsScratch)
        {
            Span<byte> retired = records.Slice(retiredId * RecordSize, RecordSize);
            int routeId = BinaryPrimitives.ReadInt16LittleEndian(retired[58..]);
            retired.Clear();
            ReleaseRoute(routeId, routeWalkers, routePaths);
            serializedMigrantIds.Remove(retiredId);
        }

        foreach (Walker walker in active)
        {
            Span<byte> record = records.Slice(walker.Id * RecordSize, RecordSize);
            if (walker.NativeRecord is { } original) original.CopyTo(record);
            else
            {
                record.Clear();
                record[15] = checked((byte)LegacyBuildingOwnership.ReadCityIndex(session.LogicalChunks));
            }
            record.Slice(8, 2).Clear();
            record.Slice(58, 6).Clear();
            record[10] = (byte)walker.Type;
            record[12] = 1;
            record[14] = checked((byte)walker.State);
            record[17] = unchecked((byte)walker.Direction);
            record[18] = unchecked((byte)walker.PreviousDirection);
            WriteCoordinateTile(record, 20, walker.Tile);
            WriteCoordinateTile(record, 24, walker.PreviousTile);
            BinaryPrimitives.WriteUInt16LittleEndian(record[30..], checked((ushort)walker.Damage));
            BinaryPrimitives.WriteInt32LittleEndian(record[32..], walker.Tile);
            WriteCoordinateTile(record, 36, walker.DestinationTile);
            WriteCoordinateTile(record, 44, walker.SourceTile);
            BinaryPrimitives.WriteInt16LittleEndian(record[54..], checked((short)walker.WaitTicks));
            record[56] = checked((byte)walker.ActionState);
            record[57] = checked((byte)walker.ProgressOnTile);
            int remainingRouteLength = walker.route.Count - walker.RoutePosition;
            int routeId = walker.SerializedRouteId;
            if (remainingRouteLength is > 0 and <= RouteCapacity &&
                EnsureRoute(walker, ref routeId, routeWalkers) &&
                TryWriteRemainingRoute(walker, routePaths.Slice(routeId * RouteCapacity, RouteCapacity),
                    remainingRouteLength))
            {
                walker.SerializedRouteId = routeId;
                routePaths.Slice(routeId * RouteCapacity + remainingRouteLength,
                    RouteCapacity - remainingRouteLength).Clear();
                BinaryPrimitives.WriteInt16LittleEndian(record[58..], checked((short)routeId));
                // The first serialized direction is already active. Pharaoh
                // stores +0x3c after it, while +0x11/+0x39 preserve the
                // direction and the progress within that step.
                BinaryPrimitives.WriteInt16LittleEndian(record[60..], 1);
                BinaryPrimitives.WriteInt16LittleEndian(record[62..], checked((short)remainingRouteLength));
                BinaryPrimitives.WriteInt16LittleEndian(
                    routeWalkers.Slice(routeId * sizeof(short)), checked((short)walker.Id));
            }
            else if (routeId > 0)
            {
                ReleaseRoute(routeId, routeWalkers, routePaths);
                walker.SerializedRouteId = 0;
            }
            BinaryPrimitives.WriteInt16LittleEndian(record[92..], checked((short)walker.HomeBuildingId));
            BinaryPrimitives.WriteUInt16LittleEndian(record[94..], checked((ushort)walker.DestinationBuildingId));
            BinaryPrimitives.WriteInt16LittleEndian(record[129..], checked((short)walker.TargetWalkerId));
            record[100] = unchecked((byte)walker.MigrantPeople);
            record[114] = checked((byte)walker.TerrainUsage);
            record[124] = checked((byte)walker.AnimationFrame);
            record[151] = checked((byte)Math.Min(byte.MaxValue, walker.RerouteCounter));
            PublishHuntingFields(record, walker);
            PublishWorkplaceWalkerFields(record,walker);
            PublishDistributionFields(record, walker);
            PublishResidentApproach(record, walker);
            serializedMigrantIds.Add(walker.Id);
        }
        foreach (IGrouping<int, Walker> tileGroup in active.GroupBy(walker => walker.Tile))
        {
            int previous = 0;
            foreach (Walker walker in tileGroup.OrderBy(walker => walker.Id))
            {
                if (previous == 0)
                    BinaryPrimitives.WriteUInt16LittleEndian(grid.Slice(tileGroup.Key * 2), checked((ushort)walker.Id));
                else
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        records.Slice(previous * RecordSize + 8), checked((ushort)walker.Id));
                previous = walker.Id;
            }
        }
    }

    private static void ReleaseRoute(int routeId, Span<byte> owners, Span<byte> paths)
    {
        if (routeId <= 0 || routeId >= MaximumRoutes)
            return;
        owners.Slice(routeId * sizeof(short), sizeof(short)).Clear();
        paths.Slice(routeId * RouteCapacity, RouteCapacity).Clear();
    }

    private static bool EnsureRoute(Walker walker, ref int routeId, Span<byte> owners)
    {
        if (routeId > 0 && routeId < MaximumRoutes)
        {
            int owner = BinaryPrimitives.ReadInt16LittleEndian(owners.Slice(routeId * sizeof(short)));
            if (owner is 0 || owner == walker.Id)
                return true;
        }
        for (int candidate = 1; candidate < MaximumRoutes; candidate++)
        {
            if (BinaryPrimitives.ReadInt16LittleEndian(owners.Slice(candidate * sizeof(short))) != 0)
                continue;
            routeId = candidate;
            return true;
        }
        routeId = 0;
        return false;
    }

    [OriginalAddress(0x004B2D20, "FUN_004b2d20")]
    private bool TryWriteRemainingRoute(Walker walker, Span<byte> destination, int count)
    {
        int current = walker.Tile;
        for (int index = 0; index < count; index++)
        {
            int next = walker.route[walker.RoutePosition + index];
            int dx = next % LegacyCityMapSnapshot.MapLength - current % LegacyCityMapSnapshot.MapLength;
            int dy = next / LegacyCityMapSnapshot.MapLength - current / LegacyCityMapSnapshot.MapLength;
            int direction = Array.IndexOf(Directions, (dx, dy));
            if (direction < 0)
                return false;
            destination[index] = checked((byte)direction);
            current = next;
        }
        return true;
    }

    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    private bool TryReadCurrentTile(ReadOnlySpan<byte> record, out int tile)
    {
        // The legacy record stores current X/Y at 0x14/0x16 and the corresponding
        // 228-wide grid index at 0x20. Reading the coordinate pair as one int creates
        // a large invalid index as soon as Y is non-zero.
        tile = BinaryPrimitives.ReadInt32LittleEndian(record[32..]);
        if (TryLocal(tile, out _, out _))
            return true;

        int x = BinaryPrimitives.ReadInt16LittleEndian(record[20..]);
        int y = BinaryPrimitives.ReadInt16LittleEndian(record[22..]);
        return TryTile(x, y, out tile);
    }

    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    private int ReadCoordinateTile(ReadOnlySpan<byte> record, int coordinateOffset, int fallback)
    {
        int x = BinaryPrimitives.ReadInt16LittleEndian(record[coordinateOffset..]);
        int y = BinaryPrimitives.ReadInt16LittleEndian(record[(coordinateOffset + 2)..]);
        return TryTile(x, y, out int tile) ? tile : fallback;
    }

    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    private void WriteCoordinateTile(Span<byte> record, int coordinateOffset, int tile)
    {
        if (!TryLocal(tile, out int x, out int y))
            x = y = 0;
        BinaryPrimitives.WriteInt16LittleEndian(record[coordinateOffset..], checked((short)x));
        BinaryPrimitives.WriteInt16LittleEndian(record[(coordinateOffset + 2)..], checked((short)y));
    }

    [OriginalAddress(0x004E90F0, "updates city population in the persistent city buffer")]
    private void UpdateCityPopulation(int added)
    {
        if (added == 0)
            return;
        LegacyPopulationAges.ApplyDelta(session, added);
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        int population = BinaryPrimitives.ReadInt32LittleEndian(city[CityPopulationOffset..]);
        BinaryPrimitives.WriteInt32LittleEndian(city[CityPopulationOffset..], checked(population + added));
    }

    [OriginalAddress(0x004E9250, "add demolished-house residents to the displaced population")]
    [OriginalAddress(0x004E91E0, "remove rehoused residents from the displaced population")]
    private void TransferDisplacedPopulation(int delta)
    {
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        int displaced = BinaryPrimitives.ReadInt32LittleEndian(city[DisplacedPopulationOffset..]);
        BinaryPrimitives.WriteInt32LittleEndian(city[DisplacedPopulationOffset..], checked(displaced + delta));
    }

    [OriginalAddress(0x00458D80, "remove emigrating residents from their house")]
    [OriginalAddress(0x0046CF40, "convert an emptied house to vacant housing")]
    private void ApplyEmigrationToHouse(
        byte[] buildings, int offset, int tile, int population, int people)
    {
        int remaining = Math.Max(0, population - people);
        int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
        if (remaining == 0 && buildings[offset + 4] != 0)
        {
            if (ConvertMergedHouseToVacantLots(buildings, offset))
                return;
        }
        if (remaining == 0)
        {
            type = (int)BuildingType.Housing;
            BinaryPrimitives.WriteInt16LittleEndian(
                buildings.AsSpan(offset + 16), (short)BuildingType.Housing);
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 18), 0);
        }
        BinaryPrimitives.WriteInt16LittleEndian(
            buildings.AsSpan(offset + 28), checked((short)remaining));

        byte[] images = Required("image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)),
            HousingManager.GetImageId(
                type, remaining, buildings[offset + 4] != 0, ReadRandom(tile)));
        session.ReplaceLogicalChunk("image_grid", images);
    }

    [OriginalAddress(0x0046CF40, "FUN_0046cf40")]
    private bool ConvertMergedHouseToVacantLots(byte[] buildings, int sourceOffset)
    {
        int sourceId = sourceOffset / BuildingRecordSize;
        int x = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(sourceOffset + 6));
        int y = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(sourceOffset + 8));
        List<int> ids = [sourceId];
        for (int id = 1; id < MaximumBuildings && ids.Count < 4; id++)
            if (id != sourceId && buildings[id * BuildingRecordSize] == 0)
                ids.Add(id);
        if (ids.Count != 4)
            return false;

        byte[] buildingGrid = Required(
            "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).ToArray();
        byte[] images = Required("image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        byte[] bitfields = Required("bitfields_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        byte[] edge = Required("edge_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        for (int index = 0; index < 4; index++)
        {
            int id = ids[index];
            int offset = id * BuildingRecordSize;
            int tileX = x + index % 2;
            int tileY = y + index / 2;
            if (!TryTile(tileX, tileY, out int tile))
                continue;
            Span<byte> record = buildings.AsSpan(offset, BuildingRecordSize);
            if (index != 0)
            {
                record.Clear();
                record[0] = 3;
                record[194] = 0x32;
            }
            record[3] = 1;
            record[4] = 0;
            record[5] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(record[6..], checked((short)tileX));
            BinaryPrimitives.WriteInt16LittleEndian(record[8..], checked((short)tileY));
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], tile);
            BinaryPrimitives.WriteInt16LittleEndian(record[16..], (short)BuildingType.Housing);
            BinaryPrimitives.WriteInt16LittleEndian(record[18..], 0);
            BinaryPrimitives.WriteInt16LittleEndian(record[28..], 0);
            BinaryPrimitives.WriteInt16LittleEndian(
                buildingGrid.AsSpan(tile * sizeof(short)), checked((short)id));
            BinaryPrimitives.WriteInt32LittleEndian(
                images.AsSpan(tile * sizeof(int)), LegacyHousingPlacement.ForSaleImageId);
            bitfields[tile] &= 0xe0;
            edge[tile] = (byte)((edge[tile] & 0x80) | 0x40);
            changedTiles.Add(tile);
            housingImageChangedTiles.Add(tile);
        }
        session.ReplaceLogicalChunk("building_grid", buildingGrid);
        session.ReplaceLogicalChunk("image_grid", images);
        session.ReplaceLogicalChunk("bitfields_grid", bitfields);
        session.ReplaceLogicalChunk("edge_grid", edge);
        return true;
    }

    private int ReadEntryTile()
    {
        uint packed = BinaryPrimitives.ReadUInt32LittleEndian(Required("scenario_info", 912).Span[908..]);
        int x = (short)(packed & 0xffff);
        int y = (short)(packed >> 16);
        return TryTile(x, y, out int tile) ? tile : origin;
    }

    private int ReadExitTile()
    {
        uint packed = BinaryPrimitives.ReadUInt32LittleEndian(Required("scenario_info", 916).Span[912..]);
        int x = (short)(packed & 0xffff);
        int y = (short)(packed >> 16);
        return TryTile(x, y, out int tile) ? tile : origin;
    }

    private byte ReadRandom(int tile) =>
        (uint)tile < LegacyCityMapSnapshot.TileCount
            ? Required("random_grid", LegacyCityMapSnapshot.TileCount).Span[tile]
            : (byte)0;

    private bool TryGetHouse(int buildingId, out int tile, out int size)
    {
        tile = size = 0;
        if (!IsValidHouse(buildingId))
            return false;
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).Span;
        int offset = buildingId * BuildingRecordSize;
        tile = BinaryPrimitives.ReadInt32LittleEndian(buildings[(offset + 12)..]);
        size = Math.Max(1, (int)buildings[offset + 3]);
        return TryLocal(tile, out _, out _);
    }

    private bool TryTile(int x, int y, out int tile)
    {
        if ((uint)x >= width || (uint)y >= height)
        {
            tile = -1;
            return false;
        }
        tile = origin + y * LegacyCityMapSnapshot.MapLength + x;
        return true;
    }

    private bool TryLocal(int tile, out int x, out int y)
    {
        int relative = tile - origin;
        x = relative % LegacyCityMapSnapshot.MapLength;
        y = relative / LegacyCityMapSnapshot.MapLength;
        return relative >= 0 && x >= 0 && x < width && y >= 0 && y < height;
    }

    private ReadOnlyMemory<byte> Required(string name, int minimumLength)
    {
        if (!session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> memory) ||
            memory.Length < minimumLength)
            throw new InvalidDataException($"Logical chunk '{name}' is missing or too short.");
        return memory;
    }
}
