using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

/// <summary>Which dictionary items to export; both null = all. Applied by the caller.</summary>
public sealed record ExportSelection(string? MissingIn, string? Group);

public static class DictionaryCsv
{
    private const string KeyHeader = "Key";

    public static byte[] Write(IReadOnlyList<DictionaryEntry> entries, IReadOnlyList<string> cultures)
    {
        var header = new List<string> { KeyHeader };
        header.AddRange(cultures);
        return CsvParser.Write(header, entries.Select(entry =>
        {
            var line = new List<string>(cultures.Count + 1) { Guard(entry.Key) };
            foreach (var culture in cultures)
            {
                line.Add(entry.Values.TryGetValue(culture, out var value) ? Guard(value) : string.Empty);
            }

            return (IReadOnlyList<string>)line;
        }));
    }

    public static ImportFile Read(Stream input, IReadOnlyCollection<string> installedCultures, long maxBytes = 10_000_000, int maxRows = 20_000)
    {
        string text;
        try
        {
            var bytes = ImportStreams.ReadBounded(input, maxBytes);
            if (bytes is null)
            {
                return ImportFile.Failed(ImportFileFormat.Csv, ImportFileError.TooLarge);
            }

            text = ImportStreams.Decode(bytes);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return ImportFile.Failed(ImportFileFormat.Csv, ImportFileError.Unreadable);
        }

        var records = CsvParser.Read(text);
        if (records.Count == 0)
        {
            return ImportFile.Failed(ImportFileFormat.Csv, ImportFileError.MissingKeyColumn);
        }

        var header = records[0];
        var keyIndex = Array.FindIndex(header, h => string.Equals(h.Trim(), KeyHeader, StringComparison.OrdinalIgnoreCase));
        if (keyIndex < 0)
        {
            return ImportFile.Failed(ImportFileFormat.Csv, ImportFileError.MissingKeyColumn);
        }

        var cultureColumns = new List<(int Index, string Culture)>();
        var unknown = new List<string>();
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (i == keyIndex || name.Length == 0)
            {
                continue;
            }

            var installed = installedCultures.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (installed is null || cultureColumns.Exists(c => c.Culture == installed))
            {
                unknown.Add(name);
            }
            else
            {
                cultureColumns.Add((i, installed));
            }
        }

        if (records.Count - 1 > maxRows)
        {
            return ImportFile.Failed(ImportFileFormat.Csv, ImportFileError.TooManyRows);
        }

        var rows = new List<ImportRow>(records.Count - 1);
        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (index, culture) in cultureColumns)
            {
                if (index < record.Length && !string.IsNullOrWhiteSpace(record[index]))
                {
                    values[culture] = Restore(record[index]);
                }
            }

            var key = keyIndex < record.Length ? Restore(record[keyIndex]).Trim() : string.Empty;
            rows.Add(new ImportRow(r + 1, key, values, null));
        }

        return new ImportFile(ImportFileFormat.Csv, rows, unknown, null, null);
    }

    // Spreadsheets run cells that start with these as formulas; dictionary values are editor input.
    private static bool IsTrigger(char c) => c is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static int AfterApostrophes(string value)
    {
        var i = 0;
        while (i < value.Length && value[i] == '\'')
        {
            i++;
        }

        return i;
    }

    /// <summary>Prefixes an apostrophe when the value (after any leading apostrophes) starts with a formula character, so <see cref="Restore"/> is exact.</summary>
    internal static string Guard(string value)
    {
        var i = AfterApostrophes(value);
        return i < value.Length && IsTrigger(value[i]) ? "'" + value : value;
    }

    internal static string Restore(string value)
    {
        var i = AfterApostrophes(value);
        return i > 0 && i < value.Length && IsTrigger(value[i]) ? value[1..] : value;
    }
}
