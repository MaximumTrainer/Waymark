using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class GetSubmissionsQueryHandler(OnboardingDbContext dbContext) : IRequestHandler<GetSubmissionsQuery, IReadOnlyList<SubmissionDto>>
{
    public async Task<IReadOnlyList<SubmissionDto>> Handle(GetSubmissionsQuery query, CancellationToken cancellationToken)
    {
        var sessionExists = await dbContext.Sessions.AnyAsync(x => x.Id == query.SessionId, cancellationToken);
        if (!sessionExists)
            throw new InvalidOperationException($"Session '{query.SessionId}' was not found.");

        var submissions = await dbContext.Submissions
            .Where(s => s.SessionId == query.SessionId)
            .Join(
                dbContext.Nodes,
                s => s.NodeId,
                n => n.Id,
                (s, n) => new SubmissionDto
                {
                    Id = s.Id,
                    SessionId = s.SessionId,
                    NodeId = s.NodeId,
                    NodeKey = n.Key,
                    SubmittedAt = s.SubmittedAt,
                    DataJson = s.DataJson
                })
            .OrderBy(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

        return submissions;
    }
}
