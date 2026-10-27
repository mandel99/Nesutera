using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x00462180, "FUN_00462180")]
    [OriginalAddress(0x004673C0, "FUN_004673c0")]
    private void DispatchBazaarWalkers(int id, int ordinal)
    {
        ReadOnlySpan<byte> b = DistributionBuilding(id);
        int percent = DistributionStaffPercent(b);
        if (percent <= 0 || !TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, id * 264, out int road)) return;
        bool stocked = distributionFoods.Any(food => BazaarStock(id, food - 1) > 0) || Enumerable.Range(8, 4).Any(index => BazaarStock(id, index) > 0);
        if (stocked && !HasDistributionSlot(id, 42, WalkerType.BazaarSeller))
        {
            Span<byte> writable = session.GetWritableLogicalChunk("buildings").Span.Slice(id * 264, 264);
            if (++writable[50] <= LegacyDistributionRules.SellerDelay(percent)) return;
            writable[50] = 0;
            if (CreateDistributionWalker(id, WalkerType.BazaarSeller, 42, road) is { } seller)
            { seller.ActionState = 1; InitializeServiceRoam(seller); }
        }
        else MarkBazaarDemand(id, 10);
        int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]);
        bool fancy = unchecked((sbyte)session.LogicalChunks["desirability_grid"].Span[tile]) > 30;
        if (fancy && stocked && !HasDistributionSlot(id, 180, WalkerType.BazaarSeller) &&
            CreateDistributionWalker(id, WalkerType.BazaarSeller, 180, road) is { } second)
        { second.ActionState = 1; InitializeServiceRoam(second); }
        if ((distributionPass & 7) != (ordinal & 7)) return;
        foreach ((int slot, bool goods) in new[] { (44, false), (182, true) })
        {
            if (HasDistributionSlot(id, slot, WalkerType.BazaarBuyer)) continue;
            var selection = SelectBazaarShopping(id, goods, []);
            if (selection.Source == 0) continue;
            var buyer = CreateDistributionWalker(id, WalkerType.BazaarBuyer, slot, road);
            if (buyer is null) continue;
            selection.Indices.CopyTo(buyer.ShoppingResources, 0);
            buyer.DestinationBuildingId = selection.Source; buyer.ActionState = 8;
            if (!StartDistributionRoute(buyer, selection.Source)) RetireDistributionWalker(buyer);
        }
    }

    [OriginalAddress(0x00456FC0, "FUN_00456fc0")]
    [OriginalAddress(0x00455E00, "FUN_00455e00")]
    [OriginalAddress(0x00456070, "FUN_00456070")]
    [OriginalAddress(0x004561E0, "FUN_004561e0")]
    [OriginalAddress(0x004565F0, "FUN_004565f0")]
    private (int Source, int[] Indices) SelectBazaarShopping(int home, bool goods, IReadOnlyCollection<int> exclude)
    {
        int[] demand = goods ? new[] { 158, 164, 160, 162 }.Select(field => (int)ReadLaborShort(DistributionBuilding(home), field)).ToArray() : [];
        if (goods)
        {
            Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(home * 264, 264);
            foreach (int field in new[] { 158, 164, 160, 162 })
                if (ReadLaborShort(b, field) > 0) WriteLaborShort(b, field, ReadLaborShort(b, field) - 1);
        }
        var domain = (goods ? Enumerable.Range(8, 4) : distributionFoods.Select(food => food - 1)).Where(index =>
            StorageMode(home, LegacyDistributionRules.BazaarResources[index]) != 1 && (!goods || demand[index - 8] > 0) &&
            !DistributionStockpiled(LegacyDistributionRules.BazaarResources[index])).ToArray();
        Dictionary<int, int> sources = [];
        foreach (int index in domain)
        {
            int resource = LegacyDistributionRules.BazaarResources[index];
            int source = FindBazaarSource(home, resource, exclude);
            if (source != 0) sources[index] = source;
        }
        int threshold = goods ? 100 : 600;
        int[] eligible = domain.Where(sources.ContainsKey).ToArray();
        int[] indices = eligible.Where(index => BazaarStock(home, index) == 0)
            .Concat(eligible.Where(index => BazaarStock(home, index) > 0 && BazaarStock(home, index) < threshold)).Distinct().Take(4).ToArray();
        int first = eligible.FirstOrDefault(index => BazaarStock(home, index) == 0, -1);
        if (first < 0)
        {
            var scarce = eligible.Where(index => BazaarStock(home, index) < (goods ? 100 : 50)).OrderBy(index => BazaarStock(home, index)).ToArray();
            first = scarce.Length > 0 ? scarce[0] : goods ? -1 : eligible.LastOrDefault(index => BazaarStock(home, index) < 600, -1);
        }
        if (first < 0) return (0, [-1, -1, -1, -1]);
        int[] result = [-1, -1, -1, -1]; indices.CopyTo(result, 0);
        return (sources[first], result);
    }

    [OriginalAddress(0x00456FC0, "FUN_00456fc0")]
    private int FindBazaarSource(int home, int resource, IReadOnlyCollection<int> exclude)
    {
        if (!TryGetBuildingRoadAccess(Required("buildings", MaximumBuildings * 264).Span, home * 264, out int road)) return 0;
        int homeTile = BinaryPrimitives.ReadInt32LittleEndian(DistributionBuilding(home)[12..]);
        return Enumerable.Range(1, MaximumBuildings - 1).Where(id => !exclude.Contains(id) && IsDistributionSource(id) &&
            DistributionStock(id, resource) > 0 && ChebyshevDistance(homeTile, BinaryPrimitives.ReadInt32LittleEndian(DistributionBuilding(id)[12..])) < 40)
            .Select(id => (Id: id, Route: DistributionRoute(road, id))).Where(item => item.Route is not null)
            .OrderBy(item => item.Route!.Count).ThenBy(item => item.Id).Select(item => item.Id).FirstOrDefault();
    }

    [OriginalAddress(0x00496140, "FUN_00496140")]
    [OriginalAddress(0x00496060, "FUN_00496060")]
    [OriginalAddress(0x00495F00, "FUN_00495f00")]
    [OriginalAddress(0x00495E10, "FUN_00495e10")]
    [OriginalAddress(0x00495DA0, "FUN_00495da0")]
    [OriginalAddress(0x00495CF0, "FUN_00495cf0")]
    [OriginalAddress(0x00496010, "FUN_00496010")]
    [OriginalAddress(0x00496C70, "FUN_00496c70")]
    [OriginalAddress(0x00496DE0, "FUN_00496de0")]
    private void AdvanceBazaarBuyer(Walker buyer)
    {
        if (!OwnsDistributionWalker(buyer, 44, 182)) { RetireDistributionWalker(buyer); return; }
        buyer.AnimationFrame = (buyer.AnimationFrame + 1) % 12;
        if (buyer.ActionState == 6) { buyer.IsHidden = true; return; }
        if (buyer.ActionState == 7) { buyer.AnimationFrame = 0; if (++buyer.WaitTicks > 100) { buyer.ActionState = 9; buyer.WaitTicks = 0; } return; }
        if (buyer.ActionState == 9)
        { if (MoveDistributionRoute(buyer)) RetireDistributionWalker(buyer); return; }
        if (buyer.ActionState != 8) { buyer.ActionState = 9; StartDistributionRoute(buyer, buyer.HomeBuildingId); return; }
        if (!IsDistributionSource(buyer.DestinationBuildingId)) { FinishBazaarShopping(buyer); return; }
        if (!MoveDistributionRoute(buyer)) return;
        int source = buyer.DestinationBuildingId;
        foreach (int index in buyer.ShoppingResources.Where(index => index >= 0 && index < 12))
        {
            int resource = LegacyDistributionRules.BazaarResources[index];
            if (DistributionStockpiled(resource)) continue;
            int position = Array.IndexOf(buyer.ShoppingResources, index);
            int loads = LegacyDistributionRules.BuyerLoads(index, BazaarStock(buyer.HomeBuildingId, index), buyer.ShoppingCommitted[position], DistributionStock(source, resource));
            if (loads == 0) continue;
            int freeWalkers = MaximumWalkers - 1 - active.Count(w => w.State == WalkerState.Alive);
            loads = Math.Min(loads, freeWalkers);
            if (loads == 0) continue;
            buyer.ShoppingCommitted[position] += CreateBazaarCarriers(buyer, source, index, loads) * 100;
        }
        if (buyer.ShoppingStops < 4) buyer.ShoppingHistory[buyer.ShoppingStops] = source;
        buyer.ShoppingStops++;
        if (buyer.ShoppingStops >= 3) { FinishBazaarShopping(buyer); return; }
        bool hasFood = buyer.ShoppingResources.Any(index => index is >= 0 and < 8);
        var remaining = buyer.ShoppingResources.Select((index, position) =>
            (Index: index, Need: index < 0 ? 0 : (hasFood ? index == 0 ? 800 : 600 : 400) - BazaarStock(buyer.HomeBuildingId, index) - buyer.ShoppingCommitted[position]))
            .Where(item => item.Index >= 0 && item.Need / 100 > 0).OrderByDescending(item => item.Need / 100).ToArray();
        foreach (var request in remaining)
        {
            int next = FindBazaarSource(buyer.HomeBuildingId, LegacyDistributionRules.BazaarResources[request.Index], buyer.ShoppingHistory.Take(buyer.ShoppingStops).ToArray());
            if (next == 0) continue;
            buyer.DestinationBuildingId = next; StartDistributionRoute(buyer, next); return;
        }
        FinishBazaarShopping(buyer);
    }

    [OriginalAddress(0x00495DE0, "FUN_00495de0")]
    [OriginalAddress(0x00495D30, "FUN_00495d30")]
    [OriginalAddress(0x00495D70, "FUN_00495d70")]
    private void FinishBazaarShopping(Walker buyer)
    {
        if (!buyer.ShoppingCommitted.Any(amount => amount != 0)) { RetireDistributionWalker(buyer); return; }
        buyer.ActionState = 9;
        if (!StartDistributionRoute(buyer, buyer.HomeBuildingId)) RetireDistributionWalker(buyer);
    }

    [OriginalAddress(0x00496660, "FUN_00496660")]
    [OriginalAddress(0x004968A0, "FUN_004968a0")]
    [OriginalAddress(0x00496640, "FUN_00496640")]
    private int CreateBazaarCarriers(Walker buyer, int source, int index, int loads)
    {
        // Unlike the original assertion loop in00496640, bounded chaining
        // tolerates an already-linked buyer and never hangs the city.
        int predecessor = buyer.Id, created = 0;
        for (int count = 0; count < loads; count++)
        {
            var child = CreateDistributionWalker(buyer.HomeBuildingId, WalkerType.BazaarCarrier, -1, buyer.Tile);
            if (child is null) break;
            int resource = LegacyDistributionRules.BazaarResources[index];
            TakeDistributionStock(source, resource, 100);
            child.Resource = resource; child.RequestedResource = index;
            child.Cargo = 100; child.PredecessorId = predecessor;
            child.SourceTile = buyer.Tile; child.IsHidden = true;
            predecessor = child.Id; created++;
        }
        return created;
    }

    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    [OriginalAddress(0x0049B3C0, "FUN_0049b3c0")]
    [OriginalAddress(0x004B22E0, "FUN_004b22e0")]
    private void AdvanceBazaarCarrier(Walker child)
    {
        Walker? parent = active.FirstOrDefault(w => w.Id == child.PredecessorId);
        if (child.PredecessorId < 1) { Destroy(child, false); return; }
        if (parent is null || parent.State != WalkerState.Alive)
        {
            bool unsupportedLiveParent = parent is null && child.PredecessorId < MaximumWalkers &&
                session.LogicalChunks["walkers"].Span[child.PredecessorId * RecordSize + 14] == 1;
            if (!unsupportedLiveParent && parent?.ActionState != 4 && IsBazaar(child.HomeBuildingId))
                AddBazaarStock(child.HomeBuildingId, child.RequestedResource, 100);
            Destroy(child, false); return;
        }
        if (parent.Type is not (WalkerType.BazaarBuyer or WalkerType.BazaarCarrier) || parent.ActionState == 4 || !IsBazaar(child.HomeBuildingId))
        { Destroy(child, false); return; }
        Walker? root = parent;
        for (int hop = 0; hop < 8 && root?.Type == WalkerType.BazaarCarrier; hop++)
            root = active.FirstOrDefault(w => w.Id == root.PredecessorId);
        if (root is null || root.Type != WalkerType.BazaarBuyer) { Destroy(child, false); return; }
        child.IsHidden = root.ActionState == 6 || parent.IsHidden;
        if (root.ActionState is 6 or 7) { child.AnimationFrame = 0; return; }
        child.AnimationFrame = (child.AnimationFrame + 1) % 12;
        if (child.route.Count == 0)
        {
            int goal = parent.PreviousTile;
            if (child.Tile == goal) { child.IsHidden |= child.Tile == child.SourceTile; return; }
            int dx = Math.Sign(goal % 228 - child.Tile % 228), dy = Math.Sign(goal / 228 - child.Tile / 228);
            int next = child.Tile + dx + dy * 228;
            if ((uint)next >= LegacyCityMapSnapshot.TileCount) return;
            if (!CanTakeRouteStep(child.Tile, next, child.TerrainUsage))
            {
                // Keep the requested global road-only entrance/pavement rule
                // when native direct following would cut a non-road corner.
                int horizontal = child.Tile + dx, vertical = child.Tile + dy * 228;
                if (dx != 0 && CanTakeRouteStep(child.Tile, horizontal, child.TerrainUsage)) next = horizontal;
                else if (dy != 0 && CanTakeRouteStep(child.Tile, vertical, child.TerrainUsage)) next = vertical;
                else return;
            }
            child.route = [next]; child.RoutePosition = 0;
        }
        if (AdvanceAlongRoute(child, advanceAnimation: false)) ClearHuntingRoute(child);
        child.IsHidden |= child.Tile == child.SourceTile;
    }

    [OriginalAddress(0x00493EC0, "FUN_00493ec0")]
    [OriginalAddress(0x00493610, "FUN_00493610")]
    private void AdvanceBazaarSeller(Walker seller)
    {
        if (!OwnsDistributionWalker(seller, 42, 180)) { RetireDistributionWalker(seller); return; }
        seller.AnimationFrame = (seller.AnimationFrame + 1) % 12;
        if (seller.ActionState == 2)
        {
            if (seller.route.Count == 0 && !StartDistributionRoute(seller, seller.HomeBuildingId)) { RetireDistributionWalker(seller); return; }
            if (seller.ProgressOnTile == MovementUnitsPerTile - 1) SupplyBazaarHousing(seller);
            if (AdvanceAlongRoute(seller, advanceAnimation: false)) RetireDistributionWalker(seller);
            return;
        }
        seller.RoamTicks++;
        int stock = distributionFoods.Sum(food => BazaarStock(seller.HomeBuildingId, food - 1)) + Enumerable.Range(8, 4).Sum(index => BazaarStock(seller.HomeBuildingId, index));
        if (seller.RoamTicks >= 384 || seller.RoamTicks > 95 && stock == 0)
        { seller.ActionState = 2; ClearHuntingRoute(seller); return; }
        if (seller.route.Count == 0 && !TryBuildNextSafetyPatrolStep(seller)) { seller.ActionState = 2; return; }
        if (seller.ProgressOnTile == MovementUnitsPerTile - 1) SupplyBazaarHousing(seller);
        if (AdvanceAlongRoute(seller, advanceAnimation: false)) ClearHuntingRoute(seller);
    }

    [OriginalAddress(0x00483910, "FUN_00483910")]
    [OriginalAddress(0x004BAA10, "FUN_004baa10")]
    private void SupplyBazaarHousing(Walker seller)
    {
        int home = seller.HomeBuildingId;
        if (housingLevels is null || !TryLocal(seller.Tile, out int x, out int y)) return;
        int hits = 0;
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
        {
            if (!TryTile(x + dx, y + dy, out int tile)) continue;
            int id = ReadLaborShort(session.LogicalChunks["building_grid"].Span, tile * 2);
            if (id <= 0 || id >= MaximumBuildings) continue;
            ReadOnlySpan<byte> house = DistributionBuilding(id);
            int population = ReadLaborShort(house, 28), level = ReadLaborShort(house, 18);
            if (house[0] != 1 || house[5] == 0 || population <= 0 || level < 0 || level > 19) continue;
            hits++;
            var model = housingLevels[Math.Min(19, level + 1)];
            foreach (var demand in new (int Field, int Need)[] { (158, model.Pottery), (164, model.Beer), (160, model.Linen), (162, model.Jewelry) })
                if (demand.Need > 0) WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, home * 264 + demand.Field, 10);
            int foodBudget = model.FoodTypes is > 0 and < 4 ? model.FoodTypes + 1 : model.FoodTypes;
            foreach (int food in distributionFoods)
            {
                if (foodBudget <= 0) break;
                int index = food - 1;
                int stored = ReadLaborShort(DistributionBuilding(id), 84 + index * 2), available = BazaarStock(home, index);
                int wanted = Math.Min(population * 2, population * 6 - stored);
                if (wanted <= 0 || available <= 0) continue;
                int portion = index is 0 or 6 ? wanted : wanted / 2;
                int amount = available < wanted && available < portion ? available : portion;
                TransferBazaarToHouse(home, id, index, 84 + index * 2, Math.Min(amount, available)); foodBudget--;
            }
            foreach (var good in new (int Index, int Offset, int Requirement)[] { (8, 100, model.Pottery), (9, 102, model.Beer), (10, 104, model.Linen), (11, 106, model.Jewelry) })
            {
                int required = good.Requirement * (house[3] > 1 ? 4 : 1);
                if (required <= 0) continue;
                int amount = Math.Min(BazaarStock(home, good.Index), required * 2 - ReadLaborShort(DistributionBuilding(id), good.Offset));
                if (amount > 0) TransferBazaarToHouse(home, id, good.Index, good.Offset, amount);
            }
        }
        WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, home * 264 + 24, Math.Min(300, ReadLaborShort(DistributionBuilding(home), 24) + hits));
    }

    [OriginalAddress(0x00483910, "FUN_00483910")]
    private void TransferBazaarToHouse(int bazaar, int house, int index, int offset, int amount)
    {
        if (amount <= 0) return;
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        WriteLaborShort(buildings, bazaar * 264 + 86 + index * 2, BazaarStock(bazaar, index) - amount);
        WriteLaborShort(buildings, house * 264 + offset, ReadLaborShort(buildings, house * 264 + offset) + amount);
    }

    [OriginalAddress(0x00455CC0, "FUN_00455cc0")]
    private void MarkBazaarDemand(int bazaar, int radius)
    {
        if (housingLevels is null) return;
        int tile = BinaryPrimitives.ReadInt32LittleEndian(DistributionBuilding(bazaar)[12..]);
        for (int id = 1; id < MaximumBuildings; id++)
        {
            var house = DistributionBuilding(id); int level = ReadLaborShort(house, 18);
            if (house[0] != 1 || house[5] == 0 || ReadLaborShort(house, 28) <= 0 || level < 0 || level > 19 ||
                ChebyshevDistance(tile, BinaryPrimitives.ReadInt32LittleEndian(house[12..])) > radius) continue;
            var model = housingLevels[Math.Min(19, level + 1)];
            foreach (var demand in new (int Offset, int Requirement)[] { (158, model.Pottery), (160, model.Linen), (162, model.Jewelry), (164, model.Beer) })
                if (demand.Requirement > 0) WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, bazaar * 264 + demand.Offset, 10);
        }
    }

    [OriginalAddress(0x00462180, "FUN_00462180")]
    private bool IsBazaar(int id) => id > 0 && id < MaximumBuildings && DistributionBuilding(id)[0] == 1 && ReadLaborShort(DistributionBuilding(id), 16) is 70 or 190;
    [OriginalAddress(0x00483910, "FUN_00483910")]
    private int BazaarStock(int id, int index) => index is >= 0 and < 12 ? Math.Max(0, (int)ReadLaborShort(DistributionBuilding(id), 86 + index * 2)) : 0;
    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    private void AddBazaarStock(int id, int index, int amount) { if (index is >= 0 and < 12) WriteLaborShort(session.GetWritableLogicalChunk("buildings").Span, id * 264 + 86 + index * 2, Math.Min(short.MaxValue, BazaarStock(id, index) + amount)); }
}
