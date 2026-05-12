using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Application.Tests.Authentication;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private static async Task<IAuthenticationHandler> BuildHandlerAsync(
        string? configuredKey,
        Action<IHeaderDictionary>? configureHeaders = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKey"] = configuredKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var optionsMonitor = new TestOptionsMonitor<AuthenticationSchemeOptions>(
            new AuthenticationSchemeOptions());

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            provider.GetRequiredService<ILoggerFactory>(),
            UrlEncoder.Default,
            config);

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationHandler.SchemeName,
            displayName: null,
            typeof(ApiKeyAuthenticationHandler));

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        configureHeaders?.Invoke(httpContext.Request.Headers);

        await handler.InitializeAsync(scheme, httpContext);
        return handler;
    }

    [Fact]
    public async Task HandleAuthenticateAsync_WhenCorrectApiKey_ReturnsSuccessWithOperatorRole()
    {
        var handler = await BuildHandlerAsync(
            "correct-key",
            headers => headers["X-Api-Key"] = "correct-key");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.IsInRole(AppRoles.Operator));
        Assert.Equal("api-key-user", result.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task HandleAuthenticateAsync_WhenMissingApiKey_ReturnsNoResult()
    {
        var handler = await BuildHandlerAsync("some-key");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_WhenWrongApiKey_ReturnsFail()
    {
        var handler = await BuildHandlerAsync(
            "correct-key",
            headers => headers["X-Api-Key"] = "wrong-key");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value)
        : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;
        public TOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
