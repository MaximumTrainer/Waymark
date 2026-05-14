using MediatR;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Domain.Enums;
namespace OpenOnboarding.Application.Queries;
public record ListSessionsQuery(Guid? FlowId, SessionStatus? Status, int Page, int PageSize) : IRequest<PaginatedResult<SessionListItemDto>>;
