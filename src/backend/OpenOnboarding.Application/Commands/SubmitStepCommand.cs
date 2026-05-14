using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Commands;
public record SubmitStepCommand(Guid SessionId, Guid NodeId, SubmitStepRequest Request) : IRequest<SessionStepResponse>;
