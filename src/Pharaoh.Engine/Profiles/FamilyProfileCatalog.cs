using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Profiles;

public sealed record FamilyProfile(string Name, string DataPath);

public static class FamilyProfileCatalog
{
    [OriginalAddress(0x00516B50, "FUN_00516b50")]
    public static IReadOnlyList<FamilyProfile> Load(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        string savePath = new LegacyGamePaths(installationRoot).SaveDirectory;
        if (!Directory.Exists(savePath))
            return Array.Empty<FamilyProfile>();

        return Directory.EnumerateFiles(savePath, "*.dat", SearchOption.TopDirectoryOnly)
            .Select(path => new FamilyProfile(Path.GetFileNameWithoutExtension(path), path))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
