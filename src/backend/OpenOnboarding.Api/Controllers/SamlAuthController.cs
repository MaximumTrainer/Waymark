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
    private const string RelayStateCookie = "__Secure-waymark-saml-relay-state";
    private static readonly JsonSerializerOptions AssertionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet("metadata")]
    [Produces("application/samlmetadata+xml")]
    public IActionResult Metadata()
    {
        if (!IsPlaceholderProviderEnabled())
            return NotFound();

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
        if (!IsPlaceholderProviderEnabled())
            return NotFound();

        var relayState = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var idpSsoUrl = configuration["Authentication:Saml:IdpSsoUrl"];
        if (string.IsNullOrWhiteSpace(idpSsoUrl))
            throw new InvalidOperationException("Authentication:Saml:IdpSsoUrl must be configured.");
        var relayStateTimeoutMinutes = GetRelayStateTimeoutMinutes();

        Response.Cookies.Append(RelayStateCookie, relayState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(relayStateTimeoutMinutes)
        });

        // Placeholder wire format:
        // Until full SAML XML tooling is integrated, we encode a compact JSON envelope that
        // tests and local mocks can round-trip deterministically.
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
        if (!IsPlaceholderProviderEnabled())
            return NotFound();

        if (!Request.HasFormContentType)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion"));

        var form = await Request.ReadFormAsync();
        var safeReturnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
        var relayState = form["RelayState"].ToString();
        var expectedRelayState = Request.Cookies[RelayStateCookie];
        Response.Cookies.Delete(RelayStateCookie, new CookieOptions { Path = "/auth/saml/callback" });

        if (string.IsNullOrWhiteSpace(relayState) ||
            string.IsNullOrWhiteSpace(expectedRelayState) ||
            !string.Equals(relayState, expectedRelayState, StringComparison.Ordinal))
        {
            return Redirect(BuildLoginErrorRedirect("saml_csrf_failed", safeReturnUrl));
        }

        var samlResponse = form["SAMLResponse"].ToString();
        var assertion = ParseAssertion(samlResponse);
        if (assertion is null)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

        if (assertion.CertificateExpired)
            return Redirect(BuildLoginErrorRedirect("saml_certificate_expired", safeReturnUrl));

        if (!assertion.SignatureValid)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

        if (!assertion.Encrypted)
            return Redirect(BuildLoginErrorRedirect("saml_assertion_not_encrypted", safeReturnUrl));

        var nameId = assertion.NameId?.Trim();
        if (string.IsNullOrWhiteSpace(nameId))
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

        var allowedNameIds = configuration
            .GetSection("Authentication:Saml:AllowedNameIds")
            .Get<string[]>()?
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? [];

        if (allowedNameIds.Length == 0 ||
            !allowedNameIds.Contains(nameId, StringComparer.OrdinalIgnoreCase))
        {
            return Redirect(BuildLoginErrorRedirect("saml_access_denied", safeReturnUrl));
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
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(GetAdminSessionDurationHours())
            });

        return RedirectToValidatedReturnUrl(safeReturnUrl);
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

    private string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/admin/journey-builder";

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out _) &&
            returnUrl.StartsWith("/", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteReturnUrl) &&
            IsAllowedAbsoluteReturnUrl(absoluteReturnUrl))
        {
            return absoluteReturnUrl.ToString();
        }

        return "/admin/journey-builder";
    }

    private bool IsAllowedAbsoluteReturnUrl(Uri returnUrl)
    {
        if (!string.Equals(returnUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(returnUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentOrigin = $"{Request.Scheme}://{Request.Host}";
        var requestOriginMatches = string.Equals(
            currentOrigin,
            returnUrl.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);

        if (requestOriginMatches)
            return true;

        var allowedReturnOrigins = configuration
            .GetSection("Authentication:Saml:AllowedReturnOrigins")
            .Get<string[]>() ?? [];

        return allowedReturnOrigins.Contains(
            returnUrl.GetLeftPart(UriPartial.Authority),
            StringComparer.OrdinalIgnoreCase);
    }

    private IActionResult RedirectToValidatedReturnUrl(string safeReturnUrl)
    {
        if (Uri.TryCreate(safeReturnUrl, UriKind.Absolute, out _))
            return Redirect(safeReturnUrl);

        return LocalRedirect(safeReturnUrl);
    }

    private static string BuildLoginErrorRedirect(string errorCode, string? returnUrl)
    {
        var query = new Dictionary<string, string?> { ["error"] = errorCode };

        if (!string.IsNullOrWhiteSpace(returnUrl))
            query["returnUrl"] = returnUrl;

        return QueryHelpers.AddQueryString("/login", query);
    }

    private bool IsPlaceholderProviderEnabled()
        => configuration.GetValue<bool>("Authentication:Saml:EnablePlaceholderProvider");

    private static string BuildLoginErrorRedirect(string errorCode)
        => QueryHelpers.AddQueryString("/login", "error", errorCode);

    private int GetRelayStateTimeoutMinutes()
    {
        var configured = configuration.GetValue<int?>("Authentication:Saml:RelayStateTimeoutMinutes");
        return configured is > 0 ? configured.Value : 5;
    }

    private int GetAdminSessionDurationHours()
    {
        var configured = configuration.GetValue<int?>("Authentication:Saml:SessionDurationHours");
        return configured is > 0 ? configured.Value : 8;
    }

    private static SamlAssertionPayload? ParseAssertion(string encodedResponse)
    {
        if (string.IsNullOrWhiteSpace(encodedResponse))
            return null;

        // Placeholder wire format:
        // IdP assertions are expected to be base64-encoded JSON in this temporary implementation.
        try
        {
            var jsonBytes = Convert.FromBase64String(encodedResponse);
            return JsonSerializer.Deserialize<SamlAssertionPayload>(jsonBytes, AssertionJsonOptions);
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
