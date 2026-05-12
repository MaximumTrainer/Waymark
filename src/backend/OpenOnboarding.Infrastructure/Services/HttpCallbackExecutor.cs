using System.Net.Http.Json;
using System.Text.Json;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class HttpCallbackExecutor(IHttpClientFactory httpClientFactory) : ILogicNodeExecutor
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public string ActionName => "HttpCallback";

    public async Task ExecuteAsync(
        Node node,
        Session session,
        IReadOnlyDictionary<string, object?> latestPayload,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(node.JsonContent);
        var root = doc.RootElement;

        if (!root.TryGetProperty("url", out var urlProp))
        {
            throw new InvalidOperationException("HttpCallback node requires a 'url' in JsonContent.");
        }

        var url = urlProp.GetString()
            ?? throw new InvalidOperationException("'url' property cannot be null.");

        var client = httpClientFactory.CreateClient(nameof(HttpCallbackExecutor));
        var response = await client.PostAsJsonAsync(url, latestPayload, _jsonOptions, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        var submission = new Submission
        {
            SessionId = session.Id,
            NodeId = node.Id,
            DataJson = JsonSerializer.Serialize(new
            {
                responseBody,
                statusCode = (int)response.StatusCode
            }, _jsonOptions),
            SubmittedAt = DateTimeOffset.UtcNow
        };

        session.Submissions.Add(submission);
    }
}
