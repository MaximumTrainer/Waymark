using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OpenOnboarding.Api.Authentication;

/// <summary>
/// Authenticates the per-session applicant credential issued at session start, presented either as
/// <c>Authorization: Bearer &lt;token&gt;</c> or, for the SSE endpoint that the browser EventSource
/// API cannot add headers to, as an <c>access_token</c> query parameter.
/// </summary>
public sealed class ApplicantSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicantSessionTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = ApplicantSessionAuthenticationDefaults.SchemeName;

    /// <summary>
    /// Extracts the token from the request, or <c>null</c> when none is present.
    /// </summary>
    public static string? ExtractToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }

        var queryToken = request.Query[ApplicantSessionAuthenticationDefaults.QueryParameterName]
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(queryToken) ? null : queryToken;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken(Request);
        if (token is null)
            return AuthenticateResult.NoResult();

        var result = await tokenService.ValidateAsync(token);
        if (!result.IsValid)
            return AuthenticateResult.Fail("Invalid applicant session token.");

        var identity = new ClaimsIdentity(result.ClaimsIdentity.Claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
