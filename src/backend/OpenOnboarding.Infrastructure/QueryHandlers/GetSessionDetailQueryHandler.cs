using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class GetSessionDetailQueryHandler(OnboardingDbContext dbContext) : IRequestHandler<GetSessionDetailQuery, SessionDetailDto>
{
    public async Task<SessionDetailDto> Handle(GetSessionDetailQuery query, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(x => x.Flow)
            .ThenInclude(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == query.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{query.SessionId}' was not found.");

        NodeDto? currentNode = null;
        if (session.CurrentNodeId.HasValue)
        {
            var node = session.Flow.Nodes.FirstOrDefault(n => n.Id == session.CurrentNodeId.Value);
            if (node is not null)
                currentNode = NodeDto.FromEntity(node);
        }

        return new SessionDetailDto
        {
            Id = session.Id,
            FlowId = session.FlowId,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            CustomerProfileId = session.CustomerProfileId,
            CurrentNode = currentNode
        };
    }
}
