using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Population;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Engine.Buildings;

public readonly record struct LegacyBuildingRiskAdvance(bool FireStarted, bool BuildingCollapsed, IReadOnlyList<int> ChangedTiles);

/// <summary>Native dispatcher phases40 (cleanup),43 (burning) and44 (risk).</summary>
public sealed class BuildingRiskManager
{
    private readonly LegacyMissionSession session;
    private readonly LegacyBuildingModelCosts models;
    private int difficulty;
    private readonly int wind;
    private static readonly (int X, int Y)[] Neighbors = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    private BuildingRiskManager(LegacyMissionSession session, LegacyBuildingModelCosts models, int difficulty, int wind)
    { this.session = session; this.models = models; this.difficulty = difficulty; this.wind = wind & 7; }

    [OriginalAddress(0x00455530, "FUN_00455530")]
    public static BuildingRiskManager Create(LegacyMissionSession session, LegacyBuildingModelCosts models, int difficulty, int? windDirection = null)
        => new(session, models, difficulty, windDirection ?? Random.Shared.Next(8));

    [OriginalAddress(0x00540C90, "Difficulty changes reload the active model table")]
    public void SetDifficulty(int value)
    {if((uint)value>4)throw new ArgumentOutOfRangeException(nameof(value));difficulty=value;}

    [OriginalAddress(0x00451C50, "FUN_00451c50")]
    public LegacyBuildingRiskAdvance Advance(long firstTick, long legacyTicks)
    {
        HashSet<int> changed = []; bool fire = false, collapse = false;
        for (long tick = firstTick + 1; tick <= firstTick + legacyTicks; tick++)
        {
            int phase = (int)(tick % 51);
            if (phase == 40) LegacyBuildingDisasters.Cleanup(session);
            if (phase == 43) Burn(changed);
            if (phase == 44) Risk(changed, ref fire, ref collapse);
        }
        return new(fire, collapse, changed.Order().ToArray());
    }

    [OriginalAddress(0x00468B70, "FUN_00468b70")]
    private void Risk(HashSet<int> changed, ref bool fire, ref bool collapse)
    {
        int group = NextRandom() & 7;
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        ReadOnlySpan<byte> random = session.LogicalChunks["random_grid"].Span, terrain = session.LogicalChunks["terrain_grid"].Span;
        for (int id = 1; id < 4000; id++)
        {
            Span<byte> b = buildings.Slice(id * 264, 264); int type = Read(b, 16);
            if (b[0] != 1 || type is 99 or 73 || Read(b, 74) != 0 ||
                (type is 32 or 226 or 210 or >=65 and <=69) && Read(b,56)!=0 || LegacyBuildingDisasters.Root(buildings, id) != id) continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]); if ((uint)tile >= LegacyCityMapSnapshot.TileCount) continue;
            var increments = models.GetRiskIncrements(difficulty, (BuildingType)type);
            int damage = Read(b, 70) + increments.Damage;
            if ((b[5] != 0 && Read(b, 18) < 2) || (BinaryPrimitives.ReadUInt32LittleEndian(terrain[(tile * 4)..]) & 0x10000) != 0) damage = 0;
            Write(b, 70, damage);
            if (damage >= 1000)
            { foreach (int cell in LegacyBuildingDisasters.Destroy(session, id, false)) changed.Add(cell); collapse = true; continue; }
            int multiplier = NextRandom() % 5 + 1;
            if (((random[tile] + id) & 7) != group) continue;
            int risk = Read(b, 72) + increments.Fire * multiplier;
            if (b[5] == 0)
            { if (increments.Fire == 0) risk = 0; else if (Climate == 2) risk += 3; }
            else if (Read(b, 28) < 1) risk = 0;
            Write(b, 72, risk);
            if (risk >= 1000)
            { Write(b, 74, 1); foreach (int cell in LegacyBuildingDisasters.Destroy(session, id, true)) changed.Add(cell); fire = true; }
        }
    }

    [OriginalAddress(0x00469730, "FUN_00469730")]
    private void Burn(HashSet<int> changed)
    {
        Span<byte> buildings = session.GetWritableLogicalChunk("buildings").Span;
        Span<byte> list = session.GetWritableLogicalChunk("building_list_burning").Span;
        int count = 0, total = 0;
        ReadOnlySpan<byte> grid = session.LogicalChunks["building_grid"].Span;
        for (int id = 1; id < 4000; id++)
        {
            Span<byte> b = buildings.Slice(id * 264, 264); if (b[0] != 1 || Read(b, 16) != 99) continue;
            int age = Math.Max(0, (int)Read(b, 74)) + 1; Write(b, 74, age);
            if (age > 32) { LegacyBuildingDisasters.FinishBurning(session, id, changed); continue; }
            if (b[189] != 0) continue;
            Write(list, count * 2, id); count = Math.Min(499, count + 1); total++;
            if (age % (Climate == 2 ? 4 : 8) != 0 || (b[77] & 3) != (NextRandom() & 3)) continue;
            int tile = BinaryPrimitives.ReadInt32LittleEndian(b[12..]);
            foreach (int direction in new[] { wind, (wind + 7) & 7, (wind + 1) & 7 })
            {
                var delta = Neighbors[direction]; int x = tile % 228 + delta.X, y = tile / 228 + delta.Y;
                if ((uint)x >= 228 || (uint)y >= 228) continue;
                int target = Read(grid, (y * 228 + x) * 2); if (target <= 0 || target >= 4000) continue;
                ReadOnlySpan<byte> other = buildings.Slice(target * 264, 264);
                if (other[0] != 1 || other[76] != 0 || !CanSpreadTo(Read(other, 16))) continue;
                foreach (int cell in LegacyBuildingDisasters.Destroy(session, target, true)) changed.Add(cell);
                break;
            }
        }
        Span<byte> info = session.GetWritableLogicalChunk("building_burning_list_info").Span;
        BinaryPrimitives.WriteInt32LittleEndian(info, total); BinaryPrimitives.WriteInt32LittleEndian(info[4..], 0);
    }

    [OriginalAddress(0x004696B0, "FUN_004696b0")]
    private static bool CanSpreadTo(int type) => type is not (183 or 207 or 210 or 213 or 215 or 218 or 219 or 220 or 222 or 225 or 227 or 228 or 229 or 230 or 234 or 235 or 236);

    [OriginalAddress(0x004BB3F0, "FUN_004bb3f0")]
    private int NextRandom()
    {
        Span<byte> random = session.GetWritableLogicalChunk("random_iv").Span;
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(random), b = BinaryPrimitives.ReadUInt32LittleEndian(random[4..]);
        // SAV descriptors store D3B384 before D3B380, opposite their memory order.
        LegacyPopulationAges.RecordPreviousRandom(session, a);
        for (int i = 0; i < 31; i++) { a = (a >> 1) | (((a ^ (a >> 4)) & 1) << 30); b = (b >> 1) | (((b ^ (b >> 4)) & 1) << 30); }
        BinaryPrimitives.WriteUInt32LittleEndian(random, a); BinaryPrimitives.WriteUInt32LittleEndian(random[4..], b);
        return (int)(a & 0x7fff);
    }

    private int Climate => session.LogicalChunks["scenario_info"].Span[0x3d8];
    private static short Read(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadInt16LittleEndian(b[offset..]);
    private static void Write(Span<byte> b, int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(b[offset..], (short)Math.Clamp(value, short.MinValue, short.MaxValue));
}
