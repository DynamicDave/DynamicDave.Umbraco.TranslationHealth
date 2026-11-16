namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>Entries kept for <see cref="TempStore.TimeToLive"/>, in the database so every server behind a load balancer sees them.</summary>
public interface ITempStore
{
    /// <summary>Stores the content under the key and kind, replacing an earlier entry with the same key and kind.</summary>
    void Put(Guid key, TempKind kind, string content, string? fileName);

    /// <summary>The entry, or null when it does not exist or has expired. Only reads.</summary>
    TempEntry? Get(Guid key, TempKind kind);

    /// <summary>
    /// Removes the entry. True only for the one caller that removed it (a single DELETE), so a plan can be claimed once,
    /// also by two concurrent requests; false when it does not exist, has expired or was claimed already.
    /// </summary>
    bool Delete(Guid key, TempKind kind);
}
