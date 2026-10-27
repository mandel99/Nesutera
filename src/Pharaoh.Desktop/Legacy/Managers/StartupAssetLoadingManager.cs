using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>
/// Replays the graphics-pack cadence from Pharaoh.exe FUN_00414420.  Each
/// operation is intentionally synchronous: the executable paints/presents a
/// checkpoint, performs the next group of file reads, then paints again.
/// </summary>
internal sealed class StartupAssetLoadingManager(Sg3AssetCatalog assets)
{
    private int nextPackSlot;

    public int NextPackSlot => nextPackSlot;
    public bool GraphicsComplete => nextPackSlot >= LegacyStartupResources.ResourcePackNames.Count;

    [OriginalAddress(0x0053DD10, "FUN_0053dd10")]
    public void RegisterNextPack()
    {
        if (GraphicsComplete) return;
        assets.RegisterArchive(LegacyStartupResources.ResourcePackNames[nextPackSlot]);
        nextPackSlot++;
    }

    [OriginalAddress(0x0053DD40, "FUN_0053dd40")]
    public void RegisterRemainingPacks()
    {
        while (!GraphicsComplete)
            RegisterNextPack();
    }

    /// <summary>
    /// Performs the work that follows a painted loading checkpoint.  The
    /// counts are the exact FUN_00414420 sequence: 1, 1, 5, then all remaining.
    /// </summary>
    [OriginalAddress(0x00414420, "FUN_00414420")]
    public void CompleteCheckpointWork(int paintedCheckpoint)
    {
        if ((uint)paintedCheckpoint >= LegacyStartupResources.GraphicsPackSlotsAfterCheckpoint.Count)
            throw new ArgumentOutOfRangeException(nameof(paintedCheckpoint));
        int targetSlot = LegacyStartupResources.GraphicsPackSlotsAfterCheckpoint[paintedCheckpoint];
        while (nextPackSlot < targetSlot)
            RegisterNextPack();
    }
}
