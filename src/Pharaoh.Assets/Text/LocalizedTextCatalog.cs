using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Pharaoh.Assets.Text;

/// <summary>Unicode catalog combining the original ENG data with optional UTF-8 translation or mod overrides.</summary>
public sealed class LocalizedTextCatalog
{
    private readonly TextEngArchive legacy;
    private readonly IReadOnlyDictionary<(int Group, int Index), string> overrides;

    private LocalizedTextCatalog(TextEngArchive legacy, IReadOnlyDictionary<(int, int), string> overrides)
    {
        this.legacy = legacy;
        this.overrides = overrides;
    }

    public static LocalizedTextCatalog Load(string engPath, Encoding legacyEncoding, string? utf8OverridePath = null)
    {
        TextEngArchive legacy = TextEngArchive.Load(engPath, legacyEncoding);
        IReadOnlyDictionary<(int, int), string> values = utf8OverridePath is not null && File.Exists(utf8OverridePath)
            ? ParseUtf8Overrides(File.ReadAllBytes(utf8OverridePath))
            : new ReadOnlyDictionary<(int, int), string>(new Dictionary<(int, int), string>());
        return new LocalizedTextCatalog(legacy, values);
    }

    public string? GetString(int group, int index) =>
        overrides.TryGetValue((group, index), out string? value) ? value : legacy.GetString(group, index);

    public static IReadOnlyDictionary<(int Group, int Index), string> ParseUtf8Overrides(ReadOnlySpan<byte> utf8Json)
    {
        Dictionary<(int, int), string> values = [];
        using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray());
        foreach (JsonProperty groupProperty in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(groupProperty.Name, out int group) || groupProperty.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Invalid UTF-8 text group '{groupProperty.Name}'.");
            foreach (JsonProperty textProperty in groupProperty.Value.EnumerateObject())
            {
                if (!int.TryParse(textProperty.Name, out int index) || textProperty.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"Invalid UTF-8 text index '{groupProperty.Name}:{textProperty.Name}'.");
                values[(group, index)] = (textProperty.Value.GetString() ?? string.Empty).Normalize(NormalizationForm.FormC);
            }
        }
        return new ReadOnlyDictionary<(int, int), string>(values);
    }
}
