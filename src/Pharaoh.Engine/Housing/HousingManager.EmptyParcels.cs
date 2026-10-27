using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Housing;

public sealed partial class HousingManager
{
    [RewriteOnly("Repairs the exact empty merged type10 state produced by the former missing native population gate")]
    private void RepairLegacyEmptyMergedPlots()
    {
        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * RecordSize;
            if (buildings[offset] is not (1 or 3) || buildings[offset + 3] != 2 || buildings[offset + 4] == 0 ||
                ReadShort(offset + 16) != 10 || ReadShort(offset + 28) != 0) continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
            if (!TryLocal(tile, out int x, out int y) || x + 2 > width || y + 2 > height) continue;
            int[] cells = [tile, tile + 1, tile + 228, tile + 229];
            if (cells.Any(cell => BinaryPrimitives.ReadInt16LittleEndian(buildingGrid.AsSpan(cell * 2)) != id ||
                (BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(cell * 4)) & TerrainBuilding) == 0)) continue;
            int[] available = Enumerable.Range(1, MaximumBuildings - 1).Where(candidate => buildings[candidate * RecordSize] == 0).Take(3).ToArray();
            if (available.Length != 3) continue;
            RestoreFourVacantPlots(id, available, cells, x, y);
        }
        if (recoveredPlotTiles.Count != 0) Publish();
    }

    [OriginalAddress(0x0046CF40, "FUN_0046cf40")]
    [OriginalAddress(0x0046A400, "FUN_0046a400")]
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    private void RestoreFourVacantPlots(int source, int[] available, int[] cells, int x, int y)
    {
        int[] ids = [source, .. available];
        for (int index = 0; index < 4; index++)
        {
            int id = ids[index], cell = cells[index];
            Span<byte> record = buildings.AsSpan(id * RecordSize, RecordSize);
            if (index != 0)
            {
                record.Clear(); LegacyBuildingOwnership.Initialize(record, session.LogicalChunks);
                record[0] = 3; record[194] = 0x32;
            }
            // Preserve the original owner's reservation and opaque metadata;
            // new parcels receive independent, unreserved native records.
            record[3] = 1; record[4] = 0; record[5] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(record[6..], (short)(x + index % 2));
            BinaryPrimitives.WriteInt16LittleEndian(record[8..], (short)(y + index / 2));
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], cell);
            BinaryPrimitives.WriteInt16LittleEndian(record[16..], 10);
            BinaryPrimitives.WriteInt16LittleEndian(record[18..], 0);
            BinaryPrimitives.WriteInt16LittleEndian(record[28..], 0);
            BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(cell * 2), (short)id);
            BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(cell * 4), LegacyHousingPlacement.ForSaleImageId);
            bitfields[cell] &= 0xe0; edge[cell] = (byte)((edge[cell] & 0x80) | 0x40);
            recoveredPlotTiles.Add(cell);
        }
    }
}
