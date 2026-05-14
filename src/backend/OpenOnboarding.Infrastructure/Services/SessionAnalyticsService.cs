using MediatR;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class SessionAnalyticsService(IMediator mediator) : ISessionAnalyticsService
{
    public Task<IReadOnlyList<SubmissionDto>> GetSubmissionsAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => mediator.Send(new GetSubmissionsQuery(sessionId), cancellationToken);

    public Task<SessionDetailDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => mediator.Send(new GetSessionDetailQuery(sessionId), cancellationToken);

    public Task<PaginatedResult<SessionListItemDto>> GetSessionsAsync(Guid? flowId, SessionStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
        => mediator.Send(new ListSessionsQuery(flowId, status, page, pageSize), cancellationToken);

    public Task<FlowStatsDto> GetFlowStatsAsync(Guid flowId, CancellationToken cancellationToken = default)
        => mediator.Send(new GetFlowStatsQuery(flowId), cancellationToken);
}
