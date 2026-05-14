using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Queries;
public record GetSessionStepQuery(Guid SessionId) : IRequest<SessionStepResponse>;
