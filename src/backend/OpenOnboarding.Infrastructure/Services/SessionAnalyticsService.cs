using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class SessionAnalyticsService(OnboardingDbContext dbContext) : ISessionAnalyticsService
{
    private const int MaxPageSize = 100;

    public async Task<IReadOnlyList<SubmissionDto>> GetSubmissionsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        // Verify session exists
        var sessionExists = await dbContext.Sessions.AnyAsync(x => x.Id == sessionId, cancellationToken);
        if (!sessionExists)
        {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        var submissions = await dbContext.Submissions
            .Where(s => s.SessionId == sessionId)
            .Join(
                dbContext.Nodes,
                s => s.NodeId,
                n => n.Id,
                (s, n) => new SubmissionDto
                {
                    Id = s.Id,
                    SessionId = s.SessionId,
                    NodeId = s.NodeId,
                    NodeKey = n.Key,
                    SubmittedAt = s.SubmittedAt,
                    DataJson = s.DataJson
                })
            .OrderBy(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

        return submissions;
    }

    public async Task<SessionDetailDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Sessions
            .Include(x => x.Flow)
            .ThenInclude(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        NodeDto? currentNode = null;
        if (session.CurrentNodeId.HasValue)
        {
            var node = session.Flow.Nodes.FirstOrDefault(n => n.Id == session.CurrentNodeId.Value);
            if (node is not null)
            {
                currentNode = NodeDto.FromEntity(node);
            }
        }

        return new SessionDetailDto
        {
            Id = session.Id,
            FlowId = session.FlowId,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            CustomerProfileId = session.CustomerProfileId,
            CurrentNode = currentNode
        };
    }

    public async Task<PaginatedResult<SessionListItemDto>> GetSessionsAsync(
        Guid? flowId,
        SessionStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = dbContext.Sessions.AsQueryable();

        if (flowId.HasValue)
        {
            query = query.Where(x => x.FlowId == flowId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

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

    public async Task<FlowStatsDto> GetFlowStatsAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.Sessions
            .Where(x => x.FlowId == flowId)
            .Select(x => new { x.Status, x.CreatedAt, x.UpdatedAt, x.CurrentNodeId })
            .ToListAsync(cancellationToken);

        var totalSessions = sessions.Count;
        var completedSessions = sessions.Count(x => x.Status == SessionStatus.Completed);
        var abandonedSessions = sessions.Count(x => x.Status == SessionStatus.Abandoned);

        var completedTimes = sessions
            .Where(x => x.Status == SessionStatus.Completed)
            .Select(x => (x.UpdatedAt - x.CreatedAt).TotalSeconds)
            .ToList();

        var averageCompletionTimeSeconds = completedTimes.Count > 0
            ? completedTimes.Average()
            : 0;

        // Build drop-off map: count abandoned sessions by the node key they were on
        var abandonedNodeIds = sessions
            .Where(x => x.Status == SessionStatus.Abandoned && x.CurrentNodeId.HasValue)
            .Select(x => x.CurrentNodeId!.Value)
            .ToList();

        Dictionary<string, int> dropOffByNodeKey = new();
        if (abandonedNodeIds.Count > 0)
        {
            var nodeKeys = await dbContext.Nodes
                .Where(n => abandonedNodeIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Key })
                .ToListAsync(cancellationToken);

            var nodeKeyById = nodeKeys.ToDictionary(n => n.Id, n => n.Key);

            foreach (var nodeId in abandonedNodeIds)
            {
                if (!nodeKeyById.TryGetValue(nodeId, out var key)) continue;
                dropOffByNodeKey.TryGetValue(key, out var count);
                dropOffByNodeKey[key] = count + 1;
            }
        }

        return new FlowStatsDto
        {
            TotalSessions = totalSessions,
            CompletedSessions = completedSessions,
            AbandonedSessions = abandonedSessions,
            AverageCompletionTimeSeconds = averageCompletionTimeSeconds,
            DropOffByNodeKey = dropOffByNodeKey
        };
    }
}
