using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Api.Authentication;

/// <summary>
/// Authenticates requests using the <c>X-Api-Key</c> header.
/// A valid API key is mapped to a virtual Operator principal.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string ApiKeyHeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedKey = apiKeyValues.FirstOrDefault();
        var expectedKey = configuration["Authentication:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedKey) || providedKey != expectedKey)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "api-key-user"),
            new Claim(ClaimTypes.Name, "api-key-user"),
            new Claim(ClaimTypes.Role, AppRoles.Operator),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
