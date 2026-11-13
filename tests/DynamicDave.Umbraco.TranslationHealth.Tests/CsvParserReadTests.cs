using DynamicDave.Umbraco.TranslationHealth.Files;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class CsvParserReadTests
{
    [Fact]
    public void Reads_dutch_excel_semicolon_export()
    {
        var text = "\uFEFFKey;nl;en\r\nHello;\"Hallo; wereld\";Hello\r\n\"a\"\"b\";x;y\r\n\r\n\r\n";

        var rows = CsvParser.Read(text);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Key", "nl", "en"], rows[0]);
        Assert.Equal(["Hello", "Hallo; wereld", "Hello"], rows[1]);
        Assert.Equal(["a\"b", "x", "y"], rows[2]);
    }

    [Fact]
    public void Detects_tab_and_keeps_multiline_quoted_fields()
    {
        var rows = CsvParser.Read("a\tb\n\"line1\nline2\"\tc");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["line1\nline2", "c"], rows[1]);
    }
}
