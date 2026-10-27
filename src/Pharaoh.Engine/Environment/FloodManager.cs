using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;

namespace Pharaoh.Engine.Environment;

public enum LegacyFloodState
{
    Imminent = 1,
    Flooding = 2,
    Inundated = 3,
    Contracting = 4,
    Resting = 5,
    Farmable = 6
}

public readonly record struct LegacyFloodSettings(
    int InitialSeason, int InitialDuration, int InitialQuality,
    int Season, int Duration, int CurrentQuality, int Unknown,
    int NextQuality, int LastQuality);

public readonly record struct LegacyFloodAdvance(
    LegacyFloodState State,
    int ProgressRow,
    IReadOnlyList<int> ChangedTiles,
    IReadOnlyList<int> FloodedTiles,
    IReadOnlyList<int> RecededTiles)
{
    public bool TerrainChanged => ChangedTiles.Count != 0;
}

/// <summary>
/// Terrain-facing reconstruction of Pharaoh's annual Nile cycle. Building,
/// walker and message callbacks remain owned by their respective subsystems;
/// this class reproduces the flood ordering, water and submerged-road flags.
/// </summary>
public sealed class FloodManager
{
    public const uint TerrainWater = 0x00000004;
    public const uint TerrainRoad = 0x00000040;
    public const uint TerrainFloodplain = 0x00010000;
    public const uint TerrainSubmergedRoad = 0x08000000;
    private const uint TerrainFloodClearedOnRise = 0x00001000;
    private const uint TerrainDeepWater = 0x04000000;
    public const int FloodRowCount = 30;
    public const int FloodSubcycleTicks = 25;

    private readonly LegacyMissionSession session;
    private readonly List<int>[] rows = Enumerable.Range(0, FloodRowCount)
        .Select(_ => new List<int>()).ToArray();
    private readonly byte[] growthByRow = new byte[FloodRowCount];
    private readonly int[] rowCursors = Enumerable.Repeat(-1, FloodRowCount).ToArray();
    private LegacyFloodplainTopology topology = null!;
    private readonly Memory<byte> terrainGrid;
    private readonly int[] tileRows = Enumerable.Repeat(-1, LegacyCityMapSnapshot.TileCount).ToArray();
    private readonly byte[] random;
    private readonly int width;
    private readonly int height;
    private readonly int origin;
    private long absoluteTick;
    private int lastCycle;
    private int lastGrowthTemporalIndex = -1;

    private FloodManager(LegacyMissionSession session)
    {
        this.session = session;
        terrainGrid = session.GetWritableLogicalChunk("terrain_grid");
        Settings = ReadSettings(session.LogicalChunks);

        ReadOnlySpan<byte> scenario = Required(session.LogicalChunks, "scenario_info", 60).Span;
        width = BinaryPrimitives.ReadInt32LittleEndian(scenario[44..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(scenario[48..]);
        origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        if (width is <= 0 or > LegacyCityMapSnapshot.MapLength ||
            height is <= 0 or > LegacyCityMapSnapshot.MapLength || origin < 0)
            throw new InvalidDataException("Invalid floodplain map geometry.");

        random = Required(session.LogicalChunks, "random_grid", LegacyCityMapSnapshot.TileCount).ToArray();
        if (session.LogicalChunks.TryGetValue("game_time", out ReadOnlyMemory<byte> savedTime) &&
            savedTime.Length >= 20)
        {
            ReadOnlySpan<byte> gameTime = savedTime.Span;
            int tickWithinDay = BinaryPrimitives.ReadInt32LittleEndian(gameTime);
            int totalDays = BinaryPrimitives.ReadInt32LittleEndian(gameTime[16..]);
            absoluteTick = checked((long)totalDays * LegacySimulationClock.DispatcherTicksPerDay + tickWithinDay);
        }
        RebuildRows();
        State = ResolveState(absoluteTick, out int progress);
        ProgressRow = progress;
        lastCycle = CurrentCycle(absoluteTick);
    }

    public LegacyFloodSettings Settings { get; private set; }
    public LegacyFloodState State { get; private set; }
    public int ProgressRow { get; private set; }
    public int FloodplainWidth { get; private set; }
    public long AbsoluteTick => absoluteTick;

    public int GetFloodRow(int tile) => (uint)tile < tileRows.Length ? tileRows[tile] : -1;
    public IReadOnlyList<int> GetFloodRowTiles(int row) => rows[row];

    [OriginalAddress(0x00479E70, "FUN_00479e70")]
    public int GetGrowthStage(int tile)
    {
        int row = GetFloodRow(tile);
        return row < 0 ? 0 : growthByRow[row];
    }

    [OriginalAddress(0x004BE0F0, "FUN_004be0f0")]
    [OriginalAddress(0x004BE160, "FUN_004be160")]
    public static FloodManager Create(LegacyMissionSession session) =>
        new(session ?? throw new ArgumentNullException(nameof(session)));

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    public LegacyFloodAdvance Advance(long legacyTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyTicks);
        HashSet<int> changed = [];
        HashSet<int> flooded = [];
        HashSet<int> receded = [];
        for (long i = 0; i < legacyTicks; i++)
        {
            absoluteTick++;
            if (absoluteTick % LegacySimulationClock.DispatcherTicksPerDay == 1 &&
                State != LegacyFloodState.Flooding)
                ApproachPredictedQuality();

            int cycle = CurrentCycle(absoluteTick);
            if (cycle != lastCycle)
            {
                int start = StartCycle();
                if (cycle == start - 1)
                    PrepareNextFlood();
                // FUN_004BE770 clears field +4 of all thirty row records at
                // start + floodplain width. Until this boundary the old
                // vegetation remains under the advancing water and WATER's
                // later render pass hides it immediately.
                if (cycle == start + FloodplainWidth)
                    ResetFloodplainGrowth(changed);
                lastCycle = cycle;
            }
            LegacyFloodState nextState = ResolveState(absoluteTick, out int progress);
            State = nextState;
            ProgressRow = progress;
            AdvanceFloodplainGrowth(changed, receded);
            if (nextState == LegacyFloodState.Flooding && progress >= 0)
                UpdateRow(progress, flooding: true, changed, flooded, receded);
            else if (nextState == LegacyFloodState.Contracting && progress >= 0)
                UpdateRow(progress, flooding: false, changed, flooded, receded);
        }
        return new LegacyFloodAdvance(
            State, ProgressRow, changed.Order().ToArray(),
            flooded.Order().ToArray(), receded.Order().ToArray());
    }

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private LegacyFloodState ResolveState(long tick, out int progress)
    {
        int cycle = CurrentCycle(tick);
        int start = StartCycle(tick);
        int inundationLength = Math.Clamp(Settings.LastQuality * FloodplainWidth / 100, 0, FloodRowCount);
        int fullStart = start + inundationLength;
        int end = start + Settings.Duration + 2 * FloodplainWidth;
        int contractionStart = end - inundationLength;

        progress = FloodRowCount;
        if (cycle >= start && cycle < fullStart)
        {
            progress = Math.Clamp(29 - (cycle - start), 0, 29);
            return LegacyFloodState.Flooding;
        }
        if (cycle >= fullStart && cycle < contractionStart)
        {
            progress = Math.Clamp(29 - inundationLength, 0, 30);
            return LegacyFloodState.Inundated;
        }
        if (cycle >= contractionStart && cycle < end)
        {
            progress = Math.Clamp(30 - (end - cycle), 0, 29);
            return LegacyFloodState.Contracting;
        }
        if (cycle < start && cycle >= start - 28)
            return LegacyFloodState.Imminent;
        if (cycle >= end && cycle < end + 28)
            return LegacyFloodState.Resting;
        return LegacyFloodState.Farmable;
    }

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private int CurrentCycle(long tick)
        => checked((int)((tick + 1) / FloodSubcycleTicks));

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private int StartCycle() => StartCycle(absoluteTick);

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private int StartCycle(long tick)
    {
        double cyclesPerYear = LegacySimulationClock.LegacyTicksPerYear / (double)FloodSubcycleTicks;
        long year = tick / LegacySimulationClock.LegacyTicksPerYear;
        int start = Settings.Season * 105 / 100 + checked((int)(year * cyclesPerYear)) + 15;
        if (start - CurrentCycle(tick) > cyclesPerYear * 0.5)
            start -= (int)cyclesPerYear;
        return start;
    }

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private void ApproachPredictedQuality()
    {
        int current = Settings.CurrentQuality;
        int target = Settings.NextQuality;
        if (current < target)
            current = Math.Min(target, current + 5);
        else if (current > target)
            current = Math.Max(target, current - 5);
        if (current != Settings.CurrentQuality)
        {
            Settings = Settings with { CurrentQuality = current };
            WriteSettings();
        }
    }

    [OriginalAddress(0x004BD550, "FUN_004bd550")]
    private void PrepareNextFlood()
    {
        int next = Math.Clamp(Settings.InitialQuality + NextQualityDelta(), 0, 100);
        Settings = Settings with
        {
            Season = Settings.InitialSeason,
            Duration = Settings.InitialDuration,
            NextQuality = next,
            LastQuality = Math.Clamp(Settings.CurrentQuality, 0, 100)
        };
        WriteSettings();
    }

    [OriginalAddress(0x004BD380, "FUN_004bd380")]
    private int NextQualityDelta()
    {
        if (!session.LogicalChunks.TryGetValue("random_iv", out ReadOnlyMemory<byte> memory) ||
            memory.Length < 8)
            return 0;
        byte[] state = memory.ToArray();
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(state);
        uint second = BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4));
        Pharaoh.Engine.Population.LegacyPopulationAges.RecordPreviousRandom(session, first);
        for (int i = 0; i < 31; i++)
        {
            first = ShiftRandomIv(first);
            second = ShiftRandomIv(second);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(state, first);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(4), second);
        session.ReplaceLogicalChunk("random_iv", state);

        int roll = (int)(second & 0x7fff) % 99 + 1;
        int[] weights = [2, 3, 5, 10, 15, 30, 15, 10, 5, 3, 2];
        int[] deltas = [100, 80, 60, 40, 20, 0, -20, -40, -60, -80, -100];
        int cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return deltas[i];
        }
        return deltas[^1];
    }

    [OriginalAddress(0x004BD380, "FUN_004bd380")]
    private static uint ShiftRandomIv(uint value)
    {
        uint feedback = ((value >> 4) & 1) ^ (value & 1);
        value >>= 1;
        return feedback == 0 ? value : value | 0x40000000;
    }

    [RewriteOnly("Publishes reconstructed runtime flood settings through the mission chunk boundary")]
    private void WriteSettings()
    {
        byte[] bytes = session.LogicalChunks["floodplain_settings"].ToArray();
        int[] values =
        [
            Settings.InitialSeason, Settings.InitialDuration, Settings.InitialQuality,
            Settings.Season, Settings.Duration, Settings.CurrentQuality, Settings.Unknown,
            Settings.NextQuality, Settings.LastQuality
        ];
        for (int i = 0; i < Math.Min(values.Length, bytes.Length / sizeof(int)); i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), values[i]);
        session.ReplaceLogicalChunk("floodplain_settings", bytes);
    }

    [OriginalAddress(0x004BD850, "FUN_004bd850")]
    private void UpdateRow(
        int row,
        bool flooding,
        HashSet<int> changed,
        HashSet<int> flooded,
        HashSet<int> receded)
    {
        List<int> candidates = rows[row];
        if (candidates.Count == 0)
            return;

        int percentage = checked((int)((absoluteTick + 1) % FloodSubcycleTicks)) * 100 / FloodSubcycleTicks;
        int first = rowCursors[row];
        if (first < 0)
            first = percentage * candidates.Count / 100;
        int count = candidates.Count / FloodSubcycleTicks + 1;
        Span<byte> terrain = terrainGrid.Span;
        for (int index = first; index < Math.Min(candidates.Count, first + count); index++)
        {
            int tile = candidates[index];
            if (!topology.IsFloodable(tile))
                continue;
            Span<byte> bytes = terrain.Slice(tile * sizeof(uint), sizeof(uint));
            uint before = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            uint after = flooding ? FloodTile(before) : UnfloodTile(before);
            if (after == before)
                continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, after);
            changed.Add(tile);
            (flooding ? flooded : receded).Add(tile);
        }
        int next = Math.Min(candidates.Count, first + count);
        rowCursors[row] = next == candidates.Count ? -1 : next;
    }

    [OriginalAddress(0x004BE0D0, "FUN_004be0d0")]
    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private void AdvanceFloodplainGrowth(HashSet<int> changed, HashSet<int> receded)
    {
        int subcycle = checked((int)((absoluteTick + 1) % FloodSubcycleTicks));
        int cycle = CurrentCycle(absoluteTick);
        int end = StartCycle() + Settings.Duration + 2 * FloodplainWidth;
        int growthStart = end - FloodplainWidth;
        if (cycle < growthStart || FloodplainWidth == 0)
        {
            lastGrowthTemporalIndex = -1;
            return;
        }

        // 0x004BEA65 loads 25.0 * (1/6), divides the subcycle by it and
        // truncates: six vegetation phases per 25-tick flood cycle.
        int phase = subcycle * 6 / FloodSubcycleTicks;
        int temporalIndex = (cycle - growthStart) * 6 + phase;
        temporalIndex = Math.Min(temporalIndex, FloodplainWidth * 60);
        if (temporalIndex >= FloodplainWidth * 60)
            return;
        if (temporalIndex == lastGrowthTemporalIndex)
            return;
        lastGrowthTemporalIndex = temporalIndex;

        int recedingFront = ProgressRow;
        int offsetBehindWater = temporalIndex % FloodplainWidth;
        if (offsetBehindWater >= FloodplainWidth - 1)
            offsetBehindWater = FloodplainWidth - 1;
        int row = recedingFront - offsetBehindWater;

        // The executable uses row < current flood front, so a row cannot
        // start growing while FUN_004BD850 is still revealing its cells.
        if (row < 0 || row >= recedingFront || row >= FloodRowCount ||
            rows[row].Count == 0 || growthByRow[row] >= 5)
            return;

        growthByRow[row]++;
        // FUN_004BE0D0 invokes FUN_004BD850(0, 1, 1): growth also
        // drains residual water in the selected row before re-submitting it.
        int first = rowCursors[row] < 0 ? rows[row].Count / 100 : rowCursors[row];
        Span<byte> terrain = terrainGrid.Span;
        foreach (int tile in rows[row].Skip(first))
        {
            if (!topology.IsFloodable(tile))
                continue;
            Span<byte> bytes = terrain.Slice(tile * sizeof(uint), sizeof(uint));
            uint before = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            uint after = UnfloodTile(before);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, after);
            if (before != after)
                receded.Add(tile);
            changed.Add(tile);
        }
        rowCursors[row] = -1;
    }

    [OriginalAddress(0x004BE770, "FUN_004be770")]
    private void ResetFloodplainGrowth(HashSet<int> changed)
    {
        for (int row = 0; row < FloodRowCount; row++)
        {
            if (growthByRow[row] == 0)
                continue;
            growthByRow[row] = 0;
            foreach (int tile in rows[row])
                changed.Add(tile);
        }
        lastGrowthTemporalIndex = -1;
    }

    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    private static uint FloodTile(uint terrain)
    {
        terrain = (terrain & ~TerrainFloodClearedOnRise) | TerrainWater;
        if ((terrain & TerrainRoad) != 0)
            terrain = (terrain & ~TerrainRoad) | TerrainSubmergedRoad;
        return terrain;
    }

    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    private static uint UnfloodTile(uint terrain)
    {
        terrain &= ~(TerrainWater | TerrainDeepWater);
        if ((terrain & TerrainSubmergedRoad) != 0)
            terrain = (terrain & ~TerrainSubmergedRoad) | TerrainRoad;
        return terrain;
    }

    /// <summary>
    /// Returns the complete image-rebuild footprint used after a flood cell
    /// changes. The original invokes terrain passes over nested one-, two-,
    /// and three-cell rectangles. Rising water uses the clipped radius-two
    /// union; receding water additionally invokes the radius-three passes.
    /// </summary>
    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    public static IEnumerable<int> EnumerateTerrainRefreshTiles(int tile, int radius)
    {
        if ((uint)tile >= LegacyCityMapSnapshot.TileCount)
            throw new ArgumentOutOfRangeException(nameof(tile));
        if (radius is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(radius));
        int x = tile % LegacyCityMapSnapshot.MapLength;
        int y = tile / LegacyCityMapSnapshot.MapLength;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = x + dx;
            int ny = y + dy;
            if ((uint)nx < LegacyCityMapSnapshot.MapLength &&
                (uint)ny < LegacyCityMapSnapshot.MapLength)
                yield return ny * LegacyCityMapSnapshot.MapLength + nx;
        }
    }

    [OriginalAddress(0x004BE160, "FUN_004be160")]
    [OriginalAddress(0x004BD740, "FUN_004bd740")]
    private void RebuildRows()
    {
        topology = LegacyFloodplainTopology.Build(terrainGrid.Span, random, width, height, origin);
        FloodplainWidth = topology.Width;
        for (int row = 0; row < FloodRowCount; row++)
        foreach (int tile in topology.GetNodes(row))
        {
            rows[row].Add(tile);
            if (topology.IsFloodable(tile))
                tileRows[tile] = row;
        }
    }

    [RewriteOnly("Decodes the versioned flood settings logical chunk")]
    private static LegacyFloodSettings ReadSettings(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks)
    {
        ReadOnlySpan<byte> bytes = Required(chunks, "floodplain_settings", 32).Span;
        int[] values = new int[9];
        for (int i = 0; i < Math.Min(values.Length, bytes.Length / sizeof(int)); i++)
            values[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * sizeof(int))..]);
        if (bytes.Length < 36)
            values[8] = values[5];
        return new(values[0], values[1], values[2], values[3], values[4],
            values[5], values[6], values[7], values[8]);
    }

    private static ReadOnlyMemory<byte> Required(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> chunks, string name, int minimumLength)
    {
        if (!chunks.TryGetValue(name, out ReadOnlyMemory<byte> chunk) || chunk.Length < minimumLength)
            throw new InvalidDataException($"Logical chunk '{name}' is missing or too short.");
        return chunk;
    }
}
