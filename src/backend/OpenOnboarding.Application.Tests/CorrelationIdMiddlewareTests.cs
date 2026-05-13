using System.Net;

namespace OpenOnboarding.Application.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Request_WithoutCorrelationIdHeader_ResponseIncludesGeneratedCorrelationId()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.True(response.Headers.Contains("X-Correlation-Id"),
            "Response should include X-Correlation-Id header when not provided in request.");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.NotNull(correlationId);
        Assert.NotEmpty(correlationId);
    }

    [Fact]
    public async Task Request_WithCorrelationIdHeader_ResponseEchoesCorrelationId()
    {
        await using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();
        var expectedCorrelationId = "my-test-correlation-id-12345";
        client.DefaultRequestHeaders.Add("X-Correlation-Id", expectedCorrelationId);

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returnedCorrelationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.Equal(expectedCorrelationId, returnedCorrelationId);
    }
}
