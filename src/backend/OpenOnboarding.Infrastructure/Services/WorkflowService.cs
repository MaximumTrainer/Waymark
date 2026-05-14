using MediatR;
using OpenOnboarding.Application.Commands;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Queries;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// CQRS facade: dispatches commands and queries to the appropriate MediatR handlers.
/// Implements IWorkflowService for backward compatibility with controllers and tests.
/// </summary>
public sealed class WorkflowService(IMediator mediator) : IWorkflowService
{
    public Task<SessionStepResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken = default)
        => mediator.Send(new StartSessionCommand(request), cancellationToken);

    public Task<SessionStepResponse> SubmitStepAsync(Guid sessionId, Guid nodeId, SubmitStepRequest request, CancellationToken cancellationToken = default)
        => mediator.Send(new SubmitStepCommand(sessionId, nodeId, request), cancellationToken);

    public Task<SessionStepResponse> GetNextStepAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => mediator.Send(new GetSessionStepQuery(sessionId), cancellationToken);

    public Task<SessionStepResponse> AbandonSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => mediator.Send(new AbandonSessionCommand(sessionId), cancellationToken);

    public Task<IReadOnlyList<StoredFileInfo>> UploadDocumentsAsync(Guid sessionId, Guid nodeId, IReadOnlyList<DocumentUploadItem> files, long maxFileSizeBytes, CancellationToken cancellationToken = default)
        => mediator.Send(new UploadDocumentsCommand(sessionId, nodeId, files, maxFileSizeBytes), cancellationToken);

    public Task<(Stream Stream, StoredFileInfo Info)> GetDocumentAsync(string fileId, CancellationToken cancellationToken = default)
        => mediator.Send(new GetDocumentQuery(fileId), cancellationToken);
}
