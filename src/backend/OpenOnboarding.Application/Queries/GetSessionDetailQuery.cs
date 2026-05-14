using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Queries;
public record GetSessionDetailQuery(Guid SessionId) : IRequest<SessionDetailDto>;
