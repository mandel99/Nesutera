using System.Drawing;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>
/// Owns the mutable city camera, rotation and repeat timing. Keyboard state is
/// kept by InputManager; foreground and cursor coordinates remain window input
/// snapshots.
/// </summary>
internal sealed class CameraManager
{
    private readonly TimeManager time;

    public CameraManager(TimeManager time) => this.time = time;

    public LegacyCityViewState? View { get; private set; }

    public void Configure(
        LegacyCityMapSnapshot map,
        LegacyIsometricViewport viewport,
        int orientation,
        int cameraX,
        int cameraY,
        bool reset)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(viewport);

        if (reset || View is null)
        {
            View = new LegacyCityViewState(map, viewport, orientation, cameraX, cameraY);
            return;
        }

        // LegacyCityViewState owns the orientation lookup built from its map.
        // Rebind it even when the viewport is unchanged so a terrain mutation
        // (for example a committed road) cannot leave hit testing and camera
        // bounds attached to the previous immutable map snapshot.
        View = new LegacyCityViewState(
            map, viewport, View.Orientation, View.CameraX, View.CameraY);
    }

    public bool TryHitTest(int x, int y, out int tileIndex)
    {
        if (View is not null)
            return View.TryHitTest(x, y, out tileIndex);
        tileIndex = LegacyMapOrientationIndex.InvalidTileIndex;
        return false;
    }

    [RewriteOnly("Projects a placement-preview cell through the verified legacy viewport geometry")]
    public bool TryGetTileScreenAnchor(int tileIndex, out Point anchor)
    {
        if (View is not null && View.Viewport.TryGetTileScreenAnchor(
                View.OrientationIndex, View.CameraX, View.CameraY, tileIndex,
                out int screenX, out int screenY))
        {
            anchor = new Point(screenX, screenY);
            return true;
        }

        anchor = Point.Empty;
        return false;
    }

    [OriginalAddress(0x00425840, "FUN_00425840")]
    public bool ActivateRotation(int segment)
    {
        if (View is null)
            return false;

        return segment switch
        {
            0 => View.OrientNorth(),
            1 or 2 => View.Rotate(LegacyCityCameraInput.ResolveRotationClockwise(segment)!.Value),
            _ => false
        };
    }

    [OriginalAddress(0x004061E0, "FUN_004061e0")]
    [OriginalAddress(0x0043EF10, "FUN_0043ef10")]
    public bool CenterOnMinimapPixel(
        LegacyCityMinimapProjection projection,
        int x,
        int y)
    {
        return View is not null &&
            projection.TryGetTileIndexAtPixel(x, y, out int tileIndex) &&
            View.CenterOnTile(tileIndex);
    }

    [OriginalAddress(0x004CF660, "FUN_004cf660")]
    [OriginalAddress(0x004DDD60, "FUN_004ddd60")]
    [OriginalAddress(0x004DDDE0, "FUN_004ddde0")]
    public bool UpdateContinuous(
        bool inputEnabled,
        int scrollSpeed,
        InputManager input,
        Rectangle screenBounds,
        out bool sampled)
    {
        sampled = false;
        if (!inputEnabled || View is null)
        {
            time.ResetCameraRepeat();
            return false;
        }

        int? interval = LegacyCityCameraInput.GetRepeatIntervalMilliseconds(scrollSpeed);
        if (interval is null)
        {
            time.ResetCameraRepeat();
            return false;
        }

        if (!time.IsCameraStepDue(interval.Value))
            return false;
        // FUN_004DDD60 advances the global cadence before FUN_004CF660 reads
        // and clears the one-shot keyboard flags, even when no direction wins.
        sampled = true;
        LegacyCameraDirection? direction = input.ResolveCameraDirection(screenBounds);
        input.ConsumeCameraKeys();
        if (direction is null)
            return false;
        return View.Move(direction.Value);
    }
}
