using System.Text;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class DictionaryCsvTests
{
    private static readonly string[] Installed = ["en-US", "nl-NL"];

    private static DictionaryEntry Entry(string key, params (string Culture, string Value)[] values) =>
        new(Guid.NewGuid(), key, null, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));

    private static ImportFile Read(byte[] bytes) => DictionaryCsv.Read(new MemoryStream(bytes), Installed);

    private static ImportFile Read(string text, int maxRows = 20_000) =>
        DictionaryCsv.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), Installed, maxRows: maxRows);

    [Fact]
    public void Writes_key_and_one_column_per_culture()
    {
        var bytes = DictionaryCsv.Write([Entry("Hello", ("en-US", "Hi"), ("nl-NL", "Hoi")), Entry("Bye", ("en-US", "Bye"))], ["en-US", "nl-NL"]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Key,en-US,nl-NL\r\nHello,Hi,Hoi\r\nBye,Bye,\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void Csv_round_trips_markup_quotes_and_newlines()
    {
        var markup = "Hello <b>{0}</b> & \"you\"";
        var twoLines = "first\r\nsecond";
        var file = Read(DictionaryCsv.Write([Entry("k", ("en-US", markup), ("nl-NL", twoLines))], ["en-US", "nl-NL"]));

        Assert.Null(file.Error);
        var row = Assert.Single(file.Rows);
        Assert.Equal("k", row.Key);
        Assert.Equal(markup, row.Values["en-US"]);
        Assert.Equal(twoLines, row.Values["nl-NL"]);
        Assert.Null(row.Sources);
    }

    [Fact]
    public void Formula_like_values_are_neutralized_and_restored()
    {
        string[] originals = ["=SUM(1)", "+31 6 1234", "-", "@home", "'=already", "'plain"];
        var entries = originals.Select((v, i) => Entry("k" + i, ("en-US", v))).ToList();

        var bytes = DictionaryCsv.Write(entries, ["en-US"]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("'=SUM(1)", text);
        Assert.Contains("'+31 6 1234", text);
        Assert.Contains("'-", text);
        Assert.Contains("'@home", text);

        var file = Read(bytes);
        Assert.Equal(originals, file.Rows.Select(r => r.Values["en-US"]));
    }

    [Fact]
    public void Missing_key_column_is_an_error()
    {
        var file = Read("en-US,nl-NL\r\nHi,Hoi\r\n");

        Assert.Equal(ImportFileError.MissingKeyColumn, file.Error);
        Assert.Empty(file.Rows);
    }

    [Fact]
    public void Unknown_columns_are_listed()
    {
        var file = Read("KEY;NL-nl;fr-FR;Notes;en-us;EN-US\r\nk;Hoi;Salut;n;Hi;Again\r\n");

        Assert.Null(file.Error);
        Assert.Equal(["fr-FR", "Notes", "EN-US"], file.UnknownColumns);
        var row = Assert.Single(file.Rows);
        Assert.Equal("Hoi", row.Values["nl-NL"]);
        Assert.Equal("Hi", row.Values["en-US"]);
        Assert.Equal(2, row.Values.Count);
    }

    [Fact]
    public void Empty_cells_are_not_values()
    {
        var file = Read("Key,en-US,nl-NL\r\nk,  ,\r\nshort\r\n");

        Assert.Equal(2, file.Rows.Count);
        Assert.All(file.Rows, r => Assert.Empty(r.Values));
        Assert.Equal([2, 3], file.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Duplicate_keys_are_kept_as_separate_rows()
    {
        Assert.Equal(2, Read("Key,en-US\r\nk,a\r\nk,b\r\n").Rows.Count);
    }

    [Fact]
    public void Too_many_rows_is_an_error()
    {
        Assert.Equal(ImportFileError.TooManyRows, Read("Key,en-US\r\na,1\r\nb,2\r\nc,3\r\n", maxRows: 2).Error);
        Assert.Equal(2, Read("Key,en-US\r\na,1\r\nb,2\r\n", maxRows: 2).Rows.Count);
    }

    [Fact]
    public void Too_large_is_an_error_without_trusting_stream_length()
    {
        var bytes = Encoding.UTF8.GetBytes("Key,en-US\r\n" + new string('x', 200));

        Assert.Equal(ImportFileError.TooLarge, DictionaryCsv.Read(new NonSeekableStream(bytes), Installed, maxBytes: 100).Error);
        Assert.Null(DictionaryCsv.Read(new NonSeekableStream(bytes), Installed, maxBytes: bytes.Length).Error);
    }

    [Fact]
    public void Reads_windows_1252_without_bom()
    {
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes("Key;nl-NL\r\nk;café €5\r\n");

        var file = Read(bytes);

        Assert.Null(file.Error);
        Assert.Equal("café €5", Assert.Single(file.Rows).Values["nl-NL"]);
    }

    [Fact]
    public void Reads_utf8_without_bom()
    {
        var file = Read("Key;nl-NL\r\nk;café €\r\n");

        Assert.Equal("café €", Assert.Single(file.Rows).Values["nl-NL"]);
    }
}

internal sealed class NonSeekableStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
