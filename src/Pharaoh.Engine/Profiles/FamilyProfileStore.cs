using Pharaoh.Engine.Installation;

namespace Pharaoh.Engine.Profiles;

/// <summary>Filesystem operations matching Pharaoh.exe's family-profile layout.</summary>
public static class FamilyProfileStore
{
    private const int CampaignRecordSize = 0x40;
    private const int CampaignRecordCountOffset = 0x1900;
    private const int CampaignCompletedOffset = 0x3c;

    public static FamilyProfile Create(string installationRoot, string name, string? templatePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LegacyGamePaths paths = new(installationRoot);
        LegacyGamePaths.ValidateName(name);
        paths.EnsureWritableDirectories(name);
        string saveRoot = paths.SaveDirectory;
        string dataPath = paths.ProfileDataPath(name);
        EnsureDirectChild(dataPath, dataPath, saveRoot);
        if (File.Exists(dataPath)) throw new IOException("Dynasty Exists");

        byte[] data = templatePath is not null && File.Exists(templatePath)
            ? File.ReadAllBytes(templatePath)
            : new byte[CampaignRecordCountOffset + sizeof(int)];
        if (data.Length >= CampaignRecordCountOffset + sizeof(int))
        {
            int count = Math.Clamp(BitConverter.ToInt32(data, CampaignRecordCountOffset), 0,
                CampaignRecordCountOffset / CampaignRecordSize);
            for (int index = 0; index < count; index++)
                data[index * CampaignRecordSize + CampaignCompletedOffset] = 0;
        }

        string temporaryPath = dataPath + ".tmp";
        File.WriteAllBytes(temporaryPath, data);
        File.Move(temporaryPath, dataPath);
        Directory.CreateDirectory(paths.FamilySaveDirectory(name));
        return new FamilyProfile(name, dataPath);
    }

    public static void Delete(string installationRoot, FamilyProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        ArgumentNullException.ThrowIfNull(profile);

        LegacyGamePaths paths = new(installationRoot);
        string saveRoot = paths.SaveDirectory;
        string dataPath = Path.GetFullPath(profile.DataPath);
        string expectedDataPath = paths.ProfileDataPath(profile.Name);
        EnsureDirectChild(dataPath, expectedDataPath, saveRoot);

        // FUN_00516DD0: delete Save\<family>.dat, then Save\<family>\*.sav,
        // and remove the family directory only when nothing else remains.
        File.Delete(dataPath);
        string familyDirectory = paths.FamilySaveDirectory(profile.Name);
        EnsureDirectChild(familyDirectory, familyDirectory, saveRoot);
        if (!Directory.Exists(familyDirectory)) return;

        foreach (string save in Directory.EnumerateFiles(familyDirectory, "*.sav", SearchOption.TopDirectoryOnly))
            File.Delete(save);
        if (!Directory.EnumerateFileSystemEntries(familyDirectory).Any())
            Directory.Delete(familyDirectory);
    }

    private static void EnsureDirectChild(string actual, string expected, string parent)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!actual.Equals(expected, comparison) ||
            !Path.GetDirectoryName(actual)!.Equals(parent, comparison))
            throw new InvalidOperationException("Family profile path is outside the installation Save directory.");
    }
}
