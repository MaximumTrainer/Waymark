using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Queries;
public record GetSubmissionsQuery(Guid SessionId) : IRequest<IReadOnlyList<SubmissionDto>>;
