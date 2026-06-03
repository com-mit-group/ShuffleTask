namespace ShuffleTask.Application.Models;

internal static class SyncIdentity
{
    public static string Required(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyCollection<string> DistinctIds(IEnumerable<string>? values)
        => values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
}
