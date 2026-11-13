namespace DynamicDave.Umbraco.TranslationHealth.Importing;

/// <summary>The import is unknown or was already undone: the undo could not claim it. Other failures of an undo are not this type.</summary>
public sealed class ImportAlreadyUndoneException() : InvalidOperationException("The import is unknown or has already been undone.");
