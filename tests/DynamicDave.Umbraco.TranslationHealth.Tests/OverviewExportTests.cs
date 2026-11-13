using System.Text;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Xunit;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class OverviewExportTests
{
    private static OverviewRow Row(string name, Guid key, params VariantRow[] variants) =>
        new(new DocumentRow(key, 1, null, "-1,1", "0001", 1, "page", Guid.Empty, name), variants);

    private static VariantRow Variant(Guid key, string culture, CellStatus status, bool outdated = false, TrackingMode mode = TrackingMode.Fingerprint, DateTime? since = null) =>
        new(key, culture, status, outdated, mode, null, null, null, null, null, null, null, null, since);

    private static string[] Lines(byte[] csv)
    {
        Assert.Equal([0xEF, 0xBB, 0xBF], csv.Take(3).ToArray());
        return Encoding.UTF8.GetString(csv, 3, csv.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Export_writes_one_column_per_culture()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = new[]
        {
            Row("Home, sweet", a,
                Variant(a, "en-US", CellStatus.Published, mode: TrackingMode.Date),
                Variant(a, "nl-NL", CellStatus.PendingChanges, outdated: true, since: new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc))),
            Row("About", b,
                Variant(b, "en-US", CellStatus.Published, mode: TrackingMode.Date),
                Variant(b, "nl-NL", CellStatus.Missing)),
        };

        var lines = Lines(OverviewExport.Csv(rows, ["en-US", "nl-NL"], "en-US", d => "Root / " + d.Name, key => "/umbraco#/" + key));

        Assert.Equal("Name,Path,Document type,en-US,nl-NL,en-US outdated since,nl-NL outdated since,Backoffice URL", lines[0]);
        Assert.Equal($"\"Home, sweet\",\"Root / Home, sweet\",page,Published,Outdated (Pending changes),,2026-03-04,/umbraco#/{a}", lines[1]);
        Assert.Equal($"About,Root / About,page,Published,Missing,,,/umbraco#/{b}", lines[2]);
    }

    [Fact]
    public void Default_culture_never_prints_outdated_since_and_absent_culture_is_not_applicable()
    {
        var a = Guid.NewGuid();
        var rows = new[] { Row("Home", a, Variant(a, "en-US", CellStatus.Draft, outdated: true, since: DateTime.UtcNow)) };

        var lines = Lines(OverviewExport.Csv(rows, ["en-US", "nl-NL"], "en-US", _ => "", _ => ""));

        Assert.Equal("Home,,page,Draft,Not applicable,,,", lines[1]);
    }

    [Fact]
    public void Not_applicable_rows_use_the_same_word()
    {
        var a = Guid.NewGuid();
        var rows = new[] { Row("P", a, Variant(a, "en-US", CellStatus.NotApplicable), Variant(a, "nl-NL", CellStatus.Draft)) };

        var lines = Lines(OverviewExport.Csv(rows, ["en-US", "nl-NL"], "en-US", _ => "", _ => ""));

        Assert.Equal("P,,page,Not applicable,Draft,,,", lines[1]);
    }

    [Fact]
    public void Default_culture_need_not_be_first()
    {
        var a = Guid.NewGuid();
        var since = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[] { Row("P", a, Variant(a, "nl-NL", CellStatus.Published, outdated: true, since: since), Variant(a, "en-US", CellStatus.Draft, outdated: true, since: since)) };

        var lines = Lines(OverviewExport.Csv(rows, ["nl-NL", "en-US"], "EN-us", _ => "", _ => ""));

        Assert.Equal("P,,page,Outdated (Published),Draft,2026-03-04,,", lines[1]);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("Home", "Home")]
    [InlineData("", "")]
    public void Text_cells_that_look_like_formulas_get_a_leading_quote(string input, string expected) =>
        Assert.Equal(expected, OverviewExport.Safe(input));

    [Fact]
    public void Name_path_and_type_are_neutralised()
    {
        var a = Guid.NewGuid();
        var rows = new[] { new OverviewRow(new DocumentRow(a, 1, null, "-1,1", "0001", 1, "page", Guid.Empty, "=HYPERLINK(1)"), []) };

        var lines = Lines(OverviewExport.Csv(rows, ["en-US"], "en-US", _ => "-x", _ => ""));

        Assert.StartsWith("'=HYPERLINK(1),'-x,page,", lines[1]);
    }
}
