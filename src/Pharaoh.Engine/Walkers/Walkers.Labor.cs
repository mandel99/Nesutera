using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Population;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    private long laborTicks;
    private byte[] laborRoadNetworkPriorities = [];
    private const int LaborCategories = 10;
    private const int CityLaborCategories = 29956;
    private static readonly (int Category, int Quota)[] LaborQuotas =
        [(0,4), (1,3), (6,2), (3,3), (5,1), (8,2), (7,1), (2,1), (4,1)];

    [OriginalAddress(0x00451C50, "workforce phase23, allocation phase25, workplace dispatch phase31")]
    private void AdvanceLabor()
    {
        int phase = (int)(++laborTicks % 51);
        if (phase == 23) RecalculateWorkforce();
        if (phase == 25) AllocateWorkplaceLabor();
        if (phase == 31) DispatchWorkplaceRecruiters();
    }

    [OriginalAddress(0x00458880, "commoner share times sixty percent of eligible age groups")]
    [OriginalAddress(0x004E9460, "age groups20..49 supply the eligible population")]
    private void RecalculateWorkforce()
    {
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * BuildingRecordSize).Span;
        int commoners = 0, residents = 0;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            ReadOnlySpan<byte> b = buildings.Slice(id * BuildingRecordSize, BuildingRecordSize);
            if (b[0] != 1 || !((BuildingType)ReadLaborShort(b,16)).IsHousing()) continue;
            int population = Math.Max(0, (int)ReadLaborShort(b,28));
            residents += population;
            if (ReadLaborShort(b,18) < 14) commoners += population;
        }
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        int eligible = 0;
        for (int age = 20; age < 50; age++) eligible += Math.Max(0, (int)ReadLaborShort(city, LegacyPopulationAges.BucketsOffset + age * 2));
        int share = residents > 0 ? commoners * 100 / residents : 0;
        WriteLaborInt(city,29944,share);
        WriteLaborInt(city,29948,eligible * 60 / 100);
        WriteLaborInt(city,29952,(eligible * 60 / 100) * share / 100);
    }

    [OriginalAddress(0x00459100, "sum workplace demand and recent housing coverage by native labor category")]
    [OriginalAddress(0x00459400, "finite category budgets, explicit priorities and weighted default quotas")]
    [OriginalAddress(0x004595A0, "hundredths-of-percent coverage weights at building short26")]
    [OriginalAddress(0x00459670, "proportional employment followed by the bounded building-id remainder pass")]
    [OriginalAddress(0x00459350, "unemployment display follows increases by at most five percentage points")]
    private void AllocateWorkplaceLabor()
    {
        RebuildLaborRoadPriorities();
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        int[] demand = new int[LaborCategories], access = new int[LaborCategories], counts = new int[LaborCategories];
        int[] allocated = new int[LaborCategories], priorities = new int[LaborCategories];
        List<(int Id, int Category, int Required)> eligible = [];
        for (int category = 0; category < LaborCategories; category++)
            priorities[category] = BinaryPrimitives.ReadInt32LittleEndian(city[(CityLaborCategories + category * 20 + 16)..]);
        for (int id = 1; id < MaximumBuildings; id++)
        {
            Span<byte> b = buildings.Slice(id * BuildingRecordSize, BuildingRecordSize);
            int type = ReadLaborShort(b,16);
            var model = LegacyLaborModelTable.Get(type);
            if (b[0] != 1 || model.Category < 0 || model.Category >= LaborCategories) continue;
            b[66] = (byte)model.Category;
            if (UsesSeparateFarmLabor(type,b) || (model.Category == 3 && ReadLaborShort(b,56) != 0)) continue;
            int required = buildingModels?.GetModel(difficulty,type)?.Employees ?? model.Employees;
            if (required <= 0) continue;
            int coverage = ReadLaborShort(b,24);
            bool road = RefreshWorkplaceRoad(b,type);
            if (!road) { WriteLaborShort(b,24,0); coverage = 0; }
            WriteLaborShort(b,26,0);
            WriteLaborShort(b,64,0);
            if (coverage <= 0 || IsWorkplaceMothballed(type,b,city)) continue;
            demand[model.Category] += required;
            access[model.Category] += coverage;
            counts[model.Category]++;
            eligible.Add((id,model.Category,required));
        }
        int available = Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(city[29952..]));
        int totalDemand = demand.Sum(), remaining = available;
        if (totalDemand <= available) Array.Copy(demand,allocated,LaborCategories);
        else
        {
            for (int priority = 1; priority <= 9 && remaining > 0; priority++)
                for (int category = 0; category < 9; category++)
                    if (priorities[category] == priority)
                    { allocated[category] = Math.Min(remaining,demand[category]); remaining -= allocated[category]; break; }
            // Unlike the original's available-worker-count loop, stop when no category can consume a worker.
            while (remaining > 0)
            {
                int before = remaining;
                foreach (var quota in LaborQuotas)
                {
                    if (priorities[quota.Category] != 0) continue;
                    int amount = Math.Min(remaining, Math.Min(quota.Quota, demand[quota.Category] - allocated[quota.Category]));
                    allocated[quota.Category] += amount; remaining -= amount;
                }
                if (remaining == before) break;
            }
        }
        int[] distributed = new int[LaborCategories];
        foreach (var workplace in eligible)
        {
            Span<byte> b = buildings.Slice(workplace.Id * BuildingRecordSize,BuildingRecordSize);
            int category = workplace.Category;
            int weight = (int)((long)ReadLaborShort(b,24) * 10000 / access[category]);
            WriteLaborShort(b,26,weight);
            int employees = allocated[category] >= demand[category] ? workplace.Required :
                (int)Math.Min(workplace.Required,(long)allocated[category] * weight / 10000);
            WriteLaborShort(b,64,employees);
            distributed[category] += employees;
        }
        foreach (var workplace in eligible)
        {
            if (workplace.Category == 8) continue;
            Span<byte> b = buildings.Slice(workplace.Id * BuildingRecordSize,BuildingRecordSize);
            int employees = ReadLaborShort(b,64);
            int extra = Math.Min(workplace.Required - employees, Math.Max(0,allocated[workplace.Category] - distributed[workplace.Category]));
            WriteLaborShort(b,64,employees + extra); distributed[workplace.Category] += extra;
        }
        for (int category = 0; category < LaborCategories; category++)
        {
            int offset = CityLaborCategories + category * 20;
            WriteLaborInt(city,offset,demand[category]); WriteLaborInt(city,offset+4,allocated[category]);
            WriteLaborInt(city,offset+8,access[category]); WriteLaborInt(city,offset+12,counts[category]);
        }
        int employed = allocated.Sum();
        WriteLaborInt(city,30156,employed); WriteLaborInt(city,30160,Math.Max(0,available-employed));
        int unemployment = available > 0 ? (available-employed)*100/available : 0;
        WriteLaborInt(city,30164,unemployment);
        int previous = BinaryPrimitives.ReadInt32LittleEndian(city[30168..]);
        WriteLaborInt(city,30168,Math.Min(100,unemployment < previous + 5 ? unemployment : previous + 5));
        WriteLaborInt(city,30172,Math.Max(0,totalDemand-employed));
    }

    [OriginalAddress(0x00488050, "floodplain farms receive workers through work camps rather than local recruiters")]
    [OriginalAddress(0x004894A0, "three-by-three farm footprint")]
    [OriginalAddress(0x004893C0, "any footprint cell carrying floodplain terrain")]
    private bool UsesSeparateFarmLabor(int type, ReadOnlySpan<byte> b)
    {
        if (type is not (100 or 101 or 102 or 103 or 104 or 105 or 196 or 224)) return false;
        int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]);
        ReadOnlySpan<byte> terrain = Required("terrain_grid",LegacyCityMapSnapshot.ImageGridLength).Span;
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
        {
            int cell = tile + y * LegacyCityMapSnapshot.MapLength + x;
            if ((uint)cell < LegacyCityMapSnapshot.TileCount && (BinaryPrimitives.ReadUInt32LittleEndian(terrain[(cell*4)..]) & 0x10000) != 0) return true;
        }
        return false;
    }

    [OriginalAddress(0x00459240, "production mothball flag indexed by building output resource")]
    private static bool IsWorkplaceMothballed(int type, ReadOnlySpan<byte> b, ReadOnlySpan<byte> city)
    {
        int category = LegacyLaborModelTable.Get(type).Category;
        if (category is not (0 or 1) && type is not (112 or 205)) return false;
        int resource = b[67];
        return resource < 36 && ReadLaborShort(city,29326 + resource * 2) != 0;
    }

    [OriginalAddress(0x00462180, "shared housing-coverage thresholds before creating the type-five recruiter")]
    [OriginalAddress(0x00467220, "slot44 is repaired or filled with exactly one native recruiter")]
    private void DispatchWorkplaceRecruiters()
    {
        if (BinaryPrimitives.ReadInt32LittleEndian(Required("city_data",18944).Span[18940..]) <= 0) return;
        RebuildLaborRoadPriorities();
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            Span<byte> b = buildings.Slice(id * BuildingRecordSize,BuildingRecordSize);
            int type = ReadLaborShort(b,16);
            var model = LegacyLaborModelTable.Get(type);
            if (b[0] != 1 || model.Category < 0 || model.Employees <= 0 || UsesSeparateFarmLabor(type,b) || !RefreshWorkplaceRoad(b,type)) continue;
            int limit = type is 55 or 71 or 72 or 81 or 167 ? 100 : 50;
            if (ReadLaborShort(b,24) > limit) continue;
            int slot = ReadLaborShort(b,44);
            if (slot > 0)
            {
                if (type is 70 or 190 && active.Any(w => w.Id == slot && w.Type == WalkerType.BazaarBuyer && w.HomeBuildingId == id && w.State == WalkerState.Alive))
                    continue;
                if (!active.Any(w => w.Id == slot && w.Type == WalkerType.Recruiter && w.HomeBuildingId == id && w.State == WalkerState.Alive))
                    WriteLaborShort(b,44,0);
                continue;
            }
            if (!TryGetBuildingRoadAccess(buildings,id * BuildingRecordSize,out int road)) continue;
            int walkerId = AllocateId(); if (walkerId == 0) return;
            active.Add(new Walker(walkerId,WalkerType.Recruiter,road)
            { HomeBuildingId = id, SourceTile = road, DestinationTile = road, ActionState = 1, TerrainUsage = WalkerTerrainUsage.Roads });
            WriteLaborShort(b,44,walkerId); changedTiles.Add(road);
        }
    }

    [OriginalAddress(0x00480180, "workplace road access")]
    [OriginalAddress(0x004806A0, "granary road access")]
    private bool RefreshWorkplaceRoad(Span<byte> b, int type)
    {
        ReadOnlySpan<byte> terrain = Required("terrain_grid",LegacyCityMapSnapshot.ImageGridLength).Span;
        int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]);
        int size = Math.Max(1,(int)b[3]);
        bool found = LegacyBuildingRoadAccess.TryFindNearbyRoad(terrain,laborRoadNetworkPriorities,width,height,origin,tile,size,out int road,type == 71 ? 4 : type == 72 ? 3 : 2);
        b[68] = found ? (byte)1 : (byte)0;
        if (found && TryLocal(road,out int x,out int y)) { WriteLaborShort(b,38,x); WriteLaborShort(b,40,y); }
        return found;
    }

    [RewriteOnly("Build shared road-component priorities once per dispatcher pass rather than once per workplace")]
    private void RebuildLaborRoadPriorities() => laborRoadNetworkPriorities = LegacyBuildingRoadAccess.BuildRoadNetworkPriorities(
        Required("terrain_grid",LegacyCityMapSnapshot.ImageGridLength).Span,width,height,origin);

    [OriginalAddress(0x0048BFF0, "type-five recruiter; home90 and matching building slot44")]
    [OriginalAddress(0x00493610, "action1 roams for384 ticks; action2 returns to the workplace entrance")]
    internal void AdvanceRecruiter(Walker walker)
    {
        if (walker.HomeBuildingId <= 0 || walker.HomeBuildingId >= MaximumBuildings) { RetireRecruiter(walker); return; }
        Span<byte> home = session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId * BuildingRecordSize,BuildingRecordSize);
        if (home[0] != 1 || ReadLaborShort(home,44) != walker.Id) { RetireRecruiter(walker); return; }
        int frame = (walker.AnimationFrame + 1) % 12;
        walker.AnimationFrame = frame;
        if (walker.ActionState != 2 && ++walker.RoamTicks >= 384) { walker.ActionState = 2; ClearHuntingRoute(walker); }
        if (walker.ActionState == 2)
        {
            if (walker.route.Count == 0 && (!TryBuildSafetyReturn(walker) || walker.route.Count == 0)) { RetireRecruiter(walker); return; }
            int previousTile = walker.Tile;
            bool arrived = AdvanceAlongRoute(walker);
            if (walker.Tile != previousTile) VisitRecruitmentHousing(walker);
            walker.AnimationFrame = frame;
            if (arrived) RetireRecruiter(walker);
            return;
        }
        walker.ActionState = 1;
        if (walker.route.Count == 0 && !TryBuildNextSafetyPatrolStep(walker))
        { walker.ActionState = 2; ClearHuntingRoute(walker); return; }
        int oldTile = walker.Tile;
        bool completedStep = AdvanceAlongRoute(walker);
        walker.AnimationFrame = frame;
        if (walker.Tile != oldTile) VisitRecruitmentHousing(walker);
        if (completedStep)
            ClearHuntingRoute(walker);
    }

    [OriginalAddress(0x00484990, "count inhabited housing cells in the inclusive radius-two square")]
    [OriginalAddress(0x004BAA10, "add recruiter housing hits to home short24, capped at300")]
    private void VisitRecruitmentHousing(Walker walker)
    {
        if (!TryLocal(walker.Tile,out int x,out int y)) return;
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        ReadOnlySpan<byte> grid = Required("building_grid",LegacyCityMapSnapshot.TileCount*2).Span;
        int hits = 0;
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
        {
            if (!TryTile(x+dx,y+dy,out int tile)) continue;
            int id = ReadLaborShort(grid,tile*2);
            if (id <= 0 || id >= MaximumBuildings) continue;
            ReadOnlySpan<byte> b = buildings.Slice(id*BuildingRecordSize,BuildingRecordSize);
            if (b[5] != 0 && ReadLaborShort(b,28) > 0) hits++;
        }
        Span<byte> home = buildings.Slice(walker.HomeBuildingId*BuildingRecordSize,BuildingRecordSize);
        WriteLaborShort(home,24,Math.Min(300,ReadLaborShort(home,24)+hits));
    }

    [OriginalAddress(0x00467220, "release a finished recruiter's slot without touching the service-walker slot42")]
    private void RetireRecruiter(Walker walker)
    {
        if (walker.HomeBuildingId > 0 && walker.HomeBuildingId < MaximumBuildings)
        {
            Span<byte> home = session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId*BuildingRecordSize,BuildingRecordSize);
            if (ReadLaborShort(home,44) == walker.Id) WriteLaborShort(home,44,0);
        }
        Destroy(walker,false);
    }

    private static short ReadLaborShort(ReadOnlySpan<byte> data,int offset) => BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
    private static void WriteLaborShort(Span<byte> data,int offset,int value) => BinaryPrimitives.WriteInt16LittleEndian(data[offset..],checked((short)value));
    private static void WriteLaborInt(Span<byte> data,int offset,int value) => BinaryPrimitives.WriteInt32LittleEndian(data[offset..],value);

    [OriginalAddress(0x0048BFF0, "recruiter home90, frame1 and roaming count68")]
    [OriginalAddress(0x0048C6A0, "architect home90")]
    [OriginalAddress(0x0048DED0, "fireman home90")]
    [OriginalAddress(0x004ABE40, "policeman home90")]
    private static void RestoreWorkplaceWalkerFields(ReadOnlySpan<byte> record,Walker walker)
    {
        if (walker.Type is not (WalkerType.Recruiter or WalkerType.Architect or WalkerType.Fireman or WalkerType.Policeman)) return;
        int home = ReadLaborShort(record,90);
        // Earlier rewrite service records used the migrant field. Preserve their continuation.
        walker.HomeBuildingId = home > 0 ? home : ReadLaborShort(record,92);
        walker.AnimationFrame = record[1];
        walker.RoamInitialized = true;
        walker.RoamFree = record[70] != 0;
        walker.RoamStepCounter = record[71];
        walker.RoamTurnDirection = unchecked((sbyte)record[72]);
        if (walker.RoamTurnDirection is not (2 or -2)) walker.RoamTurnDirection = 2;
        walker.RoamTurnCountdown = unchecked((sbyte)record[73]);
        if (walker.Type is WalkerType.Recruiter or WalkerType.Architect or WalkerType.Fireman) walker.RoamTicks = Math.Max(0,(int)ReadLaborShort(record,68));
        if(walker.Type==WalkerType.Fireman) { walker.FireSearchTicks=record[107];walker.FireFightingFrame=ReadLaborShort(record,116);if(walker.ActionState==FireFightingAction)walker.AnimationFrame=walker.FireFightingFrame>=36?0:walker.FireFightingFrame; }
    }

    [OriginalAddress(0x0048BFF0, "native recruiter fields and 384-pulse roaming budget")]
    [OriginalAddress(0x00462180, "service-walker ownership at90 is separate from migrant home92")]
    private static void PublishWorkplaceWalkerFields(Span<byte> record,Walker walker)
    {
        if (walker.Type is not (WalkerType.Recruiter or WalkerType.Architect or WalkerType.Fireman or WalkerType.Policeman)) return;
        WriteLaborShort(record,90,walker.HomeBuildingId);
        record[1] = (byte)(walker.AnimationFrame%12);
        record[70] = walker.RoamFree ? (byte)1 : (byte)0;
        record[71] = walker.RoamStepCounter;
        record[72] = unchecked((byte)walker.RoamTurnDirection);
        record[73] = unchecked((byte)walker.RoamTurnCountdown);
        record[12] = 0; record[13] = 1;
        if (walker.NativeRecord is {} original) { original.AsSpan(92,2).CopyTo(record[92..]); record[124] = original[124]; }
        else { record.Slice(92,2).Clear(); record[124] = 0; }
        if(walker.Type is WalkerType.Architect or WalkerType.Fireman) { WriteLaborShort(record,66,640);WriteLaborShort(record,68,walker.RoamTicks); }
        if(walker.Type==WalkerType.Fireman) { record[107]=(byte)walker.FireSearchTicks;WriteLaborShort(record,116,walker.FireFightingFrame); }
        if (walker.Type == WalkerType.Recruiter)
        { WriteLaborShort(record,66,384); WriteLaborShort(record,68,walker.RoamTicks); }
    }
}
