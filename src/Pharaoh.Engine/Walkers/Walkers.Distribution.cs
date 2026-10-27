using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    private long distributionTicks;
    private int distributionPass;
    private int[] distributionFoods = [];

    [OriginalAddress(0x00462180, "FUN_00462180")]
    [OriginalAddress(0x005596D0, "FUN_005596d0")]
    private void AdvanceDistributionBuildings()
    {
        if (++distributionTicks % 51 != 31) return;
        distributionPass++;
        distributionFoods = LegacyFoodAvailability.Read(session.LogicalChunks);
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        foreach (int food in distributionFoods) city[29540 + food] = 1;
        int bazaarOrdinal = 0;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            ReadOnlySpan<byte> b = DistributionBuilding(id);
            if (b[0] != 1) continue;
            int type = ReadLaborShort(b, 16);
            if (type is 71 or 191) DispatchGranaryTransporters(id);
            if (type is 70 or 190) DispatchBazaarWalkers(id, ++bazaarOrdinal);
        }
    }

    [OriginalAddress(0x00462180, "FUN_00462180")]
    [OriginalAddress(0x0045B880, "FUN_0045b880")]
    [OriginalAddress(0x0045CE90, "FUN_0045ce90")]
    [OriginalAddress(0x004611D0, "FUN_004611d0")]
    private void DispatchGranaryTransporters(int id)
    {
        ReadOnlySpan<byte> b = DistributionBuilding(id);
        if (DistributionStaffPercent(b) < 50 || !TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, id * 264, out int road)) return;
        var needs = distributionFoods.Where(resource => StorageMode(id, resource) == 2 && !DistributionStockpiled(resource))
            .Select(resource => (Resource: resource, Percent: DistributionStock(id, resource) * 100 / Math.Max(1, StorageLimit(id, resource, getting: true))))
            .Where(item => item.Percent < 100 && FindDistributionSource(id, item.Resource, require400: true) != 0)
            .OrderBy(item => item.Percent).ThenBy(item => item.Resource).Take(2).ToArray();
        // A reachable getting request takes precedence over emptying.
        int empty = needs.Length == 0 ? Enumerable.Range(1, 8).FirstOrDefault(resource => StorageMode(id, resource) == 3 && DistributionStock(id, resource) > 0) : 0;
        if (empty != 0)
        {
            if (HasDistributionSlot(id, 42, WalkerType.StorageTransporter)) return;
            int amount = TakeDistributionStock(id, empty, Math.Min(400, DistributionStock(id, empty)));
            if (amount > 0 && CreateDistributionWalker(id, WalkerType.StorageTransporter, 42, road) is { } cart)
            { cart.Resource = empty; cart.Cargo = amount; cart.ActionState = 8; }
            else if (amount > 0) AddDistributionStock(id, empty, amount);
            return;
        }
        if (needs.Length == 0) return;
        // Two resources below50% get one carrier each; a single scarce food
        // can use both slots. At50..99%, a single requested food uses one.
        int wanted = needs.Length > 1 || needs[0].Percent < 50 ? 2 : 1;
        foreach (int slot in wanted == 2 ? new[] { 42, 182 } : new[] { 42 })
        {
            if (HasDistributionSlot(id, slot, WalkerType.StorageTransporter)) continue;
            int resource = needs[Math.Min(slot == 182 ? 1 : 0, needs.Length - 1)].Resource;
            if (FindDistributionSource(id, resource, require400: true) == 0) continue;
            if (CreateDistributionWalker(id, WalkerType.StorageTransporter, slot, road) is { } cart)
            { cart.RequestedResource = resource; cart.ActionState = 8; }
        }
    }

    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x0048CAA0, "FUN_0048caa0")]
    [OriginalAddress(0x0045E150, "FUN_0045e150")]
    private void AdvanceStorageTransporter(Walker cart)
    {
        if (cart.ActionState == 8 && !OwnsDistributionWalker(cart, 42, 44, 182)) { RetireDistributionWalker(cart); return; }
        cart.AnimationFrame = (cart.AnimationFrame + 1) % 12;
        switch (cart.ActionState)
        {
            case 8:
                cart.AnimationFrame = 0;
                if (++cart.WaitTicks <= 2) return;
                cart.WaitTicks = 0;
                if (cart.Cargo > 0)
                {
                    int destination = FindGranaryReceiver(cart);
                    if (destination == 0) return;
                    cart.DestinationBuildingId = destination; cart.ActionState = 9;
                    ReserveDistributionReceiver(cart, destination);
                    StartDistributionRoute(cart, destination);
                }
                else
                {
                    int source = FindGettingGranarySource(cart.HomeBuildingId);
                    if (source == 0) source = FindDistributionSource(cart.HomeBuildingId, cart.RequestedResource, require400: true);
                    if (source == 0) return;
                    cart.DestinationBuildingId = source;
                    cart.ActionState = IsGranary(source) ? 18 : 20;
                    StartDistributionRoute(cart, source);
                }
                return;
            case 9: // Loaded delivery to another receiver.
                if (MoveDistributionRoute(cart)) { cart.ActionState = 10; cart.WaitTicks = 0; }
                return;
            case 10:
                cart.AnimationFrame = 0;
                if (++cart.WaitTicks <= 4) return;
                DepositStorageCart(cart, cart.DestinationBuildingId, returning: false);
                return;
            case 18:
            case 20:
            case 12:
            case 15:
                if (MoveDistributionRoute(cart))
                { cart.ActionState = cart.ActionState == 20 ? 21 : cart.ActionState == 15 ? 16 : cart.ActionState == 12 ? 13 : 19; cart.WaitTicks = 0; }
                return;
            case 13:
            case 19:
            case 16:
            case 21:
                cart.AnimationFrame = 0;
                if (++cart.WaitTicks <= 4) return;
                cart.WaitTicks = 0;
                if (!IsDistributionSource(cart.DestinationBuildingId)) { cart.ActionState = 8; ClearHuntingRoute(cart); return; }
                int resource = cart.RequestedResource;
                int capacity = 400;
                if (cart.ActionState == 13)
                {
                    resource = Enumerable.Range(1, 8).Where(food => StorageMode(cart.HomeBuildingId, food) == 2 && StorageMode(cart.DestinationBuildingId, food) != 2)
                        .OrderByDescending(food => DistributionStock(cart.DestinationBuildingId, food)).FirstOrDefault();
                    capacity = Math.Min(800, Math.Max(0, (int)ReadLaborShort(DistributionBuilding(cart.HomeBuildingId), 86)));
                }
                cart.Cargo = TakeDistributionStock(cart.DestinationBuildingId, resource, capacity);
                cart.Resource = resource;
                if (cart.Cargo == 0 && cart.ActionState != 13) { cart.ActionState = 8; ClearHuntingRoute(cart); return; }
                cart.ActionState = IsGranary(cart.HomeBuildingId) ? 14 : 17; StartDistributionRoute(cart, cart.HomeBuildingId);
                return;
            case 14:
            case 17:
                if (MoveDistributionRoute(cart)) DepositStorageCart(cart, cart.HomeBuildingId, returning: true);
                return;
            case 11:
                if (MoveDistributionRoute(cart)) RetireDistributionWalker(cart);
                return;
            case 5: return; // Combat motion is owned by the shared combat subsystem.
            default:
                cart.AnimationFrame = 0;
                if (++cart.WaitTicks > 4) DepositStorageCart(cart, cart.DestinationBuildingId, returning: false);
                return;
        }
    }

    [OriginalAddress(0x0048CAA0, "FUN_0048caa0")]
    [OriginalAddress(0x0045E220, "FUN_0045e220")]
    private void DepositStorageCart(Walker cart, int destination, bool returning)
    {
        int deposited = 0;
        if (IsGranary(destination))
        {
            Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(destination * 264, 264);
            deposited = LegacyDistributionRules.AcceptGranary(b, cart.Resource, cart.Cargo, DistributionStorage(destination));
        }
        else if (IsWarehouse(destination)) deposited = AddWarehouseStock(destination, cart.Resource, cart.Cargo);
        ReleaseDistributionReceiver(cart);
        RecalculateGranaryFood();
        cart.Cargo -= deposited; cart.WaitTicks = 0;
        if (cart.Cargo > 0) { cart.ActionState = 8; ClearHuntingRoute(cart); return; }
        cart.Resource = 0;
        if (returning) { RetireDistributionWalker(cart); return; }
        cart.ActionState = 11; StartDistributionRoute(cart, cart.HomeBuildingId);
    }

    [OriginalAddress(0x0045D9E0, "FUN_0045d9e0")]
    [OriginalAddress(0x0045ACF0, "FUN_0045acf0")]
    private int FindGranaryReceiver(Walker cart)
    {
        int granary = Enumerable.Range(1, MaximumBuildings - 1).Where(id => id != cart.HomeBuildingId && IsGranary(id) &&
            DistributionStaffPercent(DistributionBuilding(id)) >= 100 && StorageMode(id, cart.Resource) is 0 or 2 &&
            DistributionStock(id, cart.Resource) < StorageLimit(id, cart.Resource, StorageMode(id, cart.Resource) == 2) &&
            ReadLaborShort(DistributionBuilding(id), 86) >= 100 &&
            (ReadLaborShort(DistributionBuilding(id), 260) <= 0 ||
             DistributionStock(id, cart.Resource) + ReadLaborShort(DistributionBuilding(id), 262) < StorageLimit(id, cart.Resource, StorageMode(id, cart.Resource) == 2)))
            .Select(id => (Id: id, Route: DistributionRoute(cart.Tile, id)))
            .Where(item => item.Route is not null).OrderBy(item => item.Route!.Count).ThenBy(item => item.Id)
            .Select(item => item.Id).FirstOrDefault();
        if (granary != 0) return granary;
        return Enumerable.Range(1, MaximumBuildings - 1).Where(id => id != cart.HomeBuildingId && IsWarehouse(id) &&
            DistributionStaffPercent(DistributionBuilding(id)) >= 100 && StorageMode(id, cart.Resource) is 0 or 2 &&
            DistributionStock(id, cart.Resource) < StorageLimit(id, cart.Resource, StorageMode(id, cart.Resource) == 2) && WarehouseFree(id, cart.Resource) > 0)
            .Select(id => (Id: id, Route: DistributionRoute(cart.Tile, id))).Where(item => item.Route is not null)
            .OrderBy(item => item.Route!.Count).ThenBy(item => item.Id).Select(item => item.Id).FirstOrDefault();
    }

    [OriginalAddress(0x0045BB50, "FUN_0045bb50")]
    [OriginalAddress(0x0045DC50, "FUN_0045dc50")]
    private int FindDistributionSource(int home, int resource, bool require400)
    {
        if (resource < 1 || DistributionStockpiled(resource) || !TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, home * 264, out int start)) return 0;
        return Enumerable.Range(1, MaximumBuildings - 1).Where(id => id != home && IsDistributionSource(id) &&
            DistributionStock(id, resource) > (require400 && IsGranary(id) ? 399 : 0) && StorageMode(id, resource) != 2)
            .Select(id => (Id: id, Route: DistributionRoute(start, id), Rank: DistributionSourceDistance(home, id)))
            .Where(item => item.Route is not null).OrderBy(item => item.Rank).ThenBy(item => item.Id)
            .Select(item => item.Id).FirstOrDefault();
    }

    [OriginalAddress(0x0045DC50, "FUN_0045dc50")]
    private int DistributionSourceDistance(int home, int source)
    {
        var a = DistributionBuilding(home); var b = DistributionBuilding(source);
        return Math.Max(Math.Abs(ReadLaborShort(a, 6) - ReadLaborShort(b, 6)), Math.Abs(ReadLaborShort(a, 8) - ReadLaborShort(b, 8))) +
            Math.Abs(ReadLaborShort(a, 32) - ReadLaborShort(b, 32));
    }

    [OriginalAddress(0x0045DE80, "FUN_0045de80")]
    [OriginalAddress(0x0045DC50, "FUN_0045dc50")]
    private int FindGettingGranarySource(int home)
    {
        if (!TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, home * 264, out int start)) return 0;
        int best = 0, score = 10000;
        for (int id = 1; id < MaximumBuildings; id++)
        {
            if (id == home || !IsGranary(id)) continue;
            int stock = 0;
            for (int resource = 1; resource <= 8; resource++)
                if (StorageMode(home, resource) == 2 && StorageMode(id, resource) != 2) stock += DistributionStock(id, resource);
            if (stock <= 0 || DistributionRoute(start, id) is null) continue;
            var a = DistributionBuilding(home); var b = DistributionBuilding(id);
            // Preserve the original caller's repeated home-X argument.
            int distance = Math.Max(Math.Abs(ReadLaborShort(b, 38) - ReadLaborShort(a, 38)), Math.Abs(ReadLaborShort(b, 40) - ReadLaborShort(a, 38))) +
                Math.Abs(ReadLaborShort(a, 32) - ReadLaborShort(b, 32));
            if (stock <= 400) distance *= 2;
            if (distance < score) { score = distance; best = id; }
        }
        return best;
    }

    [RewriteOnly("One bounded shared road-route adapter for native building entrance destinations")]
    private List<int>? DistributionRoute(int from, int building)
    {
        if (building <= 0 || building >= MaximumBuildings || DistributionBuilding(building)[0] != 1 ||
            !TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, building * 264, out int road)) return null;
        return TryFindRoute(from, [road], out List<int> route, roadsOnly: true, allowDiagonal: false) ? route : null;
    }

    [RewriteOnly("Publishes the shared road endpoint for a native transport/buyer state change")]
    private bool StartDistributionRoute(Walker walker, int building)
    {
        ClearHuntingRoute(walker);
        var route = DistributionRoute(walker.Tile, building);
        if (route is null) return false;
        walker.route = route;
        walker.DestinationTile = route.Count > 0 ? route[^1] : walker.Tile;
        return true;
    }

    [OriginalAddress(0x004A5DA0, "FUN_004a5da0")]
    private bool MoveDistributionRoute(Walker walker)
    {
        if (walker.route.Count == 0 && !StartDistributionRoute(walker,
            walker.Type == WalkerType.BazaarBuyer && walker.ActionState == 9 ? walker.HomeBuildingId :
            walker.ActionState is 9 or 18 or 20 or 12 or 15 ? walker.DestinationBuildingId : walker.HomeBuildingId))
        { RetireDistributionWalker(walker); return false; }
        int oldTile = walker.Tile;
        bool arrived = AdvanceAlongRoute(walker, advanceAnimation: false);
        if (walker.Tile == oldTile && walker.route.Count == 0 && walker.Tile != walker.DestinationTile)
            RetireDistributionWalker(walker);
        return arrived;
    }

    [OriginalAddress(0x00462180, "FUN_00462180")]
    private bool HasDistributionSlot(int building, int slot, WalkerType type)
    {
        int id = ReadLaborShort(DistributionBuilding(building), slot);
        if (id <= 0) return false;
        if (active.Any(w => w.Id == id && w.HomeBuildingId == building && w.State == WalkerState.Alive && (w.Type == type || slot == 44 && w.Type == WalkerType.Recruiter))) return true;
        WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, building * 264 + slot, 0); return false;
    }

    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    [OriginalAddress(0x00462180, "FUN_00462180")]
    private Walker? CreateDistributionWalker(int home, WalkerType type, int slot, int road)
    {
        int id = AllocateId(); if (id == 0) return null;
        var walker = new Walker(id, type, road) { HomeBuildingId = home, SourceTile = road, DestinationTile = road, TerrainUsage = WalkerTerrainUsage.Roads };
        active.Add(walker); changedTiles.Add(road);
        if (slot >= 0) WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, home * 264 + slot, id);
        return walker;
    }

    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x00496140, "FUN_00496140")]
    private bool OwnsDistributionWalker(Walker walker, params int[] slots) =>
        walker.HomeBuildingId > 0 && walker.HomeBuildingId < MaximumBuildings && DistributionBuilding(walker.HomeBuildingId)[0] == 1 &&
        slots.Any(slot => ReadLaborShort(DistributionBuilding(walker.HomeBuildingId), slot) == walker.Id);

    [RewriteOnly("Releases matching native owner slots without touching a replacement walker")]
    private void RetireDistributionWalker(Walker walker)
    {
        ReleaseDistributionReceiver(walker);
        if (walker.HomeBuildingId > 0 && walker.HomeBuildingId < MaximumBuildings)
        {
            Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId * 264, 264);
            foreach (int slot in new[] { 42, 44, 180, 182 }) if (ReadLaborShort(b, slot) == walker.Id) WriteLaborShort(b, slot, 0);
        }
        Destroy(walker, false);
    }

    [RewriteOnly("Bounds-checked view of one native building record")]
    private ReadOnlySpan<byte> DistributionBuilding(int id) => Required("buildings", MaximumBuildings * 264).Span.Slice(id * 264, 264);
    [OriginalAddress(0x00462180, "FUN_00462180")]
    private bool IsGranary(int id) => id > 0 && id < MaximumBuildings && DistributionBuilding(id)[0] == 1 && ReadLaborShort(DistributionBuilding(id), 16) is 71 or 191;
    [OriginalAddress(0x0045D510, "FUN_0045d510")]
    private bool IsWarehouse(int id) => id > 0 && id < MaximumBuildings && DistributionBuilding(id)[0] == 1 && ReadLaborShort(DistributionBuilding(id), 16) is 72 or 193;
    [OriginalAddress(0x00456FC0, "FUN_00456fc0")]
    private bool IsDistributionSource(int id) => id > 0 && id < MaximumBuildings && DistributionBuilding(id)[0] == 1 && ReadLaborShort(DistributionBuilding(id), 16) is 71 or 191 or 72 or 193;
    [OriginalAddress(0x0045BB50, "FUN_0045bb50")]
    private bool DistributionStockpiled(int resource) => resource > 0 && resource < 36 && BinaryPrimitives.ReadInt32LittleEndian(session.LogicalChunks["city_data"].Span[(29792 + resource * 4)..]) != 0;
    [OriginalAddress(0x00462180, "FUN_00462180")]
    [OriginalAddress(0x004CDE60, "FUN_004cde60")]
    private int DistributionStaffPercent(ReadOnlySpan<byte> b) { int required = buildingModels?.GetModel(difficulty, (BuildingType)ReadLaborShort(b, 16))?.Employees ?? LegacyLaborModelTable.Get(ReadLaborShort(b, 16)).Employees; return required > 0 ? ReadLaborShort(b, 64) * 100 / required : 0; }
    [OriginalAddress(0x0046B100, "FUN_0046b100")]
    private ReadOnlySpan<byte> DistributionStorage(int id) => session.LogicalChunks["building_storages"].Span.Slice(DistributionBuilding(id)[193] * 196, 196);
    [OriginalAddress(0x0045BB50, "FUN_0045bb50")]
    private int StorageMode(int id, int resource) => resource is >= 0 and < 36 ? DistributionStorage(id)[10 + resource] : 1;
    [OriginalAddress(0x0045E220, "FUN_0045e220")]
    private int StorageLimit(int id, int resource, bool getting) => ReadLaborShort(DistributionStorage(id), (getting ? 124 : 52) + resource * 2);
}
