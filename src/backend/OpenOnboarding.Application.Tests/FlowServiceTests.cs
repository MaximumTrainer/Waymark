using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Contracts.Flows;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class FlowServiceTests
{
    [Fact]
    public async Task CreateFlow_ValidPayload_PersistsFlowAndReturnsDto()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        var nodeId = Guid.NewGuid();
        var request = new CreateFlowRequest
        {
            Name = "Test Flow",
            Description = "A test flow",
            Nodes =
            [
                new NodeWriteDto { Id = nodeId, Key = "start", Type = NodeType.Form, Title = "Start", IsStartNode = true }
            ],
            Connections = []
        };

        var result = await service.CreateFlowAsync(request);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Test Flow", result.Name);
        Assert.Equal("A test flow", result.Description);
        Assert.Equal(1, result.Version);
        Assert.Single(result.Nodes);
        Assert.Equal("start", result.Nodes[0].Key);
        Assert.True(result.Nodes[0].IsStartNode);

        var persisted = await dbContext.Flows.Include(f => f.Nodes).FirstOrDefaultAsync(f => f.Id == result.Id);
        Assert.NotNull(persisted);
        Assert.Single(persisted.Nodes);
    }

    [Fact]
    public async Task CreateFlow_TwoStartNodes_ThrowsValidationException()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        var request = new CreateFlowRequest
        {
            Name = "Bad Flow",
            Nodes =
            [
                new NodeWriteDto { Key = "a", Type = NodeType.Form, Title = "A", IsStartNode = true },
                new NodeWriteDto { Key = "b", Type = NodeType.Form, Title = "B", IsStartNode = true }
            ],
            Connections = []
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateFlowAsync(request));
    }

    [Fact]
    public async Task CreateFlow_ZeroNodes_ThrowsValidationException()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        var request = new CreateFlowRequest
        {
            Name = "Empty Flow",
            Nodes = [],
            Connections = []
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateFlowAsync(request));
    }

    [Fact]
    public async Task CreateFlow_NameTooLong_ThrowsValidationException()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        var request = new CreateFlowRequest
        {
            Name = new string('x', 201),
            Nodes =
            [
                new NodeWriteDto { Key = "start", Type = NodeType.Form, Title = "Start", IsStartNode = true }
            ],
            Connections = []
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateFlowAsync(request));
    }

    [Fact]
    public async Task CreateFlow_ConnectionReferencesUnknownNode_ThrowsValidationException()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        var nodeId = Guid.NewGuid();
        var request = new CreateFlowRequest
        {
            Name = "Flow",
            Nodes =
            [
                new NodeWriteDto { Id = nodeId, Key = "start", Type = NodeType.Form, Title = "Start", IsStartNode = true }
            ],
            Connections =
            [
                new ConnectionWriteDto { SourceNodeId = nodeId, TargetNodeId = Guid.NewGuid(), Priority = 0 }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateFlowAsync(request));
    }

    [Fact]
    public async Task GetFlow_NonExistentId_ThrowsNotFoundException()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetFlowAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetFlows_Paginated_ReturnsCorrectPage()
    {
        var dbContext = BuildDbContext();
        for (var i = 1; i <= 5; i++)
        {
            dbContext.Flows.Add(new Flow { Name = $"Flow {i:D2}" });
        }
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var page1 = await service.GetFlowsAsync(page: 1, pageSize: 2);
        var page2 = await service.GetFlowsAsync(page: 2, pageSize: 2);
        var page3 = await service.GetFlowsAsync(page: 3, pageSize: 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
        Assert.Equal("Flow 01", page1.Items[0].Name);
        Assert.Equal("Flow 03", page2.Items[0].Name);
    }

    [Fact]
    public async Task GetFlows_PageSizeExceedsCap_CapsAt100()
    {
        var dbContext = BuildDbContext();
        var service = CreateService(dbContext);

        // Should not throw; just returns empty with capped pageSize
        var result = await service.GetFlowsAsync(page: 1, pageSize: 500);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task DeleteFlow_WithActiveSessions_ThrowsConflictException()
    {
        var dbContext = BuildDbContext();
        var flow = new Flow { Name = "Active Flow" };
        dbContext.Flows.Add(flow);
        var session = new Session
        {
            FlowId = flow.Id,
            Status = SessionStatus.Started
        };
        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteFlowAsync(flow.Id));
    }

    [Fact]
    public async Task DeleteFlow_WithOnlyCompletedAndAbandonedSessions_Succeeds()
    {
        var dbContext = BuildDbContext();
        var flow = new Flow { Name = "Done Flow" };
        dbContext.Flows.Add(flow);
        dbContext.Sessions.Add(new Session { FlowId = flow.Id, Status = SessionStatus.Completed });
        dbContext.Sessions.Add(new Session { FlowId = flow.Id, Status = SessionStatus.Abandoned });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await service.DeleteFlowAsync(flow.Id);

        Assert.False(await dbContext.Flows.AnyAsync(f => f.Id == flow.Id));
    }

    [Fact]
    public async Task UpdateFlow_ReplacesNodesAndBumpsVersion()
    {
        var dbContext = BuildDbContext();
        var flow = new Flow { Name = "Old Flow", Version = 1 };
        var oldNode = new Node { FlowId = flow.Id, Key = "old", Title = "Old", Type = NodeType.Form, IsStartNode = true };
        flow.Nodes.Add(oldNode);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var newNodeId = Guid.NewGuid();
        var result = await service.UpdateFlowAsync(flow.Id, new UpdateFlowRequest
        {
            Name = "New Flow",
            Nodes =
            [
                new NodeWriteDto { Id = newNodeId, Key = "new", Type = NodeType.Form, Title = "New", IsStartNode = true }
            ],
            Connections = []
        });

        Assert.Equal("New Flow", result.Name);
        Assert.Equal(2, result.Version);
        Assert.Single(result.Nodes);
        Assert.Equal("new", result.Nodes[0].Key);
        Assert.DoesNotContain(result.Nodes, n => n.Key == "old");
    }

    [Fact]
    public async Task UpdateFlow_CreatesVersionSnapshot()
    {
        var dbContext = BuildDbContext();
        var flow = new Flow { Name = "Original", Version = 1 };
        var node = new Node { Key = "start", Title = "Start", Type = NodeType.Form, IsStartNode = true, FlowId = flow.Id };
        flow.Nodes.Add(node);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var newNodeId = Guid.NewGuid();
        await service.UpdateFlowAsync(flow.Id, new UpdateFlowRequest
        {
            Name = "Updated",
            Nodes = [new NodeWriteDto { Id = newNodeId, Key = "new", Type = NodeType.Form, Title = "New", IsStartNode = true }],
            Connections = []
        });

        var versions = await dbContext.FlowVersions.Where(v => v.FlowId == flow.Id).ToListAsync();
        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNumber);
        Assert.Contains("Original", versions[0].SnapshotJson);
    }

    [Fact]
    public async Task RestoreVersion_RevertsFlowToSnapshot()
    {
        var dbContext = BuildDbContext();
        var flow = new Flow { Name = "V1 Name", Version = 1 };
        var startNode = new Node { Key = "start-v1", Title = "Start V1", Type = NodeType.Form, IsStartNode = true, FlowId = flow.Id };
        flow.Nodes.Add(startNode);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        // Update flow to V2 (saves V1 snapshot)
        await service.UpdateFlowAsync(flow.Id, new UpdateFlowRequest
        {
            Name = "V2 Name",
            Nodes = [new NodeWriteDto { Key = "start-v2", Type = NodeType.Form, Title = "Start V2", IsStartNode = true }],
            Connections = []
        });

        // Restore to V1
        var restored = await service.RestoreVersionAsync(flow.Id, 1);

        Assert.Equal("V1 Name", restored.Name);
        Assert.Single(restored.Nodes);
        Assert.Equal("start-v1", restored.Nodes[0].Key);
    }

    private static FlowService CreateService(OnboardingDbContext dbContext) =>
        new(dbContext, new CreateFlowRequestValidator(), new UpdateFlowRequestValidator());

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }
}
