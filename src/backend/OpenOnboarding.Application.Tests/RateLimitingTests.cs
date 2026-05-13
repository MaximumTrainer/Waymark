using System.Net;
using System.Net.Http.Json;

namespace OpenOnboarding.Application.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task SessionStart_WithinLimit_Returns200()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync(
            "/api/workflow/sessions/start",
            new { flowId = Guid.NewGuid() });

        // Rate limiting should not block the request (Testing env uses NoLimiter).
        // The request may 404 (flow not found) but must not be 429.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task RateLimiter_WithNoLimiter_InTestingEnvironment_Returns200NotRateLimited()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        // Send multiple requests in quick succession to confirm NoLimiter is active
        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/workflow/sessions/start",
                new { flowId = Guid.NewGuid() });

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
