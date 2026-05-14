using MediatR;
using OpenOnboarding.Application.Interfaces;
namespace OpenOnboarding.Application.Queries;
public record GetDocumentQuery(string FileId) : IRequest<(Stream Stream, StoredFileInfo Info)>;
