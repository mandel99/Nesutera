using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    private long huntingTicks;
    private static readonly (int X, int Y)[] PreyOffsets =
    [ (0,0), (2,1), (-1,-1), (1,1), (1,0), (-1,1), (3,1), (-2,-1),
      (0,2), (-4,0), (-1,3), (0,3), (1,4), (4,0), (2,3), (-3,2) ];

    [OriginalAddress(0x004B92E0, "herd reinforcement and saved formation counters")]
    [OriginalAddress(0x00462180, "building production dispatcher hunting-lodge branch")]
    [OriginalAddress(0x0045D630, "operational granary food totals")]
    private void AdvanceHuntingProduction()
    {
        if (++huntingTicks % 51 != 0) return;
        ReplenishPrey();
        AdvancePreyHerdCenters();
        RebuildLaborRoadPriorities();
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * BuildingRecordSize;
            BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]);
            if (buildings[offset] != 1 || type is not (BuildingType.HuntingLodge or BuildingType.Granary or BuildingType.Bazaar) ||
                !LegacySquareBuildingPlacement.TryGetDefinition(type, out var definition)) continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings[(offset + 12)..]);
            int size = Math.Max(1, (int)buildings[offset + 3]);
            bool road = LegacyBuildingRoadAccess.TryFindNearbyRoad(
                Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span,
                laborRoadNetworkPriorities, width, height, origin, tile, size, out int access, type == BuildingType.Granary ? 4 : 2);
            buildings[offset + 68] = road ? (byte)1 : (byte)0;
            if (!road) continue;
            TryLocal(access, out int x, out int y);
            BinaryPrimitives.WriteInt16LittleEndian(buildings[(offset + 38)..], checked((short)x));
            BinaryPrimitives.WriteInt16LittleEndian(buildings[(offset + 40)..], checked((short)y));
            int required = buildingModels?.GetModel(difficulty, type)?.Employees ?? definition.Employees;
            int employed = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 64)..]);
            if (type != BuildingType.HuntingLodge || Required("scenario_info", 985).Span[984] != 2) continue;
            int percent = required > 0 ? employed * 100 / required : 0;
            int desired = percent >= 100 ? 3 : percent >= 75 ? 2 : percent >= 25 ? 1 : 0;
            int stock = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 60)..]);
            if (stock >= 100 && !active.Any(w => w.Type == WalkerType.CartPusher && w.State == WalkerState.Alive && w.HomeBuildingId == id))
            {
                int cartId = AllocateId();
                if (cartId != 0)
                {
                    active.Add(new Walker(cartId, WalkerType.CartPusher, access)
                    { HomeBuildingId = id, SourceTile = access, DestinationTile = access, ActionState = 8,
                      WaitTicks = 30, Cargo = 100, TerrainUsage = WalkerTerrainUsage.Roads });
                    stock -= 100;
                    BinaryPrimitives.WriteInt16LittleEndian(buildings[(offset + 60)..], checked((short)stock));
                    BinaryPrimitives.WriteInt16LittleEndian(buildings[(offset + 42)..], checked((short)cartId));
                    buildings[offset + 67] = 8;
                    Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
                    BinaryPrimitives.WriteInt32LittleEndian(city[37196..], checked(BinaryPrimitives.ReadInt32LittleEndian(city[37196..]) + 100));
                }
            }
            desired = Math.Min(desired, Math.Max(0, (500 - stock) / 100));
            int count = active.Count(w => w.State == WalkerState.Alive && w.Type == WalkerType.OstrichHunter && w.HomeBuildingId == id);
            while (count < desired)
            {
                int walkerId = AllocateId();
                if (walkerId == 0) break;
                active.Add(new Walker(walkerId, WalkerType.OstrichHunter, access)
                {
                    HomeBuildingId = id, SourceTile = access, DestinationTile = access,
                    ActionState = 14, WaitTicks = NextHuntingRandom() & 31,
                    TerrainUsage = WalkerTerrainUsage.PreferRoads
                });
                count++;
            }
            buildings[offset + 166] = checked((byte)count);
        }
        RecalculateGranaryFood();
    }

    [OriginalAddress(0x004B92E0, "prey herd center movement after eight daily passes")]
    [OriginalAddress(0x004EB4C0, "scan reachable square perimeters from the outer radius inward")]
    private void AdvancePreyHerdCenters()
    {
        if (Required("scenario_info", 985).Span[984] != 2) return;
        Span<byte> formations = session.GetWritableLogicalChunk("formations").Span;
        for (int id = 1; id * 144 + 144 <= formations.Length; id++)
        {
            Span<byte> formation = formations.Slice(id * 144, 144);
            if (formation[0] != 1 || formation[105] != 2) continue;
            int counter = BinaryPrimitives.ReadInt16LittleEndian(formation[84..]) + 1;
            BinaryPrimitives.WriteInt16LittleEndian(formation[84..], checked((short)Math.Min(short.MaxValue, counter)));
            Walker[] members = active.Where(w => w.Type == WalkerType.Ostrich && w.FormationId == id && w.State == WalkerState.Alive).ToArray();
            if (counter <= 8 || members.Any(w => w.ActionState is 14 or 16) || formation[116] == 17) continue;
            BinaryPrimitives.WriteInt16LittleEndian(formation[84..], 0);
            int radius = 4 + NextHuntingRandom() % 4;
            formation[118] = checked((byte)(NextHuntingRandom() & 7));
            int x = BinaryPrimitives.ReadInt16LittleEndian(formation[58..]), y = BinaryPrimitives.ReadInt16LittleEndian(formation[60..]);
            int homeX = BinaryPrimitives.ReadInt16LittleEndian(formation[54..]), homeY = BinaryPrimitives.ReadInt16LittleEndian(formation[56..]);
            if (!TryTile(x, y, out int center)) continue;
            int target = -1;
            if ((x != homeX || y != homeY) && (NextHuntingRandom() & 7) == 0 &&
                TryTile(homeX, homeY, out int home) && IsPassable(home, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span, false) &&
                TryFindRoute(center, [home], out _))
                target = home;
            else
            {
                foreach (int candidate in EnumerateHerdPerimeters(x, y, radius))
                    if (IsPassable(candidate, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span, false) &&
                        !active.Any(w => w.State == WalkerState.Alive && w.Tile == candidate) &&
                        TryFindRoute(center, [candidate], out _))
                    { target = candidate; break; }
            }
            if (target < 0 || !TryLocal(target, out int targetX, out int targetY)) continue;
            BinaryPrimitives.WriteInt16LittleEndian(formation[58..], checked((short)targetX));
            BinaryPrimitives.WriteInt16LittleEndian(formation[60..], checked((short)targetY));
            foreach (Walker prey in members)
                if (prey.ActionState is not (4 or 5 or 12 or 14 or 16 or 17))
                { prey.ActionState = 8; prey.WaitTicks = 0; ClearHuntingRoute(prey); }
        }
    }

    [OriginalAddress(0x004EB4C0, "clockwise square perimeter scan, outermost first")]
    private IEnumerable<int> EnumerateHerdPerimeters(int x, int y, int radius)
    {
        for (int r = radius; r >= 0; r--)
        {
            for (int column = x - r; column <= x + r; column++) if (TryTile(column, y - r, out int tile)) yield return tile;
            for (int row = y - r + 1; row <= y + r; row++) if (TryTile(x + r, row, out int tile)) yield return tile;
            for (int column = x + r - 1; column >= x - r; column--) if (TryTile(column, y + r, out int tile)) yield return tile;
            for (int row = y + r - 1; row > y - r; row--) if (TryTile(x - r, row, out int tile)) yield return tile;
        }
    }

    [OriginalAddress(0x004B92E0, "climate-two prey limit seven and reinforcement delay greater than four")]
    private void ReplenishPrey()
    {
        // Other climates have distinct prey types and handlers. They are not
        // substituted with ostriches.
        if (Required("scenario_info", 985).Span[984] != 2) return;
        Span<byte> formations = session.GetWritableLogicalChunk("formations").Span;
        for (int id = 1; id * 144 + 144 <= formations.Length; id++)
        {
            Span<byte> formation = formations.Slice(id * 144, 144);
            if (formation[0] != 1 || formation[105] != 2) continue;
            int count = active.Count(w => w.Type == WalkerType.Ostrich && w.FormationId == id &&
                w.State == WalkerState.Alive && w.ActionState is not (4 or 12));
            formation[40] = checked((byte)Math.Min(255, count)); formation[41] = 7;
            if (count >= 7 || ++formation[117] <= 4) continue;
            formation[117] = 0;
            int hx = BinaryPrimitives.ReadInt16LittleEndian(formation[54..]);
            int hy = BinaryPrimitives.ReadInt16LittleEndian(formation[56..]);
            int start = NextHuntingRandom() & 15;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var (dx, dy) = PreyOffsets[(start + attempt) & 15];
                if (!TryTile(hx + dx, hy + dy, out int tile) ||
                    !IsPassable(tile, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span, false)) continue;
                int walkerId = AllocateId();
                if (walkerId == 0) break;
                active.Add(new Walker(walkerId, WalkerType.Ostrich, tile)
                {
                    FormationId = id, HerdIndex = count, SourceTile = tile, DestinationTile = tile,
                    ActionState = 24, WaitTicks = 10, TerrainUsage = WalkerTerrainUsage.Animal
                });
                formation[40]++;
                break;
            }
        }
    }

    [OriginalAddress(0x0049BD40, "type-69 prey dispatcher")]
    internal void AdvanceOstrich(Walker walker)
    {
        walker.TerrainUsage = WalkerTerrainUsage.Animal;
        if (walker.ClaimedByBuildingId > 0)
        {
            int lodge = walker.ClaimedByBuildingId;
            if (!IsActiveLodge(lodge) || BinaryPrimitives.ReadInt16LittleEndian(
                    Required("buildings", MaximumBuildings * 264).Span[(lodge * 264 + 64)..]) == 0 ||
                !active.Any(w => w.Type == WalkerType.OstrichHunter && w.HomeBuildingId == lodge && w.State == WalkerState.Alive))
                walker.ClaimedByBuildingId = 0;
        }
        bool frame = ++walker.AnimationTick >= Math.Max(1, walker.AnimationPeriod);
        if (frame) { walker.AnimationTick = 0; walker.AnimationFrame = (walker.AnimationFrame + 1) & 255; }
        switch (walker.ActionState)
        {
            case 24:
                if (--walker.WaitTicks <= 0) { walker.ActionState = 8; walker.AnimationPeriod = 8; }
                return;
            case 8:
                walker.ActionState = (NextHuntingRandom() & 1) == 0 ? 18 : 19;
                walker.WaitTicks = 150 + NextHuntingRandom() % 150;
                walker.AnimationFrame = 0; walker.AnimationPeriod = 8;
                return;
            case 12:
                if (++walker.WaitTicks > 500) { walker.ActionState = 4; walker.WaitTicks = 0; }
                return;
            case 4:
                if (++walker.WaitTicks >= 128) Destroy(walker, false);
                return;
            case 10:
            case 16:
            case 17:
                if ((walker.route.Count == 0 && !TryRouteHunting(walker, walker.DestinationTile)) || AdvanceAlongRoute(walker))
                { ClearHuntingRoute(walker); walker.ActionState = 8; }
                return;
            case 18:
            case 19:
            case 14:
            case 15:
                Walker? hunter = active.FirstOrDefault(w => w.Type == WalkerType.OstrichHunter &&
                    w.State == WalkerState.Alive && ChebyshevDistance(w.Tile, walker.Tile) <= 3);
                if (hunter is not null && TryFlee(walker, hunter)) return;
                if (--walker.WaitTicks > 0 || !frame) return;
                if (walker.ActionState == 19) { walker.ActionState = 18; return; }
                if (!TryPreyWander(walker)) walker.WaitTicks = 100;
                return;
            default:
                walker.ActionState = 8;
                return;
        }
    }

    [OriginalAddress(0x004EAB10, "sixteen positions around the herd center")]
    private bool TryPreyWander(Walker walker)
    {
        ReadOnlySpan<byte> formations = Required("formations", 144).Span;
        int offset = walker.FormationId * 144;
        int hx, hy;
        if (walker.FormationId > 0 && offset + 144 <= formations.Length)
        { hx = BinaryPrimitives.ReadInt16LittleEndian(formations[(offset + 58)..]); hy = BinaryPrimitives.ReadInt16LittleEndian(formations[(offset + 60)..]); }
        else if (!TryLocal(walker.SourceTile, out hx, out hy)) return false;
        int start = NextHuntingRandom() & 15;
        for (int i = 0; i < 16; i++)
        {
            var (dx, dy) = PreyOffsets[(start + i) & 15];
            if (!TryTile(hx + dx, hy + dy, out int goal) || goal == walker.Tile ||
                !IsPassable(goal, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span, false) ||
                !TryRouteHunting(walker, goal)) continue;
            walker.ActionState = 10; walker.AnimationPeriod = 12; return true;
        }
        return false;
    }

    [OriginalAddress(0x004EB000, "prey reacts to hunters within three cells")]
    private bool TryFlee(Walker prey, Walker hunter)
    {
        if (!TryLocal(prey.Tile, out int px, out int py) || !TryLocal(hunter.Tile, out int hx, out int hy)) return false;
        int dx = Math.Sign(px - hx), dy = Math.Sign(py - hy);
        if (dx == 0 && dy == 0) dx = 1;
        if (!TryTile(px + dx * 2, py + dy * 2, out int destination) ||
            !IsPassable(destination, Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span, false) ||
            !TryRouteHunting(prey, destination)) return false;
        prey.ActionState = 16; return true;
    }

    [OriginalAddress(0x004A2430, "type-73 hunter wait search shoot retrieve and return flow")]
    internal void AdvanceHunter(Walker walker)
    {
        if (!IsActiveLodge(walker.HomeBuildingId)) { Destroy(walker, false); return; }
        walker.AnimationFrame = (walker.AnimationFrame + 1) % 12;
        switch (walker.ActionState)
        {
            case 14:
                if (--walker.WaitTicks <= 0) { walker.ActionState = 8; walker.AnimationFrame = 0; }
                return;
            case 8:
                if (--walker.WaitTicks > 0) return;
                if (SelectPrey(walker, 1000)) walker.ActionState = 9;
                else BeginHunterReturn(walker, carrying: false);
                return;
            case 9:
                if (walker.route.Count == 0 && !TryRouteHunting(walker, walker.DestinationTile))
                { walker.ActionState = 8; walker.WaitTicks = 20; return; }
                bool arrived = AdvanceHunterRoute(walker);
                if (arrived || walker.AnimationFrame == 0)
                {
                    Walker? prey = active.FirstOrDefault(w => w.Id == walker.TargetWalkerId && w.State == WalkerState.Alive && w.ActionState is not (4 or 12));
                    if (prey is not null && ChebyshevDistance(walker.Tile, prey.Tile) <= 2)
                    { walker.DestinationTile = prey.Tile; walker.Direction = LegacyHuntingGeometry.Facing(prey.Tile%228-walker.Tile%228,prey.Tile/228-walker.Tile/228); ClearHuntingRoute(walker); walker.ActionState = 15; walker.WaitTicks = 0; }
                    else if (arrived) { ClearHuntingRoute(walker); walker.ActionState = 8; }
                }
                return;
            case 15:
                if (walker.WaitTicks == 9) SpawnHunterArrow(walker);
                if (++walker.WaitTicks > 300) { walker.ActionState = 8; walker.WaitTicks = 0; }
                return;
            case 11:
                if (walker.route.Count == 0 && !TryRouteHunting(walker, walker.DestinationTile))
                { walker.ActionState = 8; walker.WaitTicks = 20; return; }
                if (!AdvanceHunterRoute(walker)) return;
                Walker? carcass = active.FirstOrDefault(w => w.Id == walker.TargetWalkerId && w.State == WalkerState.Alive && w.ActionState == 12 && w.Tile == walker.Tile);
                if (carcass is null) { walker.ActionState = 8; walker.WaitTicks = 20; ClearHuntingRoute(walker); return; }
                Destroy(carcass, false); walker.ActionState = 10; walker.WaitTicks = 0; ClearHuntingRoute(walker); return;
            case 10:
                int carryingDelay = Required("scenario_info", 985).Span[984] == 0 ? 0 : 18;
                if (++walker.WaitTicks > carryingDelay) BeginHunterReturn(walker, carrying: true);
                return;
            case 12:
            case 13:
                if (walker.ActionState == 13 && walker.AnimationFrame == 0 && SelectPrey(walker, 100))
                { walker.ActionState = 9; return; }
                if (walker.route.Count == 0 && !TryRouteHunting(walker, walker.DestinationTile)) { Destroy(walker, false); return; }
                if (!AdvanceHunterRoute(walker)) return;
                if (walker.ActionState == 12)
                {
                    Span<byte> house = session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId * 264, 264);
                    int stock = BinaryPrimitives.ReadInt16LittleEndian(house[60..]);
                    BinaryPrimitives.WriteInt16LittleEndian(house[60..], checked((short)Math.Min(short.MaxValue, stock + 100)));
                    changedTiles.Add(BinaryPrimitives.ReadInt32LittleEndian(house[12..]));
                }
                Destroy(walker, false); return;
            default:
                walker.ActionState = 8; return;
        }
    }

    [OriginalAddress(0x004A2430, "FUN_004a2430")]
    private bool AdvanceHunterRoute(Walker walker)
    {
        int frame=walker.AnimationFrame;
        bool arrived=AdvanceAlongRoute(walker);
        walker.AnimationFrame=frame;
        return arrived;
    }

    [OriginalAddress(0x004A2320, "nearest reachable prey, first thirty candidates and lodge ownership")]
    private bool SelectPrey(Walker hunter, int maximumDistance)
    {
        Walker[] candidates = active.Where(w => w.Type == WalkerType.Ostrich && w.State == WalkerState.Alive &&
            w.ActionState is not (4 or 12) && (w.ClaimedByBuildingId == 0 || w.ClaimedByBuildingId == hunter.HomeBuildingId))
            .OrderBy(w => w.Id).Take(30).ToArray();
        if (candidates.Length == 0 || !TryFindRoute(hunter.Tile, candidates.Select(w => w.Tile).ToHashSet(), out List<int> route) ||
            route.Count > maximumDistance) return false;
        int tile = route.Count == 0 ? hunter.Tile : route[^1];
        Walker prey = candidates.First(w => w.Tile == tile);
        prey.ClaimedByBuildingId = hunter.HomeBuildingId;
        hunter.TargetWalkerId = prey.Id; hunter.DestinationTile = tile;
        hunter.route = route; hunter.RoutePosition = 0; hunter.ProgressOnTile = 0; return true;
    }

    [OriginalAddress(0x004B45C0, "create hunter projectile type 74")]
    private void SpawnHunterArrow(Walker hunter)
    {
        int id = AllocateId(); if (id == 0) return;
        TryLocal(hunter.Tile, out int x, out int y);
        TryLocal(hunter.DestinationTile, out int tx, out int ty);
        int direction = LegacyHuntingGeometry.ArrowDirection(tx-x,ty-y);
        var arrow=new Walker(id, WalkerType.HunterArrow, hunter.Tile)
        { HomeBuildingId = hunter.Id, SourceTile = hunter.Tile, DestinationTile = hunter.DestinationTile,
          TargetWalkerId = hunter.TargetWalkerId, TerrainUsage = WalkerTerrainUsage.Any };
        BeginResidentApproach(arrow,hunter.DestinationTile);arrow.Direction=direction;active.Add(arrow);
    }

    [OriginalAddress(0x004A2E90, "one-hit prey projectile and shooter retrieval transition")]
    internal void AdvanceHunterArrow(Walker arrow)
    {
        arrow.ProjectileAge++;
        if(!arrow.FreeWalkInitialized)
        {
            BeginResidentApproach(arrow,arrow.DestinationTile);
            arrow.Direction=LegacyHuntingGeometry.ArrowDirection(arrow.DestinationTile%228-arrow.Tile%228,arrow.DestinationTile/228-arrow.Tile/228);
        }
        bool arrived=false;
        for(int step=0;step<3&&!arrived;step++)arrived=AdvanceResidentApproach(arrow);
        arrow.AnimationFrame=arrow.ProjectileAge%5;
        Walker? victim = active.FirstOrDefault(w => w.Type == WalkerType.Ostrich && w.State == WalkerState.Alive &&
            w.ActionState is not (4 or 12) && w.Tile == arrow.Tile);
        Walker? shooter = active.FirstOrDefault(w => w.Id == arrow.HomeBuildingId && w.Type == WalkerType.OstrichHunter && w.State == WalkerState.Alive);
        if (victim is not null)
        {
            victim.ActionState = 12; victim.WaitTicks = 0; ClearHuntingRoute(victim);
            if (shooter is not null)
            { shooter.TargetWalkerId = victim.Id; shooter.DestinationTile = victim.Tile; shooter.ActionState = 11; ClearHuntingRoute(shooter); }
            Destroy(arrow, false); return;
        }
        if (arrow.ProjectileAge > 104 || arrived)
        { if (shooter is not null && shooter.ActionState == 15) { shooter.ActionState = 8; shooter.WaitTicks = 0; } Destroy(arrow, false); return; }
    }

    [OriginalAddress(0x004A2430, "return to the lodge road entrance with or without meat")]
    private void BeginHunterReturn(Walker hunter, bool carrying)
    {
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * 264).Span;
        int offset = hunter.HomeBuildingId * 264;
        int x = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 38)..]);
        int y = BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 40)..]);
        if (!TryTile(x, y, out int destination)) { Destroy(hunter, false); return; }
        hunter.DestinationTile = destination; hunter.ActionState = carrying ? 12 : 13;
        ClearHuntingRoute(hunter);
        if (!carrying && hunter.Tile == destination) Destroy(hunter, false);
    }

    [OriginalAddress(0x004A2430, "hunter source building must remain active")]
    private bool IsActiveLodge(int id)
    {
        if (id <= 0 || id >= MaximumBuildings) return false;
        ReadOnlySpan<byte> record = Required("buildings", MaximumBuildings * 264).Span.Slice(id * 264, 264);
        return record[0] == 1 && BinaryPrimitives.ReadInt16LittleEndian(record[16..]) == 115;
    }

    [RewriteOnly("Uses the bounded shared route solver for mapped hunting actions")]
    private bool TryRouteHunting(Walker walker, int destination)
    {
        if (!TryFindRoute(walker.Tile, [destination], out List<int> route,usage:walker.TerrainUsage)) return false;
        walker.DestinationTile = destination; walker.route = route; walker.RoutePosition = 0; walker.ProgressOnTile = 0; return true;
    }

    [OriginalAddress(0x004B3250, "clear walker route before changing hunting actions")]
    private static void ClearHuntingRoute(Walker walker)
    { walker.route.Clear(); walker.RoutePosition = 0; walker.ProgressOnTile = 0; }

    [OriginalAddress(0x004BB3F0, "two 31-bit LFSRs advanced 31 times per random draw")]
    [OriginalAddress(0x004B92E0, "persistent random state used for prey replenishment")]
    private int NextHuntingRandom()
    {
        Span<byte> random = session.GetWritableLogicalChunk("random_iv").Span;
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(random), b = BinaryPrimitives.ReadUInt32LittleEndian(random[4..]);
        Pharaoh.Engine.Population.LegacyPopulationAges.RecordPreviousRandom(session, a);
        for (int i = 0; i < 31; i++)
        { a = (a >> 1) | (((a ^ (a >> 4)) & 1) << 30); b = (b >> 1) | (((b ^ (b >> 4)) & 1) << 30); }
        BinaryPrimitives.WriteUInt32LittleEndian(random, a); BinaryPrimitives.WriteUInt32LittleEndian(random[4..], b);
        return (int)(b & 0x7fff);
    }

    [OriginalAddress(0x0045D630, "sum nine inventory shorts only for road-connected half-staffed granaries")]
    private void RecalculateGranaryFood()
    {
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        city.Slice(37396, 36).Clear();
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * 264).Span;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * 264;
            if (buildings[offset] != 1 || BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 16)..]) is not (71 or 191) || buildings[offset + 68] == 0) continue;
            int required = buildingModels?.GetModel(difficulty, BuildingType.Granary)?.Employees ?? 12;
            if (BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 64)..]) * 2 < required) continue;
            for (int slot = 0; slot < 9; slot++)
            {
                Span<byte> total = city[(37396 + slot * 4)..];
                BinaryPrimitives.WriteInt32LittleEndian(total, checked(BinaryPrimitives.ReadInt32LittleEndian(total) +
                    BinaryPrimitives.ReadInt16LittleEndian(buildings[(offset + 86 + slot * 2)..])));
            }
        }
    }

    [OriginalAddress(0x0049BD40, "prey fields +96 +98 +312 +324 +328")]
    [OriginalAddress(0x004A2430, "hunter home +90 and target +94")]
    [OriginalAddress(0x004A2E90, "projectile shooter +90 and lifetime +57")]
    private static void RestoreHuntingFields(ReadOnlySpan<byte> record, Walker walker)
    {
        if (walker.Type is not (WalkerType.Ostrich or WalkerType.OstrichHunter or WalkerType.HunterArrow or WalkerType.CartPusher)) return;
        walker.HomeBuildingId = BinaryPrimitives.ReadInt16LittleEndian(record[90..]);
        walker.TargetWalkerId = BinaryPrimitives.ReadUInt16LittleEndian(record[94..]);
        walker.FormationId = BinaryPrimitives.ReadInt16LittleEndian(record[96..]); walker.HerdIndex = record[98];
        walker.ClaimedByBuildingId = BinaryPrimitives.ReadInt32LittleEndian(record[312..]);
        walker.AnimationPeriod = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(record[324..]), 1, 12);
        walker.AnimationTick = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(record[328..]), 0, 12);
        walker.AnimationFrame = record[1];
        if (walker.Type == WalkerType.CartPusher)
        { walker.DestinationBuildingId = BinaryPrimitives.ReadUInt16LittleEndian(record[94..]); walker.Cargo = BinaryPrimitives.ReadInt16LittleEndian(record[116..]); }
        if (walker.Type == WalkerType.HunterArrow) { walker.ProjectileAge = record[57]; walker.ProgressOnTile = 0; }
    }

    [OriginalAddress(0x0049BD40, "persistent prey fields")]
    [OriginalAddress(0x004A2430, "persistent hunter home and prey target")]
    private static void PublishHuntingFields(Span<byte> record, Walker walker)
    {
        if (walker.Type is not (WalkerType.Ostrich or WalkerType.OstrichHunter or WalkerType.HunterArrow or WalkerType.CartPusher)) return;
        record[1] = unchecked((byte)walker.AnimationFrame);
        record[12] = walker.Type == WalkerType.HunterArrow ? (byte)1 : (byte)0;
        record[13] = 1;
        // The common migrant serializer owns these fields only for migrants.
        // Keep the native bytes in records whose home actually lives at +90.
        if (walker.NativeRecord is { } original)
        {
            original.AsSpan(92, 2).CopyTo(record[92..]);
            original.AsSpan(129, 2).CopyTo(record[129..]);
            record[124] = original[124];
        }
        else { record.Slice(92, 2).Clear(); record.Slice(129, 2).Clear(); record[124] = 0; }
        BinaryPrimitives.WriteInt16LittleEndian(record[90..], checked((short)walker.HomeBuildingId));
        BinaryPrimitives.WriteUInt16LittleEndian(record[94..], checked((ushort)walker.TargetWalkerId));
        BinaryPrimitives.WriteInt16LittleEndian(record[96..], checked((short)walker.FormationId)); record[98] = checked((byte)walker.HerdIndex);
        BinaryPrimitives.WriteInt32LittleEndian(record[312..], walker.ClaimedByBuildingId);
        BinaryPrimitives.WriteInt32LittleEndian(record[324..], walker.AnimationPeriod);
        BinaryPrimitives.WriteInt32LittleEndian(record[328..], walker.AnimationTick);
        if (walker.Type == WalkerType.HunterArrow) record[57] = checked((byte)walker.ProjectileAge);
        if (walker.Type == WalkerType.CartPusher)
        {
            record[11] = 8;
            BinaryPrimitives.WriteInt16LittleEndian(record[116..], checked((short)walker.Cargo));
            BinaryPrimitives.WriteUInt16LittleEndian(record[94..], checked((ushort)walker.DestinationBuildingId));
        }
    }
}
