using System.Security.Cryptography;
using System.Text;

namespace DynamicDave.Umbraco.TranslationHealth.Content;

/// <summary>One translatable property value of a culture.</summary>
public record PropertyText(string Alias, string Value);

/// <summary>Hashes the translatable text of one culture, so a real change of the source text can be told from a mere re-save.</summary>
public static class VariantFingerprint
{
    private const char FieldSeparator = '\u001F';
    private const char RecordSeparator = '\u001E';

    public static string Compute(string? name, IEnumerable<PropertyText> values)
    {
        var builder = new StringBuilder(name ?? string.Empty);
        builder.Append(RecordSeparator);

        foreach (var entry in values
                     .Where(v => !string.IsNullOrEmpty(v.Value))
                     .OrderBy(v => v.Alias, StringComparer.Ordinal))
        {
            builder.Append(entry.Alias).Append(FieldSeparator).Append(entry.Value).Append(RecordSeparator);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
