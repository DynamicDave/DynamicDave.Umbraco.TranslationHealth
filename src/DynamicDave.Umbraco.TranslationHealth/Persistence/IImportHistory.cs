using System.Text.Json.Serialization;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

[JsonConverter(typeof(JsonStringEnumConverter<ImportState>))]
public enum ImportState
{
    Applied,
    Undone,
}

public sealed record ImportRecord(
    Guid Key,
    DateTime CreatedUtc,
    Guid UserKey,
    string UserName,
    string FileName,
    string Format,
    bool Overwrite,
    bool CreateKeys,
    int Applied,
    int Skipped,
    ImportState State,
    DateTime? UndoneUtc,
    string? UndoneBy);

/// <summary>A dictionary value an import wrote; <see cref="OldValue"/> is null when there was none before.</summary>
public sealed record ImportRowRecord(string Key, string Culture, string? OldValue, string NewValue, bool KeyCreated);

public interface IImportHistory
{
    void Save(ImportRecord import, IReadOnlyList<ImportRowRecord> rows);

    /// <summary>Newest first.</summary>
    (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take);

    ImportRecord? Get(Guid key);

    IReadOnlyList<ImportRowRecord> Rows(Guid key);

    /// <summary>Marks an import undone only while it is still applied. Returns false when it is unknown or already undone.</summary>
    bool MarkUndone(Guid key, string userName, DateTime utc);
}
