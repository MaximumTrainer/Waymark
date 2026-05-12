using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts.Flows;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class FlowService(
    OnboardingDbContext dbContext,
    IValidator<CreateFlowRequest> createValidator,
    IValidator<UpdateFlowRequest> updateValidator) : IFlowService
{
    public async Task<FlowDto> CreateFlowAsync(CreateFlowRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Version = 1
        };

        foreach (var n in request.Nodes)
        {
            flow.Nodes.Add(new Node
            {
                Id = n.Id == Guid.Empty ? Guid.NewGuid() : n.Id,
                FlowId = flow.Id,
                Key = n.Key,
                Type = n.Type,
                Title = n.Title,
                JsonContent = n.JsonContent,
                ComplianceRuleJson = n.ComplianceRuleJson,
                IsStartNode = n.IsStartNode
            });
        }

        foreach (var c in request.Connections)
        {
            flow.Connections.Add(new Connection
            {
                Id = Guid.NewGuid(),
                FlowId = flow.Id,
                SourceNodeId = c.SourceNodeId,
                TargetNodeId = c.TargetNodeId,
                ConditionField = c.ConditionField,
                ConditionOperator = c.ConditionOperator,
                ConditionValue = c.ConditionValue,
                Priority = c.Priority
            });
        }

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync(ct);

        return MapToDto(flow);
    }

    public async Task<PaginatedResult<FlowSummaryDto>> GetFlowsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await dbContext.Flows.CountAsync(ct);

        var items = await dbContext.Flows
            .Include(f => f.Nodes)
            .OrderBy(f => f.Name)
            .ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FlowSummaryDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                Version = f.Version,
                NodeCount = f.Nodes.Count
            })
            .ToListAsync(ct);

        return new PaginatedResult<FlowSummaryDto> { Items = items, TotalCount = totalCount };
    }

    public async Task<FlowDto> GetFlowAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await dbContext.Flows
            .Include(f => f.Nodes)
            .Include(f => f.Connections)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct)
            ?? throw new NotFoundException($"Flow '{flowId}' was not found.");

        return MapToDto(flow);
    }

    public async Task<FlowDto> UpdateFlowAsync(Guid flowId, UpdateFlowRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var flow = await dbContext.Flows
            .Include(f => f.Nodes)
            .Include(f => f.Connections)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct)
            ?? throw new NotFoundException($"Flow '{flowId}' was not found.");

        dbContext.Nodes.RemoveRange(flow.Nodes);
        dbContext.Connections.RemoveRange(flow.Connections);

        flow.Name = request.Name;
        flow.Description = request.Description;
        flow.Version++;

        var newNodes = request.Nodes.Select(n => new Node
        {
            Id = n.Id == Guid.Empty ? Guid.NewGuid() : n.Id,
            FlowId = flow.Id,
            Key = n.Key,
            Type = n.Type,
            Title = n.Title,
            JsonContent = n.JsonContent,
            ComplianceRuleJson = n.ComplianceRuleJson,
            IsStartNode = n.IsStartNode
        }).ToList();

        var newConnections = request.Connections.Select(c => new Connection
        {
            Id = Guid.NewGuid(),
            FlowId = flow.Id,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            ConditionField = c.ConditionField,
            ConditionOperator = c.ConditionOperator,
            ConditionValue = c.ConditionValue,
            Priority = c.Priority
        }).ToList();

        await dbContext.Nodes.AddRangeAsync(newNodes, ct);
        await dbContext.Connections.AddRangeAsync(newConnections, ct);

        await dbContext.SaveChangesAsync(ct);

        flow.Nodes = newNodes;
        flow.Connections = newConnections;

        return MapToDto(flow);
    }

    public async Task DeleteFlowAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await dbContext.Flows
            .FirstOrDefaultAsync(f => f.Id == flowId, ct)
            ?? throw new NotFoundException($"Flow '{flowId}' was not found.");

        var hasActiveSessions = await dbContext.Sessions
            .AnyAsync(s => s.FlowId == flowId
                && s.Status != SessionStatus.Completed
                && s.Status != SessionStatus.Abandoned, ct);

        if (hasActiveSessions)
            throw new ConflictException("Cannot delete flow with active sessions.");

        dbContext.Flows.Remove(flow);
        await dbContext.SaveChangesAsync(ct);
    }

    private static FlowDto MapToDto(Flow flow) => new()
    {
        Id = flow.Id,
        Name = flow.Name,
        Description = flow.Description,
        Version = flow.Version,
        Nodes = flow.Nodes.Select(n => new NodeReadDto
        {
            Id = n.Id,
            FlowId = n.FlowId,
            Key = n.Key,
            Type = n.Type,
            Title = n.Title,
            JsonContent = n.JsonContent,
            ComplianceRuleJson = n.ComplianceRuleJson,
            IsStartNode = n.IsStartNode
        }).ToList(),
        Connections = flow.Connections.Select(c => new ConnectionReadDto
        {
            Id = c.Id,
            FlowId = c.FlowId,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            ConditionField = c.ConditionField,
            ConditionOperator = c.ConditionOperator,
            ConditionValue = c.ConditionValue,
            Priority = c.Priority
        }).ToList()
    };
}
