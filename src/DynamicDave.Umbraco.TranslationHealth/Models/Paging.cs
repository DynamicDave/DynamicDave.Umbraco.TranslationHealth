namespace DynamicDave.Umbraco.TranslationHealth.Models;

/// <summary>Paging limits shared by the API endpoints that return pages.</summary>
public static class Paging
{
    public const int MaxTake = 100;

    /// <summary>The items of a test run are fetched in large pages: the UI needs all of them for the selection.</summary>
    public const int MaxPlanItemsTake = 1000;

    /// <summary>The size of the first page of items of a test run (the UI's page size).</summary>
    public const int FirstPageSize = 25;

    public static bool IsValid(int skip, int take, int maxTake = MaxTake) => skip >= 0 && take >= 1 && take <= maxTake;
}
