using System.Text;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

public enum ImportFileFormat { Csv, Xliff }

public enum ImportFileError { Unreadable, MissingKeyColumn, TooLarge, TooManyRows, UnknownTargetLanguage }

/// <summary>
/// One row of an import file. <see cref="RowNumber"/> is the 1-based record position in the file (the CSV header is record 1;
/// for XLIFF the 1-based trans-unit position). <see cref="Values"/> holds the non-empty cells per culture;
/// <see cref="Sources"/> holds the XLIFF source text per culture, otherwise null.
/// </summary>
public sealed record ImportRow(int RowNumber, string Key, IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string>? Sources);

public sealed record ImportFile(
    ImportFileFormat Format,
    IReadOnlyList<ImportRow> Rows,
    IReadOnlyList<string> UnknownColumns,
    string? TargetCulture,
    ImportFileError? Error)
{
    public static ImportFile Failed(ImportFileFormat format, ImportFileError error) => new(format, [], [], null, error);
}

internal static class ImportStreams
{
    /// <summary>Reads at most <paramref name="maxBytes"/> bytes; returns null when the stream is larger (the stream length is not trusted).</summary>
    public static byte[]? ReadBounded(Stream input, long maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var wanted = (int)Math.Min(chunk.Length, maxBytes + 1 - buffer.Length);
            var read = input.Read(chunk, 0, wanted);
            if (read <= 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    /// <summary>UTF-16 (BOM required), UTF-8 (BOM optional); invalid UTF-8 falls back to Windows-1252 (Excel "CSV (lijstscheidingsteken)" on Dutch Windows).</summary>
    public static string Decode(byte[] bytes)
    {
        // UTF-16 with BOM (Excel "Unicode Text").
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
        }
    }
}
