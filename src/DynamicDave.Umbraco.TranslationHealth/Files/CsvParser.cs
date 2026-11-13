using System.Text;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

/// <summary>CSV writing for the exports (reading is added with the import).</summary>
public static class CsvParser
{
    /// <summary>UTF-8 with BOM (so Excel opens it correctly), comma separated, CRLF line ends, RFC 4180 quoting.</summary>
    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, header);
        foreach (var row in rows)
        {
            AppendRecord(builder, row);
        }

        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var value = fields[i] ?? string.Empty;
            if (value.IndexOfAny(['"', ',', '\r', '\n']) >= 0)
            {
                builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        builder.Append("\r\n");
    }
}
