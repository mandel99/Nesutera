using System.Buffers.Binary;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x00489CA0, "FUN_00489ca0")]
    [OriginalAddress(0x0048A720, "FUN_0048a720")]
    internal bool PrepareResidentHouseEntry(Walker walker)
    {
        if (!TryGetHouse(walker.HomeBuildingId, out int house, out _)) return false;
        if (!walker.FreeWalkInitialized || walker.DestinationTile != house)
            BeginResidentApproach(walker, house);
        return true;
    }

    [OriginalAddress(0x004B4220, "FUN_004b4220")]
    private void BeginResidentApproach(Walker walker, int target)
    {
        if (!TryLocal(walker.Tile, out int x, out int y) || !TryLocal(target, out int tx, out int ty)) return;
        walker.route.Clear(); walker.RoutePosition = 0; walker.ProgressOnTile = 0;
        walker.DestinationTile = target;
        walker.FreeWalkX = x * 15; walker.FreeWalkY = y * 15;
        walker.FreeWalkTargetX = tx * 15; walker.FreeWalkTargetY = ty * 15;
        walker.FreeWalkRemainingX = Math.Abs(walker.FreeWalkTargetX - walker.FreeWalkX);
        walker.FreeWalkRemainingY = Math.Abs(walker.FreeWalkTargetY - walker.FreeWalkY);
        int dx = walker.FreeWalkRemainingX, dy = walker.FreeWalkRemainingY;
        walker.FreeWalkError = dx < dy ? dx * 2 - dy : dy < dx ? dy * 2 - dx : 0;
        walker.FreeWalkAxis = dx < dy ? 2 : 1;
        int direction = Array.IndexOf(Directions, (Math.Sign(tx - x), Math.Sign(ty - y)));
        if (dx * 2 < dy) direction = ty < y ? 0 : 4;
        if (dy * 2 < dx) direction = tx < x ? 6 : 2;
        walker.PreviousDirection = walker.Direction; walker.Direction = Math.Max(0, direction);
        walker.FreeWalkInitialized = true;
    }

    [OriginalAddress(0x004B3FF0, "FUN_004b3ff0")]
    [OriginalAddress(0x004B4180, "FUN_004b4180")]
    [OriginalAddress(0x004B43A0, "FUN_004b43a0")]
    [OriginalAddress(0x004B43E0, "FUN_004b43e0")]
    internal bool AdvanceResidentApproach(Walker walker)
    {
        if (!walker.FreeWalkInitialized) BeginResidentApproach(walker, walker.DestinationTile);
        if (!walker.FreeWalkInitialized || walker.FreeWalkRemainingX + walker.FreeWalkRemainingY < 1)
        { walker.FreeWalkInitialized = false; return true; }
        if (walker.FreeWalkAxis == 2)
        {
            walker.FreeWalkError += walker.FreeWalkError < 0 ? walker.FreeWalkRemainingX * 2 :
                (walker.FreeWalkRemainingX - walker.FreeWalkRemainingY) * 2;
            walker.FreeWalkRemainingY--;
            walker.FreeWalkY += Math.Sign(walker.FreeWalkTargetY - walker.FreeWalkY);
            if (walker.FreeWalkError >= 0)
            { walker.FreeWalkRemainingX--; walker.FreeWalkX += Math.Sign(walker.FreeWalkTargetX - walker.FreeWalkX); }
        }
        else
        {
            walker.FreeWalkError += walker.FreeWalkError < 0 ? walker.FreeWalkRemainingY * 2 :
                (walker.FreeWalkRemainingY - walker.FreeWalkRemainingX) * 2;
            walker.FreeWalkRemainingX--;
            walker.FreeWalkX += Math.Sign(walker.FreeWalkTargetX - walker.FreeWalkX);
            if (walker.FreeWalkError >= 0)
            { walker.FreeWalkRemainingY--; walker.FreeWalkY += Math.Sign(walker.FreeWalkTargetY - walker.FreeWalkY); }
        }
        walker.AnimationFrame = (walker.AnimationFrame + 1) % 12;
        int old = walker.Tile;
        if (!TryTile(walker.FreeWalkX / 15, walker.FreeWalkY / 15, out int tile))
        { Destroy(walker); return false; }
        walker.PreviousTile = old; walker.Tile = tile;
        changedTiles.Add(old); changedTiles.Add(tile);
        // The native mover reports completion on the pulse after exhausting
        // its remaining axes, rather than teleporting to a distant endpoint.
        return false;
    }

    [OriginalAddress(0x004B3FF0, "FUN_004b3ff0")]
    [OriginalAddress(0x004B4220, "FUN_004b4220")]
    private static void RestoreResidentApproach(ReadOnlySpan<byte> record, Walker walker)
    {
        if (!IsResidentType(walker.Type) && walker.Type!=WalkerType.HunterArrow) return;
        walker.AnimationFrame = record[1] % 12;
        if (!IsResidentApproachState(walker)) return;
        walker.FreeWalkX = ReadLaborShort(record, 74); walker.FreeWalkY = ReadLaborShort(record, 76);
        walker.FreeWalkTargetX = ReadLaborShort(record, 78); walker.FreeWalkTargetY = ReadLaborShort(record, 80);
        walker.FreeWalkRemainingX = ReadLaborShort(record, 82); walker.FreeWalkRemainingY = ReadLaborShort(record, 84);
        walker.FreeWalkError = ReadLaborShort(record, 86); walker.FreeWalkAxis = record[88];
        walker.FreeWalkInitialized = record[12] == 1 && walker.FreeWalkAxis is 1 or 2 &&
            walker.FreeWalkX >= 0 && walker.FreeWalkY >= 0 && walker.FreeWalkX < 228 * 15 && walker.FreeWalkY < 228 * 15 &&
            walker.FreeWalkTargetX is >= 0 and < 228 * 15 && walker.FreeWalkTargetY is >= 0 and < 228 * 15 &&
            walker.FreeWalkRemainingX is >= 0 and <= 228 * 15 && walker.FreeWalkRemainingY is >= 0 and <= 228 * 15;
    }

    [OriginalAddress(0x004B4800, "FUN_004b4800")]
    [OriginalAddress(0x004B4220, "FUN_004b4220")]
    private void PublishResidentApproach(Span<byte> record, Walker walker)
    {
        if (!IsResidentType(walker.Type) && walker.Type!=WalkerType.HunterArrow) return;
        record[1] = (byte)walker.AnimationFrame;
        bool free = walker.FreeWalkInitialized && IsResidentApproachState(walker);
        record[12] = free ? (byte)1 : (byte)0;
        if (free)
        {
            WriteLaborShort(record, 74, walker.FreeWalkX); WriteLaborShort(record, 76, walker.FreeWalkY);
            WriteLaborShort(record, 78, walker.FreeWalkTargetX); WriteLaborShort(record, 80, walker.FreeWalkTargetY);
            WriteLaborShort(record, 82, walker.FreeWalkRemainingX); WriteLaborShort(record, 84, walker.FreeWalkRemainingY);
            WriteLaborShort(record, 86, walker.FreeWalkError); record[88] = (byte)walker.FreeWalkAxis;
        }
        else if (TryLocal(walker.Tile, out int x, out int y))
        {
            int dx = 0, dy = 0;
            if (walker.RoutePosition < walker.route.Count)
            { var next = walker.route[walker.RoutePosition]; dx = Math.Sign(next % 228 - walker.Tile % 228); dy = Math.Sign(next / 228 - walker.Tile / 228); }
            WriteLaborShort(record, 74, x * 15 + dx * walker.ProgressOnTile);
            WriteLaborShort(record, 76, y * 15 + dy * walker.ProgressOnTile);
        }
    }

    [RewriteOnly("Keeps household-only entry states separate from workplace road endpoints")]
    private static bool IsResidentApproachState(Walker walker) => walker.Type==WalkerType.HunterArrow || walker.Type == WalkerType.Emigrant && walker.ActionState == EmigrantBehavior.WalkingToRoad ||
        walker.Type is WalkerType.Immigrant or WalkerType.Homeless && walker.ActionState == 10;

    [RewriteOnly("Identifies the three native population figures")]
    private static bool IsResidentType(WalkerType type) => type is WalkerType.Immigrant or WalkerType.Emigrant or WalkerType.Homeless;
}
