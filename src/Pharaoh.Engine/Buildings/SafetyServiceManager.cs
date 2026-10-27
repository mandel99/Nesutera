using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Engine.Buildings;

/// <summary>Employment and walker spawning for the three city safety services.</summary>
public sealed class SafetyServiceManager
{
    private const int RecordSize = 264;
    private const int MaximumBuildings = 4000;
    private const int DispatcherPeriod = 51;
    private readonly LegacyMissionSession session;
    private readonly LegacyBuildingModelCosts? models;
    private readonly int difficulty;
    private long ticks;

    private SafetyServiceManager(
        LegacyMissionSession session,
        LegacyBuildingModelCosts? models,
        int difficulty)
    {
        this.session = session;
        this.models = models;
        this.difficulty = difficulty;
    }

    public static SafetyServiceManager Create(
        LegacyMissionSession session,
        LegacyBuildingModelCosts? models = null,
        int difficulty = 2) => new(session, models, difficulty);

    [OriginalAddress(0x00462180, "staffing-dependent service dispatch delays0/1/3/7/15")]
    public IReadOnlyList<int> Advance(long legacyTicks, Pharaoh.Engine.Walkers.Walkers walkers)
    {
        HashSet<int> changed=[];
        for(long i=0;i<legacyTicks;i++)if(++ticks%51==31)
            foreach(int tile in Dispatch(walkers))changed.Add(tile);
        return changed.Order().ToArray();
    }

    [OriginalAddress(0x00462180,"phase31 workplace service dispatch")]
    private IReadOnlyList<int> Dispatch(Pharaoh.Engine.Walkers.Walkers walkers)
    {
        byte[] buildings = session.LogicalChunks["buildings"].ToArray();
        ReadOnlySpan<byte> terrain = session.LogicalChunks["terrain_grid"].Span;
        ReadOnlySpan<byte> scenario = session.LogicalChunks["scenario_info"].Span;
        int width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        int origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        byte[] roadNetworkPriorities = LegacyBuildingRoadAccess.BuildRoadNetworkPriorities(
            terrain, width, height, origin);
        List<int> changed = [];
        bool recordsChanged = false;

        for (int id = 1; id < MaximumBuildings; id++)
        {
            int offset = id * RecordSize;
            if (buildings[offset] != 1 ||
                !LegacySafetyServicePlacement.TryGetDefinition(
                    (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 16)),
                    out LegacySafetyServiceDefinition definition))
                continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(buildings.AsSpan(offset + 12));
            bool road = LegacyBuildingRoadAccess.TryFindNearbyRoad(
                terrain, roadNetworkPriorities, width, height, origin,
                tile, Math.Max(1, (int)buildings[offset + 3]), out int roadTile);
            byte roadAccess = road ? (byte)1 : (byte)0;
            if (buildings[offset + 68] != roadAccess)
            {
                buildings[offset + 68] = roadAccess;
                recordsChanged = true;
            }
            if (road)
            {
                short roadX = checked((short)(roadTile % LegacyCityMapSnapshot.MapLength -
                    origin % LegacyCityMapSnapshot.MapLength));
                short roadY = checked((short)(roadTile / LegacyCityMapSnapshot.MapLength -
                    origin / LegacyCityMapSnapshot.MapLength));
                if (BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 38)) != roadX ||
                    BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 40)) != roadY)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 38), roadX);
                    BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 40), roadY);
                    recordsChanged = true;
                }
            }
            int workers = BinaryPrimitives.ReadInt16LittleEndian(buildings.AsSpan(offset + 64));
            if (!road)
                continue;
            int requiredWorkers = models?.GetModel(difficulty, definition.Type)?.Employees
                ?? definition.RequiredWorkers;
            if (workers <= 0 || requiredWorkers <= 0 || walkers.Active.Any(w => w.HomeBuildingId == id && w.Type == definition.ServiceWalkerType)) continue;
            int percent = workers * 100 / requiredWorkers;
            int delay = percent >= 100 ? 0 : percent >= 75 ? 1 : percent >= 50 ? 3 : percent >= 25 ? 7 : 15;
            buildings[offset + 50] = unchecked((byte)(buildings[offset + 50] + 1));
            recordsChanged = true;
            if (buildings[offset + 50] <= delay) continue;
            if (walkers.SpawnSafetyWalker(id, definition.ServiceWalkerType))
            {
                buildings[offset + 50] = 0;
                int walkerId = walkers.Active.First(w => w.HomeBuildingId == id && w.Type == definition.ServiceWalkerType).Id;
                BinaryPrimitives.WriteInt16LittleEndian(buildings.AsSpan(offset + 42),checked((short)walkerId));
                recordsChanged = true;
                changed.Add(tile);
            }
        }
        if (recordsChanged)
            session.ReplaceLogicalChunk("buildings", buildings);
        return changed;
    }
}
