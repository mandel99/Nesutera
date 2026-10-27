using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x0048B620, "type-4 meat delivery: 8 search, 10 travel, 13 unload, 15 return")]
    [OriginalAddress(0x0045E220, "deposit meat and decrease granary free capacity")]
    internal void AdvanceMeatCart(Walker cart)
    {
        if (!IsActiveLodge(cart.HomeBuildingId)) { RetireMeatCart(cart); return; }
        switch (cart.ActionState)
        {
            case 8:
                cart.AnimationFrame = 0;
                if (++cart.WaitTicks <= 30) return;
                cart.WaitTicks = 0;
                if (TrySelectMeatGranary(cart)) cart.ActionState = 10;
                return;
            case 10:
                if (!CanAcceptMeat(cart.DestinationBuildingId))
                { ClearHuntingRoute(cart); cart.ActionState = 8; cart.WaitTicks = 0; return; }
                if (cart.route.Count == 0 && !TryFindRoadCartRoute(cart, cart.DestinationTile))
                { cart.ActionState = 8; return; }
                if (AdvanceAlongRoute(cart)) { cart.ActionState = 13; cart.WaitTicks = 0; ClearHuntingRoute(cart); }
                return;
            case 13:
                if (++cart.WaitTicks <= 5) return;
                if (CanAcceptMeat(cart.DestinationBuildingId))
                {
                    Span<byte> granary = session.GetWritableLogicalChunk("buildings").Span.Slice(cart.DestinationBuildingId * 264, 264);
                    int free = BinaryPrimitives.ReadInt16LittleEndian(granary[86..]);
                    int amount = Math.Min(cart.Cargo, free);
                    BinaryPrimitives.WriteInt16LittleEndian(granary[86..], checked((short)(free - amount)));
                    BinaryPrimitives.WriteInt16LittleEndian(granary[102..], checked((short)(BinaryPrimitives.ReadInt16LittleEndian(granary[102..]) + amount)));
                    cart.Cargo -= amount;
                    RecalculateGranaryFood();
                }
                if (cart.Cargo > 0) { cart.ActionState = 8; cart.WaitTicks = 0; return; }
                cart.ActionState = 15; cart.DestinationTile = cart.SourceTile;
                ClearHuntingRoute(cart);
                return;
            case 15:
                if (cart.route.Count == 0 && !TryFindRoadCartRoute(cart, cart.SourceTile)) { RetireMeatCart(cart); return; }
                if (AdvanceAlongRoute(cart)) RetireMeatCart(cart);
                return;
            default:
                RetireMeatCart(cart); return;
        }
    }

    [OriginalAddress(0x0048AE50, "select a road-reachable destination for a producer's cart")]
    private bool TrySelectMeatGranary(Walker cart)
    {
        ReadOnlySpan<byte> buildings = Required("buildings", MaximumBuildings * 264).Span;
        Dictionary<int, int> goals = [];
        for (int id = 1; id < MaximumBuildings; id++)
            if (CanAcceptMeat(id) && TryGetBuildingRoadAccess(buildings, id * 264, out int road))
                goals.TryAdd(road, id);
        if (goals.Count == 0 || !TryFindRoute(cart.Tile, goals.Keys, out List<int> route, roadsOnly: true, allowDiagonal: false)) return false;
        int tile = route.Count == 0 ? cart.Tile : route[^1];
        cart.DestinationBuildingId = goals[tile]; cart.DestinationTile = tile;
        cart.route = route; cart.RoutePosition = 0; cart.ProgressOnTile = 0; return true;
    }

    [OriginalAddress(0x0045E220, "granary resource acceptance and free capacity")]
    private bool CanAcceptMeat(int id)
    {
        if (id <= 0 || id >= MaximumBuildings) return false;
        ReadOnlySpan<byte> building = Required("buildings", MaximumBuildings * 264).Span.Slice(id * 264, 264);
        if (building[0] != 1 || BinaryPrimitives.ReadInt16LittleEndian(building[16..]) != 71 ||
            BinaryPrimitives.ReadInt16LittleEndian(building[86..]) <= 0 || building[68] == 0) return false;
        int required = buildingModels?.GetModel(difficulty, BuildingType.Granary)?.Employees ?? 12;
        if (BinaryPrimitives.ReadInt16LittleEndian(building[64..]) * 2 < required) return false;
        ReadOnlySpan<byte> storages = session.LogicalChunks["building_storages"].Span;
        int index = building[193] * 196;
        if (index + 196 > storages.Length || storages[index + 8] == 0) return false;
        byte mode = storages[index + 18];
        int limitOffset = mode == 0 ? 68 : 140;
        return mode is 0 or 2 && BinaryPrimitives.ReadInt16LittleEndian(building[102..]) <
            BinaryPrimitives.ReadInt16LittleEndian(storages[(index + limitOffset)..]);
    }

    [RewriteOnly("Uses the shared bounded road solver for the verified meat-cart actions")]
    private bool TryFindRoadCartRoute(Walker cart, int destination)
    {
        if (!TryFindRoute(cart.Tile, [destination], out List<int> route, roadsOnly: true, allowDiagonal: false)) return false;
        cart.route = route; cart.RoutePosition = 0; cart.ProgressOnTile = 0; return true;
    }

    [OriginalAddress(0x0048AEF0, "retire the returned producer cart and release its source slot")]
    private void RetireMeatCart(Walker cart)
    {
        if (cart.HomeBuildingId > 0 && cart.HomeBuildingId < MaximumBuildings)
        {
            Span<byte> source = session.GetWritableLogicalChunk("buildings").Span.Slice(cart.HomeBuildingId * 264, 264);
            if (BinaryPrimitives.ReadInt16LittleEndian(source[42..]) == cart.Id)
                BinaryPrimitives.WriteInt16LittleEndian(source[42..], 0);
        }
        Destroy(cart, false);
    }
}
