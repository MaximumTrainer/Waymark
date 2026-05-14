using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Events;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.ReadModels;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.EventHandlers;

internal sealed class SessionProjector(OnboardingDbContext dbContext) :
    INotificationHandler<SessionStartedEvent>,
    INotificationHandler<StepAdvancedEvent>,
    INotificationHandler<SessionCompletedEvent>,
    INotificationHandler<SessionAbandonedEvent>
{
    public async Task Handle(SessionStartedEvent notification, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Flow)
            .Include(s => s.CustomerProfile)
            .FirstOrDefaultAsync(s => s.Id == notification.SessionId, cancellationToken);

        if (session is null) return;

        var currentNode = session.CurrentNodeId.HasValue
            ? await dbContext.Nodes.FirstOrDefaultAsync(n => n.Id == session.CurrentNodeId.Value, cancellationToken)
            : null;

        var existing = await dbContext.SessionReadModels.FindAsync(new object[] { notification.SessionId }, cancellationToken);
        if (existing is not null)
        {
            UpdateModel(existing, session, currentNode);
        }
        else
        {
            var model = BuildModel(session, currentNode);
            dbContext.SessionReadModels.Add(model);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(StepAdvancedEvent notification, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Flow)
            .Include(s => s.CustomerProfile)
            .Include(s => s.Submissions)
            .FirstOrDefaultAsync(s => s.Id == notification.SessionId, cancellationToken);

        if (session is null) return;

        Node? currentNode = null;
        if (notification.CurrentNodeId.HasValue)
            currentNode = await dbContext.Nodes.FirstOrDefaultAsync(n => n.Id == notification.CurrentNodeId.Value, cancellationToken);

        var model = await dbContext.SessionReadModels.FindAsync(new object[] { notification.SessionId }, cancellationToken)
            ?? BuildModel(session, currentNode);

        UpdateModel(model, session, currentNode);
        model.StepCount = session.Submissions.Count;

        if (model.Id != notification.SessionId)
            dbContext.SessionReadModels.Add(model);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(SessionCompletedEvent notification, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Flow)
            .Include(s => s.CustomerProfile)
            .Include(s => s.Submissions)
            .FirstOrDefaultAsync(s => s.Id == notification.SessionId, cancellationToken);

        if (session is null) return;

        var model = await dbContext.SessionReadModels.FindAsync(new object[] { notification.SessionId }, cancellationToken)
            ?? BuildModel(session, null);

        UpdateModel(model, session, null);
        model.StepCount = session.Submissions.Count;
        model.CompletedAt = notification.OccurredAt;
        model.CompletionDurationSeconds = (notification.OccurredAt - model.CreatedAt).TotalSeconds;

        if (model.Id != notification.SessionId)
            dbContext.SessionReadModels.Add(model);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(SessionAbandonedEvent notification, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Flow)
            .Include(s => s.CustomerProfile)
            .FirstOrDefaultAsync(s => s.Id == notification.SessionId, cancellationToken);

        if (session is null) return;

        var model = await dbContext.SessionReadModels.FindAsync(new object[] { notification.SessionId }, cancellationToken)
            ?? BuildModel(session, null);

        UpdateModel(model, session, null);
        model.AbandonedAt = notification.OccurredAt;

        if (model.Id != notification.SessionId)
            dbContext.SessionReadModels.Add(model);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static SessionReadModel BuildModel(Session session, Node? currentNode) => new()
    {
        Id = session.Id,
        FlowId = session.FlowId,
        FlowName = session.Flow.Name,
        CustomerEmail = session.CustomerProfile?.Email,
        CustomerCountry = session.CustomerProfile?.Country,
        ExternalCustomerId = session.CustomerProfile?.ExternalCustomerId,
        CurrentNodeId = currentNode?.Id,
        CurrentNodeKey = currentNode?.Key,
        CurrentNodeTitle = currentNode?.Title,
        StatusName = session.Status.ToString(),
        StepCount = 0,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt
    };

    private static void UpdateModel(SessionReadModel model, Session session, Node? currentNode)
    {
        model.StatusName = session.Status.ToString();
        model.UpdatedAt = session.UpdatedAt;
        model.CurrentNodeId = currentNode?.Id;
        model.CurrentNodeKey = currentNode?.Key;
        model.CurrentNodeTitle = currentNode?.Title;
        model.FlowName = session.Flow.Name;
        model.CustomerEmail = session.CustomerProfile?.Email;
        model.CustomerCountry = session.CustomerProfile?.Country;
        model.ExternalCustomerId = session.CustomerProfile?.ExternalCustomerId;
    }
}
