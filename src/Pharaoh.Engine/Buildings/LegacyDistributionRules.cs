using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Buildings;

public static class LegacyDistributionRules
{
    public static readonly int[] BazaarResources = [1, 2, 3, 4, 5, 6, 7, 8, 15, 13, 17, 19];

    [OriginalAddress(0x00496C70, "FUN_00496c70")]
    public static int BazaarIndex(int resource) => Array.IndexOf(BazaarResources, resource);

    [OriginalAddress(0x00496550, "FUN_00496550")]
    [OriginalAddress(0x00496460, "FUN_00496460")]
    [OriginalAddress(0x004963B0, "FUN_004963b0")]
    public static int BuyerLoads(int index, int stored, int committed, int available)
    {
        int needed = (index == 0 ? 800 : index < 8 ? 600 : 400) - stored - committed;
        if (needed < 1 || available < 1) return 0;
        return Math.Min(needed / 100 + 1, Math.Min(index < 8 ? 8 : 4, Math.Max(1, available / 100)));
    }

    [OriginalAddress(0x0045B490, "FUN_0045b490")]
    [OriginalAddress(0x0045B570, "FUN_0045b570")]
    [OriginalAddress(0x0045B2A0, "FUN_0045b2a0")]
    public static int WarehouseBayImage(int resource, int amount)
    {
        if (resource is < 1 or > 35 || amount <= 0) return 13077;
        int frame = amount == 400 ? 3 : amount > 400 || amount < 200 ? 0 : amount < 300 ? 1 : 2;
        return (resource < 31 ? 13078 + (resource - 1) * 4 : 23279 + (resource - 31) * 4) + frame;
    }

    [OriginalAddress(0x00462180, "FUN_00462180")]
    public static int SellerDelay(int percentage) => percentage >= 100 ? 2 : percentage >= 75 ? 3 : percentage >= 50 ? 4 : percentage >= 25 ? 5 : 10;

    [OriginalAddress(0x0045E220, "FUN_0045e220")]
    public static int AcceptGranary(Span<byte> building, int resource, int amount, ReadOnlySpan<byte> storage)
    {
        if (resource is < 1 or > 8 || amount <= 0) return 0;
        int free = BinaryPrimitives.ReadInt16LittleEndian(building[86..]);
        if (free <= 0) return 0;
        int stock = BinaryPrimitives.ReadInt16LittleEndian(building[(86 + resource * 2)..]);
        int mode = storage[10 + resource];
        int limit = BinaryPrimitives.ReadInt16LittleEndian(storage[((mode == 0 ? 52 : 124) + resource * 2)..]);
        if (mode is 0 or 2 && stock >= limit) return 0;
        int transferred = Math.Min(free, amount);
        BinaryPrimitives.WriteInt16LittleEndian(building[86..], (short)(free - transferred));
        BinaryPrimitives.WriteInt16LittleEndian(building[(86 + resource * 2)..], checked((short)(stock + transferred)));
        return transferred;
    }
}
