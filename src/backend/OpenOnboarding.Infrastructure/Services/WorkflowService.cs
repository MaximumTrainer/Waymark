using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class WorkflowService(
    OnboardingDbContext dbContext,
    IValidator<StartSessionRequest> startSessionValidator,
    IValidator<SubmitStepRequest> submitStepValidator,
    ICustomerService customerService,
    IComplianceRuleEvaluator complianceRuleEvaluator,
    ILogger<WorkflowService> logger,
    IEnumerable<ILogicNodeExecutor> logicNodeExecutors,
    ISessionEventEmitter eventEmitter,
    IWebhookService webhookService,
    IServiceScopeFactory? serviceScopeFactory,
    IDocumentStorageService documentStorageService,
    IMetricsService metricsService,
    ITelemetryService? telemetryService = null) : IWorkflowService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<ILogicNodeExecutor> _logicNodeExecutors = logicNodeExecutors.ToList();

    public async Task<SessionStepResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken = default)
    {
        await startSessionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var customerProfileId = request.CustomerProfileId;

        if (request.CustomerProfile is not null)
        {
            var profile = await customerService.UpsertByExternalIdAsync(request.CustomerProfile, cancellationToken);
            customerProfileId = profile.Id;
        }

        var flow = await dbContext.Flows
            .Include(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == request.FlowId, cancellationToken)
            ?? throw new InvalidOperationException($"Flow '{request.FlowId}' was not found.");

        var startNode = flow.Nodes.FirstOrDefault(x => x.IsStartNode) ?? flow.Nodes.FirstOrDefault()
            ?? throw new InvalidOperationException("Flow does not define any nodes.");

        var session = new Session
        {
            FlowId = flow.Id,
            CustomerProfileId = customerProfileId,
            CurrentNodeId = startNode.Id,
            Status = SessionStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        metricsService.IncrementSessionsStarted(flow.Id.ToString());

        if (telemetryService is not null)
        {
            await telemetryService.TrackAsync(new AnalyticsEvent
            {
                EventType = "session_started",
                JourneyId = flow.Id.ToString(),
                SessionId = session.Id.ToString(),
                StepId = startNode.Id.ToString(),
                StepIndex = 0,
                Payload = new Dictionary<string, object?>
                {
                    ["flowName"] = flow.Name,
                    ["stepKey"] = startNode.Key,
                    ["stepTitle"] = startNode.Title
                }
            }, cancellationToken);
        }

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = false,
            CurrentNode = NodeDto.FromEntity(startNode)
        };
    }

    public async Task<SessionStepResponse> SubmitStepAsync(Guid sessionId, Guid nodeId, SubmitStepRequest request, CancellationToken cancellationToken = default)
    {
        await submitStepValidator.ValidateAndThrowAsync(request, cancellationToken);

        var session = await LoadSession(sessionId, cancellationToken);
        if (session.Status == SessionStatus.Completed)
        {
            return new SessionStepResponse
            {
                SessionId = session.Id,
                IsCompleted = true
            };
        }

        if (session.CurrentNodeId != nodeId)
        {
            throw new InvalidOperationException("Submitted node does not match current session node.");
        }

        var currentNode = session.Flow.Nodes.First(x => x.Id == nodeId);

        var previousSubmissions = session.Submissions.ToList();
        var violations = complianceRuleEvaluator.Evaluate(currentNode, request.Payload, previousSubmissions);
        if (violations.Count > 0)
        {
            throw new ComplianceViolationException(violations);
        }

        var submission = new Submission
        {
            SessionId = session.Id,
            NodeId = nodeId,
            DataJson = JsonSerializer.Serialize(request.Payload, _jsonOptions),
            SubmittedAt = DateTimeOffset.UtcNow
        };
        dbContext.Submissions.Add(submission);
        session.Submissions.Add(submission);

        var nextNode = ResolveNextNode(session, nodeId, request.Payload);

        const int MaxAutoAdvances = 20;
        var autoAdvanceCount = 0;

        while (nextNode?.Type == NodeType.Logic && autoAdvanceCount < MaxAutoAdvances)
        {
            await ExecuteLogicNodeAsync(nextNode, session, request.Payload, cancellationToken);

            if (session.Status == SessionStatus.Error)
            {
                session.CurrentNodeId = nextNode.Id;
                session.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return new SessionStepResponse
                {
                    SessionId = session.Id,
                    IsCompleted = false,
                    CurrentNode = BuildNodeDto(nextNode, session)
                };
            }

            nextNode = ResolveNextNode(session, nextNode.Id, request.Payload);
            autoAdvanceCount++;
        }

        if (autoAdvanceCount >= MaxAutoAdvances && nextNode?.Type == NodeType.Logic)
        {
            session.Status = SessionStatus.Error;
            session.CurrentNodeId = nextNode.Id;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SessionStepResponse
            {
                SessionId = session.Id,
                IsCompleted = false,
                CurrentNode = BuildNodeDto(nextNode, session)
            };
        }

        session.CurrentNodeId = nextNode?.Id;
        session.Status = nextNode is null ? SessionStatus.Completed : SessionStatus.Started;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (nextNode is null)
            metricsService.IncrementSessionsCompleted(session.FlowId.ToString());

        var response = new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = nextNode is null,
            CurrentNode = nextNode is null ? null : BuildNodeDto(nextNode, session)
        };

        var eventType = nextNode is null ? "session-completed" : "step-advanced";
        var eventPayload = nextNode is null
            ? (object)new { sessionId = session.Id, completedAt = session.UpdatedAt }
            : new { sessionId = session.Id, currentNode = BuildNodeDto(nextNode, session) };
        await eventEmitter.EmitAsync(session.Id, eventType, eventPayload, cancellationToken);

        if (telemetryService is not null)
        {
            var submissionIndex = session.Submissions.Count - 1;
            var analyticsEventType = nextNode is null ? "journey_complete" : "navigation_next";
            await telemetryService.TrackAsync(new AnalyticsEvent
            {
                EventType = analyticsEventType,
                JourneyId = session.FlowId.ToString(),
                SessionId = session.Id.ToString(),
                StepId = nextNode?.Id.ToString() ?? currentNode.Id.ToString(),
                StepIndex = submissionIndex,
                Payload = new Dictionary<string, object?>
                {
                    ["submittedStepId"] = currentNode.Id.ToString(),
                    ["submittedStepKey"] = currentNode.Key,
                    ["nextStepId"] = nextNode?.Id.ToString(),
                    ["nextStepKey"] = nextNode?.Key
                }
            }, cancellationToken);
        }

        if (nextNode is null)
        {
            var dispatchTask = DispatchWebhookDeliveryAsync(session.Id, session.FlowId, eventType, eventPayload);
            _ = dispatchTask.ContinueWith(
                task => logger.LogWarning(task.Exception, "Webhook dispatch task faulted for session {SessionId}.", session.Id),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        return response;
    }

    private async Task DispatchWebhookDeliveryAsync(Guid sessionId, Guid flowId, string eventType, object eventPayload)
    {
        try
        {
            if (serviceScopeFactory is null)
            {
                await webhookService.DeliverAsync(sessionId, flowId, eventType, eventPayload, CancellationToken.None);
                return;
            }

            using var scope = serviceScopeFactory.CreateScope();
            var scopedWebhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
            await scopedWebhookService.DeliverAsync(sessionId, flowId, eventType, eventPayload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook delivery failed for session {SessionId}.", sessionId);
        }
    }

    public async Task<SessionStepResponse> GetNextStepAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await LoadSession(sessionId, cancellationToken);
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

    public async Task<SessionStepResponse> AbandonSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Sessions
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        if (session.Status == SessionStatus.Completed)
        {
            throw new ConflictException("Session is already completed");
        }

        if (session.Status != SessionStatus.Abandoned)
        {
            session.Status = SessionStatus.Abandoned;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventEmitter.EmitAsync(session.Id, "session-abandoned", new { sessionId = session.Id }, cancellationToken);
        }

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = false,
            CurrentNode = null
        };
    }

    private async Task<Session> LoadSession(Guid sessionId, CancellationToken cancellationToken)
    {
        return await dbContext.Sessions
            .Include(x => x.CustomerProfile)
            .Include(x => x.Submissions)
            .Include(x => x.Flow)
            .ThenInclude(x => x.Nodes)
            .Include(x => x.Flow)
            .ThenInclude(x => x.Connections)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
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
            {
                writer.WriteString("url", interpolatedUrl);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task ExecuteLogicNodeAsync(
        Node node,
        Session session,
        IReadOnlyDictionary<string, object?> latestPayload,
        CancellationToken cancellationToken)
    {
        var actionName = ParseActionName(node.JsonContent);
        var executor = _logicNodeExecutors.FirstOrDefault(x => x.ActionName == actionName);
        var failOnError = ParseFailOnError(node.JsonContent);

        try
        {
            if (executor is null)
            {
                throw new InvalidOperationException($"No executor found for action '{actionName}'.");
            }

            await executor.ExecuteAsync(node, session, latestPayload, cancellationToken);
        }
        catch (Exception ex)
        {
            node.ExecutionErrorJson = JsonSerializer.Serialize(new
            {
                message = ex.Message,
                timestamp = DateTimeOffset.UtcNow
            }, _jsonOptions);

            if (failOnError)
            {
                session.Status = SessionStatus.Error;
            }
        }
    }

    private static string? ParseActionName(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.TryGetProperty("action", out var actionProp))
            {
                return actionProp.GetString();
            }
        }
        catch (JsonException) { }

        return null;
    }

    private static bool ParseFailOnError(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.TryGetProperty("failOnError", out var failOnErrorProp) &&
                failOnErrorProp.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        catch (JsonException) { }

        return false;
    }

    private Node? ResolveNextNode(Session session, Guid nodeId, IReadOnlyDictionary<string, object?> payload)
    {
        var candidates = session.Flow.Connections
            .Where(x => x.SourceNodeId == nodeId)
            .OrderBy(x => x.Priority)
            .ThenBy(IsFallbackConnection)
            .ThenBy(x => x.Id)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (EvaluateCondition(candidate, payload, session.CustomerProfile))
            {
                return session.Flow.Nodes.FirstOrDefault(x => x.Id == candidate.TargetNodeId);
            }
        }

        return null;
    }

    private static bool EvaluateCondition(Connection connection, IReadOnlyDictionary<string, object?> payload, CustomerProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(connection.ConditionField))
        {
            return true;
        }

        payload.TryGetValue(connection.ConditionField, out var payloadValue);
        var comparableValue = payloadValue?.ToString();

        if (comparableValue is null && profile is not null)
        {
            comparableValue = connection.ConditionField switch
            {
                nameof(CustomerProfile.Country) => profile.Country,
                nameof(CustomerProfile.Email) => profile.Email,
                _ => null
            };
        }

        return connection.ConditionOperator switch
        {
            ConditionOperator.Equals => string.Equals(comparableValue, connection.ConditionValue, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEquals => !string.Equals(comparableValue, connection.ConditionValue, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Exists => !string.IsNullOrWhiteSpace(comparableValue),
            ConditionOperator.Contains => comparableValue?.Contains(connection.ConditionValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            ConditionOperator.NotContains => !(comparableValue?.Contains(connection.ConditionValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false),
            ConditionOperator.StartsWith => comparableValue?.StartsWith(connection.ConditionValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            ConditionOperator.EndsWith => comparableValue?.EndsWith(connection.ConditionValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            ConditionOperator.GreaterThan => TryParseNumericComparison(comparableValue, connection.ConditionValue, out var gt) && gt > 0,
            ConditionOperator.LessThan => TryParseNumericComparison(comparableValue, connection.ConditionValue, out var lt) && lt < 0,
            ConditionOperator.GreaterThanOrEqual => TryParseNumericComparison(comparableValue, connection.ConditionValue, out var gte) && gte >= 0,
            ConditionOperator.LessThanOrEqual => TryParseNumericComparison(comparableValue, connection.ConditionValue, out var lte) && lte <= 0,
            ConditionOperator.MatchesRegex => EvaluateRegex(comparableValue, connection.ConditionValue),
            _ => false
        };
    }

    private static bool TryParseNumericComparison(string? fieldValue, string? conditionValue, out int comparisonResult)
    {
        comparisonResult = 0;
        if (!decimal.TryParse(fieldValue, out var left) || !decimal.TryParse(conditionValue, out var right))
            return false;

        comparisonResult = left.CompareTo(right);
        return true;
    }

    private static bool EvaluateRegex(string? fieldValue, string? pattern)
    {
        if (fieldValue is null || pattern is null)
            return false;

        try
        {
            return Regex.IsMatch(fieldValue, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsFallbackConnection(Connection connection)
    {
        return string.IsNullOrWhiteSpace(connection.ConditionField);
    }

    public async Task<IReadOnlyList<StoredFileInfo>> UploadDocumentsAsync(
        Guid sessionId,
        Guid nodeId,
        IReadOnlyList<DocumentUploadItem> files,
        long maxFileSizeBytes,
        CancellationToken cancellationToken = default)
    {
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

    public Task<(Stream Stream, StoredFileInfo Info)> GetDocumentAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return documentStorageService.GetStreamAsync(fileId, cancellationToken);
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