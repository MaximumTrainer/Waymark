using System.Text.Json;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class MockVerificationExecutor : ILogicNodeExecutor
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public string ActionName => "MockVerification";

    public Task ExecuteAsync(
        Node node,
        Session session,
        IReadOnlyDictionary<string, object?> latestPayload,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(node.JsonContent);
        var root = doc.RootElement;

        if (!root.TryGetProperty("provider", out var providerProp) || string.IsNullOrWhiteSpace(providerProp.GetString()))
        {
            throw new InvalidOperationException("MockVerification node requires a non-empty 'provider' in JsonContent.");
        }

        var provider = providerProp.GetString()!;
        var approved = root.TryGetProperty("approved", out var approvedProp) && approvedProp.ValueKind == JsonValueKind.False
            ? false
            : true;
        var resultField = root.TryGetProperty("resultField", out var resultFieldProp)
            ? resultFieldProp.GetString()
            : $"{provider}Status";

        var submission = new Submission
        {
            SessionId = session.Id,
            NodeId = node.Id,
            DataJson = JsonSerializer.Serialize(new
            {
                provider,
                resultField,
                status = approved ? "Approved" : "Rejected",
                checkedAt = DateTimeOffset.UtcNow,
                payloadSnapshot = latestPayload
            }, _jsonOptions),
            SubmittedAt = DateTimeOffset.UtcNow
        };

        session.Submissions.Add(submission);

        return Task.CompletedTask;
    }
}
