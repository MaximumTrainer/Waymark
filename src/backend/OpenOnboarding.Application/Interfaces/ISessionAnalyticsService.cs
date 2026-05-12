using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Interfaces;

public interface ISessionAnalyticsService
{
    Task<IReadOnlyList<SubmissionDto>> GetSubmissionsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionDetailDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<PaginatedResult<SessionListItemDto>> GetSessionsAsync(Guid? flowId, SessionStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<FlowStatsDto> GetFlowStatsAsync(Guid flowId, CancellationToken cancellationToken = default);
}
