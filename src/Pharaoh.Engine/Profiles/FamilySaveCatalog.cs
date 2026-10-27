using Pharaoh.Engine.Installation;

namespace Pharaoh.Engine.Profiles;

/// <summary>Discovers the saved cities belonging to one family profile.</summary>
public static class FamilySaveCatalog
{
    public static IReadOnlyList<string> Load(string installationRoot, string familyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        string directory = new LegacyGamePaths(installationRoot).FamilySaveDirectory(familyName);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.sav", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Resolves the save represented by Pharaoh.exe's current-family-save buffer.
    /// The rewrite cannot retain that process-global buffer across launches, so the
    /// most recently written family save is its durable equivalent.
    /// </summary>
    public static string? FindMostRecent(string installationRoot, string familyName) =>
        Load(installationRoot, familyName)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
}
