using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Commands;
public record StartSessionCommand(StartSessionRequest Request) : IRequest<SessionStepResponse>;
