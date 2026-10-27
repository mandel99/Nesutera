using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public readonly record struct LegacyHelpToken(string Text, int Link = -1, char Command = '\0', int Graphic = 0);

public static class LegacyHelpContent
{
    [OriginalAddress(0x004C8480, "FUN_004c8480")]
    [OriginalAddress(0x004C8590, "FUN_004c8590")]
    public static IReadOnlyList<LegacyHelpToken> Parse(string source)
    {
        List<LegacyHelpToken> result = [];
        int i = 0;
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            int link = -1;
            if (source[i] == '@' && i + 1 < source.Length)
            {
                char command = source[++i];
                if (command is 'P' or 'L') { result.Add(new("", Command: command)); i++; continue; }
                bool graphic = command == 'G';
                if (graphic) i++;
                int start = i;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                if (i > start && int.TryParse(source.AsSpan(start, i - start), out int value))
                {
                    if (graphic) { result.Add(new("", Command: 'G', Graphic: value)); continue; }
                    link = value;
                }
            }
            int textStart = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '@') i++;
            if (i > textStart) result.Add(new(source[textStart..i].Replace('&', ' '), link));
            else i++;
        }
        return result;
    }
}
