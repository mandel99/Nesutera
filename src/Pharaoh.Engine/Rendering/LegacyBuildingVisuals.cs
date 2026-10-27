using System.Buffers.Binary;
using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

public readonly record struct LegacyBuildingOverlay(int ImageId, int X, int Y);

/// <summary>Native overlays relative to the top-left of the complete building bitmap.</summary>
public static class LegacyBuildingVisuals
{
    public const int GranaryWorkerImage = 18477;
    public const int GranaryFoodImage = 13994;
    public const int HuntingStockImage = 13198;
    private static readonly (int X, int Y)[] FoodOffsets = [(110, -73), (126, -64), (145, -55), (161, -47), (94, -66), (111, -57), (130, -47), (147, -38)];

    [OriginalAddress(0x0040F2C0, "FUN_0040f2c0")]
    public static bool IsAnimated(ReadOnlySpan<byte> building)
    {
        if (building[0] != 1) return false;
        int type = BinaryPrimitives.ReadInt16LittleEndian(building[16..]);
        return type is 99 or 92 || BinaryPrimitives.ReadInt16LittleEndian(building[64..]) > 0;
    }

    [OriginalAddress(0x00442280, "FUN_00442280")]
    public static int GranaryPileCount(int amount) => amount <= 0 ? 0 : amount <= 600 ? 1 : Math.Min(8, 2 + (amount - 601) / 400);

    [OriginalAddress(0x00442280, "FUN_00442280")]
    [OriginalAddress(0x00436B60, "FUN_00436b60")]
    [OriginalAddress(0x004381B0, "FUN_004381b0")]
    [OriginalAddress(0x00436310, "FUN_00436310")]
    public static IReadOnlyList<LegacyBuildingOverlay> GetOverlays(Sg3AssetCatalog assets, ReadOnlySpan<byte> building, int imageId, int frame, bool forceAnimation = false)
    {
        List<LegacyBuildingOverlay> result = [];
        if (building[0] is not (1 or 3) && !forceAnimation) return result;
        Sg3ImageRecord record = assets.GlobalImages.Resolve(imageId).Image;
        int type = BinaryPrimitives.ReadInt16LittleEndian(building[16..]);
        int size = Math.Max(1, (int)building[3]);
        // Native draw-loop coordinates precede the multi-tile base correction.
        int nativeOriginY = record.Height - size * 30 + (size - 1) * 15;
        if (type is 71 or 191)
        {
            int position = 0;
            // 005596D0 enumerates foods in ascending id order. A valid stored
            // resource has already entered that list through production/import.
            for (int resource = 1; resource <= 8 && position < FoodOffsets.Length; resource++)
            {
                int amount = BinaryPrimitives.ReadInt16LittleEndian(building[(86 + resource * 2)..]);
                for (int n = 0; n < GranaryPileCount(amount) && position < FoodOffsets.Length; n++, position++)
                { var point = FoodOffsets[position]; result.Add(new(GranaryFoodImage + resource - 1, point.X, nativeOriginY + point.Y)); }
            }
            if ((forceAnimation || IsAnimated(building)) && frame > 0)
            {
                int worker = GranaryWorkerImage + Math.Min(frame, (int)record.SpriteCount) - 1;
                Sg3ImageRecord sprite = assets.GlobalImages.Resolve(worker).Image;
                result.Add(new(worker, 113 - sprite.SpriteOffsetX, nativeOriginY + 3 - sprite.SpriteOffsetY));
            }
        }
        else if ((forceAnimation || IsAnimated(building)) && record.SpriteCount > 0 && frame > 0)
            result.Add(new(imageId + Math.Min(frame, (int)record.SpriteCount), record.SpriteOffsetX, record.SpriteOffsetY));
        if (type == 115)
        {
            int amount = BinaryPrimitives.ReadInt16LittleEndian(building[60..]);
            // The native signed division truncates toward zero: 1..199 share
            // the first pile; exactly zero produces index -1 and no overlay.
            int stockFrame = (amount - 100) / 100;
            if (stockFrame >= 0)
                result.Add(new(HuntingStockImage + Math.Min(stockFrame, 4), 61, nativeOriginY + 13));
        }
        return result;
    }
}

/// <summary>Native20ms speed buckets and persistent per-tile animation frames.</summary>
public sealed class LegacyBuildingAnimationClock
{
    private readonly long[] last = new long[51];
    private readonly bool[] due = new bool[51];

    [OriginalAddress(0x0040F270, "FUN_0040f270")]
    public void AdvanceTo(long milliseconds)
    {
        for (int speed = 0; speed < last.Length; speed++)
        { due[speed] = milliseconds - last[speed] >= speed * 20; if (due[speed]) last[speed] = milliseconds; }
    }

    [OriginalAddress(0x0040F2C0, "FUN_0040f2c0")]
    public bool AdvanceFrame(LegacyMissionSession session, int tile, Sg3ImageRecord record)
    {
        if (record.SpriteCount == 0 || record.SpeedId >= due.Length || !due[record.SpeedId]) return false;
        Span<byte> grid = session.GetWritableLogicalChunk("sprite_grid").Span;
        int old = grid[tile], frame = old & 127;
        bool reverse = (old & 128) != 0;
        if (record.CanReverse)
        {
            if (reverse) { frame--; if (frame < 1) { frame = 1; reverse = false; } }
            else { frame++; if (frame > record.SpriteCount) { frame = record.SpriteCount; reverse = true; } }
        }
        else { frame++; if (frame > record.SpriteCount) frame = 1; reverse = false; }
        grid[tile] = (byte)(frame | (reverse ? 128 : 0));
        return grid[tile] != old;
    }
}
