using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Contracts.Flows;

namespace OpenOnboarding.Application.Interfaces;

public interface IFlowService
{
    Task<FlowDto> CreateFlowAsync(CreateFlowRequest request, CancellationToken ct = default);
    Task<PaginatedResult<FlowSummaryDto>> GetFlowsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<FlowDto> GetFlowAsync(Guid flowId, CancellationToken ct = default);
    Task<FlowDto> UpdateFlowAsync(Guid flowId, UpdateFlowRequest request, CancellationToken ct = default);
    Task DeleteFlowAsync(Guid flowId, CancellationToken ct = default);
    Task<IReadOnlyList<FlowVersionSummaryDto>> GetVersionsAsync(Guid flowId, CancellationToken ct = default);
    Task<FlowDto> RestoreVersionAsync(Guid flowId, int versionNumber, CancellationToken ct = default);
}
