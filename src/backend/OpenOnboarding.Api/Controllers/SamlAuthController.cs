using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
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
    private const string AuthnIdCookie = "__Secure-waymark-saml-authn-id";

    [HttpGet("metadata")]
    [Produces("application/samlmetadata+xml")]
    public IActionResult Metadata()
    {
        var spCert = LoadSpCertificate();
        var issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider";
        var acsUrl = ResolveAcsUrl();
        var certBase64 = Convert.ToBase64String(spCert.GetRawCertData());

        var metadataXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{issuer}">
  <SPSSODescriptor AuthnRequestsSigned="true" WantAssertionsSigned="true" protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
    <KeyDescriptor use="signing">
      <ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
        <ds:X509Data>
          <ds:X509Certificate>{certBase64}</ds:X509Certificate>
        </ds:X509Data>
      </ds:KeyInfo>
    </KeyDescriptor>
    <KeyDescriptor use="encryption">
      <ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
        <ds:X509Data>
          <ds:X509Certificate>{certBase64}</ds:X509Certificate>
        </ds:X509Data>
      </ds:KeyInfo>
    </KeyDescriptor>
    <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{acsUrl}" index="0" isDefault="true" />
  </SPSSODescriptor>
</EntityDescriptor>
""";

        return Content(metadataXml, "application/samlmetadata+xml", Encoding.UTF8);
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var config = BuildSamlConfiguration();
        var relayState = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var relayStateTimeoutMinutes = GetRelayStateTimeoutMinutes();

        Response.Cookies.Append(RelayStateCookie, relayState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(relayStateTimeoutMinutes)
        });

        var authnRequest = new Saml2AuthnRequest(config);
        var redirectBinding = new Saml2RedirectBinding();
        redirectBinding.RelayState = relayState;
        redirectBinding.Bind(authnRequest);

        Response.Cookies.Append(AuthnIdCookie, authnRequest.Id.Value, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(relayStateTimeoutMinutes)
        });

        return Redirect(redirectBinding.RedirectLocation.OriginalString);
    }

    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Callback()
    {
        if (!Request.HasFormContentType)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", null));

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

        var expectedAuthnId = Request.Cookies[AuthnIdCookie];
        Response.Cookies.Delete(AuthnIdCookie, new CookieOptions { Path = "/auth/saml/callback" });

        try
        {
            var config = BuildSamlConfiguration();
            var authnResponse = new Saml2AuthnResponse(config);
            var httpRequest = Request.ToGenericHttpRequest(validate: true);
            httpRequest.Binding.ReadSamlResponse(httpRequest, authnResponse);

            if (!string.IsNullOrWhiteSpace(expectedAuthnId) &&
                !string.Equals(authnResponse.InResponseToAsString, expectedAuthnId, StringComparison.Ordinal))
            {
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));
            }

            if (authnResponse.Status != Saml2StatusCodes.Success)
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

            httpRequest.Binding.Unbind(httpRequest, authnResponse);

            var nameId = authnResponse.NameId?.Value?.Trim();
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

            var claimsFromAssertion = authnResponse.ClaimsIdentity?.Claims.ToList() ?? [];
            var email = claimsFromAssertion
                .FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
            var displayName = claimsFromAssertion
                .FirstOrDefault(c => c.Type == ClaimTypes.GivenName
                                  || c.Type == ClaimTypes.Name
                                  || c.Type == "displayName")?.Value;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, nameId),
                new(ClaimTypes.Name, displayName?.Trim() ?? nameId),
                new(ClaimTypes.Role, AppRoles.Operator),
                new("auth_provider", "saml")
            };

            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Email, email.Trim()));

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminSessionAuthenticationDefaults.SchemeName);
        return NoContent();
    }

    private Saml2Configuration BuildSamlConfiguration()
    {
        var spCert = LoadSpCertificate();
        var idpCert = LoadIdpCertificate();
        var issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider";
        var idpSsoUrl = configuration["Authentication:Saml:IdpSsoUrl"];
        if (string.IsNullOrWhiteSpace(idpSsoUrl))
            throw new InvalidOperationException("Authentication:Saml:IdpSsoUrl must be configured.");

        var config = new Saml2Configuration
        {
            Issuer = issuer,
            SingleSignOnDestination = new Uri(idpSsoUrl),
            SigningCertificate = spCert,
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck
        };

        config.SignatureValidationCertificates.Add(idpCert);
        config.AllowedAudienceUris.Add(issuer);

        return config;
    }

    private X509Certificate2 LoadSpCertificate()
    {
        var certPem = configuration["Authentication:Saml:SpCertificate"];
        var keyPem = configuration["Authentication:Saml:SpPrivateKey"];
        if (string.IsNullOrWhiteSpace(certPem) || string.IsNullOrWhiteSpace(keyPem))
            throw new InvalidOperationException(
                "Authentication:Saml:SpCertificate and Authentication:Saml:SpPrivateKey must be configured.");

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private X509Certificate2 LoadIdpCertificate()
    {
        var certPem = configuration["Authentication:Saml:IdpCertificate"];
        if (string.IsNullOrWhiteSpace(certPem))
            throw new InvalidOperationException("Authentication:Saml:IdpCertificate must be configured.");

        return X509Certificate2.CreateFromPem(certPem);
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

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteReturnUrl))
        {
            var trustedOrigin = ResolveAllowedAbsoluteReturnOrigin(absoluteReturnUrl);
            if (!string.IsNullOrWhiteSpace(trustedOrigin))
            {
                var pathAndQuery = string.IsNullOrWhiteSpace(absoluteReturnUrl.PathAndQuery)
                    ? "/admin/journey-builder"
                    : absoluteReturnUrl.PathAndQuery;
                return $"{trustedOrigin}{pathAndQuery}{absoluteReturnUrl.Fragment}";
            }
        }

        return "/admin/journey-builder";
    }

    private string? ResolveAllowedAbsoluteReturnOrigin(Uri returnUrl)
    {
        if (!string.Equals(returnUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(returnUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentOrigin = $"{Request.Scheme}://{Request.Host}";
        var requestOriginMatches = string.Equals(
            currentOrigin,
            returnUrl.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);

        if (requestOriginMatches)
            return currentOrigin;

        var allowedReturnOrigins = configuration
            .GetSection("Authentication:Saml:AllowedReturnOrigins")
            .Get<string[]>() ?? [];

        return allowedReturnOrigins.FirstOrDefault(origin =>
            string.Equals(
                origin?.TrimEnd('/'),
                returnUrl.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase));
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
}
