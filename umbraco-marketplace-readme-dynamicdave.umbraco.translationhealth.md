# Translation Health

See which pages are translated, missing, outdated or unpublished in every language, in one overview. Translation Health also adds the dictionary tools Umbraco lacks: a missing-translations report, CSV and XLIFF export, and import with a test run and undo.

## Features

- **Translation overview** in the Translation section: every page and language in one table, with totals per language, filters, name search and CSV export
- **Outdated translations**: a translation is flagged when the default language's text changed after it was last updated, and can be marked as up to date in one click
- **Translations tab** on every document that varies by language
- **Missing translations** report for the dictionary, grouped by key
- **Export** the dictionary as CSV (one column per language) or XLIFF 1.2 (one file per target language)
- **Import** CSV or XLIFF with a **test run** of every row before anything changes, empty cells never clear a value, and a **history with undo**
- Fast on large sites: on about 10,000 documents the first overview page loads in under 10 ms
- Access follows Umbraco's own rights: start nodes, section access and language permissions

## Compatibility

- Umbraco **17 and 18** (.NET 10)
- Backoffice UI in English, Dutch, German, French and Danish
- Free and open source (MIT)

## Getting started

```
dotnet add package DynamicDave.Umbraco.TranslationHealth
```

The status table is built in the background shortly after the first start. Full documentation and configuration: [README on GitHub](https://github.com/DynamicDave/DynamicDave.Umbraco.TranslationHealth/blob/main/src/DynamicDave.Umbraco.TranslationHealth/README.md).
