using Pharaoh.Engine.Installation;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Profiles;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Owns SAV discovery and filesystem publication operations.</summary>
internal sealed class FileManager(string installationRoot)
{
    private readonly LegacyGamePaths paths = new(installationRoot);

    public IReadOnlyList<string> EnumerateFamilySaves(string familyName)
        => FamilySaveCatalog.Load(paths.InstallationRoot, familyName);

    public string? FindMostRecentFamilySave(string familyName) =>
        FamilySaveCatalog.FindMostRecent(paths.InstallationRoot, familyName);

    public void Delete(string path) => File.Delete(path);

    [OriginalAddress(0x00402540, "FUN_00402540")]
    public string SaveCity(
        LegacyMissionSession session,
        string familyName,
        string requestedName)
    {
        string safeName = SanitizeFileName(requestedName);
        string directory = paths.FamilySaveDirectory(familyName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, safeName + ".sav");
        session.CreateSaveDocument().SaveAtomically(path);
        return path;
    }

    private static string SanitizeFileName(string requestedName)
    {
        string safeName = string.Concat(requestedName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return safeName.Length == 0 ? "city" : safeName;
    }
}
