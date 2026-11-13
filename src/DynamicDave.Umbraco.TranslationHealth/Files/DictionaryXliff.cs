using System.Text;
using System.Xml;
using System.Xml.Linq;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Files;

public static class DictionaryXliff
{
    private static readonly XNamespace Ns = "urn:oasis:names:tc:xliff:document:1.2";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

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
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
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
            XDocument document;
            using (var stream = new MemoryStream(bytes))
            using (var reader = XmlReader.Create(stream, settings))
            {
                document = XDocument.Load(reader);
            }

            return Parse(document, installedCultures, maxRows);
        }
        catch (Exception ex) when (ex is XmlException or IOException or NotSupportedException or ObjectDisposedException or InvalidOperationException)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }
    }

    private static ImportFile Parse(XDocument document, IReadOnlyCollection<string> installedCultures, int maxRows)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "xliff" || (string?)root.Attribute("version") != "1.2")
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }

        var files = root.Elements().Where(e => e.Name.LocalName == "file").ToList();
        if (files.Count == 0)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }

        var targets = files.Select(f => ((string?)f.Attribute("target-language"))?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count != 1)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.Unreadable);
        }

        var target = installedCultures.FirstOrDefault(c => string.Equals(c, targets[0], StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.UnknownTargetLanguage);
        }

        var sourceName = ((string?)files[0].Attribute("source-language"))?.Trim() ?? string.Empty;
        var sourceCulture = installedCultures.FirstOrDefault(c => string.Equals(c, sourceName, StringComparison.OrdinalIgnoreCase)) ?? sourceName;

        var units = files.SelectMany(f => f.Descendants().Where(e => e.Name.LocalName == "trans-unit")).ToList();
        if (units.Count > maxRows)
        {
            return ImportFile.Failed(ImportFileFormat.Xliff, ImportFileError.TooManyRows);
        }

        var rows = new List<ImportRow>(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            var resname = ((string?)unit.Attribute("resname"))?.Trim();
            var id = ((string?)unit.Attribute("id"))?.Trim();
            var key = !string.IsNullOrEmpty(resname) ? resname : !string.IsNullOrEmpty(id) ? "id:" + id : null;
            if (key is null)
            {
                continue;
            }

            // XElement.Value flattens inline elements (g, x, bpt ...) to their text.
            var targetText = unit.Elements().FirstOrDefault(e => e.Name.LocalName == "target")?.Value;
            var sourceText = unit.Elements().FirstOrDefault(e => e.Name.LocalName == "source")?.Value;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(targetText))
            {
                values[target] = targetText;
            }

            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sourceName.Length > 0 && !string.IsNullOrWhiteSpace(sourceText))
            {
                sources[sourceCulture] = sourceText;
            }

            rows.Add(new ImportRow(i + 1, key, values, sources));
        }

        return new ImportFile(ImportFileFormat.Xliff, rows, [], target, null);
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
