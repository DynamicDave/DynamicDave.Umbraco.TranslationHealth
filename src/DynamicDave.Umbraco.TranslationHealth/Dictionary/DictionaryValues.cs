namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

/// <summary>The one definition of when two dictionary values are the same: line endings are ignored and a whitespace-only value equals no value.</summary>
internal static class DictionaryValues
{
    public static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    public static bool SameValue(string? left, string? right)
    {
        var a = string.IsNullOrWhiteSpace(left) ? string.Empty : NormalizeLineEndings(left);
        var b = string.IsNullOrWhiteSpace(right) ? string.Empty : NormalizeLineEndings(right);
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>The value of a culture (case-insensitive), or null when it has none or only whitespace.</summary>
    public static string? NonEmpty(IReadOnlyDictionary<string, string> values, string culture)
    {
        foreach (var (name, value) in values)
        {
            if (string.Equals(name, culture, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
