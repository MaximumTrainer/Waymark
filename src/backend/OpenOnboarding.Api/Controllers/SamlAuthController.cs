using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Api.Controllers;

[ApiController]
[Route("auth/saml")]
[AllowAnonymous]
public sealed class SamlAuthController(IConfiguration configuration) : ControllerBase
{
    private const string RelayStateCookie = "__Host-waymark-saml-relay-state";

    [HttpGet("metadata")]
    [Produces("application/samlmetadata+xml")]
    public IActionResult Metadata()
    {
        var issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider";
        var acsUrl = ResolveAcsUrl();

        var metadataXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{issuer}">
  <SPSSODescriptor AuthnRequestsSigned="true" WantAssertionsSigned="true" protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
    <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{acsUrl}" index="0" isDefault="true" />
  </SPSSODescriptor>
</EntityDescriptor>
""";

        return Content(metadataXml, "application/samlmetadata+xml", Encoding.UTF8);
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var relayState = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var idpSsoUrl = configuration["Authentication:Saml:IdpSsoUrl"] ?? "/login";

        Response.Cookies.Append(RelayStateCookie, relayState, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(5)
        });

        var samlRequestPayload = new
        {
            issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider",
            assertionConsumerServiceUrl = ResolveAcsUrl()
        };

        var samlRequest = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(samlRequestPayload)));

        var redirectUrl = QueryHelpers.AddQueryString(idpSsoUrl, new Dictionary<string, string?>
        {
            ["SAMLRequest"] = samlRequest,
            ["RelayState"] = relayState,
            ["returnUrl"] = safeReturnUrl
        });

        return Redirect(redirectUrl);
    }

    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Callback()
    {
        if (!Request.HasFormContentType)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion"));

        var form = await Request.ReadFormAsync();
        var relayState = form["RelayState"].ToString();
        var expectedRelayState = Request.Cookies[RelayStateCookie];
        Response.Cookies.Delete(RelayStateCookie, new CookieOptions { Path = "/auth/saml/callback" });

        if (string.IsNullOrWhiteSpace(relayState) ||
            string.IsNullOrWhiteSpace(expectedRelayState) ||
            !string.Equals(relayState, expectedRelayState, StringComparison.Ordinal))
        {
            return Redirect(BuildLoginErrorRedirect("saml_csrf_failed"));
        }

        var samlResponse = form["SAMLResponse"].ToString();
        var assertion = ParseAssertion(samlResponse);
        if (assertion is null)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion"));

        if (assertion.CertificateExpired)
            return Redirect(BuildLoginErrorRedirect("saml_certificate_expired"));

        if (!assertion.SignatureValid)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion"));

        if (!assertion.Encrypted)
            return Redirect(BuildLoginErrorRedirect("saml_assertion_not_encrypted"));

        var nameId = assertion.NameId?.Trim();
        if (string.IsNullOrWhiteSpace(nameId))
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion"));

        var allowedNameIds = configuration
            .GetSection("Authentication:Saml:AllowedNameIds")
            .Get<string[]>() ?? [];

        if (allowedNameIds.Length > 0 &&
            !allowedNameIds.Contains(nameId, StringComparer.OrdinalIgnoreCase))
        {
            return Redirect(BuildLoginErrorRedirect("saml_access_denied"));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, nameId),
            new(ClaimTypes.Name, assertion.DisplayName?.Trim() ?? nameId),
            new(ClaimTypes.Role, AppRoles.Operator),
            new("auth_provider", "saml")
        };

        if (!string.IsNullOrWhiteSpace(assertion.Email))
            claims.Add(new Claim(ClaimTypes.Email, assertion.Email.Trim()));

        var identity = new ClaimsIdentity(claims, AdminSessionAuthenticationDefaults.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            AdminSessionAuthenticationDefaults.SchemeName,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        var safeReturnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
        return LocalRedirect(safeReturnUrl);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminSessionAuthenticationDefaults.SchemeName);
        return NoContent();
    }

    private string ResolveAcsUrl()
    {
        var configured = configuration["Authentication:Saml:AcsUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return $"{Request.Scheme}://{Request.Host}/auth/saml/callback";
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/admin/journey-builder";

        if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ||
            !returnUrl.StartsWith('/', StringComparison.Ordinal) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/admin/journey-builder";
        }

        return returnUrl;
    }

    private static string BuildLoginErrorRedirect(string errorCode)
        => QueryHelpers.AddQueryString("/login", "error", errorCode);

    private static SamlAssertionPayload? ParseAssertion(string encodedResponse)
    {
        if (string.IsNullOrWhiteSpace(encodedResponse))
            return null;

        try
        {
            var jsonBytes = Convert.FromBase64String(encodedResponse);
            return JsonSerializer.Deserialize<SamlAssertionPayload>(jsonBytes);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class SamlAssertionPayload
    {
        public string? NameId { get; init; }
        public string? DisplayName { get; init; }
        public string? Email { get; init; }
        public bool SignatureValid { get; init; }
        public bool Encrypted { get; init; }
        public bool CertificateExpired { get; init; }
    }
}
