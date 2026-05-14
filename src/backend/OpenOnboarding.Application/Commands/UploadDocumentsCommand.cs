using MediatR;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
namespace OpenOnboarding.Application.Commands;
public record UploadDocumentsCommand(Guid SessionId, Guid NodeId, IReadOnlyList<DocumentUploadItem> Files, long MaxFileSizeBytes) : IRequest<IReadOnlyList<StoredFileInfo>>;
