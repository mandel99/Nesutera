using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Rendering;

/// <summary>
/// Advances the two water image ranges mutated by Pharaoh.exe's base map pass.
/// Shoreline images outside these ranges remain static.
/// </summary>
public sealed class LegacyWaterAnimation
{
    public const int PulseMilliseconds = 61;
    public const uint TerrainWater = 0x00000004;
    public const uint TerrainDeepWater = 0x04000000;
    private const int TerrainGlobalBase = 14252;
    private const int FrameCount = 6;
    private const int DeepWaterRowLength = 15;

    public LegacyWaterAnimation(Sg3Archive terrainArchive)
        : this(
            TerrainGlobalBase + GroupStart(terrainArchive, 19),
            TerrainGlobalBase + GroupStart(terrainArchive, 61))
    {
    }

    public LegacyWaterAnimation(int openWaterBase, int deepWaterBase)
    {
        if (openWaterBase <= 0) throw new ArgumentOutOfRangeException(nameof(openWaterBase));
        if (deepWaterBase <= 0) throw new ArgumentOutOfRangeException(nameof(deepWaterBase));
        OpenWaterBase = openWaterBase;
        DeepWaterBase = deepWaterBase;
    }

    public int OpenWaterBase { get; }
    public int DeepWaterBase { get; }

    [OriginalAddress(0x00430D30, "FUN_00430d30")]
    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    public int AdvanceFrame(int imageId, uint terrainFlags)
    {
        if ((terrainFlags & TerrainWater) == 0)
            return imageId;

        if ((terrainFlags & TerrainDeepWater) != 0)
            return AdvanceRange(imageId, DeepWaterBase, DeepWaterRowLength, FrameCount);

        return AdvanceRange(imageId, OpenWaterBase, stride: 1, FrameCount);
    }

    private static int AdvanceRange(int imageId, int rangeStart, int stride, int frameCount)
    {
        int rangeLength = checked(stride * frameCount);
        int rangeEnd = checked(rangeStart + rangeLength - 1);
        if (imageId < rangeStart || imageId > rangeEnd)
            return imageId;

        int next = imageId + stride;
        return next > rangeEnd ? next - rangeLength : next;
    }

    private static int GroupStart(Sg3Archive archive, int group)
    {
        ArgumentNullException.ThrowIfNull(archive);
        int local = archive.GroupImageIds[group];
        return local > 0
            ? local
            : throw new InvalidDataException($"Terrain SG3 group {group} has no image start.");
    }
}
