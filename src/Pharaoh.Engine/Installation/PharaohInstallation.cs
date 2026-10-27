using System.Security.Cryptography;

namespace Pharaoh.Engine.Installation;

public sealed record InstallationFile(string RelativePath, long Length, string Sha256);

public sealed record PharaohInstallation(
    string RootPath,
    InstallationFile Executable,
    IReadOnlyList<string> MissingRequiredFiles)
{
    private static readonly string[] RequiredFiles =
    [
        "Pharaoh.exe",
        "Pharaoh_Text.eng",
        "Pharaoh_MM.eng",
        "mission1.pak"
    ];

    public bool IsUsable => MissingRequiredFiles.Count == 0;

    public static PharaohInstallation Inspect(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Pharaoh installation not found: {root}");

        string exePath = Path.Combine(root, "Pharaoh.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Pharaoh.exe was not found.", exePath);

        FileInfo exe = new(exePath);
        string hash;
        using (FileStream stream = exe.OpenRead())
            hash = Convert.ToHexString(SHA256.HashData(stream));

        string[] missing = RequiredFiles
            .Where(relative => !File.Exists(Path.Combine(root, relative)))
            .ToArray();

        return new PharaohInstallation(
            root,
            new InstallationFile("Pharaoh.exe", exe.Length, hash),
            missing);
    }
}
