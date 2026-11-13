using System.Text;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

/// <summary>CSV writing for the exports and reading for the imports.</summary>
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

    private static readonly char[] Delimiters = [',', ';', '\t'];

    /// <summary>
    /// Parses CSV text (RFC 4180). The delimiter is the one of comma, semicolon or tab with the most occurrences outside quotes
    /// in the first non-empty line; a leading BOM is dropped; records whose fields are all empty or whitespace are ignored.
    /// </summary>
    public static IReadOnlyList<string[]> Read(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var delimiter = DetectDelimiter(text);
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var pending = false;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            pending = false;
            if (!fields.TrueForAll(string.IsNullOrWhiteSpace))
            {
                records.Add([.. fields]);
            }

            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                pending = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                pending = true;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(c);
                pending = true;
            }
        }

        if (pending)
        {
            EndRecord();
        }

        return records;
    }

    private static char DetectDelimiter(string text)
    {
        var counts = new int[Delimiters.Length];
        var inQuotes = false;
        var seenContent = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                seenContent = true;
            }
            else if (!inQuotes && (c == '\n' || c == '\r'))
            {
                if (seenContent)
                {
                    break;
                }
            }
            else
            {
                seenContent = true;
                if (!inQuotes)
                {
                    var index = Array.IndexOf(Delimiters, c);
                    if (index >= 0)
                    {
                        counts[index]++;
                    }
                }
            }
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        return Delimiters[best];
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
