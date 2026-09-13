using MediatR;
using OpenOnboarding.Application.Interfaces;
namespace OpenOnboarding.Application.Queries;

/// <summary>
/// Reads one document. The session and node scope the lookup - a file id on its own is not enough
/// to identify a document a caller is entitled to.
/// </summary>
public record GetDocumentQuery(Guid SessionId, Guid NodeId, string FileId) : IRequest<(Stream Stream, StoredFileInfo Info)>;
