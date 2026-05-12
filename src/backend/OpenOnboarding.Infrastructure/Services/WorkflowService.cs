using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
    ICustomerService customerService) : IWorkflowService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

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
        ValidateComplianceRules(currentNode, request.Payload);

        dbContext.Submissions.Add(new Submission
        {
            SessionId = session.Id,
            NodeId = nodeId,
            DataJson = JsonSerializer.Serialize(request.Payload, _jsonOptions),
            SubmittedAt = DateTimeOffset.UtcNow
        });

        var nextNode = ResolveNextNode(session, nodeId, request.Payload);
        session.CurrentNodeId = nextNode?.Id;
        session.Status = nextNode is null ? SessionStatus.Completed : SessionStatus.Started;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SessionStepResponse
        {
            SessionId = session.Id,
            IsCompleted = nextNode is null,
            CurrentNode = nextNode is null ? null : NodeDto.FromEntity(nextNode)
        };
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
            CurrentNode = NodeDto.FromEntity(node)
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
            .Include(x => x.Flow)
            .ThenInclude(x => x.Nodes)
            .Include(x => x.Flow)
            .ThenInclude(x => x.Connections)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
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

    private void ValidateComplianceRules(Node node, IReadOnlyDictionary<string, object?> payload)
    {
        if (string.IsNullOrWhiteSpace(node.ComplianceRuleJson))
        {
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(node.ComplianceRuleJson);
        }
        catch (JsonException)
        {
            throw new ValidationException($"Compliance rule configuration is invalid for node '{node.Key}'.");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("requiredFields", out var requiredFields) || requiredFields.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var element in requiredFields.EnumerateArray())
            {
                var fieldName = element.GetString();
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                if (!payload.TryGetValue(fieldName, out var value) || value is null || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    throw new ValidationException($"Compliance rule failed: '{fieldName}' is required.");
                }
            }
        }
    }
}
