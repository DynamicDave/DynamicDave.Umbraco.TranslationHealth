# DynamicDave.Umbraco.TranslationHealth

Shows, for a multilingual Umbraco site, which pages are translated, missing, in draft, have pending changes or are outdated per language, and adds the dictionary tools Umbraco itself lacks: a missing-translations report, CSV and XLIFF export, and import with a test run, history and undo.

- **Translation overview** in the Translation section: every page and every language in one table, with totals per language, filters, name search and CSV export.
- **Translations tab** on every document that varies by language: the status of all its languages, and a button to mark a translation as up to date.
- **Missing translations**: the dictionary items with an empty value per language, grouped by key.
- **Import and export**: the dictionary as CSV (one column per language) or XLIFF 1.2 (one file per target language). An import always starts with a test run, is kept in a history and can be undone.

The package never changes page content. The only content-related data it writes is its own status data, including "Mark as up to date".

## Install

    dotnet add package DynamicDave.Umbraco.TranslationHealth

Supported Umbraco versions: **17.x and 18.x** (net10.0). One build runs unchanged on both. The backoffice UI is available in English, Dutch, German, French and Danish.

The package creates its tables with a package migration at startup and then builds the status table in the background. Sites with `Umbraco:CMS:Unattended:PackageMigrationsUnattended` set to `false` must run the migration from the backoffice (Packages section) first.

## Translation overview

Open **Translation > Translation overview**. Each row is a page (in tree order, indented), each language a column; the default language comes first. A cell shows one of:

| Status | Meaning |
|---|---|
| Published | The language is created and published. |
| Draft | The language is created, not published. |
| Pending changes | Published, with newer saved changes. |
| Missing | The language was never created for this page. |
| Not applicable | The page's site does not use this language (see below). |
| Outdated | A created translation that is behind the default language. Shown on top of its state (published, draft or pending changes). |

At the top are the totals per language: the pages that use the language, how many are published and up to date, and how many are missing or outdated. Filters: site, document type, language, status (missing, draft, pending changes, outdated, any issue), name search and "Only pages with issues". The table shows 25 rows per page. A click on a cell opens the page in that language. **Export CSV** exports the current filtered result: one row per page, a column per language, plus the document type, the path and a backoffice link.

The **source language** is Umbraco's default language. It cannot be outdated; the other languages are compared with it.

**Languages per site**: a site (root node) uses the languages that have a domain assigned on it. Other languages show as Not applicable and do not count in the totals. A site without any domains uses all languages.

### Outdated

A translation is outdated when the default language's text changed after the translation was last updated. Republishing the default language without changing it does not make anything outdated.

In plain words:

- **Tracked** (fingerprint): whenever a translation is saved with a text change, the package remembers a fingerprint of the default language's text at that moment. The translation is outdated as soon as the default text no longer matches that fingerprint. The fingerprint covers the page name and all language-varying values, including text inside block editors. The latest saved (draft) texts are compared, not only the published ones.
- **Estimated** (dates): a translation that has not been saved since the package was installed has no fingerprint yet. For these the package compares dates: outdated when the default language was updated later than the translation. The overview marks these as "estimated", because republishing without changes also moves a date. As soon as the translation is saved, it is tracked.
- **Mark as up to date**: on the Translations tab, an outdated translation has a button that records the current default text as the baseline. The flag disappears until the default text changes again. The tab then shows who marked it and when.

When the default language is changed, all fingerprints and marks are cleared (they belong to the old source language) and every translation starts in estimated mode again.

### Keeping it current

The status table is updated when content is saved, published, unpublished, moved, sorted, deleted or restored, and when languages, domains or the default language change. This happens after Umbraco's own save has finished and never blocks or fails an editor's save; an error is logged and the overview shows a hint to rebuild.

**Rebuild** (button in the overview) recalculates every page in the background, with progress, and removes rows of pages that no longer exist. It keeps all fingerprints and marks, so it never loses outdated tracking. Run it after changing `ExcludedDocumentTypes`. Only one rebuild runs at a time per installation.

## Translations tab

Every document that varies by language gets a **Translations** tab: one row per language with its state, whether it is outdated (tracked or estimated), when it was last changed and published, and a link that switches the editor to that language. Documents that do not vary by language have no tab.

## Dictionary tools

### Missing translations

Per language, the number of dictionary items with an empty value and the list of those items. Filters: language, group and a key search. Keys are grouped by the dot in their name (`Footer.Contact.Phone` is under Footer > Contact); keys without dots use the dictionary's own parent and child structure. Each item links to Umbraco's dictionary editor.

### Export

Open **Translation > Import and export**.

- **CSV**: one file with a `Key` column and a column per language ISO code, default language first. UTF-8 with BOM, so Excel opens it correctly.
- **XLIFF 1.2**: one file per chosen target language, with the default language as source and the existing translation as target.
- Choose all items, only the items missing in a language, or one group.

### Import with a test run

Upload a CSV or XLIFF file. Nothing changes yet: the test run gives every row and language one outcome.

| Outcome | When |
|---|---|
| New value | The value was empty and gets filled. |
| Overwrite | A value exists and **Overwrite existing values** is on. |
| Skipped: has value | A value exists and Overwrite is off. |
| Unchanged | The file has the value that is already there. |
| New key | The key does not exist and **Create new keys** is on. New keys go under the existing item whose key is the longest dot-prefix of the new key, otherwise at the root. |
| Skipped: unknown key | The key does not exist and Create new keys is off. |
| Error | With the reason: a language you may not edit, a language that is not installed, an empty key, or a key that appears more than once in the file. |

You choose which rows to apply; all applicable rows are selected by default. For XLIFF, a row whose source text changed since the export gets a warning with both texts and is not selected by default.

File format: a CSV needs a header row with a `Key` column; the other columns are matched to language ISO codes without regard to case, and unknown columns are reported and ignored. Comma and semicolon (Excel on a Dutch Windows) are detected. For XLIFF, the `target-language` must be an installed language.

Safety rules:

- **Empty cells never clear a value.**
- **Nothing is written before the test run**, and only the selected rows are written.
- **Compare-and-set**: each value is written only if it is still what the test run saw. A value an editor changed in between is never overwritten; it is reported as "Not written: changed since the test run".
- Only languages your user group may edit are written; other languages show as errors.
- A test run can be applied for one hour; after that, upload the file again.

### History and undo

Every applied import is stored with date, user, file, options, counts and, per row, the old and new value. **Undo** (once per import) restores the old values and removes the keys the import created, but only where the value is still the one the import wrote. Later edits are left alone and listed as such; a key that gained sub-items or changed since is not removed. The import stays in the history as Undone.

## Configuration

Optional:

```json
{
  "DynamicDave": {
    "TranslationHealth": {
      "ExcludedDocumentTypes": [ "siteSettings", "dataFolder" ]
    }
  }
}
```

| Setting | Default | |
|---|---|---|
| `ExcludedDocumentTypes` | empty | Aliases of document types left out of the overview, the totals and the export (case-insensitive), for example settings or data nodes. Rebuild after changing it. |

## Permissions

- **Overview, missing translations, import and export**: users with access to the Translation section (the dictionary tree). The overview, its totals, filters and CSV export only contain pages under the user's content start nodes.
- **Translations tab**: users who can browse the document (access to the Content section and the Browse permission on that document).
- **Mark as up to date**: the Update permission on the document and permission to edit that language.
- **Importing** only writes languages the user's groups may edit. As in Umbraco itself, a user group that does not have access to all languages and has no allowed languages selected has no language rights.

## Limits and performance

- Import file: at most 10 MB and 20,000 rows. Overview export: at most 50,000 rows. The overview pages with at most 100 rows per request (the UI shows 25).
- Page content is never imported; the import writes dictionary values only.
- Measured on a database with 10,165 documents and 4 languages (SQLite, Umbraco 17.8 and 18.3). The overview reads from the package's own table, never from all content:
  - Rebuild: about 5.6 seconds, or about 15 seconds while editors are saving.
  - Overview, first page: under 10 ms. Filtered overview and totals: under 100 ms.
  - Full export of all 10,165 rows: about 0.7 seconds.
- Not tested on SQL Server.

### SQLite and editing during a rebuild

On SQLite, a save that overlaps another connection's commit can hang inside Umbraco itself (in its own relation and redirect tracking). The same stall happens during bulk saves without this package.

The rebuild therefore yields to editors: before each write it waits up to 30 seconds for a 2 second pause in editing, and it writes in chunks of 1000 documents. In the test above, with 1 editor saving during a rebuild, 0 of 50 saves got stuck. With 4 editors who started saving in the same second as a manual rebuild, about 3 of 50 saves still hung.

On SQLite, start a rebuild when nobody is editing. For production with several editors, use SQL Server (not tested with this package).

## Behind a proxy

The CSV export contains a backoffice link per page, built from the scheme and host of the request. Behind a TLS-terminating proxy or load balancer, configure ASP.NET Core to use the forwarded headers (`UseForwardedHeaders` with `X-Forwarded-Proto` and `X-Forwarded-Host`). Without that, the links can start with `http://` and the internal host name.

## Load balancing

Status data, jobs, test runs and history are stored in the database, so every server sees them. The rebuild runs on the server that received the request; its state is in `umbracoKeyValue` and is claimed with compare-and-set, so only one rebuild runs at a time. A rebuild whose server stops counts as aborted after 2 minutes.

## Uninstall

Removing the package leaves its data in the database: the tables `dynamicDaveTranslationHealthDocument`, `dynamicDaveTranslationHealthVariant`, `dynamicDaveTranslationHealthImport`, `dynamicDaveTranslationHealthImportRow` and `dynamicDaveTranslationHealthTemp`, plus the `DynamicDave.TranslationHealth.*` keys and the package migration state in `umbracoKeyValue`. Dictionary values written by imports stay; undo the imports first if you want them gone. To remove the data:

```sql
DROP TABLE dynamicDaveTranslationHealthVariant;
DROP TABLE dynamicDaveTranslationHealthDocument;
DROP TABLE dynamicDaveTranslationHealthImportRow;
DROP TABLE dynamicDaveTranslationHealthImport;
DROP TABLE dynamicDaveTranslationHealthTemp;
DELETE FROM umbracoKeyValue WHERE [key] LIKE 'DynamicDave.TranslationHealth.%' OR [key] = 'Umbraco.Core.Upgrader.State+DynamicDave.Umbraco.TranslationHealth';
```

## License

MIT
