using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

/// <summary>Reads and writes the dictionary through <see cref="IDictionaryItemService"/>. Every failed write throws with the Umbraco status.</summary>
public sealed class UmbracoDictionarySource(IDictionaryItemService items, ILanguageService languages) : IDictionarySource
{
    public IReadOnlyList<DictionaryEntry> GetAll()
    {
        // A null parent returns every item of the dictionary, translations included, in one call.
        var all = items.GetDescendantsAsync(null, null).GetAwaiter().GetResult();
        return all.Select(ToEntry).ToList();
    }

    public async Task<DictionaryEntry?> GetAsync(Guid id)
    {
        var item = await items.GetAsync(id);
        return item is null ? null : ToEntry(item);
    }

    public async Task<bool> HasChildrenAsync(Guid id) => (await items.GetChildrenAsync(id)).Any();

    public async Task<SetValuesResult> SetValuesAsync(Guid id, IReadOnlyList<ValueChange> changes, Guid userKey)
    {
        var item = await items.GetAsync(id);
        if (item is null)
        {
            return new SetValuesResult([], changes.Select(c => c.Culture).ToList());
        }

        var translations = item.Translations.ToList();
        var written = new List<WrittenValue>();
        var conflicts = new List<string>();

        foreach (var change in changes)
        {
            var existing = translations.FirstOrDefault(t => string.Equals(t.LanguageIsoCode, change.Culture, StringComparison.OrdinalIgnoreCase));
            var current = string.IsNullOrWhiteSpace(existing?.Value) ? null : existing!.Value;
            if (!DictionaryValues.SameValue(current, change.Expected))
            {
                conflicts.Add(change.Culture);
                continue;
            }

            if (string.IsNullOrEmpty(change.NewValue))
            {
                if (existing is not null)
                {
                    // UpdateAsync never deletes a translation row it was not given, so the removal is stored as an empty value (a read treats it as missing).
                    existing.Value = string.Empty;
                }
            }
            else if (existing is not null)
            {
                existing.Value = change.NewValue;
            }
            else
            {
                var language = await languages.GetAsync(change.Culture) ?? throw new InvalidOperationException($"Language {change.Culture} was not found.");
                translations.Add(new DictionaryTranslation(language, change.NewValue));
            }

            written.Add(new WrittenValue(change.Culture, current, change.NewValue));
        }

        if (written.Count > 0)
        {
            item.Translations = translations;
            var result = await items.UpdateAsync(item, userKey);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Updating dictionary item '{item.ItemKey}' failed: {result.Status}.");
            }
        }

        return new SetValuesResult(written, conflicts);
    }

    public async Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey)
    {
        var item = new DictionaryItem(parentId, key);
        var translations = new List<IDictionaryTranslation>();
        foreach (var (culture, value) in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var language = await languages.GetAsync(culture) ?? throw new InvalidOperationException($"Language {culture} was not found.");
            translations.Add(new DictionaryTranslation(language, value));
        }

        item.Translations = translations;
        var result = await items.CreateAsync(item, userKey);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Creating dictionary item '{key}' failed: {result.Status}.");
        }

        return result.Result.Key;
    }

    public async Task DeleteAsync(Guid id, Guid userKey)
    {
        var result = await items.DeleteAsync(id, userKey);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Deleting dictionary item {id} failed: {result.Status}.");
        }
    }

    private static DictionaryEntry ToEntry(IDictionaryItem item)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var translation in item.Translations)
        {
            if (!string.IsNullOrWhiteSpace(translation.Value))
            {
                values[translation.LanguageIsoCode] = translation.Value;
            }
        }

        return new DictionaryEntry(item.Key, item.ItemKey, item.ParentId, values);
    }
}
