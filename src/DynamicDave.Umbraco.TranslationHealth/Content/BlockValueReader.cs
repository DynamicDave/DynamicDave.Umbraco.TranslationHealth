using System.Globalization;
using System.Text.Json;

namespace DynamicDave.Umbraco.TranslationHealth.Content;

/// <summary>
/// Extracts the values of one culture from stored block JSON (Block List, Block Grid, rich text with blocks).
/// Only <c>contentData</c> / <c>settingsData</c> are read; <c>layout</c> and <c>expose</c> are ignored on purpose.
/// </summary>
public static class BlockValueReader
{
    private const int MaxDepth = 16;

    public static IReadOnlyList<(string Path, string Value)> CultureValues(string? json, string culture)
    {
        var result = new List<(string Path, string Value)>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            ReadBlockValue(document.RootElement, culture, result, 0);
        }
        catch (JsonException)
        {
            // Not JSON: no block values.
        }

        return result;
    }

    // Reads an object that is a block value itself (contentData/settingsData) or a rich text value (blocks.*).
    private static bool ReadBlockValue(JsonElement element, string culture, List<(string, string)> result, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > MaxDepth)
        {
            return false;
        }

        if (element.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Object)
        {
            ReadBlockValue(blocks, culture, result, depth + 1);
            return true;
        }

        var found = false;
        foreach (var listName in new[] { "contentData", "settingsData" })
        {
            if (element.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                found = true;
                foreach (var block in list.EnumerateArray())
                {
                    ReadBlock(block, culture, result, depth);
                }
            }
        }

        return found;
    }

    private static void ReadBlock(JsonElement block, string culture, List<(string, string)> result, int depth)
    {
        if (block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var key = block.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.String
            ? keyElement.GetString()
            : null;

        foreach (var entry in values.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("value", out var value))
            {
                continue;
            }

            // Invariant entries carry no text of their own, but may hold nested blocks with culture values.
            var entryCulture = entry.TryGetProperty("culture", out var cultureElement) && cultureElement.ValueKind == JsonValueKind.String
                ? cultureElement.GetString()
                : null;
            var matches = entryCulture is not null && string.Equals(entryCulture, culture, StringComparison.OrdinalIgnoreCase);
            if (entryCulture is not null && !matches)
            {
                continue;
            }

            var alias = entry.TryGetProperty("alias", out var aliasElement) && aliasElement.ValueKind == JsonValueKind.String
                ? aliasElement.GetString() ?? string.Empty
                : string.Empty;

            AddValue(value, $"{key}/{alias}", culture, matches, result, depth);
        }
    }

    private static void AddValue(JsonElement value, string path, string culture, bool emit, List<(string, string)> result, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                if (TryReadNested(text, path, culture, emit, result, depth))
                {
                    return;
                }

                if (emit)
                {
                    result.Add((path, text));
                }

                break;
            case JsonValueKind.Object:
                // A nested block value written as an object; anything else is not translatable text.
                if (!TryReadRichText(value, path, culture, emit, result, depth + 1))
                {
                    ReadBlockValue(value, culture, result, depth + 1);
                }

                break;
            case JsonValueKind.Number:
                if (emit) result.Add((path, value.TryGetDecimal(out var number) ? number.ToString(CultureInfo.InvariantCulture) : value.GetRawText()));
                break;
            case JsonValueKind.True:
                if (emit) result.Add((path, "true"));
                break;
            case JsonValueKind.False:
                if (emit) result.Add((path, "false"));
                break;
        }
    }

    // A string that parses to a block value is the double-encoded form Umbraco writes for nested blocks.
    private static bool TryReadNested(string text, string path, string culture, bool emit, List<(string, string)> result, int depth)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] != '{' || depth > MaxDepth)
        {
            return false;
        }

        try
        {
            using var nested = JsonDocument.Parse(text);
            var inner = new List<(string, string)>();
            if (!TryReadRichText(nested.RootElement, path, culture, emit, inner, depth + 1)
                && !ReadBlockValue(nested.RootElement, culture, inner, depth + 1))
            {
                return false;
            }

            result.AddRange(inner);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A rich text value inside a block: emit its markup (when the entry matches the culture), then read its blocks.
    private static bool TryReadRichText(JsonElement element, string path, string culture, bool emit, List<(string, string)> result, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object
            || depth > MaxDepth
            || !element.TryGetProperty("markup", out var markup)
            || markup.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = markup.GetString();
        if (emit && !string.IsNullOrEmpty(text))
        {
            result.Add((path, text));
        }

        if (element.TryGetProperty("blocks", out var blocks))
        {
            ReadBlockValue(blocks, culture, result, depth + 1);
        }

        return true;
    }
}
