namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class PaginatedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
}
