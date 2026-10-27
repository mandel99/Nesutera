using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public sealed partial class Walkers
{
    [OriginalAddress(0x004B1DF0, "FUN_004b1df0")]
    private void InitializeServiceRoam(Walker walker)
    {
        walker.RoamInitialized = true; walker.RoamStepCounter = 0;
        walker.RoamTurnDirection = 2; walker.RoamTurnCountdown = -1; walker.RoamFree = true;
        if (walker.HomeBuildingId <= 0 || walker.HomeBuildingId >= MaximumBuildings) return;
        Span<byte> home = session.GetWritableLogicalChunk("buildings").Span.Slice(walker.HomeBuildingId * BuildingRecordSize, BuildingRecordSize);
        int direction = home[52]; home[52] = (byte)(direction + 2 > 6 ? 0 : direction + 2);
        int x = BinaryPrimitives.ReadInt16LittleEndian(home[6..]), y = BinaryPrimitives.ReadInt16LittleEndian(home[8..]);
        switch (direction)
        { case 0: y -= 8; break; case 2: x += 8; break; case 4: y += 8; break; case 6: x -= 8; break; }
        x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1);
        if (LegacyBuildingRoadAccess.TryFindNearbyRoad(Required("terrain_grid", LegacyCityMapSnapshot.ImageGridLength).Span,
            width, height, origin, origin + x + y * 228, 1, out int goal, radius: 6))
        { walker.DestinationTile = goal; walker.RoamFree = false; }
    }

    [OriginalAddress(0x004B1EF0, "FUN_004b1ef0")]
    private int SelectRoamingDirection(Walker walker, ReadOnlySpan<int> candidates, int count)
    {
        walker.RoamStepCounter = unchecked((byte)(walker.RoamStepCounter + 1));
        int direction = (uint)walker.Direction < 8 ? walker.Direction : 0;
        // The current heading is the previous selected direction at a tile
        // boundary; the route adapter keeps the older segment separately.
        int excluded = (direction + 4) & 7;
        if (count == 1)
        {
            foreach (int cardinal in CardinalDirectionIndices) if (candidates[cardinal] != 0) return cardinal;
        }
        else if (count == 2)
        {
            if (walker.RoamTurnCountdown == -1)
            { direction = ChooseRoamingTurn(walker); excluded = -1; }
        }
        else
        {
            direction = (ReadRandom(walker.Tile) + walker.RoamStepCounter) & 6;
            if (candidates[direction] == 0 || direction == excluded)
            {
                walker.RoamTurnCountdown = unchecked((sbyte)(walker.RoamTurnCountdown - 1));
                if (walker.RoamTurnCountdown < 1)
                { direction = ChooseRoamingTurn(walker); excluded = -1; }
            }
        }
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (candidates[direction] != 0 && direction != excluded) return direction;
            direction += walker.RoamTurnDirection;
            if (direction > 6) direction = 0;
            if (direction < 0) direction = 6;
        }
        return -1;
    }

    [OriginalAddress(0x004B21D0, "FUN_004b21d0")]
    [OriginalAddress(0x0051CBA0, "FUN_0051cba0")]
    private int ChooseRoamingTurn(Walker walker)
    {
        int dx = Math.Sign(walker.DestinationTile % 228 - walker.Tile % 228);
        int dy = Math.Sign(walker.DestinationTile / 228 - walker.Tile / 228);
        int heading = Array.IndexOf(Directions, (dx, dy));
        if (heading < 0) heading = 0;
        bool Road(int direction)
        {
            var delta = Directions[direction];
            return TryLocal(walker.Tile, out int x, out int y) && TryTile(x + delta.Dx, y + delta.Dy, out int tile) &&
                (BinaryPrimitives.ReadUInt32LittleEndian(session.LogicalChunks["terrain_grid"].Span[(tile * 4)..]) & 0x40) != 0;
        }
        int clockwise = heading, clockwiseSteps = 0;
        while (clockwiseSteps < 8 && ((clockwise & 1) != 0 || !Road(clockwise)))
        { clockwise = (clockwise + 1) & 7; clockwiseSteps++; }
        int counterclockwise = heading, counterSteps = 0;
        while (counterSteps < 8 && ((counterclockwise & 1) != 0 || !Road(counterclockwise)))
        { counterclockwise = (counterclockwise + 7) & 7; counterSteps++; }
        if (clockwiseSteps == 8) clockwise = 0;
        if (counterSteps == 8) counterclockwise = 4;
        walker.RoamTurnCountdown = 5;
        walker.RoamTurnDirection = clockwiseSteps <= counterSteps ? (sbyte)2 : (sbyte)-2;
        return clockwiseSteps <= counterSteps ? clockwise : counterclockwise;
    }

    [OriginalAddress(0x0047E230, "FUN_0047e230")]
    private int GetRoamingVisit(WalkerType type, int tile)
    {
        int shift = RoamingChannel(type) * 3;
        return (int)(BinaryPrimitives.ReadUInt32LittleEndian(Required("GRID03_32BIT", LegacyCityMapSnapshot.ImageGridLength).Span[(tile * 4)..]) >> shift & 7);
    }

    [OriginalAddress(0x0047E250, "FUN_0047e250")]
    [OriginalAddress(0x004BAA10, "FUN_004baa10")]
    private void RecordRoamingVisit(Walker walker)
    {
        if (walker.Type is not (WalkerType.Fireman or WalkerType.Architect or WalkerType.Policeman or WalkerType.Recruiter or WalkerType.BazaarSeller)) return;
        int shift = RoamingChannel(walker.Type) * 3;
        Span<byte> cell = session.GetWritableLogicalChunk("GRID03_32BIT").Span.Slice(walker.Tile * 4, 4);
        uint original = BinaryPrimitives.ReadUInt32LittleEndian(cell), value = original >> shift & 7;
        if (value < 7)
            BinaryPrimitives.WriteUInt32LittleEndian(cell, (((7u << shift) ^ 0xffffu) & original) | ((value + 1) << shift));
    }

    [OriginalAddress(0x004BAA10, "FUN_004baa10")]
    private static int RoamingChannel(WalkerType type) => type switch
    { WalkerType.Fireman => 1, WalkerType.Architect => 2, WalkerType.Policeman => 3, WalkerType.BazaarSeller => 8, _ => 7 };
}
