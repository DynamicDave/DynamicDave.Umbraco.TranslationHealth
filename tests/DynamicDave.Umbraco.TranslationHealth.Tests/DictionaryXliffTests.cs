using System.Text;
using System.Xml.Linq;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class DictionaryXliffTests
{
    private static readonly string[] Installed = ["en-US", "nl-NL"];

    private static DictionaryEntry Entry(string key, params (string Culture, string Value)[] values) =>
        new(Guid.NewGuid(), key, null, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));

    private static ImportFile Read(string xml) => DictionaryXliff.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Installed);

    private static string Xliff(string body, string target = "nl-NL") =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><xliff version=\"1.2\" xmlns=\"urn:oasis:names:tc:xliff:document:1.2\">" +
        $"<file source-language=\"en-US\" target-language=\"{target}\" datatype=\"plaintext\" original=\"x\"><body>{body}</body></file></xliff>";

    [Fact]
    public void Writes_source_and_existing_target()
    {
        var withTarget = Entry("Hello", ("en-US", "Hi"), ("nl-NL", "Hoi"));
        var withoutTarget = Entry("Bye", ("en-US", "Bye"));

        var xml = Encoding.UTF8.GetString(DictionaryXliff.Write([withTarget, withoutTarget], "en-US", "nl-NL"));
        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";

        var file = doc.Root!.Element(ns + "file")!;
        Assert.Equal("1.2", (string?)doc.Root.Attribute("version"));
        Assert.Equal("en-US", (string?)file.Attribute("source-language"));
        Assert.Equal("nl-NL", (string?)file.Attribute("target-language"));
        Assert.Equal("plaintext", (string?)file.Attribute("datatype"));
        Assert.Equal("umbraco-dictionary", (string?)file.Attribute("original"));
        var units = file.Descendants(ns + "trans-unit").ToList();
        Assert.Equal(2, units.Count);
        Assert.Equal(withTarget.Id.ToString("N"), (string?)units[0].Attribute("id"));
        Assert.Equal("Hello", (string?)units[0].Attribute("resname"));
        Assert.Equal("Hi", units[0].Element(ns + "source")!.Value);
        Assert.Equal("Hoi", units[0].Element(ns + "target")!.Value);
        Assert.Null(units[1].Element(ns + "target"));
    }

    [Fact]
    public void Xliff_round_trips_markup_and_placeholders()
    {
        var value = "Hello <b>{0}</b> & \"you\" \u00e9\u20ac";
        var bytes = DictionaryXliff.Write([Entry("k <1>", ("en-US", "Src"), ("nl-NL", value))], "en-US", "nl-NL");

        var file = DictionaryXliff.Read(new MemoryStream(bytes), Installed);

        Assert.Null(file.Error);
        Assert.Equal(ImportFileFormat.Xliff, file.Format);
        Assert.Equal("nl-NL", file.TargetCulture);
        var row = Assert.Single(file.Rows);
        Assert.Equal("k <1>", row.Key);
        Assert.Equal(value, row.Values["nl-NL"]);
        Assert.Equal("Src", row.Sources!["en-US"]);
    }

    [Fact]
    public void Multiline_values_round_trip_with_their_line_endings()
    {
        var value = "a\r\nb\nc\rd";
        var bytes = DictionaryXliff.Write([Entry("k", ("en-US", "x\r\ny"), ("nl-NL", value))], "en-US", "nl-NL");

        var row = Assert.Single(DictionaryXliff.Read(new MemoryStream(bytes), Installed).Rows);

        Assert.Equal(value, row.Values["nl-NL"]);
        Assert.Equal("x\r\ny", row.Sources!["en-US"]);
    }

    [Fact]
    public void Deeply_nested_input_is_unreadable_and_does_not_overflow_the_stack()
    {
        const int depth = 50_000;
        var xml = Xliff("<trans-unit id=\"1\" resname=\"k\"><source>a</source><target>" +
                        string.Concat(Enumerable.Repeat("<g id=\"1\">", depth)) + "x" + string.Concat(Enumerable.Repeat("</g>", depth)) +
                        "</target></trans-unit>");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var file = Read(xml);
        watch.Stop();

        Assert.Equal(ImportFileError.Unreadable, file.Error);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Reads_target_with_inline_elements_as_text()
    {
        var file = Read(Xliff("<trans-unit id=\"1\" resname=\"k\"><source>Hi there</source><target>Hi <g id=\"1\">there</g></target></trans-unit>"));

        Assert.Equal("Hi there", Assert.Single(file.Rows).Values["nl-NL"]);
    }

    [Fact]
    public void Falls_back_to_id_without_resname_and_skips_empty_targets()
    {
        var file = Read(Xliff("<trans-unit id=\"abc\"><source>a</source><target>x</target></trans-unit><trans-unit id=\"d\" resname=\"k\"><source>b</source><target> </target></trans-unit>"));

        Assert.Equal(["id:abc", "k"], file.Rows.Select(r => r.Key));
        Assert.Empty(file.Rows[1].Values);
    }

    [Fact]
    public void Target_language_is_matched_case_insensitively_to_the_installed_culture()
    {
        var file = Read(Xliff("<trans-unit id=\"1\" resname=\"k\"><source>a</source><target>b</target></trans-unit>", "NL-nl"));

        Assert.Equal("nl-NL", file.TargetCulture);
        Assert.True(file.Rows[0].Values.ContainsKey("nl-NL"));
    }

    [Fact]
    public void Unknown_target_language_is_an_error()
    {
        var file = Read(Xliff("<trans-unit id=\"1\" resname=\"k\"><source>a</source></trans-unit>", "fr-FR"));

        Assert.Equal(ImportFileError.UnknownTargetLanguage, file.Error);
    }

    [Fact]
    public void Files_with_different_target_languages_are_unreadable()
    {
        var xml = "<xliff version=\"1.2\"><file source-language=\"en-US\" target-language=\"nl-NL\"><body/></file><file source-language=\"en-US\" target-language=\"en-US\"><body/></file></xliff>";

        Assert.Equal(ImportFileError.Unreadable, Read(xml).Error);
    }

    [Fact]
    public void Not_xliff_1_2_is_unreadable()
    {
        Assert.Equal(ImportFileError.Unreadable, Read("<xliff version=\"2.0\"><file/></xliff>").Error);
        Assert.Equal(ImportFileError.Unreadable, Read("not xml at all").Error);
    }

    [Fact]
    public void Dtd_is_refused()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE xliff [<!ENTITY a \"aaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]>" +
                  "<xliff version=\"1.2\"><file source-language=\"en-US\" target-language=\"nl-NL\"><body><trans-unit id=\"1\" resname=\"k\"><source>&b;</source></trans-unit></body></file></xliff>";

        Assert.Equal(ImportFileError.Unreadable, Read(xml).Error);
    }

    [Fact]
    public void External_entity_is_refused()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE xliff [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]>" +
                  "<xliff version=\"1.2\"><file source-language=\"en-US\" target-language=\"nl-NL\"><body><trans-unit id=\"1\" resname=\"k\"><source>a</source><target>&x;</target></trans-unit></body></file></xliff>";

        var file = Read(xml);

        Assert.Equal(ImportFileError.Unreadable, file.Error);
        Assert.Empty(file.Rows);
    }

    [Fact]
    public void Too_many_rows_and_too_large_are_errors()
    {
        var units = string.Concat(Enumerable.Range(0, 3).Select(i => $"<trans-unit id=\"{i}\" resname=\"k{i}\"><source>a</source></trans-unit>"));
        var bytes = Encoding.UTF8.GetBytes(Xliff(units));

        Assert.Equal(ImportFileError.TooManyRows, DictionaryXliff.Read(new MemoryStream(bytes), Installed, maxRows: 2).Error);
        Assert.Equal(ImportFileError.TooLarge, DictionaryXliff.Read(new NonSeekableStream(bytes), Installed, maxBytes: 50).Error);
    }
}
