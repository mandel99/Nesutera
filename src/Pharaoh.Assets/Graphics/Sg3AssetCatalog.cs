namespace Pharaoh.Assets.Graphics;

/// <summary>
/// Reuses SG3 metadata and its backing 555 data while a screen set is being built.
/// The original executable registers its asset packs once during startup; this
/// avoids reparsing and rereading the same pair for every requested sprite.
/// </summary>
public sealed class Sg3AssetCatalog
{
    private readonly string dataDirectory;
    private readonly Dictionary<string, Sg3Archive> archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> installedFiles;
    private readonly Lazy<LegacyGlobalImageRegistry> globalImages;

    public Sg3AssetCatalog(string installationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        dataDirectory = Path.Combine(Path.GetFullPath(installationRoot), "Data");
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Pharaoh data directory was not found: {dataDirectory}");

        installedFiles = Directory.EnumerateFiles(dataDirectory)
            .ToDictionary(path => Path.GetFileName(path)!, Path.GetFullPath, StringComparer.OrdinalIgnoreCase);
        // Pharaoh.exe builds the flattened global image table as the startup
        // pack registry advances.  Do not parse all map archives merely by
        // constructing the catalog; the table is first needed by the city.
        globalImages = new Lazy<LegacyGlobalImageRegistry>(
            () => new LegacyGlobalImageRegistry(installationRoot, LoadArchive),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public LegacyGlobalImageRegistry GlobalImages => globalImages.Value;

    /// <summary>Registers one legacy pack slot without touching its 555 pixels.</summary>
    public bool RegisterArchive(string packName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packName);
        string sg3Name = $"{packName}.sg3";
        string pixelName = $"{packName}.555";
        if (!installedFiles.ContainsKey(sg3Name) || !installedFiles.ContainsKey(pixelName))
            return false;
        _ = GetArchive(Resolve(sg3Name));
        return true;
    }

    /// <summary>
    /// Registers the original startup pack list by loading only SG3 metadata.
    /// Pixel data remains lazy, matching the executable's registry semantics.
    /// Duplicate legacy slots (notably LibMainDummy) share one archive instance.
    /// </summary>
    public int RegisterArchives(IEnumerable<string> packNames)
    {
        ArgumentNullException.ThrowIfNull(packNames);
        foreach (string packName in packNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = RegisterArchive(packName);
        }
        return archives.Count;
    }

    public int RegisteredArchiveCount => archives.Count;

    public RgbaImage Decode(string archiveName, int imageIndex)
    {
        string sg3Path = Resolve(archiveName);
        Sg3Archive archive = GetArchive(sg3Path);
        return Sg3ImageDecoder.Decode(sg3Path, archive, imageIndex, ReadSource);
    }

    public RgbaImage DecodeGlobal(int globalImageId)
    {
        LegacyGlobalImageLocation location = GlobalImages.Resolve(globalImageId);
        return Decode(location.ArchiveName, location.LocalImageIndex);
    }

    public Sg3IsometricLayers DecodeGlobalIsometricLayers(int globalImageId)
    {
        LegacyGlobalImageLocation location = GlobalImages.Resolve(globalImageId);
        string sg3Path = Resolve(location.ArchiveName);
        Sg3Archive archive = GetArchive(sg3Path);
        return Sg3ImageDecoder.DecodeIsometricLayers(
            sg3Path, archive, location.LocalImageIndex, ReadSource);
    }

    public Sg3Archive LoadArchive(string archiveName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        return GetArchive(Resolve(archiveName));
    }

    private Sg3Archive GetArchive(string sg3Path)
    {
        if (archives.TryGetValue(sg3Path, out Sg3Archive? archive)) return archive;
        archive = Sg3Archive.Load(sg3Path);
        archives.Add(sg3Path, archive);
        return archive;
    }

    private byte[] ReadSource(string path)
    {
        string resolved = Resolve(Path.GetFileName(path));
        if (!sources.TryGetValue(resolved, out byte[]? bytes))
        {
            bytes = File.ReadAllBytes(resolved);
            sources.Add(resolved, bytes);
        }
        return bytes;
    }

    private string Resolve(string fileName)
    {
        if (installedFiles.TryGetValue(fileName, out string? path)) return path;
        throw new FileNotFoundException($"Pharaoh asset was not found in {dataDirectory}.", Path.Combine(dataDirectory, fileName));
    }
}
