using System.Net;

namespace OpenOnboarding.Application.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task GetHealthLive_ReturnsOk()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthLive_DoesNotRequireAuth()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();
        // No X-Api-Key header — should still return 200

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthReady_ReturnsValidStatusCode()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // 200 (Healthy) or 503 (Degraded/Unhealthy) are both valid responses
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Unexpected status code: {response.StatusCode}");
    }

    [Fact]
    public async Task GetHealthLive_ReturnsJsonContentType()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
