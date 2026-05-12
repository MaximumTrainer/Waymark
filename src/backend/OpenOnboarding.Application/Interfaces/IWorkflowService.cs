using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

public interface IWorkflowService
{
    Task<SessionStepResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> SubmitStepAsync(Guid sessionId, Guid nodeId, SubmitStepRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> GetNextStepAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> AbandonSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileInfo>> UploadDocumentsAsync(Guid sessionId, Guid nodeId, IReadOnlyList<DocumentUploadItem> files, long maxFileSizeBytes, CancellationToken cancellationToken = default);
    Task<(Stream Stream, StoredFileInfo Info)> GetDocumentAsync(string fileId, CancellationToken cancellationToken = default);
}

public record DocumentUploadItem(Stream Stream, string FileName, string ContentType, long Length);
