namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>Writing the computed rows failed (as opposed to reading the content or computing the rows).</summary>
public sealed class IndexWriteException(Exception inner) : Exception("The status rows could not be written", inner);
