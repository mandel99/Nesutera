using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Housing;

public readonly record struct LegacyHousingAdvance(
    int UpdatedHouses,
    int ExpandedHouses,
    int RemovedHouses,
    IReadOnlyList<int> ChangedTiles)
{
    public bool TerrainChanged => ChangedTiles.Count != 0;
    public bool FootprintChanged { get; init; }
    public bool RequiresTerrainRebuild => FootprintChanged || ExpandedHouses != 0 || RemovedHouses != 0;
}

/// <summary>
/// Reconstructs the house evolution handlers at 00452980-0045399f. Each
/// occupied housing building is checked against the installed difficulty model;
/// the three original footprint transitions consume compatible houses, empty
/// land, or garden cells and the inverse transitions split the footprint again.
/// </summary>
public sealed partial class HousingManager
{
    public const int DispatcherPeriod = 51;
    public const int RecordSize = 264;
    public const int MaximumBuildings = 4000;

    private const uint TerrainBuilding = 0x8;
    private const uint Garden = 0x20;
    private const uint ExpansionBlockingMask = 0xeefed77f;
    private const uint BuildingTerrainPreserveMask = 0x03052e80;
    private const uint ClearedBuildingTerrainMask = 0x03052e84;
    private const int RoadDistanceOffset = 32;
    private const int UnreachableChecksOffset = 36;
    private const int RoadAccessRemovalChecks = 5;
    private static readonly int[] LocalGroupStarts = [749, 755, 761, 767, 773, 779, 783, 787, 789, 791];

    private readonly LegacyMissionSession session;
    private readonly IReadOnlyList<LegacyHousingLevel> levels;
    private readonly byte[] scenario;
    private byte[] terrain;
    private byte[] buildingGrid;
    private byte[] buildings;
    private byte[] images;
    private byte[] bitfields;
    private byte[] edge;
    private byte[] desirability;
    private byte[] random;
    private readonly int width;
    private readonly int height;
    private readonly int origin;
    private long pendingTicks;
    private readonly HashSet<int> recoveredPlotTiles = [];

    private HousingManager(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel> levels)
    {
        this.session = session;
        this.levels = levels;
        scenario = Required(session, "scenario_info", 60, exact: false).ToArray();
        terrain = Required(session, "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        buildingGrid = Required(session, "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).ToArray();
        buildings = Required(session, "buildings", MaximumBuildings * RecordSize).ToArray();
        images = Required(session, "image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        bitfields = Required(session, "bitfields_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        edge = Required(session, "edge_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        desirability = Required(session, "desirability_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        random = Required(session, "random_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario.AsSpan(44));
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario.AsSpan(48));
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario.AsSpan(52));
        RepairLegacyEmptyMergedPlots();
    }

    public static HousingManager Create(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count != 20)
            throw new ArgumentException("The housing model must contain exactly 20 levels.", nameof(levels));
        return new HousingManager(session, levels);
    }

    [OriginalAddress(0x00452150, "Housing evolution dispatcher reset")]
    [OriginalAddress(0x00452980, "Housing level handlers through 0045399f")]
    public LegacyHousingAdvance Advance(long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        pendingTicks = checked(pendingTicks + legacyTicks);
        int updated = 0;
        int expanded = 0;
        int removed = 0;
        bool footprintChanged = false;
        HashSet<int> changed = [];
        while (pendingTicks >= DispatcherPeriod)
        {
            pendingTicks -= DispatcherPeriod;
            LegacyHousingAdvance pass = AdvancePass();
            updated += pass.UpdatedHouses;
            expanded += pass.ExpandedHouses;
            removed += pass.RemovedHouses;
            footprintChanged |= pass.RequiresTerrainRebuild;
            changed.UnionWith(pass.ChangedTiles);
        }
        return new LegacyHousingAdvance(updated, expanded, removed, changed.Order().ToArray())
            { FootprintChanged = footprintChanged };
    }

    [OriginalAddress(0x00453AF0, "FUN_00453af0")]
    [OriginalAddress(0x00453B40, "FUN_00453b40")]
    [OriginalAddress(0x00457AF0, "FUN_00457af0")]
    [OriginalAddress(0x00452980, "FUN_00452980")]
    [OriginalAddress(0x00452A20, "FUN_00452a20")]
    public LegacyHousingAdvance AdvancePass()
    {
        RefreshMutableChunks();
        int updated = 0;
        int expanded = 0;
        int removed = 0;
        bool footprintChanged = recoveredPlotTiles.Count != 0;
        HashSet<int> changed = [.. recoveredPlotTiles];
        recoveredPlotTiles.Clear();
        int[] activeAtPassStart = Enumerable.Range(1, MaximumBuildings - 1)
            .Where(id => buildings[id * RecordSize] is 1 or 3)
            .ToArray();
        foreach (int id in activeAtPassStart)
        {
            int offset = id * RecordSize;
            int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
            if (buildings[offset] is not (1 or 3) ||
                !((BuildingType)type).IsHousing())
                continue;

            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
            if ((uint)tile >= LegacyCityMapSnapshot.TileCount)
                continue;
            // The first two native level handlers return before the merge
            // probe when the primary house has no residents. Neighbours may
            // still be vacant when an occupied primary initiates the merge.
            if (type <= 19 && (type > 11 || ReadShort(offset + 28) > 0) &&
                TryMergeTwoByTwo(id, changed))
            {
                expanded++;
                updated++;
            }
            int size = Math.Max(1, (int)buildings[offset + 3]);
            if (!LegacyBuildingRoadAccess.HasNearbyRoad(
                    terrain, width, height, origin, tile, size))
            {
                BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + RoadDistanceOffset), 0);
                short failedChecks = ReadShort(offset + UnreachableChecksOffset);
                failedChecks = checked((short)Math.Min(short.MaxValue, failedChecks + 1));
                BinaryPrimitives.WriteInt16LittleEndian(
                    buildings.AsSpan(offset + UnreachableChecksOffset), failedChecks);
                if (failedChecks >= RoadAccessRemovalChecks)
                {
                    RemoveHouse(id, changed);
                    removed++;
                    continue;
                }
            }
            else
            {
                // FUN_00458C30 requires a positive routed-road distance before
                // a displaced family may reserve this house. Until the full
                // Kingdom routing grid is reconstructed, preserve the same
                // eligible/non-eligible distinction with the minimum positive value.
                if (ReadShort(offset + RoadDistanceOffset) <= 0)
                    BinaryPrimitives.WriteInt16LittleEndian(
                        buildings.AsSpan(offset + RoadDistanceOffset), 1);
                BinaryPrimitives.WriteInt16LittleEndian(
                    buildings.AsSpan(offset + UnreachableChecksOffset), 0);
            }

            short population = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 28));
            if (population <= 0)
                continue;
            buildings[offset + 190] = desirability[tile];
            int currentDesirability = unchecked((sbyte)desirability[tile]);
            LegacyHousingLevel current = levels[type - 10];
            int direction = currentDesirability <= current.DevolveDesirability ||
                            !MeetsRequirements(offset, current)
                ? -1
                : type < 29 && currentDesirability >= current.EvolveDesirability &&
                  MeetsRequirements(offset, levels[type - 9])
                    ? 1
                    : 0;
            if (direction == 0)
            {
                buildings[offset + 132] = 0;
                continue;
            }
            if (direction < 0)
            {
                if (buildings[offset + 132]++ <= 1)
                    continue;
                buildings[offset + 132] = 0;
                if (type == (int)BuildingType.Housing)
                    continue;
                if (type is 20 or 24 or 28)
                {
                    SplitFootprint(id, type - 1, changed);
                    footprintChanged = true;
                }
                else
                    ChangeType(id, type - 1, changed);
                updated++;
                continue;
            }

            buildings[offset + 132] = 0;
            int targetType = type + 1;
            int targetSize = targetType switch { 20 => 2, 24 => 3, 28 => 4, _ => 1 };
            if (targetSize > 1)
            {
                if (!TryExpand(id, type, targetType, targetSize, changed))
                    continue;
                expanded++;
            }
            else
            {
                ChangeType(id, targetType, changed);
            }
            updated++;
        }

        if (changed.Count != 0)
            Publish();
        else
            session.ReplaceLogicalChunk("buildings", buildings);
        return new LegacyHousingAdvance(updated, expanded, removed, changed.Order().ToArray())
            { FootprintChanged = footprintChanged };
    }

    [OriginalAddress(0x00457AF0, "FUN_00457af0")]
    [OriginalAddress(0x0047C480, "FUN_0047c480")]
    private void RemoveHouse(int id, HashSet<int> changed)
    {
        int offset = id * RecordSize;
        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
        int size = Math.Max(1, (int)buildings[offset + 3]);
        if (TryLocal(anchor, out int x, out int y))
        {
            for (int yy = y; yy < Math.Min(height, y + size); yy++)
            for (int xx = x; xx < Math.Min(width, x + size); xx++)
            {
                int tile = ToTile(xx, yy);
                short owner = BinaryPrimitives.ReadInt16LittleEndian(buildingGrid.AsSpan(tile * 2));
                if (owner != id)
                    continue;
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * 4));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    terrain.AsSpan(tile * 4), flags & ClearedBuildingTerrainMask);
                BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(tile * 2), 0);
                BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * 4), 0);
                bitfields[tile] &= 0xe0;
                edge[tile] = (byte)((edge[tile] & 0xc0) | 0x40);
                changed.Add(tile);
            }
        }
        buildings.AsSpan(offset, RecordSize).Clear();
    }

    [OriginalAddress(0x00483910, "FUN_00483910")]
    private bool MeetsRequirements(int offset, LegacyHousingLevel level)
    {
        if (level.Water > 0 && buildings[offset + 53] == 0 &&
            (level.Water > 1 || buildings[offset + 63] == 0))
            return false;
        if (unchecked((sbyte)buildings[offset + 128]) < level.Entertainment ||
            unchecked((sbyte)buildings[offset + 131]) < level.Religion ||
            unchecked((sbyte)buildings[offset + 129]) < level.Education ||
            buildings[offset + 117] < level.Dentist ||
            buildings[offset + 112] < level.Physician ||
            unchecked((sbyte)buildings[offset + 130]) < level.Health)
            return false;

        int foodTypes = 0;
        for (int index = 0; index < 8; index++)
        {
            if (BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 84 + index * 2)) > 0)
                foodTypes++;
        }
        return foodTypes >= level.FoodTypes &&
               ReadInventory(offset, 100) >= level.Pottery &&
               ReadInventory(offset, 104) >= level.Linen &&
               ReadInventory(offset, 106) >= level.Jewelry &&
               ReadInventory(offset, 102) >= level.Beer;
    }

    [OriginalAddress(0x0046CEA0, "FUN_0046cea0")]
    private void ChangeType(int id, int targetType, HashSet<int> changed)
    {
        int offset = id * RecordSize;
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 16), checked((short)targetType));
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 18), checked((short)(targetType - 10)));
        int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
        int size = Math.Max(1, (int)buildings[offset + 3]);
        if (!TryLocal(tile, out int x, out int y))
            return;
        int image = GetImageId(
            targetType, ReadShort(offset + 28), buildings[offset + 4] != 0, random[tile]);
        for (int row = 0; row < size; row++)
        for (int column = 0; column < size; column++)
        {
            int footprintTile = ToTile(x + column, y + row);
            if (BinaryPrimitives.ReadInt16LittleEndian(
                    buildingGrid.AsSpan(footprintTile * sizeof(short))) != id)
                continue;
            BinaryPrimitives.WriteInt32LittleEndian(
                images.AsSpan(footprintTile * sizeof(int)), image);
            changed.Add(footprintTile);
        }
        PublishFootprintGeometry(x, y, size);
    }

    [OriginalAddress(0x0046B480, "FUN_0046b480")]
    [OriginalAddress(0x0046B940, "FUN_0046b940")]
    [OriginalAddress(0x0046B9E0, "FUN_0046b9e0")]
    private bool TryExpand(
        int id,
        int currentType,
        int targetType,
        int size,
        HashSet<int> changed)
    {
        int recordOffset = id * RecordSize;
        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(recordOffset + 12));
        if (!TryLocal(anchor, out int anchorX, out int anchorY))
            return false;

        (int X, int Y)? selected = null;
        ReadOnlySpan<(int X, int Y)> candidateOffsets =
        [
            (0, 0),
            (-1, -1),
            (-1, 0),
            (0, -1)
        ];
        for (int pass = 0; pass < 3 && selected is null; pass++)
        foreach ((int dx, int dy) in candidateOffsets)
        {
            int x = anchorX + dx;
            int y = anchorY + dy;
            if (CanConsumeSquare(id, currentType, x, y, size, pass))
            {
                selected = (x, y);
                break;
            }
        }
        if (selected is not { } topLeft)
        {
            buildings[recordOffset + 126] = 1;
            return false;
        }

        HashSet<int> consumedIds = [];
        List<int> footprint = [];
        for (int y = topLeft.Y; y < topLeft.Y + size; y++)
        for (int x = topLeft.X; x < topLeft.X + size; x++)
        {
            int tile = ToTile(x, y);
            footprint.Add(tile);
            short other = BinaryPrimitives.ReadInt16LittleEndian(buildingGrid.AsSpan(tile * 2));
            if (other > 0 && other != id)
                consumedIds.Add(other);
        }

        foreach (int other in consumedIds)
        {
            int otherOffset = other * RecordSize;
            AddShort(recordOffset + 28, ReadShort(otherOffset + 28));
            for (int inventory = 84; inventory < 108; inventory += 2)
                AddShort(recordOffset + inventory, ReadShort(otherOffset + inventory));
            buildings[otherOffset] = 5;
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(otherOffset + 28), 0);
        }

        int newAnchor = ToTile(topLeft.X, topLeft.Y);
        buildings[recordOffset + 3] = checked((byte)size);
        buildings[recordOffset + 4] = 0;
        buildings[recordOffset + 5] = checked((byte)size);
        buildings[recordOffset + 126] = 0;
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(recordOffset + 6), checked((short)topLeft.X));
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(recordOffset + 8), checked((short)topLeft.Y));
        BinaryPrimitives.WriteInt32LittleEndian(buildings.AsSpan(recordOffset + 12), newAnchor);
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(recordOffset + 16), checked((short)targetType));
        BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(recordOffset + 18), checked((short)(targetType - 10)));
        foreach (int tile in footprint)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * 4));
            BinaryPrimitives.WriteUInt32LittleEndian(
                terrain.AsSpan(tile * 4), (flags & BuildingTerrainPreserveMask) | TerrainBuilding);
            BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(tile * 2), checked((short)id));
            changed.Add(tile);
        }
        WriteHouseImage(id, newAnchor);
        int image = BinaryPrimitives.ReadInt32LittleEndian(images.AsSpan(newAnchor * sizeof(int)));
        foreach (int tile in footprint)
            BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)), image);
        PublishFootprintGeometry(topLeft.X, topLeft.Y, size);
        return true;
    }

    /// <summary>
    /// Before normal level evolution, Pharaoh.exe FUN_0046B840 can combine a
    /// top-left house and three equally developed 1x1 neighbours into the
    /// alternate 2x2 artwork. FUN_0046B9E0 aggregates their residents and
    /// inventories, while FUN_0046BDA0 publishes the merged footprint without
    /// changing the house level.
    /// </summary>
    [OriginalAddress(0x0046B840, "FUN_0046b840")]
    [OriginalAddress(0x0046B9E0, "FUN_0046b9e0")]
    [OriginalAddress(0x0046BDA0, "FUN_0046bda0")]
    private bool TryMergeTwoByTwo(int id, HashSet<int> changed)
    {
        int offset = id * RecordSize;
        if (buildings[offset] != 1 || buildings[offset + 3] != 1 ||
            buildings[offset + 4] != 0 || buildings[offset + 5] == 0)
            return false;

        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
        if (!TryLocal(anchor, out int x, out int y) || x + 2 > width || y + 2 > height ||
            (random[anchor] & 7) >= 5)
            return false;

        int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
        HashSet<int> owners = [];
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 2; column++)
        {
            int tile = ToTile(x + column, y + row);
            if ((BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * sizeof(uint))) &
                 TerrainBuilding) == 0)
                return false;
            int owner = BinaryPrimitives.ReadInt16LittleEndian(
                buildingGrid.AsSpan(tile * sizeof(short)));
            if ((uint)owner >= MaximumBuildings || owner == 0)
                return false;
            int ownerOffset = owner * RecordSize;
            if (owner != id &&
                (buildings[ownerOffset] != 1 || buildings[ownerOffset + 3] != 1 ||
                 buildings[ownerOffset + 4] != 0 || buildings[ownerOffset + 5] == 0 ||
                 BinaryPrimitives.ReadInt16LittleEndian(
                     buildings.AsSpan(ownerOffset + 16)) != type))
                return false;
            owners.Add(owner);
        }
        if (owners.Count != 4)
            return false;

        foreach (int owner in owners)
        {
            if (owner == id)
                continue;
            int ownerOffset = owner * RecordSize;
            AddShort(offset + 28, ReadShort(ownerOffset + 28));
            for (int inventory = 84; inventory < 108; inventory += 2)
                AddShort(offset + inventory, ReadShort(ownerOffset + inventory));
            buildings[ownerOffset] = 5;
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(ownerOffset + 28), 0);
        }

        buildings[offset + 3] = 2;
        buildings[offset + 4] = 1;
        buildings[offset + 5] = 2;
        int image = GetImageId(type, ReadShort(offset + 28), merged: true, random[anchor]);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 2; column++)
        {
            int tile = ToTile(x + column, y + row);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                terrain.AsSpan(tile * sizeof(uint)), (flags & BuildingTerrainPreserveMask) | TerrainBuilding);
            BinaryPrimitives.WriteInt16LittleEndian(
                buildingGrid.AsSpan(tile * sizeof(short)), checked((short)id));
            BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * sizeof(int)), image);
            changed.Add(tile);
        }
        PublishFootprintGeometry(x, y, 2);
        return true;
    }

    private bool CanConsumeSquare(int id, int currentType, int x, int y, int size, int pass)
    {
        if (x < 0 || y < 0 || x + size > width || y + size > height)
            return false;
        for (int yy = y; yy < y + size; yy++)
        for (int xx = x; xx < x + size; xx++)
        {
            int tile = ToTile(xx, yy);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(terrain.AsSpan(tile * 4));
            if ((flags & TerrainBuilding) != 0)
            {
                int other = BinaryPrimitives.ReadInt16LittleEndian(buildingGrid.AsSpan(tile * 2));
                if (other == id)
                    continue;
                if ((uint)other >= MaximumBuildings || other == 0)
                    return false;
                int otherOffset = other * RecordSize;
                int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(otherOffset + 16));
                if (buildings[otherOffset] == 0 || buildings[otherOffset] == 5 ||
                    !((BuildingType)type).IsHousing() || type > currentType)
                    return false;
                continue;
            }
            uint blocked = flags & ExpansionBlockingMask;
            if (pass == 0 || (pass == 1 && blocked != 0) ||
                (pass == 2 && blocked != 0 && blocked != Garden))
                return false;
        }
        return true;
    }

    private void SplitFootprint(int id, int targetType, HashSet<int> changed)
    {
        int offset = id * RecordSize;
        int size = buildings[offset + 3];
        int anchor = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
        if (size <= 1 || !TryLocal(anchor, out int x, out int y))
        {
            ChangeType(id, targetType, changed);
            return;
        }
        List<int> ids = [id];
        for (int candidate = 1; candidate < MaximumBuildings && ids.Count < size * size; candidate++)
        {
            if (candidate != id && buildings[candidate * RecordSize] == 0)
                ids.Add(candidate);
        }
        if (ids.Count != size * size)
            return;

        byte[] source = buildings.AsSpan(offset, RecordSize).ToArray();
        int population = BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(28));
        for (int index = 0; index < ids.Count; index++)
        {
            int child = ids[index];
            int childOffset = child * RecordSize;
            source.CopyTo(buildings, childOffset);
            int tileX = x + index % size;
            int tileY = y + index / size;
            int tile = ToTile(tileX, tileY);
            buildings[childOffset] = 1;
            buildings[childOffset + 3] = 1;
            buildings[childOffset + 4] = 0;
            buildings[childOffset + 5] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(childOffset + 6), checked((short)tileX));
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(childOffset + 8), checked((short)tileY));
            BinaryPrimitives.WriteInt32LittleEndian(buildings.AsSpan(childOffset + 12), tile);
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(childOffset + 16), checked((short)targetType));
            BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(childOffset + 18), checked((short)(targetType - 10)));
            BinaryPrimitives.WriteInt16LittleEndian(
                buildings.AsSpan(childOffset + 28), checked((short)(population / ids.Count + (index < population % ids.Count ? 1 : 0))));
            for (int inventory = 84; inventory < 108; inventory += 2)
            {
                int amount = BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(inventory));
                BinaryPrimitives.WriteInt16LittleEndian(
                    buildings.AsSpan(childOffset + inventory),
                    checked((short)(amount / ids.Count + (index < amount % ids.Count ? 1 : 0))));
            }
            BinaryPrimitives.WriteInt16LittleEndian(buildingGrid.AsSpan(tile * 2), checked((short)child));
            bitfields[tile] &= 0xe0;
            edge[tile] = 0x40;
            WriteHouseImage(child, tile);
            changed.Add(tile);
        }
    }

    /// <summary>
    /// Reproduces the footprint metadata written by Pharaoh.exe FUN_0047BF50:
    /// the low bitfield nibble stores size-1, edge values store x+y*8 within
    /// the footprint, and only the orientation-dependent draw corner receives
    /// bit 0x40.
    /// </summary>
    [OriginalAddress(0x0047BF50, "FUN_0047bf50")]
    private void PublishFootprintGeometry(int x, int y, int size)
    {
        int orientation = 0;
        if (session.LogicalChunks.TryGetValue(
                "city_view_orientation", out ReadOnlyMemory<byte> orientationChunk) &&
            orientationChunk.Length >= sizeof(int))
        {
            int serialized = BinaryPrimitives.ReadInt32LittleEndian(orientationChunk.Span);
            if (serialized is 0 or 2 or 4 or 6)
                orientation = serialized;
        }

        int drawX = orientation is 0 or 2 ? 0 : size - 1;
        int drawY = orientation is 2 or 4 ? 0 : size - 1;
        for (int row = 0; row < size; row++)
        for (int column = 0; column < size; column++)
        {
            int tile = ToTile(x + column, y + row);
            bitfields[tile] = (byte)((bitfields[tile] & 0xe0) | (size - 1));
            edge[tile] = (byte)(column + row * 8);
            if (column == drawX && row == drawY)
                edge[tile] |= 0x40;
        }
    }

    private void WriteHouseImage(int id, int tile)
    {
        int offset = id * RecordSize;
        int type = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16));
        int population = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 28));
        int image = GetImageId(type, population, buildings[offset + 4] != 0, random[tile]);
        BinaryPrimitives.WriteInt32LittleEndian(images.AsSpan(tile * 4), image);
    }

    public static int GetImageId(int type, int population, bool merged, byte randomValue)
    {
        if (type == (int)BuildingType.Housing && population <= 0)
            return LegacyHousingPlacement.ForSaleImageId;
        if (!((BuildingType)type).IsHousing())
            throw new ArgumentOutOfRangeException(nameof(type));
        int level = type - 10;
        int local = LocalGroupStarts[level / 2] + ((level & 1) * 2);
        if (merged && level < 10)
            local = LocalGroupStarts[level / 2] + 4 + (level & 1);
        else if (level < 14)
            local += randomValue & 1;
        return 11706 + local;
    }

    private short ReadInventory(int recordOffset, int fieldOffset) => ReadShort(recordOffset + fieldOffset);
    private short ReadShort(int offset) => BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset));
    private void AddShort(int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(
        buildings.AsSpan(offset), checked((short)Math.Clamp(ReadShort(offset) + value, 0, short.MaxValue)));
    private int ToTile(int x, int y) => origin + y * LegacyCityMapSnapshot.MapLength + x;

    private bool TryLocal(int tile, out int x, out int y)
    {
        int relative = tile - origin;
        x = relative % LegacyCityMapSnapshot.MapLength;
        y = relative / LegacyCityMapSnapshot.MapLength;
        return relative >= 0 && x >= 0 && x < width && y >= 0 && y < height;
    }

    private void Publish()
    {
        session.ReplaceLogicalChunk("terrain_grid", terrain);
        session.ReplaceLogicalChunk("building_grid", buildingGrid);
        session.ReplaceLogicalChunk("buildings", buildings);
        session.ReplaceLogicalChunk("image_grid", images);
        session.ReplaceLogicalChunk("bitfields_grid", bitfields);
        session.ReplaceLogicalChunk("edge_grid", edge);
    }

    private void RefreshMutableChunks()
    {
        terrain = Required(session, "terrain_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        buildingGrid = Required(session, "building_grid", LegacyCityMapSnapshot.TileCount * sizeof(short)).ToArray();
        buildings = Required(session, "buildings", MaximumBuildings * RecordSize).ToArray();
        images = Required(session, "image_grid", LegacyCityMapSnapshot.ImageGridLength).ToArray();
        bitfields = Required(session, "bitfields_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        edge = Required(session, "edge_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        desirability = Required(session, "desirability_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        random = Required(session, "random_grid", LegacyCityMapSnapshot.TileCount).ToArray();
    }

    private static ReadOnlyMemory<byte> Required(
        LegacyMissionSession session,
        string name,
        int length,
        bool exact = true)
    {
        if (!session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> data) ||
            (exact ? data.Length != length : data.Length < length))
            throw new InvalidDataException($"Logical chunk '{name}' has an invalid length.");
        return data;
    }
}
