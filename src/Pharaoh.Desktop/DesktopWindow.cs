namespace Pharaoh.Desktop;

/// <summary>Shared application window and branding lifetime.</summary>
internal abstract class DesktopWindow : Form
{
    private readonly Icon applicationIcon;

    [Pharaoh.Engine.ReverseEngineering.RewriteOnly("Applies the supplied Nesutera branding to window and taskbar icons")]
    protected DesktopWindow()
    {
        using Stream source = typeof(DesktopWindow).Assembly.GetManifestResourceStream(
            "Pharaoh.Desktop.Assets.Branding.Nesutera.ico")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using Icon original = new(source);
        applicationIcon = (Icon)original.Clone();
        Icon = applicationIcon;
    }

    [Pharaoh.Engine.ReverseEngineering.RewriteOnly("Releases the application-owned branding icon")]
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) applicationIcon.Dispose();
    }
}
