using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

/// <summary>Turns a parsed import file into a test-run plan without touching any data.</summary>
public static class ImportPlanner
{
    private const string IdPrefix = "id:";

    public static ImportPlan Plan(
        Guid id,
        string fileName,
        ImportFile file,
        IReadOnlyList<DictionaryEntry> current,
        string defaultCulture,
        IReadOnlySet<string> editableCultures,
        IReadOnlySet<string> installedCultures,
        ImportOptions options)
    {
        var byKey = new Dictionary<string, DictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<Guid, DictionaryEntry>();
        foreach (var entry in current)
        {
            byKey.TryAdd(entry.Key.Trim(), entry);
            byId.TryAdd(entry.Id, entry);
        }

        var installed = installedCultures.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);
        var editable = new HashSet<string>(editableCultures, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PlanItem>();

        void Add(ImportRow row, string key, string culture, ImportOutcome outcome, ImportErrorReason reason,
            string? currentValue, string newValue, string? fileSource, string? currentSource, Guid? entryId)
        {
            var sourceChanged = entryId is not null
                && file.Format == ImportFileFormat.Xliff
                && fileSource is not null
                && !string.Equals(DictionaryValues.NormalizeLineEndings(fileSource), DictionaryValues.NormalizeLineEndings(currentSource ?? string.Empty), StringComparison.Ordinal);
            var selected = (outcome is ImportOutcome.NewValue or ImportOutcome.Overwrite or ImportOutcome.NewKey) && !sourceChanged;
            items.Add(new PlanItem(items.Count + 1, row.RowNumber, key, culture, outcome, reason, currentValue, newValue,
                fileSource, currentSource, sourceChanged, selected, entryId));
        }

        string CultureName(string culture) => installed.TryGetValue(culture, out var name) ? name : culture;

        foreach (var row in file.Rows)
        {
            var values = row.Values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            var rowKey = row.Key.Trim();
            var fileSource = FindSource(row, defaultCulture);

            if (rowKey.Length == 0)
            {
                foreach (var (culture, value) in values)
                {
                    Add(row, rowKey, CultureName(culture), ImportOutcome.Error, ImportErrorReason.EmptyKey, null, value, fileSource, null, null);
                }

                continue;
            }

            var entry = Resolve(rowKey, byKey, byId);
            var identity = entry is not null ? "e:" + entry.Id : "k:" + rowKey;
            var duplicate = !seen.Add(identity);
            var key = entry?.Key ?? rowKey;
            var currentSource = entry is not null ? DictionaryValues.NonEmpty(entry.Values, defaultCulture) : null;

            foreach (var (culture, value) in values)
            {
                var name = CultureName(culture);

                if (duplicate)
                {
                    Add(row, key, name, ImportOutcome.Error, ImportErrorReason.DuplicateKey, null, value, fileSource, currentSource, entry?.Id);
                    continue;
                }

                if (!installed.ContainsKey(culture))
                {
                    Add(row, key, name, ImportOutcome.Error, ImportErrorReason.UnknownLanguage, null, value, fileSource, currentSource, entry?.Id);
                    continue;
                }

                if (!editable.Contains(culture))
                {
                    Add(row, key, name, ImportOutcome.Error, ImportErrorReason.LanguageNotAllowed, null, value, fileSource, currentSource, entry?.Id);
                    continue;
                }

                if (entry is null)
                {
                    var outcome = options.CreateKeys && !rowKey.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)
                        ? ImportOutcome.NewKey
                        : ImportOutcome.SkippedUnknownKey;
                    Add(row, key, name, outcome, ImportErrorReason.None, null, value, fileSource, null, null);
                    continue;
                }

                var currentValue = DictionaryValues.NonEmpty(entry.Values, culture);
                var result = currentValue is null ? ImportOutcome.NewValue
                    : DictionaryValues.NormalizeLineEndings(currentValue) == DictionaryValues.NormalizeLineEndings(value) ? ImportOutcome.Unchanged
                    : options.Overwrite ? ImportOutcome.Overwrite
                    : ImportOutcome.SkippedHasValue;
                Add(row, key, name, result, ImportErrorReason.None, currentValue, value, fileSource, currentSource, entry.Id);
            }
        }

        return new ImportPlan(id, fileName, file.Format, options, file.UnknownColumns, items);
    }

    private static DictionaryEntry? Resolve(string key, Dictionary<string, DictionaryEntry> byKey, Dictionary<Guid, DictionaryEntry> byId)
    {
        if (key.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Guid.TryParse(key[IdPrefix.Length..].Trim(), out var entryId) && byId.TryGetValue(entryId, out var byIdMatch)
                ? byIdMatch
                : null;
        }

        return byKey.GetValueOrDefault(key);
    }

    private static string? FindSource(ImportRow row, string culture)
    {
        if (row.Sources is null)
        {
            return null;
        }

        foreach (var (name, value) in row.Sources)
        {
            if (string.Equals(name, culture, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
