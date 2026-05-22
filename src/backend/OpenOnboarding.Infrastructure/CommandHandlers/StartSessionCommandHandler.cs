using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Commands;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Events;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.CommandHandlers;

internal sealed class StartSessionCommandHandler(
    OnboardingDbContext dbContext,
    IValidator<StartSessionRequest> validator,
    ICustomerService customerService,
    ISessionEventEmitter eventEmitter,
    IMetricsService metricsService,
    ITelemetryService telemetryService,
    IPublisher publisher) : IRequestHandler<StartSessionCommand, SessionStepResponse>
{
    public async Task<SessionStepResponse> Handle(StartSessionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var customerProfileId = request.CustomerProfileId;
        if (request.CustomerProfile is not null)
        {
            var profile = await customerService.UpsertByExternalIdAsync(request.CustomerProfile, cancellationToken);
            customerProfileId = profile.Id;
        }

        var flow = await dbContext.Flows
            .Include(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == request.FlowId, cancellationToken)
            ?? throw new InvalidOperationException($"Flow '{request.FlowId}' was not found.");

        var startNode = flow.Nodes.FirstOrDefault(x => x.IsStartNode) ?? flow.Nodes.FirstOrDefault()
            ?? throw new InvalidOperationException("Flow does not define any nodes.");

        var session = new Session
        {
            FlowId = flow.Id,
            CustomerProfileId = customerProfileId,
            CurrentNodeId = startNode.Id,
            Status = SessionStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        metricsService.IncrementSessionsStarted(flow.Id.ToString());

        await eventEmitter.EmitAsync(session.Id, "session-started", new { sessionId = session.Id }, cancellationToken);

        await telemetryService.TrackAsync(new AnalyticsEvent
        {
            EventType = "session_started",
            JourneyId = flow.Id.ToString(),
            SessionId = session.Id.ToString(),
            StepId = startNode.Id.ToString(),
            StepIndex = 0,
            Payload = new Dictionary<string, object?>
            {
                ["flowName"] = flow.Name,
                ["stepKey"] = startNode.Key,
                ["stepTitle"] = startNode.Title
            }
        }, cancellationToken);

        try
        {
            await publisher.Publish(new SessionStartedEvent(session.Id, flow.Id, session.CreatedAt), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* Projector failure must not fail the command */ }

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = false,
            CurrentNode = NodeDto.FromEntity(startNode)
        };
    }
}
