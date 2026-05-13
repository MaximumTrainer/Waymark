using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Application.Tests;

public sealed class ComplianceViolation422Tests
{
    [Fact]
    public async Task SubmitStep_WithComplianceViolation_Returns422WithViolationsJson()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var factory = TestWebAppFactory.Create(dbName);

        // Seed a flow with a required-field compliance rule
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
            await SeedFlowAsync(db);
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        // Start a session
        var startResponse = await client.PostAsJsonAsync("/api/workflow/sessions/start",
            new { flowId = SeededFlowId });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var startBody = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = startBody.GetProperty("sessionId").GetGuid();
        var nodeId = startBody.GetProperty("currentNode").GetProperty("id").GetGuid();

        // Submit without the required field (should get 422)
        var submitResponse = await client.PostAsJsonAsync(
            $"/api/workflow/sessions/{sessionId}/steps/{nodeId}/submit",
            new { payload = new Dictionary<string, object?>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, submitResponse.StatusCode);

        var body = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(422, body.GetProperty("status").GetInt32());
        Assert.Equal("Compliance violations", body.GetProperty("title").GetString());

        var violations = body.GetProperty("violations").EnumerateArray().ToList();
        Assert.NotEmpty(violations);
        Assert.True(violations[0].TryGetProperty("fieldName", out _));
        Assert.True(violations[0].TryGetProperty("message", out _));
    }

    private static readonly Guid SeededFlowId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static async Task SeedFlowAsync(OnboardingDbContext db)
    {
        var startNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = SeededFlowId,
            Key = "compliance-test-node",
            Title = "Compliance Test",
            Type = NodeType.Form,
            IsStartNode = true,
            ComplianceRuleJson = "{\"requiredFields\":[\"RequiredField\"]}"
        };

        db.Flows.Add(new Flow
        {
            Id = SeededFlowId,
            Name = "422 Test Flow",
            Description = "For 422 compliance test",
            Nodes = [startNode],
            Connections = []
        });

        await db.SaveChangesAsync();
    }
}
