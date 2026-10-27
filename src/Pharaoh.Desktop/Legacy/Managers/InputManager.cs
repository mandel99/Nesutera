using System.Drawing;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

internal enum CityTimeAction
{
    TogglePause,
    DecreaseSpeed,
    IncreaseSpeed
}

/// <summary>Collects desktop input state and resolves simultaneous camera directions.</summary>
internal sealed class InputManager
{
    private readonly HashSet<Keys> pendingCameraKeys = [];

    public Point PointerClientPosition { get; private set; }
    public bool PointerInsideWindow { get; private set; }
    public bool WindowActive { get; private set; }

    public void PointerMoved(Point clientPosition)
    {
        PointerClientPosition = clientPosition;
        PointerInsideWindow = true;
    }

    public void PointerLeft() => PointerInsideWindow = false;

    public void SetWindowActive(bool active)
    {
        WindowActive = active;
        if (!active)
            Clear();
    }

    public bool KeyDown(Keys key)
    {
        if (!IsCameraMovementKey(key))
            return false;
        // Pharaoh's Win32 key path writes one-shot direction flags. The camera
        // sampler consumes and clears them; holding a key is driven by the OS
        // key-repeat messages instead of a permanently held movement state.
        pendingCameraKeys.Add(key);
        return true;
    }

    public bool KeyUp(Keys key)
    {
        if (!IsCameraMovementKey(key))
            return false;
        // The EXE has no matching key-up write for DAT_00E92E2C..38. A short
        // tap therefore remains queued until FUN_004CF660 consumes it on the
        // next camera cadence instead of being lost before that sample.
        return true;
    }

    public void Clear() => pendingCameraKeys.Clear();

    [OriginalAddress(0x004CF660, "FUN_004cf660")]
    public void ConsumeCameraKeys() => pendingCameraKeys.Clear();

    public LegacyCameraDirection? ResolveCameraDirection(
        (bool North, bool East, bool South, bool West) edge) =>
        LegacyCityCameraInput.ResolveDirection(
            pendingCameraKeys.Contains(Keys.Up) || edge.North,
            pendingCameraKeys.Contains(Keys.Right) || edge.East,
            pendingCameraKeys.Contains(Keys.Down) || edge.South,
            pendingCameraKeys.Contains(Keys.Left) || edge.West);

    public LegacyCameraDirection? ResolveCameraDirection(Rectangle screenBounds)
    {
        (bool North, bool East, bool South, bool West) edge = default;
        if (WindowActive && PointerInsideWindow && screenBounds.Width > 0 && screenBounds.Height > 0)
        {
            Point pointer = Cursor.Position;
            edge = LegacyCityCameraInput.ResolveEdgeFlags(
                pointer.X, pointer.Y,
                screenBounds.Left, screenBounds.Top,
                screenBounds.Right, screenBounds.Bottom);
        }
        return ResolveCameraDirection(edge);
    }

    public static bool IsCameraMovementKey(Keys key) => key is
        Keys.Up or Keys.Right or Keys.Down or Keys.Left;

    public static CityTimeAction? ResolveTimeAction(Keys key) => key switch
    {
        Keys.P => CityTimeAction.TogglePause,
        Keys.OemOpenBrackets => CityTimeAction.DecreaseSpeed,
        Keys.OemCloseBrackets => CityTimeAction.IncreaseSpeed,
        _ => null
    };
}
