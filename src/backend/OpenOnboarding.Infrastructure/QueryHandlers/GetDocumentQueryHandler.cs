using MediatR;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Queries;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class GetDocumentQueryHandler(IDocumentStorageService documentStorageService) : IRequestHandler<GetDocumentQuery, (Stream Stream, StoredFileInfo Info)>
{
    public Task<(Stream Stream, StoredFileInfo Info)> Handle(GetDocumentQuery query, CancellationToken cancellationToken)
        => documentStorageService.GetStreamAsync(query.FileId, cancellationToken);
}
