using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Pharaoh.Assets.Text;

public sealed record TextEngHeader(string Name, int GroupCount, int TotalStrings, int TotalWords);
public sealed record TextEngMatch(int GroupId, int Index, string Value);

public sealed class TextEngArchive
{
    private const int HeaderSize = 28;
    private const int IndexEntries = 1000;
    private const int IndexEntrySize = 8;
    private const int TextDataOffset = HeaderSize + (IndexEntries * IndexEntrySize);

    private TextEngArchive(TextEngHeader header, IReadOnlyDictionary<int, IReadOnlyList<string>> groups)
    {
        Header = header;
        Groups = groups;
    }

    public TextEngHeader Header { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<string>> Groups { get; }

    public static TextEngArchive Load(string path, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path), encoding);
    }

    public static TextEngArchive Parse(ReadOnlySpan<byte> bytes, Encoding? encoding = null)
    {
        if (bytes.Length < TextDataOffset)
            throw new InvalidDataException($"Text ENG is shorter than its {TextDataOffset}-byte header and index.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        encoding ??= Encoding.GetEncoding(1252);

        string name = DecodeNullTerminated(bytes[..16], encoding);
        TextEngHeader header = new(
            name,
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..20]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[20..24]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[24..28]));

        ReadOnlySpan<byte> textData = bytes[TextDataOffset..];
        List<(int Id, int Offset)> usedGroups = [];
        for (int id = 0; id < IndexEntries; id++)
        {
            int indexOffset = HeaderSize + (id * IndexEntrySize);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[indexOffset..(indexOffset + 4)]);
            int used = BinaryPrimitives.ReadInt32LittleEndian(bytes[(indexOffset + 4)..(indexOffset + 8)]);
            if (used == 0)
                continue;
            if ((uint)offset > (uint)textData.Length)
                throw new InvalidDataException($"Text ENG group {id} has invalid offset {offset}.");
            usedGroups.Add((id, offset));
        }

        Dictionary<int, IReadOnlyList<string>> groups = [];
        for (int index = 0; index < usedGroups.Count; index++)
        {
            (int id, int start) = usedGroups[index];
            int end = index + 1 < usedGroups.Count ? usedGroups[index + 1].Offset : textData.Length;
            if (end < start)
                throw new InvalidDataException($"Text ENG group {id} ends before it starts.");

            groups[id] = ParseStrings(textData[start..end], encoding);
        }

        return new TextEngArchive(header, new ReadOnlyDictionary<int, IReadOnlyList<string>>(groups));
    }

    public string? GetString(int groupId, int index)
    {
        if (index < 0 || !Groups.TryGetValue(groupId, out IReadOnlyList<string>? group) || index >= group.Count)
            return null;
        return group[index];
    }

    public IReadOnlyList<TextEngMatch> Search(string value, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        List<TextEngMatch> matches = [];
        foreach ((int groupId, IReadOnlyList<string> group) in Groups)
        {
            for (int index = 0; index < group.Count; index++)
            {
                if (group[index].Contains(value, comparison))
                    matches.Add(new TextEngMatch(groupId, index, group[index]));
            }
        }
        return matches.AsReadOnly();
    }

    private static IReadOnlyList<string> ParseStrings(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        List<string> values = [];
        int position = 0;
        while (position < bytes.Length)
        {
            int terminator = bytes[position..].IndexOf((byte)0);
            int length = terminator < 0 ? bytes.Length - position : terminator;
            values.Add(encoding.GetString(bytes.Slice(position, length)));
            if (terminator < 0)
                break;
            position += length + 1;
        }

        while (values.Count > 0 && values[^1].Length == 0)
            values.RemoveAt(values.Count - 1);
        return values.AsReadOnly();
    }

    private static string DecodeNullTerminated(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        int terminator = bytes.IndexOf((byte)0);
        return encoding.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }
}
