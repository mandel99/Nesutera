using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Pharaoh.Assets.Graphics;

public sealed record Sg3Header(
    uint DeclaredSg3Size,
    uint Version,
    int MaximumImageRecords,
    int ImageRecordCount,
    int BitmapRecordCount,
    int BitmapRecordCountWithoutSystem,
    uint TotalFileSize,
    uint Internal555Size,
    uint External555Size);

public sealed record Sg3BitmapRecord(
    int Id,
    string FileName,
    string Comment,
    uint Width,
    uint Height,
    uint ImageCount,
    uint StartIndex,
    uint EndIndex);

public sealed record Sg3ImageRecord(
    int Index,
    uint Offset,
    uint Length,
    uint UncompressedLength,
    int MirrorOffset,
    short Width,
    short Height,
    ushort GroupId,
    ushort GroupIndex,
    ushort SpriteCount,
    short SpriteOffsetX,
    short SpriteOffsetY,
    ushort Type,
    byte BitmapId,
    byte DataOffsetAdjustment,
    byte IsometricSize,
    bool CanReverse,
    bool IsFullyCompressed,
    bool IsExternal,
    bool HasIsometricTop,
    byte SpeedId,
    uint AlphaOffset,
    uint AlphaLength)
{
    public bool IsMirror => MirrorOffset < 0;
    public int? MirroredImageIndex => IsMirror && Index + MirrorOffset >= 0 ? Index + MirrorOffset : null;
}

public sealed class Sg3Archive
{
    public const int HeaderSize = 680;
    public const int BitmapRecordSize = 200;
    public const int MaximumBitmapRecords = 200;
    private const int BitmapTableSize = BitmapRecordSize * MaximumBitmapRecords;

    private Sg3Archive(
        Sg3Header header,
        IReadOnlyList<ushort> groupImageIds,
        IReadOnlyList<Sg3BitmapRecord> bitmaps,
        IReadOnlyList<Sg3ImageRecord> images)
    {
        Header = header;
        GroupImageIds = groupImageIds;
        Bitmaps = bitmaps;
        Images = images;
    }

    public Sg3Header Header { get; }
    /// <summary>
    /// Local image-index starts for the 300 legacy image groups stored at
    /// SG3 header offset 80. Pharaoh.exe uses these values when converting a
    /// terrain/building group plus frame offset into a global image id.
    /// </summary>
    public IReadOnlyList<ushort> GroupImageIds { get; }
    public IReadOnlyList<Sg3BitmapRecord> Bitmaps { get; }
    public IReadOnlyList<Sg3ImageRecord> Images { get; }

    public static Sg3Archive Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static Sg3Archive Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize + BitmapTableSize)
            throw new InvalidDataException("SG3 file is shorter than its header and bitmap table.");

        Sg3Header header = new(
            U32(bytes, 0), U32(bytes, 4), I32(bytes, 12), I32(bytes, 16), I32(bytes, 20), I32(bytes, 24),
            U32(bytes, 28), U32(bytes, 32), U32(bytes, 36));
        if (header.BitmapRecordCount is < 0 or > MaximumBitmapRecords)
            throw new InvalidDataException($"Invalid SG3 bitmap count {header.BitmapRecordCount}.");
        if (header.ImageRecordCount < 0 || header.ImageRecordCount > header.MaximumImageRecords)
            throw new InvalidDataException($"Invalid SG3 image count {header.ImageRecordCount}.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = Encoding.GetEncoding(1252);
        ushort[] groupImageIds = new ushort[300];
        for (int group = 0; group < groupImageIds.Length; group++)
            groupImageIds[group] = U16(bytes, 80 + (group * sizeof(ushort)));

        List<Sg3BitmapRecord> bitmaps = new(header.BitmapRecordCount);
        for (int id = 0; id < header.BitmapRecordCount; id++)
        {
            int offset = HeaderSize + (id * BitmapRecordSize);
            bitmaps.Add(new Sg3BitmapRecord(
                id,
                CString(bytes.Slice(offset, 65), encoding),
                CString(bytes.Slice(offset + 65, 51), encoding),
                U32(bytes, offset + 116), U32(bytes, offset + 120), U32(bytes, offset + 124),
                U32(bytes, offset + 128), U32(bytes, offset + 132)));
        }

        bool hasAlpha = header.Version >= 0xD6;
        int imageRecordSize = hasAlpha ? 72 : 64;
        int imageTableOffset = HeaderSize + BitmapTableSize;
        int storedImageCount = checked(header.ImageRecordCount + 1);
        if ((long)imageTableOffset + ((long)storedImageCount * imageRecordSize) > bytes.Length)
            throw new InvalidDataException("SG3 image table extends beyond the file.");

        List<Sg3ImageRecord> images = new(storedImageCount);
        for (int index = 0; index < storedImageCount; index++)
        {
            int offset = imageTableOffset + (index * imageRecordSize);
            images.Add(new Sg3ImageRecord(
                index, U32(bytes, offset), U32(bytes, offset + 4), U32(bytes, offset + 8), I32(bytes, offset + 16),
                I16(bytes, offset + 20), I16(bytes, offset + 22), U16(bytes, offset + 24), U16(bytes, offset + 26),
                U16(bytes, offset + 30), I16(bytes, offset + 34), I16(bytes, offset + 36), U16(bytes, offset + 50),
                bytes[offset + 56], bytes[offset + 52], bytes[offset + 55], bytes[offset + 48] != 0, bytes[offset + 51] != 0, bytes[offset + 52] != 0,
                bytes[offset + 53] != 0, bytes[offset + 58], hasAlpha ? U32(bytes, offset + 64) : 0,
                hasAlpha ? U32(bytes, offset + 68) : 0));
        }

        return new Sg3Archive(
            header,
            Array.AsReadOnly(groupImageIds),
            new ReadOnlyCollection<Sg3BitmapRecord>(bitmaps),
            new ReadOnlyCollection<Sg3ImageRecord>(images));
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static short I16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static int I32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static string CString(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        int terminator = bytes.IndexOf((byte)0);
        return encoding.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }
}
