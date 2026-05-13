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

        var approved = true;
        if (root.TryGetProperty("approved", out var approvedProp))
        {
            approved = approvedProp.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidOperationException("MockVerification node 'approved' must be a boolean when provided.")
            };
        }

        var resultField = $"{provider}Status";
        if (root.TryGetProperty("resultField", out var resultFieldProp))
        {
            resultField = resultFieldProp.ValueKind switch
            {
                JsonValueKind.Null => resultField,
                JsonValueKind.String when string.IsNullOrWhiteSpace(resultFieldProp.GetString()) => resultField,
                JsonValueKind.String => resultFieldProp.GetString()!,
                _ => throw new InvalidOperationException("MockVerification node 'resultField' must be a string when provided.")
            };
        }

        var timestamp = DateTimeOffset.UtcNow;
        var submission = new Submission
        {
            SessionId = session.Id,
            NodeId = node.Id,
            DataJson = JsonSerializer.Serialize(new
            {
                provider,
                resultField,
                status = approved ? "Approved" : "Rejected",
                checkedAt = timestamp,
                payloadSnapshot = latestPayload
            }, _jsonOptions),
            SubmittedAt = timestamp
        };

        session.Submissions.Add(submission);

        return Task.CompletedTask;
    }
}
