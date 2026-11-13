using DynamicDave.Umbraco.TranslationHealth.Content;

namespace DynamicDave.Umbraco.TranslationHealth.Status;

/// <summary>Turns a document snapshot plus its previously stored rows into the stored status rows.</summary>
public static class StatusIndexer
{
    public static IndexResult Index(
        DocumentSnapshot doc,
        Guid siteRootKey,
        string defaultCulture,
        IReadOnlyCollection<string> siteCultures,
        IReadOnlyList<VariantRow> previous,
        IndexMode mode,
        DateTime nowUtc)
    {
        var defaultSnapshot = doc.Cultures.FirstOrDefault(c => Same(c.Culture, defaultCulture));
        var sourceExists = defaultSnapshot is { State: not VariantBaseState.NotCreated };
        var defaultFingerprint = sourceExists ? defaultSnapshot!.Fingerprint : null;
        var defaultUpdate = sourceExists ? defaultSnapshot!.UpdateDateUtc : null;

        var rows = new List<VariantRow>();
        foreach (var culture in doc.Cultures)
        {
            var applicable = siteCultures.Count == 0 || siteCultures.Any(s => Same(s, culture.Culture));
            if (!applicable)
            {
                rows.Add(Empty(doc.Key, culture.Culture, CellStatus.NotApplicable));
                continue;
            }

            if (culture.State == VariantBaseState.NotCreated)
            {
                rows.Add(Empty(doc.Key, culture.Culture, CellStatus.Missing));
                continue;
            }

            var row = new VariantRow(
                doc.Key,
                culture.Culture,
                culture.State switch
                {
                    VariantBaseState.Draft => CellStatus.Draft,
                    VariantBaseState.Published => CellStatus.Published,
                    _ => CellStatus.PendingChanges,
                },
                false,
                TrackingMode.Date,
                culture.UpdateDateUtc,
                culture.PublishDateUtc,
                culture.Name,
                culture.Fingerprint,
                null, null, null, null, null);

            if (!Same(culture.Culture, defaultCulture))
            {
                var prev = previous.FirstOrDefault(p => Same(p.Culture, culture.Culture));
                row = ApplyBaseline(row, prev, previous.Count > 0, mode, defaultFingerprint);
                row = ApplyOutdated(row, prev, mode, sourceExists, defaultFingerprint, defaultUpdate, nowUtc);
            }

            rows.Add(row);
        }

        var name = defaultSnapshot?.Name
            ?? doc.Cultures.Select(c => c.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n))
            ?? string.Empty;

        var document = new DocumentRow(
            doc.Key, doc.Id, doc.ParentKey, doc.Path, doc.SortPath, doc.Level, doc.ContentTypeAlias, siteRootKey, name);
        return new IndexResult(document, rows);
    }

    public static VariantRow MarkUpToDate(VariantRow row, string defaultFingerprint, Guid userKey, DateTime nowUtc) =>
        row with
        {
            BaselineFingerprint = defaultFingerprint,
            BaselineSource = Status.BaselineSource.MarkedUpToDate,
            MarkedByUserKey = userKey,
            MarkedUtc = nowUtc,
            Mode = TrackingMode.Fingerprint,
            Outdated = false,
            OutdatedSinceUtc = null,
        };

    private static VariantRow ApplyBaseline(
        VariantRow row, VariantRow? prev, bool documentHadRows, IndexMode mode, string? defaultFingerprint)
    {
        if (mode == IndexMode.RebuildClearBaselines)
        {
            return row;
        }

        if (mode == IndexMode.Rebuild)
        {
            return prev is null ? row : KeepBaseline(row, prev);
        }

        var created = prev is null || prev.Status is CellStatus.Missing or CellStatus.NotApplicable;
        var textChanged = prev?.TextFingerprint is not null && prev.TextFingerprint != row.TextFingerprint;

        if ((created && documentHadRows) || textChanged)
        {
            return defaultFingerprint is null
                ? row
                : row with { BaselineFingerprint = defaultFingerprint, BaselineSource = Status.BaselineSource.Saved };
        }

        return prev is null ? row : KeepBaseline(row, prev);
    }

    private static VariantRow KeepBaseline(VariantRow row, VariantRow prev) =>
        row with
        {
            BaselineFingerprint = prev.BaselineFingerprint,
            BaselineSource = prev.BaselineSource,
            MarkedByUserKey = prev.MarkedByUserKey,
            MarkedUtc = prev.MarkedUtc,
        };

    private static VariantRow ApplyOutdated(
        VariantRow row,
        VariantRow? prev,
        IndexMode mode,
        bool sourceExists,
        string? defaultFingerprint,
        DateTime? defaultUpdate,
        DateTime nowUtc)
    {
        var fingerprintMode = row.BaselineFingerprint is not null;
        row = row with { Mode = fingerprintMode ? TrackingMode.Fingerprint : TrackingMode.Date };

        var outdated = sourceExists && (fingerprintMode
            ? defaultFingerprint != row.BaselineFingerprint
            : defaultUpdate is not null && row.UpdateDateUtc is not null && defaultUpdate > row.UpdateDateUtc);

        if (!outdated)
        {
            return row with { Outdated = false, OutdatedSinceUtc = null };
        }

        var keep = mode != IndexMode.RebuildClearBaselines && prev is { Outdated: true, OutdatedSinceUtc: not null };
        var since = keep ? prev!.OutdatedSinceUtc : fingerprintMode ? nowUtc : defaultUpdate;
        return row with { Outdated = true, OutdatedSinceUtc = since };
    }

    private static VariantRow Empty(Guid documentKey, string culture, CellStatus status) =>
        new(documentKey, culture, status, false, TrackingMode.Date, null, null, null, null, null, null, null, null, null);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
