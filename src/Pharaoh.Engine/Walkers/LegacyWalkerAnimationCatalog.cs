using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Walkers;

public readonly record struct LegacyWalkerAnimation(
    string ArchiveName,
    int FirstImage,
    int FramesPerDirection,
    int DirectionCount);

public readonly record struct LegacyWalkerSpriteSheet(int BitmapId, string Name);

/// <summary>SG3 group mapping used by the first executable-verified walker.</summary>
public static class LegacyWalkerAnimationCatalog
{
    public const string MainArchive = "SprMain.sg3";
    public const int EmigrantWalkGroup = 2;
    public const int EmigrantDeathGroup = 3;
    public const int HomelessWalkGroup = 12;
    public const int HomelessDeathGroup = 13;
    public const int ImmigrantWalkGroup = 14;
    public const int ImmigrantDeathGroup = 15;
    public const int ArchitectWalkGroup = 4;
    public const int FiremanWalkGroup = 6;
    public const int PolicemanWalkGroup = 20;
    public const int FarmLaborerWalkGroup = 118;
    public const int DonkeyWalkGroup = 52;

    /// <summary>
    /// The complete bitmap-name inventory embedded in the original SprMain.sg3.
    /// Names and spelling are preserved verbatim; a sheet is not assigned to a
    /// WalkerType until its executable dispatch branch has also been verified.
    /// </summary>
    public static IReadOnlyList<LegacyWalkerSpriteSheet> SpriteSheets { get; } =
        new[]
        {
            "hunter_arrow", "hunter_arrow_shadow", "Emmigrant", "Engineer", "Fireman",
            "GranBoy", "hunter_stick", "Vagrant", "Immigrant", "MarketBuyer",
            "MarketTrader", "Vigile", "Croc", "Pharaoh", "Rioter", "Thief",
            "Transport", "ReedWorker", "Taxman", "Pusher", "Hunter_Ostrich", "Donkey",
            "Wall_Guy", "Teacher", "WaterCarrier", "Leg_Miss", "Leg_Aux", "Leg_Heavy",
            "Doctor", "Lumberjack", "Cart", "BrickWalker", "Leg_Horse", "Birds",
            "Peasant", "Trade_Boat", "DockPusher", "DanceWalker", "JugglerWalk", "Senet",
            "Fishing_Boat", "Ferry_Boat", "War_Boat", "Carpenter", "StoneWalker", "Ostrich",
            "Hyena", "sled", "Apothecary", "Dentist", "HunterBird", "Ptah", "Governor",
            "MusicWalker", "Seth", "Embalmer", "Osiris", "Scribe", "Librarian", "Diseased",
            "Walker", "Bast", "Ra", "Magistrate"
        }.Select((name, bitmapId) => new LegacyWalkerSpriteSheet(bitmapId, name)).ToArray();

    [OriginalAddress(0x0053C100, "FUN_0053c100")]
    public static LegacyWalkerAnimation ImmigrantWalk(Sg3Archive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.GroupImageIds.Count != 300)
            throw new InvalidDataException("SprMain does not contain Pharaoh's 300-entry group table.");
        return new LegacyWalkerAnimation(
            MainArchive, archive.GroupImageIds[ImmigrantWalkGroup], 12, 8);
    }

    [OriginalAddress(0x0053C100, "FUN_0053c100")]
    public static LegacyWalkerAnimation EmigrantWalk(Sg3Archive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.GroupImageIds.Count != 300)
            throw new InvalidDataException("SprMain does not contain Pharaoh's 300-entry group table.");
        return new LegacyWalkerAnimation(
            MainArchive, archive.GroupImageIds[EmigrantWalkGroup], 12, 8);
    }

    [OriginalAddress(0x0053C100, "FUN_0053c100")]
    public static LegacyWalkerAnimation HomelessWalk(Sg3Archive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.GroupImageIds.Count != 300)
            throw new InvalidDataException("SprMain does not contain Pharaoh's 300-entry group table.");
        return new LegacyWalkerAnimation(
            MainArchive, archive.GroupImageIds[HomelessWalkGroup], 12, 8);
    }

    [OriginalAddress(0x0048DED0, "FUN_0048ded0")]
    public static LegacyWalkerAnimation SafetyWalk(Sg3Archive archive,Walker walker)=>
        walker.Type==WalkerType.Fireman && walker.ActionState==Walkers.FireFightingAction
        ? new(MainArchive,archive.GroupImageIds[8],36,8):SafetyWalk(archive,walker.Type);

    public static LegacyWalkerAnimation SafetyWalk(Sg3Archive archive, WalkerType type)
    {
        int group = type switch
        {
            WalkerType.Architect => ArchitectWalkGroup,
            WalkerType.Fireman => FiremanWalkGroup,
            WalkerType.Policeman => PolicemanWalkGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        int frames = 12;
        return new LegacyWalkerAnimation(MainArchive, archive.GroupImageIds[group], frames, 8);
    }

    [OriginalAddress(0x0049BD40, "native tags 195..199 map to SprMain groups 156..160")]
    [OriginalAddress(0x004A2430, "native tags 84..90 map to SprMain groups 45..51")]
    [OriginalAddress(0x004A2E90, "hunter arrow graphics")]
    [OriginalAddress(0x0048BFF0, "recruiter tag245 resolves to SprMain group206")]
    [OriginalAddress(0x0053DFF0, "runtime tags are sorted and rebased across packs")]
    public static int HuntingImage(Sg3Archive archive, Walker walker, int orientation)
    {
        int direction = ((walker.Direction - orientation) % 8 + 8) % 8;
        if (walker.Type == WalkerType.CartPusher)
            return archive.GroupImageIds[43] + walker.AnimationFrame % 12 * 8 + direction;
        if (walker.Type == WalkerType.Recruiter)
            return archive.GroupImageIds[206] + walker.AnimationFrame % 12 * 8 + direction;
        if (walker.Type == WalkerType.Ostrich)
        {
            if (walker.ActionState == 24) return 0;
            if (walker.ActionState is 4 or 12)
                return archive.GroupImageIds[157] + Math.Min(walker.WaitTicks, 7);
            int group = walker.ActionState switch { 8 or 18 => 159, 14 or 15 or 19 => 160, _ => 156 };
            return archive.GroupImageIds[group] + walker.AnimationTick * 8 + direction;
        }
        if (walker.Type == WalkerType.HunterArrow)
            return archive.GroupImageIds[0] + ((walker.Direction - orientation * 4 + 32) % 32);
        if (walker.ActionState == 10)
            return archive.GroupImageIds[50] + Math.Clamp(walker.WaitTicks,0,18)*8 + direction;
        if (walker.ActionState == 4)
        {
            int[] deathFrames=[0,1,2,3,4,4,4,4,4,4,4,4,4,4,4,4,5,5,5,5];
            return archive.GroupImageIds[46]+deathFrames[Math.Clamp(walker.WaitTicks/2,0,deathFrames.Length-1)];
        }
        int hunterGroup=walker.ActionState switch {15=>47,12=>51,5=>48,_=>45};
        int frame=walker.ActionState==15?walker.WaitTicks%12:walker.AnimationFrame%12;
        return archive.GroupImageIds[hunterGroup]+frame*8+direction;
    }

    [OriginalAddress(0x0048CE30, "FUN_0048ce30")]
    [OriginalAddress(0x00493EC0, "FUN_00493ec0")]
    [OriginalAddress(0x00496140, "FUN_00496140")]
    [OriginalAddress(0x0049B400, "FUN_0049b400")]
    public static int DistributionImage(Sg3Archive archive, Walker walker, int orientation)
    {
        if (walker.IsHidden) return 0;
        int first = walker.Type switch
        {
            WalkerType.StorageTransporter => 2553,
            WalkerType.BazaarBuyer => 990,
            WalkerType.BazaarSeller => 1094,
            WalkerType.BazaarCarrier => 673,
            _ => throw new ArgumentOutOfRangeException(nameof(walker))
        };
        if (walker.ActionState == 4)
        {
            int phase = Math.Clamp(walker.WaitTicks / 2, 0, 63);
            int frame = phase < 4 ? phase : phase < 16 ? 4 : phase < 36 ? 5 : 6;
            return first + 96 + frame;
        }
        return first + walker.AnimationFrame % 12 * 8 + ((walker.Direction - orientation + 8) & 7);
    }

    [OriginalAddress(0x00438E50, "FUN_00438e50")]
    [OriginalAddress(0x0053C100, "FUN_0053c100")]
    public static int ImageFor(
        LegacyWalkerAnimation animation,
        int worldDirection,
        int animationFrame,
        int mapOrientation)
    {
        int direction = ((worldDirection - mapOrientation) % animation.DirectionCount +
            animation.DirectionCount) % animation.DirectionCount;
        int frame = Math.Clamp(animationFrame, 0, animation.FramesPerDirection - 1);

        // SprMain stores one frame for all eight directions before the next
        // frame: frame 0 directions 0..7, frame 1 directions 0..7, etc.
        return animation.FirstImage + frame * animation.DirectionCount + direction;
    }

    public static int ImageFor(
        LegacyWalkerAnimation animation,
        Walker walker,
        int mapOrientation) =>
        ImageFor(animation, walker.Direction, walker.AnimationFrame, mapOrientation);
}
