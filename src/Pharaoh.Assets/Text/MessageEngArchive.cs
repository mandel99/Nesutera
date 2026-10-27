using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Pharaoh.Assets.Text;

public sealed record MessageEntry(
    int Id,
    short Type,
    short Subtype,
    short Data,
    short BoxX,
    short BoxY,
    short BoxWidth,
    short BoxHeight,
    short Picture1,
    short Picture1X,
    short Picture1Y,
    short Picture2,
    short Picture2X,
    short Picture2Y,
    short TitleX,
    short TitleY,
    short CaptionX,
    short CaptionY,
    short TextX,
    short TextY,
    short AnimationX,
    short AnimationY,
    int Delay,
    string Animation,
    string Sound,
    string Title,
    string Caption,
    string Text);

/// <summary>Reader for the original fixed-record Pharaoh_MM.eng message archive.</summary>
public sealed class MessageEngArchive
{
    private const int HeaderSize = 24;
    private const int RecordSize = 80;
    private const int ShortFieldCount = 21;

    private MessageEngArchive(string name, int usedEntryCount, IReadOnlyList<MessageEntry> entries)
    {
        Name = name;
        UsedEntryCount = usedEntryCount;
        Entries = entries;
    }

    public string Name { get; }
    public int UsedEntryCount { get; }
    public IReadOnlyList<MessageEntry> Entries { get; }

    public MessageEntry? GetEntry(int id) => (uint)id < (uint)Entries.Count ? Entries[id] : null;

    public static MessageEngArchive Load(string path, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path), encoding);
    }

    public static MessageEngArchive Parse(ReadOnlySpan<byte> bytes, Encoding? encoding = null)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("Message ENG is shorter than its header.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        encoding ??= Encoding.GetEncoding(1252);
        string name = DecodeNullTerminated(bytes[..16], encoding);
        int total = I32(bytes, 16);
        int used = I32(bytes, 20);
        if (total < 0 || used < 0 || used > total)
            throw new InvalidDataException($"Invalid message counts: total={total}, used={used}.");

        int textOffset = checked(HeaderSize + total * RecordSize);
        if (textOffset > bytes.Length)
            throw new InvalidDataException("Message record table extends beyond the file.");

        ReadOnlySpan<byte> textData = bytes[textOffset..];
        List<MessageEntry> entries = new(total);
        short[] fields = new short[ShortFieldCount];
        for (int id = 0; id < total; id++)
        {
            int position = HeaderSize + id * RecordSize;
            for (int index = 0; index < fields.Length; index++)
                fields[index] = I16(bytes, position + index * 2);

            // 21 shorts, 14 reserved bytes, then delay and five text offsets.
            int tail = position + 56;
            entries.Add(new MessageEntry(
                id, fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6],
                fields[7], fields[8], fields[9], fields[10], fields[11], fields[12], fields[13], fields[14],
                fields[15], fields[16], fields[17], fields[18], fields[19], fields[20],
                I32(bytes, tail),
                ReadString(textData, I32(bytes, tail + 4), encoding),
                ReadString(textData, I32(bytes, tail + 8), encoding),
                ReadString(textData, I32(bytes, tail + 12), encoding),
                ReadString(textData, I32(bytes, tail + 16), encoding),
                ReadString(textData, I32(bytes, tail + 20), encoding)));
        }

        return new MessageEngArchive(name, used, new ReadOnlyCollection<MessageEntry>(entries));
    }

    private static string ReadString(ReadOnlySpan<byte> data, int offset, Encoding encoding)
    {
        if (offset <= 0 || offset >= data.Length) return string.Empty;
        int length = data[offset..].IndexOf((byte)0);
        if (length < 0) length = data.Length - offset;
        return encoding.GetString(data.Slice(offset, length));
    }

    private static string DecodeNullTerminated(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        int terminator = bytes.IndexOf((byte)0);
        return encoding.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }

    private static short I16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static int I32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
}
