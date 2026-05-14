using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Commands;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Events;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.CommandHandlers;

internal sealed class AbandonSessionCommandHandler(
    OnboardingDbContext dbContext,
    ISessionEventEmitter eventEmitter,
    IPublisher publisher) : IRequestHandler<AbandonSessionCommand, SessionStepResponse>
{
    public async Task<SessionStepResponse> Handle(AbandonSessionCommand command, CancellationToken cancellationToken)
    {
        var sessionId = command.SessionId;

        var session = await dbContext.Sessions
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        if (session.Status == SessionStatus.Completed)
            throw new ConflictException("Session is already completed");

        if (session.Status != SessionStatus.Abandoned)
        {
            session.Status = SessionStatus.Abandoned;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventEmitter.EmitAsync(session.Id, "session-abandoned", new { sessionId = session.Id }, cancellationToken);

            try
            {
                await publisher.Publish(new SessionAbandonedEvent(session.Id, session.FlowId, session.UpdatedAt), cancellationToken);
            }
            catch (Exception) { /* Projector failure must not fail the command */ }
        }

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = false,
            CurrentNode = null
        };
    }
}
