using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class StatusRowMappingTests
{
    private static readonly DateTime Moment = new(2026, 11, 13, 9, 30, 15, DateTimeKind.Utc);

    private static VariantRow Full() => new(
        Guid.NewGuid(), "nl-NL", CellStatus.PendingChanges, true, TrackingMode.Fingerprint,
        Moment, Moment.AddDays(-1), "Over ons", "fp-text", "fp-baseline", BaselineSource.MarkedUpToDate,
        Guid.NewGuid(), Moment.AddHours(2), Moment.AddHours(3));

    [Fact]
    public void Round_trips_every_field()
    {
        var row = Full();

        var back = StatusRowMapping.ToRow(StatusRowMapping.ToDto(row));

        Assert.Equal(row, back);
        Assert.Equal(DateTimeKind.Utc, back.UpdateDateUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, back.PublishDateUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, back.MarkedUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, back.OutdatedSinceUtc!.Value.Kind);
    }

    [Fact]
    public void Round_trips_nulls()
    {
        var row = new VariantRow(Guid.NewGuid(), "da", CellStatus.Missing, false, TrackingMode.Date,
            null, null, null, null, null, null, null, null, null);

        Assert.Equal(row, StatusRowMapping.ToRow(StatusRowMapping.ToDto(row)));
    }

    [Fact]
    public void Stores_enums_as_strings()
    {
        var dto = StatusRowMapping.ToDto(Full());

        Assert.Equal("PendingChanges", dto.Status);
        Assert.Equal("Fingerprint", dto.Mode);
        Assert.Equal("MarkedUpToDate", dto.BaselineSource);
    }

    [Fact]
    public void Reads_unspecified_dates_as_utc_and_converts_local_ones()
    {
        var dto = StatusRowMapping.ToDto(Full());
        dto.UpdateDateUtc = new DateTime(2026, 11, 13, 9, 30, 15, DateTimeKind.Unspecified);

        var back = StatusRowMapping.ToRow(dto);

        Assert.Equal(DateTimeKind.Utc, back.UpdateDateUtc!.Value.Kind);
        Assert.Equal(Moment, back.UpdateDateUtc);

        var local = StatusRowMapping.ToDto(Full() with { UpdateDateUtc = Moment.ToLocalTime() });
        Assert.Equal(Moment, local.UpdateDateUtc);
    }

    [Fact]
    public void Round_trips_a_document()
    {
        var row = new DocumentRow(Guid.NewGuid(), 1234, Guid.NewGuid(), "-1,12,1234", "00001.00002", 2, "translatedPage", Guid.NewGuid(), "Home");

        Assert.Equal(row, StatusRowMapping.ToRow(StatusRowMapping.ToDto(row)));
    }
}
