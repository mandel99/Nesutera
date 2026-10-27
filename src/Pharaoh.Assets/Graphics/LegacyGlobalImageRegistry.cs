namespace Pharaoh.Assets.Graphics;

/// <summary>
/// Identifies one image in the flattened global table built by Pharaoh.exe.
/// </summary>
public sealed record LegacyGlobalImageLocation(
    int GlobalImageId,
    string ArchiveName,
    int LocalImageIndex,
    Sg3ImageRecord Image);

/// <summary>
/// Resolves the global image ids stored in MAP/SAV image_grid chunks.
///
/// Pharaoh.exe FUN_0053DD90/FUN_0053DFF0 appends 64-byte SG3 records to one
/// global table. For packs containing system.bmp it omits the first 200
/// records. The bases below are limited to the two city-map packs for which
/// both that loader arithmetic and the installed SG3 boundaries have been
/// verified. The GlobalBase is the value added to the original local SG3
/// index; it may therefore precede the first copied global slot by 200.
/// </summary>
public sealed class LegacyGlobalImageRegistry
{
    public const int MaximumExecutableImageId = 0x639b;

    private static readonly PackDefinition[] VerifiedMapPacks =
    [
        // Startup's first pack retains global slot 200. The following packs
        // have no SYSTEM prefix; their exact counts lead to General slot 11906.
        new("Pharaoh_Unloaded.sg3", GlobalBase: 0, FirstLocalImage: 200),
        new("SprMain.sg3", GlobalBase: 682, FirstLocalImage: 0),
        new("Hyksos.sg3", GlobalBase: 11007, FirstLocalImage: 0),
        // EXE global 11928 resolves to Pharaoh_General local 222.
        new("Pharaoh_General.sg3", GlobalBase: 11706, FirstLocalImage: 200),
        // After General's 200 stripped SYSTEM.BMP records, Terrain starts at
        // the next global slot; local 201 therefore resolves as global 14453.
        new("Pharaoh_Terrain.sg3", GlobalBase: 14252, FirstLocalImage: 200),

        // These packs occupy the contiguous slots between Terrain and
        // SprAmbient in the startup order at FUN_00414420. Each SYSTEM.BMP
        // pack contributes Header.ImageRecordCount - 200 records.
        new("SPHINX1A.sg3", GlobalBase: 15567, FirstLocalImage: 200),
        new("SPHINX1B.sg3", GlobalBase: 15569, FirstLocalImage: 200),
        new("SPHINX1C.sg3", GlobalBase: 15571, FirstLocalImage: 200),
        new("SPHINX1D.sg3", GlobalBase: 15573, FirstLocalImage: 200),
        new("SPHINX_EXTRA.sg3", GlobalBase: 15575, FirstLocalImage: 200),
        new("TEMPLE_NILE.sg3", GlobalBase: 15591, FirstLocalImage: 200),
        new("empire.sg3", GlobalBase: 15629, FirstLocalImage: 200),

        // SprAmbient has no SYSTEM.BMP prefix and is copied from local zero.
        new("SprAmbient.sg3", GlobalBase: 15830, FirstLocalImage: 0),

        // Startup continues with Fonts (SYSTEM stripped), Egyptian and
        // SprMain2 before Expansion. Expansion group 33 starts at local 741,
        // hence its cliff-rock base is global 23035 + 741 = 23776.
        new("Expansion.sg3", GlobalBase: 23035, FirstLocalImage: 200)
    ];

    private readonly Pack[] packs;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int,LegacyGlobalImageLocation> resolved=[];

    public LegacyGlobalImageRegistry(string installationRoot)
        : this(installationRoot, loadArchive: null)
    {
    }

    internal LegacyGlobalImageRegistry(
        string installationRoot,
        Func<string, Sg3Archive>? loadArchive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        string dataDirectory = Path.Combine(Path.GetFullPath(installationRoot), "Data");
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Pharaoh data directory was not found: {dataDirectory}");

        packs = VerifiedMapPacks.Select(definition =>
        {
            string path = Path.Combine(dataDirectory, definition.ArchiveName);
            return new Pack(definition, loadArchive?.Invoke(definition.ArchiveName) ?? Sg3Archive.Load(path));
        }).ToArray();
    }

    public LegacyGlobalImageLocation Resolve(int globalImageId)
    {
        if (!TryResolve(globalImageId, out LegacyGlobalImageLocation? location))
            throw new KeyNotFoundException(
                $"Global image {globalImageId} is not in an EXE-verified city-map pack.");
        return location!;
    }

    public bool TryResolve(int globalImageId, out LegacyGlobalImageLocation? location)
    {
        if(resolved.TryGetValue(globalImageId,out location))return true;
        location = null;
        if (globalImageId is < 1 or > MaximumExecutableImageId)
            return false;

        foreach (Pack pack in packs)
        {
            int localIndex = globalImageId - pack.Definition.GlobalBase;
            if (localIndex < pack.Definition.FirstLocalImage ||
                localIndex >= pack.Archive.Header.ImageRecordCount)
                continue;

            LegacyGlobalImageLocation candidate = new(
                globalImageId,
                pack.Definition.ArchiveName,
                localIndex,
                pack.Archive.Images[localIndex]);
            if (location is not null)
                throw new InvalidDataException(
                    $"Global image {globalImageId} resolves to multiple SG3 records.");
            location = candidate;
        }

        if(location is not null)resolved.TryAdd(globalImageId,location);
        return location is not null;
    }

    private sealed record PackDefinition(string ArchiveName, int GlobalBase, int FirstLocalImage);
    private sealed record Pack(PackDefinition Definition, Sg3Archive Archive);
}
