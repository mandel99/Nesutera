using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Housing;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Desktop.Legacy.UI;

internal enum LegacyDebugOverlayMode
{
    None,
    Walker,
    Network,
    Evolution,
    Building
}

/// <summary>Presentation for the four dormant city diagnostics retained in Pharaoh.exe.</summary>
internal static class LegacyDebugOverlay
{
    private const uint TerrainRoad = 0x00000040;

    [OriginalAddress(0x00536120, "FUN_00536120 - Figure info (F1); exposed as Walker info")]
    [OriginalAddress(0x00536620, "FUN_00536620 - Network info (F2)")]
    [OriginalAddress(0x00534DF0, "FUN_00534df0 - Evolve info (F3)")]
    [OriginalAddress(0x00535080, "FUN_00535080 - Building info (F4)")]
    public static void Draw(
        Graphics graphics,
        LegacyBitmapFont font,
        LegacyDebugOverlayMode mode,
        LegacyMissionSession session,
        CitySimulation simulation,
        LegacyCityHudState hud,
        int? hoverTile,
        long totalLegacyTicks,
        int monthIndex,
        int year,
        int gameSpeed,
        bool paused,
        Rectangle mapBounds)
    {
        if (mode == LegacyDebugOverlayMode.None)
            return;

        List<string> lines = mode switch
        {
            LegacyDebugOverlayMode.Walker => WalkerLines(session, simulation.Walkers, hoverTile),
            LegacyDebugOverlayMode.Network => NetworkLines(session, hoverTile),
            LegacyDebugOverlayMode.Evolution => EvolutionLines(
                session, simulation, hud, totalLegacyTicks, monthIndex, year, gameSpeed, paused),
            LegacyDebugOverlayMode.Building => BuildingLines(session, hoverTile),
            _ => []
        };

        string title = mode switch
        {
            LegacyDebugOverlayMode.Walker => "Walker info (F1)",
            LegacyDebugOverlayMode.Network => "Network info (F2)",
            LegacyDebugOverlayMode.Evolution => "Evolve info (F3)",
            LegacyDebugOverlayMode.Building => "Building info (F4)",
            _ => string.Empty
        };

        int width = Math.Max(font.Measure(graphics, title),
            lines.Count == 0 ? 0 : lines.Max(line => font.Measure(graphics, line))) + 12;
        int height = (lines.Count + 1) * (font.LineHeight + 2) + 10;
        width = Math.Min(width, Math.Max(1, mapBounds.Width - 16));
        Rectangle panel = new(mapBounds.Left + 8, mapBounds.Top + 8, width, height);
        using SolidBrush background = new(Color.FromArgb(210, 0, 0, 0));
        using Pen border = new(Color.FromArgb(220, 216, 166, 58));
        graphics.FillRectangle(background, panel);
        graphics.DrawRectangle(border, panel.X, panel.Y, panel.Width - 1, panel.Height - 1);

        int x = panel.X + 6;
        int y = panel.Y + 5;
        font.DrawAtSolidColor(graphics, title, x, y, Color.FromArgb(255, 245, 190, 64));
        y += font.LineHeight + 2;
        foreach (string line in lines)
        {
            font.DrawAtSolidColor(graphics, line, x, y, Color.White);
            y += font.LineHeight + 2;
        }
    }

    private static List<string> WalkerLines(
        LegacyMissionSession session,
        Walkers walkers,
        int? hoverTile)
    {
        List<string> lines = [TileLine(session, hoverTile)];
        Walker? walker = hoverTile is int tile
            ? walkers.Active.FirstOrDefault(candidate => candidate.Tile == tile)
            : null;
        if (walker is null)
        {
            lines.Add("walker: none");
            lines.Add($"active walkers: {walkers.Active.Count}");
            return lines;
        }

        lines.Add($"id: {walker.Id}  type: {walker.Type}");
        lines.Add($"state: {walker.State}  action: {walker.ActionState}");
        lines.Add($"tile: {walker.Tile}  previous: {walker.PreviousTile}");
        lines.Add($"direction: {walker.Direction}  progress: {walker.ProgressOnTile}");
        lines.Add($"home: {walker.HomeBuildingId}  target: {walker.DestinationBuildingId}");
        lines.Add($"route: {walker.RoutePosition}/{walker.Route.Count}  destination: {walker.DestinationTile}");
        lines.Add($"people: {walker.MigrantPeople}  damage: {walker.Damage}");
        return lines;
    }

    private static List<string> NetworkLines(LegacyMissionSession session, int? hoverTile)
    {
        List<string> lines = [TileLine(session, hoverTile)];
        if (hoverTile is not int tile || !TryReadUInt32(session, "terrain_grid", tile, out uint terrain))
        {
            lines.Add("network: no tile under cursor");
            return lines;
        }

        int building = ReadGridUInt16(session, "building_grid", tile);
        int walker = ReadGridUInt16(session, "walker_grid", tile);
        lines.Add($"road: {((terrain & TerrainRoad) != 0 ? "yes" : "no")}  terrain: 0x{terrain:X8}");
        lines.Add($"building grid: {building}  walker grid: {walker}");
        lines.Add($"road neighbours N/E/S/W: {RoadNeighbourMask(session, tile)}");
        return lines;
    }

    private static List<string> EvolutionLines(
        LegacyMissionSession session,
        CitySimulation simulation,
        LegacyCityHudState hud,
        long ticks,
        int monthIndex,
        int year,
        int gameSpeed,
        bool paused)
    {
        int activeBuildings = CountActiveBuildings(session);
        return
        [
            $"tick: {ticks}  month: {monthIndex + 1}  year: {year}",
            $"speed: {gameSpeed}  paused: {(paused ? "yes" : "no")}",
            $"population: {hud.Population}  treasury: {hud.Treasury}",
            $"buildings: {activeBuildings}  walkers: {simulation.Walkers.Active.Count}",
            $"flood: {simulation.Flood.State}  row: {simulation.Flood.ProgressRow}",
            $"flood tick: {simulation.Flood.AbsoluteTick}  width: {simulation.Flood.FloodplainWidth}"
        ];
    }

    private static List<string> BuildingLines(LegacyMissionSession session, int? hoverTile)
    {
        List<string> lines = [TileLine(session, hoverTile)];
        if (hoverTile is not int tile)
        {
            lines.Add("building: none");
            return lines;
        }

        int id = ReadGridUInt16(session, "building_grid", tile);
        if (id <= 0 || id >= HousingManager.MaximumBuildings ||
            !session.LogicalChunks.TryGetValue("buildings", out ReadOnlyMemory<byte> memory))
        {
            lines.Add("building: none");
            return lines;
        }

        ReadOnlySpan<byte> buildings = memory.Span;
        int offset = id * HousingManager.RecordSize;
        if (offset < 0 || offset + HousingManager.RecordSize > buildings.Length)
        {
            lines.Add($"building grid id: {id} (invalid record)");
            return lines;
        }

        ReadOnlySpan<byte> record = buildings.Slice(offset, HousingManager.RecordSize);
        BuildingType type = (BuildingType)BinaryPrimitives.ReadInt16LittleEndian(record[16..]);
        lines.Add($"id: {id}  type: {type} ({(short)type})");
        lines.Add($"state: {record[0]}  size: {record[3]}  variant: {record[4]}");
        lines.Add($"anchor x/y: {BinaryPrimitives.ReadInt16LittleEndian(record[6..])}/{BinaryPrimitives.ReadInt16LittleEndian(record[8..])}");
        lines.Add($"tile: {BinaryPrimitives.ReadInt32LittleEndian(record[12..])}  subtype: {BinaryPrimitives.ReadInt16LittleEndian(record[18..])}");
        lines.Add($"population: {BinaryPrimitives.ReadInt16LittleEndian(record[28..])}  capacity: {BinaryPrimitives.ReadInt16LittleEndian(record[30..])}");
        lines.Add($"road access: {record[52]}  water: {record[53]}  well: {record[63]}");
        lines.Add($"damage: {ReadGridUInt16(session, "building_damage_grid", tile)}  " +
            $"desirability: {ReadGridByte(session, "desirability_grid", tile, signed: true)}");
        return lines;
    }

    private static string TileLine(LegacyMissionSession session, int? tile)
    {
        if (tile is not int value)
            return "cursor tile: none";
        if (!TryMapCoordinates(session, value, out int x, out int y))
            return $"cursor tile: {value}";
        return $"cursor tile: {value}  map x/y: {x}/{y}";
    }

    private static bool TryMapCoordinates(LegacyMissionSession session, int tile, out int x, out int y)
    {
        x = y = 0;
        if (!session.LogicalChunks.TryGetValue("scenario_info", out ReadOnlyMemory<byte> memory) ||
            memory.Length < 56 || tile < 0)
            return false;
        ReadOnlySpan<byte> scenario = memory.Span;
        int origin = BinaryPrimitives.ReadInt32LittleEndian(scenario[52..]);
        int relative = tile - origin;
        if (relative < 0)
            return false;
        x = relative % 228;
        y = relative / 228;
        return true;
    }

    private static string RoadNeighbourMask(LegacyMissionSession session, int tile)
    {
        int[] offsets = [-228, 1, 228, -1];
        char[] labels = ['N', 'E', 'S', 'W'];
        char[] result = ['-', '-', '-', '-'];
        for (int i = 0; i < offsets.Length; i++)
        {
            int neighbour = tile + offsets[i];
            bool crossesRow = i == 1 && tile % 228 == 227 || i == 3 && tile % 228 == 0;
            if (!crossesRow &&
                TryReadUInt32(session, "terrain_grid", neighbour, out uint terrain) &&
                (terrain & TerrainRoad) != 0)
                result[i] = labels[i];
        }
        return new string(result);
    }

    private static int CountActiveBuildings(LegacyMissionSession session)
    {
        if (!session.LogicalChunks.TryGetValue("buildings", out ReadOnlyMemory<byte> memory))
            return 0;
        ReadOnlySpan<byte> buildings = memory.Span;
        int count = 0;
        int records = Math.Min(HousingManager.MaximumBuildings, buildings.Length / HousingManager.RecordSize);
        for (int id = 1; id < records; id++)
        {
            byte state = buildings[id * HousingManager.RecordSize];
            if (state != 0 && state != 5)
                count++;
        }
        return count;
    }

    private static int ReadGridUInt16(LegacyMissionSession session, string name, int tile)
    {
        if (tile < 0 || !session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> memory))
            return 0;
        int offset = tile * sizeof(ushort);
        return offset >= 0 && offset + sizeof(ushort) <= memory.Length
            ? BinaryPrimitives.ReadUInt16LittleEndian(memory.Span[offset..])
            : 0;
    }

    private static int ReadGridByte(
        LegacyMissionSession session,
        string name,
        int tile,
        bool signed = false)
    {
        if (tile < 0 || !session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> memory) ||
            tile >= memory.Length)
            return 0;
        byte value = memory.Span[tile];
        return signed ? unchecked((sbyte)value) : value;
    }

    private static bool TryReadUInt32(
        LegacyMissionSession session,
        string name,
        int tile,
        out uint value)
    {
        value = 0;
        if (tile < 0 || !session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> memory))
            return false;
        int offset = tile * sizeof(uint);
        if (offset < 0 || offset + sizeof(uint) > memory.Length)
            return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(memory.Span[offset..]);
        return true;
    }
}
