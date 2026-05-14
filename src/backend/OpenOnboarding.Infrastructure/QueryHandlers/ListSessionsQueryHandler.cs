using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class ListSessionsQueryHandler(OnboardingDbContext dbContext) : IRequestHandler<ListSessionsQuery, PaginatedResult<SessionListItemDto>>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedResult<SessionListItemDto>> Handle(ListSessionsQuery query, CancellationToken cancellationToken)
    {
        var (flowId, status, page, pageSize) = query;

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        // Read from denormalized read model (CQRS read side)
        var readQuery = dbContext.SessionReadModels.AsQueryable();

        if (flowId.HasValue)
            readQuery = readQuery.Where(x => x.FlowId == flowId.Value);

        if (status.HasValue)
            readQuery = readQuery.Where(x => x.StatusName == status.Value.ToString());

        var totalCount = await readQuery.CountAsync(cancellationToken);

        // If read model is empty (e.g. during migration), fall back to Sessions table
        if (totalCount == 0)
        {
            return await FallbackToSessionsTableAsync(flowId, status, page, pageSize, cancellationToken);
        }

        var items = await readQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SessionListItemDto
            {
                Id = x.Id,
                FlowId = x.FlowId,
                Status = Enum.Parse<SessionStatus>(x.StatusName),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                CustomerProfileId = null
            })
            .ToListAsync(cancellationToken);

        return new PaginatedResult<SessionListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private async Task<PaginatedResult<SessionListItemDto>> FallbackToSessionsTableAsync(
        Guid? flowId, SessionStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Sessions.AsQueryable();

        if (flowId.HasValue)
            query = query.Where(x => x.FlowId == flowId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SessionListItemDto
            {
                Id = x.Id,
                FlowId = x.FlowId,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                CustomerProfileId = x.CustomerProfileId
            })
            .ToListAsync(cancellationToken);

        return new PaginatedResult<SessionListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
