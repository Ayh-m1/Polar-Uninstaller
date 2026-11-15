namespace PolarUninstallerWpf.Services;

/// <summary>
/// Converts the persisted Quick Cleaner selection to and from category names.
/// A missing value selects User Temp and System Temp;
/// an explicitly saved empty list stays empty.
/// </summary>
public static class CleanerSelection
{
    public static long EstimatedBytes(IEnumerable<TempCleanerService.Category> selected,IReadOnlyDictionary<TempCleanerService.Category,long> sizes)=>
        selected.Distinct().Sum(category=>Math.Max(0,sizes.GetValueOrDefault(category)));
    public static TempCleanerService.Category[] Parse(IEnumerable<string>? saved)
    {
        if (saved is null) return [TempCleanerService.Category.UserTemp,TempCleanerService.Category.SystemTemp];
        return saved
            .Select(name => Enum.TryParse<TempCleanerService.Category>(name, true, out var category) ? category : (TempCleanerService.Category?)null)
            .Where(category => category.HasValue&&Enum.IsDefined(category.Value))
            .Select(category => category!.Value)
            .Distinct()
            .OrderBy(category => category)
            .ToArray();
    }

    public static string[] Serialize(IEnumerable<TempCleanerService.Category> categories) =>
        categories.Distinct().OrderBy(category => category).Select(category => category.ToString()).ToArray();
}
