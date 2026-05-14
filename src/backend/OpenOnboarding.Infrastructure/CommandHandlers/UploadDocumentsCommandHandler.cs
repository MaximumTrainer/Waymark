using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Commands;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.CommandHandlers;

internal sealed class UploadDocumentsCommandHandler(
    OnboardingDbContext dbContext,
    IDocumentStorageService documentStorageService) : IRequestHandler<UploadDocumentsCommand, IReadOnlyList<StoredFileInfo>>
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StoredFileInfo>> Handle(UploadDocumentsCommand command, CancellationToken cancellationToken)
    {
        var (sessionId, nodeId, files, maxFileSizeBytes) = command;

        var session = await dbContext.Sessions
            .Include(s => s.Flow)
            .ThenInclude(f => f.Nodes)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        var node = session.Flow.Nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new NotFoundException($"Node '{nodeId}' not found in session flow.");

        var nodeContent = ParseJsonContent(node.JsonContent);
        var acceptedTypes = GetStringArrayFromContent(nodeContent, "acceptedFileTypes");
        var maxFiles = GetIntFromContent(nodeContent, "maxFiles", int.MaxValue);

        if (files.Count == 0)
            throw new ArgumentException("No files provided.");

        if (files.Count > maxFiles)
            throw new ArgumentException($"Too many files. Maximum is {maxFiles}.");

        foreach (var file in files)
        {
            if (file.Length == 0)
                throw new ArgumentException($"File '{file.FileName}' is empty.");

            if (file.Length > maxFileSizeBytes)
                throw new InvalidOperationException($"FILE_TOO_LARGE:{file.FileName}");

            if (acceptedTypes.Count > 0 && !acceptedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"UNSUPPORTED_MEDIA_TYPE:{file.ContentType}");
        }

        var stored = new List<StoredFileInfo>();
        foreach (var file in files)
        {
            var info = await documentStorageService.StoreAsync(file.Stream, file.FileName, file.ContentType, cancellationToken);
            ScanResult scanResult;
            try
            {
                scanResult = await documentStorageService.ScanAsync(info.FileId, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new ScanServiceUnavailableException();
            }
            if (!scanResult.IsSafe)
                throw new ScanFailedException(file.FileName, scanResult.ThreatName ?? "Unknown");
            stored.Add(info);
        }

        dbContext.Submissions.Add(new Submission
        {
            SessionId = sessionId,
            NodeId = nodeId,
            DataJson = JsonSerializer.Serialize(stored, _jsonOptions),
            SubmittedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return stored;
    }

    private static Dictionary<string, JsonElement> ParseJsonContent(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static List<string> GetStringArrayFromContent(Dictionary<string, JsonElement> content, string key)
    {
        if (!content.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return new();

        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }

    private static int GetIntFromContent(Dictionary<string, JsonElement> content, string key, int defaultValue)
    {
        if (content.TryGetValue(key, out var el) && el.TryGetInt32(out var v))
            return v;
        return defaultValue;
    }
}
