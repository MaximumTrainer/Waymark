using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

public interface IWorkflowService
{
    Task<SessionStepResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> SubmitStepAsync(Guid sessionId, Guid nodeId, SubmitStepRequest request, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> GetNextStepAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionStepResponse> AbandonSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileInfo>> UploadDocumentsAsync(Guid sessionId, Guid nodeId, IReadOnlyList<DocumentUploadItem> files, long maxFileSizeBytes, CancellationToken cancellationToken = default);
    /// <summary>
    /// Reads a document stored against a specific session and node.
    /// <para>
    /// The session and node are part of the lookup, not decoration: a file id alone used to resolve
    /// straight from storage, so any caller holding any id could read any applicant's document. The
    /// signature takes them so that path cannot be called at all.
    /// </para>
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">
    /// The file is not recorded against this session and node - including when it exists and belongs
    /// to another session. Not found rather than forbidden: a refusal would confirm the id is real.
    /// </exception>
    Task<(Stream Stream, StoredFileInfo Info)> GetDocumentAsync(Guid sessionId, Guid nodeId, string fileId, CancellationToken cancellationToken = default);
}

public record DocumentUploadItem(Stream Stream, string FileName, string ContentType, long Length);
