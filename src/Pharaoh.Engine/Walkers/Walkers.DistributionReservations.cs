using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x0045D9E0, "FUN_0045d9e0")]
    private void ReserveDistributionReceiver(Walker cart, int destination)
    {
        if (!IsGranary(destination)) return;
        cart.ReservedDistributionDestination = destination;
        Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(destination * 264, 264);
        WriteLaborShort(b, 260, ReadLaborShort(b, 260) + 1);
        WriteLaborShort(b, 262, ReadLaborShort(b, 262) + cart.Cargo);
    }

    [OriginalAddress(0x0048CAA0, "FUN_0048caa0")]
    private void ReleaseDistributionReceiver(Walker cart)
    {
        int destination = cart.ReservedDistributionDestination;
        cart.ReservedDistributionDestination = 0;
        if (!IsGranary(destination)) return;
        Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(destination * 264, 264);
        WriteLaborShort(b, 260, Math.Max(0, ReadLaborShort(b, 260) - 1));
        WriteLaborShort(b, 262, Math.Max(0, ReadLaborShort(b, 262) - cart.Cargo));
    }

    [OriginalAddress(0x0045ACF0, "FUN_0045acf0")]
    private int WarehouseFree(int id, int resource)
    {
        int part = id, total = 0;
        for (int count = 0; count < 8; count++)
        {
            part = ReadLaborShort(DistributionBuilding(part), 58);
            if (part <= 0 || part >= MaximumBuildings) break;
            int kind = ReadLaborShort(DistributionBuilding(part), 18);
            if (kind == 0 || kind == resource) total += Math.Max(0, 400 - ReadLaborShort(DistributionBuilding(part), 60));
        }
        return total;
    }

    [OriginalAddress(0x0045B2A0, "FUN_0045b2a0")]
    [OriginalAddress(0x0045B230, "FUN_0045b230")]
    private int AddWarehouseStock(int id, int resource, int requested)
    {
        if (ReadLaborShort(DistributionBuilding(id), 64) <= 0 || ReadLaborShort(DistributionBuilding(id), 74) != 0) return 0;
        int limit = StorageLimit(id, resource, StorageMode(id, resource) != 0);
        int amount = Math.Min(requested, Math.Min(WarehouseFree(id, resource), Math.Max(0, limit - DistributionStock(id, resource))));
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        int remaining = amount;
        while (remaining > 0)
        {
            int part = id, selected = 0, largest = -1;
            for (int count = 0; count < 8; count++)
            {
                part = ReadLaborShort(buildings, part * 264 + 58);
                if (part <= 0 || part >= MaximumBuildings) break;
                int kind = ReadLaborShort(buildings, part * 264 + 18), stock = ReadLaborShort(buildings, part * 264 + 60);
                if ((kind == 0 || kind == resource) && stock < 400 && stock > largest) { selected = part; largest = stock; }
            }
            if (selected == 0) return amount - remaining;
            int deposited = Math.Min(remaining, 400 - largest); remaining -= deposited;
            WriteLaborShort(buildings, selected * 264 + 18, resource);
            WriteLaborShort(buildings, selected * 264 + 60, largest + deposited);
            UpdateWarehouseBayImage(selected, resource, largest + deposited);
            UpdateWarehouseResourceTotals(resource, deposited);
        }
        return amount;
    }
    [OriginalAddress(0x0045B570, "FUN_0045b570")]
    [OriginalAddress(0x0045B2A0, "FUN_0045b2a0")]
    private void UpdateWarehouseResourceTotals(int resource, int amount)
    {
        Span<byte> city = session.GetWritableLogicalChunk("city_data").Span;
        foreach ((int offset, int delta) in new[] { (37432 + resource * 4, -amount), (37576 + resource * 4, amount) })
            BinaryPrimitives.WriteInt32LittleEndian(city[offset..], BinaryPrimitives.ReadInt32LittleEndian(city[offset..]) + delta);
    }

    [OriginalAddress(0x0045B570, "FUN_0045b570")]
    [OriginalAddress(0x0045B2A0, "FUN_0045b2a0")]
    private void UpdateWarehouseBayImage(int bay, int resource, int amount)
    {
        int tile = BinaryPrimitives.ReadInt32LittleEndian(DistributionBuilding(bay)[12..]);
        if (!TryLocal(tile, out _, out _)) return;
        BinaryPrimitives.WriteInt32LittleEndian(session.GetWritableLogicalChunk("image_grid").Span[(tile * 4)..], LegacyDistributionRules.WarehouseBayImage(resource, amount));
        housingImageChangedTiles.Add(tile);
    }

}
