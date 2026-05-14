using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class GetFlowStatsQueryHandler(OnboardingDbContext dbContext) : IRequestHandler<GetFlowStatsQuery, FlowStatsDto>
{
    public async Task<FlowStatsDto> Handle(GetFlowStatsQuery query, CancellationToken cancellationToken)
    {
        var flowId = query.FlowId;

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

        var averageCompletionTimeSeconds = completedTimes.Count > 0 ? completedTimes.Average() : 0;

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
