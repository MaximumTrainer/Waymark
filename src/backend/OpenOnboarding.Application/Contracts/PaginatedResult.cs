namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// A paginated wrapper for a list of items.
/// </summary>
public sealed class PaginatedResult<T>
{
    /// <summary>The items on the current page.</summary>
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>The 1-based current page number.</summary>
    public int Page { get; set; }

    /// <summary>The maximum number of items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>The total number of items across all pages.</summary>
    public int TotalCount { get; set; }
}
