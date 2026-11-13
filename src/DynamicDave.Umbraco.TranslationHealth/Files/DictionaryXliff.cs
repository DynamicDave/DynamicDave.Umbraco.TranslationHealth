using System.Text;
using System.Xml;
using System.Xml.Linq;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

public static class DictionaryXliff
{
    // Real XLIFF nests a few levels (xliff/file/body/group/trans-unit/target/inline); anything deeper is refused.
    private const int MaxDepth = 32;

    private static readonly XNamespace Ns = "urn:oasis:names:tc:xliff:document:1.2";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    private sealed record RawUnit(string Key, string? Target, string? Source);

    public static byte[] Write(IReadOnlyList<DictionaryEntry> entries, string sourceCulture, string targetCulture)
    {
        var body = new XElement(Ns + "body");
        foreach (var entry in entries)
        {
            var unit = new XElement(Ns + "trans-unit", new XAttribute("id", entry.Id.ToString("N")), new XAttribute("resname", Clean(entry.Key)));
            entry.Values.TryGetValue(sourceCulture, out var source);
            unit.Add(Text("source", source ?? string.Empty));
            if (entry.Values.TryGetValue(targetCulture, out var target) && target.Length > 0)
            {
                unit.Add(Text("target", target));
            }

            body.Add(unit);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "xliff", new XAttribute("version", "1.2"),
                new XElement(Ns + "file",
                    new XAttribute("source-language", sourceCulture),
                    new XAttribute("target-language", targetCulture),
                    new XAttribute("datatype", "plaintext"),
                    new XAttribute("original", "umbraco-dictionary"),
                    body)));

        using var stream = new MemoryStream();
        // Entitize keeps a carriage return as &#xD; so line endings survive the round trip.
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    public static ImportFile Read(Stream input, IReadOnlyCollection<string> installedCultures, long maxBytes = 10_000_000, int maxRows = 20_000)
    {
        try
        {
            var bytes = ImportStreams.ReadBounded(input, maxBytes);
            if (bytes is null)
            {
                return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.TooLarge);
            }

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, settings);
            return Parse(reader, installedCultures, maxRows);
        }
        catch (Exception ex) when (ex is XmlException or IOException or NotSupportedException or ObjectDisposedException or InvalidOperationException)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }
    }

    // Streams the document with the reader (no element tree), so deeply nested input cannot overflow the stack.
    private static ImportFile Parse(XmlReader reader, IReadOnlyCollection<string> installedCultures, int maxRows)
    {
        var files = new List<(string Target, string Source)>();
        var units = new List<RawUnit>();
        var sawRoot = false;
        while (reader.Read())
        {
            CheckDepth(reader);
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Depth == 0)
            {
                if (reader.LocalName != "xliff" || reader.GetAttribute("version") != "1.2")
                {
                    return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
                }

                sawRoot = true;
            }
            else if (reader.LocalName == "file")
            {
                files.Add(((reader.GetAttribute("target-language") ?? string.Empty).Trim(), (reader.GetAttribute("source-language") ?? string.Empty).Trim()));
            }
            else if (reader.LocalName == "trans-unit")
            {
                var unit = ReadUnit(reader);
                if (unit is not null)
                {
                    units.Add(unit);
                    if (units.Count > maxRows)
                    {
                        return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.TooManyRows);
                    }
                }
            }
        }

        if (!sawRoot || files.Count == 0 || files.Select(f => f.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }

        var target = installedCultures.FirstOrDefault(c => string.Equals(c, files[0].Target, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.UnknownTargetLanguage);
        }

        var sourceName = files[0].Source;
        var sourceCulture = installedCultures.FirstOrDefault(c => string.Equals(c, sourceName, StringComparison.OrdinalIgnoreCase)) ?? sourceName;

        var rows = new List<ImportRow>(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(unit.Target))
            {
                values[target] = unit.Target;
            }

            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sourceName.Length > 0 && !string.IsNullOrWhiteSpace(unit.Source))
            {
                sources[sourceCulture] = unit.Source;
            }

            rows.Add(new ImportRow(i + 1, unit.Key, values, sources));
        }

        return new ImportFile(ImportFileFormat.Xliff, rows, [], target, null);
    }

    // Positioned on a trans-unit start element; leaves the reader on its end element.
    private static RawUnit? ReadUnit(XmlReader reader)
    {
        var resname = reader.GetAttribute("resname")?.Trim();
        var id = reader.GetAttribute("id")?.Trim();
        var key = !string.IsNullOrEmpty(resname) ? resname : !string.IsNullOrEmpty(id) ? "id:" + id : null;
        string? target = null;
        string? source = null;
        if (!reader.IsEmptyElement)
        {
            var depth = reader.Depth;
            while (reader.Read())
            {
                CheckDepth(reader);
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
                {
                    if (reader.LocalName == "target")
                    {
                        target ??= ReadText(reader);
                    }
                    else if (reader.LocalName == "source")
                    {
                        source ??= ReadText(reader);
                    }
                }
            }
        }

        return key is null ? null : new RawUnit(key, target, source);
    }

    // Concatenates the text of an element, flattening inline elements (g, x, bpt ...) to their text.
    private static string ReadText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = reader.Depth;
        var builder = new StringBuilder();
        while (reader.Read())
        {
            CheckDepth(reader);
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                builder.Append(reader.Value);
            }
        }

        return builder.ToString();
    }

    private static void CheckDepth(XmlReader reader)
    {
        if (reader.Depth > MaxDepth)
        {
            throw new XmlException("The XLIFF document is nested too deeply.");
        }
    }

    private static XElement Text(string name, string value) =>
        new(Ns + name, new XAttribute(XmlNs + "space", "preserve"), Clean(value));

    // XML 1.0 cannot carry control characters; drop them rather than fail the whole export. Surrogate pairs are kept.
    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (XmlConvert.IsXmlChar(c))
            {
                builder.Append(c);
            }
            else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
        }

        return builder.ToString();
    }
}
