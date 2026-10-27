using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>
/// Cursor state numbers consumed by FUN_004CF460. The attached high-resolution
/// artwork is downsampled onto the original 32x32 cursor canvas and mapped to
/// the original hotspots.
/// </summary>
internal enum LegacyCursorKind
{
    Default = 0,
    PersonDirection = 1,
    ClearLand = 2,
    Boat = 3,
    BoatFigureA = 4,
    BoatFigureB = 5,
    BoatDownA = 6,
    BoatDownB = 7,
    Sword = 8,
    Bow = 9,
    Cart = 10,
    PersonParticlesA = 11,
    PersonParticlesB = 12,
    PersonParticlesC = 13,
    PersonParticlesD = 14,
    Combat = 15,
    BoatHorizontal = 16,
    BoatRight = 17,
    BoatBrush = 18
}

internal sealed class LegacyCursorManager : IDisposable
{
    private const int CursorSize = 32;
    private static readonly CursorDefinition[] Definitions =
    [
        new(LegacyCursorKind.Default, "cursor-00-default-resource-66.png", 0, 0),
        new(LegacyCursorKind.PersonDirection, "cursor-01-person-direction-resource-96.png", 28, 16),
        new(LegacyCursorKind.ClearLand, "cursor-02-clear-land-resource-82.png", 8, 31),
        new(LegacyCursorKind.Boat, "cursor-03-boat-resource-8e.png", 31, 16),
        new(LegacyCursorKind.BoatFigureA, "cursor-04-boat-figure-a-resource-90.png", 31, 14),
        new(LegacyCursorKind.BoatFigureB, "cursor-05-boat-figure-b-resource-91.png", 31, 14),
        new(LegacyCursorKind.BoatDownA, "cursor-06-boat-down-a-resource-92.png", 28, 16),
        new(LegacyCursorKind.BoatDownB, "cursor-07-boat-down-b-resource-93.png", 28, 15),
        new(LegacyCursorKind.Sword, "cursor-08-sword-resource-88.png", 0, 0),
        new(LegacyCursorKind.Bow, "cursor-09-bow-resource-94.png", 0, 0),
        new(LegacyCursorKind.Cart, "cursor-10-cart-resource-95.png", 28, 21),
        new(LegacyCursorKind.PersonParticlesA, "cursor-11-person-particles-a-resource-98.png", 16, 16),
        new(LegacyCursorKind.PersonParticlesB, "cursor-12-person-particles-b-resource-99.png", 16, 14),
        new(LegacyCursorKind.PersonParticlesC, "cursor-13-person-particles-c-resource-9a.png", 16, 14),
        new(LegacyCursorKind.PersonParticlesD, "cursor-14-person-particles-d-resource-9b.png", 16, 14),
        new(LegacyCursorKind.Combat, "cursor-15-combat-resource-9c.png", 16, 14),
        new(LegacyCursorKind.BoatHorizontal, "cursor-16-boat-horizontal-resource-9d.png", 31, 14),
        new(LegacyCursorKind.BoatRight, "cursor-17-boat-right-resource-9f.png", 31, 16),
        new(LegacyCursorKind.BoatBrush, "cursor-18-boat-brush-resource-a0.png", 31, 14)
    ];

    private readonly Dictionary<LegacyCursorKind, CursorEntry> cursors = [];
    private LegacyCursorKind? applied;

    public LegacyCursorManager()
    {
        foreach (CursorDefinition definition in Definitions)
            cursors.Add(definition.Kind, Load(definition));
    }

    [OriginalAddress(0x004CF460, "FUN_004cf460")]
    public void Apply(Control control, LegacyCursorKind kind)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (applied == kind)
            return;
        control.Cursor = cursors[kind].Cursor;
        applied = kind;
    }

    private static CursorEntry Load(CursorDefinition definition)
    {
        string resource = $"Pharaoh.Desktop.Assets.Cursors.{definition.FileName}";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Embedded cursor resource '{resource}' is missing.");
        using Bitmap source = new(stream);
        using Bitmap scaled = new(CursorSize, CursorSize, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(scaled))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, CursorSize, CursorSize));
        }

        IntPtr icon = scaled.GetHicon();
        if (!GetIconInfo(icon, out IconInfo info))
        {
            DestroyIcon(icon);
            throw new InvalidOperationException($"Could not create cursor '{definition.FileName}'.");
        }
        info.IsIcon = false;
        info.XHotspot = checked((uint)definition.HotspotX);
        info.YHotspot = checked((uint)definition.HotspotY);
        IntPtr handle = CreateIconIndirect(ref info);
        DeleteObject(info.MaskBitmap);
        DeleteObject(info.ColorBitmap);
        DestroyIcon(icon);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create cursor '{definition.FileName}'.");
        return new CursorEntry(new Cursor(handle), handle);
    }

    public void Dispose()
    {
        foreach (CursorEntry entry in cursors.Values)
        {
            entry.Cursor.Dispose();
            DestroyIcon(entry.Handle);
        }
        cursors.Clear();
    }

    private readonly record struct CursorDefinition(
        LegacyCursorKind Kind, string FileName, int HotspotX, int HotspotY);
    private readonly record struct CursorEntry(Cursor Cursor, IntPtr Handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
