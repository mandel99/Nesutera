using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x0045D510, "FUN_0045d510")]
    [OriginalAddress(0x0045E360, "FUN_0045e360")]
    private int DistributionStock(int id, int resource)
    {
        if (resource < 1 || resource >= 36 || id <= 0 || id >= MaximumBuildings) return 0;
        if (IsGranary(id)) return resource < 9 ? Math.Max(0, (int)ReadLaborShort(DistributionBuilding(id), 86 + resource * 2)) : 0;
        int total = 0, part = id;
        for (int count = 0; count < 8; count++)
        {
            part = ReadLaborShort(DistributionBuilding(part), 58);
            if (part <= 0 || part >= MaximumBuildings) break;
            if (ReadLaborShort(DistributionBuilding(part), 18) == resource) total += Math.Max(0, (int)ReadLaborShort(DistributionBuilding(part), 60));
        }
        return total;
    }

    [OriginalAddress(0x0045E360, "FUN_0045e360")]
    [OriginalAddress(0x0045B570, "FUN_0045b570")]
    private int TakeDistributionStock(int id, int resource, int requested)
    {
        int amount = Math.Min(Math.Max(0, requested), DistributionStock(id, resource));
        if (amount == 0) return 0;
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        if (IsGranary(id))
        {
            WriteLaborShort(buildings, id * 264 + 86 + resource * 2, DistributionStock(id, resource) - amount);
            WriteLaborShort(buildings, id * 264 + 86, ReadLaborShort(buildings, id * 264 + 86) + amount);
        }
        else
        {
            int remaining = amount;
            while (remaining > 0)
            {
                int part = id, selected = 0, smallest = 401;
                for (int count = 0; count < 8; count++)
                {
                    part = ReadLaborShort(buildings, part * 264 + 58);
                    if (part <= 0 || part >= MaximumBuildings) break;
                    int stock = ReadLaborShort(buildings, part * 264 + 60);
                    if (ReadLaborShort(buildings, part * 264 + 18) == resource && stock > 0 && stock < smallest)
                    { selected = part; smallest = stock; }
                }
                if (selected == 0) break;
                int withdrawn = Math.Min(remaining, smallest); remaining -= withdrawn;
                WriteLaborShort(buildings, selected * 264 + 60, smallest - withdrawn);
                if (smallest == withdrawn) WriteLaborShort(buildings, selected * 264 + 18, 0);
                UpdateWarehouseBayImage(selected, resource, smallest - withdrawn);
                UpdateWarehouseResourceTotals(resource, -withdrawn);
            }
            amount -= remaining;
        }
        RecalculateGranaryFood();
        return amount;
    }

    [OriginalAddress(0x0045E220, "FUN_0045e220")]
    private void AddDistributionStock(int id, int resource, int amount)
    {
        if (!IsGranary(id) || resource is < 1 or > 8) return;
        Span<byte> b = session.GetWritableLogicalChunk("buildings").Span.Slice(id * 264, 264);
        WriteLaborShort(b, 86, Math.Max(0, ReadLaborShort(b, 86) - amount));
        WriteLaborShort(b, 86 + resource * 2, checked(ReadLaborShort(b, 86 + resource * 2) + amount));
        RecalculateGranaryFood();
    }

    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x00496140, "FUN_00496140")]
    [OriginalAddress(0x00493EC0, "FUN_00493ec0")]
    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    [OriginalAddress(0x004B1D50, "FUN_004b1d50")]
    internal void AdvanceDistributionWalker(Walker walker)
    {
        if (walker.Type != WalkerType.BazaarCarrier) walker.IsHidden = false;
        if (walker.ActionState == 4)
        {
            walker.IsHidden = false;
            walker.WaitTicks = Math.Max(0, walker.WaitTicks);
            if (++walker.WaitTicks > 127) RetireDistributionWalker(walker);
            return;
        }
        switch (walker.Type)
        {
            case WalkerType.StorageTransporter: AdvanceStorageTransporter(walker); break;
            case WalkerType.BazaarBuyer: AdvanceBazaarBuyer(walker); break;
            case WalkerType.BazaarSeller: AdvanceBazaarSeller(walker); break;
            case WalkerType.BazaarCarrier: AdvanceBazaarCarrier(walker); break;
        }
    }

    [OriginalAddress(0x00496140, "FUN_00496140")]
    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x00493EC0, "FUN_00493ec0")]
    private static void RestoreDistributionFields(ReadOnlySpan<byte> record, Walker walker)
    {
        if (!IsDistributionType(walker.Type)) return;
        walker.HomeBuildingId = ReadLaborShort(record, 90);
        if (walker.Type == WalkerType.StorageTransporter && walker.ActionState is 9 or 10 && walker.DestinationBuildingId != walker.HomeBuildingId)
            walker.ReservedDistributionDestination = walker.DestinationBuildingId;
        walker.AnimationFrame = record[1]; walker.IsHidden = record[101] != 0;
        if (walker.Type == WalkerType.StorageTransporter)
        { walker.Resource = record[11]; walker.RequestedResource = record[122]; walker.Cargo = ReadLaborShort(record, 116); }
        if (walker.Type == WalkerType.BazaarCarrier)
        {
            walker.PredecessorId = ReadLaborShort(record, 104); walker.RequestedResource = record[122]; walker.Cargo = 100;
            if (walker.RequestedResource < 12) walker.Resource = Buildings.LegacyDistributionRules.BazaarResources[walker.RequestedResource];
        }
        if (walker.Type == WalkerType.BazaarSeller)
        {
            walker.RoamTicks = ReadLaborShort(record, 68); walker.RoamFree = record[70] != 0; walker.RoamInitialized = true;
            walker.RoamStepCounter = record[71]; walker.RoamTurnDirection = unchecked((sbyte)record[72]);
            if (walker.RoamTurnDirection is not (2 or -2)) walker.RoamTurnDirection = 2;
            walker.RoamTurnCountdown = unchecked((sbyte)record[73]);
        }
        if (walker.Type == WalkerType.BazaarBuyer)
        {
            walker.ShoppingStops = record[147];
            for (int index = 0; index < 4; index++)
            {
                walker.ShoppingResources[index] = BinaryPrimitives.ReadInt32LittleEndian(record[(156 + index * 4)..]);
                walker.ShoppingHistory[index] = BinaryPrimitives.ReadInt32LittleEndian(record[(172 + index * 4)..]);
                walker.ShoppingCommitted[index] = BinaryPrimitives.ReadInt32LittleEndian(record[(188 + index * 4)..]);
            }
        }
    }

    [OriginalAddress(0x00496140, "FUN_00496140")]
    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x00493EC0, "FUN_00493ec0")]
    private static void PublishDistributionFields(Span<byte> record, Walker walker)
    {
        if (!IsDistributionType(walker.Type)) return;
        WriteLaborShort(record, 90, walker.HomeBuildingId);
        // Preserve unions owned by other figure types, including the generic
        // migrant/animal publication fields. The original records remain the base.
        foreach ((int offset, int length) in new[] { (100, 1), (124, 1), (129, 2), (151, 1) })
        {
            if (walker.NativeRecord is { } native) native.AsSpan(offset, length).CopyTo(record[offset..]);
            else record.Slice(offset, length).Clear();
        }
        // Household-home92 is an unrelated native union for these types.
        if (walker.NativeRecord is { } original) original.AsSpan(92, 2).CopyTo(record[92..]); else record.Slice(92, 2).Clear();
        if (walker.Type == WalkerType.BazaarBuyer) WriteLaborShort(record, 66, 800);
        if (walker.Type == WalkerType.BazaarSeller) WriteLaborShort(record, 66, 384);
        record[12] = 0;
        record[1] = (byte)walker.AnimationFrame; record[101] = walker.IsHidden ? (byte)1 : (byte)0;
        if (walker.Type == WalkerType.StorageTransporter)
        { record[11] = (byte)walker.Resource; record[122] = (byte)walker.RequestedResource; WriteLaborShort(record, 116, walker.Cargo); }
        if (walker.Type == WalkerType.BazaarCarrier)
        { record[122] = (byte)walker.RequestedResource; WriteLaborShort(record, 104, walker.PredecessorId); }
        if (walker.Type == WalkerType.BazaarSeller)
        {
            WriteLaborShort(record, 68, walker.RoamTicks); record[70] = walker.RoamFree ? (byte)1 : (byte)0;
            record[71] = walker.RoamStepCounter; record[72] = unchecked((byte)walker.RoamTurnDirection); record[73] = unchecked((byte)walker.RoamTurnCountdown);
        }
        if (walker.Type == WalkerType.BazaarBuyer)
        {
            record[147] = walker.ShoppingStops;
            for (int index = 0; index < 4; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(record[(156 + index * 4)..], walker.ShoppingResources[index]);
                BinaryPrimitives.WriteInt32LittleEndian(record[(172 + index * 4)..], walker.ShoppingHistory[index]);
                BinaryPrimitives.WriteInt32LittleEndian(record[(188 + index * 4)..], walker.ShoppingCommitted[index]);
            }
        }
    }

    [RewriteOnly("Limits figure loading/publication to the implemented native distribution roles")]
    private static bool IsDistributionType(WalkerType type) => type is WalkerType.StorageTransporter or WalkerType.BazaarBuyer or WalkerType.BazaarSeller or WalkerType.BazaarCarrier;
}
