using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Contracts.Flows;

namespace OpenOnboarding.Application.Tests;

public sealed class CrudControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpClient CreateAuthorizedClient()
    {
        var factory = TestWebAppFactory.Create();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        return client;
    }

    private static async Task<FlowDto> CreateFlowAsync(HttpClient client, string name = "Test Flow")
    {
        var nodeId = Guid.NewGuid();
        var payload = new
        {
            name,
            nodes = new[]
            {
                new
                {
                    id = nodeId,
                    key = "start",
                    type = "Form",
                    title = "Start",
                    isStartNode = true,
                    jsonContent = "{}"
                }
            },
            connections = Array.Empty<object>()
        };
        var response = await client.PostAsJsonAsync("/api/flows", payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FlowDto>(JsonOptions))!;
    }

    private static async Task<CustomerProfileDto> CreateCustomerAsync(HttpClient client, string externalId)
    {
        var payload = new
        {
            externalCustomerId = externalId,
            country = "US",
            email = $"{externalId}@example.com",
            metadataJson = "{}"
        };
        var response = await client.PostAsJsonAsync("/api/customers", payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerProfileDto>())!;
    }

    // ── FlowsController ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFlow_WithValidPayload_Returns200()
    {
        using var client = CreateAuthorizedClient();
        var flow = await CreateFlowAsync(client, "Original Name");

        var updatePayload = new
        {
            name = "Updated Name",
            nodes = new[]
            {
                new { key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{}" }
            },
            connections = Array.Empty<object>()
        };
        var response = await client.PutAsJsonAsync($"/api/flows/{flow.Id}", updatePayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<FlowDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
    }

    [Fact]
    public async Task UpdateFlow_WhenFlowNotFound_Returns404()
    {
        using var client = CreateAuthorizedClient();

        var updatePayload = new
        {
            name = "Updated Name",
            nodes = new[]
            {
                new { key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{}" }
            },
            connections = Array.Empty<object>()
        };
        var response = await client.PutAsJsonAsync($"/api/flows/{Guid.NewGuid()}", updatePayload);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFlow_WithActiveSession_Returns409Conflict()
    {
        using var client = CreateAuthorizedClient();
        var flow = await CreateFlowAsync(client);

        // Start a session to make the flow "active"
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/workflow/sessions/start",
            new { flowId = flow.Id });
        sessionResponse.EnsureSuccessStatusCode();

        // Now try to delete the flow — should be blocked
        var deleteResponse = await client.DeleteAsync($"/api/flows/{flow.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteFlow_WhenFlowHasNoSessions_Returns204()
    {
        using var client = CreateAuthorizedClient();
        var flow = await CreateFlowAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/flows/{flow.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ListFlows_WithPagination_ReturnsCursorAndItems()
    {
        using var client = CreateAuthorizedClient();

        for (var i = 1; i <= 5; i++)
            await CreateFlowAsync(client, $"Pagination Flow {i:D2}");

        var response = await client.GetAsync("/api/flows?page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<FlowSummaryDto>>();
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 5, $"Expected at least 5 total, got {result.TotalCount}");
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    // ── CustomersController ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCustomer_ChangesFieldsAndReturns200()
    {
        using var client = CreateAuthorizedClient();
        var customer = await CreateCustomerAsync(client, "ext-update-test");

        var updatePayload = new { country = "GB", email = "updated@example.com", metadataJson = "{}" };
        var response = await client.PutAsJsonAsync($"/api/customers/{customer.Id}", updatePayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CustomerProfileDto>();
        Assert.NotNull(updated);
        Assert.Equal("GB", updated.Country);
        Assert.Equal("updated@example.com", updated.Email);
    }

    [Fact]
    public async Task DeleteCustomer_WithNoSessions_Returns204()
    {
        using var client = CreateAuthorizedClient();
        var customer = await CreateCustomerAsync(client, "ext-delete-test");

        var response = await client.DeleteAsync($"/api/customers/{customer.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerByExternalId_WhenNotFound_Returns404()
    {
        using var client = CreateAuthorizedClient();

        var response = await client.GetAsync("/api/customers?externalId=nonexistent-customer-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── WebhooksController ────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterWebhook_WithDuplicateUrl_Returns409Conflict()
    {
        using var client = CreateAuthorizedClient();
        var flow = await CreateFlowAsync(client);

        var webhookPayload = new { url = "https://example.com/webhook-dup", secret = "my-secret" };

        // First registration should succeed
        var first = await client.PostAsJsonAsync($"/api/flows/{flow.Id}/webhooks", webhookPayload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second registration with the same URL should conflict
        var second = await client.PostAsJsonAsync($"/api/flows/{flow.Id}/webhooks", webhookPayload);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_WhenNotFound_Returns404()
    {
        using var client = CreateAuthorizedClient();
        var flow = await CreateFlowAsync(client);

        var response = await client.DeleteAsync($"/api/flows/{flow.Id}/webhooks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
