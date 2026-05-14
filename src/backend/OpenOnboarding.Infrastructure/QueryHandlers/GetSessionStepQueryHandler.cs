using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Queries;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.QueryHandlers;

internal sealed class GetSessionStepQueryHandler(
    OnboardingDbContext dbContext,
    ILogger<GetSessionStepQueryHandler> logger) : IRequestHandler<GetSessionStepQuery, SessionStepResponse>
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SessionStepResponse> Handle(GetSessionStepQuery query, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(x => x.CustomerProfile)
            .Include(x => x.Submissions)
            .Include(x => x.Flow)
            .ThenInclude(x => x.Nodes)
            .Include(x => x.Flow)
            .ThenInclude(x => x.Connections)
            .FirstOrDefaultAsync(x => x.Id == query.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{query.SessionId}' was not found.");

        if (session.Status == SessionStatus.Completed || session.CurrentNodeId is null)
        {
            return new SessionStepResponse
            {
                SessionId = session.Id,
                IsCompleted = true
            };
        }

        var node = session.Flow.Nodes.First(x => x.Id == session.CurrentNodeId.Value);

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = false,
            CurrentNode = BuildNodeDto(node, session)
        };
    }

    private NodeDto BuildNodeDto(Node node, Session session)
    {
        var dto = NodeDto.FromEntity(node);
        if (node.Type == NodeType.Redirect)
        {
            dto.JsonContent = InterpolateRedirectUrl(node, session);
        }
        return dto;
    }

    private string InterpolateRedirectUrl(Node node, Session session)
    {
        string urlTemplate;
        bool isJson;

        try
        {
            using var doc = JsonDocument.Parse(node.JsonContent);
            if (doc.RootElement.TryGetProperty("url", out var urlProp))
            {
                urlTemplate = urlProp.GetString() ?? string.Empty;
                isJson = true;
            }
            else
            {
                return node.JsonContent;
            }
        }
        catch (JsonException)
        {
            urlTemplate = node.JsonContent;
            isJson = false;
        }

        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sessionId"] = session.Id.ToString(),
            ["flowId"] = session.FlowId.ToString(),
            ["nodeKey"] = node.Key,
            ["customerProfileId"] = session.CustomerProfileId?.ToString(),
            ["externalCustomerId"] = session.CustomerProfile?.ExternalCustomerId
        };

        var recentSubmission = session.Submissions.OrderByDescending(x => x.SubmittedAt).FirstOrDefault();
        if (recentSubmission is not null)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(recentSubmission.DataJson, _jsonOptions);
                if (data is not null)
                {
                    foreach (var (key, value) in data)
                    {
                        variables.TryAdd(key, value.ValueKind == JsonValueKind.Null ? null : value.ToString());
                    }
                }
            }
            catch (JsonException) { }
        }

        var interpolated = Regex.Replace(urlTemplate, @"\{\{(\w+)\}\}", match =>
        {
            var varName = match.Groups[1].Value;
            if (variables.TryGetValue(varName, out var varValue) && varValue is not null)
            {
                return Uri.EscapeDataString(varValue);
            }
            logger.LogWarning(
                "Redirect URL interpolation: unknown placeholder '{{{VarName}}}' in node '{NodeKey}'.",
                varName, node.Key);
            return string.Empty;
        });

        return isJson ? ReplaceUrlInJson(node.JsonContent, interpolated) : interpolated;
    }

    private static string ReplaceUrlInJson(string originalJson, string interpolatedUrl)
    {
        using var origDoc = JsonDocument.Parse(originalJson);
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        foreach (var prop in origDoc.RootElement.EnumerateObject())
        {
            if (prop.Name == "url")
                writer.WriteString("url", interpolatedUrl);
            else
                prop.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
