namespace Pharaoh.Engine.Installation;

/// <summary>
/// Resolves writable files exactly where Pharaoh 2.1 stores them. No per-user
/// or roaming-profile fallback is permitted because the files must remain
/// visible to the original executable.
/// </summary>
public sealed class LegacyGamePaths
{
    public LegacyGamePaths(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        InstallationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        SaveDirectory = DirectChild(InstallationRoot, "Save");
        MapsDirectory = DirectChild(InstallationRoot, "Maps");
    }

    public string InstallationRoot { get; }
    public string SaveDirectory { get; }
    public string MapsDirectory { get; }

    public string ProfileDataPath(string familyName) =>
        DirectChild(SaveDirectory, ValidateName(familyName) + ".dat");

    public string FamilySaveDirectory(string familyName) =>
        DirectChild(SaveDirectory, ValidateName(familyName));

    public string FamilySavePath(string familyName, string saveName) =>
        DirectChild(FamilySaveDirectory(familyName), ValidateName(saveName) + ".sav");

    public string MapPath(string mapName) =>
        DirectChild(MapsDirectory, ValidateName(mapName) + ".map");

    public string MapMetadataPath(string mapName) =>
        DirectChild(MapsDirectory, ValidateName(mapName) + ".txt");

    public void EnsureWritableDirectories(string? familyName = null)
    {
        Directory.CreateDirectory(SaveDirectory);
        Directory.CreateDirectory(MapsDirectory);
        if (familyName is not null)
            Directory.CreateDirectory(FamilySaveDirectory(familyName));
    }

    public static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("The legacy file name must be a single safe path component.", nameof(name));
        return name;
    }

    private static string DirectChild(string parent, string child)
    {
        string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string result = Path.GetFullPath(Path.Combine(fullParent, child));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(result), fullParent, comparison))
            throw new InvalidOperationException("Resolved legacy path escaped its expected game directory.");
        return result;
    }
}
