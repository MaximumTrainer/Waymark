using MediatR;
using OpenOnboarding.Application.Contracts;
namespace OpenOnboarding.Application.Queries;
public record GetFlowStatsQuery(Guid FlowId) : IRequest<FlowStatsDto>;
