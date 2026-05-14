using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Commands;
public record AbandonSessionCommand(Guid SessionId) : IRequest<SessionStepResponse>;
