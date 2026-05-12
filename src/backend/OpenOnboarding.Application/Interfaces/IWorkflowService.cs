using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

public interface IWorkflowService
{
    Task<SessionStepResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> SubmitStepAsync(Guid sessionId, Guid nodeId, SubmitStepRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> GetNextStepAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
