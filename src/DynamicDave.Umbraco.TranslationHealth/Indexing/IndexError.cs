namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>The last failure of the indexing worker, stored as JSON under <see cref="Constants.LastIndexErrorKey"/>.</summary>
public sealed record IndexError(DateTime Utc, string Message);
