using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

/// <summary>
/// Resolves a document only when it is recorded against the session and node named in the query.
/// <para>
/// This previously passed the file id straight to storage, so the session and node the caller had
/// been authorised for played no part in the lookup. Any caller holding any id could read any
/// applicant's document. An ownership check on the endpoint alone does not close that: the owner of
/// one session could still name their own session in the route and ask for someone else's file.
/// </para>
/// </summary>
internal sealed class GetDocumentQueryHandler(
    OnboardingDbContext dbContext,
    IDocumentStorageService documentStorageService)
    : IRequestHandler<GetDocumentQuery, (Stream Stream, StoredFileInfo Info)>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(Stream Stream, StoredFileInfo Info)> Handle(
        GetDocumentQuery query,
        CancellationToken cancellationToken)
    {
        if (!await IsRecordedAgainstAsync(query, cancellationToken))
        {
            // Not found rather than forbidden: distinguishing "not yours" from "does not exist"
            // confirms the id names a real document.
            throw new NotFoundException(
                $"Document '{query.FileId}' was not found for session '{query.SessionId}'.");
        }

        return await documentStorageService.GetStreamAsync(query.FileId, cancellationToken);
    }

    /// <summary>
    /// Whether an upload recorded for this session and node carries the file id.
    /// <para>
    /// Document uploads are written as a <c>Submission</c> whose <c>DataJson</c> holds the stored
    /// file descriptors, so the session-to-file link already exists; it was simply never consulted.
    /// </para>
    /// </summary>
    private async Task<bool> IsRecordedAgainstAsync(GetDocumentQuery query, CancellationToken cancellationToken)
    {
        var submissions = await dbContext.Submissions
            .AsNoTracking()
            .Where(submission => submission.SessionId == query.SessionId && submission.NodeId == query.NodeId)
            .Select(submission => submission.DataJson)
            .ToListAsync(cancellationToken);

        return submissions.Any(dataJson => ContainsFileId(dataJson, query.FileId));
    }

    private static bool ContainsFileId(string dataJson, string fileId)
    {
        try
        {
            // A form submission stores an object rather than an array of files; those simply do not
            // match, which is correct - no document was uploaded there.
            var stored = JsonSerializer.Deserialize<List<StoredFileInfo>>(dataJson, JsonOptions);

            return stored is not null
                && stored.Any(file => string.Equals(file.FileId, fileId, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
